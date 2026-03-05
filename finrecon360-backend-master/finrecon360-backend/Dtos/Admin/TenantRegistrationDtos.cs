namespace finrecon360_backend.Dtos.Admin
{
    public record TenantRegistrationSummaryDto(
        Guid Id,
        string BusinessName,
        string AdminEmail,
        string PhoneNumber,
        string BusinessRegistrationNumber,
        string BusinessType,
        string Status,
        DateTime SubmittedAt);

    public record TenantRegistrationDetailDto(
        Guid Id,
        string BusinessName,
        string AdminEmail,
        string PhoneNumber,
        string BusinessRegistrationNumber,
        string BusinessType,
        string Status,
        DateTime SubmittedAt,
        DateTime? ReviewedAt,
        string? ReviewedByEmail,
        string? ReviewNote,
        string? OnboardingMetadata);

    public class TenantRegistrationReviewRequest
    {
        public string? Note { get; set; }
    }
}
