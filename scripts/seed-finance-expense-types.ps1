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
        if ($services -contains $DbService) {
            return @{
                Mode = "Compose"
                Name = $DbService
            }
        }

        if ($Environment -eq "Production") {
            $running = & docker inspect -f '{{.State.Running}}' $DbService 2>$null
            $image = & docker inspect -f '{{.Config.Image}}' $DbService 2>$null
            if ($LASTEXITCODE -eq 0 -and "$running".Trim().ToLowerInvariant() -eq "true" -and
                ("$image" -match '(?i)postgres' -or $DbService -match '(?i)postgres|lensee.*db')) {
                return @{
                    Mode = "Container"
                    Name = $DbService
                }
            }
        }

        $shown = if ($services.Count -gt 0) { $services -join ", " } else { "<none>" }
        throw "Compose database service '$DbService' is not running. Running services: $shown"
    }

    foreach ($candidate in @("db", "postgres", "postgresql", "database")) {
        if ($services -contains $candidate) {
            return @{
                Mode = "Compose"
                Name = $candidate
            }
        }
    }

    $serviceMatch = $services |
        Where-Object { $_ -match '(?i)postgres|(^|[-_])db($|[-_])|database' } |
        Select-Object -First 1

    if ($serviceMatch) {
        return @{
            Mode = "Compose"
            Name = $serviceMatch
        }
    }

    if ($Environment -ne "Production") {
        throw "Could not find a running PostgreSQL Compose service. Specify -DbService or -DbContainer explicitly."
    }

    $postgresContainers = @(
        Get-RunningContainers | Where-Object {
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

    throw "Could not find a running PostgreSQL target. Specify -DbService or -DbContainer explicitly."
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

    $remotePath = "/tmp/lensee-finance-expense-types-$([guid]::NewGuid().ToString("N")).sql"
    $containerRef = $Target.Name
    if ($Target.Mode -eq "Compose") {
        $containerIdArgs = Get-ComposeArgs -Tail @("ps", "-q", $Target.Name)
        $containerRef = (& docker @containerIdArgs | Select-Object -First 1).Trim()
        if ([string]::IsNullOrWhiteSpace($containerRef)) {
            throw "Could not resolve Compose service '$($Target.Name)' to a container."
        }
    }

    & docker cp $Path "${containerRef}:$remotePath"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not copy SQL seed file into PostgreSQL container."
    }

    try {
        if ($Target.Mode -eq "Compose") {
            $dockerArgs = Get-ComposeArgs -Tail @(
                "exec", "-T", $Target.Name,
                "psql", "-v", "ON_ERROR_STOP=1", "-U", $DbUser, "-d", $Database, "-f", $remotePath
            )
        }
        else {
            $dockerArgs = @(
                "exec", $Target.Name,
                "psql", "-v", "ON_ERROR_STOP=1", "-U", $DbUser, "-d", $Database, "-f", $remotePath
            )
        }

        & docker @dockerArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Finance expense type seed failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        & docker exec $containerRef rm -f $remotePath 2>$null | Out-Null
    }
}

if ($Environment -eq "Production" -and $ConfirmProductionSeed -ne "SEED PRODUCTION EXPENSE TYPES") {
    throw @"
Production expense type seeding requires explicit confirmation.

Re-run with:
  -Environment Production -ConfirmProductionSeed 'SEED PRODUCTION EXPENSE TYPES'
"@
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($SqlPath)) {
    $SqlPath = Join-Path $repoRoot "scripts/seed-finance-expense-types.sql"
}
else {
    if (-not [System.IO.Path]::IsPathRooted($SqlPath)) {
        $SqlPath = Join-Path $repoRoot $SqlPath
    }
}

if (-not (Test-Path -LiteralPath $SqlPath)) {
    throw "SQL seed file not found: $SqlPath"
}
$SqlPath = (Resolve-Path -LiteralPath $SqlPath).Path

$target = Resolve-DbTarget
Invoke-PostgresFile -Path $SqlPath -Target $target

Write-Host "Finance expense type seed complete."
