using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.HasOne(x => x.SecurityData)
            .WithOne()
            .HasForeignKey<UserSecurityDataEntity>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // Logins look the user up by the hash of the identity
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.IdentityHash });
    }
}

public class UserSecurityDataConfiguration : IEntityTypeConfiguration<UserSecurityDataEntity>
{
    public void Configure(EntityTypeBuilder<UserSecurityDataEntity> builder)
    {
        builder.ToTable("UserSecurityData");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).ValueGeneratedNever();
    }
}

public class UserPasskeyConfiguration : IEntityTypeConfiguration<UserPasskeyEntity>
{
    public void Configure(EntityTypeBuilder<UserPasskeyEntity> builder)
    {
        builder.ToTable("UserPasskeys");
        builder.HasKey(x => x.Id);
        // A credential belongs to one user; logins look it up by its id
        builder.HasIndex(x => x.CredentialId).IsUnique();
        builder.Property(x => x.Label).HasMaxLength(64);
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSessionEntity>
{
    public void Configure(EntityTypeBuilder<UserSessionEntity> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(x => x.Id);
        // Logins look a session up by the token, or the one before it
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.PreviousTokenHash);
        builder.HasIndex(x => new { x.CoordinatorInstanceId, x.UserId });
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
