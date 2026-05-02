namespace finrecon360_backend.Services
{
    /// <summary>
    /// WHY: This abstraction prevents the backend from becoming tightly coupled to a specific 
    /// email provider's SDK. The `parameters` dictionary provides a universal format for 
    /// feeding dynamic variables (like magic links or names) into remote templates.
    /// </summary>
    public interface IEmailSender
    {
        Task SendTemplateAsync(string toEmail, long templateId, IDictionary<string, object> parameters, CancellationToken cancellationToken = default);
    }
}
