param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [switch]$SeedLocations
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$hostProject = Join-Path $repoRoot "backend/Lensee.Host/Lensee.Host.csproj"
$migrationBuildRoot = Join-Path $repoRoot "tmp/migrate-prod-build/"
$migrationDll = Join-Path $migrationBuildRoot "Debug/net8.0/Lensee.Host.dll"

function Import-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($EnvironmentName))) {
        return
    }

    $envPath = Join-Path $repoRoot ".env"
    if (-not (Test-Path -LiteralPath $envPath)) {
        return
    }

    $line = Get-Content -LiteralPath $envPath |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Name))\s*=" } |
        Select-Object -First 1

    if (-not $line) {
        return
    }

    $value = ($line -split "=", 2)[1].Trim().Trim('"').Trim("'")
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        [Environment]::SetEnvironmentVariable($EnvironmentName, $value, "Process")
    }
}

Push-Location $repoRoot
try {
    Import-DotEnvValue -Name "JWT_SECRET" -EnvironmentName "Jwt__Secret"
    Import-DotEnvValue -Name "JWT_ISSUER" -EnvironmentName "Jwt__Issuer"
    Import-DotEnvValue -Name "JWT_AUDIENCE" -EnvironmentName "Jwt__Audience"
    Import-DotEnvValue -Name "CORS_ALLOWED_ORIGINS" -EnvironmentName "Cors__AllowedOrigins__0"

    if ([string]::IsNullOrWhiteSpace($env:Jwt__Secret)) {
        throw "Jwt:Secret is not configured. Set JWT_SECRET in .env or Jwt__Secret in the current process before running migrations."
    }

    $env:ConnectionStrings__DefaultConnection = $ConnectionString
    $env:Database__AutoMigrate = "false"
    dotnet build $hostProject -p:BaseOutputPath=$migrationBuildRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Migration host build failed."
    }

    dotnet $migrationDll --migrate
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
