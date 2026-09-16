param(
    [ValidateSet("Local", "Production")]
    [string]$Environment = "Local",

    [string]$ComposeProjectName = "",
    [string]$DbService = "",
    [string]$DbContainer = "",
    [string]$Database = "lensee",
    [string]$DbUser = "lensee_user",
    [string]$SqlPath = "",

    # Required only when Environment=Production.
    [string]$ConfirmProductionSeed = ""
)

$ErrorActionPreference = "Stop"

function Get-ComposeArgs {
    param([string[]]$Tail = @())

    $composeArgsLocal = @("compose")
    if (-not [string]::IsNullOrWhiteSpace($ComposeProjectName)) {
        $composeArgsLocal += @("--project-name", $ComposeProjectName)
    }
    $composeArgsLocal += $Tail
    return $composeArgsLocal
}

function Get-RunningComposeServices {
    $composePsArgs = Get-ComposeArgs -Tail @("ps", "--services", "--status", "running")
    $output = & docker @composePsArgs 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    return @(
        $output |
            ForEach-Object { "$_".Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Get-RunningContainers {
    $output = & docker ps --format '{{.Names}}|{{.Image}}' 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    $items = @()
    foreach ($line in $output) {
        $parts = "$line".Split("|", 2)
        if ($parts.Count -eq 2) {
            $items += [pscustomobject]@{
                Name  = $parts[0].Trim()
                Image = $parts[1].Trim()
            }
        }
    }
    return $items
}

function Resolve-DbTarget {
    if (-not [string]::IsNullOrWhiteSpace($DbContainer)) {
        $running = & docker inspect -f '{{.State.Running}}' $DbContainer 2>$null
        if ($LASTEXITCODE -ne 0 -or "$running".Trim().ToLowerInvariant() -ne "true") {
            throw "Database container '$DbContainer' does not exist or is not running."
        }

        return @{
            Mode = "Container"
            Name = $DbContainer
        }
    }

    $services = @(Get-RunningComposeServices)

    if (-not [string]::IsNullOrWhiteSpace($DbService)) {
        if ($services -notcontains $DbService) {
            $shown = if ($services.Count -gt 0) { $services -join ", " } else { "<none>" }
            throw "Compose database service '$DbService' is not running. Running services: $shown"
        }

        return @{
            Mode = "Compose"
            Name = $DbService
        }
    }

    # Prefer conventional Compose service names.
    foreach ($candidate in @("db", "postgres", "postgresql", "database")) {
        if ($services -contains $candidate) {
            return @{
                Mode = "Compose"
                Name = $candidate
            }
        }
    }

    # Then accept a Compose service whose name clearly looks PostgreSQL-related.
    $serviceMatch = $services |
        Where-Object { $_ -match '(?i)postgres|(^|[-_])db($|[-_])|database' } |
        Select-Object -First 1

    if ($serviceMatch) {
        return @{
            Mode = "Compose"
            Name = $serviceMatch
        }
    }

    # Finally support production deployments where PostgreSQL is a standalone
    # Docker container rather than a service in the current Compose project.
    if ($Environment -ne "Production") {
        throw "Could not find a running PostgreSQL Compose service. Specify -DbService or -DbContainer explicitly."
    }

    $containers = @(Get-RunningContainers)
    $postgresContainers = @(
        $containers | Where-Object {
            $_.Image -match '(?i)postgres' -or
            $_.Name -match '(?i)postgres|lensee.*db|db.*lensee'
        }
    )

    if ($postgresContainers.Count -gt 1) {
        $lenseeMatch = $postgresContainers |
            Where-Object { $_.Name -match '(?i)lensee' } |
            Select-Object -First 1
        if ($lenseeMatch) {
            return @{
                Mode = "Container"
                Name = $lenseeMatch.Name
            }
        }

        $names = ($postgresContainers | ForEach-Object { $_.Name }) -join ", "
        throw "Multiple PostgreSQL containers are running ($names). Specify -DbContainer explicitly."
    }

    if ($postgresContainers.Count -eq 1) {
        return @{
            Mode = "Container"
            Name = $postgresContainers[0].Name
        }
    }

    throw @"
Could not find a running PostgreSQL target.

For Compose, pass:
  -DbService <service-name>

For a standalone production container, pass:
  -DbContainer <container-name>

Useful checks:
  docker compose ps
  docker compose ps --services
  docker ps --format "table {{.Names}}\t{{.Image}}"
"@
}

function Invoke-PostgresFile {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [hashtable]$Target
    )

    Write-Host "Environment : $Environment"
    Write-Host "DB target   : $($Target.Mode) '$($Target.Name)'"
    Write-Host "Database    : $Database"
    Write-Host "DB user     : $DbUser"
    Write-Host "SQL file    : $Path"

    if ($Target.Mode -eq "Compose") {
        $dockerArgs = Get-ComposeArgs -Tail @(
            "exec", "-T", $Target.Name,
            "psql", "-v", "ON_ERROR_STOP=1", "-U", $DbUser, "-d", $Database
        )
    }
    else {
        $dockerArgs = @(
            "exec", "-i", $Target.Name,
            "psql", "-v", "ON_ERROR_STOP=1", "-U", $DbUser, "-d", $Database
        )
    }

    Get-Content -Raw -LiteralPath $Path | & docker @dockerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Direct DB lens variant seed failed with exit code $LASTEXITCODE."
    }
}

if ($Environment -eq "Production" -and $ConfirmProductionSeed -ne "SEED PRODUCTION LENSES") {
    throw @"
Production lens seeding requires explicit confirmation.

Re-run with:
  -Environment Production -ConfirmProductionSeed 'SEED PRODUCTION LENSES'
"@
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($SqlPath)) {
    $candidates = @(
        (Join-Path $repoRoot "scripts/seed-lens-variants-direct.sql"),
        (Join-Path $repoRoot "database/seed-lens-variants-direct.sql")
    )

    $resolvedSql = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $resolvedSql) {
        throw "Could not find seed-lens-variants-direct.sql under database/ or scripts/. Use -SqlPath to specify it."
    }
    $SqlPath = $resolvedSql
}
else {
    if (-not [System.IO.Path]::IsPathRooted($SqlPath)) {
        $SqlPath = Join-Path $repoRoot $SqlPath
    }
    if (-not (Test-Path -LiteralPath $SqlPath)) {
        throw "SQL seed file not found: $SqlPath"
    }
    $SqlPath = (Resolve-Path -LiteralPath $SqlPath).Path
}

$target = Resolve-DbTarget
Invoke-PostgresFile -Path $SqlPath -Target $target

Write-Host "Direct DB lens variant seed complete."
