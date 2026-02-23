using finrecon360_backend.Data.Configurations;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace finrecon360_backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<AuthActionToken> AuthActionTokens => Set<AuthActionToken>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<AppComponent> AppComponents => Set<AppComponent>();
        public DbSet<PermissionAction> PermissionActions => Set<PermissionAction>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserConfiguration());
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
            modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
            modelBuilder.ApplyConfiguration(new RolePermissionConfiguration());
            modelBuilder.ApplyConfiguration(new AuthActionTokenConfiguration());
            modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
            modelBuilder.ApplyConfiguration(new AppComponentConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionActionConfiguration());

            base.OnModelCreating(modelBuilder);
        }
    }
}
