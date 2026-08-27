param(
    [string]$EvidenceRoot = "artifacts/certification",
    [switch]$SkipDockerMemoryGate,
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
    if (-not $SkipDockerMemoryGate -and $memoryBytes -lt 8GB) {
        throw "Docker reports $memoryBytes bytes, below the required 8 GiB."
    }

    Invoke-Logged -Name "docker-hello-world" -Command { docker run --rm hello-world } | Out-Null
    $defaultBefore = Write-DefaultStackFingerprint -Name "default-stack-before"

    New-CertificationEnvFile -Path $successEnv -Prefix $successPrefix -DbPort 18181 -ApiPort 15000 -FrontendPort 13001 -CaddyPort 18080
    Invoke-Logged -Name "deploy-success-first" -Command {
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts/deploy-prod.ps1") -ProjectName $successProject -EnvFile $successEnv -AdditionalComposeFiles "docker-compose.certification.yml" -EvidenceDirectory $evidenceDirectory
    } | Out-Null
    Assert-Ready -ApiPort 15000 -CaddyPort 18080
    Assert-AutoMigrateDisabled -ContainerName "$successPrefix-api" | Set-Content -LiteralPath (Join-Path $evidenceDirectory "auto-migrate-api.txt")
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
