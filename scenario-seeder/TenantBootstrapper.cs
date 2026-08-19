namespace ScenarioSeeder;

/// <summary>
/// Runs the full tenant-registration -> approval -> onboarding -> trial-activation flow so this
/// tool ends up with a fresh, isolated tenant to seed into. Each run creates a brand-new tenant
/// (its own physically separate SQL database), which is deliberate: a "test now" run and a later
/// "demo" run never share data, so there's no possibility of one contaminating the other.
/// </summary>
public sealed class TenantBootstrapper
{
    private readonly ApiClient _api;

    public TenantBootstrapper(ApiClient api)
    {
        _api = api;
    }

    public async Task<TenantSession> BootstrapAsync(
        string systemAdminEmail,
        string systemAdminPassword,
        string businessName,
        string tenantAdminEmail,
        string tenantPassword,
        string businessType)
    {
        Console.WriteLine("Submitting tenant registration...");
        var reg = await _api.PostAsync<TenantRegistrationCreateResponse>("api/public/tenant-registrations", new
        {
            businessName,
            adminEmail = tenantAdminEmail,
            phoneNumber = "0770000000",
            businessRegistrationNumber = $"REG-{DateTime.UtcNow:yyyyMMddHHmmss}",
            businessType
        });

        Console.WriteLine("Logging in as system admin...");
        var sysLogin = await _api.PostAsync<LoginResponseDto>("api/auth/login", new
        {
            email = systemAdminEmail,
            password = systemAdminPassword
        });
        _api.SetToken(sysLogin!.Token);

        Console.WriteLine("Approving tenant registration...");
        var approval = await _api.PostAsync<TenantRegistrationApprovalResponseDto>(
            $"api/system/tenant-registrations/{reg!.RequestId}/approve",
            new { note = "Approved by scenario-seeder tool." });

        var magicToken = ExtractQueryParam(approval!.OnboardingLink, "token")
            ?? throw new InvalidOperationException(
                "No onboarding link/token in the approval response - check FRONTEND_BASE_URL in the backend's .env.");

        _api.ClearToken();

        Console.WriteLine("Verifying onboarding magic link...");
        var verify = await _api.PostAsync<OnboardingMagicLinkVerifyResponseDto>(
            "api/onboarding/magic-link/verify", new { token = magicToken });

        Console.WriteLine("Setting tenant admin password...");
        await _api.PostAsync<object>("api/onboarding/set-password", new
        {
            onboardingToken = verify!.OnboardingToken,
            magicLinkToken = magicToken,
            password = tenantPassword,
            confirmPassword = tenantPassword
        });

        Console.WriteLine("Fetching plans and activating the trial subscription...");
        var plans = await _api.GetAsync<List<PublicPlanSummaryDto>>("api/public/plans") ?? new List<PublicPlanSummaryDto>();
        var trial = plans.FirstOrDefault(p => p.Code == "TRIAL" && p.PriceCents == 0)
            ?? plans.FirstOrDefault(p => p.PriceCents == 0)
            ?? throw new InvalidOperationException("No zero-price plan is available to activate.");

        await _api.PostAsync<OnboardingCheckoutResponseDto>("api/onboarding/subscriptions/checkout", new
        {
            onboardingToken = verify.OnboardingToken,
            planId = trial.Id
        });

        Console.WriteLine("Logging in as tenant admin...");
        var tenantLogin = await _api.PostAsync<LoginResponseDto>("api/auth/login", new
        {
            email = tenantAdminEmail,
            password = tenantPassword
        });
        _api.SetToken(tenantLogin!.Token);
        _api.SetTenant(verify.TenantId);

        return new TenantSession(verify.TenantId, tenantAdminEmail, tenantPassword, tenantLogin.Token);
    }

    private static string? ExtractQueryParam(string? url, string key)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var qIndex = url.IndexOf('?');
        if (qIndex < 0)
        {
            return null;
        }

        var query = url[(qIndex + 1)..];
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
