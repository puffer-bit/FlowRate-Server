using Shared.Enums;

namespace Server.Application.Abstractions.Push;

public sealed record PushMessage(string Token, PushPlatform Platform, string KeyId, string Payload);

public enum PushSendStatus
{
    Delivered,
    TokenInvalid,
    Failed
}

public readonly record struct PushSendResult(PushSendStatus Status, string? Detail = null);

public interface IPushSender
{
    bool IsEnabled { get; }

    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default);
}
