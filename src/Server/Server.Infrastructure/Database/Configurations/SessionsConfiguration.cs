using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<SessionEntity>
{
    public void Configure(EntityTypeBuilder<SessionEntity> builder) => builder.ToTable("Sessions");
}

public class HybridSessionConfiguration : IEntityTypeConfiguration<HybridSessionEntity>
{
    public void Configure(EntityTypeBuilder<HybridSessionEntity> builder) => builder.ToTable("HybridSessions");
}

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSessionEntity>
{
    public void Configure(EntityTypeBuilder<ChatSessionEntity> builder) => builder.ToTable("ChatSessions");
}
public class SessionPermissionOverrideConfiguration : IEntityTypeConfiguration<SessionPermissionOverrideEntity>
{
    public void Configure(EntityTypeBuilder<SessionPermissionOverrideEntity> builder)
    {
        builder.ToTable("SessionPermissionOverrides");
        builder.HasKey(x => new { x.SessionId, x.RoleId });
        builder.HasIndex(x => x.RoleId);
        // SQLite has no unsigned 64-bit integer; the flags keep their bits as a signed one
        builder.Property(x => x.Allow).HasConversion<long>();
        builder.Property(x => x.Deny).HasConversion<long>();
        // An override goes with its session and with its role
        builder.HasOne<SessionEntity>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
