namespace finrecon360_backend.Services
{
    /// <summary>
    /// WHY: Defines the audit log interface to capture all significant user/admin actions.
    /// By injecting IAuditLogger into controllers, services, and middleware, every important event
    /// (login, permission changes, enforcement actions, onboarding approvals) is logged for compliance/forensics.
    /// This creates an immutable trail for regulatory audits and security incident investigations.
    /// </summary>
    public interface IAuditLogger
    {
        Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null);
    }
}
