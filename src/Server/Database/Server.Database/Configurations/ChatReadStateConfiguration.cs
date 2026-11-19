using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Database.Entities.TextChat;

namespace Server.Database.Configurations;

public class ChatReadStateConfiguration : IEntityTypeConfiguration<ChatReadStateEntity>
{
    public void Configure(EntityTypeBuilder<ChatReadStateEntity> builder)
    {
        builder.ToTable("ChatReadStates");
        builder.HasKey(x => new { x.UserId, x.ChatId });

        builder.HasOne(x => x.ChatSession)
               .WithMany()
               .HasForeignKey(x => x.ChatId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
