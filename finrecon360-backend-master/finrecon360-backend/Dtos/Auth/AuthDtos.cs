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

    /// <summary>
    /// The ID token returned by Google Identity Services in the browser. It is a signed assertion
    /// from Google, not a password, and it is verified server-side before it is trusted.
    /// </summary>
    public record GoogleSsoLoginRequest(string IdToken);

    public class LoginResponse
    {
        public string Email { get; set; } = default!;
        public string FullName { get; set; } = default!;
        public string Token { get; set; } = default!;
    }
}
