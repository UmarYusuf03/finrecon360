namespace finrecon360_backend.Models
{
    public class TenantRegistrationRequest
    {
        public Guid TenantRegistrationRequestId { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string BusinessRegistrationNumber { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string? OnboardingMetadata { get; set; }
        public string Status { get; set; } = "PENDING_REVIEW";
        public DateTime SubmittedAt { get; set; }
        public Guid? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }

        public User? ReviewedByUser { get; set; }
    }
}
