using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Database.Entities.TextChat;

namespace Server.Database.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChatMessageEntity> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasQueryFilter(p => !p.IsDeleted);
        builder.HasIndex(x => new { x.ChatId, x.Revision });
        builder.HasIndex(x => new { x.ChatId, x.Id });
        builder.HasIndex(x => new { x.ChatId, x.PinnedAt });
        
        builder.HasOne(x => x.ChatSession)
               .WithMany(x => x.Messages)
               .HasForeignKey(x => x.ChatId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(x => x.TextContent);

        // A preview outlives none of its messages: the cleanup removes only those nothing shows
        builder.HasOne(x => x.LinkPreview)
            .WithMany()
            .HasForeignKey(x => x.LinkPreviewId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.OwnsMany(x => x.Reactions, b =>
        {
            b.ToTable("ChatMessageReactions");
            b.WithOwner().HasForeignKey("MessageId");
            b.Property(x => x.UserId).HasMaxLength(36);
            b.Property(x => x.Emoji).HasMaxLength(50);
            b.HasKey("MessageId", "UserId", "Emoji");
        });

        builder.HasMany(x => x.Images)
            .WithOne()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Files)
            .WithOne()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Videos)
            .WithMany(x => x.Messages)
            .UsingEntity(j => j.ToTable("ChatMessageVideos"));
    }
}
