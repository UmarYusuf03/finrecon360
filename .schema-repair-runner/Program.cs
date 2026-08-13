using Microsoft.Data.SqlClient;

var controlPlaneConnectionString = Environment.GetEnvironmentVariable("CONTROL_PLANE_CONNECTION_STRING")
    ?? "Server=localhost,1433;Database=FinRecon360;User Id=sa;Password=19884@Zcc;TrustServerCertificate=True;";

var targetTenantId = Environment.GetEnvironmentVariable("TARGET_TENANT_ID")
    ?? "e4cb366a-30b6-4e39-b804-39280cde5648";

var tenantConnectionString = Environment.GetEnvironmentVariable("TARGET_TENANT_CONNECTION_STRING")
    ?? $"Server=localhost,1433;Database=FinRecon360_Tenant_{targetTenantId};User Id=sa;Password=19884@Zcc;TrustServerCertificate=True;";

Console.WriteLine($"Control plane: {controlPlaneConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}");
Console.WriteLine($"Target tenant: {targetTenantId}");
Console.WriteLine($"Tenant DB: FinRecon360_Tenant_{targetTenantId}");

await RepairTenantAsync(tenantConnectionString);

static async Task RepairTenantAsync(string tenantConnectionString)
{
    await using var connection = new SqlConnection(tenantConnectionString);
    await connection.OpenAsync();

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        await ExecuteAsync(connection, transaction, @"
IF OBJECT_ID(N'dbo.ImportedNormalizedRecords', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'ReferenceNumber') IS NULL
    BEGIN
        ALTER TABLE dbo.ImportedNormalizedRecords ADD ReferenceNumber nvarchar(120) NULL;
        PRINT '[✓] Added ReferenceNumber column';
    END
    ELSE
    BEGIN
        PRINT '[•] ReferenceNumber column already exists';
    END

    IF COL_LENGTH(N'dbo.ImportedNormalizedRecords', N'SettlementId') IS NULL
    BEGIN
        ALTER TABLE dbo.ImportedNormalizedRecords ADD SettlementId nvarchar(max) NULL;
        PRINT '[✓] Added SettlementId column';
    END
    ELSE
    BEGIN
        PRINT '[•] SettlementId column already exists';
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ImportedNormalizedRecords')
          AND name = N'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate')
    BEGIN
        CREATE INDEX IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate
            ON dbo.ImportedNormalizedRecords (ReferenceNumber, TransactionDate);
        PRINT '[✓] Created composite index on ReferenceNumber and TransactionDate';
    END
    ELSE
    BEGIN
        PRINT '[•] Composite index already exists';
    END
END
");

        await transaction.CommitAsync();
        Console.WriteLine("Schema repair completed successfully.");
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
{
    await using var command = new SqlCommand(sql, connection, transaction);
    command.CommandTimeout = 120;
    await command.ExecuteNonQueryAsync();
}
