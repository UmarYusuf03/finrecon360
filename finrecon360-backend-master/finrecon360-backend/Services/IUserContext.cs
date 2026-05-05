namespace finrecon360_backend.Services
{
    /// <summary>
    /// WHY: Represents the authenticated user extracted from the JWT token at request time.
    /// By injecting this interface into controllers/services, endpoints can access the current UserId and Email
    /// without parsing claims directly. This abstraction also supports mock/stub implementations for testing.
    /// IsAuthenticated and Status flags enable guarding logic (e.g., reject actions for Suspended users).
    /// </summary>
    public interface IUserContext
    {
        Guid? UserId { get; }
        string? Email { get; }
        bool IsAuthenticated { get; }
        bool IsActive { get; }
        Models.UserStatus? Status { get; }
    }
}
