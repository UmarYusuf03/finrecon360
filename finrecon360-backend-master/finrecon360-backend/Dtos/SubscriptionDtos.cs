using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Dtos.Public;

namespace finrecon360_backend.Dtos.Subscriptions
{
    public class SubscriptionChangeRequest
    {
        public Guid PlanId { get; set; }
    }

    public record SubscriptionCheckoutResponse(
        Guid SubscriptionId,
        string CheckoutUrl);

    public record SubscriptionOverviewDto(
        TenantSubscriptionDto? CurrentSubscription,
        IReadOnlyList<PublicPlanSummaryDto> AvailablePlans);
}