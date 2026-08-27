$ErrorActionPreference = "Stop"

function New-Pbkdf2PasswordHash {
    param([Parameter(Mandatory = $true)][string]$Password)

    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt)
    $rng.Dispose()

    $pbkdf2 = New-Object Security.Cryptography.Rfc2898DeriveBytes($Password, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $key = $pbkdf2.GetBytes(32)

    return "pbkdf2-sha256.100000.{0}.{1}" -f [Convert]::ToBase64String($salt), [Convert]::ToBase64String($key)
}

function Convert-PlainPasswordPlaceholdersToHashes {
    param([Parameter(Mandatory = $true)][string]$Sql)

    return [regex]::Replace($Sql, "<hash:(?<password>[^>]+)>", {
        param($Match)
        New-Pbkdf2PasswordHash -Password $Match.Groups["password"].Value
    })
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectName = "lensee-e2e"
$composeFiles = @("-p", $projectName, "-f", "docker-compose.yml", "-f", "docker-compose.e2e.yml")
$databaseVolume = "lensee-e2e-pgdata"
$dataProtectionVolume = "lensee-e2e-data-protection"
$apiPort = if ([string]::IsNullOrWhiteSpace($env:LENSEE_E2E_API_PORT)) { "55000" } else { $env:LENSEE_E2E_API_PORT }
$frontendPort = if ([string]::IsNullOrWhiteSpace($env:LENSEE_E2E_FRONTEND_PORT)) { "53001" } else { $env:LENSEE_E2E_FRONTEND_PORT }
$apiUrl = "http://127.0.0.1:$apiPort"
$frontendUrl = "http://127.0.0.1:$frontendPort"

function Invoke-E2ECompose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose @composeFiles @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "E2E docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-E2ESql {
    param(
        [Parameter(Mandatory = $true)][string]$Sql,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $Sql | docker compose @composeFiles exec -T db psql -v ON_ERROR_STOP=1 -U lensee_e2e_user -d lensee_e2e
    if ($LASTEXITCODE -ne 0) {
        throw "E2E SQL seed '$Label' failed with exit code $LASTEXITCODE."
    }
}

function Wait-ForE2EDatabase {
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        & docker compose @composeFiles exec -T db pg_isready -U lensee_e2e_user -d lensee_e2e | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 1
    }

    Invoke-E2ECompose -Arguments @("logs", "--tail", "120", "db")
    throw "E2E PostgreSQL did not become ready in time."
}

function Wait-ForE2EApi {
    for ($attempt = 1; $attempt -le 90; $attempt++) {
        try {
            $ready = Invoke-RestMethod "$apiUrl/ready" -TimeoutSec 2
            if ($ready.status -eq "Healthy") {
                return
            }
        }
        catch {
            # The API is expected to be unavailable until the migrator has completed.
        }

        Start-Sleep -Seconds 1
    }

    Invoke-E2ECompose -Arguments @("logs", "--tail", "120", "lensee.host")
    throw "E2E API did not become ready in time."
}

Push-Location $repoRoot
try {
    if ($env:ASPNETCORE_ENVIRONMENT -eq "Production") {
        throw "Refusing to reset E2E data while ASPNETCORE_ENVIRONMENT=Production."
    }

    Write-Host "E2E target project: $projectName"
    Write-Host "E2E target database: lensee_e2e (volume: $databaseVolume)"
    Write-Host "E2E target Data Protection volume: $dataProtectionVolume"
    Write-Host "E2E target API: $apiUrl; frontend: $frontendUrl"
    Invoke-E2ECompose -Arguments @("config", "--quiet")

    Write-Host "Resetting only the dedicated E2E Compose project..."
    Invoke-E2ECompose -Arguments @("down", "--volumes", "--remove-orphans")
    Invoke-E2ECompose -Arguments @("build", "lensee.host", "migrator", "frontend")
    Invoke-E2ECompose -Arguments @("up", "-d", "db")
    Wait-ForE2EDatabase

    Write-Host "Applying migrations with the dedicated one-shot migrator..."
    Invoke-E2ECompose -Arguments @("--profile", "migrate", "run", "--rm", "--no-deps", "migrator")

    Write-Host "Starting the isolated API and frontend..."
    Invoke-E2ECompose -Arguments @("up", "-d", "lensee.host", "frontend")
    Wait-ForE2EApi

    Write-Host "Applying deterministic shared and E2E-only seed data..."
    Invoke-E2ESql -Sql (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql")) -Label "locations"

    $sharedSeedSql = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-dev.sql")
    $sharedSeedSql = Convert-PlainPasswordPlaceholdersToHashes -Sql $sharedSeedSql
    Invoke-E2ESql -Sql $sharedSeedSql -Label "shared development data"

    $e2eSeedSql = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-e2e.sql")
    $e2eSeedSql = Convert-PlainPasswordPlaceholdersToHashes -Sql $e2eSeedSql
    Invoke-E2ESql -Sql $e2eSeedSql -Label "E2E users"

    $frontend = Invoke-WebRequest $frontendUrl -UseBasicParsing -TimeoutSec 10
    if ($frontend.StatusCode -ne 200) {
        throw "E2E frontend returned HTTP $($frontend.StatusCode)."
    }

    Write-Host "E2E setup complete for project $projectName."
}
finally {
    Pop-Location
}
