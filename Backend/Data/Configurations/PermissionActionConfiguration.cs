using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class PermissionActionConfiguration : IEntityTypeConfiguration<PermissionAction>
    {
        public void Configure(EntityTypeBuilder<PermissionAction> builder)
        {
            builder.ToTable("PermissionActions");

            builder.HasKey(a => a.PermissionActionId);

            builder.Property(a => a.PermissionActionId)
                .ValueGeneratedNever();

            builder.Property(a => a.Code)
                .HasMaxLength(50)
                .IsRequired();

            builder.HasIndex(a => a.Code)
                .IsUnique();

            builder.Property(a => a.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(a => a.Description)
                .HasMaxLength(500);

            builder.Property(a => a.IsActive)
                .HasDefaultValue(true);

            builder.Property(a => a.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
        }
    }
}
