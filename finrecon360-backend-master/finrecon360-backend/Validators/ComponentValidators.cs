using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class ComponentCreateRequestValidator : AbstractValidator<ComponentCreateRequest>
    {
        public ComponentCreateRequestValidator()
        {
            RuleFor(c => c.Code)
                .NotEmpty()
                .MaximumLength(100)
                .Matches("^[A-Za-z0-9_]+$");

            RuleFor(c => c.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(c => c.RoutePath)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(c => c.Category)
                .MaximumLength(100);

            RuleFor(c => c.Description)
                .MaximumLength(500);
        }
    }

    public class ComponentUpdateRequestValidator : AbstractValidator<ComponentUpdateRequest>
    {
        public ComponentUpdateRequestValidator()
        {
            RuleFor(c => c.Code)
                .NotEmpty()
                .MaximumLength(100)
                .Matches("^[A-Za-z0-9_]+$");

            RuleFor(c => c.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(c => c.RoutePath)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(c => c.Category)
                .MaximumLength(100);

            RuleFor(c => c.Description)
                .MaximumLength(500);
        }
    }
}
