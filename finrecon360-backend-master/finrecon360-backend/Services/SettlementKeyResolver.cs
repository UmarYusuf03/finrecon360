using finrecon360_backend.Models;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Single source of truth for turning an <see cref="ImportedNormalizedRecord"/> into the
    /// settlement key used to line up records across sources (GATEWAY/BANK/POS/ERP/etc.).
    /// Shared by the reconciliation workers so every matching rule resolves the same key
    /// the same way.
    /// </summary>
    public static class SettlementKeyResolver
    {
        /// <summary>
        /// Prefers the explicit <see cref="ImportedNormalizedRecord.SettlementId"/> supplied by the
        /// source system. Falls back to an AccountCode|ReferenceNumber composite, then to whichever
        /// of AccountCode/ReferenceNumber is present alone. Returns null when none are available.
        /// </summary>
        public static string? Resolve(ImportedNormalizedRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.SettlementId))
            {
                return record.SettlementId.Trim();
            }

            var hasAccountCode = !string.IsNullOrWhiteSpace(record.AccountCode);
            var hasReferenceNumber = !string.IsNullOrWhiteSpace(record.ReferenceNumber);

            if (hasAccountCode && hasReferenceNumber)
            {
                return $"{record.AccountCode!.Trim()}|{record.ReferenceNumber!.Trim()}";
            }

            if (hasAccountCode)
            {
                return record.AccountCode!.Trim();
            }

            if (hasReferenceNumber)
            {
                return record.ReferenceNumber!.Trim();
            }

            return null;
        }
    }
}
