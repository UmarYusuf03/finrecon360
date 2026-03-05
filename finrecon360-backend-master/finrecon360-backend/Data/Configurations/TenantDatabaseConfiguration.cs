using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class TenantDatabaseConfiguration : IEntityTypeConfiguration<TenantDatabase>
    {
        public void Configure(EntityTypeBuilder<TenantDatabase> builder)
        {
            builder.ToTable("TenantDatabases");

            builder.HasKey(d => d.TenantDatabaseId);

            builder.Property(d => d.TenantDatabaseId)
                .ValueGeneratedNever();

            builder.Property(d => d.EncryptedConnectionString)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(d => d.Provider)
                .HasMaxLength(64)
                .HasDefaultValue("SqlServer");

            builder.Property(d => d.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TenantDatabaseStatus.Provisioning);

            builder.Property(d => d.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(d => d.ProvisionedAt)
                .HasColumnType("datetime2");

            builder.HasOne(d => d.Tenant)
                .WithMany(t => t.Databases)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
