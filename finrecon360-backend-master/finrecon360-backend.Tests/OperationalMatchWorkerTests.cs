using finrecon360_backend.Data;
using finrecon360_backend.Services;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class OperationalMatchWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-OperationalRecon-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static OperationalMatchWorker CreateWorker()
    {
        return new OperationalMatchWorker(NullLogger<OperationalMatchWorker>.Instance, new ReconciliationSettingsProvider());
    }

    [Fact]
    public async Task ExecuteAsync_with_no_pending_transactions_returns_zero_counts()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();

        var result = await worker.ExecuteAsync(Guid.NewGuid(), tenantDb);

        Assert.Equal(0, result.TotalCandidates);
        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);
    }

    [Fact]
    public async Task ExecuteAsync_auto_matches_when_POS_record_matches_amount_and_date()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;
        var amount = 500m;

        // 1. Staff manual cash-in transaction
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = amount,
            TransactionDate = txnDate,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Cash,
            Description = "Shift 1 Cash",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        // 2. POS EOD record
        var posBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "POS",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow
        };
        tenantDb.ImportBatches.Add(posBatch);

        var posRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = posBatch.ImportBatchId,
            TransactionDate = txnDate,
            ReferenceNumber = "Shift 1 Cash", // MUST match Transaction Description
            NetAmount = amount,
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(posRecord);

        await tenantDb.SaveChangesAsync();

        // 3. Execute
        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        // 4. Verify
        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.AutoMatchedCount);
        
        var matchGroup = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(matchGroup);
        Assert.Equal("Level1", matchGroup.MatchLevel);
        Assert.True(matchGroup.IsConfirmed);

        var updatedRecord = await tenantDb.ImportedNormalizedRecords.FindAsync(posRecord.ImportedNormalizedRecordId);
        Assert.Equal("MATCHED", updatedRecord!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_logs_variance_event_when_amount_differs()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 500m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Cash,
            Description = "REF-123",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS", Status = "COMMITTED", ImportedAt = DateTime.UtcNow };
        tenantDb.ImportBatches.Add(posBatch);

        var posRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = posBatch.ImportBatchId,
            TransactionDate = txnDate,
            ReferenceNumber = "REF-123", // Reference matches
            NetAmount = 490m, // But amount differs
            MatchStatus = "PENDING",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.ImportedNormalizedRecords.Add(posRecord);

        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.ExceptionCount);
        
        var evt = await tenantDb.ReconciliationEvents.FirstOrDefaultAsync();
        Assert.NotNull(evt);
        Assert.Equal("Variance", evt.EventType);
        Assert.Equal("Level1", evt.MatchLevel);
    }
}
