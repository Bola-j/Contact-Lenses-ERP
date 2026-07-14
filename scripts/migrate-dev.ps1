param(
    [switch]$BaselineExisting,
    [switch]$SeedLocations
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$hostProject = Join-Path $repoRoot "backend/Lensee.Host/Lensee.Host.csproj"
$hostDir = Split-Path -Parent $hostProject
$dbPassword = $env:DB_PASSWORD
if ([string]::IsNullOrWhiteSpace($dbPassword)) {
    $envPath = Join-Path $repoRoot ".env"
    if (Test-Path -LiteralPath $envPath) {
        $envPasswordLine = Get-Content -LiteralPath $envPath |
            Where-Object { $_ -match "^\s*DB_PASSWORD\s*=" } |
            Select-Object -First 1

        if ($envPasswordLine) {
            $dbPassword = ($envPasswordLine -split "=", 2)[1].Trim().Trim('"').Trim("'")
        }
    }
}

if ([string]::IsNullOrWhiteSpace($dbPassword)) {
    $dbPassword = "SomeStrongPassword123!"
}

$defaultConnection = "Host=127.0.0.1;Port=8181;Database=lensee;Username=lensee_user;Password=$dbPassword"

$contexts = @(
    @{ Project = "backend/Lensee.SharedKernel/Lensee.SharedKernel.csproj"; Context = "SharedDbContext"; Migrations = "backend/Lensee.SharedKernel/Migrations" },
    @{ Project = "backend/Lensee.Modules.Identity/Lensee.Modules.Identity.csproj"; Context = "IdentityDbContext"; Migrations = "backend/Lensee.Modules.Identity/Migrations" },
    @{ Project = "backend/Lensee.Modules.Catalog/Lensee.Modules.Catalog.csproj"; Context = "CatalogDbContext"; Migrations = "backend/Lensee.Modules.Catalog/Migrations" },
    @{ Project = "backend/Lensee.Modules.Inventory/Lensee.Modules.Inventory.csproj"; Context = "InventoryDbContext"; Migrations = "backend/Lensee.Modules.Inventory/Migrations" },
    @{ Project = "backend/Lensee.Modules.CRM/Lensee.Modules.CRM.csproj"; Context = "CrmDbContext"; Migrations = "backend/Lensee.Modules.CRM/Migrations" },
    @{ Project = "backend/Lensee.Modules.Operations/Lensee.Modules.Operations.csproj"; Context = "OperationsDbContext"; Migrations = "backend/Lensee.Modules.Operations/Migrations" },
    @{ Project = "backend/Lensee.Modules.Payments/Lensee.Modules.Payments.csproj"; Context = "PaymentsDbContext"; Migrations = "backend/Lensee.Modules.Payments/Migrations" },
    @{ Project = "backend/Lensee.Modules.Notifications/Lensee.Modules.Notifications.csproj"; Context = "NotificationsDbContext"; Migrations = "backend/Lensee.Modules.Notifications/Migrations" },
    @{ Project = "backend/Lensee.Modules.Reporting/Lensee.Modules.Reporting.csproj"; Context = "ReportingDbContext"; Migrations = "backend/Lensee.Modules.Reporting/Migrations" }
)

function Invoke-DbSql {
    param([Parameter(Mandatory = $true)][string]$Sql)

    $Sql | docker compose exec -T db psql -U lensee_user -d lensee
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

docker compose up -d db

for ($attempt = 1; $attempt -le 30; $attempt++) {
    docker compose exec -T db pg_isready -U lensee_user -d lensee | Out-Null
    if ($LASTEXITCODE -eq 0) {
        break
    }

    if ($attempt -eq 30) {
        throw "PostgreSQL did not become ready in time."
    }

    Start-Sleep -Seconds 1
}

if ($BaselineExisting) {
    Invoke-DbSql 'create table if not exists "__EFMigrationsHistory" ("MigrationId" character varying(150) not null primary key, "ProductVersion" character varying(32) not null);'

    foreach ($item in $contexts) {
        $migrationPath = Join-Path $repoRoot $item.Migrations
        Get-ChildItem -LiteralPath $migrationPath -Filter "*.cs" |
            Where-Object { $_.Name -notlike "*.Designer.cs" -and $_.BaseName -match "^\d{14}_Initial" } |
            Sort-Object Name |
            ForEach-Object {
                $migrationId = $_.BaseName.Replace("'", "''")
                Invoke-DbSql "insert into ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") values ('$migrationId', '8.0.27') on conflict (""MigrationId"") do nothing;"
            }
    }

    Write-Host "Existing schema baselined for initial migrations. Pending hardening migrations will run now."
}

$escapedDbPassword = Escape-SqlLiteral $dbPassword
Invoke-DbSql "alter user lensee_user with password '$escapedDbPassword';"

Push-Location $hostDir
try {
    $env:ConnectionStrings__DefaultConnection = $defaultConnection
    $dotnetEf = Join-Path $env:USERPROFILE ".dotnet/tools/dotnet-ef.exe"
    if (-not (Test-Path -LiteralPath $dotnetEf)) {
        $dotnetEf = "dotnet-ef"
    }
    foreach ($item in $contexts) {
        $project = Join-Path $repoRoot $item.Project
        & $dotnetEf database update --project $project --startup-project $hostProject --context $item.Context --connection $defaultConnection
        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed for $($item.Context)."
        }
    }
} finally {
    Pop-Location
}

if ($SeedLocations) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot "database/seed-locations.sql") | docker compose exec -T db psql -U lensee_user -d lensee
}

Write-Host "Migration flow complete."
