#!/usr/bin/env bash
set -euo pipefail

# Destructively clears application data from the Lensee Compose database while
# preserving PostgreSQL itself, EF migration history, and schema definitions.
# Intended only for a disposable/test production deployment.

PROJECT_NAME="${COMPOSE_PROJECT_NAME:-lenseeproduction}"
EXPECTED_CONFIRMATION="RESET lenseeproduction/lensee"

if [[ "$PROJECT_NAME" != "lenseeproduction" ]]; then
  echo "Refusing: expected Compose project lenseeproduction, got '$PROJECT_NAME'." >&2
  exit 1
fi

if [[ ! -f docker-compose.yml || ! -f docker-compose.prod.yml || ! -f docker-compose.deploy.yml ]]; then
  echo "Run this script from the repository root containing the production Compose files." >&2
  exit 1
fi
if [[ ! -f .env ]]; then
  echo "Missing .env. Run this script from the deployed repo root." >&2
  exit 1
fi
command -v docker >/dev/null || { echo "docker is required." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required." >&2; exit 1; }

DC=(docker compose --project-name "$PROJECT_NAME" --env-file .env \
  -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml)

db_id="$("${DC[@]}" ps -q db)"
if [[ -z "$db_id" ]]; then
  echo "Refusing: Compose project '$PROJECT_NAME' has no running db service." >&2
  exit 1
fi
db_name="$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.service" }}' "$db_id")"
db_project="$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' "$db_id")"
db_image="$(docker inspect --format '{{.Config.Image}}' "$db_id")"
if [[ "$db_name" != "db" || "$db_project" != "$PROJECT_NAME" || "$db_image" != postgres:17-alpine ]]; then
  echo "Refusing: running database identity did not match expected Compose service/project/image." >&2
  exit 1
fi

echo "TARGET: Compose project=$db_project service=$db_name image=$db_image database=lensee"
echo "This permanently deletes all application rows in every non-system schema."
echo "PostgreSQL schemas and EF migration history will be preserved."
read -r -p "Type '$EXPECTED_CONFIRMATION' to continue: " confirmation
if [[ "$confirmation" != "$EXPECTED_CONFIRMATION" ]]; then
  echo "Confirmation did not match; no data was changed." >&2
  exit 1
fi

# Stop application writers, retaining the database and proxy. Re-start API and
# frontend even if the transactional truncate fails.
"${DC[@]}" stop lensee.host frontend
restart_app() {
  "${DC[@]}" up -d --no-deps lensee.host frontend >/dev/null || true
}
trap restart_app EXIT

"${DC[@]}" exec -T db psql -v ON_ERROR_STOP=1 -U lensee_user -d lensee <<'SQL'
BEGIN;
DO $$
DECLARE
  targets text;
BEGIN
  SELECT string_agg(format('%I.%I', schemaname, tablename), ', ' ORDER BY schemaname, tablename)
    INTO targets
  FROM pg_tables
  WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
    AND tablename <> '__EFMigrationsHistory';

  IF targets IS NOT NULL THEN
    EXECUTE 'TRUNCATE TABLE ' || targets || ' RESTART IDENTITY CASCADE';
  END IF;
END $$;
COMMIT;
SQL

echo "Application data cleared. PostgreSQL migration history and schema were preserved."
