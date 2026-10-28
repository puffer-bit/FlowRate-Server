using System.ComponentModel.DataAnnotations;
using Shared.Enums.User;

namespace Server.Infrastructure.Database.Entities
{
    public class UserEntity
    {
        [MaxLength(36)]
        public required int Id { get; set; }
        
        /// <summary>SHA-256 of the identity the user logs in with; the identity itself is not kept.</summary>
        [MaxLength(32)]
        public required byte[] IdentityHash { get; set; }
        
        [MaxLength(32)]
        public required string Username { get; set; }
        
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
        public DateTime FirstConnectionTime { get; set; }
        public DateTime LastConnectionTime { get; set; }
        public TimeSpan ConnectionTime => DateTime.UtcNow - LastConnectionTime;
        
        public UserSecurityDataEntity SecurityData { get; set; } = null!;

        public List<UserRoleEntity> Roles { get; set; } = [];
    }
}
