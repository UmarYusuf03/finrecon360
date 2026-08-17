using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Transactions;
using finrecon360_backend.Models;
using finrecon360_backend.Services.Transactions;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Tests;

/// <summary>
/// Covers the rules of the transaction module rather than its plumbing: the approval routing that
/// decides whether a transaction needs bank matching, the invariants that keep the ledger honest,
/// and the fields whose loss is silent rather than loud.
/// </summary>
public class TransactionServiceTests
{
    [Fact]
    public async Task CreateAsync_persists_the_reference_number()
    {
        // WHY this test exists: the reference survived the form and the database column but was
        // absent from the DTO, model, and service, so every value a user typed was dropped by the
        // model binder without an error. Nothing failed loudly, which is why it went unnoticed.
        await using var db = CreateTenantDb();
        var service = new TransactionService();
        var account = await AddBankAccountAsync(db);

        var created = await service.CreateAsync(db, NewRequest(account.BankAccountId, reference: "GW-4417"), Guid.NewGuid(), default);

        Assert.Equal("GW-4417", created.ReferenceNumber);
        var stored = await db.Transactions.SingleAsync(x => x.TransactionId == created.TransactionId);
        Assert.Equal("GW-4417", stored.ReferenceNumber);
    }

    [Fact]
    public async Task CreateAsync_stores_a_blank_reference_as_null()
    {
        // An empty string looks like a reference to every downstream matcher but can never match.
        await using var db = CreateTenantDb();
        var service = new TransactionService();
        var account = await AddBankAccountAsync(db);

        var created = await service.CreateAsync(db, NewRequest(account.BankAccountId, reference: "   "), Guid.NewGuid(), default);

        Assert.Null(created.ReferenceNumber);
    }

    [Fact]
    public async Task ApproveAsync_routes_a_card_cash_out_to_needs_bank_match()
    {
        // The central rule of the module: money leaving by card is not journal-ready until the
        // bank confirms it left.
        await using var db = CreateTenantDb();
        var service = new TransactionService();
        var account = await AddBankAccountAsync(db);

        var request = NewRequest(account.BankAccountId);
        request.TransactionType = nameof(TransactionType.CashOut);
        request.PaymentMethod = nameof(PaymentMethod.Card);
        var created = await service.CreateAsync(db, request, Guid.NewGuid(), default);

        var approved = await service.ApproveAsync(db, created.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default);

        Assert.Equal(nameof(TransactionState.NeedsBankMatch), approved!.TransactionState);
    }

