using FluentValidation;
using finrecon360_backend.Dtos.Reconciliation;

namespace finrecon360_backend.Validators
{
    public class UpdateReconciliationSettingsRequestValidator : AbstractValidator<UpdateReconciliationSettingsRequest>
    {
        public UpdateReconciliationSettingsRequestValidator()
        {
            RuleFor(r => r.AmountTolerance).GreaterThanOrEqualTo(0);
            RuleFor(r => r.DateToleranceDays).GreaterThanOrEqualTo(0);
            RuleFor(r => r.SettlementDateWindowDays).GreaterThanOrEqualTo(0);
        }
    }
}
