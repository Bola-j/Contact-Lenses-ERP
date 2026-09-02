param(
    [string]$EvidenceRoot = "artifacts/certification",
    [switch]$IncludeRuntime,
    [switch]$IncludeRecovery,
    [switch]$IncludeBrowser,
    [switch]$RequireImageScan,
    [switch]$AllowDirtyWorktree
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDirectory = Join-Path $repoRoot (Join-Path $EvidenceRoot "phase4-10-$runId")
$candidateSha = (& git -C $repoRoot rev-parse HEAD).Trim()
$shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($shell)) { $shell = (Get-Command powershell -ErrorAction Stop).Source }

function Invoke-Logged {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][scriptblock]$Command)
    $logPath = Join-Path $evidenceDirectory "$Name.log"
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $Command 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($exitCode -ne 0) { throw "$Name failed with exit code $exitCode. See $logPath." }
}

function New-VerificationEnvironment {
    param([string]$Path)
    $lines = @(
        "APP_DOMAIN=certification.example.test",
        "TLS_EMAIL=certification@example.test",
        "DB_PASSWORD=CertificationOnlyDatabasePassword-2026!",
        "JWT_SECRET=CertificationOnlyJwtSecretAtLeastThirtyTwoCharacters!",
        "JWT_ISSUER=LenseeCertification",
        "JWT_AUDIENCE=LenseeCertification",
        "FRONTEND_API_BASE_URL=https://certification.example.test",
        "CORS_ALLOWED_ORIGINS=https://certification.example.test",
        "HOSTING_TRUSTED_PROXY_NETWORK=172.16.0.0/12",
        "DATABASE_AUTO_MIGRATE=false",
        "DATABASE_BASELINE_EXISTING_SCHEMA=false"
    )
    [IO.File]::WriteAllLines($Path, [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

function Write-EvidenceManifest {
    $manifestPath = Join-Path $evidenceDirectory "sha256-manifest.txt"
    Get-ChildItem -LiteralPath $evidenceDirectory -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.FullName.Substring($evidenceDirectory.Length + 1))"
        } | Set-Content -LiteralPath $manifestPath
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$verificationEnv = Join-Path ([System.IO.Path]::GetTempPath()) "lensee-production-config-$runId.env"
Push-Location $repoRoot
try {
    "candidate-sha=$candidateSha`nstarted-at=$(Get-Date -Format o)" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")
    $statusBefore = & git status --short
    $statusBefore | Set-Content -LiteralPath (Join-Path $evidenceDirectory "git-status-before.txt")
    if (-not $AllowDirtyWorktree -and @($statusBefore).Count -gt 0) {
        throw "Certification requires a clean worktree. Commit or stash changes before running, or use -AllowDirtyWorktree for non-certifying diagnostics."
    }
    Invoke-Logged -Name "dotnet-restore" -Command { dotnet restore Lensee.slnx }
    Invoke-Logged -Name "dotnet-build-release" -Command { dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror }
    Invoke-Logged -Name "dotnet-format" -Command { dotnet format Lensee.slnx --verify-no-changes --no-restore }
    Invoke-Logged -Name "unit-contract-tests" -Command { dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal" }

    $previousPostgresFlag = $env:LENSEE_RUN_POSTGRES_TESTS
    try {
        $env:LENSEE_RUN_POSTGRES_TESTS = "true"
        Invoke-Logged -Name "postgres-integration-tests" -Command { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal" }
        Invoke-Logged -Name "phase9-catalog-rollback" -Command { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~CatalogMutationTransaction_RollsBackCatalogAuditAndOutboxTogether" }
        Invoke-Logged -Name "phase9-stock-concurrency" -Command { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ConcurrentWarehouseReservations_ReturnOneConflictWithoutPartialLedger" }
        Invoke-Logged -Name "phase9-payment-constraint" -Command { dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~PaymentAggregateTrigger_RollsBackMismatchedDraftTotals" }
    }
    finally {
        if ($null -eq $previousPostgresFlag) { Remove-Item Env:LENSEE_RUN_POSTGRES_TESTS -ErrorAction SilentlyContinue } else { $env:LENSEE_RUN_POSTGRES_TESTS = $previousPostgresFlag }
    }

    Invoke-Logged -Name "npm-ci" -Command { npm ci }
    Invoke-Logged -Name "frontend-guards" -Command { npm run check }
    $previousFrontendApiBase = $env:LENSEE_API_BASE_URL
    try {
        $env:LENSEE_API_BASE_URL = "http://localhost:5000"
        Invoke-Logged -Name "frontend-build" -Command { npm run vercel:build }
    }
    finally {
        if ($null -eq $previousFrontendApiBase) { Remove-Item Env:LENSEE_API_BASE_URL -ErrorAction SilentlyContinue } else { $env:LENSEE_API_BASE_URL = $previousFrontendApiBase }
    }

    $imageTag = "lensee-api:certification-$($candidateSha.Substring(0, 12))"
    Invoke-Logged -Name "image-build" -Command { docker build --pull --tag $imageTag --file backend/Lensee.Host/Dockerfile . }
    $imageId = (& docker image inspect --format '{{.Id}}' $imageTag).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect certification image $imageTag." }
    $imageDigest = (& docker image inspect --format '{{join .RepoDigests "\n"}}' $imageTag).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect certification image digest metadata for $imageTag." }
    @(
        "id=$imageId"
        "digest=$(if ([string]::IsNullOrWhiteSpace($imageDigest)) { 'not-pushed' } else { $imageDigest })"
    ) | Set-Content -LiteralPath (Join-Path $evidenceDirectory "image-identity.txt")
    Invoke-Logged -Name "image-runtime-policy" -Command { docker run --rm --entrypoint /bin/sh $imageTag -ec 'id -u | grep -Eq "^[1-9][0-9]*$"; test -f /usr/share/zoneinfo/Africa/Cairo' }

    $trivy = Get-Command trivy -ErrorAction SilentlyContinue
    if ($null -ne $trivy) {
        Invoke-Logged -Name "image-trivy" -Command { & $trivy.Source image --severity HIGH,CRITICAL --exit-code 1 $imageTag }
    }
    elseif ($RequireImageScan) {
        throw "Trivy is required for this run but is not installed. CI must provide the image-scan evidence."
    }
    else {
        "not-run=Trivy CLI unavailable; CI sbom-and-image-scan remains required" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "image-trivy.txt")
    }

    New-VerificationEnvironment -Path $verificationEnv
    Invoke-Logged -Name "production-config" -Command { & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/verify-production-config.ps1") -EnvFile $verificationEnv -EvidenceDirectory $evidenceDirectory }

    if ($IncludeRuntime) {
        Invoke-Logged -Name "phase0-3-runtime" -Command { & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/verify-phase0-3-runtime.ps1") -EvidenceRoot $EvidenceRoot }
    }
    if ($IncludeBrowser) {
        Invoke-Logged -Name "e2e-setup" -Command { & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/e2e-setup.ps1") }
        $previousSkipWebServer = $env:LENSEE_E2E_SKIP_WEBSERVER
        try {
            $env:LENSEE_E2E_SKIP_WEBSERVER = "1"
            Invoke-Logged -Name "e2e-critical-browser" -Command { npx playwright test e2e/catalog-crm.spec.js e2e/inventory-transfer.spec.js e2e/operations-sales-return-change.spec.js e2e/payments-accounting.spec.js e2e/stocktake.spec.js e2e/notifications-reports.spec.js --project=chromium }
        }
        finally {
            if ($null -eq $previousSkipWebServer) { Remove-Item Env:LENSEE_E2E_SKIP_WEBSERVER -ErrorAction SilentlyContinue } else { $env:LENSEE_E2E_SKIP_WEBSERVER = $previousSkipWebServer }
        }
    }
    if ($IncludeRecovery) {
        Invoke-Logged -Name "synthetic-recovery-drill" -Command { & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/verify-recovery-drill.ps1") -EvidenceRoot $EvidenceRoot }
    }

    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw "git diff --check failed after certification." }
    & git status --short | Set-Content -LiteralPath (Join-Path $evidenceDirectory "git-status-after.txt")
    "completed-at=$(Get-Date -Format o)" | Add-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")
    Write-EvidenceManifest
}
finally {
    if (Test-Path -LiteralPath $verificationEnv) { Remove-Item -LiteralPath $verificationEnv -Force }
    Pop-Location
}
