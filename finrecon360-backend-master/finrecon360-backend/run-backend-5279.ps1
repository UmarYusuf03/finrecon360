$ErrorActionPreference = 'Stop'

$backendPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $backendPath

$listeners = Get-NetTCPConnection -LocalPort 5279 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty OwningProcess -Unique

if ($listeners) {
  foreach ($pidValue in $listeners) {
    try {
      $proc = Get-Process -Id $pidValue -ErrorAction Stop
      Write-Host "Stopping process on 5279: PID=$($proc.Id), Name=$($proc.ProcessName)"
      Stop-Process -Id $proc.Id -Force
    }
    catch {
      Write-Host "Could not stop PID $pidValue (already exited)."
    }
  }
}

Start-Sleep -Milliseconds 300

$stillListening = Get-NetTCPConnection -LocalPort 5279 -State Listen -ErrorAction SilentlyContinue
if ($stillListening) {
  Write-Error "Port 5279 is still in use. Close the owning app manually and retry."
  exit 1
}

Write-Host "Starting backend on http://localhost:5279 ..."
dotnet run
