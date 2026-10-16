param(
    [string]$EvidenceRoot = "artifacts/certification",
    [string]$CandidateSha,
    [string]$ApiImage,
    [string]$FrontendImage,
    [ValidateRange(1, 64)][int]$TargetCpuCount = 2,
    [ValidateRange(1, 64)][int]$TargetMemoryGb = 4,
    [switch]$SkipDockerCapacityGate,
    [switch]$KeepResources
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$candidateSha = if ([string]::IsNullOrWhiteSpace($CandidateSha)) { (& git -C $repoRoot rev-parse HEAD).Trim() } else { $CandidateSha }
if ($candidateSha -notmatch '^[0-9a-f]{40}$') { throw "Candidate SHA must be a full 40-character commit id." }
$checkedOutSha = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($checkedOutSha -ne $candidateSha) { throw "Checked-out commit $checkedOutSha does not match requested candidate $candidateSha." }
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
    # Container status includes a continuously changing elapsed uptime (for
    # example, "Up 37 minutes"), so it cannot be part of a before/after
    # resource fingerprint. Name, ID, and image still detect replacement.
    $containers = & docker ps -a --format '{{.Names}}|{{.ID}}|{{.Image}}' |
        Where-Object {
            $name = ($_ -split '\|', 2)[0]
            $containerNames -contains $name
        }

    $volumes = & docker volume ls --format '{{.Name}}' |
        Where-Object { $_ -match '^lensee' -and $_ -notmatch '^lensee[_-]e2e[_-]' -and $_ -notmatch '^lensee[_-]certification[_-]' }
    $listeners = if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        foreach ($port in 8181, 5000, 3001) {
            # Docker Desktop can recreate its IPv6 forwarding entry while the
            # default container and published host port remain unchanged. The
            # gate needs the listener's presence, not that transient proxy PID
            # or address-family implementation detail.
            if (@(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue).Count -gt 0) {
                "$port|listening"
            }
        }
    }
    else {
        # Docker's Linux-friendly port output contains values such as
        # "127.0.0.1:18181->5432/tcp". Match only complete published host-port
        # tokens so the isolated certification ports (18181, 15000, 13001) do
        # not get mistaken for the default-stack ports (8181, 5000, 3001).
        & docker ps -a --format '{{.Names}}|{{.Ports}}' |
            Where-Object { $_ -match '(?<!\d)(?:8181|5000|3001)->' }
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
        [Parameter(Mandatory = $true)][int]$CaddyPort,
        [string]$ApiImage,
        [string]$FrontendImage
    )

    if ([string]::IsNullOrWhiteSpace($ApiImage) -ne [string]::IsNullOrWhiteSpace($FrontendImage)) {
        throw "Both prebuilt API and frontend image references must be supplied together."
    }
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
    if (-not [string]::IsNullOrWhiteSpace($ApiImage)) {
        $lines += "LENSEE_HOST_IMAGE=$ApiImage"
        $lines += "LENSEE_FRONTEND_IMAGE=$FrontendImage"
    }
    [System.IO.File]::WriteAllLines($Path, [string[]]$lines, [System.Text.UTF8Encoding]::new($false))
}

function Get-DatabaseFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile
    )

    # PostgreSQL 17 emits a randomized \restrict/\unrestrict pair in every pg_dump.
    # Remove only those dump-session directives so the fingerprint measures schema state.
    $schemaHash = Invoke-CertificationCompose -Project $Project -EnvFile $EnvFile -Arguments @("exec", "-T", "db", "sh", "-c", "pg_dump -U lensee_user -d lensee --schema-only | sed '/^\\restrict /d; /^\\unrestrict /d' | sha256sum")
    $migrationHistory = Invoke-CertificationCompose -Project $Project -EnvFile $EnvFile -Arguments @("exec", "-T", "db", "psql", "-At", "-U", "lensee_user", "-d", "lensee", "-c", "select table_schema from information_schema.tables where table_name = '__EFMigrationsHistory' order by table_schema;")
    return [pscustomobject]@{ SchemaHash = ($schemaHash | Out-String).Trim(); MigrationSchemas = ($migrationHistory | Out-String).Trim() }
}

