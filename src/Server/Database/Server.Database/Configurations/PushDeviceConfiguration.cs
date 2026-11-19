using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Database.Entities;

namespace Server.Database.Configurations;

public class PushDeviceConfiguration : IEntityTypeConfiguration<PushDeviceEntity>
{
    public void Configure(EntityTypeBuilder<PushDeviceEntity> builder)
    {
        builder.ToTable("PushDevices");
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.Token }).IsUnique();
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.ReplyTokenHash }).IsUnique();
    }
}
