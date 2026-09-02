param(
    [ValidatePattern("^[a-z0-9][a-z0-9_-]*$")]
    [string]$ProjectName = "lenseeproduction",
    [string]$EnvFile = ".env",
    [string[]]$AdditionalComposeFiles = @(),
    [string]$EvidenceDirectory,
    [ValidatePattern("^[0-9a-fA-F]{40}$")]
    [string]$ApprovedCandidateSha,
    [string]$CertificationEvidenceDirectory,
    [string]$ReleaseApprovalPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Assert-CleanCheckout {
    $currentCandidateSha = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $currentCandidateSha -notmatch "^[0-9a-f]{40}$") {
        throw "Deployment requires a checked-out Git commit."
    }

    $changes = @(& git -C $repoRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine Git checkout status."
    }
    if ($changes.Count -gt 0) {
        throw "Refusing deployment from a dirty checkout. Commit, stash, or remove all tracked and untracked changes first."
    }

    return $currentCandidateSha.ToLowerInvariant()
}

function Assert-CertificationEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$CandidateSha
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Certification evidence directory '$Path' was not found."
    }

    $evidenceRoot = (Resolve-Path -LiteralPath $Path).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $metadataPath = Join-Path $evidenceRoot "run-metadata.txt"
    $manifestPath = Join-Path $evidenceRoot "sha256-manifest.txt"
    $statusBeforePath = Join-Path $evidenceRoot "git-status-before.txt"
    $statusAfterPath = Join-Path $evidenceRoot "git-status-after.txt"
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $statusBeforePath -PathType Leaf) -or -not (Test-Path -LiteralPath $statusAfterPath -PathType Leaf)) {
        throw "Certification evidence must contain run metadata, before/after Git status, and a SHA-256 manifest."
    }

    $metadata = @{}
    foreach ($line in Get-Content -LiteralPath $metadataPath) {
        if ($line -match "^([^=]+)=(.*)$") {
            $metadata[$Matches[1].Trim()] = $Matches[2].Trim()
        }
    }
    if (-not $metadata.ContainsKey("candidate-sha") -or $metadata["candidate-sha"].ToLowerInvariant() -cne $CandidateSha) {
        throw "Certification evidence candidate SHA does not match the checked-out commit."
    }
    if (-not $metadata.ContainsKey("completed-at") -or [string]::IsNullOrWhiteSpace($metadata["completed-at"])) {
        throw "Certification evidence is incomplete because run-metadata.txt has no completed-at value."
    }
    if (-not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $statusBeforePath -Raw)) -or -not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $statusAfterPath -Raw))) {
        throw "Certification evidence was produced from or left a dirty checkout."
    }

    $evidenceRootPrefix = $evidenceRoot + [System.IO.Path]::DirectorySeparatorChar
    $manifestEntries = @{}
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        if ($line -notmatch "^([0-9a-fA-F]{64})  (.+)$") {
            throw "Certification evidence manifest contains an invalid entry."
        }

        $expectedHash = $Matches[1].ToLowerInvariant()
        $relativePath = $Matches[2]
        if ($manifestEntries.ContainsKey($relativePath)) {
            throw "Certification evidence manifest contains a duplicate entry for '$relativePath'."
        }

        $candidatePath = [System.IO.Path]::GetFullPath((Join-Path $evidenceRoot $relativePath))
        if (-not $candidatePath.StartsWith($evidenceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            throw "Certification evidence manifest references a file outside its evidence directory or a missing file."
        }

        $actualHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $expectedHash) {
            throw "Certification evidence hash mismatch for '$relativePath'."
        }
        $manifestEntries[$relativePath] = $true
    }
    foreach ($requiredEvidenceFile in @("run-metadata.txt", "git-status-before.txt", "git-status-after.txt")) {
        if (-not $manifestEntries.ContainsKey($requiredEvidenceFile)) {
            throw "Certification evidence manifest does not protect $requiredEvidenceFile."
        }
    }

    return (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ReleaseApproval {
    param([Parameter(Mandatory = $true)][string]$CandidateSha)

    if ([string]::IsNullOrWhiteSpace($ApprovedCandidateSha) -or [string]::IsNullOrWhiteSpace($CertificationEvidenceDirectory) -or [string]::IsNullOrWhiteSpace($ReleaseApprovalPath)) {
        throw "Production deployment requires -ApprovedCandidateSha, -CertificationEvidenceDirectory, and -ReleaseApprovalPath."
    }
    if ($ApprovedCandidateSha.ToLowerInvariant() -cne $CandidateSha) {
        throw "Approved candidate SHA does not match the checked-out commit."
    }
    if (-not (Test-Path -LiteralPath $ReleaseApprovalPath -PathType Leaf)) {
        throw "Release approval record '$ReleaseApprovalPath' was not found."
    }

    try {
        $approval = Get-Content -LiteralPath $ReleaseApprovalPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Release approval record must be valid JSON."
    }

    foreach ($property in @("candidateSha", "certificationManifestSha256", "decision", "approver", "approvedAtUtc")) {
        if ($null -eq $approval.$property -or [string]::IsNullOrWhiteSpace([string]$approval.$property)) {
            throw "Release approval record is missing required property '$property'."
        }
    }
    if ([string]$approval.candidateSha -cne $CandidateSha) {
        throw "Release approval record candidate SHA does not match the checked-out commit."
    }
    if ([string]$approval.decision -cne "GO") {
        throw "Release approval record decision must be GO."
    }
    try {
        [DateTimeOffset]::Parse([string]$approval.approvedAtUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind) | Out-Null
    }
    catch {
        throw "Release approval record approvedAtUtc must be an ISO 8601 timestamp."
    }

    $manifestHash = Assert-CertificationEvidence -Path $CertificationEvidenceDirectory -CandidateSha $CandidateSha
    if ([string]$approval.certificationManifestSha256 -notmatch "^[0-9a-fA-F]{64}$" -or [string]$approval.certificationManifestSha256.ToLowerInvariant() -cne $manifestHash) {
        throw "Release approval record does not match the certification evidence manifest."
    }

    return [pscustomobject]@{
        ApprovalRecord = (Resolve-Path -LiteralPath $ReleaseApprovalPath).Path
        CertificationManifestSha256 = $manifestHash
    }
}

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
    $candidateSha = Assert-CleanCheckout
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

    $isCertificationDeployment = $ProjectName -match "^lensee-certification-[a-z0-9_-]+$" -and @($normalizedComposeFiles | ForEach-Object { [System.IO.Path]::GetFileName($_) }) -contains "docker-compose.certification.yml"
    if ($isCertificationDeployment) {
        Write-Host "Certification Compose project detected; release approval gate is not applicable."
    }
    else {
        if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
            throw "Production deployment requires -EvidenceDirectory for deployment evidence."
        }
        $releaseApproval = Assert-ReleaseApproval -CandidateSha $candidateSha
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
        if (-not $isCertificationDeployment) {
            @(
                "candidate-sha=$candidateSha"
                "approval-record=$($releaseApproval.ApprovalRecord)"
                "certification-manifest-sha256=$($releaseApproval.CertificationManifestSha256)"
                "validated-at=$(Get-Date -Format o)"
            ) | Set-Content -LiteralPath (Join-Path $resolvedEvidenceDirectory "release-approval-$ProjectName-$timestamp.txt") -NoNewline
        }
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
