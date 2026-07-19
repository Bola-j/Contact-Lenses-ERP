$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sqlPath = Join-Path $repoRoot "database/seed-lens-variants-direct.sql"

Get-Content -Raw -LiteralPath $sqlPath |
    docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee

if ($LASTEXITCODE -ne 0) {
    throw "Direct DB lens variant seed failed with exit code $LASTEXITCODE."
}

Write-Host "Direct DB lens variant seed complete."
