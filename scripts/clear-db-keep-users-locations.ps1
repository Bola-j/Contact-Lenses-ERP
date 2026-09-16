[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$ConfirmReset,
    [string]$DatabaseName = "lensee"
)

$ErrorActionPreference = "Stop"

if (-not $Apply) {
    Write-Host "Preview mode: no data will be changed. Use -Apply -ConfirmReset 'RESET BUSINESS DATA' to execute." -ForegroundColor Yellow
}

if ($Apply -and $ConfirmReset -ne "RESET BUSINESS DATA") {
    throw "Apply requires -ConfirmReset 'RESET BUSINESS DATA'."
}

if ($DatabaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "DatabaseName must contain only letters, numbers, and underscores."
}

$context = ((docker context show 2>$null) -join "").Trim()
if ($context -and $context -notin @("default", "desktop-linux")) {
    throw "Refusing to run against Docker context '$context'. This utility is local-only."
}

$services = @(docker compose config --services)
if ($LASTEXITCODE -ne 0 -or $services -notcontains "db") {
    throw "The local Docker Compose database service was not found."
}

if ($Apply) {
    $writer = @(docker compose ps --status running -q lensee.host)
    if ($writer.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace(($writer -join ""))) {
        throw "Stop the lensee.host writer service before applying the reset. Preview mode may run while it is active."
    }
}

$sql = @'
\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE reset_targets(schema_name text, table_name text) ON COMMIT DROP;
INSERT INTO reset_targets(schema_name, table_name)
SELECT schemaname, tablename
FROM pg_tables
WHERE schemaname IN ('crm', 'inventory', 'notifications', 'operations', 'payments', 'reporting', 'shared', 'identity')
  AND tablename <> '__EFMigrationsHistory'
  AND NOT (schemaname = 'identity' AND tablename IN ('users', 'roles_permissions'))
  AND NOT (schemaname = 'inventory' AND tablename = 'locations')
  AND NOT (schemaname = 'shared' AND tablename = 'system_settings');

SELECT schema_name || '.' || table_name AS table_name,
       (xpath('//*[local-name()="c"]/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', schema_name, table_name), true, false, '')))[1]::text::bigint AS rows_before
FROM reset_targets
ORDER BY schema_name, table_name;

SELECT format(
  'Preserved before: users=%s, permissions=%s, catalog tables=%s, locations=%s, settings=%s',
  (SELECT count(*) FROM identity.users),
  (SELECT count(*) FROM identity.roles_permissions),
  (SELECT count(*) FROM pg_tables WHERE schemaname = 'catalog'),
  (SELECT count(*) FROM inventory.locations),
  (SELECT count(*) FROM shared.system_settings)
) AS preservation_before;

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

SELECT format(
  'Preserved users=%s, permissions=%s, catalog tables=%s, locations=%s, settings=%s',
  (SELECT count(*) FROM identity.users),
  (SELECT count(*) FROM identity.roles_permissions),
  (SELECT count(*) FROM pg_tables WHERE schemaname = 'catalog'),
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

$sql | docker compose exec -T lensee_db psql -v ON_ERROR_STOP=1 -U lensee_user -d $DatabaseName
if ($LASTEXITCODE -ne 0) {
    throw "The local business-data reset failed. PostgreSQL rolled back the transaction."
}

if ($Apply) {
    Write-Host "Business data reset committed. Master data and migration history were preserved." -ForegroundColor Green
}
