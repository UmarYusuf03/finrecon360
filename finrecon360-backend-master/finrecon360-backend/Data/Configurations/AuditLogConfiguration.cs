using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace finrecon360_backend.Data.Configurations
{
    public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            builder.ToTable("AuditLogs");

            builder.HasKey(a => a.AuditLogId);

            builder.Property(a => a.AuditLogId)
                .ValueGeneratedNever();

            builder.Property(a => a.Action)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(a => a.Entity)
                .HasMaxLength(200);

            builder.Property(a => a.EntityId)
                .HasMaxLength(100);

            builder.Property(a => a.Metadata)
                .HasColumnType("nvarchar(max)");

            builder.Property(a => a.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(a => a.CreatedAt);
            builder.HasIndex(a => a.UserId);

            builder.HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
