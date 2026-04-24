namespace finrecon360_backend.Dtos.Admin
{
    public record PlanSummaryDto(
        Guid Id,
        string Code,
        string Name,
        long PriceCents,
        string Currency,
        int DurationDays,
        int MaxUsers,
        int MaxAccounts,
        bool IsActive);

    public class PlanCreateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long PriceCents { get; set; }
        public string Currency { get; set; } = "USD";
        public int DurationDays { get; set; }
        public int MaxUsers { get; set; }
        public int MaxAccounts { get; set; }
    }

    public class PlanUpdateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long PriceCents { get; set; }
        public string Currency { get; set; } = "USD";
        public int DurationDays { get; set; }
        public int MaxUsers { get; set; }
        public int MaxAccounts { get; set; }
    }
}
