using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Server.Push;

public interface IFcmClient
{
    Task<FcmSendResult> SendAsync(FcmMessage message, CancellationToken cancellationToken = default);
}

public sealed class FcmClient : IFcmClient
{
    public const string PayloadVersion = "1";

    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string SendEndpoint = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";
    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan AccessTokenMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MessageLifetime = TimeSpan.FromDays(1);

    private readonly HttpClient _httpClient;
    private readonly ServiceAccountKeyFile _keyFile;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private ServiceAccountKey? _tokenKey;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpires;

    public FcmClient(HttpClient httpClient, ServiceAccountKeyFile keyFile, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _keyFile = keyFile;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FcmSendResult> SendAsync(FcmMessage message, CancellationToken cancellationToken = default)
    {
        ServiceAccountKey key;
        string accessToken;
        try
        {
            key = _keyFile.Get();
            accessToken = await GetAccessTokenAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FcmSendResult(FcmSendStatus.Failed, $"No access token: {exception.Message}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(SendEndpoint, key.ProjectId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new ByteArrayContent(BuildBody(message));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException &&
                                          !cancellationToken.IsCancellationRequested)
        {
            return new FcmSendResult(FcmSendStatus.Failed, exception.Message);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return FcmSendResult.Delivered;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = $"{(int)response.StatusCode}: {Shorten(body)}";

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new FcmSendResult(FcmSendStatus.TokenInvalid, detail);
                case HttpStatusCode.BadRequest when body.Contains("registration token", StringComparison.OrdinalIgnoreCase):
                    return new FcmSendResult(FcmSendStatus.TokenInvalid, detail);
                case HttpStatusCode.Unauthorized:
                    DropAccessToken();
                    return new FcmSendResult(FcmSendStatus.Failed, detail);
                default:
                    return new FcmSendResult(FcmSendStatus.Failed, detail);
            }
        }
    }

    private static byte[] BuildBody(FcmMessage message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("message");
            writer.WriteString("token", message.Token);

            writer.WriteStartObject("data");
            writer.WriteString("v", PayloadVersion);
            writer.WriteString("k", message.KeyId);
            writer.WriteString("c", message.Payload);
            writer.WriteEndObject();

            if (message.Platform == FcmPlatform.Android)
            {
                writer.WriteStartObject("android");
                writer.WriteString("priority", "HIGH");
                writer.WriteString("ttl", $"{(long)MessageLifetime.TotalSeconds}s");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private async Task<string> GetAccessTokenAsync(ServiceAccountKey key, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_accessToken != null && ReferenceEquals(_tokenKey, key) && now < _accessTokenExpires - AccessTokenMargin)
                return _accessToken;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = CreateAssertion(key, now)
            });

            using var response = await _httpClient.PostAsync(key.TokenUri, content, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"The token endpoint answered {(int)response.StatusCode}: {Shorten(Encoding.UTF8.GetString(body))}");

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("access_token").GetString()
                        ?? throw new FormatException("The token endpoint returned no access token.");
            var lifetime = document.RootElement.TryGetProperty("expires_in", out var expiresIn)
                ? TimeSpan.FromSeconds(expiresIn.GetInt32())
                : AssertionLifetime;

            _accessToken = token;
            _tokenKey = key;
            _accessTokenExpires = now + lifetime;
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void DropAccessToken()
    {
        _tokenLock.Wait();
        _accessToken = null;
        _tokenLock.Release();
    }

    private static string CreateAssertion(ServiceAccountKey key, DateTimeOffset now)
    {
        using var claims = new MemoryStream();
        using (var writer = new Utf8JsonWriter(claims))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", key.ClientEmail);
            writer.WriteString("scope", Scope);
            writer.WriteString("aud", key.TokenUri);
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", (now + AssertionLifetime).ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        var signed = Base64Url.EncodeToString("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"u8) + "." +
                     Base64Url.EncodeToString(claims.ToArray());

        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKeyPem);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return signed + "." + Base64Url.EncodeToString(signature);
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300];
}
