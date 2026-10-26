namespace Server.Push;

public enum FcmPlatform
{
    Android = 0,
    Ios = 1
}

public sealed record FcmMessage(string Token, FcmPlatform Platform, string KeyId, string Payload);

public enum FcmSendStatus
{
    Delivered,
    TokenInvalid,
    Failed
}

public readonly record struct FcmSendResult(FcmSendStatus Status, string? Detail = null)
{
    public static readonly FcmSendResult Delivered = new(FcmSendStatus.Delivered);
}
