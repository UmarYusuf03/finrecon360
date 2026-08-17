using finrecon360_backend.Data;
using finrecon360_backend.Services;
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
        return new SettlementMatchWorker(NullLogger<SettlementMatchWorker>.Instance, new ReconciliationSettingsProvider());
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

    [Fact]
    public async Task ExecuteAsync_flags_ambiguous_match_instead_of_picking_first_candidate()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var gwBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "GATEWAY", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(gwBatch, bankBatch);

        var gwRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            SettlementId = "SETTLE-AMBIGUOUS",
            ReferenceNumber = "SETTLE-AMBIGUOUS",
            NetAmount = 300m,
            MatchStatus = "LEVEL3_MATCHED",
        };

        // Two BANK deposits share the same reference and are both within tolerance of the
        // expected payout (and of each other) — the worker should not silently pick one.
        var bankCandidate1 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            ReferenceNumber = "SETTLE-AMBIGUOUS",
            NetAmount = 300.00m,
            MatchStatus = "PENDING",
        };
        var bankCandidate2 = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            ReferenceNumber = "SETTLE-AMBIGUOUS",
            NetAmount = 300.005m,
            MatchStatus = "PENDING",
        };

        tenantDb.ImportedNormalizedRecords.AddRange(gwRecord, bankCandidate1, bankCandidate2);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(0, result.AutoMatchedCount);
        Assert.Equal(1, result.ExceptionCount);

        var events = await tenantDb.ReconciliationEvents.ToListAsync();
        Assert.Contains(events, e => e.EventType == "RequiresReview");

        // Neither bank candidate should have been matched.
        var updated1 = await tenantDb.ImportedNormalizedRecords.FindAsync(bankCandidate1.ImportedNormalizedRecordId);
        var updated2 = await tenantDb.ImportedNormalizedRecords.FindAsync(bankCandidate2.ImportedNormalizedRecordId);
        Assert.Equal("PENDING", updated1!.MatchStatus);
        Assert.Equal("PENDING", updated2!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_uses_tenant_configured_amount_tolerance_instead_of_hardcoded_default()
    {
        using var tenantDb = CreateTenantDb();
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        // A custom, wider tolerance than the 0.01 default every worker used to hardcode.
        tenantDb.ReconciliationSettings.Add(new ReconciliationSettings
        {
            ReconciliationSettingsId = Guid.NewGuid(),
            AmountTolerance = 5.00m,
            DateToleranceDays = 1,
        });

        var gwBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "GATEWAY", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        tenantDb.ImportBatches.AddRange(gwBatch, bankBatch);

        var gwRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = gwBatch.ImportBatchId,
            SettlementId = "SETTLE-TOLERANCE",
            ReferenceNumber = "SETTLE-TOLERANCE",
            NetAmount = 300m,
            MatchStatus = "LEVEL3_MATCHED",
        };

        // Delta of 4 from expected payout — would NOT match under the old hardcoded 0.01
        // tolerance, but should match under the tenant's configured 5.00 tolerance.
        var bankRecord = new ImportedNormalizedRecord
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = bankBatch.ImportBatchId,
            ReferenceNumber = "SETTLE-TOLERANCE",
            NetAmount = 304m,
            MatchStatus = "PENDING",
        };

        tenantDb.ImportedNormalizedRecords.AddRange(gwRecord, bankRecord);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.AutoMatchedCount);
        var updatedBank = await tenantDb.ImportedNormalizedRecords.FindAsync(bankRecord.ImportedNormalizedRecordId);
        Assert.Equal("MATCHED", updatedBank!.MatchStatus);
    }
}
