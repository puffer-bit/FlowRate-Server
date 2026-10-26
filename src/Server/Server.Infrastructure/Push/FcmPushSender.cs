using Server.Application.Abstractions.Push;
using Server.Push;
using Shared.Enums;

namespace Server.Infrastructure.Push;

public sealed class FcmPushSender : IPushSender
{
    private readonly IFcmClient _client;

    public FcmPushSender(IFcmClient client)
    {
        _client = client;
    }

    public bool IsEnabled => true;

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendAsync(
            new FcmMessage(message.Token, message.Platform.ToFcm(), message.KeyId, message.Payload),
            cancellationToken);

        return new PushSendResult(result.Status switch
        {
            FcmSendStatus.Delivered => PushSendStatus.Delivered,
            FcmSendStatus.TokenInvalid => PushSendStatus.TokenInvalid,
            _ => PushSendStatus.Failed
        }, result.Detail);
    }
}

internal static class PushPlatformMapper
{
    public static FcmPlatform ToFcm(this PushPlatform platform) =>
        platform == PushPlatform.Ios ? FcmPlatform.Ios : FcmPlatform.Android;
}
