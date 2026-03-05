using FluentValidation;
using finrecon360_backend.Dtos.Users;

namespace finrecon360_backend.Validators
{
    public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
    {
        public UpdateProfileRequestValidator()
        {
            RuleFor(x => x.DisplayName)
                .MaximumLength(256);

            RuleFor(x => x.FirstName)
                .MaximumLength(256);

            RuleFor(x => x.LastName)
                .MaximumLength(256);

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(32);
        }
    }
}
