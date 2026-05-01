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
        public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<TransactionStateHistory> TransactionStateHistories => Set<TransactionStateHistory>();
        public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
        public DbSet<ImportedRawRecord> ImportedRawRecords => Set<ImportedRawRecord>();
        public DbSet<ImportedNormalizedRecord> ImportedNormalizedRecords => Set<ImportedNormalizedRecord>();
        public DbSet<ImportMappingTemplate> ImportMappingTemplates => Set<ImportMappingTemplate>();

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

            modelBuilder.Entity<BankAccount>(entity =>
            {
                entity.ToTable("BankAccounts");
                entity.HasKey(x => x.BankAccountId);
                entity.Property(x => x.BankAccountId).ValueGeneratedNever();
                entity.Property(x => x.BankName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.AccountNumber).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");
                entity.HasIndex(x => x.AccountNumber).IsUnique();
            });

            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.ToTable("Transactions", table =>
                {
                    table.HasCheckConstraint("CK_Transactions_Amount_Positive", "[Amount] > 0");
                    table.HasCheckConstraint("CK_Transactions_TransactionType", "[TransactionType] IN (N'CashIn', N'CashOut')");
                    table.HasCheckConstraint("CK_Transactions_PaymentMethod", "[PaymentMethod] IN (N'Cash', N'Card')");
                    table.HasCheckConstraint("CK_Transactions_TransactionState", "[TransactionState] IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')");
                    table.HasCheckConstraint("CK_Transactions_PaymentMethod_BankAccount", "([PaymentMethod] <> N'Card' OR [BankAccountId] IS NOT NULL)");
                });
                entity.HasKey(x => x.TransactionId);
                entity.Property(x => x.TransactionId).ValueGeneratedNever();
                entity.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(x => x.TransactionDate).HasColumnType("datetime2").IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
                entity.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.TransactionState)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .HasDefaultValue(TransactionState.Pending)
                    .IsRequired();
                entity.Property(x => x.RejectionReason).HasMaxLength(500);
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(x => x.ApprovedAt).HasColumnType("datetime2");
                entity.Property(x => x.RejectedAt).HasColumnType("datetime2");
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");
                entity.HasIndex(x => x.TransactionDate);
                entity.HasIndex(x => x.BankAccountId);
                entity.HasIndex(x => x.TransactionState);
                entity.HasIndex(x => x.CreatedByUserId);
                entity.HasIndex(x => x.ApprovedByUserId);
                entity.HasIndex(x => x.RejectedByUserId);

                entity.HasOne(x => x.BankAccount)
                    .WithMany()
                    .HasForeignKey(x => x.BankAccountId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<TransactionStateHistory>(entity =>
            {
                entity.ToTable("TransactionStateHistories", table =>
                {
                    table.HasCheckConstraint("CK_TransactionStateHistories_FromState", "[FromState] IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')");
                    table.HasCheckConstraint("CK_TransactionStateHistories_ToState", "[ToState] IN (N'Pending', N'Approved', N'Rejected', N'NeedsBankMatch', N'JournalReady')");
                });
                entity.HasKey(x => x.TransactionStateHistoryId);
                entity.Property(x => x.TransactionStateHistoryId).ValueGeneratedNever();
                entity.Property(x => x.FromState).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(x => x.ToState).HasConversion<string>().HasMaxLength(30).IsRequired();
                entity.Property(x => x.ChangedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.HasIndex(x => x.TransactionId);
                entity.HasIndex(x => x.ChangedAt);

                entity.HasOne(x => x.Transaction)
                    .WithMany(x => x.StateHistories)
                    .HasForeignKey(x => x.TransactionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ImportBatch>(entity =>
            {
                entity.ToTable("ImportBatches");
                entity.HasKey(x => x.ImportBatchId);
                entity.Property(x => x.ImportBatchId).ValueGeneratedNever();
                entity.Property(x => x.MappingTemplateId);
                entity.Property(x => x.SourceType).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
                entity.Property(x => x.OriginalFileName).HasMaxLength(260);
                entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
                entity.Property(x => x.RawRecordCount).HasDefaultValue(0);
                entity.Property(x => x.NormalizedRecordCount).HasDefaultValue(0);
                entity.Property(x => x.ImportedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.ImportedAt);
                entity.HasIndex(x => new { x.SourceType, x.Status });
                entity.HasIndex(x => x.MappingTemplateId);

                entity.HasOne(x => x.MappingTemplate)
                    .WithMany(x => x.Batches)
                    .HasForeignKey(x => x.MappingTemplateId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ImportedRawRecord>(entity =>
            {
                entity.ToTable("ImportedRawRecords");
                entity.HasKey(x => x.ImportedRawRecordId);
                entity.Property(x => x.ImportedRawRecordId).ValueGeneratedNever();
                entity.Property(x => x.SourcePayloadJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.NormalizationStatus).HasMaxLength(50).IsRequired();
                entity.Property(x => x.NormalizationErrors).HasMaxLength(2000);
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.ImportBatchId);
                entity.HasIndex(x => x.CreatedAt);

                entity.HasOne(x => x.ImportBatch)
                    .WithMany(x => x.RawRecords)
                    .HasForeignKey(x => x.ImportBatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ImportedNormalizedRecord>(entity =>
            {
                entity.ToTable("ImportedNormalizedRecords");
                entity.HasKey(x => x.ImportedNormalizedRecordId);
                entity.Property(x => x.ImportedNormalizedRecordId).ValueGeneratedNever();
                entity.Property(x => x.TransactionType).HasMaxLength(30);
                entity.Property(x => x.ReferenceNumber).HasMaxLength(120);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.AccountCode).HasMaxLength(100);
                entity.Property(x => x.AccountName).HasMaxLength(200);
                entity.Property(x => x.GrossAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.ProcessingFee).HasColumnType("decimal(18,2)");
                entity.Property(x => x.DebitAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.CreditAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(x => x.ImportBatchId);
                entity.HasIndex(x => x.TransactionDate);

                entity.HasOne(x => x.ImportBatch)
                    .WithMany(x => x.NormalizedRecords)
                    .HasForeignKey(x => x.ImportBatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.SourceRawRecord)
                    .WithMany(x => x.NormalizedRecords)
                    .HasForeignKey(x => x.SourceRawRecordId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ImportMappingTemplate>(entity =>
            {
                entity.ToTable("ImportMappingTemplates");
                entity.HasKey(x => x.ImportMappingTemplateId);
                entity.Property(x => x.ImportMappingTemplateId).ValueGeneratedNever();
                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.SourceType).HasMaxLength(100).IsRequired();
                entity.Property(x => x.CanonicalSchemaVersion).HasMaxLength(30).IsRequired();
                entity.Property(x => x.MappingJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(x => x.Version).HasDefaultValue(1);
                entity.Property(x => x.IsActive).HasDefaultValue(true);
                entity.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .HasDefaultValueSql("SYSUTCDATETIME()");
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");
                entity.HasIndex(x => x.Name).IsUnique();
                entity.HasIndex(x => new { x.SourceType, x.IsActive });
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
