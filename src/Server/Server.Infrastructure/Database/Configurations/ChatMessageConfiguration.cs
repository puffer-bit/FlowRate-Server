using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChatMessageEntity> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasQueryFilter(p => !p.IsDeleted);
        builder.HasIndex(x => new { x.ChatId, x.Revision });
        
        builder.HasOne(x => x.ChatSession)
               .WithMany(x => x.Messages)
               .HasForeignKey(x => x.ChatId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(x => x.TextContent);

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
