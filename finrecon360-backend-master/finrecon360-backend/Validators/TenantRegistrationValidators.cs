using finrecon360_backend.Dtos.Public;
using FluentValidation;

namespace finrecon360_backend.Validators
{
    public class TenantRegistrationCreateRequestValidator : AbstractValidator<TenantRegistrationCreateRequest>
    {
        private static readonly HashSet<string> AllowedBusinessTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "VEHICLE_RENTAL",
            "ACCOMMODATION"
        };

        public TenantRegistrationCreateRequestValidator()
        {
            RuleFor(request => request.BusinessName)
                .NotEmpty()
                .MaximumLength(256);

            RuleFor(request => request.AdminEmail)
                .NotEmpty()
                .MaximumLength(256)
                .EmailAddress();

            RuleFor(request => request.PhoneNumber)
                .NotEmpty()
                .MaximumLength(32);

            RuleFor(request => request.BusinessRegistrationNumber)
                .NotEmpty()
                .MaximumLength(128);

            RuleFor(request => request.BusinessType)
                .NotEmpty()
                .MaximumLength(64)
                .Must(type => AllowedBusinessTypes.Contains(type))
                .WithMessage("Business type must be VEHICLE_RENTAL or ACCOMMODATION.");
        }
    }
}