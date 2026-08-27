param(
    [string]$EvidenceRoot = "artifacts/certification",
    [ValidateRange(1, 64)][int]$TargetCpuCount = 2,
    [ValidateRange(1, 64)][int]$TargetMemoryGb = 4,
    [switch]$SkipDockerCapacityGate,
    [switch]$KeepResources
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDirectory = Join-Path $repoRoot (Join-Path $EvidenceRoot $runId)
$successProject = "lensee-certification-success"
$failureProject = "lensee-certification-failure"
$successPrefix = "lensee_cert_success"
$failurePrefix = "lensee_cert_failure"
$certificationHost = "certification.local"
$shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($shell)) {
    $shell = (Get-Command powershell -ErrorAction Stop).Source
}

function Invoke-Logged {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [switch]$AllowFailure
    )

    $logPath = Join-Path $evidenceDirectory "$Name.log"
    $thrown = $null
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $Command 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    }
    catch {
        $thrown = $_
        $exitCode = if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { $LASTEXITCODE } else { 1 }
        $_ | Out-String | Add-Content -LiteralPath $logPath
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if (-not $AllowFailure -and ($thrown -or $exitCode -ne 0)) {
        if ($thrown) { throw $thrown }
        throw "$Name failed with exit code $exitCode. See $logPath."
    }

    return [pscustomobject]@{ ExitCode = $exitCode; LogPath = $logPath; Thrown = $thrown }
}

function Invoke-CertificationCompose {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$FailureOverlay
    )

    $composeArguments = @("--project-name", $Project, "--env-file", $EnvFile,
        "-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml",
        "-f", "docker-compose.certification.yml")
    if ($FailureOverlay) {
        $composeArguments += @("-f", "docker-compose.certification-failure.yml")
    }
    $composeArguments += $Arguments
    & docker compose @composeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed for $Project."
    }
}

function Get-DefaultStackFingerprint {
    $containerNames = @("lensee_db", "lensee_api", "lensee_web", "lensee_caddy")
    $containers = & docker ps -a --format '{{.Names}}|{{.ID}}|{{.Image}}|{{.Status}}' |
        Where-Object {
            $name = ($_ -split '\|', 2)[0]
            $containerNames -contains $name
        }

    $volumes = & docker volume ls --format '{{.Name}}' |
        Where-Object { $_ -match '^lensee' -and $_ -notmatch '^lensee-e2e-' -and $_ -notmatch '^lensee-certification-' }
    $listeners = if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        foreach ($port in 8181, 5000, 3001) {
            Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
                ForEach-Object { "$port|$($_.LocalAddress)|$($_.OwningProcess)" }
        }
    }
    else {
        & docker ps -a --format '{{.Names}}|{{.Ports}}' | Where-Object { $_ -match '8181|5000|3001' }
    }

    return (@($containers) + @($volumes) + @($listeners) | Sort-Object) -join "`n"
}

function New-RandomBase64 {
    param([Parameter(Mandatory = $true)][int]$ByteCount)

    $bytes = New-Object byte[] $ByteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function Write-DefaultStackFingerprint {
    param([Parameter(Mandatory = $true)][string]$Name)

    $content = Get-DefaultStackFingerprint
    $path = Join-Path $evidenceDirectory "$Name.txt"
    Set-Content -LiteralPath $path -Value $content -NoNewline
    return $content
}

function New-CertificationEnvFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Prefix,
        [Parameter(Mandatory = $true)][int]$DbPort,
        [Parameter(Mandatory = $true)][int]$ApiPort,
        [Parameter(Mandatory = $true)][int]$FrontendPort,
        [Parameter(Mandatory = $true)][int]$CaddyPort
    )

    $jwtSecret = New-RandomBase64 -ByteCount 48
    $dbPassword = New-RandomBase64 -ByteCount 32
    $lines = @(
        "APP_DOMAIN=$certificationHost"
        "TLS_EMAIL=certification@example.invalid"
        "DB_PASSWORD=$dbPassword"
        "JWT_SECRET=$jwtSecret"
        "JWT_ISSUER=LenseeCertification"
        "JWT_AUDIENCE=LenseeCertification"
        "FRONTEND_API_BASE_URL=http://127.0.0.1:$ApiPort"
        "CORS_ALLOWED_ORIGINS=https://$certificationHost"
        "DATABASE_AUTO_MIGRATE=false"
        "DATABASE_BASELINE_EXISTING_SCHEMA=false"
        "CERT_CONTAINER_PREFIX=$Prefix"
        "CERT_DB_PORT=$DbPort"
        "CERT_API_PORT=$ApiPort"
        "CERT_FRONTEND_PORT=$FrontendPort"
        "CERT_CADDY_PORT=$CaddyPort"
    )
    [System.IO.File]::WriteAllLines($Path, [string[]]$lines, [System.Text.UTF8Encoding]::new($false))
}

