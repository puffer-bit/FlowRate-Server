using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class BanConfiguration : IEntityTypeConfiguration<BanEntity>
{
    public void Configure(EntityTypeBuilder<BanEntity> builder)
    {
        builder.ToTable("GlobalBans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CoordinatorId).HasMaxLength(64);
        builder.Property(x => x.Address).HasMaxLength(45);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CoordinatorId, x.Kind, x.UserId });
        builder.HasIndex(x => new { x.CoordinatorId, x.Kind, x.Address });
        // Keep the audit trail when a user is deleted; there is intentionally no user foreign key.
    }
}
