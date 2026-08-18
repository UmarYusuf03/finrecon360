using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class ReconciliationSnapshotWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-ReconciliationSnapshot-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static ReconciliationSnapshotWorker CreateWorker() =>
        new(NullLogger<ReconciliationSnapshotWorker>.Instance);

    [Fact]
    public async Task ExecuteAsync_counts_matched_and_confirmed_groups_per_level_for_the_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        // Created and confirmed same day: contributes to both counts and the dwell-time average.
        db.ReconciliationMatchGroups.Add(new ReconciliationMatchGroup
        {
            ReconciliationMatchGroupId = Guid.NewGuid(),
            MatchLevel = "Level4",
            SettlementKey = "A",
            CreatedAt = day.AddHours(2),
            ConfirmedAt = day.AddHours(4),
            IsConfirmed = true,
        });
        // Created same day, still unconfirmed: contributes to MatchedCount only.
        db.ReconciliationMatchGroups.Add(new ReconciliationMatchGroup
        {
            ReconciliationMatchGroupId = Guid.NewGuid(),
            MatchLevel = "Level4",
            SettlementKey = "B",
            CreatedAt = day.AddHours(6),
        });
        // Outside the day entirely: must not leak into the count.
        db.ReconciliationMatchGroups.Add(new ReconciliationMatchGroup
        {
            ReconciliationMatchGroupId = Guid.NewGuid(),
            MatchLevel = "Level4",
            SettlementKey = "C",
            CreatedAt = day.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        var result = await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        Assert.Equal(1, result.RowsUpserted);
        var row = await db.ReconciliationDailySnapshots.SingleAsync();
        Assert.Equal("Level4", row.MatchLevel);
        Assert.Equal(2, row.MatchedCount);
        Assert.Equal(1, row.ConfirmedCount);
        Assert.Equal(2m, row.AverageTimeToMatchHours);
    }

    [Fact]
    public async Task ExecuteAsync_counts_variance_events_per_level_for_the_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        db.ReconciliationEvents.Add(new ReconciliationEvent
        {
            ReconciliationEventId = Guid.NewGuid(),
            EventType = "Variance",
            MatchLevel = "Level3",
            CreatedAt = day.AddHours(1),
        });
        db.ReconciliationEvents.Add(new ReconciliationEvent
        {
            ReconciliationEventId = Guid.NewGuid(),
            EventType = "Variance",
            MatchLevel = "Level3",
            CreatedAt = day.AddHours(3),
        });
        // Different event type: must not count as an exception.
        db.ReconciliationEvents.Add(new ReconciliationEvent
        {
            ReconciliationEventId = Guid.NewGuid(),
            EventType = "MatchFound",
            MatchLevel = "Level3",
            CreatedAt = day.AddHours(3),
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        var row = await db.ReconciliationDailySnapshots.SingleAsync(r => r.MatchLevel == "Level3");
        Assert.Equal(2, row.ExceptionCount);
        Assert.Equal(0, row.MatchedCount);
    }

    [Fact]
    public async Task ExecuteAsync_counts_unmatched_bank_records_dated_that_day_only_for_level4()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var batch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            ImportedAt = day,
        };
        db.ImportBatches.Add(batch);

        db.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batch.ImportBatchId,
            TransactionDate = day.AddHours(5),
            MatchStatus = "PENDING",
            NetAmount = 100m,
            CreatedAt = day,
        });
        // Matched already: must not count as unmatched.
        db.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batch.ImportBatchId,
            TransactionDate = day.AddHours(6),
            MatchStatus = "MATCHED",
            NetAmount = 50m,
            CreatedAt = day,
        });
        // Dated a different day: must not count toward this day's snapshot.
        db.ImportedNormalizedRecords.Add(new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batch.ImportBatchId,
            TransactionDate = day.AddDays(1),
            MatchStatus = "PENDING",
            NetAmount = 75m,
            CreatedAt = day,
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        var row = await db.ReconciliationDailySnapshots.SingleAsync();
        Assert.Equal("Level4", row.MatchLevel);
        Assert.Equal(1, row.UnmatchedCount);
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_when_run_twice_for_the_same_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var tenantId = Guid.NewGuid();

        db.ReconciliationMatchGroups.Add(new ReconciliationMatchGroup
        {
            ReconciliationMatchGroupId = Guid.NewGuid(),
            MatchLevel = "Level1",
            SettlementKey = "A",
            CreatedAt = day.AddHours(1),
        });
        await db.SaveChangesAsync();

        var worker = CreateWorker();
        await worker.ExecuteAsync(tenantId, db, day);
        await worker.ExecuteAsync(tenantId, db, day);

        var rows = await db.ReconciliationDailySnapshots.Where(r => r.MatchLevel == "Level1").ToListAsync();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].MatchedCount);
    }

    [Fact]
    public async Task ExecuteAsync_returns_zero_rows_when_there_is_no_activity_for_the_day()
    {
        using var db = CreateTenantDb();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        var worker = CreateWorker();
        var result = await worker.ExecuteAsync(Guid.NewGuid(), db, day);

        Assert.Equal(0, result.RowsUpserted);
        Assert.Empty(await db.ReconciliationDailySnapshots.ToListAsync());
    }
}
