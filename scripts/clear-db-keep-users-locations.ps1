$ErrorActionPreference = "Stop"

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
    ('identity', 'users'),
    ('inventory', 'locations')
  );

  IF tables_to_clear IS NOT NULL THEN
    EXECUTE 'TRUNCATE TABLE ' || tables_to_clear || ' RESTART IDENTITY';
  END IF;
END $$;

SELECT format(
  'Cleanup complete. Preserved %s users and %s locations.',
  (SELECT count(*) FROM identity.users),
  (SELECT count(*) FROM inventory.locations)
);
'@

$sql | docker compose exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee
