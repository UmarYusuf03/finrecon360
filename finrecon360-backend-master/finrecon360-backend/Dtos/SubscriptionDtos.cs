using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Dtos.Subscriptions
{
    public class SubscriptionChangeRequest
    {
        public Guid PlanId { get; set; }
    }

    public record SubscriptionCheckoutResponse(
        Guid SubscriptionId,
        string CheckoutUrl);

    public record SubscriptionUsageDto(
        int ActiveUsers,
        int ActiveBankAccounts);

    // WHY a separate DTO from PublicPlanSummaryDto: eligibility is computed against THIS tenant's
    // current usage, so it isn't a property of the plan itself — it doesn't belong on the DTO the
    // public/system plan-listing endpoints share.
    public record SubscriptionPlanOptionDto(
        Guid Id,
        string Code,
        string Name,
        long PriceCents,
        string Currency,
        int DurationDays,
        int MaxUsers,
        int MaxAccounts,
        bool IsEligible,
        string? IneligibleReason);

    public record SubscriptionOverviewDto(
        TenantSubscriptionDto? CurrentSubscription,
        SubscriptionUsageDto Usage,
        IReadOnlyList<SubscriptionPlanOptionDto> AvailablePlans);
}