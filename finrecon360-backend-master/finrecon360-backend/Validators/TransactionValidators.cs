using FluentValidation;
using finrecon360_backend.Dtos.Transactions;

namespace finrecon360_backend.Validators
{
    public class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
    {
        public CreateTransactionRequestValidator()
        {
            RuleFor(r => r.Amount).GreaterThan(0);
        }
    }

    public class UpdateTransactionRequestValidator : AbstractValidator<UpdateTransactionRequest>
    {
        public UpdateTransactionRequestValidator()
        {
            RuleFor(r => r.Amount).GreaterThan(0);
        }
    }
}
