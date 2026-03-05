namespace finrecon360_backend.Options
{
    public class StripeOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }
}
