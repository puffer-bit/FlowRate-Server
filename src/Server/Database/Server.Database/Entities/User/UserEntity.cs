using System.ComponentModel.DataAnnotations;
using Shared.Enums.User;

namespace Server.Database.Entities
{
    public class UserEntity
    {
        [MaxLength(36)]
        public required int Id { get; set; }
        
        /// <summary>
        /// SHA-256 of the identity of the earlier protocol 3; null for accounts made with devices, and once the account
        /// moved over to devices.
        /// </summary>
        [MaxLength(32)]
        public byte[]? IdentityHash { get; set; }

        /// <summary>
        /// SHA-256 of the public key the recovery phrase derives (SubjectPublicKeyInfo); finds the account of a login
        /// with the phrase. The phrase itself never reaches the server.
        /// </summary>
        [MaxLength(32)]
        public byte[]? RecoveryKeyHash { get; set; }

        /// <summary>The identity key of the account for end-to-end encryption (SubjectPublicKeyInfo), kept for later.</summary>
        [MaxLength(128)]
        public byte[]? AccountPublicKey { get; set; }
        
        [MaxLength(32)]
        public required string Username { get; set; }

        /// <summary>A person or a bot; set when the account is made.</summary>
        public UserKind Kind { get; set; }

        /// <summary>The user who made the bot; null for people, and once that user is deleted.</summary>
        public int? OwnerUserId { get; set; }
        
        [MaxLength(45)]
        public string? Ip { get; set; }
        
        [MaxLength(64)]
        public required string CoordinatorInstanceId { get; set; }
        
        [MaxLength(100)]
        public string? RegionName { get; set; }
        
        [MaxLength(10)]
        public string? RegionCode { get; set; }
        
        [MaxLength(100)]
        public string? DisplayName { get; set; }
        
        [MaxLength(10)]
        public string? Prefix { get; set; }
        
        [MaxLength(2048)]
        public string? AvatarUrl { get; set; }
        public UserState State { get; set; }
        public UserStatus LastStatus { get; set; }
        public DateTimeOffset FirstConnectionTime { get; set; }
        public DateTimeOffset LastConnectionTime { get; set; }
        public TimeSpan ConnectionTime => DateTimeOffset.UtcNow - LastConnectionTime;
        
        public UserSecurityDataEntity SecurityData { get; set; } = null!;

        public List<UserRoleEntity> Roles { get; set; } = [];
    }
}
