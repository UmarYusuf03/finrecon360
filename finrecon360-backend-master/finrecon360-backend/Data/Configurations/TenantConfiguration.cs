using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
    {
        public void Configure(EntityTypeBuilder<Tenant> builder)
        {
            builder.ToTable("Tenants");

            builder.HasKey(t => t.TenantId);

            builder.Property(t => t.TenantId)
                .ValueGeneratedNever();

            builder.Property(t => t.Name)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(t => t.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TenantStatus.Pending);

            builder.Property(t => t.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(t => t.ActivatedAt)
                .HasColumnType("datetime2");

            builder.Property(t => t.PrimaryDomain)
                .HasMaxLength(256);

            builder.HasOne(t => t.CurrentSubscription)
                .WithMany()
                .HasForeignKey(t => t.CurrentSubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
