param(
    [string]$EvidenceDirectory = "artifacts/certification/release-config",
    [string]$CandidateSha
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($CandidateSha)) {
    $CandidateSha = (& git -C $repoRoot rev-parse HEAD).Trim()
}
if ($CandidateSha -notmatch '^[0-9a-f]{40}$') {
    throw "Candidate SHA must be a full 40-character commit id."
}
$checkedOutSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($checkedOutSha -ne $CandidateSha) {
    throw "Checked-out commit $checkedOutSha does not match requested candidate $CandidateSha."
}

function New-CertificationSecret {
    param([int]$ByteCount)
    $bytes = New-Object byte[] $ByteCount
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

$resolvedEvidenceDirectory = Join-Path $repoRoot $EvidenceDirectory
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$envFile = Join-Path ([System.IO.Path]::GetTempPath()) "lensee-release-config-$runId.env"
$domain = "certification.example.test"
$envLines = @(
    "APP_DOMAIN=$domain"
    "TLS_EMAIL=certification@example.test"
    "DB_PASSWORD=$(New-CertificationSecret -ByteCount 32)"
    "JWT_SECRET=$(New-CertificationSecret -ByteCount 48)"
    "JWT_ISSUER=LenseeCertification"
    "JWT_AUDIENCE=LenseeCertification"
    "FRONTEND_API_BASE_URL=https://$domain"
    "CORS_ALLOWED_ORIGINS=https://$domain"
    "HOSTING_TRUSTED_PROXY_NETWORK=172.16.0.0/12"
    "DATABASE_AUTO_MIGRATE=false"
    "DATABASE_BASELINE_EXISTING_SCHEMA=false"
)

New-Item -ItemType Directory -Force -Path $resolvedEvidenceDirectory | Out-Null
[IO.File]::WriteAllLines($envFile, [string[]]$envLines, [Text.UTF8Encoding]::new($false))
Push-Location $repoRoot
try {
    try {
        & (Join-Path $PSScriptRoot "verify-production-config.ps1") -EnvFile $envFile -EvidenceDirectory $resolvedEvidenceDirectory
    }
    catch {
        @(
            "candidate-sha=$CandidateSha"
            "suite=release-config"
            "result=failed"
            "completed-at=$(Get-Date -Format o)"
            "error=$($_.Exception.Message)"
        ) | Set-Content -LiteralPath (Join-Path $resolvedEvidenceDirectory "release-config-result.txt")
        throw
    }
    @(
        "candidate-sha=$CandidateSha"
        "suite=release-config"
        "result=passed"
        "completed-at=$(Get-Date -Format o)"
        "secret-values=not-recorded"
    ) | Set-Content -LiteralPath (Join-Path $resolvedEvidenceDirectory "release-config-result.txt")
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $envFile) { Remove-Item -LiteralPath $envFile -Force }
}
