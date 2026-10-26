using System.Net;
using System.Text.Json;
using Server.Application.Abstractions.Push;
using Server.Infrastructure.Options;
using Server.Infrastructure.Push;
using Shared.Enums;
using Xunit;

namespace Server.Infrastructure.Tests.Push;

public class RelayPushSenderTests
{
    private static readonly PushMessage Message = new("device-token", PushPlatform.Android, "abcd", "sealed");

    [Fact]
    public async Task Send_PostsTheMessageToTheRelay()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var sender = new RelayPushSender(new HttpClient(handler), "https://relay.example/");

        var result = await sender.SendAsync(Message);

        Assert.Equal(PushSendStatus.Delivered, result.Status);
        Assert.Equal("https://relay.example/v1/push", handler.Uri);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("device-token", body.RootElement.GetProperty("token").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("platform").GetInt32());
        Assert.Equal("abcd", body.RootElement.GetProperty("keyId").GetString());
        Assert.Equal("sealed", body.RootElement.GetProperty("payload").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PushSendStatus.TokenInvalid)]
    [InlineData(HttpStatusCode.TooManyRequests, PushSendStatus.Failed)]
    [InlineData(HttpStatusCode.BadGateway, PushSendStatus.Failed)]
    public async Task RelayAnswers_AreMapped(HttpStatusCode status, PushSendStatus expected)
    {
        var sender = new RelayPushSender(new HttpClient(new StubHandler(status)), "https://relay.example");

        Assert.Equal(expected, (await sender.SendAsync(Message)).Status);
    }

    [Fact]
    public async Task UnreachableRelay_FailsWithoutThrowing()
    {
        var sender = new RelayPushSender(new HttpClient(new StubHandler(null)), "https://relay.example");

        Assert.Equal(PushSendStatus.Failed, (await sender.SendAsync(Message)).Status);
    }

    [Fact]
    public void Factory_PrefersTheOwnKeyThenTheRelayThenNothing()
    {
        Assert.IsType<DisabledPushSender>(PushSenderFactory.Create(new PushOptions()));
        Assert.IsType<RelayPushSender>(PushSenderFactory.Create(new PushOptions { RelayUrl = "https://relay.example" }));
        Assert.IsType<FcmPushSender>(PushSenderFactory.Create(new PushOptions
        {
            ServiceAccountPath = "key.json",
            RelayUrl = "https://relay.example"
        }));
    }

    private sealed class StubHandler(HttpStatusCode? status) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return status is { } code ? new HttpResponseMessage(code) : throw new HttpRequestException("unreachable");
        }
    }
}
