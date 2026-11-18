using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Configurations;

public class ChatReadMarkConfiguration : IEntityTypeConfiguration<ChatReadMarkEntity>
{
    public void Configure(EntityTypeBuilder<ChatReadMarkEntity> builder)
    {
        builder.ToTable("ChatReadMarks");
        // A position only moves forward, so a user never has two marks up to the same message
        builder.HasKey(x => new { x.ChatId, x.UserId, x.UpToMessageId });
        builder.HasIndex(x => new { x.ChatId, x.UpToMessageId });

        builder.HasOne(x => x.ChatSession)
               .WithMany()
               .HasForeignKey(x => x.ChatId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
