using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class SettlementMatchWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-Settlement-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static SettlementMatchWorker CreateWorker()
    {
        return new SettlementMatchWorker(NullLogger<SettlementMatchWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_groups_gateway_transactions_and_matches_to_bank_payout()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var gwBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "GATEWAY", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(gwBatch, bankBatch);

        // 1. Two gateway records belonging to same settlement
        var gwRecord1 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            SettlementId = "SETTLE-001",
            ReferenceNumber = "SETTLE-001",
            NetAmount = 100m, // Gross was 110, fee was 10, payout is 100
            MatchStatus = "LEVEL3_MATCHED" // Rule 6 processes LEVEL3_MATCHED records
        };
        
        var gwRecord2 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            SettlementId = "SETTLE-001",
            ReferenceNumber = "SETTLE-001",
            NetAmount = 200m,
            MatchStatus = "LEVEL3_MATCHED"
        };

        // 2. One bank record for the total payout
        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            ReferenceNumber = "SETTLE-001",
            NetAmount = 300m, // 100 + 200 = 300 payout to bank
            MatchStatus = "PENDING"
        };
        
        tenantDb.ImportedNormalizedRecords.AddRange(gwRecord1, gwRecord2, bankRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.TotalCandidates); // 1 settlement group candidate
        Assert.Equal(1, result.AutoMatchedCount);
        
        var group = await tenantDb.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level6", group.MatchLevel);
        Assert.True(group.IsConfirmed);
        Assert.Equal(300m, group.MatchedAmount); // 300 total

        var updatedGw1 = await tenantDb.ImportedNormalizedRecords.FindAsync(gwRecord1.ImportedNormalizedRecordId);
        var updatedGw2 = await tenantDb.ImportedNormalizedRecords.FindAsync(gwRecord2.ImportedNormalizedRecordId);
        var updatedBank = await tenantDb.ImportedNormalizedRecords.FindAsync(bankRecord.ImportedNormalizedRecordId);
        
        Assert.Equal("LEVEL6_MATCHED", updatedGw1!.MatchStatus);
        Assert.Equal("LEVEL6_MATCHED", updatedGw2!.MatchStatus);
        Assert.Equal("MATCHED", updatedBank!.MatchStatus);
    }
}
