namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Common result summary returned by every matching worker.
    /// Used for logging, API responses, and dashboard counters.
    /// </summary>
    public record MatchingRunResult(
        string MatchLevel,
        int TotalCandidates,
        int AutoMatchedCount,
        int ExceptionCount,
        int NoMatchCount)
    {
        public static MatchingRunResult Empty(string level) =>
            new(level, 0, 0, 0, 0);
    }
}
