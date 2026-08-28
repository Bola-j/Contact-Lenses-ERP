param(
    [ValidatePattern("^[a-z0-9][a-z0-9_-]*$")]
    [string]$ProjectName = "lenseeproduction",
    [string]$EnvFile = ".env",
    [string[]]$AdditionalComposeFiles = @(),
    [string]$EvidenceDirectory
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & docker compose @composeFiles @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Wait-ForDatabase {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & docker compose @composeFiles exec -T db pg_isready -U lensee_user -d lensee | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 2
    }

    Invoke-Compose -Arguments @("logs", "--tail", "120", "db")
    throw "Database did not become ready."
}

function Wait-ForHealthyService {
    param([Parameter(Mandatory = $true)][string]$Service)

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        $containerId = ((@(& docker compose @composeFiles ps -q $Service) -join "").Trim())
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($containerId)) {
            $health = (& docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $containerId).Trim()
            if ($LASTEXITCODE -eq 0 -and $health -eq "healthy") {
                return
            }
        }

        Start-Sleep -Seconds 2
    }

    Invoke-Compose -Arguments @("logs", "--tail", "120", $Service)
    throw "$Service did not reach Docker health status 'healthy'."
}

function Write-ImageEvidence {
    param([Parameter(Mandatory = $true)][string]$Service)

    $containerId = ((@(& docker compose @composeFiles ps -q $Service) -join "").Trim())
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        Write-Warning "No container ID found for $Service."
        return
    }

    [string]$imageId = & docker inspect --format '{{.Image}}' $containerId
    $imageId = $imageId.Trim()
    [string]$digest = & docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' $imageId 2>$null
    $digest = $digest.Trim()
    Write-Host "$Service image ID: $imageId"
    Write-Host "$Service image digest: $(if ($digest) { $digest } else { 'not published locally' })"
}

Push-Location $repoRoot
try {
    $resolvedEnvFile = if ([System.IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $repoRoot $EnvFile }
    if (-not (Test-Path -LiteralPath $resolvedEnvFile)) {
        throw "Missing environment file '$EnvFile'. Copy .env.production.example to .env and fill real values first."
    }

    $resolvedEnvFile = (Resolve-Path -LiteralPath $resolvedEnvFile).Path
    $composeFiles = @("--project-name", $ProjectName, "--env-file", $resolvedEnvFile, "-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml")
    # A child PowerShell process passes a string-array script parameter as one
    # comma-delimited argument. Normalize either form before resolving files.
    $normalizedComposeFiles = @($AdditionalComposeFiles | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($composeFile in $normalizedComposeFiles) {
        $resolvedComposeFile = if ([System.IO.Path]::IsPathRooted($composeFile)) { $composeFile } else { Join-Path $repoRoot $composeFile }
        if (-not (Test-Path -LiteralPath $resolvedComposeFile)) {
            throw "Additional Compose file '$composeFile' was not found."
        }

        $composeFiles += @("-f", (Resolve-Path -LiteralPath $resolvedComposeFile).Path)
    }

    $envLines = Get-Content -LiteralPath $resolvedEnvFile | Where-Object { $_ -match "^\s*[^#][^=]+=" }
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

    Invoke-Compose -Arguments @("config", "--quiet")

    Write-Host "Pulling external production images..."
    Invoke-Compose -Arguments @("pull", "db", "caddy")
    Write-Host "Building application images..."
    Invoke-Compose -Arguments @("build", "lensee.host", "migrator", "frontend")

    Write-Host "Starting PostgreSQL only..."
    Invoke-Compose -Arguments @("up", "-d", "db")
    Wait-ForDatabase

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        $resolvedEvidenceDirectory = if ([System.IO.Path]::IsPathRooted($EvidenceDirectory)) { $EvidenceDirectory } else { Join-Path $repoRoot $EvidenceDirectory }
        New-Item -ItemType Directory -Force -Path $resolvedEvidenceDirectory | Out-Null
        $migrationLog = Join-Path $resolvedEvidenceDirectory "migrator-$ProjectName-$timestamp.log"
    }
    else {
        $migrationLog = Join-Path ([System.IO.Path]::GetTempPath()) "lensee-migrator-$timestamp.log"
    }
    Write-Host "Running the explicit migrator. Log: $migrationLog"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & docker compose @composeFiles --profile migrate run --rm --no-deps migrator 2>&1 | Tee-Object -FilePath $migrationLog
        $migrationExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($migrationExitCode -ne 0) {
        Write-Host "Migration log retained at: $migrationLog"
        throw "Migrator failed with exit code $migrationExitCode. API, frontend, and proxy were not started."
    }

    Write-Host "Starting API after a successful migration..."
    Invoke-Compose -Arguments @("up", "-d", "lensee.host")
    Wait-ForHealthyService lensee.host

    Write-Host "Starting frontend, then proxy..."
    Invoke-Compose -Arguments @("up", "-d", "frontend")
    Invoke-Compose -Arguments @("up", "-d", "caddy")

    Write-Host "Production deployment is up. Migration evidence: $migrationLog"
    Invoke-Compose -Arguments @("ps")
    foreach ($service in @("db", "lensee.host", "frontend", "caddy")) {
        Write-ImageEvidence $service
    }
}
finally {
    Pop-Location
}
