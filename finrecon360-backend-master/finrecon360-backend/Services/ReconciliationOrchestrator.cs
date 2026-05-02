namespace finrecon360_backend.Services
{
    public enum ReconciliationWorkflowStage
    {
        InternalOperationalMatch,
        AccountingSyncAudit,
        DigitalCollectionAudit,
        PhysicalBankReconciliation
    }

    public enum ReconciliationWorkflowEvent
    {
        OperationalMatch,
        SyncAudit,
        SalesMatch,
        SettlementMatch,
        ExpenseMatch,
        CollectionMatch
    }

    public record ReconciliationWorkflowStep(
        ReconciliationWorkflowStage Stage,
        ReconciliationWorkflowEvent Event,
        string Purpose,
        string Comparison,
        string MatchKey,
        string Outcome);

    public record ReconciliationWorkflowPlan(
        string SourceType,
        IReadOnlyList<ReconciliationWorkflowStep> Steps,
        string Summary);

    public interface IReconciliationOrchestrator
    {
        bool TryBuildPlan(string? sourceType, out ReconciliationWorkflowPlan plan);
        string DescribeRouting(string? sourceType);
    }

    public class ReconciliationOrchestrator : IReconciliationOrchestrator
    {
        public bool TryBuildPlan(string? sourceType, out ReconciliationWorkflowPlan plan)
        {
            var normalizedSourceType = NormalizeSourceType(sourceType);
            switch (normalizedSourceType)
            {
                case "POS":
                    plan = BuildPlan(
                        normalizedSourceType,
                        [
                            CreateStep(
                                ReconciliationWorkflowStage.InternalOperationalMatch,
                                ReconciliationWorkflowEvent.OperationalMatch,
                                "Verify staff entries against POS EOD",
                                "Staff Manual Input ↔ POS End-of-Day (EOD)",
                                "Time + Amount + Ref",
                                "INTERNAL_VERIFIED")
                        ],
                        "POS -> Stage 1 (Operational Match)");
                    return true;

                case "ERP":
                    plan = BuildPlan(
                        normalizedSourceType,
                        [
                            CreateStep(
                                ReconciliationWorkflowStage.AccountingSyncAudit,
                                ReconciliationWorkflowEvent.SyncAudit,
                                "Ensure POS made it into the books",
                                "POS EOD ↔ ERP Sales Ledger",
                                "ReferenceNo / Order ID",
                                "Accounting sync confirmed"),
                            CreateStep(
                                ReconciliationWorkflowStage.DigitalCollectionAudit,
                                ReconciliationWorkflowEvent.SalesMatch,
                                "Prove sale was charged",
                                "ERP Sales Ledger ↔ Payment Gateway File",
                                "ReferenceNo / Order ID",
                                "SALES_VERIFIED or EXCEPTION")
                        ],
                        "ERP -> Stages 2 and 3 (Sync Audit, Sales Match)");
                    return true;

                case "GATEWAY":
                    plan = BuildPlan(
                        normalizedSourceType,
                        [
                            CreateStep(
                                ReconciliationWorkflowStage.DigitalCollectionAudit,
                                ReconciliationWorkflowEvent.SalesMatch,
                                "Prove sale was charged",
                                "ERP Sales Ledger ↔ Payment Gateway File",
                                "ReferenceNo / Order ID",
                                "SALES_VERIFIED or EXCEPTION"),
                            CreateStep(
                                ReconciliationWorkflowStage.PhysicalBankReconciliation,
                                ReconciliationWorkflowEvent.SettlementMatch,
                                "Confirm online payout landed in bank",
                                "Gateway Payout Totals ↔ Bank Statement",
                                "SettlementID",
                                "MATCHED after human confirmation")
                        ],
                        "Gateway -> Stages 3 and 4 (Sales Match, Settlement Match)");
                    return true;

                case "BANK":
                    plan = BuildPlan(
                        normalizedSourceType,
                        [
                            CreateStep(
                                ReconciliationWorkflowStage.PhysicalBankReconciliation,
                                ReconciliationWorkflowEvent.SettlementMatch,
                                "Confirm online payout landed in bank",
                                "Gateway Payout Totals ↔ Bank Statement",
                                "SettlementID",
                                "MATCHED after human confirmation"),
                            CreateStep(
                                ReconciliationWorkflowStage.PhysicalBankReconciliation,
                                ReconciliationWorkflowEvent.ExpenseMatch,
                                "Gate approved card cashout before journal posting",
                                "Approved Card Cashout ↔ Bank Statement",
                                "Auth Code / Ref",
                                "Must match before posting"),
                            CreateStep(
                                ReconciliationWorkflowStage.PhysicalBankReconciliation,
                                ReconciliationWorkflowEvent.CollectionMatch,
                                "Confirm physical card receipt settlement",
                                "Physical Card Receipt ↔ Bank Statement",
                                "Settlement / receipt ref",
                                "Matched before posting")
                        ],
                        "Bank -> Stage 4 (Settlement, Expense, Collection Match)");
                    return true;

                default:
                    plan = new ReconciliationWorkflowPlan(
                        normalizedSourceType,
                        Array.Empty<ReconciliationWorkflowStep>(),
                        string.IsNullOrWhiteSpace(normalizedSourceType)
                            ? "Unclassified source type"
                            : $"Unclassified source type: {normalizedSourceType}");
                    return false;
            }
        }

        public string DescribeRouting(string? sourceType)
        {
            return TryBuildPlan(sourceType, out var plan)
                ? plan.Summary
                : plan.Summary;
        }

        private static ReconciliationWorkflowPlan BuildPlan(
            string sourceType,
            IReadOnlyList<ReconciliationWorkflowStep> steps,
            string summary)
        {
            return new ReconciliationWorkflowPlan(sourceType, steps, summary);
        }

        private static ReconciliationWorkflowStep CreateStep(
            ReconciliationWorkflowStage stage,
            ReconciliationWorkflowEvent @event,
            string purpose,
            string comparison,
            string matchKey,
            string outcome)
        {
            return new ReconciliationWorkflowStep(stage, @event, purpose, comparison, matchKey, outcome);
        }

        private static string NormalizeSourceType(string? sourceType)
        {
            return string.IsNullOrWhiteSpace(sourceType)
                ? string.Empty
                : sourceType.Trim().ToUpperInvariant();
        }
    }
}