using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class AuthActionTokenConfiguration : IEntityTypeConfiguration<AuthActionToken>
    {
        public void Configure(EntityTypeBuilder<AuthActionToken> builder)
        {
            builder.ToTable("AuthActionTokens");

            builder.HasKey(a => a.AuthActionTokenId);

            builder.Property(a => a.AuthActionTokenId)
                .ValueGeneratedNever();

            builder.Property(a => a.Email)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(a => a.Purpose)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(a => a.TokenHash)
                .HasColumnType("varbinary(32)")
                .IsRequired();

            builder.Property(a => a.TokenSalt)
                .HasColumnType("varbinary(16)")
                .IsRequired();

            builder.Property(a => a.ExpiresAt)
                .HasColumnType("datetime2")
                .IsRequired();

            builder.Property(a => a.ConsumedAt)
                .HasColumnType("datetime2");

            builder.Property(a => a.AttemptCount)
                .HasDefaultValue(0);

            builder.Property(a => a.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(a => a.CreatedIp)
                .HasMaxLength(64);

            builder.Property(a => a.LastAttemptAt)
                .HasColumnType("datetime2");

            builder.HasIndex(a => new { a.Email, a.Purpose, a.ExpiresAt })
                .HasDatabaseName("IX_AuthActionTokens_Email_Purpose_ExpiresAt");

            builder.HasIndex(a => new { a.UserId, a.Purpose })
                .HasDatabaseName("IX_AuthActionTokens_UserId_Purpose");

            builder.HasOne(a => a.User)
                .WithMany(u => u.AuthActionTokens)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
