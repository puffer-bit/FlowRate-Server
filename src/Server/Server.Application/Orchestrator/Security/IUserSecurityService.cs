using Server.Core.Models.User;

namespace Server.Application.Orchestrator.Security;

/// <summary>
/// Sets up how users prove who they are: their password, one-time codes and passkeys. The control panel uses it;
/// users log in with what is set here (see <c>Coordinator.Authentication.Authenticator</c>).
/// </summary>
public interface IUserSecurityService
{
    /// <summary>Null when the coordinator has no such user.</summary>
    Task<UserSecurityOverview?> GetAsync(string coordinatorId, int userId, CancellationToken ct = default);

    /// <summary>
    /// Whether the user proved to be themselves with their current password and one-time code, each needed when the
    /// user has it. True for a user who has neither. Codes are checked, not used up: the session is logged in.
    /// </summary>
    Task<bool> CheckProofAsync(int userId, string? password, string? twoFactorCode, CancellationToken ct = default);

    /// <summary>Sets the password, or removes it when <paramref name="password"/> is null.</summary>
    Task<UserSecurityStatus> SetPasswordAsync(string coordinatorId, int userId, string? password,
        CancellationToken ct = default);

    /// <summary>
    /// Offers a new TOTP secret, which takes effect once <see cref="EnableTwoFactorAsync"/> gets a code made from
    /// it. Refused while codes are enabled: disable them first.
    /// </summary>
    Task<(UserSecurityStatus Status, TwoFactorOffer? Offer)> OfferTwoFactorAsync(string coordinatorId, int userId,
        CancellationToken ct = default);

    Task<UserSecurityStatus> EnableTwoFactorAsync(string coordinatorId, int userId, string code,
        CancellationToken ct = default);

    Task<UserSecurityStatus> DisableTwoFactorAsync(string coordinatorId, int userId, CancellationToken ct = default);

    /// <summary>Options for navigator.credentials.create; the answer goes to <see cref="RegisterPasskeyAsync"/>.</summary>
    Task<(UserSecurityStatus Status, string? OptionsJson)> BeginPasskeyRegistrationAsync(string coordinatorId,
        int userId, CancellationToken ct = default);

    /// <summary>The credential JSON navigator.credentials.create gave for the last options of this user.</summary>
    Task<UserSecurityStatus> RegisterPasskeyAsync(string coordinatorId, int userId, string? label,
        string credentialJson, CancellationToken ct = default);

    Task<UserSecurityStatus> DeletePasskeyAsync(string coordinatorId, int userId, int passkeyId,
        CancellationToken ct = default);
}

public enum UserSecurityStatus
{
    Done,
    UserNotFound,
    PasskeyNotFound,
    /// <summary>The value cannot be used: a password too long, codes enabled already, nothing offered...</summary>
    Invalid,
    /// <summary>The code or the credential does not hold.</summary>
    Refused,
    /// <summary>Passkeys are not configured on this server.</summary>
    Unavailable
}

public sealed record UserSecurityOverview(UserSecurityData Data, IReadOnlyList<UserPasskey> Passkeys,
    bool PasskeysAvailable);

/// <param name="Secret">Base32, for typing into an authenticator app.</param>
/// <param name="Uri">otpauth:// for a QR code.</param>
public sealed record TwoFactorOffer(string Secret, string Uri);
