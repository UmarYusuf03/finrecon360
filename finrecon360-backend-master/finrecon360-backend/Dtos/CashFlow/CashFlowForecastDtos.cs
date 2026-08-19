namespace finrecon360_backend.Dtos.CashFlow
{
    public record CashFlowHistoryDayDto(DateTime Date, decimal NetAmount);

    public record CashFlowForecastDayDto(
        DateTime Date,
        decimal ProjectedNetFlow,
        decimal CumulativeNetFlow,
        decimal KnownPendingAmount);

    public record CashFlowForecastResponse(
        Guid? BankAccountId,
        string BankAccountName,
        DateTime GeneratedAt,
        int LookbackDays,
        decimal DailyAverageNetFlow,
        int SettlementLagDays,
        IReadOnlyList<CashFlowHistoryDayDto> History,
        IReadOnlyList<CashFlowForecastDayDto> Forecast);
}
