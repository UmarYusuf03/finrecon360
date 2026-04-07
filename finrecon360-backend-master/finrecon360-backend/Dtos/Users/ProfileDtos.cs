namespace finrecon360_backend.Dtos.Users
{
    public record UserProfileDto(
        Guid UserId,
        string Email,
        string? DisplayName,
        string FirstName,
        string LastName,
        string? PhoneNumber,
        bool HasProfileImage,
        bool IsActive);

    public record UpdateProfileRequest(
        string? DisplayName,
        string? FirstName,
        string? LastName,
        string? PhoneNumber);

    public class UploadProfilePhotoRequest
    {
        public IFormFile? File { get; set; }
    }
}
