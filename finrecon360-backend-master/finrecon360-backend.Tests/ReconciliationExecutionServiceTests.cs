using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Tests;

public class ReconciliationExecutionServiceTests
{
    [Fact]
    public async Task ExecuteOnCommitAsync_for_erp_performs_level3_gross_match()
    {
        await using var db = CreateTenantDb();
        var service = new ReconciliationExecutionService();

        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow
        };

        var erpBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "ERP",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow
        };

        db.ImportBatches.AddRange(gatewayBatch, erpBatch);

        var gatewayRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            ReferenceNumber = "ORD-100",
            GrossAmount = 1000m,
            NetAmount = 950m,
            Currency = "LKR"
        };

        var erpMatch = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            ReferenceNumber = "ORD-100",
            GrossAmount = 1000m,
            NetAmount = 1000m,
            Currency = "LKR"
        };

        var erpMismatch = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            ReferenceNumber = "ORD-200",
            GrossAmount = 500m,
            NetAmount = 500m,
            Currency = "LKR"
        };

        db.ImportedNormalizedRecords.AddRange(gatewayRecord, erpMatch, erpMismatch);
        await db.SaveChangesAsync();

        var result = await service.ExecuteOnCommitAsync(db, erpBatch, new[] { erpMatch, erpMismatch });

        Assert.Equal("ERP", result.SourceType);
        Assert.Equal(1, result.Level3VerifiedCount);
        Assert.Equal(1, result.Level3ExceptionCount);
        Assert.Equal(0, result.Level4MatchedCount);
        Assert.Equal(0m, result.FeeAdjustmentTotal);
    }

    [Fact]
    public async Task ExecuteOnCommitAsync_for_bank_performs_level4_net_settlement_and_fee_total()
    {
        await using var db = CreateTenantDb();
        var service = new ReconciliationExecutionService();

        var gatewayBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "GATEWAY",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow
        };

        var bankBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "BANK",
            Status = "COMMITTED",
            ImportedAt = DateTime.UtcNow
        };

        db.ImportBatches.AddRange(gatewayBatch, bankBatch);

        var g1 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            AccountCode = "SETTLE-1",
            NetAmount = 900m,
            ProcessingFee = 50m,
            Currency = "LKR"
        };

        var g2 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            AccountCode = "SETTLE-1",
            NetAmount = 100m,
            ProcessingFee = 5m,
            Currency = "LKR"
        };

        var g3 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gatewayBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            AccountCode = "SETTLE-2",
            NetAmount = 250m,
            ProcessingFee = 10m,
            Currency = "LKR"
        };

        var bankLineMatched = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            AccountCode = "SETTLE-1",
            NetAmount = 1000m,
            Currency = "LKR"
        };

        var bankLineMismatch = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            TransactionDate = DateTime.UtcNow.Date,
            AccountCode = "SETTLE-2",
            NetAmount = 200m,
            Currency = "LKR"
        };

        db.ImportedNormalizedRecords.AddRange(g1, g2, g3, bankLineMatched, bankLineMismatch);
        await db.SaveChangesAsync();

        var result = await service.ExecuteOnCommitAsync(db, bankBatch, new[] { bankLineMatched, bankLineMismatch });

        Assert.Equal("BANK", result.SourceType);
        Assert.Equal(1, result.Level4MatchedCount);
        Assert.Equal(1, result.Level4ExceptionCount);
        Assert.Equal(55m, result.FeeAdjustmentTotal);
    }

    [Fact]
    public async Task ExecuteOnCommitAsync_for_pos_matches_with_small_time_drift()
    {
        await using var db = CreateTenantDb();
        var service = new ReconciliationExecutionService();

        var now = DateTime.UtcNow;

        var erpBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "ERP",
            Status = "COMMITTED",
            ImportedAt = now
        };

        var posBatch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(),
            SourceType = "POS",
            Status = "COMMITTED",
            ImportedAt = now
        };

        db.ImportBatches.AddRange(erpBatch, posBatch);

        var erpRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            TransactionDate = now,
            ReferenceNumber = "ORD-300",
            GrossAmount = 1250m,
            NetAmount = 1250m,
            Currency = "LKR",
            CreatedAt = now
        };

        var posRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = posBatch.ImportBatchId,
            TransactionDate = now.AddMinutes(3),
            ReferenceNumber = "ord-300",
            GrossAmount = 1250m,
            NetAmount = 1250m,
            Currency = "LKR",
            CreatedAt = now.AddMinutes(3)
        };

        db.ImportedNormalizedRecords.AddRange(erpRecord, posRecord);
        await db.SaveChangesAsync();

        var result = await service.ExecuteOnCommitAsync(db, posBatch, new[] { posRecord });

        Assert.Equal("POS", result.SourceType);
        Assert.Equal(1, result.Level3VerifiedCount);
        Assert.Equal(0, result.Level3ExceptionCount);

        var group = await db.ReconciliationMatchGroups.SingleAsync();
        Assert.Equal("Level1", group.MatchLevel);

        var matchedPos = await db.ImportedNormalizedRecords.SingleAsync(x => x.ImportedNormalizedRecordId == posRecord.ImportedNormalizedRecordId);
        var matchedErp = await db.ImportedNormalizedRecords.SingleAsync(x => x.ImportedNormalizedRecordId == erpRecord.ImportedNormalizedRecordId);

        Assert.Equal("MATCHED", matchedPos.MatchStatus);
        Assert.Equal("MATCHED", matchedErp.MatchStatus);
    }

    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantReconciliation-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }
}