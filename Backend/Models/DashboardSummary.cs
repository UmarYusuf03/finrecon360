namespace finrecon360_backend.Models
{
    public class DashboardSummary
    {
        public int TotalAccounts { get; set; }
        public int PendingReconciliations { get; set; }
        public int CompletedToday { get; set; }
        public int Alerts { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }
}
