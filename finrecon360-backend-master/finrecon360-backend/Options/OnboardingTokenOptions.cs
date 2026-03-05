namespace finrecon360_backend.Options
{
    public class OnboardingTokenOptions
    {
        public string Issuer { get; set; } = "finrecon360";
        public string Audience { get; set; } = "onboarding";
        public int ExpiresMinutes { get; set; } = 20;
    }
}
