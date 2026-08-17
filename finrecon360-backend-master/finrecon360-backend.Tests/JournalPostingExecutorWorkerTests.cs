using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace finrecon360_backend.Tests;

public class JournalPostingExecutorWorkerTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-JournalPosting-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static JournalPostingExecutorWorker CreateWorker() =>
        new(NullLogger<JournalPostingExecutorWorker>.Instance);

    private static void SeedChartOfAccounts(TenantDbContext tenantDb)
    {
        tenantDb.ChartOfAccounts.AddRange(
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "1000-BANK", Name = "Bank / Cash Received", AccountType = AccountType.Asset },
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "2000-CASHOUT", Name = "Cash-Out Clearing", AccountType = AccountType.Liability },
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "5000-FEE", Name = "Processing Fee Expense", AccountType = AccountType.Expense },
            new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "4000-FEEOFFSET", Name = "Fee Offset Revenue", AccountType = AccountType.Revenue });
    }

    [Fact]
    public async Task ExecuteAsync_posts_balanced_voucher_for_direct_cash_cashout()
    {
        using var tenantDb = CreateTenantDb();
        SeedChartOfAccounts(tenantDb);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        // Cash cashouts have no Level4 gate — they post directly on approval.
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 500m,
            TransactionDate = DateTime.UtcNow.Date,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Cash,
            Description = "Direct cash cashout",
            CreatedAt = DateTime.UtcNow,
        };
        tenantDb.Transactions.Add(txn);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.PostedCount);
        Assert.Equal(0, result.FailedCount);

        var voucher = await tenantDb.JournalVouchers
            .Include(v => v.Entries)
            .FirstOrDefaultAsync(v => v.TransactionId == txn.TransactionId);

        Assert.NotNull(voucher);
        Assert.Equal(2, voucher!.Entries.Count);
        Assert.Equal(0m, voucher.Entries.Sum(e => e.Amount)); // must balance to zero

        var debitBank = voucher.Entries.Single(e => e.EntryType == "DebitBank");
        var creditCashOut = voucher.Entries.Single(e => e.EntryType == "CreditCashOut");
        Assert.Equal(500m, debitBank.Amount);
        Assert.Equal(-500m, creditCashOut.Amount);
        Assert.NotNull(debitBank.ChartOfAccountId);
        Assert.NotNull(creditCashOut.ChartOfAccountId);
        Assert.NotEqual(debitBank.ChartOfAccountId, creditCashOut.ChartOfAccountId);
    }

    [Fact]
    public async Task ExecuteAsync_posts_balanced_voucher_with_fee_entries_for_confirmed_card_cashout()
    {
        using var tenantDb = CreateTenantDb();
        SeedChartOfAccounts(tenantDb);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 1000m,
            TransactionDate = DateTime.UtcNow.Date,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashOut,
            PaymentMethod = PaymentMethod.Card,
            Description = "Card cashout with confirmed bank match",
            CreatedAt = DateTime.UtcNow,
        };
        tenantDb.Transactions.Add(txn);

        var matchMetadata = JsonSerializer.Serialize(new
        {
            transactionId = txn.TransactionId,
            bankNetTotal = 980m,
            gatewayNetAmount = 1000m,
            processingFeeAdjustment = 20m,
        });

        var matchGroup = new ReconciliationMatchGroup
        {
            ReconciliationMatchGroupId = Guid.NewGuid(),
            MatchLevel = "Level4",
            SettlementKey = "ACCT001|REF001",
            IsConfirmed = true,
            ConfirmedAt = DateTime.UtcNow,
            MatchedAmount = 980m,
            MatchMetadataJson = matchMetadata,
        };
        tenantDb.ReconciliationMatchGroups.Add(matchGroup);
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(1, result.PostedCount);

        var voucher = await tenantDb.JournalVouchers
            .Include(v => v.Entries)
            .FirstOrDefaultAsync(v => v.TransactionId == txn.TransactionId);

        Assert.NotNull(voucher);
        Assert.Equal(4, voucher!.Entries.Count); // bank/cashout pair + fee/offset pair
        Assert.Equal(0m, voucher.Entries.Sum(e => e.Amount)); // must balance to zero

        Assert.Equal(980m, voucher.Entries.Single(e => e.EntryType == "DebitBank").Amount);
        Assert.Equal(20m, voucher.Entries.Single(e => e.EntryType == "DebitFeeExpense").Amount);
        Assert.All(voucher.Entries, e => Assert.NotNull(e.ChartOfAccountId));
    }

    [Fact]
    public async Task ExecuteAsync_does_not_double_post_a_transaction_that_already_has_entries()
    {
        using var tenantDb = CreateTenantDb();
        SeedChartOfAccounts(tenantDb);
        var worker = CreateWorker();
        var tenantId = Guid.NewGuid();

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            Amount = 250m,
            TransactionDate = DateTime.UtcNow.Date,
            TransactionState = TransactionState.JournalReady,
            TransactionType = TransactionType.CashIn,
            PaymentMethod = PaymentMethod.Cash,
            Description = "Already posted",
            CreatedAt = DateTime.UtcNow,
        };
        tenantDb.Transactions.Add(txn);
        tenantDb.JournalEntries.Add(new JournalEntry
        {
            JournalEntryId = Guid.NewGuid(),
            TransactionId = txn.TransactionId,
            EntryType = "CashIn",
            Amount = 250m,
            Currency = "LKR",
            PostedAt = DateTime.UtcNow,
        });
        await tenantDb.SaveChangesAsync();

        var result = await worker.ExecuteAsync(tenantId, tenantDb);

        Assert.Equal(0, result.PostedCount);
        var entryCount = await tenantDb.JournalEntries.CountAsync(e => e.TransactionId == txn.TransactionId);
        Assert.Equal(1, entryCount); // still just the pre-existing one — no duplicate posting
    }
}
