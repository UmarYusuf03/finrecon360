using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class PlanCreateRequestValidator : AbstractValidator<PlanCreateRequest>
    {
        public PlanCreateRequestValidator()
        {
            RuleFor(p => p.Code).NotEmpty().MaximumLength(100);
            RuleFor(p => p.Name).NotEmpty().MaximumLength(200);
            RuleFor(p => p.Currency).NotEmpty().MaximumLength(10);
            RuleFor(p => p.PriceCents).GreaterThanOrEqualTo(0);
            RuleFor(p => p.DurationDays).GreaterThan(0);
            RuleFor(p => p.MaxUsers).GreaterThan(0);
            RuleFor(p => p.MaxAccounts).GreaterThan(0);
        }
    }

    public class PlanUpdateRequestValidator : AbstractValidator<PlanUpdateRequest>
    {
        public PlanUpdateRequestValidator()
        {
            RuleFor(p => p.Code).NotEmpty().MaximumLength(100);
            RuleFor(p => p.Name).NotEmpty().MaximumLength(200);
            RuleFor(p => p.Currency).NotEmpty().MaximumLength(10);
            RuleFor(p => p.PriceCents).GreaterThanOrEqualTo(0);
            RuleFor(p => p.DurationDays).GreaterThan(0);
            RuleFor(p => p.MaxUsers).GreaterThan(0);
            RuleFor(p => p.MaxAccounts).GreaterThan(0);
        }
    }
}
