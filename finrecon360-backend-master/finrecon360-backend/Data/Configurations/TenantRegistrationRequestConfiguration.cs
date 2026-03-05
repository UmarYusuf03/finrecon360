using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class TenantRegistrationRequestConfiguration : IEntityTypeConfiguration<TenantRegistrationRequest>
    {
        public void Configure(EntityTypeBuilder<TenantRegistrationRequest> builder)
        {
            builder.ToTable("TenantRegistrationRequests");

            builder.HasKey(r => r.TenantRegistrationRequestId);

            builder.Property(r => r.TenantRegistrationRequestId)
                .ValueGeneratedNever();

            builder.Property(r => r.BusinessName)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(r => r.AdminEmail)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(r => r.PhoneNumber)
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(r => r.BusinessRegistrationNumber)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(r => r.BusinessType)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(r => r.OnboardingMetadata)
                .HasColumnType("nvarchar(max)");

            builder.Property(r => r.Status)
                .HasMaxLength(32)
                .HasDefaultValue("PENDING_REVIEW");

            builder.Property(r => r.SubmittedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(r => r.ReviewedAt)
                .HasColumnType("datetime2");

            builder.Property(r => r.ReviewNote)
                .HasMaxLength(1000);

            builder.HasOne(r => r.ReviewedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(r => r.AdminEmail);
            builder.HasIndex(r => r.Status);
        }
    }
}
