using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class PosSettlementMatchWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-PosSettlement-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static PosSettlementMatchWorker CreateWorker() =>
        new(NullLogger<PosSettlementMatchWorker>.Instance, new ReconciliationSettingsProvider());

    private static void SeedChartOfAccounts(TenantDbContext db)
    {
        db.ChartOfAccounts.AddRange(
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "1000-BANK", Name = "Bank", AccountType = AccountType.Asset },
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "5000-FEE", Name = "MDR Fee", AccountType = AccountType.Expense },
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "6000-POSCLEARING", Name = "POS Clearing", AccountType = AccountType.Liability });
    }

    private static ImportedNormalizedRecord MakePos(
        Guid batchId, DateTime date, decimal gross, decimal fee, string? batchNumber = null, string? terminalId = null, string? merchantId = null) =>
        new()
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batchId,
            TransactionDate = date,
            GrossAmount = gross,
            ProcessingFee = fee,
            NetAmount = gross - fee,
            Currency = "LKR",
            MatchStatus = "PENDING",
            BatchNumber = batchNumber,
            TerminalId = terminalId,
            MerchantId = merchantId,
        };

    private static ImportedNormalizedRecord MakeBank(
        Guid batchId, DateTime date, decimal net, string? batchNumber = null, string? terminalId = null, string? merchantId = null) =>
        new()
        {
            ImportedNormalizedRecordId = Guid.NewGuid(),
            ImportBatchId = batchId,
            TransactionDate = date,
            NetAmount = net,
            Currency = "LKR",
            MatchStatus = "PENDING",
            BatchNumber = batchNumber,
            TerminalId = terminalId,
            MerchantId = merchantId,
        };

    [Fact]
    public async Task ExecuteAsync_tier1_auto_matches_1to1_and_posts_balanced_voucher()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 970m, batchNumber: "452"); // within 3-day window
        db.ImportedNormalizedRecords.AddRange(pos, bank);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(1, result.Tier1Matched);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(0, result.NoMatchCount);

        var group = await db.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal("Level7", group!.MatchLevel);
        Assert.True(group.IsConfirmed); // Tier1 auto-confirms
        Assert.True(group.IsJournalPosted);

        var voucher = await db.JournalVouchers.Include(v => v.Entries)
            .FirstOrDefaultAsync(v => v.ReconciliationMatchGroupId == group.ReconciliationMatchGroupId);
        Assert.NotNull(voucher);
        Assert.Equal(3, voucher!.Entries.Count);
        Assert.Equal(0m, voucher.Entries.Sum(e => e.Amount)); // Debit 970 + Debit 30 - Credit 1000 = 0
        Assert.Equal(970m, voucher.Entries.Single(e => e.EntryType == "DebitBankPos").Amount);
        Assert.Equal(30m, voucher.Entries.Single(e => e.EntryType == "DebitMdrFee").Amount);
        Assert.Equal(-1000m, voucher.Entries.Single(e => e.EntryType == "CreditPosClearingGross").Amount);

        var updatedPos = await db.ImportedNormalizedRecords.FindAsync(pos.ImportedNormalizedRecordId);
        var updatedBank = await db.ImportedNormalizedRecords.FindAsync(bank.ImportedNormalizedRecordId);
        Assert.Equal("MATCHED", updatedPos!.MatchStatus);
        Assert.Equal("MATCHED", updatedBank!.MatchStatus);
    }

    [Fact]
    public async Task ExecuteAsync_tier1_matches_1to_many_split_network_settlement()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        // One POS batch, split by the bank into a Visa/Mastercard deposit and a separate Amex deposit.
        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        var bankVisaMc = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 700m, batchNumber: "452");
        var bankAmex = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 270m, batchNumber: "452");
        db.ImportedNormalizedRecords.AddRange(pos, bankVisaMc, bankAmex);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(1, result.Tier1Matched);
        var group = await db.ReconciliationMatchGroups
            .Include(g => g.MatchedRecords)
            .FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.Equal(3, group!.MatchedRecords.Count); // 1 POS + 2 BANK
        Assert.Equal(2, group.MatchedRecords.Count(r => r.SourceType == "BANK"));
    }

    [Fact]
    public async Task ExecuteAsync_tier2_consolidates_many_pos_batches_by_terminal_into_one_deposit()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        // No BatchNumber on either side (Tier1 skips these) — but both POS batches share a TerminalId,
        // and their combined net equals one lump bank deposit.
        var pos1 = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 500m, fee: 15m, terminalId: "88552");
        var pos2 = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 300m, fee: 9m, terminalId: "88552");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 776m, terminalId: "88552"); // 485 + 291
        db.ImportedNormalizedRecords.AddRange(pos1, pos2, bank);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(0, result.Tier1Matched);
        Assert.Equal(1, result.Tier2Matched);

        var group = await db.ReconciliationMatchGroups.Include(g => g.MatchedRecords).FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.True(group!.IsConfirmed); // Tier2 also auto-confirms
        Assert.Equal(2, group.MatchedRecords.Count(r => r.SourceType == "POS_SETTLEMENT"));
    }

    [Fact]
    public async Task ExecuteAsync_tier3_aggregated_match_never_auto_confirms()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        // No BatchNumber, no TerminalId anywhere — only MerchantId survives extraction.
        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, merchantId: "987654321001");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 970m, merchantId: "987654321001");
        db.ImportedNormalizedRecords.AddRange(pos, bank);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(0, result.Tier1Matched);
        Assert.Equal(0, result.Tier2Matched);
        Assert.Equal(1, result.Tier3Matched);

        var group = await db.ReconciliationMatchGroups.FirstOrDefaultAsync();
        Assert.NotNull(group);
        Assert.False(group!.IsConfirmed); // Tier3 always requires human review
        Assert.Equal("Pending", group.Status);
        Assert.False(group.IsJournalPosted); // not posted until a human confirms
    }

    [Fact]
    public async Task ExecuteAsync_flags_variance_when_batch_key_matches_but_amount_does_not_reconcile()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 900m, batchNumber: "452"); // 70 short
        db.ImportedNormalizedRecords.AddRange(pos, bank);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(0, result.Tier1Matched);
        Assert.Equal(1, result.ExceptionCount);

        var events = await db.ReconciliationEvents.ToListAsync();
        Assert.Contains(events, e => e.EventType == "Variance");

        var updatedPos = await db.ImportedNormalizedRecords.FindAsync(pos.ImportedNormalizedRecordId);
        Assert.Equal("EXCEPTION", updatedPos!.MatchStatus);

        // A batch-key-matched-but-mismatched pair is a distinct exception, not something Tier3
        // should be allowed to blend into a broader aggregate.
        Assert.Equal(0, result.Tier3Matched);
    }

    [Fact]
    public async Task ExecuteAsync_emits_MatchNotFound_for_records_with_no_reconcilable_counterpart()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        db.ImportBatches.Add(posBatch);

        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        db.ImportedNormalizedRecords.Add(pos);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(1, result.NoMatchCount);
        var events = await db.ReconciliationEvents.ToListAsync();
        Assert.Contains(events, e => e.EventType == "MatchNotFound");

        var updatedPos = await db.ImportedNormalizedRecords.FindAsync(pos.ImportedNormalizedRecordId);
        Assert.Equal("PENDING", updatedPos!.MatchStatus); // stays pending for a later cycle
    }

    [Fact]
    public async Task ExecuteAsync_excludes_bank_deposit_outside_the_settlement_window()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        // Default SettlementDateWindowDays is 3 business days; Monday 2026-08-17 + 3 business
        // days = Thursday 2026-08-20, so a deposit on Friday 2026-08-21 is outside the window.
        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 21), net: 970m, batchNumber: "452");
        db.ImportedNormalizedRecords.AddRange(pos, bank);
        await db.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(0, result.Tier1Matched);
        Assert.Equal(0, result.Tier2Matched);
        Assert.Equal(0, result.Tier3Matched);
        Assert.Equal(1, result.NoMatchCount); // POS side falls through to MatchNotFound
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_across_repeated_runs()
    {
        using var db = CreateTenantDb();
        SeedChartOfAccounts(db);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var posBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "POS_SETTLEMENT", Status = "COMMITTED" };
        var bankBatch = new ImportBatch { ImportBatchId = Guid.NewGuid(), SourceType = "BANK", Status = "COMMITTED" };
        db.ImportBatches.AddRange(posBatch, bankBatch);

        var pos = MakePos(posBatch.ImportBatchId, new DateTime(2026, 8, 17), gross: 1000m, fee: 30m, batchNumber: "452");
        var bank = MakeBank(bankBatch.ImportBatchId, new DateTime(2026, 8, 19), net: 970m, batchNumber: "452");
        db.ImportedNormalizedRecords.AddRange(pos, bank);
        await db.SaveChangesAsync();

        var firstRun = await worker.ExecuteAsync(tenantId, db);
        var secondRun = await worker.ExecuteAsync(tenantId, db);

        Assert.Equal(1, firstRun.Tier1Matched);
        Assert.Equal(0, secondRun.TotalCandidates); // already-matched records are no longer PENDING
        Assert.Equal(1, await db.ReconciliationMatchGroups.CountAsync()); // no duplicate group
        Assert.Equal(1, await db.JournalVouchers.CountAsync()); // no duplicate posting
    }
}
