using finrecon360_backend.Data;
using finrecon360_backend.Services;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class PosErpSyncAuditWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-PosErpSync-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static PosErpSyncAuditWorker CreateWorker()
    {
        return new PosErpSyncAuditWorker(NullLogger<PosErpSyncAuditWorker>.Instance, new ReconciliationSettingsProvider());
    }

    [Fact]
    public async Task ExecuteAsync_auto_matches_when_reference_and_amount_match()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS", Status = "COMMITTED" };
        var erpBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "ERP", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(posBatch, erpBatch);

        var posRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = posBatch.ImportBatchId,
            ReferenceNumber = "ORD-001",
            NetAmount = 100m,
            MatchStatus = "PENDING"
        };
        
        var erpRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            ReferenceNumber = "ORD-001",
            NetAmount = 100m,
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.AddRange(posRecord, erpRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.AutoMatchedCount);
        
        var group = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level2", group.MatchLevel);
        Assert.True(group.IsConfirmed);

        var updatedPos = await tenantDb.ImportedNormalizedRecords.FindAsync(posRecord.ImportedNormalizedRecordId);
        var updatedErp = await tenantDb.ImportedNormalizedRecords.FindAsync(erpRecord.ImportedNormalizedRecordId);
        
        Assert.Equal("LEVEL2_MATCHED", updatedPos!.MatchStatus);
        Assert.Equal("MATCHED", updatedErp!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_logs_MatchNotFound_when_POS_has_no_ERP_entry()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS", Status = "COMMITTED" };
        tenantDb.ImportBatches.Add(posBatch);

        var posRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = posBatch.ImportBatchId,
            ReferenceNumber = "ORD-NO-SYNC",
            NetAmount = 100m,
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.Add(posRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.NoMatchCount);

        var evt = await tenantDb.ReconciliationEvents.FirstOrDefaultAsync();
        Assert.NotNull(evt);
        Assert.Equal("MatchNotFound", evt.EventType);
        Assert.Equal("Level2", evt.MatchLevel);
    }
}
