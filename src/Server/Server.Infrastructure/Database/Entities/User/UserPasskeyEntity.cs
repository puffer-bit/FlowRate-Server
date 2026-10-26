namespace Server.Infrastructure.Database.Entities;

public class UserPasskeyEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public uint SignatureCounter { get; set; }
    public string Label { get; set; } = "Passkey";
    public Guid AaGuid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    
    public bool IsEnabled { get; set; }
}