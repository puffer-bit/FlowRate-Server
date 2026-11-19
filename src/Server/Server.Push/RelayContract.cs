using System.Text.Json.Serialization;

namespace Server.Push;

public static class RelayContract
{
    public const string PushPath = "/v1/push";
    public const int MaxTokenLength = 512;
    public const int KeyIdLength = 32;
    public const int MaxPayloadLength = 3500;
}

public sealed record RelayPushRequest(string Token, FcmPlatform Platform, string KeyId, string Payload, int? TimeToLiveSeconds = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RelayPushRequest))]
public sealed partial class RelayJsonContext : JsonSerializerContext;