    [Fact]
    public async Task ApproveAsync_routes_a_cash_transaction_straight_to_journal_ready()
    {
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var request = NewRequest(bankAccountId: null);
        request.TransactionType = nameof(TransactionType.CashOut);
        request.PaymentMethod = nameof(PaymentMethod.Cash);
        var created = await service.CreateAsync(db, request, Guid.NewGuid(), default);

        var approved = await service.ApproveAsync(db, created.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default);

        Assert.Equal(nameof(TransactionState.JournalReady), approved!.TransactionState);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_card_payment_without_a_bank_account()
    {
        // A card payment with no account cannot ever be bank-matched, so it must not be accepted.
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var request = NewRequest(bankAccountId: null);
        request.PaymentMethod = nameof(PaymentMethod.Card);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(db, request, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CreateAsync_drops_card_digits_on_a_cash_transaction()
    {
        // Cash has no card. Storing digits here would leave data a later matcher might trust.
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var request = NewRequest(bankAccountId: null);
        request.PaymentMethod = nameof(PaymentMethod.Cash);
        request.CardLast4 = "4242";

        var created = await service.CreateAsync(db, request, Guid.NewGuid(), default);

        Assert.Null(created.CardLast4);
    }

    [Fact]
    public async Task CreateAsync_rejects_card_digits_that_are_not_four_numerals()
    {
        await using var db = CreateTenantDb();
        var service = new TransactionService();
        var account = await AddBankAccountAsync(db);

        var request = NewRequest(account.BankAccountId);
        request.PaymentMethod = nameof(PaymentMethod.Card);
        request.CardLast4 = "12x";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(db, request, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ApproveAsync_refuses_a_transaction_that_is_not_pending()
    {
        // Approved and rejected records are immutable so the audit trail cannot be rewritten.
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var created = await service.CreateAsync(db, NewRequest(bankAccountId: null), Guid.NewGuid(), default);
        await service.ApproveAsync(db, created.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(db, created.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default));
    }

    [Fact]
    public async Task CreateAsync_writes_an_opening_history_row()
    {
        // The audit trail has to start at creation, otherwise the first transition has no origin.
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var created = await service.CreateAsync(db, NewRequest(bankAccountId: null), Guid.NewGuid(), default);

        var history = await db.TransactionStateHistories
            .Where(x => x.TransactionId == created.TransactionId)
            .ToListAsync();

        Assert.Single(history);
        Assert.Equal(TransactionState.Pending, history[0].ToState);
    }

    [Fact]
    public async Task RejectAsync_records_the_reason_on_the_record_and_in_history()
    {
        await using var db = CreateTenantDb();
        var service = new TransactionService();

        var created = await service.CreateAsync(db, NewRequest(bankAccountId: null), Guid.NewGuid(), default);
        var rejected = await service.RejectAsync(
            db, created.TransactionId, Guid.NewGuid(), new RejectTransactionRequest { Reason = "Duplicate entry" }, default);

        Assert.Equal(nameof(TransactionState.Rejected), rejected!.TransactionState);
        Assert.Equal("Duplicate entry", rejected.RejectionReason);

        var history = await db.TransactionStateHistories
            .Where(x => x.TransactionId == created.TransactionId)
            .OrderBy(x => x.ChangedAt)
            .ToListAsync();

        Assert.Contains(history, x => x.ToState == TransactionState.Rejected && x.Note == "Duplicate entry");
    }

    [Fact]
    public async Task GetJournalReadyAsync_excludes_transactions_awaiting_a_bank_match()
    {
        // The journal queue is a promise that everything in it may be posted. A card cash-out
        // awaiting settlement must not appear in it.
        await using var db = CreateTenantDb();
        var service = new TransactionService();
        var account = await AddBankAccountAsync(db);

        var cardRequest = NewRequest(account.BankAccountId);
        cardRequest.TransactionType = nameof(TransactionType.CashOut);
        cardRequest.PaymentMethod = nameof(PaymentMethod.Card);
        var card = await service.CreateAsync(db, cardRequest, Guid.NewGuid(), default);
        await service.ApproveAsync(db, card.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default);

        var cash = await service.CreateAsync(db, NewRequest(bankAccountId: null), Guid.NewGuid(), default);
        await service.ApproveAsync(db, cash.TransactionId, Guid.NewGuid(), new ApproveTransactionRequest(), default);

        var journalReady = await service.GetJournalReadyAsync(db, default);

        Assert.Contains(journalReady, x => x.TransactionId == cash.TransactionId);
        Assert.DoesNotContain(journalReady, x => x.TransactionId == card.TransactionId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CreateTransactionRequest NewRequest(Guid? bankAccountId, string? reference = null) => new()
    {
        Amount = 1500m,
        TransactionDate = DateTime.UtcNow.Date.AddDays(-1),
        Description = "Test transaction",
        BankAccountId = bankAccountId,
        TransactionType = nameof(TransactionType.CashIn),
        PaymentMethod = nameof(PaymentMethod.Cash),
        ReferenceNumber = reference,
    };

    private static async Task<BankAccount> AddBankAccountAsync(TenantDbContext db)
    {
        var account = new BankAccount
        {
            BankAccountId = Guid.NewGuid(),
            BankName = "Test Bank",
            AccountNumber = $"ACC-{Guid.NewGuid():N}"[..12],
            IsActive = true,
        };

        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static TenantDbContext CreateTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"TenantDb-Transactions-{Guid.NewGuid()}")
            .Options;

        return new TenantDbContext(options);
    }
}
