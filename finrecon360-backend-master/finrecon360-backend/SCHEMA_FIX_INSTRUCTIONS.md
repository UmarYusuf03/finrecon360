# Database Schema Mismatch: Missing ReferenceNumber Column

## Error Analysis

### Error Message
```
Microsoft.Data.SqlClient.SqlException (0x80131904): Invalid column name 'ReferenceNumber'
Error Number:207,State:1,Class:16
```

### Where It Occurs
- **BankReconciliationHostedService** - Processing tenant `e4cb366a-30b6-4e39-b804-39280cde5648`
- **JournalPostingHostedService** - Same tenant
- **Time**: Occurs during background service cycles

### Root Cause
The `ImportedNormalizedRecords` table in the tenant database is missing the `ReferenceNumber` and `SettlementId` columns that the reconciliation code expects.

**Why this happens**: Tenant databases may have been provisioned with an older version of the code/schema, and the migration/setup script that adds these columns was not applied.

---

## Code References

### BankStatementReconciliationWorker.cs (Line 292)
```csharp
private static string? ResolveSettlementKey(ImportedNormalizedRecord record)
{
    // Settlement key is AccountCode + ReferenceNumber (both required for Level-4 matching)
    var accountCode = record.AccountCode?.Trim();
    var referenceNumber = record.ReferenceNumber?.Trim();  // ← FAILS HERE
    
    if (string.IsNullOrWhiteSpace(accountCode) || string.IsNullOrWhiteSpace(referenceNumber))
        return null;
    
    return $"{accountCode}|{referenceNumber}";
}
```

### Query Context (Line 89-90)
```csharp
var gatewayRecords = await QueryCommittedBySourceType(tenantDb, "GATEWAY", cancellationToken);
var bankRecords = await QueryCommittedBySourceType(tenantDb, "BANK", cancellationToken);
```

The `.ToListAsync()` call triggers SQL execution, and EF Core tries to SELECT the `ReferenceNumber` column which doesn't exist.

---

## Database Info

### Affected Tenant
- **Tenant ID**: `e4cb366a-30b6-4e39-b804-39280cde5648`
- **Tenant Database**: `FinRecon360_Tenant_e4cb366a-30b6-4e39-b804-39280cde5648`

### Connection Details
From `.env` file:
- **Server**: `localhost,1433`
- **User**: `sa`
- **Password**: `19884@Zcc`
- **Template**: `FinRecon360_Tenant_{tenantId}`

---

## Solution

### Step 1: Diagnose All Affected Tenants

Run on the **control plane database** (`FinRecon360`):
```sql
-- diagnose_missing_columns.sql
-- This identifies which tenant databases are missing required columns
```

### Step 2: Apply Schema Fix

For each affected tenant database, execute:
```sql
-- repair_missing_import_columns.sql
-- This adds the missing ReferenceNumber and SettlementId columns
```

### Step 3: Verify and Restart

After applying the repair script:
1. Verify the columns were added:
   ```sql
   SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS 
   WHERE TABLE_NAME = 'ImportedNormalizedRecords'
   ORDER BY COLUMN_NAME;
   ```

2. Restart the background services:
   - `BankReconciliationHostedService`
   - `JournalPostingHostedService`

---

## Files Provided

1. **repair_missing_import_columns.sql**
   - Adds `ReferenceNumber` column (nvarchar(120), nullable)
   - Adds `SettlementId` column (nvarchar(max), nullable)
   - Creates composite index on (ReferenceNumber, TransactionDate)
   - Safe to run multiple times (checks if columns exist)

2. **diagnose_missing_columns.sql**
   - Scans all tenant databases
   - Identifies which ones are affected
   - Generates list of databases needing repair

3. **SCHEMA_FIX_INSTRUCTIONS.txt** (this file)
   - Explains the issue
   - Documents the fix procedure

---

## Prevention

To prevent this issue in the future:

1. **Ensure database provisioning always applies the latest schema**
   - Review `TenantDbContext.OnModelCreating()` to ensure all required columns are configured
   - Update tenant database provisioning scripts to run all migrations

2. **Validate schema during tenant onboarding**
   - Add a schema validation check when a tenant is marked as "Ready"

3. **Version migrations**
   - Track which schema version each tenant database has

---

## Related Issues

- **Payment Gateway Integration**: Separate issue with PayHere redirect
- **Missing Tenant Tables**: Use `repair_missing_tenant_tables.sql` for control plane issues

---

## Support

If the repair script doesn't resolve the issue, check:
1. Is the tenant database accessible?
2. Are there foreign key constraints preventing column addition?
3. Have you restarted the services after running the repair?
