using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Infrastructure.Coordinator.Connection.Gateway;
using Server.Infrastructure.Coordinator.Connection.Gateway.Kestrel;
using Server.Infrastructure.Coordinator.Services;
using Server.Infrastructure.Options;
using Server.Infrastructure.Security;
using Shared.Models;
using Shared.Models.Requests.Heartbeat;
using Shared.Models.Responses.Heartbeat;
using Shared.Protocol;
using Shared.Serialization;
using Xunit;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Connections;
using Server.Core.Models.Coordinator.Settings;

namespace Server.Infrastructure.Tests.Coordinator.Connection.Gateway;

public sealed class GatewayServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = ProtocolJsonContext.Default.Options;

    private readonly string _certificatePath = Path.GetTempFileName();
    private readonly string _keyPath = Path.GetTempFileName();
    private readonly int _port;
    private readonly GatewayService _service;
    private readonly FakeRouter _router = new();

    public GatewayServiceTests()
    {
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(_certificatePath, certificate.ExportCertificatePem());
            File.WriteAllText(_keyPath, rsa.ExportPkcs8PrivateKeyPem());
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var serverCertificate = new ServerCertificate(
            Microsoft.Extensions.Options.Options.Create(new CertificateOptions { Path = _certificatePath, KeyPath = _keyPath }),
            Microsoft.Extensions.Options.Options.Create(new StorageOptions()),
            NullLogger<ServerCertificate>.Instance);
        _service = new GatewayService(NullLogger.Instance, Json, new MessageParser(NullLogger.Instance, Json),
            serverCertificate, "test", "127.0.0.1", _port);
        _service.StartAsync(_router).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _service.Dispose();
        File.Delete(_certificatePath);
        File.Delete(_keyPath);
    }

    private async Task<(TestClient Client, ICoordinatorInstanceGateway Gateway)> ConnectAsync()
    {
        var next = _router.NextConnection;
        var client = await TestClient.ConnectAsync(_port);
        return (client, await next.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Request_GetsResponse()
    {
        _router.OnMessage = (message, gateway) =>
            gateway.Send(new HeartbeatPingResponse(((HeartbeatPingRequest)message).RequestId, 42));
        var (client, gateway) = await ConnectAsync();
        await using var _ = client;

        var request = new HeartbeatPingRequest();
        await client.SendTextAsync(JsonSerializer.Serialize<BaseMessage>(request, Json));

        var response = JsonSerializer.Deserialize<BaseMessage>((await client.ReceiveTextAsync(Timeout))!, Json);
        Assert.Equal(request.RequestId, Assert.IsType<HeartbeatPingResponse>(response).RequestId);
        Assert.Equal("127.0.0.1", gateway.IpAddress);
    }

    [Fact]
    public async Task InvalidJson_IsDropped()
    {
        _router.OnMessage = (message, gateway) =>
            gateway.Send(new HeartbeatPingResponse(((HeartbeatPingRequest)message).RequestId, 1));
        var (client, gateway) = await ConnectAsync();
        await using var _ = client;

        await client.SendTextAsync("{not json");
        await client.SendTextAsync(JsonSerializer.Serialize<BaseMessage>(new HeartbeatPingRequest(), Json));

        Assert.NotNull(await client.ReceiveTextAsync(Timeout));
        Assert.True(gateway.IsAvailable);
        Assert.Equal(1, _router.MalformedCount);
    }

    [Fact]
    public async Task OversizedMessage_ClosesWithMessageTooBig()
    {
        var (client, _) = await ConnectAsync();
        await using var __ = client;

        var send = client.SendTextAsync(new string('x', KestrelCoordinatorInstanceGateway.MaxReceiveMessageSize + 1));
        var (status, _) = await client.ReceiveCloseAsync(Timeout);
        await send;

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, status);
        await _router.FirstDisconnect.Task.WaitAsync(Timeout);
        await Task.Delay(200);
        Assert.Equal(1, _router.DisconnectCount);
    }

    [Fact]
    public async Task BinaryMessage_ClosesWithInvalidMessageType()
    {
        var (client, _) = await ConnectAsync();
        await using var __ = client;

        await client.Socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, (await client.ReceiveCloseAsync(Timeout)).Status);
    }

    [Fact]
    public async Task SendThenClose_DeliversTheMessageFirst()
    {
        var (client, gateway) = await ConnectAsync();
        await using var _ = client;

        gateway.Send(new HeartbeatPingResponse(Guid.NewGuid(), 1));
        gateway.Close();

        Assert.NotNull(await client.ReceiveTextAsync(Timeout));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, (await client.ReceiveCloseAsync(Timeout)).Status);
        await _router.FirstDisconnect.Task.WaitAsync(Timeout);
        Assert.False(gateway.IsAvailable);
    }

    [Theory]
    [InlineData(ConnectionCloseReason.AuthenticationTimeout, "Authentication timeout")]
    [InlineData(ConnectionCloseReason.TooManyRejectedRequests, "Too many rejected requests")]
    public async Task CloseReason_IsSentAsPolicyViolation(ConnectionCloseReason reason, string description)
    {
        var (client, gateway) = await ConnectAsync();
        await using var _ = client;

        gateway.Close(reason);

        var close = await client.ReceiveCloseAsync(Timeout);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.Status);
        Assert.Equal(description, close.Description);
    }

    [Fact]
    public async Task ClientClose_RaisesDisconnectedOnce()
    {
        var (client, _) = await ConnectAsync();

        await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await client.DisposeAsync();

        await _router.FirstDisconnect.Task.WaitAsync(Timeout);
        await Task.Delay(200);
        Assert.Equal(1, _router.DisconnectCount);
    }

    [Fact]
    public async Task StopAsync_ClosesClientsWithEndpointUnavailable()
    {
        var (client, _) = await ConnectAsync();
        await using var __ = client;

        var close = client.ReceiveCloseAsync(Timeout);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _service.StopAsync();

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, (await close).Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Stopping took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ConnectionsOverPerIpLimit_AreRejected()
    {
        var clients = new List<TestClient>();
        try
        {
            for (var i = 0; i < CoordinatorRateLimitSettings.Default.MaxConnectionsPerIp; i++)
                clients.Add((await ConnectAsync()).Client);

            await Assert.ThrowsAsync<WebSocketException>(() => TestClient.ConnectAsync(_port));

            await clients[0].DisposeAsync();
            clients.RemoveAt(0);
            await Task.Delay(300);

            clients.Add((await ConnectAsync()).Client);
        }
        finally
        {
            foreach (var client in clients)
                await client.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("garbage")]
    public async Task ClientWithoutAServedProtocolVersion_GetsUpgradeRequiredWithTheServedRange(string? version)
    {
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.CollectHttpResponseDetails = true;
        if (version != null)
            socket.Options.SetRequestHeader(ProtocolVersion.Header, version);

        await Assert.ThrowsAsync<WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"wss://127.0.0.1:{_port}/"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.UpgradeRequired, socket.HttpStatusCode);
        Assert.Equal(ProtocolVersion.Format(ProtocolVersion.Current), socket.HttpResponseHeaders![ProtocolVersion.Header].Single());
        Assert.Equal(ProtocolVersion.Format(ProtocolVersion.MinSupported), socket.HttpResponseHeaders[ProtocolVersion.MinHeader].Single());
    }

    [Fact]
    public async Task ClientWithANewerProtocolVersion_IsRejected()
    {
        await Assert.ThrowsAsync<WebSocketException>(() =>
            TestClient.ConnectAsync(_port, ProtocolVersion.Format(ProtocolVersion.Current + 1)));
    }

    [Fact]
    public async Task PlainHttpsRequest_GetsBadRequest()
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var http = new HttpClient(handler);

        var response = await http.GetAsync($"https://127.0.0.1:{_port}/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
