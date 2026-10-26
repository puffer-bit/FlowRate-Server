using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class SessionCategoryConfiguration : IEntityTypeConfiguration<SessionCategoryEntity>
{
    public void Configure(EntityTypeBuilder<SessionCategoryEntity> builder)
    {
        builder.ToTable("SessionCategories");
        builder.HasIndex(x => x.CoordinatorInstanceId);
    }
}

public class SessionPlacementConfiguration : IEntityTypeConfiguration<SessionPlacementEntity>
{
    public void Configure(EntityTypeBuilder<SessionPlacementEntity> builder)
    {
        builder.ToTable("SessionPlacements");
        builder.HasKey(x => x.SessionId);
        builder.Property(x => x.SessionId).ValueGeneratedNever();
        builder.HasIndex(x => x.CoordinatorInstanceId);
    }
}
