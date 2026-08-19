using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class SystemBillingSettingsConfiguration : IEntityTypeConfiguration<SystemBillingSettings>
    {
        public void Configure(EntityTypeBuilder<SystemBillingSettings> builder)
        {
            builder.ToTable("SystemBillingSettings");

            builder.HasKey(s => s.SystemBillingSettingsId);

            builder.Property(s => s.SystemBillingSettingsId)
                .ValueGeneratedNever();

            builder.Property(s => s.PaymentOverdueSuspensionThresholdDays)
                .HasDefaultValue(7)
                .IsRequired();

            builder.Property(s => s.UpdatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
        }
    }
}
