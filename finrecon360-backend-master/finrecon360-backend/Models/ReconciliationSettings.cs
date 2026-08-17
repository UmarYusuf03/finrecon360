namespace finrecon360_backend.Models
{
    /// <summary>
    /// Single-row per-tenant configuration for the reconciliation matching workers.
    /// Replaces the private `const decimal AmountTolerance = 0.01m` (and one-off
    /// `DateToleranceDays`) that used to be duplicated independently across seven worker
    /// files, so every Level1-6 worker reads the same tenant-adjustable values.
    /// </summary>
    public class ReconciliationSettings
    {
        public Guid ReconciliationSettingsId { get; set; }

        // Absolute currency-unit tolerance used when comparing two amounts for a match.
        public decimal AmountTolerance { get; set; } = 0.01m;

        // +/- day window used by workers that fall back to date-window matching
        // (e.g. Collection Match) when an exact-date/reference match isn't found.
        public int DateToleranceDays { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
