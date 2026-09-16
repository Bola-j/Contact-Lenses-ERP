[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$ConfirmReset,
    [ValidateSet("Local", "Production")]
    [string]$Environment = "Local",
    [string]$ComposeProjectName = "",
    [string]$DatabaseName = "lensee",
    [string]$DbUser = "lensee_user",
    [string]$DbService = "",
    [string]$DbContainer = "",
    [string]$WriterService = "",
    [string]$WriterContainer = "",
    [switch]$StopWriter
)

$ErrorActionPreference = "Stop"

function Get-ComposeArgs {
    $args = @("compose")
    if (-not [string]::IsNullOrWhiteSpace($ComposeProjectName)) {
        $args += @("--project-name", $ComposeProjectName)
    }
    return ,$args
}

function Get-RunningComposeServices {
    $composeArgs = Get-ComposeArgs
    $output = @(& docker @composeArgs "ps" "--services" "--status" "running" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return @()
    }
    return @($output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
}

function Get-ConfiguredComposeServices {
    $composeArgs = Get-ComposeArgs
    $output = @(& docker @composeArgs "config" "--services" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return @()
    }
    return @($output | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
}

function Test-ContainerRunning {
    param([Parameter(Mandatory = $true)][string]$NameOrId)
    $state = ((& docker inspect -f '{{.State.Running}}' $NameOrId 2>$null) -join "").Trim()
    return ($LASTEXITCODE -eq 0 -and $state -eq "true")
}

function Resolve-DatabaseTarget {
    if (-not [string]::IsNullOrWhiteSpace($DbContainer)) {
        if (-not (Test-ContainerRunning -NameOrId $DbContainer)) {
            throw "Database container '$DbContainer' is not running."
        }
        return [pscustomobject]@{ Mode = "container"; Name = $DbContainer }
    }

    $configured = @(Get-ConfiguredComposeServices)
    $running = @(Get-RunningComposeServices)

    if (-not [string]::IsNullOrWhiteSpace($DbService)) {
        if ($configured.Count -gt 0 -and $configured -notcontains $DbService) {
            throw "Compose service '$DbService' does not exist. Configured services: $($configured -join ', ')."
        }
        if ($running -notcontains $DbService) {
            throw "Compose database service '$DbService' exists but is not running."
        }
        return [pscustomobject]@{ Mode = "compose"; Name = $DbService }
    }

    foreach ($candidate in @("db", "postgres", "postgresql", "database")) {
        if ($running -contains $candidate) {
            return [pscustomobject]@{ Mode = "compose"; Name = $candidate }
        }
    }

    $postgresContainers = @(
        & docker ps --format '{{.ID}}|{{.Names}}|{{.Image}}' 2>$null |
        ForEach-Object {
            $parts = ([string]$_).Split("|", 3)
            if ($parts.Count -eq 3 -and $parts[2] -match '(?i)postgres' -and $parts[1] -match '(?i)lensee') {
                [pscustomobject]@{ Id = $parts[0]; Name = $parts[1]; Image = $parts[2] }
            }
        }
    )

    if ($postgresContainers.Count -eq 1) {
        return [pscustomobject]@{ Mode = "container"; Name = $postgresContainers[0].Name }
    }

    if ($postgresContainers.Count -gt 1) {
        throw "More than one running Lensee PostgreSQL container was found: $($postgresContainers.Name -join ', '). Re-run with -DbContainer <name>."
    }

    $runningText = if ($running.Count -gt 0) { $running -join ", " } else { "<none>" }
    throw "Could not resolve a running PostgreSQL target. Running compose services: $runningText. Use -DbService <service> or -DbContainer <container>."
}

function Resolve-WriterTarget {
    if (-not [string]::IsNullOrWhiteSpace($WriterContainer)) {
        if (Test-ContainerRunning -NameOrId $WriterContainer) {
            return [pscustomobject]@{ Mode = "container"; Name = $WriterContainer }
        }
        return $null
    }

    $running = @(Get-RunningComposeServices)

    if (-not [string]::IsNullOrWhiteSpace($WriterService)) {
        if ($running -contains $WriterService) {
            return [pscustomobject]@{ Mode = "compose"; Name = $WriterService }
        }
        return $null
    }

    foreach ($candidate in @("lensee.host", "api", "backend", "host")) {
        if ($running -contains $candidate) {
            return [pscustomobject]@{ Mode = "compose"; Name = $candidate }
        }
    }

    $writerContainers = @(
        & docker ps --format '{{.Names}}|{{.Image}}' 2>$null |
        ForEach-Object {
            $parts = ([string]$_).Split("|", 2)
            if ($parts.Count -eq 2 -and $parts[0] -match '(?i)lensee' -and $parts[0] -match '(?i)(api|host|backend)') {
                [pscustomobject]@{ Name = $parts[0]; Image = $parts[1] }
            }
        }
    )

    if ($writerContainers.Count -eq 1) {
        return [pscustomobject]@{ Mode = "container"; Name = $writerContainers[0].Name }
    }

    if ($writerContainers.Count -gt 1) {
        throw "More than one possible Lensee writer container was found: $($writerContainers.Name -join ', '). Use -WriterService or -WriterContainer."
    }

    return $null
}

function Stop-WriterTarget {
    param([Parameter(Mandatory = $true)]$Target)
    if ($Target.Mode -eq "compose") {
        $composeArgs = Get-ComposeArgs
        & docker @composeArgs "stop" $Target.Name | Out-Host
    }
    else {
        & docker stop $Target.Name | Out-Host
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to stop writer '$($Target.Name)'."
    }
}

function Start-WriterTarget {
    param([Parameter(Mandatory = $true)]$Target)
    if ($Target.Mode -eq "compose") {
        $composeArgs = Get-ComposeArgs
        & docker @composeArgs "start" $Target.Name | Out-Host
    }
    else {
        & docker start $Target.Name | Out-Host
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restart writer '$($Target.Name)'."
    }
}

function Invoke-Psql {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    if ($Target.Mode -eq "compose") {
        $composeArgs = Get-ComposeArgs
        $Sql | & docker @composeArgs "exec" "-T" $Target.Name "psql" "-v" "ON_ERROR_STOP=1" "-U" $DbUser "-d" $DatabaseName
    }
    else {
        $Sql | & docker exec -i $Target.Name psql -v ON_ERROR_STOP=1 -U $DbUser -d $DatabaseName
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Business-data reset failed. PostgreSQL should have rolled back the transaction."
    }
}

if (-not $Apply) {
    Write-Host "Preview mode: no data will be changed." -ForegroundColor Yellow
}

if ($DatabaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "DatabaseName must contain only letters, numbers, and underscores."
}
if ($DbUser -notmatch '^[A-Za-z0-9_]+$') {
    throw "DbUser must contain only letters, numbers, and underscores."
}

$requiredConfirmation = if ($Environment -eq "Production") {
    "RESET PRODUCTION BUSINESS DATA"
}
else {
    "RESET BUSINESS DATA"
}

if ($Apply -and $ConfirmReset -ne $requiredConfirmation) {
    throw "Apply in $Environment mode requires -ConfirmReset '$requiredConfirmation'."
}

if ($Environment -eq "Local") {
    $context = ((docker context show 2>$null) -join "").Trim()
    if ($context -and $context -notin @("default", "desktop-linux")) {
        throw "Local mode refuses Docker context '$context'. Use -Environment Production only when you intentionally target a production Docker context/server."
    }
}

$dbTarget = Resolve-DatabaseTarget
Write-Host "Environment: $Environment" -ForegroundColor Cyan
Write-Host "Database target: $($dbTarget.Mode) '$($dbTarget.Name)' / DB '$DatabaseName' / user '$DbUser'" -ForegroundColor Cyan

$writerTarget = $null
$writerWasStopped = $false

if ($Apply) {
    $writerTarget = Resolve-WriterTarget
    if ($null -ne $writerTarget) {
        if (-not $StopWriter) {
            $targetHint = if ($writerTarget.Mode -eq "compose") {
                "-WriterService '$($writerTarget.Name)'"
            }
            else {
                "-WriterContainer '$($writerTarget.Name)'"
            }
            throw "Writer '$($writerTarget.Name)' is running. Re-run with -StopWriter $targetHint so the script stops it before reset and restarts it afterward."
        }

        Write-Host "Stopping writer '$($writerTarget.Name)' before reset..." -ForegroundColor Yellow
        Stop-WriterTarget -Target $writerTarget
        $writerWasStopped = $true
    }
    else {
        Write-Warning "No running Lensee writer service/container was detected. Make sure no API/worker is writing to this database."
    }
}

$sql = @'
\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE reset_targets(schema_name text, table_name text) ON COMMIT DROP;
INSERT INTO reset_targets(schema_name, table_name)
SELECT schemaname, tablename
FROM pg_tables
WHERE schemaname IN ('catalog', 'crm', 'inventory', 'notifications', 'operations', 'payments', 'reporting', 'shared', 'identity')
  AND tablename <> '__EFMigrationsHistory'
  AND NOT (schemaname = 'identity' AND tablename IN ('users', 'roles_permissions'))
  AND NOT (schemaname = 'inventory' AND tablename = 'locations')
  AND NOT (schemaname = 'shared' AND tablename = 'system_settings');

SELECT schema_name || '.' || table_name AS table_name,
       (xpath('//*[local-name()="c"]/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', schema_name, table_name), true, false, '')))[1]::text::bigint AS rows_before
