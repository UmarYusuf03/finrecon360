using System.ComponentModel.DataAnnotations;

namespace finrecon360_backend.Dtos.Admin
{
    public record PaymentAlertResponse(
        Guid Id,
        Guid TenantId,
        string TenantName,
        Guid SubscriptionId,
        string PlanName,
        DateTime PeriodEndAt,
        int DaysOverdue,
        string Status,
        DateTime CreatedAt,
        DateTime? AcknowledgedAt,
        DateTime? ResolvedAt);

    public record PaymentAlertSummaryResponse(int OpenCount);

    public record BillingSettingsResponse(int PaymentOverdueSuspensionThresholdDays, DateTime UpdatedAt);

    public class BillingSettingsUpdateRequest
    {
        [Range(1, 365)]
        public int PaymentOverdueSuspensionThresholdDays { get; set; }
    }
}
