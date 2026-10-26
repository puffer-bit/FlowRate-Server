using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Models.Coordinator.Settings;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class SfuNodeConfiguration : IEntityTypeConfiguration<SfuNodeEntity>
{
    public void Configure(EntityTypeBuilder<SfuNodeEntity> builder)
    {
        builder.ToTable("SfuNodes");
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public class CoordinatorInstanceConfiguration : IEntityTypeConfiguration<CoordinatorInstanceEntity>
{
    public void Configure(EntityTypeBuilder<CoordinatorInstanceEntity> builder)
    {
        builder.HasMany(x => x.SfuNodes)
            .WithMany(x => x.Coordinators)
            .UsingEntity("CoordinatorSfuNodes");

        // Nothing queries single limits, so all of them go into one column
        builder.Property(x => x.RateLimits).HasConversion(
            limits => JsonSerializer.Serialize(limits, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<CoordinatorRateLimitSettings>(json, JsonSerializerOptions.Default)!);
    }
}
