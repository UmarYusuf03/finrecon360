using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace finrecon360_backend.Tests;

public class FinancialReportingServicesTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-FinancialReports-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static ChartOfAccount Account(string code, string name, AccountType type) => new()
    {
        ChartOfAccountId = Guid.NewGuid(),
        Code = code,
        Name = name,
        AccountType = type,
    };

    private static JournalEntry Entry(Guid? accountId, decimal amount, DateTime postedAt, string entryType = "Test", string? notes = null) => new()
    {
        JournalEntryId = Guid.NewGuid(),
        ChartOfAccountId = accountId,
        Amount = amount,
        EntryType = entryType,
        Notes = notes,
        PostedAt = postedAt,
    };

    [Fact]
    public async Task GeneralLedger_computes_opening_balance_and_running_balance()
    {
        using var db = CreateTenantDb();
        var bank = Account("1000-BANK", "Bank", AccountType.Asset);
        db.ChartOfAccounts.Add(bank);

        var day0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Before the report window: contributes to opening balance only.
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 100m, day0.AddDays(-5)));
        // Inside the window: two entries, running balance should accumulate on top of opening.
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 50m, day0.AddDays(1)));
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, -20m, day0.AddDays(2)));
        await db.SaveChangesAsync();

        var service = new GeneralLedgerService();
        var result = await service.GetAsync(db, day0, day0.AddDays(10));

        var account = Assert.Single(result.Accounts);
        Assert.Equal("1000-BANK", account.AccountCode);
        Assert.Equal(100m, account.OpeningBalance);
        Assert.Equal(2, account.Entries.Count);
        Assert.Equal(150m, account.Entries[0].RunningBalance);
        Assert.Equal(130m, account.Entries[1].RunningBalance);
        Assert.Equal(130m, account.ClosingBalance);
    }

    [Fact]
    public async Task GeneralLedger_does_not_throw_when_unclassified_entries_exist_before_the_window()
    {
        // Regression test: Dictionary<Guid?, T> throws ArgumentNullException on a null key at
        // runtime. Opening-balance computation must not build such a dictionary, or this — the
        // exact "unclassified activity before the report window" case — throws a 500.
        using var db = CreateTenantDb();
        var day0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.JournalEntries.Add(Entry(null, 75m, day0.AddDays(-3)));
        db.JournalEntries.Add(Entry(null, -25m, day0.AddDays(1)));
        await db.SaveChangesAsync();

        var service = new GeneralLedgerService();
        var result = await service.GetAsync(db, day0, day0.AddDays(10));

        var account = Assert.Single(result.Accounts);
        Assert.Equal("UNCLASSIFIED", account.AccountCode);
        Assert.Null(account.AccountType);
        Assert.Equal(75m, account.OpeningBalance);
        Assert.Equal(50m, account.ClosingBalance);
    }

    [Fact]
    public async Task TrialBalance_splits_net_amount_into_debit_or_credit_and_balances()
    {
        using var db = CreateTenantDb();
        var bank = Account("1000-BANK", "Bank", AccountType.Asset);
        var cashOut = Account("2000-CASHOUT", "Cash-Out Clearing", AccountType.Liability);
        db.ChartOfAccounts.AddRange(bank, cashOut);

        var asOf = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 300m, asOf.AddDays(-1)));
        db.JournalEntries.Add(Entry(cashOut.ChartOfAccountId, -300m, asOf.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = new TrialBalanceService();
        var result = await service.GetAsync(db, asOf);

        var bankLine = Assert.Single(result.Lines, l => l.AccountCode == "1000-BANK");
        Assert.Equal(300m, bankLine.Debit);
        Assert.Equal(0m, bankLine.Credit);

        var cashOutLine = Assert.Single(result.Lines, l => l.AccountCode == "2000-CASHOUT");
        Assert.Equal(0m, cashOutLine.Debit);
        Assert.Equal(300m, cashOutLine.Credit);

        Assert.Equal(300m, result.TotalDebit);
        Assert.Equal(300m, result.TotalCredit);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task TrialBalance_flags_unbalanced_data_as_a_data_integrity_signal()
    {
        // Not reachable through the normal posting path (vouchers are required to sum to zero
        // before they're allowed to post) but the report should surface it plainly if it ever
        // happens rather than silently reporting IsBalanced = true.
        using var db = CreateTenantDb();
        var bank = Account("1000-BANK", "Bank", AccountType.Asset);
        db.ChartOfAccounts.Add(bank);
        var asOf = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 100m, asOf.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = new TrialBalanceService();
        var result = await service.GetAsync(db, asOf);

        Assert.False(result.IsBalanced);
        Assert.Equal(100m, result.TotalDebit);
        Assert.Equal(0m, result.TotalCredit);
    }

    [Fact]
    public async Task IncomeStatement_negates_revenue_and_keeps_expense_as_is()
    {
        using var db = CreateTenantDb();
        var revenue = Account("4000-FEEOFFSET", "Fee Offset Revenue", AccountType.Revenue);
        var expense = Account("5000-FEE", "Processing Fee Expense", AccountType.Expense);
        var bank = Account("1000-BANK", "Bank", AccountType.Asset);
        db.ChartOfAccounts.AddRange(revenue, expense, bank);

        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);
        // Revenue accounts are credit-normal (negative under this ledger's sign convention).
        db.JournalEntries.Add(Entry(revenue.ChartOfAccountId, -40m, from.AddDays(1)));
        db.JournalEntries.Add(Entry(expense.ChartOfAccountId, 40m, from.AddDays(1)));
        // Out of range: must not affect totals.
        db.JournalEntries.Add(Entry(revenue.ChartOfAccountId, -1000m, from.AddDays(-5)));
        // Asset activity: must be excluded from an income statement entirely.
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 40m, from.AddDays(1)));
        // Unclassified: must be surfaced separately, not silently dropped or misattributed.
        db.JournalEntries.Add(Entry(null, 15m, from.AddDays(1)));
        await db.SaveChangesAsync();

        var service = new IncomeStatementService();
        var result = await service.GetAsync(db, from, to);

        var revenueLine = Assert.Single(result.RevenueLines);
        Assert.Equal("4000-FEEOFFSET", revenueLine.AccountCode);
        Assert.Equal(40m, revenueLine.Amount);

        var expenseLine = Assert.Single(result.ExpenseLines);
        Assert.Equal(40m, expenseLine.Amount);

        Assert.Equal(40m, result.TotalRevenue);
        Assert.Equal(40m, result.TotalExpense);
        Assert.Equal(0m, result.NetIncome);
        Assert.Equal(15m, result.UnclassifiedAmount);
    }

    [Fact]
    public async Task BalanceSheet_negates_liability_and_equity_but_not_asset()
    {
        using var db = CreateTenantDb();
        var bank = Account("1000-BANK", "Bank", AccountType.Asset);
        var cashOut = Account("2000-CASHOUT", "Cash-Out Clearing", AccountType.Liability);
        var equity = Account("9000-EQUITY", "Owner Equity", AccountType.Equity);
        db.ChartOfAccounts.AddRange(bank, cashOut, equity);

        var asOf = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        db.JournalEntries.Add(Entry(bank.ChartOfAccountId, 500m, asOf.AddDays(-1)));
        db.JournalEntries.Add(Entry(cashOut.ChartOfAccountId, -300m, asOf.AddDays(-1)));
        db.JournalEntries.Add(Entry(equity.ChartOfAccountId, -200m, asOf.AddDays(-1)));
        db.JournalEntries.Add(Entry(null, 10m, asOf.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = new BalanceSheetService();
        var result = await service.GetAsync(db, asOf);

        Assert.Equal(500m, Assert.Single(result.AssetLines).Amount);
        Assert.Equal(300m, Assert.Single(result.LiabilityLines).Amount);
        Assert.Equal(200m, Assert.Single(result.EquityLines).Amount);
        Assert.Equal(500m, result.TotalAssets);
        Assert.Equal(300m, result.TotalLiabilities);
        Assert.Equal(200m, result.TotalEquity);
        Assert.Equal(10m, result.UnclassifiedAmount);
    }
}
