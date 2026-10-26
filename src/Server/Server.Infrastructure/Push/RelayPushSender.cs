using System.Net;
using System.Net.Http.Json;
using Server.Application.Abstractions.Push;
using Server.Push;

namespace Server.Infrastructure.Push;

public sealed class RelayPushSender : IPushSender
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    public RelayPushSender(HttpClient httpClient, string relayUrl)
    {
        _httpClient = httpClient;
        _endpoint = new Uri(relayUrl.TrimEnd('/') + RelayContract.PushPath);
    }

    public bool IsEnabled => true;

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        var request = new RelayPushRequest(message.Token, message.Platform.ToFcm(), message.KeyId, message.Payload);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_endpoint, request,
                RelayJsonContext.Default.RelayPushRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
                return new PushSendResult(PushSendStatus.Delivered);

            return new PushSendResult(
                response.StatusCode == HttpStatusCode.NotFound ? PushSendStatus.TokenInvalid : PushSendStatus.Failed,
                $"The relay answered {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException &&
                                          !cancellationToken.IsCancellationRequested)
        {
            return new PushSendResult(PushSendStatus.Failed, exception.Message);
        }
    }
}
