using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class TenantDailySnapshotWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-TenantDailySnapshot-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static TenantDailySnapshotWorker CreateWorker() =>
        new(NullLogger<TenantDailySnapshotWorker>.Instance);

    [Fact]
    public async Task ExecuteAsync_counts_pending_approvals_created_that_day_and_oldest_age()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        db.Transactions.Add(new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 100m,
            TransactionDate = day,
            Description = "A",
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Cash,
            TransactionState = TransactionState.Pending,
            CreatedAt = day.AddHours(2),
        });
        // Older pending transaction from a prior day — should not inflate the day's count but
        // should be the one that determines "oldest pending age".
        db.Transactions.Add(new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 200m,
            TransactionDate = day.AddDays(-3),
            Description = "B",
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Cash,
            TransactionState = TransactionState.Pending,
            CreatedAt = day.AddDays(-3),
        });
        // Approved: must not count as pending.
        db.Transactions.Add(new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 50m,
            TransactionDate = day,
            Description = "C",
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Cash,
            TransactionState = TransactionState.JournalReady,
            CreatedAt = day.AddHours(3),
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        var row = await db.TenantDailySnapshots.SingleAsync();
        Assert.Equal(1, row.PendingApprovalCount);
        Assert.NotNull(row.OldestPendingApprovalAgeHours);
        // The oldest pending transaction (day - 3) is at least 3 days = 72 hours old.
        Assert.True(row.OldestPendingApprovalAgeHours >= 72m);
    }

    [Fact]
    public async Task ExecuteAsync_sums_positive_journal_amounts_posted_that_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        db.JournalEntries.Add(new JournalEntry { JournalEntryId = Guid.NewGuid(), EntryType = "DebitBank", Amount = 500m, PostedAt = day.AddHours(1) });
        db.JournalEntries.Add(new JournalEntry { JournalEntryId = Guid.NewGuid(), EntryType = "CreditCashOut", Amount = -500m, PostedAt = day.AddHours(1) });
        // Outside the window: must not count.
        db.JournalEntries.Add(new JournalEntry { JournalEntryId = Guid.NewGuid(), EntryType = "DebitBank", Amount = 999m, PostedAt = day.AddDays(-1) });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        var row = await db.TenantDailySnapshots.SingleAsync();
        Assert.Equal(2, row.JournalEntriesPostedCount);
        Assert.Equal(500m, row.JournalDebitAmountPosted);
    }

    [Fact]
    public async Task ExecuteAsync_counts_bank_records_dated_that_day_and_how_many_are_matched()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var batch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED", ImportedAt = day };
        db.ImportBatches.Add(batch);

        db.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batch.ImportBatchId,
            TransactionDate = day.AddHours(4),
            MatchStatus = "MATCHED",
            NetAmount = 10m,
            CreatedAt = day,
        });
        db.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batch.ImportBatchId,
            TransactionDate = day.AddHours(5),
            MatchStatus = "PENDING",
            NetAmount = 20m,
            CreatedAt = day,
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        var row = await db.TenantDailySnapshots.SingleAsync();
        Assert.Equal(2, row.BankRecordsTotalCount);
        Assert.Equal(1, row.BankRecordsMatchedCount);
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_when_run_twice_for_the_same_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var tenantId = Guid.NewGuid();

        db.JournalEntries.Add(new JournalEntry { JournalEntryId = Guid.NewGuid(), EntryType = "DebitBank", Amount = 100m, PostedAt = day.AddHours(1) });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(tenantId, db, day);
        await worker.ExecuteAsync(tenantId, db, day);

        var rows = await db.TenantDailySnapshots.ToListAsync();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].JournalEntriesPostedCount);
    }
}
