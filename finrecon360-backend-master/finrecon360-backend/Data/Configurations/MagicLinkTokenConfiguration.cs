using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class MagicLinkTokenConfiguration : IEntityTypeConfiguration<MagicLinkToken>
    {
        public void Configure(EntityTypeBuilder<MagicLinkToken> builder)
        {
            builder.ToTable("MagicLinkTokens");

            builder.HasKey(t => t.MagicLinkTokenId);

            builder.Property(t => t.MagicLinkTokenId)
                .ValueGeneratedNever();

            builder.Property(t => t.Purpose)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(t => t.TokenHash)
                .HasColumnType("varbinary(32)")
                .IsRequired();

            builder.Property(t => t.TokenSalt)
                .HasColumnType("varbinary(16)")
                .IsRequired();

            builder.Property(t => t.ExpiresAt)
                .HasColumnType("datetime2");

            builder.Property(t => t.UsedAt)
                .HasColumnType("datetime2");

            builder.Property(t => t.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(t => t.CreatedIp)
                .HasMaxLength(128);

            builder.Property(t => t.LastAttemptAt)
                .HasColumnType("datetime2");

            builder.Property(t => t.AttemptCount)
                .HasDefaultValue(0);

            builder.HasOne(t => t.GlobalUser)
                .WithMany()
                .HasForeignKey(t => t.GlobalUserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => new { t.GlobalUserId, t.Purpose, t.ExpiresAt });
        }
    }
}
