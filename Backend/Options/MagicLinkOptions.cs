namespace finrecon360_backend.Options
{
    public class MagicLinkOptions
    {
        public int ExpiresMinutes { get; set; } = 10;
        public int MaxAttempts { get; set; } = 5;
        public int ResendCooldownSeconds { get; set; } = 60;
        public string FrontendBaseUrl { get; set; } = string.Empty;
    }
}
