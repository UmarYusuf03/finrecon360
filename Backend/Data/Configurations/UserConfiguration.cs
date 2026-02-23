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

            builder.Property(u => u.PasswordHash)
                .HasMaxLength(512)
                .IsRequired();

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
