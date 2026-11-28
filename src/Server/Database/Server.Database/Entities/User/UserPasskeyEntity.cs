namespace Server.Database.Entities;

public class UserPasskeyEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public uint SignatureCounter { get; set; }
    public string Label { get; set; } = "Passkey";
    public Guid AaGuid { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    
    public bool IsEnabled { get; set; }
}