param(
    [Parameter(Mandatory = $true)][string]$EnvFile,
    [string]$EvidenceDirectory
)

$ErrorActionPreference = "Stop"

function Read-EnvironmentFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Environment file not found: $Path"
    }

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) { continue }
        $separator = $trimmed.IndexOf("=")
        if ($separator -lt 1) { throw "Invalid environment-file line: $trimmed" }
        $values[$trimmed.Substring(0, $separator)] = $trimmed.Substring($separator + 1)
    }
    return $values
}

function Require-Value {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Values,
        [Parameter(Mandatory = $true)][string]$Name,
        [int]$MinimumLength = 1
    )

    $value = $Values[$Name]
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match 'replace-with|<your-|example\.com|example\.invalid') {
        throw "$Name is missing or still contains a placeholder."
    }
    if ($value.Length -lt $MinimumLength) {
        throw "$Name must contain at least $MinimumLength characters."
    }
    return $value
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedEnvFile = (Resolve-Path $EnvFile).Path
$values = Read-EnvironmentFile -Path $resolvedEnvFile

$appDomain = Require-Value -Values $values -Name "APP_DOMAIN"
$tlsEmail = Require-Value -Values $values -Name "TLS_EMAIL"
$jwtSecret = Require-Value -Values $values -Name "JWT_SECRET" -MinimumLength 32
$dbPassword = Require-Value -Values $values -Name "DB_PASSWORD" -MinimumLength 24
$frontendBaseUrl = Require-Value -Values $values -Name "FRONTEND_API_BASE_URL"
$corsOrigin = Require-Value -Values $values -Name "CORS_ALLOWED_ORIGINS"
$trustedProxyNetwork = Require-Value -Values $values -Name "HOSTING_TRUSTED_PROXY_NETWORK"

if ($appDomain -match '[:/]' -or $appDomain -notmatch '\.') {
    throw "APP_DOMAIN must be a DNS host name, not a URL or placeholder."
}
if ($tlsEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') {
    throw "TLS_EMAIL is not an email address."
}
foreach ($name in "FRONTEND_API_BASE_URL", "CORS_ALLOWED_ORIGINS") {
    $uri = [Uri](Require-Value -Values $values -Name $name)
    if ($uri.Scheme -ne "https" -or $uri.Host -ne $appDomain -or -not [string]::IsNullOrWhiteSpace($uri.AbsolutePath.Trim('/'))) {
        throw "$name must be the exact HTTPS origin for APP_DOMAIN."
    }
}
if ($values["DATABASE_AUTO_MIGRATE"] -ne "false" -or $values["DATABASE_BASELINE_EXISTING_SCHEMA"] -ne "false") {
    throw "Production configuration must set DATABASE_AUTO_MIGRATE=false and DATABASE_BASELINE_EXISTING_SCHEMA=false."
}
if ($trustedProxyNetwork -notmatch '^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$') {
    throw "HOSTING_TRUSTED_PROXY_NETWORK must be an explicit CIDR."
}

$compose = @("--env-file", $resolvedEnvFile, "-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml", "config", "--format", "json")
$merged = (& docker compose @compose | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw "Production Compose configuration could not be rendered." }

$dbPorts = @($merged.services.db.ports)
if ($dbPorts.Count -ne 1 -or $dbPorts[0].host_ip -ne "127.0.0.1") {
    throw "Production database must bind only to 127.0.0.1."
}
$api = $merged.services.PSObject.Properties["lensee.host"].Value
if ($api.environment.Database__AutoMigrate -ne "false" -or $api.environment.Database__BaselineExistingSchema -ne "false") {
    throw "The production API Compose service permits schema mutation."
}
if ($api.environment.DataProtection__KeyRingPath -notmatch '^/') {
    throw "Data Protection key storage must be an absolute container path."
}
if (@($api.volumes | Where-Object { $_.target -eq $api.environment.DataProtection__KeyRingPath }).Count -ne 1) {
    throw "Data Protection key storage is not backed by exactly one persistent volume."
}

$caddyFile = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "deploy/Caddyfile")
if ($caddyFile -notmatch '\{\$APP_DOMAIN\}' -or $caddyFile -notmatch '\{\$TLS_EMAIL\}') {
    throw "Caddyfile does not use the configured domain and TLS email."
}
if ($caddyFile -notmatch '/health /ready') {
    throw "Caddyfile does not proxy health and readiness endpoints."
}

$alerts = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "deploy/observability/alerts.yml")
foreach ($requiredAlert in "LenseeOutboxDeadLetters", "LenseeCorrectionFailures") {
    if ($alerts -notmatch $requiredAlert) { throw "Missing required alert rule: $requiredAlert" }
}

$summary = @(
    "status=pass"
    "env-file=$([IO.Path]::GetFileName($resolvedEnvFile))"
    "app-domain=$appDomain"
    "database-loopback=true"
    "auto-migrate=false"
    "data-protection-persistent=true"
    "caddy-tls-shape=true"
    "alert-rules=true"
) -join [Environment]::NewLine

if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $EvidenceDirectory "production-config-verification.txt") -Value $summary -NoNewline
}
else {
    $summary
}
