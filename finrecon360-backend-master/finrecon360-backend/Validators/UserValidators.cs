using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class AdminUserCreateRequestValidator : AbstractValidator<AdminUserCreateRequest>
    {
        public AdminUserCreateRequestValidator()
        {
            RuleFor(u => u.Email)
                .NotEmpty()
                .MaximumLength(256)
                .EmailAddress();

            RuleFor(u => u.DisplayName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(u => u.Password)
                .NotEmpty()
                .MinimumLength(8)
                .MaximumLength(128);

            RuleFor(u => u.PhoneNumber)
                .MaximumLength(32);
        }
    }

    public class AdminUserUpdateRequestValidator : AbstractValidator<AdminUserUpdateRequest>
    {
        public AdminUserUpdateRequestValidator()
        {
            RuleFor(u => u.DisplayName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(u => u.PhoneNumber)
                .MaximumLength(32);
        }
    }

    public class AdminUserRoleSetRequestValidator : AbstractValidator<AdminUserRoleSetRequest>
    {
        public AdminUserRoleSetRequestValidator()
        {
            RuleFor(r => r)
                .Must(r => r.RoleIds != null || r.RoleCodes != null)
                .WithMessage("RoleIds or RoleCodes must be provided.");
        }
    }
}
