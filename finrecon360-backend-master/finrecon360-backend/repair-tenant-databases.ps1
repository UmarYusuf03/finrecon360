#!/usr/bin/env pwsh

# =====================================================================
# PowerShell Script: Bulk Repair Tenant Databases
# =====================================================================
# This script automates the repair process across multiple tenant databases.
#
# PREREQUISITES:
# 1. SQL Server must be running on localhost,1433
# 2. SqlServer PowerShell module must be installed:
#    Install-Module -Name SqlServer -AllowClobber -Force -Confirm:$false
#
# USAGE:
#   .\repair-tenant-databases.ps1 -TenantId "e4cb366a-30b6-4e39-b804-39280cde5648" -Verbose
#   OR
#   .\repair-tenant-databases.ps1 -RepairAll -Verbose
# =====================================================================

param(
    [string]$TenantId,
    [switch]$RepairAll,
    [string]$SqlServer = "localhost,1433",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = "19884@Zcc"
)

$ErrorActionPreference = "Stop"

function Test-SqlModule {
    try {
        Import-Module SqlServer -ErrorAction Stop
        Write-Verbose "SqlServer module loaded successfully"
        return $true
    } catch {
        Write-Error "SqlServer PowerShell module not found. Install it with:"
        Write-Error "Install-Module -Name SqlServer -AllowClobber -Force -Confirm:`$false"
        return $false
    }
}

function Get-AffectedTenants {
    param([string]$SqlServer, [string]$User, [string]$Password)
    
    Write-Verbose "Querying control plane database for affected tenants..."
    
    $query = @"
SELECT TOP 100
    t.TenantId,
    t.BusinessName,
    'FinRecon360_Tenant_' + CAST(t.TenantId AS NVARCHAR(36)) as DatabaseName,
    CASE 
        WHEN COL_LENGTH('FinRecon360_Tenant_' + CAST(t.TenantId AS NVARCHAR(36)) + '.ImportedNormalizedRecords', 'ReferenceNumber') IS NULL 
        THEN 'MISSING' 
        ELSE 'OK' 
    END as ReferenceNumberStatus
FROM Tenants t
INNER JOIN TenantDatabases td ON t.TenantId = td.TenantId
WHERE t.Status = 'Active' AND td.Status = 'Ready'
ORDER BY t.CreatedAt DESC
"@
    
    try {
        $results = Invoke-SqlCmd `
            -Server $SqlServer `
            -Database "FinRecon360" `
            -User $User `
            -Password $Password `
            -Query $query `
            -ErrorAction Stop
        
        return $results
    } catch {
        Write-Error "Failed to query control plane database: $_"
        return @()
    }
}

function Repair-TenantDatabase {
    param(
        [string]$DatabaseName,
        [string]$SqlServer,
        [string]$User,
        [string]$Password
    )
    
    Write-Verbose "Repairing database: $DatabaseName"
    
    $repairScript = @"
USE [$DatabaseName];

-- Add ReferenceNumber column if missing
IF COL_LENGTH('ImportedNormalizedRecords', 'ReferenceNumber') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [ReferenceNumber] nvarchar(120) NULL;
    PRINT 'Added ReferenceNumber column';
END

-- Add SettlementId column if missing
IF COL_LENGTH('ImportedNormalizedRecords', 'SettlementId') IS NULL
BEGIN
    ALTER TABLE [ImportedNormalizedRecords]
    ADD [SettlementId] nvarchar(max) NULL;
    PRINT 'Added SettlementId column';
END

-- Create index if not exists
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate'
)
BEGIN
    CREATE INDEX [IX_ImportedNormalizedRecords_ReferenceNumber_TransactionDate] 
    ON [ImportedNormalizedRecords] ([ReferenceNumber], [TransactionDate]);
    PRINT 'Created index';
END
"@
    
    try {
        Invoke-SqlCmd `
            -Server $SqlServer `
            -User $User `
            -Password $Password `
            -Query $repairScript `
            -ErrorAction Stop
        
        Write-Host "✓ Successfully repaired: $DatabaseName" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "✗ Failed to repair $DatabaseName : $_" -ForegroundColor Red
        return $false
    }
}

# Main execution
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Tenant Database Schema Repair Utility" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for SqlServer module
if (-not (Test-SqlModule)) {
    exit 1
}

# Create secure password
$SecurePassword = ConvertTo-SecureString $SqlPassword -AsPlainText -Force

if ($TenantId) {
    # Repair specific tenant
    $DatabaseName = "FinRecon360_Tenant_$TenantId"
    Write-Host "Repairing specific tenant: $TenantId" -ForegroundColor Yellow
    Write-Host "Database: $DatabaseName" -ForegroundColor Yellow
    
    if (Repair-TenantDatabase -DatabaseName $DatabaseName -SqlServer $SqlServer -User $SqlUser -Password $SqlPassword) {
        Write-Host "Repair completed successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next steps:" -ForegroundColor Cyan
        Write-Host "1. Restart BankReconciliationHostedService"
        Write-Host "2. Restart JournalPostingHostedService"
        Write-Host "3. Monitor application logs"
    }
} elseif ($RepairAll) {
    # Repair all affected tenants
    Write-Host "Scanning for affected tenants..." -ForegroundColor Yellow
    $affectedTenants = Get-AffectedTenants -SqlServer $SqlServer -User $SqlUser -Password $SqlPassword | Where-Object { $_.ReferenceNumberStatus -eq "MISSING" }
    
    if ($affectedTenants.Count -eq 0) {
        Write-Host "No affected tenants found!" -ForegroundColor Green
        exit 0
    }
    
    Write-Host "Found $($affectedTenants.Count) affected tenant(s)" -ForegroundColor Yellow
    Write-Host ""
    
    $repaired = 0
    $failed = 0
    
    foreach ($tenant in $affectedTenants) {
        if (Repair-TenantDatabase -DatabaseName $tenant.DatabaseName -SqlServer $SqlServer -User $SqlUser -Password $SqlPassword) {
            $repaired++
        } else {
            $failed++
        }
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Repair Summary" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Successfully repaired: $repaired" -ForegroundColor Green
    Write-Host "Failed: $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Manually verify the repairs in SQL Server"
    Write-Host "2. Restart background services"
    Write-Host "3. Monitor application logs for errors"
    
} else {
    Write-Host "ERROR: Please specify either -TenantId or -RepairAll" -ForegroundColor Red
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\repair-tenant-databases.ps1 -TenantId ""YOUR-TENANT-ID""" -ForegroundColor Yellow
    Write-Host "  .\repair-tenant-databases.ps1 -RepairAll" -ForegroundColor Yellow
    exit 1
}
