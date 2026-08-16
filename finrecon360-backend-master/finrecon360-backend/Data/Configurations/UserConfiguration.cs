using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(u => u.UserId);

            builder.Property(u => u.UserId)
                .ValueGeneratedNever();

            builder.Property(u => u.Email)
                .HasMaxLength(256)
                .IsRequired();

            builder.HasIndex(u => u.Email)
                .IsUnique();

            builder.Property(u => u.DisplayName)
                .HasMaxLength(256);

            builder.Property(u => u.PhoneNumber)
                .HasMaxLength(32);

            builder.Property(u => u.EmailConfirmed)
                .HasDefaultValue(false);

            builder.Property(u => u.IsActive)
                .HasDefaultValue(true);

            builder.Property(u => u.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(UserStatus.Active)
                .HasSentinel(UserStatus.Active);

            builder.Property(u => u.UserType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(UserType.GlobalPublic)
                .HasSentinel(UserType.GlobalPublic);

            builder.Property(u => u.IsSystemAdmin)
                .HasDefaultValue(false);

            builder.Property(u => u.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(u => u.UpdatedAt)
                .HasColumnType("datetime2");

            builder.Property(u => u.FirstName)
                .HasMaxLength(256);

            builder.Property(u => u.LastName)
                .HasMaxLength(256);

            builder.Property(u => u.Country)
                .HasMaxLength(256);

            builder.Property(u => u.Gender)
                .HasMaxLength(64);

            // Optional, not required: accounts that sign in only through an external provider
            // have no password at all. Leaving IsRequired() here made EF reject every SSO-created
            // account on save, even though the database column itself already allows null.
            builder.Property(u => u.PasswordHash)
                .HasMaxLength(512);

            builder.Property(u => u.ExternalProvider)
                .HasMaxLength(64);

            builder.Property(u => u.ExternalProviderId)
                .HasMaxLength(256);

            // One account per external identity. Filtered so the many password-only accounts,
            // which leave both columns null, do not collide with one another.
            builder.HasIndex(u => new { u.ExternalProvider, u.ExternalProviderId })
                .IsUnique()
                .HasFilter("[ExternalProvider] IS NOT NULL AND [ExternalProviderId] IS NOT NULL");

            builder.Property(u => u.VerificationCode)
                .HasMaxLength(64);

            builder.Property(u => u.VerificationCodeExpiresAt)
                .HasColumnType("datetime2");

            builder.Property(u => u.ProfileImage)
                .HasColumnType("varbinary(max)");

            builder.Property(u => u.ProfileImageContentType)
                .HasMaxLength(100);
        }
    }
}