function Assert-Ready {
    param([Parameter(Mandatory = $true)][int]$ApiPort, [Parameter(Mandatory = $true)][int]$CaddyPort)

    $direct = Invoke-RestMethod "http://127.0.0.1:$ApiPort/ready" -TimeoutSec 10
    if ($direct.status -ne "Healthy") {
        throw "Direct API readiness was '$($direct.status)', not Healthy."
    }

    $caddyReady = $false
    $lastCaddyError = $null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $proxied = Invoke-WebRequest "http://127.0.0.1:$CaddyPort/ready" -Headers @{ Host = $certificationHost } -UseBasicParsing -TimeoutSec 2
            $proxiedBody = $proxied.Content | ConvertFrom-Json
            if ($proxied.StatusCode -eq 200 -and $proxiedBody.status -eq "Healthy") {
                $caddyReady = $true
                break
            }
            $lastCaddyError = "HTTP $($proxied.StatusCode) / $($proxiedBody.status)"
        }
        catch {
            $lastCaddyError = $_.Exception.Message
        }
        Start-Sleep -Seconds 1
    }
    if (-not $caddyReady) {
        throw "Caddy readiness did not return HTTP 200 / Healthy within 20 seconds: $lastCaddyError"
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

function Get-ResolvedComposeConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [string]$EnvFile,
        [Parameter(Mandatory = $true)][string[]]$ComposeFiles
    )

    $arguments = @("--project-name", $Project)
    if (-not [string]::IsNullOrWhiteSpace($EnvFile)) { $arguments += @("--env-file", $EnvFile) }
    # The migrator is profile-gated in docker-compose.yml but is part of the
    # deployment contract whose resolved resource limits this suite certifies.
    $arguments += @("--profile", "migrate")
    foreach ($composeFile in $ComposeFiles) { $arguments += @("-f", $composeFile) }
    $arguments += @("config", "--format", "json")
    $json = & docker compose @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not render the resolved Compose resource configuration for $Project." }
    try { return (($json -join [Environment]::NewLine) | ConvertFrom-Json) }
    catch { throw "Resolved Compose resource configuration for $Project was not valid JSON." }
}

function Get-ResolvedServiceLimits {
    param(
        [Parameter(Mandatory = $true)]$Configuration,
        [Parameter(Mandatory = $true)][string]$ServiceName
    )

    $serviceProperty = $Configuration.services.PSObject.Properties[$ServiceName]
    if ($null -eq $serviceProperty) { throw "Resolved Compose service '$ServiceName' is missing." }
    $service = $serviceProperty.Value
    $cpuValue = $service.cpus
    $memoryValue = $service.mem_limit
    if ($null -eq $cpuValue -and $null -ne $service.deploy) { $cpuValue = $service.deploy.resources.limits.cpus }
    if ($null -eq $memoryValue -and $null -ne $service.deploy) { $memoryValue = $service.deploy.resources.limits.memory }
    if ($null -eq $cpuValue -or $null -eq $memoryValue) {
        throw "Resolved Compose service '$ServiceName' must declare positive CPU and memory limits."
    }

    try {
        $nanoCpus = [int64][Math]::Round(([decimal]$cpuValue) * 1000000000L)
        $memoryBytes = [int64]$memoryValue
    }
    catch { throw "Resolved Compose resource limits for '$ServiceName' were not numeric bytes/CPU values." }
    if ($nanoCpus -le 0 -or $memoryBytes -le 0) { throw "Resolved Compose service '$ServiceName' has a non-positive resource limit." }
    return [pscustomobject]@{
        ServiceName = $ServiceName
        NanoCpus = $nanoCpus
        MemoryBytes = $memoryBytes
    }
}

