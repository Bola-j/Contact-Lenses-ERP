param(
    [string]$EvidenceRoot = "artifacts/certification",
    [switch]$KeepResources
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
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
$shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($shell)) { $shell = (Get-Command powershell -ErrorAction Stop).Source }

function New-Secret {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Write-EnvFile {
    param([string]$Path, [string]$Prefix, [int]$DbPort, [int]$ApiPort)
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
    [IO.File]::WriteAllLines($Path, [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

function Invoke-Compose {
    param([string]$Project, [string]$EnvFile, [string[]]$Arguments, [switch]$Recovery)
    $files = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "-f", "docker-compose.certification.yml")
    if ($Recovery) { $files += @("-f", "docker-compose.certification-recovery.yml") }
    & docker compose --project-name $Project --env-file $EnvFile @files @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($Arguments -join ' ') failed for $Project." }
}

function Invoke-Logged {
    param([string]$Name, [scriptblock]$Command)
    $path = Join-Path $evidenceDirectory "$Name.log"
    & $Command 2>&1 | Tee-Object -FilePath $path
    if ($LASTEXITCODE -ne 0) { throw "$Name failed. See $path." }
}

function Get-Counts {
    param([string]$Project, [string]$EnvFile, [switch]$Recovery)
    $sql = "select 'locations=' || count(*) from inventory.locations union all select 'users=' || count(*) from identity.users union all select 'products=' || count(*) from catalog.products union all select 'skus=' || count(*) from catalog.skus order by 1;"
    $files = @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "-f", "docker-compose.certification.yml")
    if ($Recovery) { $files += @("-f", "docker-compose.certification-recovery.yml") }
    $result = & docker compose --project-name $Project --env-file $EnvFile @files exec -T db psql -At -U lensee_user -d lensee -c $sql
    if ($LASTEXITCODE -ne 0) { throw "Could not collect reconciliation counts for $Project." }
    return (($result | Sort-Object) -join "`n")
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
Push-Location $repoRoot
try {
    Write-EnvFile -Path $sourceEnv -Prefix $sourcePrefix -DbPort 18381 -ApiPort 15300
    Write-EnvFile -Path $recoveryEnv -Prefix $recoveryPrefix -DbPort 18382 -ApiPort 15301

    Invoke-Logged -Name "source-deploy" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $sourceProject -EnvFile $sourceEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory
    }
    $seedSql = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql")) + "`n" + (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-dev.sql"))
    $seedSql | docker compose --project-name $sourceProject --env-file $sourceEnv -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml -f docker-compose.certification.yml exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee
    if ($LASTEXITCODE -ne 0) { throw "Synthetic source seed failed." }
    $sourceCounts = Get-Counts -Project $sourceProject -EnvFile $sourceEnv
    Set-Content -LiteralPath (Join-Path $evidenceDirectory "source-counts.txt") -Value $sourceCounts -NoNewline

    Invoke-Compose -Project $sourceProject -EnvFile $sourceEnv -Arguments @("exec", "-T", "db", "sh", "-ec", "pg_dump -U lensee_user -d lensee -Fc -f /tmp/certification.dump")
    & docker cp "$sourcePrefix-db`:/tmp/certification.dump" $backupPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $backupPath)) { throw "Could not copy the synthetic backup from the source database." }
    $backup = Get-Item -LiteralPath $backupPath
    $backupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $backupPath).Hash
    "size-bytes=$($backup.Length)`nsha256=$backupHash`ncreated-at=$(Get-Date -Format o)" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "backup-metadata.txt") -NoNewline

    $restoreStarted = Get-Date
    Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("up", "-d", "db")
    & docker cp $backupPath "$recoveryPrefix-db`:/tmp/certification.dump"
    if ($LASTEXITCODE -ne 0) { throw "Could not copy the synthetic backup to the isolated recovery database." }
    Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("exec", "-T", "db", "pg_restore", "-U", "lensee_user", "-d", "lensee", "--clean", "--if-exists", "/tmp/certification.dump")
    Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("--profile", "migrate", "run", "--rm", "--no-deps", "migrator")
    Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("up", "-d", "lensee.host")

    $ready = $false
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try {
            $response = Invoke-RestMethod "http://127.0.0.1:15301/ready" -TimeoutSec 2
            if ($response.status -eq "Healthy") { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw "Recovered API did not reach Healthy readiness." }
    $recoveryCounts = Get-Counts -Project $recoveryProject -EnvFile $recoveryEnv -Recovery
    Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-counts.txt") -Value $recoveryCounts -NoNewline
    if ($sourceCounts -ne $recoveryCounts) { throw "Synthetic recovery reconciliation counts differ from the source." }
    $restoreEnded = Get-Date
    $elapsed = [Math]::Round(($restoreEnded - $restoreStarted).TotalSeconds, 1)
    if ($elapsed -gt 3600) { throw "Recovery drill exceeded the 60-minute RTO target." }
    "ready=Healthy`nreconciliation=matched`nrto-seconds=$elapsed`nbackup-sha256=$backupHash" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "recovery-summary.txt") -NoNewline
}
finally {
    if (-not $KeepResources) {
        $ErrorActionPreference = "Continue"
        if (Test-Path -LiteralPath $sourceEnv) { Invoke-Compose -Project $sourceProject -EnvFile $sourceEnv -Arguments @("down", "--volumes", "--remove-orphans") }
        if (Test-Path -LiteralPath $recoveryEnv) { Invoke-Compose -Project $recoveryProject -EnvFile $recoveryEnv -Recovery -Arguments @("down", "--volumes", "--remove-orphans") }
        $ErrorActionPreference = "Stop"
    }
    if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
    if (Test-Path -LiteralPath $sourceEnv) { Remove-Item -LiteralPath $sourceEnv -Force }
    if (Test-Path -LiteralPath $recoveryEnv) { Remove-Item -LiteralPath $recoveryEnv -Force }
    Pop-Location
}
