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

Push-Location $repoRoot
try {
    $env:RATE_LIMITING_PERMIT_LIMIT = "5000"
    $env:RATE_LIMITING_WINDOW_SECONDS = "60"
    $env:RATE_LIMITING_QUEUE_LIMIT = "100"

    Write-Host "Resetting Docker database volume..."
    docker compose down --volumes
    docker compose up -d db

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        docker compose exec -T db pg_isready -U lensee_user -d lensee | Out-Null
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 60) { throw "PostgreSQL did not become ready in time." }
        Start-Sleep -Seconds 1
    }

    Write-Host "Applying migrations with the one-shot advisory-lock command..."
    $migrationPassword = $env:DB_PASSWORD
    if ([string]::IsNullOrWhiteSpace($migrationPassword)) { $migrationPassword = "SomeStrongPassword123!" }
    $migrationConnection = "Host=localhost;Port=8181;Database=lensee;Username=lensee_user;Password=$migrationPassword"
    & (Join-Path $repoRoot "scripts/migrate-prod.ps1") -ConnectionString $migrationConnection
    if ($LASTEXITCODE -ne 0) { throw "Migration command failed." }

    Write-Host "Starting API after successful migration validation..."
    docker compose build lensee.host frontend
    docker compose up -d lensee.host frontend

    for ($attempt = 1; $attempt -le 90; $attempt++) {
        try {
            $health = Invoke-RestMethod "http://localhost:5000/health" -TimeoutSec 2
            if ($health.status -eq "Healthy") {
                Write-Host "API health is Healthy."
                break
            }
        } catch {
            if ($attempt -eq 90) { throw "API did not become healthy in time." }
            Start-Sleep -Seconds 1
        }
    }

    Write-Host "Applying deterministic E2E seed data..."
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql") |
        docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee

    $seedSql = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-dev.sql")
    $seedSql = Convert-PlainPasswordPlaceholdersToHashes -Sql $seedSql
    $seedSql | docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee

    $frontend = Invoke-WebRequest "http://localhost:3001" -UseBasicParsing -TimeoutSec 10
    if ($frontend.StatusCode -ne 200) {
        throw "Frontend returned HTTP $($frontend.StatusCode)."
    }

    Write-Host "E2E setup complete."
} finally {
    Pop-Location
}
