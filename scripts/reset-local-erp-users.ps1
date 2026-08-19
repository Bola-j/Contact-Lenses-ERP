param(
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$Username = "bola",
    [string]$FullName = "Lansee Admin",
    [switch]$Confirm
)

$ErrorActionPreference = "Stop"

if (-not $Confirm) {
    throw "This resets local ERP and identity data. Run again with -Confirm and a new password for the primary Administrator."
}

if ($Password.Length -lt 8) {
    throw "Password must be at least 8 characters."
}

$sql = @'
DO $$
DECLARE
  tables_to_clear text;
BEGIN
  SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY schemaname, tablename)
  INTO tables_to_clear
  FROM pg_tables
  WHERE schemaname IN (
    'catalog', 'crm', 'identity', 'inventory', 'notifications',
    'operations', 'payments', 'reporting', 'shared'
  )
  AND (schemaname, tablename) NOT IN (
    ('inventory', 'locations'),
    ('identity', 'roles_permissions')
  );

  IF tables_to_clear IS NOT NULL THEN
    EXECUTE 'TRUNCATE TABLE ' || tables_to_clear || ' RESTART IDENTITY';
  END IF;
END $$;

SELECT format(
  'Reset complete. Preserved %s locations and %s role permissions.',
  (SELECT count(*) FROM inventory.locations),
  (SELECT count(*) FROM identity.roles_permissions)
);
'@

$sql | docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee

& "$PSScriptRoot\bootstrap-admin.ps1" -Username $Username -FullName $FullName -Password $Password -Primary

Write-Host "Local reset complete. '$Username' is the only user and primary Administrator."
