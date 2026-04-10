namespace finrecon360_backend.Dtos.Onboarding
{
    public class OnboardingMagicLinkVerifyRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    public record OnboardingMagicLinkVerifyResponse(
        string OnboardingToken,
        string Email,
        Guid TenantId,
        string TenantName,
        DateTime ExpiresAtUtc);

    public class OnboardingSetPasswordRequest
    {
        public string OnboardingToken { get; set; } = string.Empty;
        public string MagicLinkToken { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class OnboardingCheckoutRequest
    {
        public string OnboardingToken { get; set; } = string.Empty;
        public Guid PlanId { get; set; }
    }

    public record OnboardingCheckoutResponse(string CheckoutUrl);
}
