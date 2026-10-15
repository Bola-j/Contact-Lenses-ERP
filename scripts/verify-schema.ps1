param(
    [switch]$Production,
    [switch]$E2E
)

$ErrorActionPreference = "Stop"

if ($Production -and $E2E) {
    throw "Choose either -Production or -E2E, not both."
}

$composeFiles = if ($Production) {
    @("-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "-f", "docker-compose.deploy.yml")
} elseif ($E2E) {
    @("-f", "docker-compose.yml", "-f", "docker-compose.e2e.yml")
} else {
    @()
}

$databaseUser = if ($E2E) { "lensee_e2e_user" } else { "lensee_user" }
$databaseName = if ($E2E) { "lensee_e2e" } else { "lensee" }

$requiredTables = @(
    "catalog.products",
    "catalog.skus",
    "inventory.locations",
    "inventory.stock_balances",
    "inventory.inventory_batches",
    "operations.operation_logs",
    "operations.operation_lines",
    "payments.main_payment_logs",
    "payments.installment_sub_logs",
    "payments.cash_records",
    "payments.financial_adjustments",
    "finance.finance_accounts",
    "finance.finance_ledger_entries",
    "finance.finance_opening_balances",
    "finance.payment_movement_registry",
    "finance.finance_expenses",
    "finance.c_level_withdrawals",
    "finance.reconciliation_import_packages",
    "finance.reconciliation_import_rows",
    "reporting.export_logs"
)

$requiredConstraints = @(
    "chk_op_type",
    "chk_op_status",
    "chk_txn_type",
    "chk_cash_payment_type",
    "chk_main_payment_status",
    "chk_stocktake_status"
)

$tablesSql = @"
select table_schema || '.' || table_name
from information_schema.tables
where table_schema in ('catalog','inventory','operations','payments','finance','reporting')
order by 1;
"@

$constraintsSql = @"
select conname
from pg_constraint
where conname in ($(($requiredConstraints | ForEach-Object { "'$_'" }) -join ","))
order by conname;
"@

$tables = $tablesSql | docker compose @composeFiles exec -T db psql -U $databaseUser -d $databaseName -At
$constraints = $constraintsSql | docker compose @composeFiles exec -T db psql -U $databaseUser -d $databaseName -At

$missingTables = $requiredTables | Where-Object { $tables -notcontains $_ }
$missingConstraints = $requiredConstraints | Where-Object { $constraints -notcontains $_ }

if ($missingTables.Count -gt 0 -or $missingConstraints.Count -gt 0) {
    if ($missingTables.Count -gt 0) {
        Write-Error "Missing tables: $($missingTables -join ', ')"
    }

    if ($missingConstraints.Count -gt 0) {
        Write-Error "Missing constraints: $($missingConstraints -join ', ')"
    }

    exit 1
}

$operationConstraintSql = @"
select conname || '|' || pg_get_constraintdef(c.oid)
from pg_constraint c
join pg_class t on t.oid = c.conrelid
join pg_namespace n on n.oid = t.relnamespace
where n.nspname = 'operations'
  and t.relname = 'operation_logs'
  and c.conname in ('chk_op_status', 'chk_op_type')
order by conname;
"@

$operationConstraintRows = $operationConstraintSql | docker compose @composeFiles exec -T db psql -U $databaseUser -d $databaseName -At
$operationConstraints = @{}
foreach ($row in $operationConstraintRows) {
    $parts = $row -split '\|', 2
    if ($parts.Count -eq 2) { $operationConstraints[$parts[0]] = $parts[1] }
}

$statusDefinition = $operationConstraints['chk_op_status']
$typeDefinition = $operationConstraints['chk_op_type']
$expectedOperationStatuses = @('Draft', 'PendingApproval', 'Approved', 'Confirmed', 'Completed', 'Reserved', 'Shipped', 'Received', 'Cancelled', 'Rejected', 'Dispatched', 'Delivered', 'FailedDelivery')
$expectedOperationTypes = @('InventoryReceipt', 'WarehouseTransfer', 'WholesaleSale', 'RetailSale', 'Reserve', 'WriteOff', 'StocktakeAdjustment', 'Change', 'Return')

function Assert-ConstraintValues {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Definition,
        [Parameter(Mandatory = $true)][string[]]$ExpectedValues
    )

    if ([string]::IsNullOrWhiteSpace($Definition)) {
        throw "$Name is missing from operations.operation_logs."
    }

    $actualValues = [regex]::Matches($Definition, "'([^']+)'") |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    $difference = Compare-Object -ReferenceObject ($ExpectedValues | Sort-Object) -DifferenceObject $actualValues
    if ($difference) {
        $changedValues = ($difference | ForEach-Object { $_.InputObject }) -join ', '
        throw "$Name does not exactly match the current Operations contract: $changedValues."
    }
}

Assert-ConstraintValues -Name 'chk_op_status' -Definition $statusDefinition -ExpectedValues $expectedOperationStatuses
Assert-ConstraintValues -Name 'chk_op_type' -Definition $typeDefinition -ExpectedValues $expectedOperationTypes

$historyCount = 'select count(*) from "__EFMigrationsHistory";' | docker compose @composeFiles exec -T db psql -U $databaseUser -d $databaseName -At

Write-Host "Schema verification passed. EF migrations recorded: $historyCount"
