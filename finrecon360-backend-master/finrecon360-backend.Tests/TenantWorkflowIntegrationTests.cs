using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Dtos.Onboarding;
using finrecon360_backend.Dtos.Public;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace finrecon360_backend.Tests;

public class TenantWorkflowIntegrationTests : IClassFixture<TenantWorkflowTestFactory>
{
    private readonly TenantWorkflowTestFactory _factory;

    public TenantWorkflowIntegrationTests(TenantWorkflowTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Registration_approval_creates_tenant_and_returns_onboarding_link()
    {
        var email = $"tenant.approval.{Guid.NewGuid():N}@test.local";
        using var publicClient = _factory.CreateClient();

        var registrationResponse = await publicClient.PostAsJsonAsync("/api/public/tenant-registrations", new TenantRegistrationCreateRequest
        {
            BusinessName = "Demo Rentals",
            AdminEmail = email,
            PhoneNumber = "+94112223344",
            BusinessRegistrationNumber = $"BRN-{Guid.NewGuid():N}"[..20],
            BusinessType = "VEHICLE_RENTAL"
        });

        Assert.Equal(HttpStatusCode.OK, registrationResponse.StatusCode);
        var registrationBody = await registrationResponse.Content.ReadFromJsonAsync<TenantRegistrationCreateResponse>();
        Assert.NotNull(registrationBody);
        Assert.Equal("PENDING_REVIEW", registrationBody!.Status);

        using var adminClient = await CreateSystemAdminClientAsync("ADMIN.TENANT_REGISTRATIONS.MANAGE");
        var approveResponse = await adminClient.PostAsJsonAsync($"/api/system/tenant-registrations/{registrationBody.RequestId}/approve", new TenantRegistrationReviewRequest
        {
            Note = "approved by integration test"
        });

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approval = await approveResponse.Content.ReadFromJsonAsync<TenantRegistrationApprovalResponse>();
        Assert.NotNull(approval);
        Assert.Equal(registrationBody.RequestId, approval!.RequestId);
        Assert.Equal(email, approval.AdminEmail);
        Assert.False(string.IsNullOrWhiteSpace(approval.OnboardingLink));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var request = await db.TenantRegistrationRequests.AsNoTracking().FirstAsync(r => r.TenantRegistrationRequestId == registrationBody.RequestId);
        Assert.Equal("APPROVED", request.Status);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        var tenantMembership = await db.TenantUsers.AsNoTracking().FirstAsync(tu => tu.UserId == user.UserId);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == tenantMembership.TenantId);
        Assert.Equal(TenantStatus.Pending, tenant.Status);
    }

