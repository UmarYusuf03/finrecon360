using finrecon360_backend.Models;
using finrecon360_backend.Options;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// WHY: Encapsulates the SQL Server tenant database provisioning logic.
    /// During onboarding approval, this creates a new isolated database using a connection string template
    /// and runs schema migrations to initialize tables. By separating provisioning from business logic,
    /// we can unit-test the flow in isolation and swap provisioners for different storage backends
    /// (Azure SQL, PostgreSQL) without touching the approval workflow.
    /// </summary>
    public class DefaultTenantProvisioner : ITenantProvisioner
    {
        private readonly TenantProvisioningOptions _options;
        private readonly ITenantSchemaMigrator _tenantSchemaMigrator;
        private readonly ILogger<DefaultTenantProvisioner> _logger;

        public DefaultTenantProvisioner(
            IOptions<TenantProvisioningOptions> options,
            ITenantSchemaMigrator tenantSchemaMigrator,
            ILogger<DefaultTenantProvisioner> logger)
        {
            _options = options.Value;
            _tenantSchemaMigrator = tenantSchemaMigrator;
            _logger = logger;
        }

        public async Task<TenantProvisionResult> ProvisionAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.DefaultConnectionString))
            {
                return new TenantProvisionResult(false, null, "Tenant DB connection template is not configured.");
            }

            var connectionString = _options.DefaultConnectionString
                .Replace("{tenantId}", tenant.TenantId.ToString(), StringComparison.OrdinalIgnoreCase);

            try
            {
                await EnsureDatabaseExistsAsync(connectionString, cancellationToken);
                await _tenantSchemaMigrator.ApplyAsync(connectionString, cancellationToken);
                return new TenantProvisionResult(true, connectionString, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed provisioning tenant database for tenant {TenantId}", tenant.TenantId);
                return new TenantProvisionResult(false, null, ex.Message);
            }
        }

        private static async Task EnsureDatabaseExistsAsync(string tenantConnectionString, CancellationToken cancellationToken)
        {
            var tenantBuilder = new SqlConnectionStringBuilder(tenantConnectionString);
            var databaseName = tenantBuilder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new InvalidOperationException("Tenant DB template must include a database name.");
            }

            var masterBuilder = new SqlConnectionStringBuilder(tenantConnectionString)
            {
                InitialCatalog = "master"
            };

            await using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"""
                IF DB_ID(N'{databaseName.Replace("'", "''")}') IS NULL
                BEGIN
                    CREATE DATABASE [{databaseName.Replace("]", "]]")}];
                END
                """;

            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