function Assert-VpsRuntimeResourceEnvelope {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    $files = @("docker-compose.yml", "docker-compose.prod.yml", "docker-compose.deploy.yml", "docker-compose.certification.yml")
    $configuration = Get-ResolvedComposeConfiguration -Project $Project -EnvFile $EnvFile -ComposeFiles $files
    $targets = @(
        [pscustomobject]@{ Service = "db"; Container = "$Prefix-db" },
        [pscustomobject]@{ Service = "lensee.host"; Container = "$Prefix-api" },
        [pscustomobject]@{ Service = "frontend"; Container = "$Prefix-web" },
        [pscustomobject]@{ Service = "caddy"; Container = "$Prefix-caddy" }
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $containerNames = [System.Collections.Generic.List[string]]::new()
    $totalNanoCpus = 0L
    $totalMemoryBytes = 0L
    foreach ($target in $targets) {
        $resolved = Get-ResolvedServiceLimits -Configuration $configuration -ServiceName $target.Service
        $actual = (& docker inspect --format '{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}' $target.Container).Trim()
        if ($LASTEXITCODE -ne 0 -or $actual -notmatch '^(\d+)\|(\d+)$') {
            throw "Could not read resource limits for $($target.Container)."
        }

        $actualNanoCpus = [int64]$Matches[1]
        $actualMemoryBytes = [int64]$Matches[2]
        if ($actualNanoCpus -ne $resolved.NanoCpus -or $actualMemoryBytes -ne $resolved.MemoryBytes) {
            throw "$($target.Container) runtime limits differ from resolved Compose service '$($target.Service)': actual CPU=$actualNanoCpus memory=$actualMemoryBytes; configured CPU=$($resolved.NanoCpus) memory=$($resolved.MemoryBytes)."
        }

        $totalNanoCpus += $actualNanoCpus
        $totalMemoryBytes += $actualMemoryBytes
        $containerNames.Add($target.Container)
        $lines.Add("$($target.Container): service=$($target.Service); cpu=$actualNanoCpus; memory=$actualMemoryBytes")
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
    $dockerNames = $containerNames.ToArray()
    & docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}' @dockerNames |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "docker-stats-runtime.txt")
    $runtimeState | Set-Content -LiteralPath (Join-Path $evidenceDirectory "vps-runtime-state.txt")
}

function Assert-VpsMigratorResourceLimit {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$EnvFile
    )

    $files = @("docker-compose.yml", "docker-compose.prod.yml", "docker-compose.deploy.yml", "docker-compose.certification.yml")
    $config = Get-ResolvedComposeConfiguration -Project $Project -EnvFile $EnvFile -ComposeFiles $files
    $migrator = Get-ResolvedServiceLimits -Configuration $config -ServiceName "migrator"
    if ($migrator.NanoCpus -ne 750000000L -or $migrator.MemoryBytes -ne 1GB) {
        throw "Migrator resource limit does not match the VPS envelope."
    }
    "migrator: cpu=$($migrator.NanoCpus); memory=$($migrator.MemoryBytes)" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "vps-migrator-resource-limit.txt")
}