FROM reset_targets
ORDER BY schema_name, table_name;

CREATE TEMP TABLE preservation_snapshot ON COMMIT DROP AS
SELECT
  (SELECT count(*) FROM identity.users) AS users_count,
  (SELECT count(*) FROM identity.roles_permissions) AS permissions_count,
  (SELECT count(*) FROM inventory.locations) AS locations_count,
  (SELECT count(*) FROM shared.system_settings) AS settings_count;

SELECT format(
  'Preserved before: users=%s, permissions=%s, locations=%s, settings=%s',
  users_count,
  permissions_count,
  locations_count,
  settings_count
) AS preservation_before
FROM preservation_snapshot;

DO $$
DECLARE
  target_list text;
BEGIN
  SELECT string_agg(format('%I.%I', schema_name, table_name), ', ' ORDER BY schema_name, table_name)
    INTO target_list
  FROM reset_targets;

  IF :'APPLY' = 'true' AND target_list IS NOT NULL THEN
    EXECUTE 'TRUNCATE TABLE ' || target_list || ' RESTART IDENTITY CASCADE';
  END IF;
END $$;

SELECT schema_name || '.' || table_name AS table_name,
       (xpath('//*[local-name()="c"]/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', schema_name, table_name), true, false, '')))[1]::text::bigint AS rows_after
