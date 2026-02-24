using FluentValidation;
using finrecon360_backend.Dtos.Auth;

namespace finrecon360_backend.Validators
{
    public class VerifyEmailLinkRequestValidator : AbstractValidator<VerifyEmailLinkRequest>
    {
        public VerifyEmailLinkRequestValidator()
        {
            RuleFor(r => r.Token).NotEmpty().MaximumLength(512);
        }
    }

    public class RequestPasswordResetLinkRequestValidator : AbstractValidator<RequestPasswordResetLinkRequest>
    {
        public RequestPasswordResetLinkRequestValidator()
        {
            RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();
        }
    }

    public class ConfirmPasswordResetLinkRequestValidator : AbstractValidator<ConfirmPasswordResetLinkRequest>
    {
        public ConfirmPasswordResetLinkRequestValidator()
        {
            RuleFor(r => r.Token).NotEmpty().MaximumLength(512);
            RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
        }
    }

    public class ConfirmChangePasswordLinkRequestValidator : AbstractValidator<ConfirmChangePasswordLinkRequest>
    {
        public ConfirmChangePasswordLinkRequestValidator()
        {
            RuleFor(r => r.Token).NotEmpty().MaximumLength(512);
            RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(128);
            RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
        }
    }
}
