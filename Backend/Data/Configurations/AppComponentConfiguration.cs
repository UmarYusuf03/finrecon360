using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class AppComponentConfiguration : IEntityTypeConfiguration<AppComponent>
    {
        public void Configure(EntityTypeBuilder<AppComponent> builder)
        {
            builder.ToTable("AppComponents");

            builder.HasKey(c => c.AppComponentId);

            builder.Property(c => c.AppComponentId)
                .ValueGeneratedNever();

            builder.Property(c => c.Code)
                .HasMaxLength(100)
                .IsRequired();

            builder.HasIndex(c => c.Code)
                .IsUnique();

            builder.Property(c => c.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(c => c.RoutePath)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(c => c.Category)
                .HasMaxLength(100);

            builder.Property(c => c.Description)
                .HasMaxLength(500);

            builder.Property(c => c.IsActive)
                .HasDefaultValue(true);

            builder.Property(c => c.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
        }
    }
}
