using Shared.Enums;

namespace Server.Core.Models.Push;

public sealed record PushDeviceModel(
    string CoordinatorInstanceId,
    int UserId,
    string Token,
    PushPlatform Platform,
    string KeyId,
    byte[] Key,
    byte[]? ReplyTokenHash = null);
