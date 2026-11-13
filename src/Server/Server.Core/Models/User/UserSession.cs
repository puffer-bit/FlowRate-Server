namespace Server.Core.Models.User;

/// <summary>
/// A device a user is logged in from. The device logs in again with its token, without the second stage, until the
/// session expires or is ended. Only hashes of the tokens are kept.
/// </summary>
/// <param name="TokenHash">The token the device was given last.</param>
/// <param name="PreviousTokenHash">
/// The token the device logged in with last time. It still works, for a device that lost the answer with the next one.
/// </param>
public sealed record UserSession(
    Guid Id,
    string CoordinatorInstanceId,
    int UserId,
    byte[] TokenHash,
    byte[]? PreviousTokenHash,
    string DeviceName,
    string DevicePlatform,
    string AppVersion,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    string? LastAddress);
