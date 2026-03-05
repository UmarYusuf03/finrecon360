using FluentValidation;
using finrecon360_backend.Dtos.Auth;

namespace finrecon360_backend.Validators
{
    public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
    {
        public RegisterRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256);

            RuleFor(x => x.FirstName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(x => x.LastName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(x => x.Country)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(x => x.Gender)
                .NotEmpty()
                .MaximumLength(64);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8)
                .MaximumLength(128);

            RuleFor(x => x.ConfirmPassword)
                .NotEmpty()
                .Equal(x => x.Password)
                .WithMessage("Passwords do not match.");
        }
    }

    public class LoginRequestValidator : AbstractValidator<LoginRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MaximumLength(128);
        }
    }
}
