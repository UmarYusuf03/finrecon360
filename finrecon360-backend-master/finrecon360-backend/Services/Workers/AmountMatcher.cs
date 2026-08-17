using finrecon360_backend.Models;

namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// Shared closest-amount-match selection used by the matching workers. Replaces the
    /// previous `candidates.FirstOrDefault(within tolerance)` pattern, which silently picked
    /// whichever candidate EF happened to return first when two or more records were within
    /// tolerance of the target amount — risking a wrong match instead of surfacing the
    /// ambiguity for manual review (the way BankStatementReconciliationWorker already handled
    /// ambiguous GATEWAY candidates).
    /// </summary>
    public static class AmountMatcher
    {
        public enum Outcome
        {
            Matched,
            Ambiguous,
            NoMatch,
        }

        public readonly record struct Result(Outcome Outcome, ImportedNormalizedRecord? Best, int CandidateCount);

        /// <summary>
        /// Picks the candidate whose NetAmount is closest to <paramref name="targetAmount"/>,
        /// among those within <paramref name="tolerance"/>. If two or more candidates are
        /// themselves within tolerance of each other (so the closest one can't be confidently
        /// preferred over the others), returns Ambiguous instead of guessing.
        /// </summary>
        public static Result FindBestMatch(
            IEnumerable<ImportedNormalizedRecord> candidates,
            decimal targetAmount,
            decimal tolerance)
        {
            var within = candidates
                .Select(c => (Record: c, Delta: Math.Abs(c.NetAmount - targetAmount)))
                .Where(x => x.Delta < tolerance)
                .OrderBy(x => x.Delta)
                .ToList();

            if (within.Count == 0)
            {
                return new Result(Outcome.NoMatch, null, 0);
            }

            var closest = within[0];
            var tiedWithClosest = within.Count(x => Math.Abs(x.Delta - closest.Delta) < tolerance);

            return tiedWithClosest > 1
                ? new Result(Outcome.Ambiguous, closest.Record, within.Count)
                : new Result(Outcome.Matched, closest.Record, within.Count);
        }
    }
}