    [Fact]
    public async Task Approved_tenant_can_verify_magic_link_and_set_password()
    {
        var email = $"tenant.onboarding.{Guid.NewGuid():N}@test.local";
        using var publicClient = _factory.CreateClient();

        var registrationResponse = await publicClient.PostAsJsonAsync("/api/public/tenant-registrations", new TenantRegistrationCreateRequest
        {
            BusinessName = "Demo Stay",
            AdminEmail = email,
            PhoneNumber = "+94113334455",
            BusinessRegistrationNumber = $"BRN-{Guid.NewGuid():N}"[..20],
            BusinessType = "ACCOMMODATION"
        });
        registrationResponse.EnsureSuccessStatusCode();

        var registration = await registrationResponse.Content.ReadFromJsonAsync<TenantRegistrationCreateResponse>();
        Assert.NotNull(registration);

        using var adminClient = await CreateSystemAdminClientAsync("ADMIN.TENANT_REGISTRATIONS.MANAGE");
        var approveResponse = await adminClient.PostAsJsonAsync($"/api/system/tenant-registrations/{registration!.RequestId}/approve", new TenantRegistrationReviewRequest());
        approveResponse.EnsureSuccessStatusCode();

        var approval = await approveResponse.Content.ReadFromJsonAsync<TenantRegistrationApprovalResponse>();
        Assert.NotNull(approval);
        Assert.False(string.IsNullOrWhiteSpace(approval!.OnboardingLink));

        var onboardingUri = new Uri(approval.OnboardingLink!);
        var query = QueryHelpers.ParseQuery(onboardingUri.Query);
        Assert.True(query.TryGetValue("token", out var tokenValues));
        var magicLinkToken = tokenValues.ToString();
        Assert.False(string.IsNullOrWhiteSpace(magicLinkToken));

        var verifyResponse = await publicClient.PostAsJsonAsync("/api/onboarding/magic-link/verify", new OnboardingMagicLinkVerifyRequest
        {
            Token = magicLinkToken
        });
        verifyResponse.EnsureSuccessStatusCode();

        var verifyBody = await verifyResponse.Content.ReadFromJsonAsync<OnboardingMagicLinkVerifyResponse>();
        Assert.NotNull(verifyBody);
        Assert.False(string.IsNullOrWhiteSpace(verifyBody!.OnboardingToken));

        var setPasswordResponse = await publicClient.PostAsJsonAsync("/api/onboarding/set-password", new OnboardingSetPasswordRequest
        {
            OnboardingToken = verifyBody.OnboardingToken,
            MagicLinkToken = magicLinkToken,
            Password = "StrongPass123!",
            ConfirmPassword = "StrongPass123!"
        });
        setPasswordResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public async Task System_admin_can_manage_plans_and_public_only_returns_active()
    {
        using var adminClient = await CreateSystemAdminClientAsync("ADMIN.PLANS.MANAGE");
        var code = $"P{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        var createResponse = await adminClient.PostAsJsonAsync("/api/system/plans", new PlanCreateRequest
        {
            Code = code,
            Name = $"Plan-{code}",
            PriceCents = 25000,
            Currency = "USD",
            DurationDays = 30,
            MaxUsers = 7,
            MaxAccounts = 4
        });
        createResponse.EnsureSuccessStatusCode();

        var createdPlan = await createResponse.Content.ReadFromJsonAsync<PlanSummaryDto>();
        Assert.NotNull(createdPlan);
        Assert.Equal(7, createdPlan!.MaxUsers);
        Assert.Equal(4, createdPlan.MaxAccounts);

        var deactivateResponse = await adminClient.PostAsync($"/api/system/plans/{createdPlan.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        using var publicClient = _factory.CreateClient();
        var publicPlans = await publicClient.GetFromJsonAsync<List<PublicPlanSummaryDto>>("/api/public/plans");
        Assert.NotNull(publicPlans);
        Assert.DoesNotContain(publicPlans!, p => p.Id == createdPlan.Id);

        var activateResponse = await adminClient.PostAsync($"/api/system/plans/{createdPlan.Id}/activate", null);
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        var publicPlansAfterActivate = await publicClient.GetFromJsonAsync<List<PublicPlanSummaryDto>>("/api/public/plans");
        Assert.NotNull(publicPlansAfterActivate);
        Assert.Contains(publicPlansAfterActivate!, p => p.Id == createdPlan.Id && p.MaxUsers == 7 && p.MaxAccounts == 4);
    }

    [Fact]
    public async Task Tenant_suspend_and_reinstate_rules_are_enforced()
    {
        var tenantId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant
            {
                TenantId = tenantId,
                Name = "Suspend Test Tenant",
                Status = TenantStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var adminClient = await CreateSystemAdminClientAsync("ADMIN.TENANTS.MANAGE");

        var suspendResponse = await adminClient.PostAsJsonAsync($"/api/system/tenants/{tenantId}/suspend", new EnforcementActionRequest
        {
            Reason = "suspend for test"
        });
        Assert.Equal(HttpStatusCode.NoContent, suspendResponse.StatusCode);

        var reinstateResponse = await adminClient.PostAsync($"/api/system/tenants/{tenantId}/reinstate", null);
        Assert.Equal(HttpStatusCode.NoContent, reinstateResponse.StatusCode);

        var reinstateAgainResponse = await adminClient.PostAsync($"/api/system/tenants/{tenantId}/reinstate", null);
        Assert.Equal(HttpStatusCode.BadRequest, reinstateAgainResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await verifyDb.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == tenantId);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    private async Task<HttpClient> CreateSystemAdminClientAsync(params string[] permissionCodes)
    {
        var email = $"sysadmin.{Guid.NewGuid():N}@test.local";
        Guid userId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = Guid.NewGuid();

            var user = new User
            {
                UserId = userId,
                Email = email,
                FirstName = "System",
                LastName = "Admin",
                Country = "US",
                Gender = "NA",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true,
                IsActive = true,
                Status = UserStatus.Active,
                UserType = UserType.SystemAdmin,
                IsSystemAdmin = true
            };

            var roleId = Guid.NewGuid();
            var role = new Role
            {
                RoleId = roleId,
                Code = $"SYSROLE-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                Name = $"System Role {Guid.NewGuid():N}"[..20],
                IsSystem = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(user);
            db.Roles.Add(role);
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow
            });

            foreach (var code in permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
                if (permission == null)
                {
                    permission = new Permission
                    {
                        PermissionId = Guid.NewGuid(),
                        Code = code,
                        Name = code,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Permissions.Add(permission);
                }

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permission.PermissionId,
                    GrantedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        return TestWebApplicationFactory.CreateAuthenticatedClient(_factory, userId, email);
    }
}

public class TenantWorkflowTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ITenantProvisioner));
            services.AddSingleton<ITenantProvisioner, StubTenantProvisioner>();

            services.RemoveAll(typeof(ITenantDbProtector));
            services.AddSingleton<ITenantDbProtector, PassThroughTenantDbProtector>();
        });
    }

    private sealed class StubTenantProvisioner : ITenantProvisioner
    {
        public Task<TenantProvisionResult> ProvisionAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            var conn = $"Server=localhost;Database=Tenant_{tenant.TenantId:N};Trusted_Connection=True;TrustServerCertificate=True;";
            return Task.FromResult(new TenantProvisionResult(true, conn, null));
        }
    }

    private sealed class PassThroughTenantDbProtector : ITenantDbProtector
    {
        public string Protect(string plainText) => plainText;

        public string Unprotect(string protectedText) => protectedText;
    }
}
