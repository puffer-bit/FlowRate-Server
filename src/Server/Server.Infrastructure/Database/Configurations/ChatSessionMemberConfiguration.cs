using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Database.Entities.TextChat;

namespace Server.Infrastructure.Database.Configurations;

public class ChatSessionMemberConfiguration : IEntityTypeConfiguration<ChatSessionMemberEntity>
{
    public void Configure(EntityTypeBuilder<ChatSessionMemberEntity> builder)
    {
        builder.ToTable("ChatSessionMembers", table =>
            table.HasCheckConstraint("CK_ChatSessionMembers_AccountId", "\"AccountId\" IS NULL OR \"AccountId\" = \"UserId\""));
        builder.HasKey(member => new { member.ChatSessionId, member.UserId });
        builder.HasIndex(member => new { member.UserId, member.ChatSessionId });
        builder.Property(member => member.JoinedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(member => member.ChatSession)
            .WithMany(chat => chat.Members)
            .HasForeignKey(member => member.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        // Keep historical membership when an account is physically removed, as with message authors.
        builder.HasOne<UserEntity>().WithMany()
            .HasForeignKey(member => member.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
