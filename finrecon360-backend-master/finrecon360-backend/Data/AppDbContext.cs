using finrecon360_backend.Data.Configurations;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace finrecon360_backend.Data
{
    public class AppDbContext : DbContext
    {
        private readonly IServiceProvider? _serviceProvider;
        private Guid? _currentTenantId;
        private bool _tenantResolved;

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public AppDbContext(DbContextOptions<AppDbContext> options, IServiceProvider serviceProvider)
            : base(options)
        {
            _serviceProvider = serviceProvider;
        }

        private Guid CurrentTenantId
        {
            get
            {
                if (_tenantResolved)
                {
                    return _currentTenantId ?? Guid.Empty;
                }

                _tenantResolved = true;

                if (_serviceProvider == null)
                {
                    return Guid.Empty;
                }

                try
                {
                    var tenantContext = _serviceProvider.GetService(typeof(finrecon360_backend.Services.ITenantContext)) as finrecon360_backend.Services.ITenantContext;
                    var resolved = tenantContext?.ResolveAsync().GetAwaiter().GetResult();
                    _currentTenantId = resolved?.TenantId;
                }
                catch
                {
                    _currentTenantId = null;
                }

                return _currentTenantId ?? Guid.Empty;
            }
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
        public DbSet<SystemTransaction> SystemTransactions => Set<SystemTransaction>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<PaymentGatewayPayout> PaymentGatewayPayouts => Set<PaymentGatewayPayout>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
        public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
        public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
        public DbSet<MatchGroup> MatchGroups => Set<MatchGroup>();
        public DbSet<MatchDecision> MatchDecisions => Set<MatchDecision>();
        public DbSet<ReconciliationException> ReconciliationExceptions => Set<ReconciliationException>();
        public DbSet<ReportSnapshot> ReportSnapshots => Set<ReportSnapshot>();

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

            modelBuilder.Entity<SystemTransaction>()
                .Property(x => x.Type)
                .HasConversion<string>();

            modelBuilder.Entity<SystemTransaction>()
                .Property(x => x.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Invoice>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<Invoice>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PaymentGatewayPayout>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<PaymentGatewayPayout>()
                .Property(x => x.GrossAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PaymentGatewayPayout>()
                .Property(x => x.ProcessingFees)
                .HasPrecision(18, 2);

            modelBuilder.Entity<PaymentGatewayPayout>()
                .Property(x => x.NetAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Customer>()
                .HasMany(x => x.Invoices)
                .WithOne(x => x.Customer)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PaymentGatewayPayout>()
                .HasMany(x => x.Orders)
                .WithOne(x => x.PaymentGatewayPayout)
                .HasForeignKey(x => x.PaymentGatewayPayoutId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SystemTransaction>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<Customer>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<Invoice>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<PaymentGatewayPayout>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<Order>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<BankStatementImport>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<ReconciliationRun>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<MatchGroup>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<MatchDecision>()
                .Property(x => x.Decision)
                .HasConversion<string>();

            modelBuilder.Entity<ReconciliationException>()
                .Property(x => x.Status)
                .HasConversion<string>();

            modelBuilder.Entity<MatchDecision>()
                .HasOne(x => x.MatchGroup)
                .WithMany(x => x.MatchDecisions)
                .HasForeignKey(x => x.MatchGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BankStatementImport>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<BankStatementLine>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<ReconciliationRun>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<MatchGroup>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<MatchDecision>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<ReconciliationException>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);

            modelBuilder.Entity<ReportSnapshot>()
                .HasQueryFilter(e => CurrentTenantId != Guid.Empty && e.TenantId == CurrentTenantId);


        }
    }
}
