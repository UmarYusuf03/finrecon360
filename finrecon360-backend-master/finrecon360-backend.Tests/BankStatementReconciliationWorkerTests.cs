using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class BankStatementReconciliationWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-BankRecon-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static BankStatementReconciliationWorker CreateWorker()
    {
        return new BankStatementReconciliationWorker(NullLogger<BankStatementReconciliationWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_with_no_NeedsBankMatch_transactions_returns_zero_counts()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var result = await worker.ExecuteAsync(Guid.NewGuid(), tenantDb);

        Assert.Equal(0, result.NeedsBankMatchCount);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);
    }

    [Fact]
    public async Task ExecuteAsync_auto_matches_transaction_when_GATEWAY_and_BANK_records_found()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;
        var txnAmount = 1000m;

        // 1. Create NeedsBankMatch transaction
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = txnAmount,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Test card cashout",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        // 2. Create GATEWAY import batch and records
        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            FileName = "gateway.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(gatewayBatch);

        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = txnDate,
            GrossAmount = null,
            ProcessingFee = 0m,
            NetAmount = txnAmount,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        // 3. Create BANK import batch and records
        var bankBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            FileName = "bank.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(bankBatch);

        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = txnAmount,
            ProcessingFee = 0m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(bankRecord);

        await tenantDb.SaveChangesAsync();

        // 4. Execute reconciliation
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 5. Verify results
        Assert.Equal(1, result.NeedsBankMatchCount);
        Assert.Equal(1, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);

        // 6. Verify transaction moved to JournalReady
        var updatedTxn = await tenantDb.Transactions.FirstOrDefaultAsync(x => x.TransactionId == txn.TransactionId);
        Assert.NotNull(updatedTxn);
        Assert.Equal(TransactionState.JournalReady, updatedTxn.TransactionState);

        // 7. Verify match group created
        var matchGroup = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(matchGroup);
        Assert.Equal("Level4", matchGroup.MatchLevel);
        Assert.True(matchGroup.IsConfirmed);
        Assert.Equal("ACCT001|REF001", matchGroup.SettlementKey);

        // 8. Verify matched records linked
        var matchedRecords = await tenantDb.ReconciliationMatchedRecords
            .Where(mr => mr.ReconciliationMatchGroupId == matchGroup.ReconciliationMatchGroupId)
            .ToListAsync();
        Assert.Equal(2, matchedRecords.Count);
        Assert.Single(matchedRecords.Where(mr => mr.SourceType == "GATEWAY"));
        Assert.Single(matchedRecords.Where(mr => mr.SourceType == "BANK"));
    }

    [Fact]
    public async Task ExecuteAsync_handles_amount_variance_as_exception()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        // 1. Create NeedsBankMatch transaction
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Test card cashout with variance",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        // 2. Create GATEWAY record
        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            FileName = "gateway.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(gatewayBatch);

        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = txnDate,
            GrossAmount = null,
            ProcessingFee = 0m,
            NetAmount = 1000m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        // 3. Create BANK record with DIFFERENT amount
        var bankBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            FileName = "bank.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(bankBatch);

        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 950m, // Variance!
            ProcessingFee = 0m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(bankRecord);

        await tenantDb.SaveChangesAsync();

        // 4. Execute reconciliation
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 5. Verify results
        Assert.Equal(1, result.NeedsBankMatchCount);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);

        // 6. Verify transaction remained in NeedsBankMatch
        var updatedTxn = await tenantDb.Transactions.FirstOrDefaultAsync(x => x.TransactionId == txn.TransactionId);
        Assert.NotNull(updatedTxn);
        Assert.Equal(TransactionState.NeedsBankMatch, updatedTxn.TransactionState);

        // 7. The current worker records the exception in the result counters
        // but does not persist a dedicated reconciliation event for variance.
        var events = await tenantDb.ReconciliationEvents.ToListAsync();
        Assert.Empty(events);
    }

    [Fact]
    public async Task ExecuteAsync_handles_missing_SettlementKey_as_exception()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        // 1. Create NeedsBankMatch transaction
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Test card cashout with missing settlement key",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        // 2. Create GATEWAY record WITHOUT settlement key
        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            FileName = "gateway.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(gatewayBatch);

        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 1000m,
            Currency = "LKR",
            // Missing AccountCode and ReferenceNumber
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        await tenantDb.SaveChangesAsync();

        // 3. Execute reconciliation
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 4. Verify results
        Assert.Equal(1, result.NeedsBankMatchCount);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);
    }

    [Fact]
    public async Task ExecuteAsync_handles_no_matching_BANK_records_as_noMatch()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        // 1. Create NeedsBankMatch transaction
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Test card cashout with no bank match",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        // 2. Create GATEWAY record
        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            FileName = "gateway.csv",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(gatewayBatch);

        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 1000m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        // 3. No BANK records created — waiting for bank statement import

        await tenantDb.SaveChangesAsync();

        // 4. Execute reconciliation
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 5. Verify results
        Assert.Equal(1, result.NeedsBankMatchCount);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(1, result.NoMatchCount);

        // 6. Verify transaction remained in NeedsBankMatch
        var updatedTxn = await tenantDb.Transactions.FirstOrDefaultAsync(x => x.TransactionId == txn.TransactionId);
        Assert.NotNull(updatedTxn);
        Assert.Equal(TransactionState.NeedsBankMatch, updatedTxn.TransactionState);
    }

    [Fact]
    public async Task ExecuteAsync_ignores_already_posted_transactions()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        // 1. Create JournalReady transaction (not NeedsBankMatch)
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Already processed transaction",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        await tenantDb.SaveChangesAsync();

        // 2. Execute reconciliation
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 3. Verify results — transaction is not counted
        Assert.Equal(0, result.NeedsBankMatchCount);
        Assert.Equal(0, result.AutoMatchedCount);
    }
}
