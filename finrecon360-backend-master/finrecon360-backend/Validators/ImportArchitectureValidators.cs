using FluentValidation;
using finrecon360_backend.Dtos.Admin;

namespace finrecon360_backend.Validators
{
    public class CreateImportBatchRequestValidator : AbstractValidator<CreateImportBatchRequest>
    {
        public CreateImportBatchRequestValidator()
        {
            RuleFor(x => x.SourceType)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Status)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.OriginalFileName)
                .MaximumLength(260);

            RuleFor(x => x.ErrorMessage)
                .MaximumLength(1000);
        }
    }

    public class ImportRawRecordRequestValidator : AbstractValidator<ImportRawRecordRequest>
    {
        public ImportRawRecordRequestValidator()
        {
            RuleFor(x => x.NormalizationStatus)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.NormalizationErrors)
                .MaximumLength(2000);

            RuleFor(x => x.SourcePayload.ValueKind)
                .Must(kind => kind is not System.Text.Json.JsonValueKind.Undefined)
                .WithMessage("SourcePayload is required.");
        }
    }

    public class ImportNormalizedRecordRequestValidator : AbstractValidator<ImportNormalizedRecordRequest>
    {
        public ImportNormalizedRecordRequestValidator()
        {
            RuleFor(x => x.ReferenceNumber)
                .MaximumLength(120);

            RuleFor(x => x.Description)
                .MaximumLength(500);

            RuleFor(x => x.AccountCode)
                .MaximumLength(100);

            RuleFor(x => x.AccountName)
                .MaximumLength(200);

            RuleFor(x => x.Currency)
                .NotEmpty()
                .Length(3);
        }
    }

    public class ImportMappingTemplateCreateRequestValidator : AbstractValidator<ImportMappingTemplateCreateRequest>
    {
        public ImportMappingTemplateCreateRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.SourceType)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.CanonicalSchemaVersion)
                .NotEmpty()
                .MaximumLength(30);

            RuleFor(x => x.MappingJson)
                .NotEmpty();
        }
    }

    public class ImportMappingTemplateUpdateRequestValidator : AbstractValidator<ImportMappingTemplateUpdateRequest>
    {
        public ImportMappingTemplateUpdateRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.SourceType)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.CanonicalSchemaVersion)
                .NotEmpty()
                .MaximumLength(30);

            RuleFor(x => x.MappingJson)
                .NotEmpty();
        }
    }
}
