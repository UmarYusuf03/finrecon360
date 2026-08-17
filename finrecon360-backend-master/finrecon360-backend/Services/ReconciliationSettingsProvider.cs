using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public interface IReconciliationSettingsProvider
    {
        /// <summary>
        /// Loads the tenant's reconciliation tolerances. Falls back to the hardcoded defaults
        /// (0.01 amount tolerance, 1 day date tolerance — the values every worker used to hardcode
        /// independently) when a tenant has no settings row yet, so pre-existing tenants don't
        /// break before their row is seeded.
        /// </summary>
        Task<ReconciliationSettings> GetAsync(TenantDbContext tenantDb, CancellationToken cancellationToken = default);
    }

    public class ReconciliationSettingsProvider : IReconciliationSettingsProvider
    {
        private static readonly ReconciliationSettings Defaults = new()
        {
            AmountTolerance = 0.01m,
            DateToleranceDays = 1,
        };

        public async Task<ReconciliationSettings> GetAsync(TenantDbContext tenantDb, CancellationToken cancellationToken = default)
        {
            var settings = await tenantDb.ReconciliationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            return settings ?? Defaults;
        }
    }
}
