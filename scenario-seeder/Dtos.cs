namespace ScenarioSeeder;

// Client-side mirrors of the finrecon360-backend response/request shapes this tool talks to.
// Field NAMES must match the server's JSON (case-insensitive); field order and record-vs-class
// don't matter for System.Text.Json's positional-record deserialization.

public sealed record LoginResponseDto(string Email, string FullName, string Token);

public sealed record TenantRegistrationCreateResponse(Guid RequestId, string Status);

public sealed record TenantRegistrationApprovalResponseDto(
    Guid RequestId, string AdminEmail, string? OnboardingLink, bool EmailSent, string? EmailError);

public sealed record OnboardingMagicLinkVerifyResponseDto(
    string OnboardingToken, string Email, Guid TenantId, string TenantName, DateTime ExpiresAtUtc);

public sealed record PublicPlanSummaryDto(
    Guid Id, string Code, string Name, int PriceCents, string Currency,
    int DurationDays, int MaxUsers, int MaxAccounts);

public sealed record OnboardingCheckoutResponseDto(string CheckoutUrl);

public sealed record BankAccountResponseDto(
    Guid BankAccountId, string BankName, string AccountName, string AccountNumber,
    string Currency, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record TransactionResponseDto(
    Guid TransactionId, decimal Amount, DateTime TransactionDate, string Description,
    string? ReferenceNumber, Guid? BankAccountId, string TransactionType, string PaymentMethod,
    string TransactionState, Guid? CreatedByUserId, DateTime? ApprovedAt, Guid? ApprovedByUserId,
    DateTime? RejectedAt, Guid? RejectedByUserId, string? RejectionReason, string? CardLast4,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record ImportUploadResponseDto(
    Guid Id, string Status, string SourceType, string OriginalFileName, DateTime ImportedAt);

public sealed record ImportValidationErrorDto(int RowNumber, string Message);

public sealed record ImportValidateResponseDto(
    Guid BatchId, string Status, int TotalRows, int ValidRows, int InvalidRows,
    List<ImportValidationErrorDto> Errors);

public sealed record ImportCommitResponseDto(
    Guid BatchId, string Status, int NormalizedCount, DateTime CommittedAt);

/// <summary>Saved to disk after bootstrap so a later run (e.g. a demo re-run) can be pointed
/// at a fresh tenant without touching this one, and so login details survive the process exit.</summary>
public sealed record TenantSession(Guid TenantId, string AdminEmail, string AdminPassword, string Token);
