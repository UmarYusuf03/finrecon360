using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class EnforcementActionConfiguration : IEntityTypeConfiguration<EnforcementAction>
    {
        public void Configure(EntityTypeBuilder<EnforcementAction> builder)
        {
            builder.ToTable("EnforcementActions");

            builder.HasKey(e => e.EnforcementActionId);

            builder.Property(e => e.EnforcementActionId)
                .ValueGeneratedNever();

            builder.Property(e => e.TargetType)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            builder.Property(e => e.ActionType)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            builder.Property(e => e.Reason)
                .HasMaxLength(1000)
                .IsRequired();

            builder.Property(e => e.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(e => e.ExpiresAt)
                .HasColumnType("datetime2");

            builder.HasIndex(e => new { e.TargetType, e.TargetId });
        }
    }
}
