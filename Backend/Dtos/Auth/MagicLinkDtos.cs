namespace finrecon360_backend.Dtos.Auth
{
    public class VerifyEmailLinkRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    public class RequestPasswordResetLinkRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ConfirmPasswordResetLinkRequest
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ConfirmChangePasswordLinkRequest
    {
        public string Token { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
