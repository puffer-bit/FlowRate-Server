using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class MediaUploadConfiguration : IEntityTypeConfiguration<MediaUploadEntity>
{
    public void Configure(EntityTypeBuilder<MediaUploadEntity> builder)
    {
        builder.ToTable("MediaUploads");
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.State, x.CreatedAt });
        builder.HasIndex(x => x.MessageId);
        builder.HasIndex(x => x.ChatId);
    }
}
