using Microsoft.EntityFrameworkCore;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database;

public class ServerDbContext : DbContext
{
    public DbSet<OrchestratorInstanceEntity> OrchestratorInstance { get; set; }
    public DbSet<CoordinatorInstanceEntity> CoordinatorInstances { get; set; }
    public DbSet<SfuNodeEntity> SfuNodes { get; set; }
    public DbSet<UserEntity> UserEntities { get; set; }
    public DbSet<SessionEntity> Sessions { get; set; }
    public DbSet<HybridSessionEntity> VideoSessionsEntities { get; set; }
    public DbSet<ChatSessionEntity> ChatSessionEntities { get; set; }
    public DbSet<ChatMessageEntity> ChatMessagesEntities { get; set; }
    public DbSet<ChatImageContentEntity> ChatImageContents { get; set; }
    public DbSet<ChatVideoContentEntity> ChatVideoContents { get; set; }
    public DbSet<ChatFileContentEntity> ChatFileContents { get; set; }
    public DbSet<ChatReadStateEntity> ChatReadStates { get; set; }
    public DbSet<PushDeviceEntity> PushDevices { get; set; }
    public DbSet<MediaUploadEntity> MediaUploads { get; set; }
    public DbSet<SessionCategoryEntity> SessionCategories { get; set; }
    public DbSet<SessionPlacementEntity> SessionPlacements { get; set; }
    public DbSet<RoleEntity> Roles { get; set; }
    public DbSet<UserRoleEntity> UserRoles { get; set; }
    public DbSet<SessionPermissionOverrideEntity> SessionPermissionOverrides { get; set; }

    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServerDbContext).Assembly);
    }
}