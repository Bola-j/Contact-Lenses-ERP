param(
    [switch]$Schema,
    [switch]$Seed,
    [switch]$DevSeed
)

$ErrorActionPreference = "Stop"

function Convert-PlainPasswordPlaceholdersToHashes {
    param([Parameter(Mandatory = $true)][string]$Sql)

    return [regex]::Replace($Sql, "<hash:(?<password>[^>]+)>", {
        param($Match)

        New-Pbkdf2PasswordHash -Password $Match.Groups["password"].Value
    })
}

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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

docker compose down --volumes
docker compose up -d db

for ($attempt = 1; $attempt -le 30; $attempt++) {
    docker compose exec -T db pg_isready -U lensee_user -d lensee | Out-Null
    if ($LASTEXITCODE -eq 0) {
        break
    }

    if ($attempt -eq 30) {
        throw "PostgreSQL did not become ready in time."
    }

    Start-Sleep -Seconds 1
}

& (Join-Path $PSScriptRoot "migrate-dev.ps1")

if ($Seed) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql") |
        docker compose exec -T db psql -U lensee_user -d lensee
}

if ($DevSeed) {
    $seedSql = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-dev.sql")
    $seedSql = Convert-PlainPasswordPlaceholdersToHashes -Sql $seedSql
    $seedSql | docker compose exec -T db psql -U lensee_user -d lensee
}

Write-Host "PostgreSQL reset complete. Start the API with: dotnet run --project backend/Lensee.Host/Lensee.Host.csproj"
