using Server.Core.Models.User;
using Shared.Auth;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Authentication;

/// <summary>A device being added to an account: its public key, checked to be a P-256 key.</summary>
public sealed record NewDevice(byte[] PublicKey, bool IsTemporary)
{
    public static NewDevice? From(NewDeviceDTO? dto) =>
        LoginSignature.TryDecodePublicKey(dto?.PublicKey) is { } key ? new NewDevice(key, dto!.IsTemporary) : null;
}

public static class RecoveryKeys
{
    /// <summary>What the server keeps of the keys, or null when they are not P-256 keys.</summary>
    public static UserRecoveryKeys? From(RecoveryKeysDTO? dto)
    {
        var recovery = LoginSignature.TryDecodePublicKey(dto?.RecoveryPublicKey);
        var account = LoginSignature.TryDecodePublicKey(dto?.AccountPublicKey);
        return recovery == null || account == null ? null : new UserRecoveryKeys(SecretHash.Of(recovery), account);
    }
}
