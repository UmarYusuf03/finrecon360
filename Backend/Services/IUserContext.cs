namespace finrecon360_backend.Services
{
    public interface IUserContext
    {
        Guid? UserId { get; }
        string? Email { get; }
        bool IsAuthenticated { get; }
        bool IsActive { get; }
    }
}
