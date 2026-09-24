[CmdletBinding()]
param(
    [string]$OutputDirectory = "tmp/migration-integrity"
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

# The EF tooling needs a constructible host, but --no-connect/script generation
# must never target a persistent database. These values are process-local audit
# placeholders and are not used for database mutation.
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=1;Database=migration_integrity_audit;Username=audit;Password=audit'
$env:Jwt__Secret = 'migration-integrity-audit-non-production-secret-123456789'

$startupProject = 'backend/Lensee.Host/Lensee.Host.csproj'
$contexts = @(
    @{ Name = 'IdentityDbContext'; Project = 'backend/Lensee.Modules.Identity/Lensee.Modules.Identity.csproj' },
    @{ Name = 'CatalogDbContext'; Project = 'backend/Lensee.Modules.Catalog/Lensee.Modules.Catalog.csproj' },
    @{ Name = 'InventoryDbContext'; Project = 'backend/Lensee.Modules.Inventory/Lensee.Modules.Inventory.csproj' },
    @{ Name = 'CrmDbContext'; Project = 'backend/Lensee.Modules.CRM/Lensee.Modules.CRM.csproj' },
    @{ Name = 'FinanceDbContext'; Project = 'backend/Lensee.Modules.Finance/Lensee.Modules.Finance.csproj' },
    @{ Name = 'OperationsDbContext'; Project = 'backend/Lensee.Modules.Operations/Lensee.Modules.Operations.csproj' },
    @{ Name = 'PaymentsDbContext'; Project = 'backend/Lensee.Modules.Payments/Lensee.Modules.Payments.csproj' },
    @{ Name = 'NotificationsDbContext'; Project = 'backend/Lensee.Modules.Notifications/Lensee.Modules.Notifications.csproj' },
    @{ Name = 'ReportingDbContext'; Project = 'backend/Lensee.Modules.Reporting/Lensee.Modules.Reporting.csproj' },
    @{ Name = 'SharedDbContext'; Project = 'backend/Lensee.SharedKernel/Lensee.SharedKernel.csproj' }
)

function Invoke-EfAuditCommand {
    param([string]$Label, [string[]]$Arguments)
    $log = Join-Path $outputPath "$Label.log"
    & dotnet @Arguments *> $log
    if ($LASTEXITCODE -ne 0) {
        throw "EF migration validation failed: $Label. See $log"
    }
}

$summary = [System.Collections.Generic.List[object]]::new()
foreach ($context in $contexts) {
    $prefix = $context.Name -replace 'DbContext$', ''
    $common = @('--project', $context.Project, '--startup-project', $startupProject, '--context', $context.Name, '--no-build')

    Invoke-EfAuditCommand "$prefix-migrations-list" (@('ef', 'migrations', 'list') + $common + '--no-connect')
    Invoke-EfAuditCommand "$prefix-pending-model" (@('ef', 'migrations', 'has-pending-model-changes') + $common)
    Invoke-EfAuditCommand "$prefix-script" (@('ef', 'migrations', 'script', '0') + $common + @('--output', (Join-Path $outputPath "$prefix-from-zero.sql")))
    Invoke-EfAuditCommand "$prefix-idempotent-script" (@('ef', 'migrations', 'script', '--idempotent') + $common + @('--output', (Join-Path $outputPath "$prefix-idempotent.sql")))
    $summary.Add([pscustomobject]@{ DbContext = $context.Name; MigrationList = 'Passed'; PendingModel = 'Passed'; ScriptFromZero = 'Passed'; IdempotentScript = 'Passed' })
}

$summary | Export-Csv (Join-Path $outputPath 'ef-validation-summary.csv') -NoTypeInformation
$summary | Format-Table -AutoSize
