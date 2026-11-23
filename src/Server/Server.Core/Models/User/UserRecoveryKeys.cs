namespace Server.Core.Models.User;

/// <summary>What the server keeps of the recovery phrase of an account.</summary>
/// <param name="RecoveryKeyHash">SHA-256 of the public key a login with the phrase signs with.</param>
/// <param name="AccountPublicKey">The identity key of the account for end-to-end encryption, kept for later.</param>
public sealed record UserRecoveryKeys(byte[] RecoveryKeyHash, byte[] AccountPublicKey);
