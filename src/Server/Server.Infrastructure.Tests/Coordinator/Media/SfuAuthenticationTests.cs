using System.Buffers.Binary;
using Xunit;
using System.Net;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.SFUPool.Models;
using Server.Core.Models.Orchestrator;
using Server.Infrastructure.Coordinator.Services.Mediasoup;
using Server.Api.Grpc;

namespace Server.Infrastructure.Tests.Coordinator.Media;

public class SfuAuthenticationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gateway_AuthenticatesPingAndOperations_AndRejectsBadCredentials(bool valid)
    {
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:sëcret:123"));
        var ping = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));
        await using var app = builder.Build();
        app.Run(async context =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            var isPing = context.Request.Path.Value!.EndsWith("/Ping", StringComparison.Ordinal);
            if (isPing) ping.TrySetResult(auth); else operation.TrySetResult(auth);
            context.Response.ContentType = "application/grpc";
            if (auth != expected)
            {
                context.Response.Headers.Append("grpc-status", "16");
                context.Response.Headers.Append("grpc-message", "SFU authentication failed");
                return;
            }
            IMessage message = isPing ? new PingResponse() : new BaseResponse();
            var payload = message.ToByteArray();
            var frame = new byte[5 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
            payload.CopyTo(frame.AsSpan(5));
            await context.Response.Body.WriteAsync(frame);
            context.Response.AppendTrailer("grpc-status", "0");
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var port = (ushort)new Uri(address).Port;
        var model = new SFUNodeModel("node", new SFUEndpoint("127.0.0.1", port),
            new SFUCredentials("user", valid ? "sëcret:123" : "wrong"));
        var context = new Mock<IOrchestratorInstanceContext>();
        context.SetupGet(c => c.SfuConnection).Returns(new SfuConnectionSettings { PingIntervalSeconds = 1 });
        var gateway = new MediasoupGateway(NullLogger.Instance, model, context.Object);
        try
        {
            await gateway.Connect();
            var sent = await ping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (valid)
            {
                Assert.Equal(expected, sent);
                await WaitUntil(() => gateway.IsConnected);
                await gateway.LoadSession(1);
                Assert.Equal(expected, await operation.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.NotEqual(expected, sent);
                await WaitUntil(() => gateway.Status.Error?.Contains("authentication", StringComparison.OrdinalIgnoreCase) == true);
                Assert.False(gateway.IsConnected);
            }
        }
        finally
        {
            await gateway.Disconnect();
            await app.StopAsync();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }
}