function Get-DatabaseFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile
    )

    $schemaHash = Invoke-CertificationCompose -Project $Project -EnvFile $EnvFile -Arguments @("exec", "-T", "db", "sh", "-c", "pg_dump -U lensee_user -d lensee --schema-only | sha256sum")
    $migrationHistory = Invoke-CertificationCompose -Project $Project -EnvFile $EnvFile -Arguments @("exec", "-T", "db", "psql", "-At", "-U", "lensee_user", "-d", "lensee", "-c", "select table_schema from information_schema.tables where table_name = '__EFMigrationsHistory' order by table_schema;")
    return [pscustomobject]@{ SchemaHash = ($schemaHash | Out-String).Trim(); MigrationSchemas = ($migrationHistory | Out-String).Trim() }
}

function Assert-Ready {
    param([Parameter(Mandatory = $true)][int]$ApiPort, [Parameter(Mandatory = $true)][int]$CaddyPort)

    $direct = Invoke-RestMethod "http://127.0.0.1:$ApiPort/ready" -TimeoutSec 10
    if ($direct.status -ne "Healthy") {
        throw "Direct API readiness was '$($direct.status)', not Healthy."
    }

    $proxied = Invoke-WebRequest "http://127.0.0.1:$CaddyPort/ready" -Headers @{ Host = $certificationHost } -UseBasicParsing -TimeoutSec 10
    $proxiedBody = $proxied.Content | ConvertFrom-Json
    if ($proxied.StatusCode -ne 200 -or $proxiedBody.status -ne "Healthy") {
        throw "Caddy readiness did not return HTTP 200 / Healthy."
    }

    "direct=Healthy; caddy=Healthy" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "readiness.txt")
}

function Assert-AutoMigrateDisabled {
    param([Parameter(Mandatory = $true)][string]$ContainerName)

    $environment = & docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $ContainerName
    if ($LASTEXITCODE -ne 0 -or -not ($environment -contains "Database__AutoMigrate=false")) {
        throw "$ContainerName does not expose Database__AutoMigrate=false."
    }

    $environment | Where-Object { $_ -match '^Database__(AutoMigrate|BaselineExistingSchema)=' }
}

