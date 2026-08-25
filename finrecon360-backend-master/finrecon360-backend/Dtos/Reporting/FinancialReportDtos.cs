namespace finrecon360_backend.Dtos.Reporting
{
    // "UNCLASSIFIED" is a synthetic pseudo-account (ChartOfAccountId == null, AccountType == null)
    // used throughout these reports for JournalEntry rows posted without a chart-of-account
    // mapping (currently: the two manual posting endpoints on ReconciliationController). Rather
    // than silently dropping that ledger activity from the reports, it is surfaced as its own
    // line/bucket so a reader can see the gap instead of the numbers quietly not tying out.
    public static class UnclassifiedAccount
    {
        public const string Code = "UNCLASSIFIED";
        public const string Name = "Unclassified (no chart of account mapping)";
    }

    // Synthetic equity line on the Balance Sheet representing cumulative Revenue minus Expense
    // account activity as of the report date — the standard retained-earnings roll-up needed for
    // Assets == Liabilities + Equity to hold. Not backed by a real ChartOfAccount row.
    public static class RetainedEarningsAccount
    {
        public const string Code = "RETAINED-EARNINGS";
        public const string Name = "Retained Earnings (Net Income)";
    }

    public record GeneralLedgerEntryDto(
        DateTime PostedAt,
        Guid JournalEntryId,
        Guid? JournalVoucherId,
        string EntryType,
        string? Notes,
        decimal Amount,
        decimal RunningBalance);

    public record GeneralLedgerAccountDto(
        Guid? ChartOfAccountId,
        string AccountCode,
        string AccountName,
        string? AccountType,
        decimal OpeningBalance,
        decimal ClosingBalance,
        IReadOnlyList<GeneralLedgerEntryDto> Entries);

    public record GeneralLedgerResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        IReadOnlyList<GeneralLedgerAccountDto> Accounts);

    public record TrialBalanceLineDto(
        Guid? ChartOfAccountId,
        string AccountCode,
        string AccountName,
        string? AccountType,
        decimal Debit,
        decimal Credit);

    public record TrialBalanceResponse(
        DateTime AsOfUtc,
        IReadOnlyList<TrialBalanceLineDto> Lines,
        decimal TotalDebit,
        decimal TotalCredit,
        bool IsBalanced);

    public record IncomeStatementLineDto(string AccountCode, string AccountName, decimal Amount);

    public record IncomeStatementResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        IReadOnlyList<IncomeStatementLineDto> RevenueLines,
        IReadOnlyList<IncomeStatementLineDto> ExpenseLines,
        decimal TotalRevenue,
        decimal TotalExpense,
        decimal NetIncome,
        decimal UnclassifiedAmount);

    public record BalanceSheetLineDto(string AccountCode, string AccountName, decimal Amount);

    public record BalanceSheetResponse(
        DateTime AsOfUtc,
        IReadOnlyList<BalanceSheetLineDto> AssetLines,
        IReadOnlyList<BalanceSheetLineDto> LiabilityLines,
        IReadOnlyList<BalanceSheetLineDto> EquityLines,
        decimal TotalAssets,
        decimal TotalLiabilities,
        decimal TotalEquity,
        decimal UnclassifiedAmount);

    public record CashFlowDayDto(DateTime Date, decimal OpeningBalance, decimal CashIn, decimal CashOut, decimal ClosingBalance);

    public record CashFlowResponse(
        DateTime FromUtc, DateTime ToUtc, IReadOnlyList<CashFlowDayDto> Days,
        decimal TotalCashIn, decimal TotalCashOut, decimal NetChange, decimal UnclassifiedAmount);
}
