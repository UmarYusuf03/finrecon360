namespace finrecon360_backend.Services
{
    public interface IEmailSender
    {
        Task SendTemplateAsync(string toEmail, long templateId, IDictionary<string, object> parameters, CancellationToken cancellationToken = default);
    }
}
