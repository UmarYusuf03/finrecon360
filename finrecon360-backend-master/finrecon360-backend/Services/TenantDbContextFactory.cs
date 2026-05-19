using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public interface ITenantDbContextFactory
    {
        Task<TenantDbContext> CreateAsync(Guid tenantId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// WHY: This factory creates instances of TenantDbContext dynamically at runtime. 
    /// Standard Dependency Injection for DbContext expects a static connection string, 
    /// so this factory pattern is required to intercept the request and inject the resolved tenant string.
    /// </summary>
    public class TenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly ITenantDbResolver _resolver;
        private readonly ITenantSchemaMigrator _schemaMigrator;

        public TenantDbContextFactory(ITenantDbResolver resolver, ITenantSchemaMigrator schemaMigrator)
        {
            _resolver = resolver;
            _schemaMigrator = schemaMigrator;
        }

        public async Task<TenantDbContext> CreateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var connectionString = await _resolver.ResolveConnectionStringAsync(tenantId, cancellationToken);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Tenant database connection not found for tenant {tenantId}.");
            }

            try
            {
                // WHY: Guarantees that any newly provisioned database or newly deployed schema version 
                // is automatically updated to match the active EF Core models before the connection is handed off.
                await _schemaMigrator.ApplyAsync(connectionString, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to prepare tenant database for tenant {tenantId}.", ex);
            }

            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new TenantDbContext(options);
        }
    }
}
