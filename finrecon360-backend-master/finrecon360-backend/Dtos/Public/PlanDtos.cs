namespace finrecon360_backend.Dtos.Public
{
    public record PublicPlanSummaryDto(
        Guid Id,
        string Code,
        string Name,
        long PriceCents,
        string Currency,
        int DurationDays,
        int MaxAccounts);
}
