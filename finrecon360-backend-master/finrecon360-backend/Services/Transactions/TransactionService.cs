using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Transactions;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.Transactions
{
    public class TransactionService
    {
        public async Task<TransactionResponse> CreateAsync(
            TenantDbContext db,
            CreateTransactionRequest request,
            Guid userId,
            CancellationToken ct)
        {
            if (request.Amount <= 0)
            {
                throw new InvalidOperationException("Amount must be greater than zero.");
            }

            if (request.TransactionDate == default)
            {
                throw new InvalidOperationException("TransactionDate is required.");
            }

            var transactionType = ParseEnum<TransactionType>(request.TransactionType, nameof(request.TransactionType));
            var paymentMethod = ParseEnum<PaymentMethod>(request.PaymentMethod, nameof(request.PaymentMethod));

            if (paymentMethod == PaymentMethod.Card && request.BankAccountId == null)
            {
                throw new InvalidOperationException("BankAccountId is required for card transactions.");
            }

            if (request.BankAccountId.HasValue)
            {
                var bankAccountExists = await db.BankAccounts
                    .AsNoTracking()
                    .AnyAsync(x => x.BankAccountId == request.BankAccountId.Value, ct);

                if (!bankAccountExists)
                {
                    throw new InvalidOperationException("Bank account was not found.");
                }
            }

            var now = DateTime.UtcNow;
            var entity = new Transaction
            {
                TransactionId = Guid.NewGuid(),
                Amount = request.Amount,
                TransactionDate = request.TransactionDate,
                Description = request.Description.Trim(),
                BankAccountId = request.BankAccountId,
                TransactionType = transactionType,
                PaymentMethod = paymentMethod,
                TransactionState = TransactionState.Pending,
                CreatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = null
            };

            // Transactions enter the workflow as Pending and get an initial history row for audit continuity.
            var history = new TransactionStateHistory
            {
                TransactionStateHistoryId = Guid.NewGuid(),
                TransactionId = entity.TransactionId,
                FromState = TransactionState.Pending,
                ToState = TransactionState.Pending,
                ChangedByUserId = userId,
                ChangedAt = now,
                Note = "Transaction created"
            };

            db.Transactions.Add(entity);
            db.TransactionStateHistories.Add(history);
            await db.SaveChangesAsync(ct);

            return Map(entity);
        }

        public async Task<List<TransactionResponse>> GetAllAsync(TenantDbContext db, CancellationToken ct)
        {
            var items = await db.Transactions
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

            return items.Select(Map).ToList();
        }

        public async Task<List<TransactionResponse>> GetJournalReadyAsync(TenantDbContext db, CancellationToken ct)
        {
            // Intentionally excludes NeedsBankMatch items; those must be matched before journal posting.
            var items = await db.Transactions
                .AsNoTracking()
                .Where(x => x.TransactionState == TransactionState.JournalReady)
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            return items.Select(Map).ToList();
        }

        public async Task<List<TransactionResponse>> GetNeedsBankMatchAsync(TenantDbContext db, CancellationToken ct)
        {
            // This queue is the handoff point for the future matcher/reconciliation workflow.
            var items = await db.Transactions
                .AsNoTracking()
                .Where(x => x.TransactionState == TransactionState.NeedsBankMatch)
                .OrderBy(x => x.TransactionDate)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(ct);

            return items.Select(Map).ToList();
        }

        public async Task<TransactionResponse?> GetByIdAsync(TenantDbContext db, Guid id, CancellationToken ct)
        {
            var entity = await db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TransactionId == id, ct);

            return entity == null ? null : Map(entity);
        }

        public async Task<List<TransactionStateHistoryResponse>> GetHistoryAsync(
            TenantDbContext db,
            Guid transactionId,
            CancellationToken ct)
        {
            var items = await db.TransactionStateHistories
                .AsNoTracking()
                .Where(x => x.TransactionId == transactionId)
                .OrderBy(x => x.ChangedAt)
                .ToListAsync(ct);

            return items.Select(MapHistory).ToList();
        }

        public async Task<TransactionResponse?> ApproveAsync(
            TenantDbContext db,
            Guid transactionId,
            Guid userId,
            ApproveTransactionRequest request,
            CancellationToken ct)
        {
            var entity = await db.Transactions
                .FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);

            if (entity == null)
            {
                return null;
            }

            if (entity.TransactionState != TransactionState.Pending)
            {
                throw new InvalidOperationException("Only pending transactions can be approved.");
            }

            var now = DateTime.UtcNow;
            var fromState = entity.TransactionState;
            // Cash and other non-card-cashout approvals can move straight to JournalReady.
            // Card cash-outs pause at NeedsBankMatch so the matcher module can take over first.
            var toState = entity.TransactionType == TransactionType.CashOut && entity.PaymentMethod == PaymentMethod.Card
                ? TransactionState.NeedsBankMatch
                : TransactionState.JournalReady;

            entity.TransactionState = toState;
            entity.ApprovedAt = now;
            entity.ApprovedByUserId = userId;
            entity.UpdatedAt = now;

            AddStateHistory(db, entity.TransactionId, fromState, toState, userId, now, NormalizeOptionalNote(request.Note));
            await db.SaveChangesAsync(ct);

            return Map(entity);
        }

        public async Task<TransactionResponse?> RejectAsync(
            TenantDbContext db,
            Guid transactionId,
            Guid userId,
            RejectTransactionRequest request,
            CancellationToken ct)
        {
            var reason = NormalizeRequiredNote(request.Reason, "Rejection reason is required.");

            var entity = await db.Transactions
                .FirstOrDefaultAsync(x => x.TransactionId == transactionId, ct);

            if (entity == null)
            {
                return null;
            }

            if (entity.TransactionState != TransactionState.Pending)
            {
                throw new InvalidOperationException("Only pending transactions can be rejected.");
            }

            var now = DateTime.UtcNow;
            var fromState = entity.TransactionState;

            entity.TransactionState = TransactionState.Rejected;
            entity.RejectedAt = now;
            entity.RejectedByUserId = userId;
            entity.RejectionReason = reason;
            entity.UpdatedAt = now;

            AddStateHistory(db, entity.TransactionId, fromState, TransactionState.Rejected, userId, now, reason);
            await db.SaveChangesAsync(ct);

            return Map(entity);
        }

        private static TEnum ParseEnum<TEnum>(string value, string fieldName)
            where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"{fieldName} is invalid.");
        }

        private static void AddStateHistory(
            TenantDbContext db,
            Guid transactionId,
            TransactionState fromState,
            TransactionState toState,
            Guid userId,
            DateTime changedAt,
            string? note)
        {
            // Keep state changes append-only so approval/rejection decisions remain reviewable.
            db.TransactionStateHistories.Add(new TransactionStateHistory
            {
                TransactionStateHistoryId = Guid.NewGuid(),
                TransactionId = transactionId,
                FromState = fromState,
                ToState = toState,
                ChangedByUserId = userId,
                ChangedAt = changedAt,
                Note = note
            });
        }

        private static string? NormalizeOptionalNote(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var note = value.Trim();
            if (note.Length > 500)
            {
                throw new InvalidOperationException("Note cannot exceed 500 characters.");
            }

            return note;
        }

        private static string NormalizeRequiredNote(string value, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(errorMessage);
            }

            var note = value.Trim();
            if (note.Length > 500)
            {
                throw new InvalidOperationException("Reason cannot exceed 500 characters.");
            }

            return note;
        }

        private static TransactionResponse Map(Transaction entity) =>
            new(
                entity.TransactionId,
                entity.Amount,
                entity.TransactionDate,
                entity.Description,
                entity.BankAccountId,
                entity.TransactionType.ToString(),
                entity.PaymentMethod.ToString(),
                entity.TransactionState.ToString(),
                entity.CreatedByUserId,
                entity.ApprovedAt,
                entity.ApprovedByUserId,
                entity.RejectedAt,
                entity.RejectedByUserId,
                entity.RejectionReason,
                entity.CreatedAt,
                entity.UpdatedAt);

        private static TransactionStateHistoryResponse MapHistory(TransactionStateHistory entity) =>
            new(
                entity.TransactionStateHistoryId,
                entity.TransactionId,
                entity.FromState.ToString(),
                entity.ToState.ToString(),
                entity.ChangedByUserId,
                entity.ChangedAt,
                entity.Note);
    }
}
