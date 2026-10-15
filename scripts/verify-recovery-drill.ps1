param(
    [string]$EvidenceRoot = "artifacts/certification",
    [string]$CandidateSha,
    [string]$ApiImage,
    [string]$FrontendImage,
    [switch]$KeepResources
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$candidateSha = if ([string]::IsNullOrWhiteSpace($CandidateSha)) { (& git -C $repoRoot rev-parse HEAD).Trim() } else { $CandidateSha }
if ($candidateSha -notmatch '^[0-9a-f]{40}$') { throw "Candidate SHA must be a full 40-character commit id." }
$checkedOutSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($checkedOutSha -ne $candidateSha) { throw "Checked-out commit $checkedOutSha does not match requested candidate $candidateSha." }
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDirectory = Join-Path $repoRoot (Join-Path $EvidenceRoot "recovery-$runId")
$sourceProject = "lensee-certification-backup-source"
$recoveryProject = "lensee-certification-recovery"
$sourcePrefix = "lensee_cert_backup_source"
$recoveryPrefix = "lensee_cert_recovery"
$temporaryDirectory = [System.IO.Path]::GetTempPath()
$sourceEnv = Join-Path $temporaryDirectory "lensee-certification-backup-source-$runId.env"
$recoveryEnv = Join-Path $temporaryDirectory "lensee-certification-recovery-$runId.env"
$backupPath = Join-Path $temporaryDirectory "lensee-certification-recovery-$runId.dump"
$script:CurrentStepName = "initialize"
$script:SecretValues = [System.Collections.Generic.List[string]]::new()
$script:CleanupFailures = [System.Collections.Generic.List[string]]::new()
$shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($shell)) { $shell = (Get-Command powershell -ErrorAction Stop).Source }

function Protect-Output {
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) { return "" }
    $protected = $Value
    foreach ($secret in $script:SecretValues) {
        if (-not [string]::IsNullOrEmpty($secret)) { $protected = $protected.Replace($secret, "[REDACTED]") }
    }
    $protected = [regex]::Replace($protected, '(?i)((?:db_password|password|jwt(?:__|_)secret|secret|token)\s*[=:]\s*)("[^"]*"|''[^'']*''|[^\s;,]+)', '$1[REDACTED]')
    return $protected
}

function New-Secret {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $secret = [Convert]::ToBase64String($bytes)
    $script:SecretValues.Add($secret)
    return $secret
}

function Write-EnvFile {
    param([string]$Path, [string]$Prefix, [int]$DbPort, [int]$ApiPort, [string]$ApiImage, [string]$FrontendImage)
    $lines = @(
        "APP_DOMAIN=certification.local",
        "TLS_EMAIL=certification@example.invalid",
        "DB_PASSWORD=$(New-Secret)",
        "JWT_SECRET=$(New-Secret)",
        "JWT_ISSUER=LenseeCertification",
        "JWT_AUDIENCE=LenseeCertification",
        "FRONTEND_API_BASE_URL=http://127.0.0.1:$ApiPort",
        "CORS_ALLOWED_ORIGINS=https://certification.local",
        "HOSTING_TRUSTED_PROXY_NETWORK=172.16.0.0/12",
        "DATABASE_AUTO_MIGRATE=false",
        "DATABASE_BASELINE_EXISTING_SCHEMA=false",
        "CERT_CONTAINER_PREFIX=$Prefix",
        "CERT_DB_PORT=$DbPort",
        "CERT_API_PORT=$ApiPort",
        "CERT_FRONTEND_PORT=0",
        "CERT_CADDY_PORT=0",
        "RECOVERY_CONTAINER_PREFIX=$Prefix",
        "RECOVERY_DB_PORT=$DbPort",
        "RECOVERY_API_PORT=$ApiPort"
    )
    if ([string]::IsNullOrWhiteSpace($ApiImage) -ne [string]::IsNullOrWhiteSpace($FrontendImage)) {
        throw "Both prebuilt API and frontend image references must be supplied together."
    }
    if (-not [string]::IsNullOrWhiteSpace($ApiImage)) {
        $lines += "LENSEE_HOST_IMAGE=$ApiImage"
        $lines += "LENSEE_FRONTEND_IMAGE=$FrontendImage"
    }
    [IO.File]::WriteAllLines($Path, [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

function Invoke-Compose {
    param([string]$Project, [string]$EnvFile, [string[]]$Arguments, [switch]$Recovery)
    $files = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "-f", "docker-compose.certification.yml")
    if ($Recovery) { $files += @("-f", "docker-compose.certification-recovery.yml") }
    & docker compose --project-name $Project --env-file $EnvFile @files @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose returned exit code $LASTEXITCODE for project '$Project'." }
}

function Invoke-Logged {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][scriptblock]$Command)
    $script:CurrentStepName = $Name
    $path = Join-Path $evidenceDirectory "$Name.log"
    $startedAt = Get-Date
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $global:LASTEXITCODE = 0
        & $Command 2>&1 | ForEach-Object {
            $safeLine = Protect-Output -Value ([string]$_)
            Add-Content -LiteralPath $path -Value $safeLine
            Write-Output $safeLine
        }
        $exitCode = $LASTEXITCODE
        if ($null -ne $exitCode -and $exitCode -ne 0) { throw "Native command exited with code $exitCode." }
        "step=$Name`nresult=passed`nstarted-at=$($startedAt.ToString('o'))`ncompleted-at=$((Get-Date).ToString('o'))" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "$Name.result.txt")
    }
    catch {
        $safeError = Protect-Output -Value ([string]$_.Exception.Message)
        Add-Content -LiteralPath $path -Value "SAFE FAILURE: $safeError"
        @(
            "step=$Name"
            "result=failed"
            "started-at=$($startedAt.ToString('o'))"
            "failed-at=$((Get-Date).ToString('o'))"
            "error=$safeError"
        ) | Set-Content -LiteralPath (Join-Path $evidenceDirectory "$Name.result.txt")
        throw "Recovery step '$Name' failed. See $Name.log and $Name.result.txt."
    }
    finally { $ErrorActionPreference = $previousPreference }
}

