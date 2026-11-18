using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Configurations;

public class LinkPreviewConfiguration : IEntityTypeConfiguration<LinkPreviewEntity>
{
    public void Configure(EntityTypeBuilder<LinkPreviewEntity> builder)
    {
        builder.ToTable("LinkPreviews");
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.Url, x.FetchedAt });
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.FetchedAt });
    }
}