function Assert-VpsRuntimeResourceEnvelope {
    param([Parameter(Mandatory = $true)][string]$Prefix)

    $expectedLimits = @(
        [pscustomobject]@{ Name = "$Prefix-db"; NanoCpus = 750000000L; MemoryBytes = 768MB },
        [pscustomobject]@{ Name = "$Prefix-api"; NanoCpus = 1000000000L; MemoryBytes = 1GB },
        [pscustomobject]@{ Name = "$Prefix-web"; NanoCpus = 100000000L; MemoryBytes = 128MB },
        [pscustomobject]@{ Name = "$Prefix-caddy"; NanoCpus = 150000000L; MemoryBytes = 128MB }
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $containerNames = [System.Collections.Generic.List[string]]::new()
    $totalNanoCpus = 0L
    $totalMemoryBytes = 0L
    foreach ($limit in $expectedLimits) {
        $actual = (& docker inspect --format '{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}' $limit.Name).Trim()
        if ($LASTEXITCODE -ne 0 -or $actual -notmatch '^(\d+)\|(\d+)$') {
            throw "Could not read resource limits for $($limit.Name)."
        }

        $actualNanoCpus = [int64]$Matches[1]
        $actualMemoryBytes = [int64]$Matches[2]
        if ($actualNanoCpus -ne $limit.NanoCpus -or $actualMemoryBytes -ne $limit.MemoryBytes) {
            throw "$($limit.Name) has CPU=$actualNanoCpus and memory=$actualMemoryBytes; expected CPU=$($limit.NanoCpus) and memory=$($limit.MemoryBytes)."
        }

        $totalNanoCpus += $actualNanoCpus
        $totalMemoryBytes += $actualMemoryBytes
        $containerNames.Add($limit.Name)
        $lines.Add("$($limit.Name): cpu=$actualNanoCpus; memory=$actualMemoryBytes")
    }

    if ($totalNanoCpus -gt ($TargetCpuCount * 1000000000L)) {
        throw "Concurrent runtime CPU caps exceed the $TargetCpuCount-vCPU VPS envelope."
    }
    if ($totalMemoryBytes -gt ($TargetMemoryGb * 1000000000L)) {
        throw "Concurrent runtime memory caps exceed the $TargetMemoryGb-GB VPS envelope."
    }

    $lines.Insert(0, "target=$TargetCpuCount vCPU / $TargetMemoryGb GB; concurrent-runtime-caps=cpu=$totalNanoCpus; memory=$totalMemoryBytes")
    $lines | Set-Content -LiteralPath (Join-Path $evidenceDirectory "vps-resource-envelope.txt")

    $runtimeState = [System.Collections.Generic.List[string]]::new()
    foreach ($containerName in $containerNames) {
        $state = (& docker inspect --format '{{.State.OOMKilled}}|{{.RestartCount}}' $containerName).Trim()
        if ($LASTEXITCODE -ne 0 -or $state -notmatch '^(true|false)\|(\d+)$') {
            throw "Could not read runtime state for $containerName."
        }
        if ($Matches[1] -eq 'true' -or [int]$Matches[2] -ne 0) {
            throw "$containerName was OOM-killed or restarted during the VPS-envelope rehearsal: $state."
        }
        $runtimeState.Add("${containerName}: oom-killed=$($Matches[1]); restarts=$($Matches[2])")
    }
    & docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}' @containerNames |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "docker-stats-runtime.txt")
    $runtimeState | Set-Content -LiteralPath (Join-Path $evidenceDirectory "vps-runtime-state.txt")
}

function Assert-VpsMigratorResourceLimit {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile
    )

    $composeArguments = @("--project-name", $Project, "--env-file", $EnvFile,
        "-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml",
        "-f", "docker-compose.certification.yml", "--profile", "migrate", "config", "--format", "json")
    $config = (& docker compose @composeArguments | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { throw "Could not render the VPS-envelope Compose configuration." }

    $migrator = $config.services.PSObject.Properties["migrator"].Value
    if ($null -eq $migrator -or $migrator.cpus -ne "0.75" -or [int64]$migrator.mem_limit -ne 1GB) {
        throw "Migrator resource limit does not match the VPS envelope."
    }
    "migrator: cpu=$($migrator.cpus); memory=$($migrator.mem_limit)" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "vps-migrator-resource-limit.txt")
}

function Assert-E2EWorkloadRuntime {
    $expectedLimits = @(
        [pscustomobject]@{ Name = "lensee_e2e_db"; NanoCpus = 750000000L; MemoryBytes = 768MB },
        [pscustomobject]@{ Name = "lensee_e2e_api"; NanoCpus = 1000000000L; MemoryBytes = 1GB },
        [pscustomobject]@{ Name = "lensee_e2e_web"; NanoCpus = 100000000L; MemoryBytes = 128MB }
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($limit in $expectedLimits) {
        $actual = (& docker inspect --format '{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}|{{.State.OOMKilled}}|{{.RestartCount}}' $limit.Name).Trim()
        if ($LASTEXITCODE -ne 0 -or $actual -notmatch '^(\d+)\|(\d+)\|(true|false)\|(\d+)$') {
            throw "Could not read E2E workload state for $($limit.Name)."
        }
        if ([int64]$Matches[1] -ne $limit.NanoCpus -or [int64]$Matches[2] -ne $limit.MemoryBytes -or $Matches[3] -eq 'true' -or [int]$Matches[4] -ne 0) {
            throw "$($limit.Name) exceeded its workload safety envelope: $actual."
        }
        $lines.Add("$($limit.Name): cpu=$($Matches[1]); memory=$($Matches[2]); oom-killed=$($Matches[3]); restarts=$($Matches[4])")
    }

    $lines | Set-Content -LiteralPath (Join-Path $evidenceDirectory "e2e-workload-runtime-state.txt")
    & docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}' lensee_e2e_db lensee_e2e_api lensee_e2e_web |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "e2e-workload-docker-stats.txt")
}

