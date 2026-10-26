using Server.Push;

namespace Server.PushRelay;

public static class RelayEndpoint
{
    public static async Task<IResult> HandleAsync(RelayPushRequest? request, HttpContext context, IFcmClient client,
        RelayLimiter limiter, ILogger<RelayLimiter> logger)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.TryAcquireAddress(address))
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        if (request == null || !IsValid(request))
            return Results.BadRequest();

        if (!limiter.TryAcquireToken(request.Token))
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        var result = await client.SendAsync(
            new FcmMessage(request.Token, request.Platform, request.KeyId, request.Payload), context.RequestAborted);

        switch (result.Status)
        {
            case FcmSendStatus.Delivered:
                return Results.Ok();
            case FcmSendStatus.TokenInvalid:
                return Results.NotFound();
            default:
                logger.LogWarning("FCM refused a push from {Address}: {Detail}", address, result.Detail);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static bool IsValid(RelayPushRequest request) =>
        request.Token is { Length: > 0 and <= RelayContract.MaxTokenLength }
        && Enum.IsDefined(request.Platform)
        && request.KeyId is { Length: RelayContract.KeyIdLength } && request.KeyId.All(char.IsAsciiHexDigit)
        && request.Payload is { Length: > 0 and <= RelayContract.MaxPayloadLength }
        && IsBase64(request.Payload);

    private static bool IsBase64(string text)
    {
        Span<byte> bytes = stackalloc byte[RelayContract.MaxPayloadLength];
        return Convert.TryFromBase64String(text, bytes, out _);
    }
}
