using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Server.Push;
using Xunit;

namespace Server.Infrastructure.Tests.Push;

public sealed class FcmClientTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"fcm-key-{Guid.NewGuid():N}.json");
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly StubHandler _handler = new();
    private readonly FcmClient _client;

    private static readonly FcmMessage Message = new("device-token", FcmPlatform.Android, "abcd", "sealed");

    public FcmClientTests()
    {
        WriteKey("project-one", _rsa);
        _client = new FcmClient(new HttpClient(_handler), new ServiceAccountKeyFile(_keyPath), _time);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        File.Delete(_keyPath);
    }

    private void WriteKey(string projectId, RSA rsa)
    {
        File.WriteAllText(_keyPath, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["project_id"] = projectId,
            ["client_email"] = "push@project.iam.gserviceaccount.com",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["token_uri"] = "https://oauth2.example/token"
        }));
        File.SetLastWriteTimeUtc(_keyPath, DateTime.UtcNow.AddSeconds(Random.Shared.Next(1, 100_000)));
    }

    [Fact]
    public async Task Send_SignsAnAssertionAndPostsTheEncryptedDataMessage()
    {
        var result = await _client.SendAsync(Message);

        Assert.Equal(FcmSendStatus.Delivered, result.Status);

        var tokenRequest = _handler.Requests[0];
        Assert.Equal("https://oauth2.example/token", tokenRequest.Uri);
        var assertion = WebUtility.UrlDecode(tokenRequest.Body.Split("assertion=")[1].Split('&')[0]);
        var parts = assertion.Split('.');
        Assert.True(_rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        Assert.Equal("push@project.iam.gserviceaccount.com", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("https://oauth2.example/token", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());

        var send = _handler.Requests[1];
        Assert.Equal("https://fcm.googleapis.com/v1/projects/project-one/messages:send", send.Uri);
        Assert.Equal("Bearer access-1", send.Authorization);

        using var body = JsonDocument.Parse(send.Body);
        var message = body.RootElement.GetProperty("message");
        Assert.Equal("device-token", message.GetProperty("token").GetString());
        Assert.Equal("1", message.GetProperty("data").GetProperty("v").GetString());
        Assert.Equal("abcd", message.GetProperty("data").GetProperty("k").GetString());
        Assert.Equal("sealed", message.GetProperty("data").GetProperty("c").GetString());
        Assert.Equal("HIGH", message.GetProperty("android").GetProperty("priority").GetString());
        Assert.False(message.TryGetProperty("notification", out _));
    }

    [Fact]
    public async Task AccessToken_IsReusedUntilItIsAboutToExpire()
    {
        await _client.SendAsync(Message);
        await _client.SendAsync(Message);
        Assert.Equal(1, _handler.TokenRequests);

        _time.Advance(TimeSpan.FromMinutes(59.5));
        await _client.SendAsync(Message);
        Assert.Equal(2, _handler.TokenRequests);
    }

    [Fact]
    public async Task ReplacedKeyFile_IsPickedUpWithoutARestart()
    {
        await _client.SendAsync(Message);

        using var newKey = RSA.Create(2048);
        WriteKey("project-two", newKey);
        await _client.SendAsync(Message);

        Assert.Equal(2, _handler.TokenRequests);
        Assert.Contains("/projects/project-two/", _handler.Requests[^1].Uri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"status\":\"NOT_FOUND\"}}", FcmSendStatus.TokenInvalid)]
    [InlineData(HttpStatusCode.BadRequest, "The registration token is not a valid FCM registration token", FcmSendStatus.TokenInvalid)]
    [InlineData(HttpStatusCode.BadRequest, "Invalid JSON payload received", FcmSendStatus.Failed)]
    [InlineData(HttpStatusCode.Forbidden, "SENDER_ID_MISMATCH", FcmSendStatus.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", FcmSendStatus.Failed)]
    public async Task ErrorResponses_AreToldApart(HttpStatusCode status, string body, FcmSendStatus expected)
    {
        _handler.SendStatus = status;
        _handler.SendBody = body;

        var result = await _client.SendAsync(Message);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Unauthorized_DropsTheAccessToken()
    {
        _handler.SendStatus = HttpStatusCode.Unauthorized;
        await _client.SendAsync(Message);

        _handler.SendStatus = HttpStatusCode.OK;
        await _client.SendAsync(Message);

        Assert.Equal(2, _handler.TokenRequests);
    }

    [Fact]
    public async Task UnreachableTokenEndpoint_FailsWithoutThrowing()
    {
        _handler.TokenStatus = HttpStatusCode.InternalServerError;

        var result = await _client.SendAsync(Message);

        Assert.Equal(FcmSendStatus.Failed, result.Status);
    }

    private sealed record Recorded(string Uri, string? Authorization, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];
        public int TokenRequests { get; private set; }
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode SendStatus { get; set; } = HttpStatusCode.OK;
        public string SendBody { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));

            if (request.RequestUri.Host == "oauth2.example")
            {
                TokenRequests++;
                return new HttpResponseMessage(TokenStatus)
                {
                    Content = new StringContent($"{{\"access_token\":\"access-{TokenRequests}\",\"expires_in\":3600}}")
                };
            }

            return new HttpResponseMessage(SendStatus) { Content = new StringContent(SendBody) };
        }
    }
}
