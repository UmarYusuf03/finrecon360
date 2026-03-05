using System.Text.Json;

namespace finrecon360_backend.Dtos.Public
{
    public class TenantRegistrationCreateRequest
    {
        public string BusinessName { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string BusinessRegistrationNumber { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public JsonElement? OnboardingMetadata { get; set; }
    }

    public record TenantRegistrationCreateResponse(Guid RequestId, string Status);
}
