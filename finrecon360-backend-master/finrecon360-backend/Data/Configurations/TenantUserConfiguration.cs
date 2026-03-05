using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
    {
        public void Configure(EntityTypeBuilder<TenantUser> builder)
        {
            builder.ToTable("TenantUsers");

            builder.HasKey(tu => tu.TenantUserId);

            builder.Property(tu => tu.TenantUserId)
                .ValueGeneratedNever();

            builder.Property(tu => tu.Role)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(TenantUserRole.TenantUser)
                .HasSentinel(TenantUserRole.TenantUser);

            builder.Property(tu => tu.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasOne(tu => tu.Tenant)
                .WithMany(t => t.TenantUsers)
                .HasForeignKey(tu => tu.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(tu => tu.User)
                .WithMany()
                .HasForeignKey(tu => tu.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(tu => new { tu.TenantId, tu.UserId })
                .IsUnique();
        }
    }
}
