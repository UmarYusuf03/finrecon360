namespace finrecon360_backend.Services
{
    public record EmailAttachment(string FileName, byte[] Content);

    /// <summary>
    /// WHY: This abstraction prevents the backend from becoming tightly coupled to a specific
    /// email provider's SDK. The `parameters` dictionary provides a universal format for
    /// feeding dynamic variables (like magic links or names) into remote templates.
    /// </summary>
    public interface IEmailSender
    {
        Task SendTemplateAsync(string toEmail, long templateId, IDictionary<string, object> parameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plain (non-template) email with an attachment — added for scheduled report delivery,
        /// which has no pre-built Brevo template to render into (the body is generated content,
        /// not a fixed marketing/transactional layout) and needs to carry a binary attachment
        /// SendTemplateAsync has no field for.
        /// </summary>
        Task SendWithAttachmentAsync(
            string toEmail,
            string subject,
            string htmlBody,
            IReadOnlyList<EmailAttachment> attachments,
            CancellationToken cancellationToken = default);
    }
}
