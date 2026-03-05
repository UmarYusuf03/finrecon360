using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class PlanConfiguration : IEntityTypeConfiguration<Plan>
    {
        public void Configure(EntityTypeBuilder<Plan> builder)
        {
            builder.ToTable("Plans");

            builder.HasKey(p => p.PlanId);

            builder.Property(p => p.PlanId)
                .ValueGeneratedNever();

            builder.Property(p => p.Code)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(p => p.Name)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(p => p.Currency)
                .HasMaxLength(8)
                .HasDefaultValue("USD");

            builder.Property(p => p.IsActive)
                .HasDefaultValue(true);

            builder.Property(p => p.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(p => p.Code)
                .IsUnique();
        }
    }
}
