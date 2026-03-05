using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class PaymentSessionConfiguration : IEntityTypeConfiguration<PaymentSession>
    {
        public void Configure(EntityTypeBuilder<PaymentSession> builder)
        {
            builder.ToTable("PaymentSessions");

            builder.HasKey(p => p.PaymentSessionId);

            builder.Property(p => p.PaymentSessionId)
                .ValueGeneratedNever();

            builder.Property(p => p.StripeSessionId)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(p => p.StripeCustomerId)
                .HasMaxLength(256);

            builder.Property(p => p.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(PaymentSessionStatus.Created);

            builder.Property(p => p.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(p => p.PaidAt)
                .HasColumnType("datetime2");

            builder.HasOne(p => p.Tenant)
                .WithMany()
                .HasForeignKey(p => p.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(p => p.Subscription)
                .WithMany(s => s.PaymentSessions)
                .HasForeignKey(p => p.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(p => p.StripeSessionId)
                .IsUnique();
        }
    }
}
