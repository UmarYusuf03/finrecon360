namespace ScenarioSeeder;

/// <summary>A staff-entered Transaction to be created via POST /api/admin/transactions and then
/// approved. Level1 and Level4 match against these — they are never CSV-imported.</summary>
public sealed record TransactionSeed(
    string Label,
    decimal Amount,
    DateOnly Date,
    string Description,
    string? ReferenceNumber,
    Guid? BankAccountId,
    string TransactionType,   // "CashIn" | "CashOut"
    string PaymentMethod);    // "Cash" | "Card"

/// <summary>
/// Accumulates every row the scenario catalog generates, plus the shared date cursors that keep
/// every generic scenario's date unique (so Level4's amount+date GATEWAY lookup never
/// accidentally collides two unrelated scenarios) and keep Level7's dates far enough from
/// "today" that AddBusinessDays forward offsets never land in the future.
/// </summary>
public sealed class ScenarioContext
{
    public DateOnly AnchorEnd { get; }
    public HashSet<DateOnly> Holidays { get; } = new();
    public Guid PrimaryBankAccountId { get; }
    public Guid SecondaryBankAccountId { get; }

    public List<TransactionSeed> Transactions { get; } = new();
    public List<ImportRow> Pos { get; } = new();
    public List<ImportRow> Erp { get; } = new();
    public List<ImportRow> Gateway { get; } = new();
    public List<ImportRow> Bank { get; } = new();
    public List<ImportRow> BankSecondary { get; } = new();
    public List<ImportRow> PosSettlement { get; } = new();

    // ~50 generic scenario dates need to fit in this budget with zero collisions.
    private int _genericCursor = 80;
    private const int GenericStep = 1;

    // 18 Level7 dates, but each one grows forward by up to ~8 calendar days
    // (window + 1 extra business day), so it needs real headroom before AnchorEnd.
    private int _l7Cursor = 90;
    private const int L7Step = 4;
    private const int L7MinCursor = 15;

    public ScenarioContext(DateOnly anchorEnd, Guid primaryBankAccountId, Guid secondaryBankAccountId)
    {
        AnchorEnd = anchorEnd;
        PrimaryBankAccountId = primaryBankAccountId;
        SecondaryBankAccountId = secondaryBankAccountId;
    }

    public DateOnly ClaimGenericDate()
    {
        var date = AnchorEnd.AddDays(-_genericCursor);
        _genericCursor -= GenericStep;
        return date;
    }

    public DateOnly ClaimL7BaseDate()
    {
        var raw = AnchorEnd.AddDays(-_l7Cursor);
        var date = BusinessDayMath.NextBusinessDay(raw, Holidays);
        _l7Cursor = Math.Max(L7MinCursor, _l7Cursor - L7Step);
        return date;
    }
}
