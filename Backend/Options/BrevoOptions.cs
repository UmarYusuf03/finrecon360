namespace finrecon360_backend.Options
{
    public class BrevoOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public long TemplateIdMagicLinkVerify { get; set; }
        public long TemplateIdMagicLinkReset { get; set; }
        public long? TemplateIdMagicLinkChange { get; set; }
    }
}
