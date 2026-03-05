using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class ActionCreateRequestValidator : AbstractValidator<ActionCreateRequest>
    {
        public ActionCreateRequestValidator()
        {
            RuleFor(a => a.Code)
                .NotEmpty()
                .MaximumLength(50)
                .Matches("^[A-Za-z0-9_]+$");

            RuleFor(a => a.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(a => a.Description)
                .MaximumLength(500);
        }
    }

    public class ActionUpdateRequestValidator : AbstractValidator<ActionUpdateRequest>
    {
        public ActionUpdateRequestValidator()
        {
            RuleFor(a => a.Code)
                .NotEmpty()
                .MaximumLength(50)
                .Matches("^[A-Za-z0-9_]+$");

            RuleFor(a => a.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(a => a.Description)
                .MaximumLength(500);
        }
    }
}
