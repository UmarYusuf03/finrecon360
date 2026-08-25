using System.Text;
using finrecon360_backend.Data;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Export;
using finrecon360_backend.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace finrecon360_backend.Tests;

public class ScheduledReportRendererTests
{
    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-ScheduledReportRenderer-{Guid.NewGuid()}")
            .Options;
        return new TenantDbContext(options);
    }

    private static ScheduledReportRenderer CreateRenderer() =>
        new(new TrialBalanceService(), new IncomeStatementService(), new BalanceSheetService(), new CashFlowReportService(), new ReportExporter());

    [Theory]
    [InlineData("TrialBalance", true)]
    [InlineData("IncomeStatement", true)]
    [InlineData("BalanceSheet", true)]
    [InlineData("CashFlow", true)]
    [InlineData("ReconciliationTrend", true)]
    [InlineData("SomethingElse", false)]
    public void IsKnownReportType_recognizes_the_five_supported_types_only(string reportType, bool expected)
    {
        var renderer = CreateRenderer();
        Assert.Equal(expected, renderer.IsKnownReportType(reportType));
    }

    [Theory]
    [InlineData("TrialBalance")]
    [InlineData("IncomeStatement")]
    [InlineData("BalanceSheet")]
    [InlineData("CashFlow")]
    [InlineData("ReconciliationTrend")]
    public async Task RenderAsync_produces_a_non_empty_csv_with_a_header_row_for_every_known_type(string reportType)
    {
        using var db = CreateTenantDb();
        var account = new ChartOfAccount { ChartOfAccountId = Guid.NewGuid(), Code = "1000-BANK", Name = "Bank", AccountType = AccountType.Asset };
        db.ChartOfAccounts.Add(account);
        db.JournalEntries.Add(new JournalEntry { JournalEntryId = Guid.NewGuid(), ChartOfAccountId = account.ChartOfAccountId, EntryType = "DebitBank", Amount = 100m, PostedAt = DateTime.UtcNow.AddHours(-1) });
        db.ReconciliationDailySnapshots.Add(new ReconciliationDailySnapshot { ReconciliationDailySnapshotId = Guid.NewGuid(), SnapshotDate = DateTime.UtcNow.Date, MatchLevel = "Level4", MatchedCount = 3 });
        await db.SaveChangesAsync();

        var renderer = CreateRenderer();
        var file = await renderer.RenderAsync(db, reportType, ReportExportFormat.Csv);

        Assert.Equal("text/csv", file.ContentType);
        Assert.True(file.Content.Length > 0);
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');
        var headerLine = text.Split("\r\n")[0];
        Assert.False(string.IsNullOrWhiteSpace(headerLine));
    }

    [Fact]
    public async Task RenderAsync_throws_for_an_unknown_report_type()
    {
        using var db = CreateTenantDb();
        var renderer = CreateRenderer();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => renderer.RenderAsync(db, "NotARealReport", ReportExportFormat.Csv));
    }
}
