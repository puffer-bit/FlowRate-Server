using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Server.Push;
using Xunit;

namespace Server.PushRelay.Tests;

public sealed class RelayTests : IAsyncLifetime
{
    private static readonly string KeyId = new('a', RelayContract.KeyIdLength);
    private static readonly string Payload = Convert.ToBase64String(new byte[64]);

    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"relay-key-{Guid.NewGuid():N}.json");
    private readonly RecordingFcmClient _fcm = new();
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false });
    private WebApplication? _app;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync(int perAddress = 600, int perToken = 60)
    {
        File.WriteAllText(_keyPath, "{}");

        _app = Program.Build(
        [
            "--urls", "http://127.0.0.1:0",
            $"--Relay:ServiceAccountPath={_keyPath}",
            $"--Relay:RequestsPerMinutePerAddress={perAddress}",
            $"--Relay:RequestsPerMinutePerToken={perToken}"
        ], services => services.AddSingleton<IFcmClient>(_fcm));

        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http.BaseAddress = new Uri(address);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_app != null)
            await _app.DisposeAsync();
        File.Delete(_keyPath);
    }

    private async Task RestartAsync(int perAddress, int perToken)
    {
        await _app!.DisposeAsync();
        _http.BaseAddress = null;
        await StartAsync(perAddress, perToken);
    }

    private Task<HttpResponseMessage> Push(string token = "device-token", string? keyId = null, string? payload = null,
        int platform = 0) =>
        _http.PostAsJsonAsync(RelayContract.PushPath,
            new { token, platform, keyId = keyId ?? KeyId, payload = payload ?? Payload });

    [Fact]
    public async Task ValidPush_IsHandedToFcm()
    {
        var response = await Push();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_fcm.Sent);
        Assert.Equal(new FcmMessage("device-token", FcmPlatform.Android, KeyId, Payload), sent);
    }

    [Theory]
    [InlineData(FcmSendStatus.TokenInvalid, HttpStatusCode.NotFound)]
    [InlineData(FcmSendStatus.Failed, HttpStatusCode.BadGateway)]
    public async Task FcmAnswer_BecomesTheStatus(FcmSendStatus status, HttpStatusCode expected)
    {
        _fcm.Status = status;

        Assert.Equal(expected, (await Push()).StatusCode);
    }

    [Fact]
    public async Task InvalidRequests_AreRefusedBeforeFcm()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(token: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(token: new string('t', RelayContract.MaxTokenLength + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(keyId: "short")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(keyId: new string('z', RelayContract.KeyIdLength))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(payload: "not base64!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(payload: new string('A', RelayContract.MaxPayloadLength + 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Push(platform: 9)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _http.PostAsync(RelayContract.PushPath, new StringContent("{broken", Encoding.UTF8, "application/json"))).StatusCode);

        Assert.Empty(_fcm.Sent);
    }

    [Fact]
    public async Task OversizedBody_IsRefused()
    {
        var response = await Push(payload: new string('A', 16 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_fcm.Sent);
    }

    [Fact]
    public async Task TokenLimit_StopsAFloodToOneDeviceOnly()
    {
        await RestartAsync(perAddress: 600, perToken: 2);

        Assert.Equal(HttpStatusCode.OK, (await Push("one")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Push("one")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Push("one")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Push("two")).StatusCode);
    }

    [Fact]
    public async Task AddressLimit_StopsAFloodFromOneSender()
    {
        await RestartAsync(perAddress: 2, perToken: 60);

        Assert.Equal(HttpStatusCode.OK, (await Push("one")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Push("two")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Push("three")).StatusCode);
    }

    private sealed class RecordingFcmClient : IFcmClient
    {
        public List<FcmMessage> Sent { get; } = [];
        public FcmSendStatus Status { get; set; } = FcmSendStatus.Delivered;

        public Task<FcmSendResult> SendAsync(FcmMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new FcmSendResult(Status));
        }
    }
}
