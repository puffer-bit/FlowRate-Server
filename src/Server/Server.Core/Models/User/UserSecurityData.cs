namespace Server.Core.Models.User;

/// <summary>
/// How a user proves who they are besides their identity: a password, a one-time code (TOTP) and passkeys (FIDO2).
/// A user with none of them logs in with the identity alone.
/// </summary>
public sealed class UserSecurityData
{
    public int UserId { get; }
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// The TOTP secret, Base32. While <see cref="IsTwoFactorEnabled"/> is false it is only offered and waits for a
    /// code that proves the user's authenticator has it.
    /// </summary>
    public string? TwoFactorKey { get; private set; }

    public bool IsTwoFactorEnabled { get; private set; }
    public bool IsPasswordEnabled { get; private set; }

    /// <summary>The user has at least one enabled passkey.</summary>
    public bool IsFIDO2Enabled { get; private set; }

    public UserSecurityData(int userId, string? passwordHash, string? twoFactorKey, bool isTwoFactorEnabled,
        bool isPasswordEnabled, bool isFido2Enabled)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        TwoFactorKey = twoFactorKey;
        // A flag without its secret cannot be checked; such data lets the user in by that factor nowhere
        IsTwoFactorEnabled = isTwoFactorEnabled && !string.IsNullOrEmpty(twoFactorKey);
        IsPasswordEnabled = isPasswordEnabled && !string.IsNullOrEmpty(passwordHash);
        IsFIDO2Enabled = isFido2Enabled;
    }

    public static UserSecurityData CreateEmpty(int userId) => new(userId, null, null, false, false, false);

    /// <summary>A password or a one-time code is asked for.</summary>
    public bool HasKnowledgeFactor => IsPasswordEnabled || IsTwoFactorEnabled;

    /// <summary>Logging in takes more than the identity.</summary>
    public bool RequiresSecondStage => HasKnowledgeFactor || IsFIDO2Enabled;

    /// <summary>Whether a TOTP secret is offered and waits for its first code.</summary>
    public bool IsTwoFactorPending => !IsTwoFactorEnabled && !string.IsNullOrEmpty(TwoFactorKey);

    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);
        PasswordHash = passwordHash;
        IsPasswordEnabled = true;
    }

    public void RemovePassword()
    {
        PasswordHash = null;
        IsPasswordEnabled = false;
    }

    /// <summary>Offers a new TOTP secret. An enabled one stays in use until the new one is confirmed.</summary>
    public void OfferTwoFactorKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (IsTwoFactorEnabled)
            throw new InvalidOperationException("Disable the one-time codes before offering a new secret.");
        TwoFactorKey = key;
    }

    /// <summary>Turns on the offered secret, once a code made from it was checked.</summary>
    public void EnableTwoFactor()
    {
        if (string.IsNullOrEmpty(TwoFactorKey))
            throw new InvalidOperationException("No secret was offered.");
        IsTwoFactorEnabled = true;
    }

    public void DisableTwoFactor()
    {
        TwoFactorKey = null;
        IsTwoFactorEnabled = false;
    }

    public void SetPasskeysEnabled(bool hasEnabledPasskeys) => IsFIDO2Enabled = hasEnabledPasskeys;
}
