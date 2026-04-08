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
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantRegistrationRequest> TenantRegistrationRequests => Set<TenantRegistrationRequest>();
        public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();
        public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
        public DbSet<Plan> Plans => Set<Plan>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<PaymentSession> PaymentSessions => Set<PaymentSession>();
        public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
        public DbSet<EnforcementAction> EnforcementActions => Set<EnforcementAction>();

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
            modelBuilder.ApplyConfiguration(new TenantConfiguration());
            modelBuilder.ApplyConfiguration(new TenantRegistrationRequestConfiguration());
            modelBuilder.ApplyConfiguration(new TenantDatabaseConfiguration());
            modelBuilder.ApplyConfiguration(new TenantUserConfiguration());
            modelBuilder.ApplyConfiguration(new PlanConfiguration());
            modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new PaymentSessionConfiguration());
            modelBuilder.ApplyConfiguration(new MagicLinkTokenConfiguration());
            modelBuilder.ApplyConfiguration(new EnforcementActionConfiguration());

            base.OnModelCreating(modelBuilder);
        }
    }
}
