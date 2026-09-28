#!/usr/bin/env bash
set -Eeuo pipefail

# Direct PostgreSQL runner for the Clear Vision lens seed.
# Defaults to Local. Production requires an explicit confirmation phrase.
ENVIRONMENT="${ENVIRONMENT:-Local}"
COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-}"
DB_SERVICE="${DB_SERVICE:-}"
DB_CONTAINER="${DB_CONTAINER:-}"
DATABASE="${DATABASE:-lensee}"
DB_USER="${DB_USER:-lensee_user}"
SQL_PATH="${SQL_PATH:-}"
CONFIRM_PRODUCTION_SEED="${CONFIRM_PRODUCTION_SEED:-}"

usage() {
  cat <<'EOF'
Usage: scripts/seed-lenses-direct-db.sh [options]

Options:
  --environment Local|Production
  --compose-project-name NAME
  --db-service NAME
  --db-container NAME
  --database NAME
  --db-user NAME
  --sql-path PATH
  --confirm-production-seed 'SEED PRODUCTION LENSES'
  -h, --help

Environment variables with the same uppercase names are also supported.
Production mode requires --confirm-production-seed 'SEED PRODUCTION LENSES'.
EOF
}

while (($#)); do
  case "$1" in
    --environment) ENVIRONMENT="${2:?Missing value for --environment}"; shift 2 ;;
    --compose-project-name) COMPOSE_PROJECT_NAME="${2:?Missing value for --compose-project-name}"; shift 2 ;;
    --db-service) DB_SERVICE="${2:?Missing value for --db-service}"; shift 2 ;;
    --db-container) DB_CONTAINER="${2:?Missing value for --db-container}"; shift 2 ;;
    --database) DATABASE="${2:?Missing value for --database}"; shift 2 ;;
    --db-user) DB_USER="${2:?Missing value for --db-user}"; shift 2 ;;
    --sql-path) SQL_PATH="${2:?Missing value for --sql-path}"; shift 2 ;;
    --confirm-production-seed) CONFIRM_PRODUCTION_SEED="${2:?Missing value for --confirm-production-seed}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

case "$ENVIRONMENT" in
  Local|Production) ;;
  *) echo "Environment must be Local or Production." >&2; exit 2 ;;
esac

if [[ "$ENVIRONMENT" == Production && "$CONFIRM_PRODUCTION_SEED" != "SEED PRODUCTION LENSES" ]]; then
  cat >&2 <<'EOF'
Production lens seeding requires explicit confirmation.
Re-run with: --environment Production --confirm-production-seed 'SEED PRODUCTION LENSES'
EOF
  exit 2
fi

command -v docker >/dev/null 2>&1 || { echo "docker is required." >&2; exit 127; }
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
if [[ -z "$SQL_PATH" ]]; then
  for candidate in "$REPO_ROOT/scripts/seed-lens-variants-direct.sql" "$REPO_ROOT/database/seed-lens-variants-direct.sql"; do
    if [[ -f "$candidate" ]]; then SQL_PATH="$candidate"; break; fi
  done
else
  [[ "$SQL_PATH" = /* ]] || SQL_PATH="$REPO_ROOT/$SQL_PATH"
fi
[[ -n "$SQL_PATH" && -f "$SQL_PATH" ]] || { echo "SQL seed file not found: $SQL_PATH" >&2; exit 2; }

compose=(docker compose)
[[ -z "$COMPOSE_PROJECT_NAME" ]] || compose+=(--project-name "$COMPOSE_PROJECT_NAME")

running_services() {
  "${compose[@]}" ps --services --status running 2>/dev/null || true
}

TARGET_MODE=""
TARGET_NAME=""
if [[ -n "$DB_CONTAINER" ]]; then
  running="$(docker inspect -f '{{.State.Running}}' "$DB_CONTAINER" 2>/dev/null || true)"
  [[ "$running" == true ]] || { echo "Database container '$DB_CONTAINER' does not exist or is not running." >&2; exit 2; }
  TARGET_MODE=Container
  TARGET_NAME="$DB_CONTAINER"
else
  mapfile -t services < <(running_services)
  if [[ -n "$DB_SERVICE" ]]; then
    found=false
    for service in "${services[@]}"; do [[ "$service" == "$DB_SERVICE" ]] && found=true; done
    [[ "$found" == true ]] || { printf "Compose database service '%s' is not running. Running services: %s\n" "$DB_SERVICE" "${services[*]:-<none>}" >&2; exit 2; }
    TARGET_MODE=Compose
    TARGET_NAME="$DB_SERVICE"
  else
    for candidate in db postgres postgresql database; do
      for service in "${services[@]}"; do
        if [[ "$service" == "$candidate" ]]; then TARGET_MODE=Compose; TARGET_NAME="$service"; break 2; fi
      done
    done
    if [[ -z "$TARGET_MODE" ]]; then
      for service in "${services[@]}"; do
        if [[ "$service" =~ postgres|(^|[-_])db($|[-_])|database ]]; then TARGET_MODE=Compose; TARGET_NAME="$service"; break; fi
      done
    fi
  fi

  if [[ -z "$TARGET_MODE" && "$ENVIRONMENT" == Production ]]; then
    mapfile -t containers < <(docker ps --format '{{.Names}}|{{.Image}}' 2>/dev/null | awk -F'|' 'tolower($2) ~ /postgres/ || tolower($1) ~ /postgres|lensee.*db|db.*lensee/ {print $1}')
    if ((${#containers[@]} > 1)); then
      lensee=""
      for container in "${containers[@]}"; do [[ "$container" =~ lensee ]] && { lensee="$container"; break; }; done
      if [[ -n "$lensee" ]]; then TARGET_MODE=Container; TARGET_NAME="$lensee";
      else printf 'Multiple PostgreSQL containers are running (%s). Specify --db-container.\n' "${containers[*]}" >&2; exit 2; fi
    elif ((${#containers[@]} == 1)); then TARGET_MODE=Container; TARGET_NAME="${containers[0]}";
    fi
  fi

  if [[ -z "$TARGET_MODE" ]]; then
    echo "Could not find a running PostgreSQL target. Specify --db-service; in Production, use --db-container for a standalone container." >&2
    exit 2
  fi
fi

printf 'Environment : %s\nDB target   : %s %s\nDatabase    : %s\nDB user     : %s\nSQL file    : %s\n' "$ENVIRONMENT" "$TARGET_MODE" "$TARGET_NAME" "$DATABASE" "$DB_USER" "$SQL_PATH"
if [[ "$TARGET_MODE" == Compose ]]; then
  "${compose[@]}" exec -T "$TARGET_NAME" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DATABASE" < "$SQL_PATH"
else
  docker exec -i "$TARGET_NAME" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DATABASE" < "$SQL_PATH"
fi
printf 'Direct DB lens variant seed complete.\n'