function Assert-RuntimeImagePolicy {
    param([Parameter(Mandatory = $true)][string]$ContainerName, [string]$ExpectedImageId)

    $identity = (& docker exec $ContainerName /bin/sh -ec 'uid=$(id -u); test "$uid" -gt 0; test -f /usr/share/zoneinfo/Africa/Cairo; printf "uid=%s\n" "$uid"').Trim()
    if ($LASTEXITCODE -ne 0 -or $identity -notmatch '^uid=[1-9]\d*$') {
        throw "$ContainerName does not satisfy the non-root/timezone image runtime policy."
    }
    $imageId = (& docker inspect --format '{{.Image}}' $ContainerName).Trim()
    if ($LASTEXITCODE -ne 0 -or $imageId -notmatch '^sha256:[0-9a-f]{64}$') {
        throw "Could not identify the immutable image used by $ContainerName."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedImageId) -and $imageId -ne $ExpectedImageId) {
        throw "$ContainerName is running image $imageId instead of the certified image $ExpectedImageId."
    }
    "container=$ContainerName`nimage-id=$imageId`n$identity`ntimezone=Africa/Cairo-present" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "runtime-image-policy.txt")
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
$temporaryDirectory = [System.IO.Path]::GetTempPath()
$successEnv = Join-Path $temporaryDirectory "lensee-certification-success-$runId.env"
$failureEnv = Join-Path $temporaryDirectory "lensee-certification-failure-$runId.env"
$defaultBefore = $null
$cleanupFailures = [System.Collections.Generic.List[string]]::new()
$deployExtraArgs = @()
$expectedApiImageId = $null
$expectedFrontendImageId = $null
if (-not [string]::IsNullOrWhiteSpace($ApiImage) -or -not [string]::IsNullOrWhiteSpace($FrontendImage)) {
    if ([string]::IsNullOrWhiteSpace($ApiImage) -or [string]::IsNullOrWhiteSpace($FrontendImage)) {
        throw "Both -ApiImage and -FrontendImage are required when using a prebuilt candidate image bundle."
    }
    $deployExtraArgs = @("-SkipApplicationBuild")
    $expectedApiImageId = (& docker image inspect --format '{{.Id}}' $ApiImage).Trim()
    $expectedFrontendImageId = (& docker image inspect --format '{{.Id}}' $FrontendImage).Trim()
    if ($LASTEXITCODE -ne 0 -or $expectedApiImageId -notmatch '^sha256:[0-9a-f]{64}$' -or $expectedFrontendImageId -notmatch '^sha256:[0-9a-f]{64}$') {
        throw "Preloaded candidate application images could not be identified."
    }
}

