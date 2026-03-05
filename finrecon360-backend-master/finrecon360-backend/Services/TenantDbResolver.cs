using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using finrecon360_backend.Options;
using System.Security.Cryptography;

namespace finrecon360_backend.Services
{
    public interface ITenantDbResolver
    {
        Task<string?> ResolveConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default);
    }

    public class TenantDbResolver : ITenantDbResolver
    {
        private readonly AppDbContext _dbContext;
        private readonly ITenantDbProtector _protector;
        private readonly TenantProvisioningOptions _provisioningOptions;
        private readonly ILogger<TenantDbResolver> _logger;

        public TenantDbResolver(
            AppDbContext dbContext,
            ITenantDbProtector protector,
            IOptions<TenantProvisioningOptions> provisioningOptions,
            ILogger<TenantDbResolver> logger)
        {
            _dbContext = dbContext;
            _protector = protector;
            _provisioningOptions = provisioningOptions.Value;
            _logger = logger;
        }

        public async Task<string?> ResolveConnectionStringAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var record = await _dbContext.TenantDatabases
                .Where(t => t.TenantId == tenantId && t.Status == Models.TenantDatabaseStatus.Ready)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (record == null)
            {
                return null;
            }

            try
            {
                return _protector.Unprotect(record.EncryptedConnectionString);
            }
            catch (CryptographicException ex)
            {
                var template = _provisioningOptions.DefaultConnectionString;
                if (string.IsNullOrWhiteSpace(template))
                {
                    _logger.LogError(ex, "Failed to decrypt tenant DB connection and no TENANT_DB_TEMPLATE is configured for tenant {TenantId}.", tenantId);
                    throw;
                }

                var fallbackConnection = template.Replace("{tenantId}", tenantId.ToString(), StringComparison.OrdinalIgnoreCase);
                record.EncryptedConnectionString = _protector.Protect(fallbackConnection);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(ex, "Recovered tenant DB connection for tenant {TenantId} using TENANT_DB_TEMPLATE fallback and rotated encrypted value.", tenantId);
                return fallbackConnection;
            }
        }
    }
}
