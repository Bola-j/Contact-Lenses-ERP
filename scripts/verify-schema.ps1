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

$historyCount = 'select count(*) from "__EFMigrationsHistory";' | docker compose @composeFiles exec -T db psql -U $databaseUser -d $databaseName -At

Write-Host "Schema verification passed. EF migrations recorded: $historyCount"
