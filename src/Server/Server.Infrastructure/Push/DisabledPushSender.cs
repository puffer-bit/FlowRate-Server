using Server.Application.Abstractions.Push;

namespace Server.Infrastructure.Push;

public sealed class DisabledPushSender : IPushSender
{
    public bool IsEnabled => false;

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PushSendResult(PushSendStatus.Failed, "Push is disabled"));
}
