using System;

namespace finrecon360_backend.Models
{
    public class ReportSnapshot
    {
        public Guid ReportSnapshotId { get; set; }
        public DateTime SnapshotDate { get; set; }
        
        public int TotalUnmatchedCardCashouts { get; set; }
        public int PendingExceptions { get; set; }
        public int TotalJournalReady { get; set; }
        public decimal ReconciliationCompletionPercentage { get; set; }
        
        public int TotalMatchGroupsConfirmed { get; set; }
        public decimal TotalFeeAdjustments { get; set; }
        
        public DateTime CreatedAt { get; set; }
    }
}
