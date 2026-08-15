using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class CollectionMatchWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-CollectionMatch-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static CollectionMatchWorker CreateWorker()
    {
        return new CollectionMatchWorker(NullLogger<CollectionMatchWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_auto_matches_when_amount_date_and_last4_match()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 150m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Card,
            CardLast4 = "4242",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        tenantDb.ImportBatches.Add(bankBatch);

        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = txnDate, // exact date match
            NetAmount = 150m,
            Description = "POS SWIPE CARD 4242",
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.Add(bankRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.AutoMatchedCount);
        
        var group = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level5", group.MatchLevel);
        Assert.True(group.IsConfirmed); // Exact date match confirms automatically

        var updatedBank = await tenantDb.ImportedNormalizedRecords.FindAsync(bankRecord.ImportedNormalizedRecordId);
        Assert.Equal("MATCHED", updatedBank!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_creates_pending_match_when_date_drift_occurs()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();
        var txnDate = DateTime.UtcNow.Date;

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 150m,
            TransactionDate = txnDate,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Card,
            CardLast4 = "4242",
            CreatedAt = DateTime.UtcNow
        };
        tenantDb.Transactions.Add(txn);

        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        tenantDb.ImportBatches.Add(bankBatch);

        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = txnDate.AddDays(1), // Date drift of 1 day
            NetAmount = 150m,
            Description = "POS SWIPE CARD 4242",
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.Add(bankRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.AutoMatchedCount);
        
        var group = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level5", group.MatchLevel);
        Assert.False(group.IsConfirmed); // Date drift puts it in pending confirmation state!
        Assert.Equal("Pending", group.Status);
    }
}
