using finrecon360_backend.Models;

namespace finrecon360_backend.Services
{
    public record TenantProvisionResult(bool Success, string? ConnectionString, string? Error);

    public interface ITenantProvisioner
    {
        Task<TenantProvisionResult> ProvisionAsync(Tenant tenant, CancellationToken cancellationToken = default);
    }
}
