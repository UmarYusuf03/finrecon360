using System.Text.RegularExpressions;

namespace finrecon360_backend.Services.Reconciliation
{
    /// <summary>
    /// Pulls structured identifiers (Batch Number, Terminal ID, Merchant ID) out of unstructured
    /// bank-statement narrative text — e.g. "POS SETTLEMENT - TID88552 - BATCH 000452",
    /// "MERCHANT EOD 987654321001 BATCH:000452", "COMBANK POS CR TID88552 BATCH000452" — using
    /// per-ImportMappingTemplate regex patterns, since different banks/acquirers format the same
    /// information differently and no single hardcoded pattern covers them all.
    ///
    /// Called once, at import-commit time (see ImportsController.Commit), not on every
    /// reconciliation cycle — extracted values are persisted on ImportedNormalizedRecord so a
    /// bad parse can be corrected by an admin rather than silently failing every cycle.
    /// </summary>
    public static class PosIdentifierExtractor
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        public readonly record struct Result(string? BatchNumber, string? TerminalId, string? MerchantId);

        public static Result Extract(string? narrative, Dictionary<string, string>? patterns)
        {
            if (string.IsNullOrWhiteSpace(narrative) || patterns is null || patterns.Count == 0)
            {
                return new Result(null, null, null);
            }

            return new Result(
                ExtractField(narrative, patterns, "BatchNumber", stripLeadingZeros: true),
                ExtractField(narrative, patterns, "TerminalId", stripLeadingZeros: false),
                ExtractField(narrative, patterns, "MerchantId", stripLeadingZeros: false));
        }

        private static string? ExtractField(
            string narrative,
            Dictionary<string, string> patterns,
            string field,
            bool stripLeadingZeros)
        {
            if (!patterns.TryGetValue(field, out var pattern) || string.IsNullOrWhiteSpace(pattern))
            {
                return null;
            }

            Match match;
            try
            {
                // Timeout guards against a malformed, catastrophically-backtracking admin-supplied
                // pattern hanging the import-commit request.
                match = Regex.Match(narrative, pattern, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return null; // malformed regex syntax in the mapping template
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }

            if (!match.Success)
            {
                return null;
            }

            // Prefer the first capture group; fall back to the whole match if the pattern has none.
            var raw = match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : match.Value;
            return Normalize(raw, stripLeadingZeros);
        }

        private static string? Normalize(string value, bool stripLeadingZeros)
        {
            var trimmed = value.Trim().ToUpperInvariant();
            if (trimmed.Length == 0)
            {
                return null;
            }

            if (stripLeadingZeros)
            {
                var stripped = trimmed.TrimStart('0');
                // "000452" -> "452", but don't reduce an all-zero value like "000" to empty.
                trimmed = stripped.Length == 0 ? "0" : stripped;
            }

            return trimmed;
        }
    }
}
