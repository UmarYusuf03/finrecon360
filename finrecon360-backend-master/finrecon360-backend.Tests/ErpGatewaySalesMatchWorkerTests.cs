using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class ErpGatewaySalesMatchWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-ErpGateway-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static ErpGatewaySalesMatchWorker CreateWorker()
    {
        return new ErpGatewaySalesMatchWorker(NullLogger<ErpGatewaySalesMatchWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_auto_matches_when_reference_and_amount_match()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var erpBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "ERP", Status = "COMMITTED" };
        var gwBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "GATEWAY", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(erpBatch, gwBatch);

        var erpRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            ReferenceNumber = "INV-555",
            NetAmount = 250m,
            MatchStatus = "PENDING"
        };
        
        var gwRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            ReferenceNumber = "INV-555",
            NetAmount = 250m,
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.AddRange(erpRecord, gwRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.AutoMatchedCount);
        
        var group = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level3", group.MatchLevel);

        var updatedErp = await tenantDb.ImportedNormalizedRecords.FindAsync(erpRecord.ImportedNormalizedRecordId);
        var updatedGw = await tenantDb.ImportedNormalizedRecords.FindAsync(gwRecord.ImportedNormalizedRecordId);
        
        Assert.Equal("SALES_VERIFIED", updatedErp!.MatchStatus);
        Assert.Equal("LEVEL3_MATCHED", updatedGw!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_logs_Variance_when_amounts_differ()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var erpBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "ERP", Status = "COMMITTED" };
        var gwBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "GATEWAY", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(erpBatch, gwBatch);

        var erpRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = erpBatch.ImportBatchId,
            ReferenceNumber = "INV-555",
            NetAmount = 250m,
            MatchStatus = "PENDING"
        };
        
        var gwRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            ReferenceNumber = "INV-555",
            NetAmount = 240m, // Fee deduction variance
            MatchStatus = "PENDING"
        };
        tenantDb.ImportedNormalizedRecords.AddRange(erpRecord, gwRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.ExceptionCount);
        
        var evt = await tenantDb.ReconciliationEvents.FirstOrDefaultAsync();
        Assert.NotNull(evt);
        Assert.Equal("Variance", evt.EventType);
        Assert.Equal("Level3", evt.MatchLevel);
    }
}
