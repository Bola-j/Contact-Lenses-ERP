param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [switch]$SeedLocations
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$hostProject = Join-Path $repoRoot "backend/Lensee.Host/Lensee.Host.csproj"
$hostDir = Split-Path -Parent $hostProject

$contexts = @(
    @{ Project = "backend/Lensee.SharedKernel/Lensee.SharedKernel.csproj"; Context = "SharedDbContext" },
    @{ Project = "backend/Lensee.Modules.Identity/Lensee.Modules.Identity.csproj"; Context = "IdentityDbContext" },
    @{ Project = "backend/Lensee.Modules.Catalog/Lensee.Modules.Catalog.csproj"; Context = "CatalogDbContext" },
    @{ Project = "backend/Lensee.Modules.Inventory/Lensee.Modules.Inventory.csproj"; Context = "InventoryDbContext" },
    @{ Project = "backend/Lensee.Modules.CRM/Lensee.Modules.CRM.csproj"; Context = "CrmDbContext" },
    @{ Project = "backend/Lensee.Modules.Operations/Lensee.Modules.Operations.csproj"; Context = "OperationsDbContext" },
    @{ Project = "backend/Lensee.Modules.Payments/Lensee.Modules.Payments.csproj"; Context = "PaymentsDbContext" },
    @{ Project = "backend/Lensee.Modules.Notifications/Lensee.Modules.Notifications.csproj"; Context = "NotificationsDbContext" },
    @{ Project = "backend/Lensee.Modules.Reporting/Lensee.Modules.Reporting.csproj"; Context = "ReportingDbContext" }
)

Push-Location $hostDir
try {
    $env:ConnectionStrings__DefaultConnection = $ConnectionString
    $env:Database__AutoMigrate = "false"
    foreach ($item in $contexts) {
        $project = Join-Path $repoRoot $item.Project
        dotnet tool run dotnet-ef database update --project $project --startup-project $hostProject --context $item.Context --connection $ConnectionString
        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed for $($item.Context)."
        }
    }
} finally {
    Pop-Location
}

if ($SeedLocations) {
    Write-Host "SeedLocations is intended for Docker/local production-style runs. Apply database/seed-locations.sql with psql using the same connection string."
}

Write-Host "Production migration flow complete."
