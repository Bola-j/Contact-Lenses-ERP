$ErrorActionPreference = "Stop"

$sql = @"
TRUNCATE TABLE
  catalog.brands,
  catalog.categories,
  catalog.skus,
  catalog.products,
  crm.merchant_notes,
  crm.merchants,
  crm.representatives,
  identity.audit_logs,
  identity.refresh_tokens,
  identity.roles_permissions,
  inventory.inventory_batches,
  inventory.opened_piece_lots,
  inventory.stock_balances,
  inventory.stock_transactions,
  notifications.alert_configs,
  notifications.notification_logs,
  operations.inventory_receipt_headers,
  operations.operation_lines,
  operations.operation_logs,
  operations.operation_versions,
  operations.stocktake_adjustment_lines,
  operations.stocktake_sessions,
  payments.cash_records,
  payments.financial_adjustments,
  payments.installment_sub_logs,
  payments.main_payment_logs,
  reporting.export_logs,
  shared.system_settings
RESTART IDENTITY CASCADE;
"@

$sql | docker compose exec -T db psql -U lensee_user -d lensee

Write-Host "Cleanup complete. Preserved tables: inventory.locations, identity.users"