Push-Location $repoRoot
try {
    @("candidate-sha=$candidateSha", "suite=runtime", "started-at=$(Get-Date -Format o)") |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")
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

    New-CertificationEnvFile -Path $successEnv -Prefix $successPrefix -DbPort 18181 -ApiPort 15000 -FrontendPort 13001 -CaddyPort 18080 -ApiImage $ApiImage -FrontendImage $FrontendImage
    Assert-VpsMigratorResourceLimit -Project $successProject -EnvFile $successEnv
    Invoke-Logged -Name "deploy-success-first" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $successProject -EnvFile $successEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory @deployExtraArgs
    } | Out-Null
    Assert-Ready -ApiPort 15000 -CaddyPort 18080
    Assert-AutoMigrateDisabled -ContainerName "$successPrefix-api" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "auto-migrate-api.txt")
    Assert-VpsRuntimeResourceEnvelope -Project $successProject -EnvFile $successEnv -Prefix $successPrefix
    Assert-RuntimeImagePolicy -ContainerName "$successPrefix-api" -ExpectedImageId $expectedApiImageId
    $frontendContainerImageId = (& docker inspect --format '{{.Image}}' "$successPrefix-web").Trim()
    if ($LASTEXITCODE -ne 0 -or ($expectedFrontendImageId -and $frontendContainerImageId -ne $expectedFrontendImageId)) {
        throw "Certification frontend container did not use the exact candidate image."
    }
    "container=$successPrefix-web`nimage-id=$frontendContainerImageId`nexpected-image-id=$expectedFrontendImageId" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "runtime-frontend-image-identity.txt")
    $firstFingerprint = Get-DatabaseFingerprint -Project $successProject -EnvFile $successEnv
    $firstFingerprint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "database-first.json")

    Invoke-Logged -Name "deploy-success-second" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $successProject -EnvFile $successEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory @deployExtraArgs
    } | Out-Null
    Assert-Ready -ApiPort 15000 -CaddyPort 18080
    $secondFingerprint = Get-DatabaseFingerprint -Project $successProject -EnvFile $successEnv
    $secondFingerprint | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDirectory "database-second.json")
    if ($firstFingerprint.SchemaHash -ne $secondFingerprint.SchemaHash -or $firstFingerprint.MigrationSchemas -ne $secondFingerprint.MigrationSchemas) {
        throw "Second deployment changed the certification database schema or migration-history schema set."
    }

    New-CertificationEnvFile -Path $failureEnv -Prefix $failurePrefix -DbPort 18182 -ApiPort 15001 -FrontendPort 13002 -CaddyPort 18081 -ApiImage $ApiImage -FrontendImage $FrontendImage
    $failure = Invoke-Logged -Name "deploy-injected-migrator-failure" -AllowFailure -Command {
        # Pass one explicit comma-delimited value through the child PowerShell process.
        # deploy-prod.ps1 normalizes it to the two overlay files.
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $failureProject -EnvFile $failureEnv -AdditionalComposeFiles "docker-compose.certification.yml,docker-compose.certification-failure.yml" -EvidenceDirectory $evidenceDirectory @deployExtraArgs
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

    $defaultAfter = Write-DefaultStackFingerprint -Name "default-stack-after"
    if ($defaultBefore -ne $defaultAfter) {
        throw "Certification verification changed default-stack containers, volumes, or listeners."
    }
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $evidenceDirectory "runtime-failure.txt")
    "candidate-sha=$candidateSha`nsuite=runtime`nresult=failed`nfailed-at=$(Get-Date -Format o)`nerror=$($_.Exception.Message)" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "suite-result.txt")
    throw
}
finally {
    if (-not $KeepResources) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $cleanupTargets = @(
                @{ Name = "runtime-success"; Project = $successProject; Env = $successEnv; Files = @("docker-compose.yml", "docker-compose.prod.yml", "docker-compose.deploy.yml", "docker-compose.certification.yml") },
                @{ Name = "runtime-failure"; Project = $failureProject; Env = $failureEnv; Files = @("docker-compose.yml", "docker-compose.prod.yml", "docker-compose.deploy.yml", "docker-compose.certification.yml", "docker-compose.certification-failure.yml") }
            )
            foreach ($target in $cleanupTargets) {
                if ($target.Env -and -not (Test-Path -LiteralPath $target.Env)) { continue }
                $composeArguments = @("--project-name", $target.Project)
                if ($target.Env) { $composeArguments += @("--env-file", $target.Env) }
                foreach ($composeFile in $target.Files) { $composeArguments += @("-f", $composeFile) }
                $composeArguments += @("down", "--volumes", "--remove-orphans")
                & docker compose @composeArguments 2>&1 |
                    Tee-Object -FilePath (Join-Path $evidenceDirectory "cleanup-$($target.Name).log")
                if ($LASTEXITCODE -ne 0) { $cleanupFailures.Add("$($target.Name) cleanup exited with code $LASTEXITCODE") }
            }
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
            if ([System.IO.File]::Exists($successEnv)) { [System.IO.File]::Delete($successEnv) }
            if ([System.IO.File]::Exists($failureEnv)) { [System.IO.File]::Delete($failureEnv) }
        }
    }
    if ($KeepResources) {
        "result=not-run`nreason=KeepResources" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
    }
    elseif ($cleanupFailures.Count -eq 0) {
        "result=passed`nprojects=$successProject,$failureProject" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
    }
    else {
        ("result=failed" + [Environment]::NewLine + ($cleanupFailures -join [Environment]::NewLine)) |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "cleanup-result.txt")
        "candidate-sha=$candidateSha`nsuite=runtime`nresult=failed`nfailed-at=$(Get-Date -Format o)`nerror=isolated resource cleanup failed" |
            Set-Content -LiteralPath (Join-Path $evidenceDirectory "suite-result.txt")
    }
    if (Test-Path -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")) {
        "completed-at=$(Get-Date -Format o)" | Add-Content -LiteralPath (Join-Path $evidenceDirectory "run-metadata.txt")
    }
    Pop-Location
}

if ($cleanupFailures.Count -gt 0) {
    throw "Runtime certification completed but isolated resource cleanup failed. See $(Join-Path $evidenceDirectory 'cleanup-result.txt')."
}
if (-not (Test-Path -LiteralPath (Join-Path $evidenceDirectory "suite-result.txt"))) {
    "candidate-sha=$candidateSha`nsuite=runtime`nresult=passed`ncompleted-at=$(Get-Date -Format o)" |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory "suite-result.txt")
}
