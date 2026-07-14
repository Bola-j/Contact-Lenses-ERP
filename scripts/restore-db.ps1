param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $InputPath)) {
    throw "Backup file not found: $InputPath"
}

if (-not $Force) {
    throw "Restore is destructive. Re-run with -Force after taking a fresh backup."
}

Get-Content -Encoding Byte -LiteralPath $InputPath | docker compose exec -T db pg_restore -U lensee_user -d lensee --clean --if-exists

Write-Host "Database restored from $InputPath"
