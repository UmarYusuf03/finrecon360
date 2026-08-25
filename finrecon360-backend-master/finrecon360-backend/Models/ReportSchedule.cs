namespace finrecon360_backend.Models
{
    /// <summary>
    /// A tenant admin's standing request to email a report on a weekly cadence (e.g. "email me
    /// the trial balance every Monday"). Checked and executed by ReportScheduleHostedService.
    /// </summary>
    public class ReportSchedule
    {
        public Guid ReportScheduleId { get; set; }

        // TrialBalance | IncomeStatement | BalanceSheet | CashFlow | ReconciliationTrend
        public string ReportType { get; set; } = string.Empty;

        // csv | xlsx
        public string Format { get; set; } = "csv";

        // 0 (Sunday) .. 6 (Saturday) — System.DayOfWeek's underlying values.
        public int DayOfWeek { get; set; }

        public string RecipientEmail { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastRunAt { get; set; }
        public DateTime NextRunAt { get; set; }
    }
}
