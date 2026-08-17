using finrecon360_backend.Dtos.Public;
using finrecon360_backend.Validators;
using FluentValidation;

namespace finrecon360_backend.Tests;

public class TenantRegistrationValidatorTests
{
    private static readonly IValidator<TenantRegistrationCreateRequest> Validator = new TenantRegistrationCreateRequestValidator();

    [Fact]
    public void Valid_request_passes_validation()
    {
        var request = new TenantRegistrationCreateRequest
        {
            BusinessName = "Acme Rentals",
            AdminEmail = "admin@example.com",
            PhoneNumber = "+94123456789",
            BusinessRegistrationNumber = "BRN-123456",
            BusinessType = "VEHICLE_RENTAL"
        };

        var result = Validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Whitespace_and_invalid_values_fail_validation()
    {
        var request = new TenantRegistrationCreateRequest
        {
            BusinessName = "   ",
            AdminEmail = "not-an-email",
            PhoneNumber = "",
            BusinessRegistrationNumber = " ",
            BusinessType = "UNKNOWN"
        };

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TenantRegistrationCreateRequest.BusinessName));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TenantRegistrationCreateRequest.AdminEmail));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TenantRegistrationCreateRequest.PhoneNumber));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TenantRegistrationCreateRequest.BusinessRegistrationNumber));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TenantRegistrationCreateRequest.BusinessType));
    }
}