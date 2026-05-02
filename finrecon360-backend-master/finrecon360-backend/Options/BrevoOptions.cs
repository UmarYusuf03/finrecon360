namespace finrecon360_backend.Options
{
    /// <summary>
    /// WHY: Isolates Brevo-specific template IDs into an options pattern. This allows 
    /// different environments (Dev, Staging, Prod) to map their own localized template IDs 
    /// dynamically without polluting or changing core C# business logic.
    /// </summary>
    public class BrevoOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public long TemplateIdMagicLinkVerify { get; set; }
        public long? TemplateIdMagicLinkInvite { get; set; }
        public long TemplateIdMagicLinkReset { get; set; }
        public long? TemplateIdMagicLinkChange { get; set; }
    }
}
