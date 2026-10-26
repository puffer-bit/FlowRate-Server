namespace Server.Infrastructure.Database.Entities;

public class UserSecurityDataEntity
{
    public int UserId { get; set; }
    public string? PasswordHash { get; set; }
    public string? TwoFactorKey { get; set; }
    public List<string> UserPasskeyIds { get; set; } = new();
    
    public bool IsTwoFactorEnabled { get; set; }
    public bool IsPasswordEnabled { get; set; }
    public bool IsFIDO2Enabled { get; set; }
}