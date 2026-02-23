namespace finrecon360_backend.Dtos.Auth
{
    public record RegisterRequest(
        string Email,
        string FirstName,
        string LastName,
        string Country,
        string Gender,
        string Password,
        string ConfirmPassword);

    public record LoginRequest(string Email, string Password);

    public class LoginResponse
    {
        public string Email { get; set; } = default!;
        public string FullName { get; set; } = default!;
        public string Token { get; set; } = default!;
    }
}