function Invoke-E2ESetup {
    param([Parameter(Mandatory = $true)][string]$Name, [switch]$Production)

    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    if ($Production) { $env:ASPNETCORE_ENVIRONMENT = "Production" } else { Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue }
    try {
        return Invoke-Logged -Name $Name -AllowFailure:$Production -Command { & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/e2e-setup.ps1") }
    }
    finally {
        if ($null -eq $previousEnvironment) { Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue } else { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }
    }
}

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$successEnv = Join-Path $env:TEMP "lensee-certification-success-$runId.env"
$failureEnv = Join-Path $env:TEMP "lensee-certification-failure-$runId.env"
$defaultBefore = $null

Push-Location $repoRoot
try {
    $dockerInfo = & docker info --format 'Server={{.ServerVersion}}; OS={{.OSType}}; CPUs={{.NCPU}}; MemoryBytes={{.MemTotal}}'
    if ($LASTEXITCODE -ne 0) { throw "Docker Engine is unavailable." }
    $dockerInfo | Set-Content -LiteralPath (Join-Path $evidenceDirectory "docker-info.txt")
    $memoryBytes = [int64](($dockerInfo -split 'MemoryBytes=')[-1])
    $cpuCount = [int](($dockerInfo -split 'CPUs=')[-1] -split ';')[0]
    $minimumMemoryBytes = [int64]$TargetMemoryGb * 1000000000L
    if (-not $SkipDockerCapacityGate -and ($memoryBytes -lt $minimumMemoryBytes -or $cpuCount -lt $TargetCpuCount)) {
        throw "Docker reports $cpuCount CPUs and $memoryBytes bytes; the VPS-envelope rehearsal requires at least $TargetCpuCount CPUs and $TargetMemoryGb GB ($minimumMemoryBytes bytes)."
    }

    Invoke-Logged -Name "docker-hello-world" -Command { docker run --rm hello-world } | Out-Null
    $defaultBefore = Write-DefaultStackFingerprint -Name "default-stack-before"

    New-CertificationEnvFile -Path $successEnv -Prefix $successPrefix -DbPort 18181 -ApiPort 15000 -FrontendPort 13001 -CaddyPort 18080
    Assert-VpsMigratorResourceLimit -Project $successProject -EnvFile $successEnv
    Invoke-Logged -Name "deploy-success-first" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $successProject -EnvFile $successEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory
    } | Out-Null
    Assert-Ready -ApiPort 15000 -CaddyPort 18080
    Assert-AutoMigrateDisabled -ContainerName "$successPrefix-api" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "auto-migrate-api.txt")
    Assert-VpsRuntimeResourceEnvelope -Prefix $successPrefix
    $firstFingerprint = Get-DatabaseFingerprint -Project $successProject -EnvFile $successEnv
    $firstFingerprint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "database-first.json")

    Invoke-Logged -Name "deploy-success-second" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $successProject -EnvFile $successEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory
    } | Out-Null
    Assert-Ready -ApiPort 15000 -CaddyPort 18080
    $secondFingerprint = Get-DatabaseFingerprint -Project $successProject -EnvFile $successEnv
    $secondFingerprint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "database-second.json")
    if ($firstFingerprint.SchemaHash -ne $secondFingerprint.SchemaHash -or $firstFingerprint.MigrationSchemas -ne $secondFingerprint.MigrationSchemas) {
        throw "Second deployment changed the certification database schema or migration-history schema set."
    }

    New-CertificationEnvFile -Path $failureEnv -Prefix $failurePrefix -DbPort 18182 -ApiPort 15001 -FrontendPort 13002 -CaddyPort 18081
    $failure = Invoke-Logged -Name "deploy-injected-migrator-failure" -AllowFailure -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $failureProject -EnvFile $failureEnv -AdditionalComposeFiles @("docker-compose.certification.yml", "docker-compose.certification-failure.yml") -EvidenceDirectory $evidenceDirectory
    }
    if ($failure.ExitCode -eq 0 -or -not (Select-String -LiteralPath $failure.LogPath -Pattern "certification-injected-migrator-failure" -Quiet)) {
        throw "Injected migration failure was not surfaced by deployment evidence."
    }
    $failureApi = Invoke-CertificationCompose -Project $failureProject -EnvFile $failureEnv -FailureOverlay -Arguments @("ps", "-q", "lensee.host")
    if (-not [string]::IsNullOrWhiteSpace(($failureApi | Out-String))) {
        throw "API started after the injected migrator failure."
    }

    $beforeProductionRefusal = Write-DefaultStackFingerprint -Name "default-stack-before-production-refusal"
    $productionRefusal = Invoke-E2ESetup -Name "e2e-production-refusal" -Production
    if ($productionRefusal.ExitCode -eq 0 -or -not (Select-String -LiteralPath $productionRefusal.LogPath -Pattern "Refusing to reset E2E data" -Quiet)) {
        throw "E2E Production refusal did not fail safely."
    }
    if ($beforeProductionRefusal -ne (Write-DefaultStackFingerprint -Name "default-stack-after-production-refusal")) {
        throw "Production refusal changed default-stack resources."
    }

    Invoke-E2ESetup -Name "e2e-setup-first" | Out-Null
    Invoke-E2ESetup -Name "e2e-setup-second" | Out-Null
    foreach ($username in "e2e_admin", "e2e_erp_admin", "e2e_clevel", "e2e_accountant", "e2e_roxy_clerk", "e2e_retail_clerk", "e2e_online_clerk") {
        $body = @{ username = $username; password = "E2E-only-not-production-2026!" } | ConvertTo-Json -Compress
        $login = Invoke-WebRequest "http://127.0.0.1:55000/api/v1/auth/login" -Method Post -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec 10
        if ($login.StatusCode -ne 200) { throw "Synthetic E2E user $username did not authenticate." }
        "$username=authenticated" | Add-Content -LiteralPath (Join-Path $evidenceDirectory "e2e-authentication.txt")
    }

    Invoke-Logged -Name "e2e-eight-user-workload" -Command {
        node (Join-Path $repoRoot "scripts/run-workload-test.mjs") --users 8 --duration-seconds 60 --output (Join-Path $evidenceDirectory "e2e-eight-user-workload.json")
    } | Out-Null
    Assert-E2EWorkloadRuntime

    $previousSkipWebserver = $env:LENSEE_E2E_SKIP_WEBSERVER
    $env:LENSEE_E2E_SKIP_WEBSERVER = "1"
    try {
        Invoke-Logged -Name "e2e-auth-playwright" -Command { npx playwright test e2e/auth-permissions.spec.js --project=chromium } | Out-Null
    }
    finally {
        if ($null -eq $previousSkipWebserver) { Remove-Item Env:LENSEE_E2E_SKIP_WEBSERVER -ErrorAction SilentlyContinue } else { $env:LENSEE_E2E_SKIP_WEBSERVER = $previousSkipWebserver }
    }

    $defaultAfter = Write-DefaultStackFingerprint -Name "default-stack-after"
    if ($defaultBefore -ne $defaultAfter) {
        throw "Certification verification changed default-stack containers, volumes, or listeners."
    }
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $evidenceDirectory "runtime-failure.txt")
    throw
}
finally {
    if (-not $KeepResources) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            if (Test-Path -LiteralPath $successEnv) {
                & docker compose --project-name $successProject --env-file $successEnv -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml -f docker-compose.certification.yml down --volumes --remove-orphans 2>$null
            }
            if (Test-Path -LiteralPath $failureEnv) {
                & docker compose --project-name $failureProject --env-file $failureEnv -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml -f docker-compose.certification.yml -f docker-compose.certification-failure.yml down --volumes --remove-orphans 2>$null
            }
            & docker compose --project-name lensee-e2e -f docker-compose.yml -f docker-compose.e2e.yml down --volumes --remove-orphans 2>$null
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
            if ([System.IO.File]::Exists($successEnv)) { [System.IO.File]::Delete($successEnv) }
            if ([System.IO.File]::Exists($failureEnv)) { [System.IO.File]::Delete($failureEnv) }
        }
    }
    Pop-Location
}
