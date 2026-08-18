using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class TenantPaymentAlertConfiguration : IEntityTypeConfiguration<TenantPaymentAlert>
    {
        public void Configure(EntityTypeBuilder<TenantPaymentAlert> builder)
        {
            builder.ToTable("TenantPaymentAlerts");

            builder.HasKey(a => a.TenantPaymentAlertId);

            builder.Property(a => a.TenantPaymentAlertId)
                .ValueGeneratedNever();

            builder.Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            builder.Property(a => a.PeriodEndAt)
                .HasColumnType("datetime2");

            builder.Property(a => a.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(a => a.AcknowledgedAt).HasColumnType("datetime2");
            builder.Property(a => a.ResolvedAt).HasColumnType("datetime2");

            builder.HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(a => a.Subscription)
                .WithMany()
                .HasForeignKey(a => a.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            // One open alert per subscription — the monitor updates DaysOverdue on the
            // existing row instead of creating duplicates while an episode is ongoing.
            builder.HasIndex(a => new { a.SubscriptionId, a.Status });
            builder.HasIndex(a => a.TenantId);
        }
    }
}
