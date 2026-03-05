namespace finrecon360_backend.Services
{
    public interface IAuditLogger
    {
        Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null);
    }
}
