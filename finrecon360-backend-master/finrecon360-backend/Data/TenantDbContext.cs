using Microsoft.EntityFrameworkCore;
using finrecon360_backend.Models;

namespace finrecon360_backend.Data
{
    public class TenantDbContext : DbContext
    {
        public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options)
        {
        }

        public DbSet<TenantScopedUser> TenantUsers => Set<TenantScopedUser>();
        public DbSet<TenantRole> Roles => Set<TenantRole>();
        public DbSet<TenantPermission> Permissions => Set<TenantPermission>();
        public DbSet<TenantRolePermission> RolePermissions => Set<TenantRolePermission>();
        public DbSet<TenantComponent> Components => Set<TenantComponent>();
        public DbSet<TenantPermissionAction> PermissionActions => Set<TenantPermissionAction>();
        public DbSet<TenantUserRoleAssignment> UserRoles => Set<TenantUserRoleAssignment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantScopedUser>(entity =>
            {
                entity.ToTable("TenantUsers");
                entity.HasKey(x => x.TenantUserId);
                entity.Property(x => x.TenantUserId).ValueGeneratedNever();
                entity.Property(x => x.UserId).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                entity.Property(x => x.DisplayName).HasMaxLength(256);
                entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");
                entity.HasIndex(x => x.UserId).IsUnique();
                entity.HasIndex(x => x.Email);
            });

            modelBuilder.Entity<TenantRole>(entity =>
            {
                entity.ToTable("Roles");
                entity.HasKey(x => x.RoleId);
                entity.Property(x => x.RoleId).ValueGeneratedNever();
                entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.IsSystem).HasDefaultValue(false);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<TenantPermission>(entity =>
            {
                entity.ToTable("Permissions");
                entity.HasKey(x => x.PermissionId);
                entity.Property(x => x.PermissionId).ValueGeneratedNever();
                entity.Property(x => x.Code).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.Module).HasMaxLength(100);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<TenantRolePermission>(entity =>
            {
                entity.ToTable("RolePermissions");
                entity.HasKey(x => new { x.RoleId, x.PermissionId });
                entity.Property(x => x.GrantedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasOne(x => x.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(x => x.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantComponent>(entity =>
            {
                entity.ToTable("AppComponents");
                entity.HasKey(x => x.ComponentId);
                entity.Property(x => x.ComponentId).ValueGeneratedNever();
                entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.RoutePath).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Category).HasMaxLength(100);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<TenantPermissionAction>(entity =>
            {
                entity.ToTable("PermissionActions");
                entity.HasKey(x => x.PermissionActionId);
                entity.Property(x => x.PermissionActionId).ValueGeneratedNever();
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(300);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<TenantUserRoleAssignment>(entity =>
            {
                entity.ToTable("UserRoles");
                entity.HasKey(x => new { x.UserId, x.RoleId });
                entity.Property(x => x.AssignedAt).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasOne(x => x.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
