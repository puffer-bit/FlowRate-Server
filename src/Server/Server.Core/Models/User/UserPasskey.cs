namespace Server.Core.Models.User;

/// <summary>A FIDO2 credential (passkey or security key) a user logs in with.</summary>
public sealed class UserPasskey
{
    public const int LabelMaxLength = 64;
    public const string DefaultLabel = "Passkey";

    // Assigned by the database after the first save
    public int Id { get; set; }
    public int UserId { get; }
    public byte[] CredentialId { get; }
    /// <summary>COSE encoded public key.</summary>
    public byte[] PublicKey { get; }
    /// <summary>Grows with every use on authenticators that count; a value that does not grow means a clone.</summary>
    public uint SignatureCounter { get; private set; }
    public string Label { get; private set; }
    public Guid AaGuid { get; }
    public DateTime CreatedAt { get; }
    public DateTime? LastUsedAt { get; private set; }
    public bool IsEnabled { get; private set; }

    public UserPasskey(int id, int userId, byte[] credentialId, byte[] publicKey, uint signatureCounter, string label,
        Guid aaGuid, DateTime createdAt, DateTime? lastUsedAt, bool isEnabled)
    {
        if (credentialId is not { Length: > 0 })
            throw new ArgumentException("A passkey needs its credential id.", nameof(credentialId));
        if (publicKey is not { Length: > 0 })
            throw new ArgumentException("A passkey needs its public key.", nameof(publicKey));

        Id = id;
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignatureCounter = signatureCounter;
        Label = NormalizeLabel(label);
        AaGuid = aaGuid;
        CreatedAt = createdAt;
        LastUsedAt = lastUsedAt;
        IsEnabled = isEnabled;
    }

    /// <summary>A newly registered passkey; not saved yet.</summary>
    public static UserPasskey Create(int userId, byte[] credentialId, byte[] publicKey, uint signatureCounter,
        string? label, Guid aaGuid, DateTime createdAt) =>
        new(0, userId, credentialId, publicKey, signatureCounter, label ?? DefaultLabel, aaGuid, createdAt, null, true);

    public void RecordUse(uint signatureCounter, DateTime usedAt)
    {
        SignatureCounter = signatureCounter;
        LastUsedAt = usedAt;
    }

    public void Rename(string label) => Label = NormalizeLabel(label);

    public void Disable() => IsEnabled = false;

    private static string NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsControl))
            return DefaultLabel;
        return trimmed.Length > LabelMaxLength ? trimmed[..LabelMaxLength] : trimmed;
    }
}
