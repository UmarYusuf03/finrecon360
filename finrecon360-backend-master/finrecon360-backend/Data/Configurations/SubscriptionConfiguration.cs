using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
    {
        public void Configure(EntityTypeBuilder<Subscription> builder)
        {
            builder.ToTable("Subscriptions");

            builder.HasKey(s => s.SubscriptionId);

            builder.Property(s => s.SubscriptionId)
                .ValueGeneratedNever();

            builder.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(SubscriptionStatus.PendingPayment);

            builder.Property(s => s.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(s => s.CurrentPeriodStart)
                .HasColumnType("datetime2");

            builder.Property(s => s.CurrentPeriodEnd)
                .HasColumnType("datetime2");

            builder.HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(s => s.Plan)
                .WithMany(p => p.Subscriptions)
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
