param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$backupDir = Join-Path $repoRoot "backups"

if (-not (Test-Path -LiteralPath $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = Join-Path $backupDir "lensee-$stamp.dump"
}

docker compose exec -T db pg_dump -U lensee_user -d lensee -Fc | Set-Content -Encoding Byte -LiteralPath $OutputPath

Write-Host "Database backup written to $OutputPath"
