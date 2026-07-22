param(
    [switch]$SeedLocations,
    [switch]$SeedUsers,
    [switch]$SeedLenses,
    [switch]$SkipMigrations
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$powerShellCommand = if ($PSVersionTable.PSEdition -eq "Core") { "pwsh" } else { "powershell" }
Push-Location $repoRoot

try {
    if (-not (Test-Path -LiteralPath ".env")) {
        throw "Missing .env. Copy .env.production.example to .env and fill real values first."
    }

    $envLines = Get-Content -LiteralPath ".env" | Where-Object { $_ -match "^\s*[^#][^=]+=" }
    $envMap = @{}
    foreach ($line in $envLines) {
        $name, $value = $line -split "=", 2
        $envMap[$name.Trim()] = $value.Trim()
    }

    foreach ($required in @("APP_DOMAIN", "TLS_EMAIL", "DB_PASSWORD", "JWT_SECRET", "FRONTEND_API_BASE_URL", "CORS_ALLOWED_ORIGINS")) {
        if (-not $envMap.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($envMap[$required])) {
            throw "Missing required .env value: $required"
        }
    }

    $composeFiles = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml")

    if ($SkipMigrations) {
        $env:DATABASE_AUTO_MIGRATE = "false"
    }

    Write-Host "Building and starting production containers..."
    docker compose @composeFiles up -d --build

    Write-Host "Waiting for database..."
    for ($i = 1; $i -le 30; $i++) {
        docker compose @composeFiles exec -T db pg_isready -U lensee_user -d lensee | Out-Null
        if ($LASTEXITCODE -eq 0) {
            break
        }

        Start-Sleep -Seconds 2
        if ($i -eq 30) {
            throw "Database did not become ready."
        }
    }

    if (-not $SkipMigrations) {
        Write-Host "Waiting for API readiness after startup migrations..."
        for ($i = 1; $i -le 60; $i++) {
            docker compose @composeFiles exec -T lensee.host bash -lc "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 && grep -q '200 OK' <&3"
            if ($LASTEXITCODE -eq 0) {
                break
            }

            Start-Sleep -Seconds 2
            if ($i -eq 60) {
                docker compose @composeFiles logs --tail 120 lensee.host
                throw "API did not become ready after startup migrations."
            }
        }
    }

    if ($SeedLocations) {
        Write-Host "Seeding locations..."
        Get-Content ".\database\seed-locations.sql" -Raw |
            docker compose @composeFiles exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee
    }

    if ($SeedUsers) {
        throw "SeedUsers is a local development flow and is not supported by deploy-prod.ps1. Bootstrap production users with scripts/bootstrap-admin.ps1 using a unique password."
    }

    if ($SeedLenses) {
        Write-Host "Seeding lens catalog..."
        $seedUsername = if ($envMap.ContainsKey("SEED_ADMIN_USERNAME") -and $envMap["SEED_ADMIN_USERNAME"]) { $envMap["SEED_ADMIN_USERNAME"] } else { "admin" }
        $seedPassword = if ($envMap.ContainsKey("SEED_ADMIN_PASSWORD") -and $envMap["SEED_ADMIN_PASSWORD"]) { $envMap["SEED_ADMIN_PASSWORD"] } else { "Admin123!" }
        & $powerShellCommand -ExecutionPolicy Bypass -File ".\scripts\seed-lenses.ps1" -ApiBaseUrl $envMap["FRONTEND_API_BASE_URL"] -Username $seedUsername -Password $seedPassword
        if ($LASTEXITCODE -ne 0) {
            throw "Seed lenses script failed."
        }
    }

    Write-Host "Production deployment is up."
    docker compose @composeFiles ps
}
finally {
    Pop-Location
}

