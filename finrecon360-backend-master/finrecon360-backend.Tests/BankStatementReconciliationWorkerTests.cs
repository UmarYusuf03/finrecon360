using finrecon360_backend.Data;
using finrecon360_backend.Services;
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
        return new BankStatementReconciliationWorker(NullLogger<BankStatementReconciliationWorker>.Instance, new ReconciliationSettingsProvider());
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
    public async Task ExecuteAsync_tier1_exact_match_auto_confirms_and_promotes_to_JournalReady()
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
            OriginalFileName = "gateway.csv",
            ImportedAt = DateTime.UtcNow
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
            OriginalFileName = "bank.csv",
            ImportedAt = DateTime.UtcNow
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

        // 6. Tier1 (exact match, no fee) auto-confirms AND promotes the transaction —
        // IsConfirmed alone doesn't move it; the worker also runs CardCashoutPromoter.
        var updatedTxn = await tenantDb.Transactions.FirstOrDefaultAsync(x => x.TransactionId == txn.TransactionId);
        Assert.NotNull(updatedTxn);
        Assert.Equal(TransactionState.JournalReady, updatedTxn.TransactionState);

        var stateHistory = await tenantDb.TransactionStateHistories
            .FirstOrDefaultAsync(h => h.TransactionId == txn.TransactionId);
        Assert.NotNull(stateHistory);
        Assert.Null(stateHistory!.ChangedByUserId); // system auto-confirm, not a human

        // 7. Verify match group was auto-confirmed
        var matchGroup = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(matchGroup);
        Assert.Equal("Level4", matchGroup.MatchLevel);
        Assert.True(matchGroup.IsConfirmed);
        Assert.NotNull(matchGroup.ConfirmedAt);
        Assert.Null(matchGroup.ConfirmedByUserId); // system, not a human
        Assert.Equal(0m, matchGroup.Variance);
        Assert.Equal("ACCT001|REF001", matchGroup.SettlementKey);

        var metadata = finrecon360_backend.Services.Reconciliation.MatchGroupMetadata
            .TryParse(matchGroup.MatchMetadataJson);
        Assert.NotNull(metadata);
        Assert.True(metadata.AutoMatched);
        Assert.Equal("Tier1_ExactMatch", metadata.RuleTriggered);
        Assert.Equal(txn.TransactionId, metadata.TransactionId);

        // 8. Verify matched records linked
        var matchedRecords = await tenantDb.ReconciliationMatchedRecords
            .Where(mr => mr.ReconciliationMatchGroupId == matchGroup.ReconciliationMatchGroupId)
            .ToListAsync();
        Assert.Equal(2, matchedRecords.Count);
        Assert.Single(matchedRecords, mr => mr.SourceType == "GATEWAY");
        Assert.Single(matchedRecords, mr => mr.SourceType == "BANK");
    }

    [Fact]
    public async Task ExecuteAsync_tier2_fee_explained_match_auto_confirms_with_variance_equal_to_fee()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Test card cashout with gateway fee",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            OriginalFileName = "gateway.csv",
            ImportedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(gatewayBatch);

        // GATEWAY nets to the transaction amount (this is what Step 1 matches on) but carries a
        // known 30 LKR processing fee — the bank only ever receives txn.Amount - fee.
        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = txnDate,
            GrossAmount = 1030m,
            ProcessingFee = 30m,
            NetAmount = 1000m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        var bankBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            OriginalFileName = "bank.csv",
            ImportedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(bankBatch);

        // Bank deposit is exactly txn.Amount - ProcessingFee (970), not txn.Amount (1000).
        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 970m,
            ProcessingFee = 0m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(bankRecord);

        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);

        var updatedTxn = await tenantDb.Transactions.FirstOrDefaultAsync(x => x.TransactionId == txn.TransactionId);
        Assert.Equal(TransactionState.JournalReady, updatedTxn!.TransactionState);

        var matchGroup = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(matchGroup);
        Assert.True(matchGroup!.IsConfirmed);
        // Variance is recorded as the known fee, not zero and not flagged as unexplained.
        Assert.Equal(30m, matchGroup.Variance);

        var metadata = finrecon360_backend.Services.Reconciliation.MatchGroupMetadata
            .TryParse(matchGroup.MatchMetadataJson);
        Assert.Equal("Tier2_FeeExplained", metadata!.RuleTriggered);
        Assert.Equal(30m, metadata.ProcessingFeeTotal);
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
            OriginalFileName = "gateway.csv",
            ImportedAt = DateTime.UtcNow
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
            OriginalFileName = "bank.csv",
            ImportedAt = DateTime.UtcNow
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
            OriginalFileName = "gateway.csv",
            ImportedAt = DateTime.UtcNow
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
            OriginalFileName = "gateway.csv",
            ImportedAt = DateTime.UtcNow
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

    [Fact]
    public async Task ExecuteAsync_scopes_BANK_candidates_to_the_transactions_bank_account()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        // Card cashout tied to accountA.
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.NeedsBankMatch,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            BankAccountId = accountA,
            Description = "Card cashout scoped to account A",
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow,
        };
        tenantDb.Transactions.Add(txn);

        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow,
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
            CreatedAt = DateTime.UtcNow,
        };
        tenantDb.ImportedNormalizedRecords.Add(gatewayRecord);

        // Two BANK batches — one per physical account — both containing a record with the same
        // settlement key and amount. Without account scoping these would collide as ambiguous.
        var bankBatchA = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            BankAccountId = accountA,
            ImportedAt = DateTime.UtcNow,
        };
        var bankBatchB = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            BankAccountId = accountB,
            ImportedAt = DateTime.UtcNow,
        };
        tenantDb.ImportBatches.AddRange(bankBatchA, bankBatchB);

        var bankRecordA = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatchA.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 1000m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow,
        };
        var bankRecordB = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatchB.ImportBatchId,
            TransactionDate = txnDate,
            NetAmount = 1000m,
            Currency = "LKR",
            AccountCode = "ACCT001",
            ReferenceNumber = "REF001",
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow,
        };
        tenantDb.ImportedNormalizedRecords.AddRange(bankRecordA, bankRecordB);

        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // Scoping to accountA leaves exactly one eligible candidate, so this is a clean match
        // rather than an ambiguous one.
        Assert.Equal(1, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);

        var updatedA = await tenantDb.ImportedNormalizedRecords.FindAsync(bankRecordA.ImportedNormalizedRecordId);
        var updatedB = await tenantDb.ImportedNormalizedRecords.FindAsync(bankRecordB.ImportedNormalizedRecordId);
        Assert.Equal("MATCHED", updatedA!.MatchStatus);
        Assert.Equal("PENDING", updatedB!.MatchStatus); // untouched — wrong account
    }
}
