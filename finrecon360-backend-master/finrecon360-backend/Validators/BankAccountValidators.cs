using FluentValidation;
using finrecon360_backend.Dtos.BankAccounts;

namespace finrecon360_backend.Validators
{
    public class CreateBankAccountRequestValidator : AbstractValidator<CreateBankAccountRequest>
    {
        public CreateBankAccountRequestValidator()
        {
            RuleFor(r => r.BankName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.AccountName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.AccountNumber).NotEmpty().MaximumLength(100);
            RuleFor(r => r.Currency).NotEmpty().MaximumLength(10);
        }
    }

    public class UpdateBankAccountRequestValidator : AbstractValidator<UpdateBankAccountRequest>
    {
        public UpdateBankAccountRequestValidator()
        {
            RuleFor(r => r.BankName).NotEmpty().MaximumLength(200).When(r => r.BankName != null);
            RuleFor(r => r.AccountName).NotEmpty().MaximumLength(200).When(r => r.AccountName != null);
            RuleFor(r => r.AccountNumber).NotEmpty().MaximumLength(100).When(r => r.AccountNumber != null);
            RuleFor(r => r.Currency).NotEmpty().MaximumLength(10).When(r => r.Currency != null);
        }
    }
}
