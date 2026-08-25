param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [switch]$SeedLocations
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$hostProject = Join-Path $repoRoot "backend/Lensee.Host/Lensee.Host.csproj"

Push-Location $repoRoot
try {
    $env:ConnectionStrings__DefaultConnection = $ConnectionString
    $env:Database__AutoMigrate = "false"
    dotnet run --project $hostProject -- --migrate
    if ($LASTEXITCODE -ne 0) {
        throw "Migration command failed."
    }
} finally {
    Pop-Location
}

if ($SeedLocations) {
    Write-Host "SeedLocations is intended for Docker/local production-style runs. Apply database/seed-locations.sql with psql using the same connection string."
}

Write-Host "Production migration flow complete."