FROM reset_targets
ORDER BY schema_name, table_name;

DO $$
DECLARE
  snap preservation_snapshot%ROWTYPE;
  current_users bigint;
  current_permissions bigint;
  current_locations bigint;
  current_settings bigint;
BEGIN
  SELECT * INTO snap FROM preservation_snapshot;

  SELECT count(*) INTO current_users FROM identity.users;
  SELECT count(*) INTO current_permissions FROM identity.roles_permissions;
  SELECT count(*) INTO current_locations FROM inventory.locations;
  SELECT count(*) INTO current_settings FROM shared.system_settings;

  IF current_users <> snap.users_count
     OR current_permissions <> snap.permissions_count
     OR current_locations <> snap.locations_count
     OR current_settings <> snap.settings_count THEN
    RAISE EXCEPTION
      'Preservation check failed. users %->%, permissions %->%, locations %->%, settings %->%',
      snap.users_count, current_users,
      snap.permissions_count, current_permissions,
      snap.locations_count, current_locations,
      snap.settings_count, current_settings;
  END IF;
END $$;

SELECT format(
  'Preserved users=%s, permissions=%s, locations=%s, settings=%s',
  (SELECT count(*) FROM identity.users),
  (SELECT count(*) FROM identity.roles_permissions),
  (SELECT count(*) FROM inventory.locations),
  (SELECT count(*) FROM shared.system_settings)
) AS preservation_check;

ROLLBACK;
'@

$applyLiteral = if ($Apply) { "true" } else { "false" }
$sql = $sql.Replace(":'APPLY'", "'$applyLiteral'")
if ($Apply) {
    $sql = $sql.Replace("ROLLBACK;", "COMMIT;")
}

try {
    Invoke-Psql -Target $dbTarget -Sql $sql
    if ($Apply) {
        Write-Host "Business and catalog data reset committed. Users, permissions, locations, settings, and migration history were preserved." -ForegroundColor Green
    }
    else {
        Write-Host "Preview completed and rolled back." -ForegroundColor Green
    }
}
finally {
    if ($writerWasStopped -and $null -ne $writerTarget) {
        Write-Host "Restarting writer '$($writerTarget.Name)'..." -ForegroundColor Yellow
        Start-WriterTarget -Target $writerTarget
    }
}