function Get-Counts {
    param([string]$Project, [string]$EnvFile, [switch]$Recovery)
    $sql = "select 'locations=' || count(*) from inventory.locations union all select 'users=' || count(*) from identity.users union all select 'products=' || count(*) from catalog.products union all select 'skus=' || count(*) from catalog.skus order by 1;"
    $files = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "-f", "docker-compose.certification.yml")
    if ($Recovery) { $files += @("-f", "docker-compose.certification-recovery.yml") }
    $result = & docker compose --project-name $Project --env-file $EnvFile @files exec -T db psql -At -U lensee_user -d lensee -c $sql
    if ($LASTEXITCODE -ne 0) { throw "Could not collect reconciliation counts for project '$Project'." }
    return (($result | Sort-Object) -join "`n")
}

function Write-FailureDiagnostics {
    param([string]$FailedStep)
    $script:CurrentStepName = $FailedStep
    foreach ($diagnostic in @(
        @{ Name = "recovery-failure-compose-ps"; Project = $recoveryProject; Env = $recoveryEnv; Recovery = $true; Args = @("ps", "-a") },
        @{ Name = "recovery-failure-compose-logs"; Project = $recoveryProject; Env = $recoveryEnv; Recovery = $true; Args = @("logs", "--no-color", "--tail", "160") },
        @{ Name = "source-failure-compose-ps"; Project = $sourceProject; Env = $sourceEnv; Recovery = $false; Args = @("ps", "-a") }
    )) {
        if (-not (Test-Path -LiteralPath $diagnostic.Env)) { continue }
        try {
            Invoke-Logged -Name $diagnostic.Name -Command {
                Invoke-Compose -Project $diagnostic.Project -EnvFile $diagnostic.Env -Recovery:$diagnostic.Recovery -Arguments $diagnostic.Args
            } | Out-Null
        }
        catch { }
    }
    $script:CurrentStepName = $FailedStep
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
Push-Location $repoRoot
try {
    @("candidate-sha=$candidateSha", "suite=recovery", "started-at=$(Get-Date -Format o)", "secrets=synthetic-and-not-recorded") |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")

    $script:CurrentStepName = "environment-setup"
    Write-EnvFile -Path $sourceEnv -Prefix $sourcePrefix -DbPort 18381 -ApiPort 15300 -ApiImage $ApiImage -FrontendImage $FrontendImage
    Write-EnvFile -Path $recoveryEnv -Prefix $recoveryPrefix -DbPort 18382 -ApiPort 15301 -ApiImage $ApiImage -FrontendImage $FrontendImage
    "Temporary synthetic environment files created; secret values omitted." |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "environment-setup.log")

    Invoke-Logged -Name "source-deploy" -Command {
        $deploymentArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $repoRoot "scripts/deploy-prod.ps1"), "-ProjectName", $sourceProject, "-EnvFile", $sourceEnv, "-AdditionalComposeFiles", "docker-compose.certification.yml", "-EvidenceDirectory", $evidenceDirectory)
        if ($ApiImage) { $deploymentArguments += "-SkipApplicationBuild" }
        & $shell @deploymentArguments
        if ($LASTEXITCODE -ne 0) { throw "Isolated source deployment returned exit code $LASTEXITCODE." }
    } | Out-Null
    if ($ApiImage) {
        $sourceImageId = (& docker inspect --format '{{.Image}}' "$sourcePrefix-api").Trim()
        $expectedImageId = (& docker image inspect --format '{{.Id}}' $ApiImage).Trim()
        $sourceFrontendImageId = (& docker inspect --format '{{.Image}}' "$sourcePrefix-web").Trim()
        $expectedFrontendImageId = (& docker image inspect --format '{{.Id}}' $FrontendImage).Trim()
        if ($LASTEXITCODE -ne 0 -or $sourceImageId -ne $expectedImageId -or $sourceFrontendImageId -ne $expectedFrontendImageId) {
            throw "Recovery source deployment did not use the exact candidate API image."
        }
        "api-image-id=$sourceImageId`nfrontend-image-id=$sourceFrontendImageId" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-source-image-identities.txt")
    }

    Invoke-Logged -Name "source-seed" -Command {
        $seedSql = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql")) + "`n" + (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-dev.sql"))
        $composeFiles = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "-f", "docker-compose.certification.yml")
        $seedSql | & docker compose --project-name $sourceProject --env-file $sourceEnv @composeFiles exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee
        if ($LASTEXITCODE -ne 0) { throw "Synthetic source seed returned exit code $LASTEXITCODE." }
    } | Out-Null

    Invoke-Logged -Name "source-reconcile" -Command {
        $script:SourceCounts = Get-Counts -Project $sourceProject -EnvFile $sourceEnv
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "source-counts.txt") -Value $script:SourceCounts -NoNewline
        "Synthetic source reconciliation counts recorded."
    } | Out-Null

    Invoke-Logged -Name "backup-create" -Command {
        Invoke-Compose -Project $sourceProject -EnvFile $sourceEnv -Arguments @("exec", "-T", "db", "sh", "-ec", "pg_dump -U lensee_user -d lensee -Fc -f /tmp/certification.dump")
        & docker cp "$sourcePrefix-db`:/tmp/certification.dump" $backupPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $backupPath)) { throw "Could not copy the synthetic backup from the source database." }
        $backup = Get-Item -LiteralPath $backupPath
        $backupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $backupPath).Hash
        "size-bytes=$($backup.Length)`nsha256=$backupHash`ncreated-at=$(Get-Date -Format o)" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "backup-metadata.txt") -NoNewline
        $script:BackupHash = $backupHash
    } | Out-Null

    $restoreStarted = Get-Date
    Invoke-Logged -Name "recovery-database-start" -Command {
        Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("up", "-d", "db")
    } | Out-Null

    Invoke-Logged -Name "recovery-backup-copy" -Command {
        & docker cp $backupPath "$recoveryPrefix-db`:/tmp/certification.dump"
        if ($LASTEXITCODE -ne 0) { throw "Could not copy the synthetic backup into the isolated recovery database." }
    } | Out-Null

    Invoke-Logged -Name "recovery-pg-restore" -Command {
        Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("exec", "-T", "db", "pg_restore", "-U", "lensee_user", "-d", "lensee", "--clean", "--if-exists", "/tmp/certification.dump")
    } | Out-Null

    Invoke-Logged -Name "recovery-migrator" -Command {
        Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("--profile", "migrate", "run", "--rm", "--no-deps", "migrator")
    } | Out-Null

    Invoke-Logged -Name "recovery-api-start" -Command {
        Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("up", "-d", "lensee.host")
    } | Out-Null

    Invoke-Logged -Name "recovery-readiness" -Command {
        $ready = $false
        for ($attempt = 1; $attempt -le 90; $attempt++) {
            try {
                $response = Invoke-RestMethod "http://127.0.0.1:15301/ready" -TimeoutSec 2
                if ($response.status -eq "Healthy") { $ready = $true; "readiness=Healthy; attempts=$attempt"; break }
            }
            catch { }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw "Recovered API did not reach Healthy readiness within 90 seconds." }
        if ($ApiImage) {
            $recoveredImageId = (& docker inspect --format '{{.Image}}' "$recoveryPrefix-api").Trim()
            $expectedImageId = (& docker image inspect --format '{{.Id}}' $ApiImage).Trim()
            if ($LASTEXITCODE -ne 0 -or $recoveredImageId -ne $expectedImageId) {
                throw "Recovered API did not start from the exact candidate image."
            }
            "api-image-id=$recoveredImageId" |
                Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-api-image-identity.txt")
        }
    } | Out-Null

    Invoke-Logged -Name "recovery-reconcile" -Command {
        $recoveryCounts = Get-Counts -Project $recoveryProject -EnvFile $recoveryEnv -Recovery
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-counts.txt") -Value $recoveryCounts -NoNewline
        if ($script:SourceCounts -ne $recoveryCounts) { throw "Synthetic recovery reconciliation counts differ from the source." }
        "reconciliation=matched"
    } | Out-Null

    $elapsed = [Math]::Round(((Get-Date) - $restoreStarted).TotalSeconds, 1)
    if ($elapsed -gt 3600) { throw "Recovery drill exceeded the 60-minute RTO target." }
    @(
        "candidate-sha=$candidateSha"
        "ready=Healthy"
        "reconciliation=matched"
        "rto-seconds=$elapsed"
        "backup-sha256=$script:BackupHash"
        "result=passed"
    ) | Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-summary.txt")
}
catch {
    $failedStep = $script:CurrentStepName
    $safeError = Protect-Output -Value ([string]$_.Exception.Message)
    @(
        "candidate-sha=$candidateSha"
        "suite=recovery"
        "result=failed"
        "failed-step=$failedStep"
        "failed-at=$(Get-Date -Format o)"
        "error=$safeError"
    ) | Set-Content -LiteralPath (Join-Path $evidenceDirectory "failure-summary.txt")
    Write-FailureDiagnostics -FailedStep $failedStep
    throw "Recovery drill failed at '$failedStep': $safeError. See $evidenceDirectory."
}
finally {
    if (-not $KeepResources) {
        foreach ($cleanup in @(
            @{ Name = "cleanup-source"; Project = $sourceProject; Env = $sourceEnv; Recovery = $false },
            @{ Name = "cleanup-recovery"; Project = $recoveryProject; Env = $recoveryEnv; Recovery = $true }
        )) {
            if (-not (Test-Path -LiteralPath $cleanup.Env)) { continue }
            try {
                Invoke-Logged -Name $cleanup.Name -Command {
                    Invoke-Compose -Project $cleanup.Project -EnvFile $cleanup.Env -Recovery:$cleanup.Recovery -Arguments @("down", "--volumes", "--remove-orphans")
                } | Out-Null
            }
            catch {
                $cleanupFailure = Protect-Output -Value ([string]$_.Exception.Message)
                $script:CleanupFailures.Add("$($cleanup.Name): $cleanupFailure")
                Write-Warning $cleanupFailure
            }
        }
        if ($script:CleanupFailures.Count -eq 0) {
            "result=passed`nprojects=$sourceProject,$recoveryProject" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
        }
        else {
            ("result=failed" + [Environment]::NewLine + ($script:CleanupFailures -join [Environment]::NewLine)) |
                Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
        }
        if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
        if (Test-Path -LiteralPath $sourceEnv) { Remove-Item -LiteralPath $sourceEnv -Force }
        if (Test-Path -LiteralPath $recoveryEnv) { Remove-Item -LiteralPath $recoveryEnv -Force }
    }
    elseif ($KeepResources) {
        "cleanup=skipped-by-KeepResources" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
    }
    "completed-at=$(Get-Date -Format o)" | Add-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")
    Pop-Location
}

if (-not $KeepResources -and $script:CleanupFailures.Count -gt 0) {
    throw "Recovery drill completed but isolated resource cleanup failed. See $(Join-Path $evidenceDirectory 'cleanup-result.txt')."
}
