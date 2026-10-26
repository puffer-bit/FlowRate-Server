using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Infrastructure.Database.Entities;

namespace Server.Infrastructure.Database.Configurations;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder) => builder
        .HasOne(x => x.SecurityData)
        .WithOne()
        .HasForeignKey<UserSecurityDataEntity>(x => x.UserId)
        .OnDelete(DeleteBehavior.Cascade);
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
