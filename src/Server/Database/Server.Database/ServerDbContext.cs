using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Server.Database.Entities;
using Server.Database.Entities.TextChat;

namespace Server.Database;

public class ServerDbContext : DbContext
{
    public DbSet<OrchestratorInstanceEntity> OrchestratorInstance { get; set; }
    public DbSet<CoordinatorInstanceEntity> CoordinatorInstances { get; set; }
    public DbSet<SfuNodeEntity> SfuNodes { get; set; }
    public DbSet<BanEntity> GlobalBans { get; set; }
    public DbSet<UserEntity> UserEntities { get; set; }
    public DbSet<UserSessionEntity> UserSessions { get; set; }
    public DbSet<UserLoginCodeEntity> UserLoginCodes { get; set; }
    public DbSet<SessionEntity> Sessions { get; set; }
    public DbSet<HybridSessionEntity> VideoSessionsEntities { get; set; }
    public DbSet<ChatSessionEntity> ChatSessionEntities { get; set; }
    public DbSet<ChatSessionMemberEntity> ChatSessionMembers { get; set; }
    public DbSet<ChatMessageEntity> ChatMessagesEntities { get; set; }
    public DbSet<ChatImageContentEntity> ChatImageContents { get; set; }
    public DbSet<ChatVideoContentEntity> ChatVideoContents { get; set; }
    public DbSet<ChatFileContentEntity> ChatFileContents { get; set; }
    public DbSet<ChatReadStateEntity> ChatReadStates { get; set; }
    public DbSet<ChatReadMarkEntity> ChatReadMarks { get; set; }
    public DbSet<LinkPreviewEntity> LinkPreviews { get; set; }
    public DbSet<PushDeviceEntity> PushDevices { get; set; }
    public DbSet<MediaUploadEntity> MediaUploads { get; set; }
    public DbSet<SessionCategoryEntity> SessionCategories { get; set; }
    public DbSet<SessionPlacementEntity> SessionPlacements { get; set; }
    public DbSet<RoleEntity> Roles { get; set; }
    public DbSet<UserRoleEntity> UserRoles { get; set; }
    public DbSet<SessionPermissionOverrideEntity> SessionPermissionOverrides { get; set; }

    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Times are DateTimeOffset in code and stored as UTC timestamps, the column type every migration created.
    /// SQLite returns them without a kind, so a read marks them UTC.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTimestampConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcTimestampConverter>();
    }

    private sealed class UtcTimestampConverter() : ValueConverter<DateTimeOffset, DateTime>(
        value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServerDbContext).Assembly);
    }
}