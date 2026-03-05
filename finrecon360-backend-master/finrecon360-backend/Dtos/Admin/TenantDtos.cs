namespace finrecon360_backend.Dtos.Admin
{
    public record TenantSummaryDto(
        Guid Id,
        string Name,
        string Status,
        DateTime CreatedAt,
        string? CurrentPlan);

    public record TenantAdminDto(
        Guid UserId,
        string Email,
        string DisplayName,
        string Status);

    public record TenantSubscriptionDto(
        Guid SubscriptionId,
        string PlanCode,
        string PlanName,
        string Status,
        DateTime? PeriodStart,
        DateTime? PeriodEnd);

    public record TenantDetailDto(
        Guid Id,
        string Name,
        string Status,
        DateTime CreatedAt,
        DateTime? ActivatedAt,
        string? PrimaryDomain,
        TenantSubscriptionDto? CurrentSubscription,
        IReadOnlyList<TenantAdminDto> Admins);

    public class TenantAdminSetRequest
    {
        public IReadOnlyList<Guid>? UserIds { get; set; }
        public IReadOnlyList<string>? Emails { get; set; }
    }
}
