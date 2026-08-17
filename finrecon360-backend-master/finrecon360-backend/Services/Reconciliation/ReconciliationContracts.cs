using System.Text.Json;
using System.Text.Json.Serialization;
using finrecon360_backend.Models;

namespace finrecon360_backend.Services.Reconciliation
{
    /// <summary>
    /// WHY: The settlement key is the join between a gateway payout and a bank deposit line.
    /// It previously had three different definitions — the documented SettlementID, the import
    /// pipeline's "AccountCode ?? ReferenceNumber", and the worker's "AccountCode|ReferenceNumber" —
    /// so the same data matched differently depending on which code path reached it first.
    ///
    /// This is now the single definition. Resolution order is deliberate:
    ///   1. SettlementId — the documented key, once the source file provides it.
    ///   2. AccountCode|ReferenceNumber — the composite fallback, strongest available signal.
    ///   3. Either half alone — weakest, but better than dropping the record entirely.
    /// Keys are upper-cased so matching is case-insensitive without relying on comparer choice
    /// at every call site.
    /// </summary>
    public static class SettlementKeyResolver
    {
        public static string? Resolve(ImportedNormalizedRecord record)
        {
            if (record is null)
            {
                return null;
            }

            var settlementId = Clean(record.SettlementId);
            if (settlementId is not null)
            {
                return settlementId;
            }

            var accountCode = Clean(record.AccountCode);
            var referenceNumber = Clean(record.ReferenceNumber);

            if (accountCode is not null && referenceNumber is not null)
            {
                return $"{accountCode}|{referenceNumber}";
            }

            return referenceNumber ?? accountCode;
        }

        /// <summary>
        /// True when the record carries the documented SettlementId. Records without one can still
        /// be matched on the fallback key, but they belong in the waiting queue so staff can attach
        /// the real ID when a later gateway import supplies it.
        /// </summary>
        public static bool HasSettlementId(ImportedNormalizedRecord record) =>
            Clean(record?.SettlementId) is not null;

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// WHY: Match-group metadata used to be an anonymous object at each write site and a
    /// Dictionary&lt;string, object&gt; lookup at the read site. The two drifted — the import path wrote
    /// "gatewayNetTotal"/"bankTotal"/"feeTotal" while the posting worker read
    /// "bankNetTotal"/"processingFeeAdjustment" — so groups created on import were invisible to the
    /// poster, and would have posted zero-value entries if found.
    ///
    /// Any match group that can reach journal posting must be serialized through this type.
    /// Descriptive-only levels (1 and 3) may still carry their own shape; they are never posted.
    /// </summary>
    public sealed record MatchGroupMetadata
    {
        /// <summary>Set when the group settles a specific transaction — the poster looks it up by this.</summary>
        public Guid? TransactionId { get; init; }

        public string? SettlementKey { get; init; }

        /// <summary>Gateway side of the comparison, net of processing fees.</summary>
        public decimal GatewayNetTotal { get; init; }

        /// <summary>Bank side of the comparison — what actually landed in the account.</summary>
        public decimal BankNetTotal { get; init; }

        /// <summary>Processing fees to be journalled separately so revenue (gross) and cash (net) reconcile.</summary>
        public decimal ProcessingFeeTotal { get; init; }

        public decimal Variance { get; init; }

        /// <summary>
        /// True when a worker proposed this match rather than a human. Auto-matched groups are still
        /// unconfirmed — IsConfirmed remains a human action.
        /// </summary>
        public bool AutoMatched { get; init; }

        public string? GatewayBankSession { get; init; }

        public int GatewayRecordCount { get; init; }

        public int BankRecordCount { get; init; }

        public DateTime MatchedAt { get; init; } = DateTime.UtcNow;

        // ── Level7 (PosSettlementMatchWorker) fields ──────────────────────────────────
        // Kept distinct from the gateway-named fields above rather than reused, so a reader
        // of a Level7 group's metadata isn't left wondering why "GatewayNetTotal" appears on a
        // POS↔BANK match.

        /// <summary>Gross POS sales total for the matched group (before MDR fee deduction).</summary>
        public decimal PosGrossTotal { get; init; }

        /// <summary>Net POS sales total for the matched group (Gross - MDR fee) — this is what
        /// was actually compared against the bank side, per the "match Net, not Gross" rule.</summary>
        public decimal PosNetTotal { get; init; }

        /// <summary>MDR (merchant discount rate) fee total for the matched group.</summary>
        public decimal PosFeeTotal { get; init; }

        /// <summary>Which waterfall tier produced this match — the audit-trail "rule triggered".</summary>
        public string? RuleTriggered { get; init; }

        /// <summary>The exact normalized identifiers the match was keyed on, for audit purposes.</summary>
        public List<string>? MatchedBatchNumbers { get; init; }
        public List<string>? MatchedTerminalIds { get; init; }
        public List<string>? MatchedMerchantIds { get; init; }

        /// <summary>POS-side record count for the group (named distinctly from GatewayRecordCount
        /// since Level7 groups aren't gateway-sourced).</summary>
        public int PosRecordCount { get; init; }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

        /// <summary>
        /// Returns null for absent or malformed metadata rather than throwing — a corrupt group
        /// should surface as an unpostable exception, not take down the whole posting cycle.
        /// </summary>
        public static MatchGroupMetadata? TryParse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<MatchGroupMetadata>(json, SerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Canonical MatchStatus values for ImportedNormalizedRecord. These were documented on the model
    /// but only two of the six were ever written, which left every queue and filter downstream either
    /// empty or indistinguishable from untouched data.
    /// </summary>
    public static class MatchStatuses
    {
        public const string Pending = "PENDING";
        public const string InternalVerified = "INTERNAL_VERIFIED";
        public const string SalesVerified = "SALES_VERIFIED";
        public const string Exception = "EXCEPTION";
        public const string Waiting = "WAITING";
        public const string Matched = "MATCHED";
    }
}
