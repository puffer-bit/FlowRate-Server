using Shared.Enums.Auth;

namespace Server.Core.Models.User;

/// <summary>
/// A device of a user's account. The device logs in by signing a challenge with its key, until it goes unused for its
/// lifetime or is removed. Sessions of the earlier protocol 3 have a token instead of a key; only its hash is kept,
/// and they are moved over to a key by the next login.
/// </summary>
/// <param name="TokenHash">The session token of the earlier protocol 3; null for devices with a key.</param>
/// <param name="PreviousTokenHash">The token before it, still good for a device that lost the answer.</param>
/// <param name="PublicKey">The P-256 key of the device (SubjectPublicKeyInfo); null for sessions of the earlier protocol.</param>
/// <param name="IsTemporary">Not remembered by the client: it ends after a day without a login.</param>
/// <param name="AddedBy">How the device was added to the account.</param>
public sealed record UserSession(
    Guid Id,
    string CoordinatorInstanceId,
    int UserId,
    byte[]? TokenHash,
    byte[]? PreviousTokenHash,
    string DeviceName,
    string DevicePlatform,
    string AppVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? LastAddress,
    byte[]? PublicKey = null,
    bool IsTemporary = false,
    DeviceLoginMethod AddedBy = DeviceLoginMethod.Legacy);
