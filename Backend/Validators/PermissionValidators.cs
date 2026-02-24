using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class PermissionCreateRequestValidator : AbstractValidator<PermissionCreateRequest>
    {
        public PermissionCreateRequestValidator()
        {
            RuleFor(p => p.Code)
                .NotEmpty()
                .MaximumLength(150)
                .Matches("^[A-Z0-9_]+(\\.[A-Z0-9_]+)*$");

            RuleFor(p => p.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(p => p.Description)
                .MaximumLength(500);

            RuleFor(p => p.Module)
                .MaximumLength(100);
        }
    }

    public class PermissionUpdateRequestValidator : AbstractValidator<PermissionUpdateRequest>
    {
        public PermissionUpdateRequestValidator()
        {
            RuleFor(p => p.Code)
                .NotEmpty()
                .MaximumLength(150)
                .Matches("^[A-Z0-9_]+(\\.[A-Z0-9_]+)*$");

            RuleFor(p => p.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(p => p.Description)
                .MaximumLength(500);

            RuleFor(p => p.Module)
                .MaximumLength(100);
        }
    }
}
