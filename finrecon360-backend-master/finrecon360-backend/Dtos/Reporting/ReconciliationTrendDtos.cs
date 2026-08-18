namespace finrecon360_backend.Dtos.Reporting
{
    public record ReconciliationTrendDayDto(
        DateTime SnapshotDate,
        string MatchLevel,
        int MatchedCount,
        int ConfirmedCount,
        int ExceptionCount,
        int UnmatchedCount,
        decimal? AverageTimeToMatchHours);

    public record ReconciliationTrendResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        IReadOnlyList<ReconciliationTrendDayDto> Days);
}
