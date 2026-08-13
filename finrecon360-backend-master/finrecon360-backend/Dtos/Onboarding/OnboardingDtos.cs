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
        DateTime ExpiresAtUtc,
        int? RequestedBankAccounts);

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

    public class PayHereCheckoutPreviewRequest
    {
        public string OrderId { get; set; } = string.Empty;
        public long AmountCents { get; set; }
        public string? Currency { get; set; }
    }

    public record PayHereCheckoutPreviewResponse(
        string MerchantId,
        string OrderId,
        string Amount,
        string Currency,
        bool MerchantSecretWasBase64Decoded,
        int MerchantSecretLength,
        int DecodedMerchantSecretLength,
        string MerchantSecretHash,
        string Hash);
}
