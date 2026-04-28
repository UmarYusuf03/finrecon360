using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Skeleton service for automated matching strategies.
    /// </summary>
    public class MatchingEngineService : IMatchingEngineService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<MatchingEngineService> _logger;
        private readonly ITenantContext _tenantContext;

        public MatchingEngineService(
            AppDbContext dbContext,
            ILogger<MatchingEngineService> logger,
            ITenantContext tenantContext)
        {
            _dbContext = dbContext;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        public async Task<MatchingSummaryResponse> RunAutomatedMatchingAsync(Guid bankStatementImportId, Guid currentUserId)
        {
            // Fetch import first to get its TenantId for System Admin fallback
            var import = await _dbContext.BankStatementImports
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == bankStatementImportId);

            if (import == null)
            {
                throw new InvalidOperationException("Bank statement import was not found.");
            }

            // Resolve tenant context; fall back to import's TenantId for System Admins
            var tenantResolution = await _tenantContext.ResolveAsync();
            var effectiveTenantId = tenantResolution?.TenantId ?? import.TenantId;

            var unresolvedLines = await _dbContext.BankStatementLines
                .IgnoreQueryFilters()
                .Where(x => x.BankStatementImportId == bankStatementImportId && !x.IsReconciled && x.TenantId == effectiveTenantId)
                .ToListAsync();

            _logger.LogInformation(
                "Starting automated matching for import {ImportId}. TenantId={TenantId}, Lines={LineCount}, UserId={UserId}",
                bankStatementImportId,
                effectiveTenantId,
                unresolvedLines.Count,
                currentUserId);

            var summary = new MatchingSummaryResponse();

            var now = DateTime.UtcNow;
            var run = new ReconciliationRun
            {
                Id = Guid.NewGuid(),
                RunDate = now,
                BankAccountId = import.BankAccountId,
                Status = ReconciliationRunStatus.InProgress,
                TotalMatchesProposed = 0,
                TenantId = effectiveTenantId,
                CreatedAt = now,
                CreatedBy = currentUserId
            };
            _dbContext.ReconciliationRuns.Add(run);

            #region Strategy 1 - General Ledger
            // 1) Fetch Side B in bulk (single query)
            var unmatchedSystemTransactions = await _dbContext.SystemTransactions
                .IgnoreQueryFilters()
                .Where(x => !x.IsReconciled && x.TenantId == effectiveTenantId)
                .ToListAsync();

            // 2) Initialize tracking lists for batched inserts
            var newMatchGroups = new List<MatchGroup>();
            var newMatchDecisions = new List<MatchDecision>();
            var matchedSystemTransactionIds = new HashSet<Guid>();

            var resolvedMatchGroupStatus = Enum.TryParse<MatchGroupStatus>("SystemMatched", true, out var systemMatchedStatus)
                ? systemMatchedStatus
                : MatchGroupStatus.Confirmed;

            var resolvedMatchDecisionStatus = Enum.TryParse<MatchDecisionStatus>("Matched", true, out var matchedDecision)
                ? matchedDecision
                : MatchDecisionStatus.Approved;

            // 3) Matching loop over unresolved bank lines (no DB roundtrips inside the loop)
            foreach (var line in unresolvedLines)
            {
                summary.TotalLinesProcessed++;

                var windowStart = line.TransactionDate.Date.AddDays(-2);
                var windowEnd = line.TransactionDate.Date.AddDays(2);

                var matchedTransaction = unmatchedSystemTransactions.FirstOrDefault(tx =>
                    !matchedSystemTransactionIds.Contains(tx.Id)
                    && tx.Amount == line.Amount
                    && tx.TransactionDate.Date >= windowStart
                    && tx.TransactionDate.Date <= windowEnd);

                if (matchedTransaction == null)
                {
                    continue;
                }

                // 4) Mark both sides reconciled
                line.IsReconciled = true;
                line.UpdatedAt = now;
                line.UpdatedBy = currentUserId;

                matchedTransaction.IsReconciled = true;
                matchedTransaction.UpdatedAt = now;
                matchedTransaction.UpdatedBy = currentUserId;
                matchedSystemTransactionIds.Add(matchedTransaction.Id);

                var matchGroupId = Guid.NewGuid();
                var matchGroup = new MatchGroup
                {
                    Id = matchGroupId,
                    ReconciliationRunId = run.Id,
                    MatchConfidenceScore = 1.0000m,
                    Status = resolvedMatchGroupStatus,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                var matchDecision = new MatchDecision
                {
                    Id = Guid.NewGuid(),
                    MatchGroupId = matchGroupId,
                    Decision = resolvedMatchDecisionStatus,
                    DecisionReason = "Auto-matched using General Ledger strategy (exact amount, +/-2 days).",
                    DecidedBy = currentUserId,
                    DecidedAt = now,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                newMatchGroups.Add(matchGroup);
                newMatchDecisions.Add(matchDecision);

                summary.MatchesFound++;
                summary.GeneralLedgerMatches++;
            }

            // 5) Persist all tracked records in batch
            #endregion

            #region Strategy 2 - Accounts Receivable
            var unmatchedInvoices = await _dbContext.Invoices
                .IgnoreQueryFilters()
                .Where(x => !x.IsReconciled && x.TenantId == effectiveTenantId && (x.Status == InvoiceStatus.Sent || x.Status == InvoiceStatus.Overdue))
                .ToListAsync();

            var matchedInvoiceIds = new HashSet<Guid>();

            foreach (var line in unresolvedLines.Where(x => !x.IsReconciled && x.Amount > 0))
            {
                var matchedInvoice = unmatchedInvoices.FirstOrDefault(invoice =>
                    !matchedInvoiceIds.Contains(invoice.Id)
                    && invoice.TotalAmount == line.Amount);

                if (matchedInvoice == null)
                {
                    continue;
                }

                line.IsReconciled = true;
                line.UpdatedAt = now;
                line.UpdatedBy = currentUserId;

                matchedInvoice.IsReconciled = true;
                matchedInvoice.Status = InvoiceStatus.Paid;
                matchedInvoice.UpdatedAt = now;
                matchedInvoice.UpdatedBy = currentUserId;
                matchedInvoiceIds.Add(matchedInvoice.Id);

                var matchGroupId = Guid.NewGuid();
                var matchGroup = new MatchGroup
                {
                    Id = matchGroupId,
                    ReconciliationRunId = run.Id,
                    MatchConfidenceScore = 1.0000m,
                    Status = resolvedMatchGroupStatus,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                var matchDecision = new MatchDecision
                {
                    Id = Guid.NewGuid(),
                    MatchGroupId = matchGroupId,
                    Decision = resolvedMatchDecisionStatus,
                    DecisionReason = "Auto-matched AR Invoice by exact amount",
                    DecidedBy = currentUserId,
                    DecidedAt = now,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                newMatchGroups.Add(matchGroup);
                newMatchDecisions.Add(matchDecision);

                summary.MatchesFound++;
                summary.InvoiceMatches++;
            }
            #endregion

            #region Strategy 3 - E-Commerce Payouts
            var unmatchedPayouts = await _dbContext.PaymentGatewayPayouts
                .IgnoreQueryFilters()
                .Where(x => !x.IsReconciled && x.TenantId == effectiveTenantId && x.Status == PaymentGatewayPayoutStatus.Pending)
                .ToListAsync();

            var matchedPayoutIds = new HashSet<Guid>();

            foreach (var line in unresolvedLines.Where(x => !x.IsReconciled && x.Amount > 0))
            {
                var matchedPayout = unmatchedPayouts.FirstOrDefault(payout =>
                    !matchedPayoutIds.Contains(payout.Id)
                    && payout.NetAmount == line.Amount);

                if (matchedPayout == null)
                {
                    continue;
                }

                line.IsReconciled = true;
                line.UpdatedAt = now;
                line.UpdatedBy = currentUserId;

                matchedPayout.IsReconciled = true;
                matchedPayout.Status = PaymentGatewayPayoutStatus.Settled;
                matchedPayout.UpdatedAt = now;
                matchedPayout.UpdatedBy = currentUserId;
                matchedPayoutIds.Add(matchedPayout.Id);

                var matchGroupId = Guid.NewGuid();
                var matchGroup = new MatchGroup
                {
                    Id = matchGroupId,
                    ReconciliationRunId = run.Id,
                    MatchConfidenceScore = 1.0000m,
                    Status = resolvedMatchGroupStatus,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                var matchDecision = new MatchDecision
                {
                    Id = Guid.NewGuid(),
                    MatchGroupId = matchGroupId,
                    Decision = resolvedMatchDecisionStatus,
                    DecisionReason = "Auto-matched E-Commerce Payout by net amount",
                    DecidedBy = currentUserId,
                    DecidedAt = now,
                    TenantId = effectiveTenantId,
                    CreatedAt = now,
                    CreatedBy = currentUserId
                };

                newMatchGroups.Add(matchGroup);
                newMatchDecisions.Add(matchDecision);

                summary.MatchesFound++;
                summary.PayoutMatches++;
            }
            #endregion

            if (newMatchGroups.Count > 0)
            {
                _dbContext.MatchGroups.AddRange(newMatchGroups);
                _dbContext.MatchDecisions.AddRange(newMatchDecisions);
            }

            run.TotalMatchesProposed = summary.MatchesFound;
            run.Status = ReconciliationRunStatus.Completed;
            run.UpdatedAt = now;
            run.UpdatedBy = currentUserId;

            await _dbContext.SaveChangesAsync();

            return summary;
        }
    }
}
