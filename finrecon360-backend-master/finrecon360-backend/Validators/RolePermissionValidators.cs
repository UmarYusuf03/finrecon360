using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class RolePermissionSetRequestValidator : AbstractValidator<RolePermissionSetRequest>
    {
        public RolePermissionSetRequestValidator()
        {
            RuleFor(r => r)
                .Must(r => r.PermissionIds != null || r.PermissionCodes != null)
                .WithMessage("PermissionIds or PermissionCodes must be provided.");
        }
    }
}
