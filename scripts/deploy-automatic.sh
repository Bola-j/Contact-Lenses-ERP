#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/deploy-automatic.sh --env-file PATH --backup-dir PATH --release-sha SHA

Deploys the already checked-out candidate with Docker Compose. The database is
backed up before the migrator runs. Application containers are restored to
their previous image IDs if migration, startup, or health verification fails.
Database migrations are intentionally not reversed automatically.
EOF
}

ENV_FILE=""
BACKUP_DIR=""
RELEASE_SHA=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file) ENV_FILE="${2:?--env-file requires a value}"; shift 2 ;;
    --backup-dir) BACKUP_DIR="${2:?--backup-dir requires a value}"; shift 2 ;;
    --release-sha) RELEASE_SHA="${2:?--release-sha requires a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ -n "$ENV_FILE" && -f "$ENV_FILE" ]] || { echo "A valid --env-file is required." >&2; exit 2; }
[[ "$RELEASE_SHA" =~ ^[0-9a-f]{40}$ ]] || { echo "--release-sha must be a 40-character SHA." >&2; exit 2; }
[[ -n "$BACKUP_DIR" ]] || { echo "--backup-dir is required." >&2; exit 2; }

for required_file in docker-compose.yml docker-compose.prod.yml docker-compose.deploy.yml; do
  [[ -f "$required_file" ]] || { echo "Missing $required_file." >&2; exit 1; }
done

DC=(docker compose --project-name lenseeproduction --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml)
mkdir -p "$BACKUP_DIR"

old_api_image="$("${DC[@]}" images -q lensee.host | head -n 1)"
old_frontend_image="$("${DC[@]}" images -q frontend | head -n 1)"
[[ -n "$old_api_image" && -n "$old_frontend_image" ]] || { echo "Could not identify the currently running application images." >&2; exit 1; }
old_api_container="$("${DC[@]}" ps -q lensee.host | head -n 1)"
old_frontend_container="$("${DC[@]}" ps -q frontend | head -n 1)"
old_api_ref="$(docker inspect --format '{{.Config.Image}}' "$old_api_container")"
old_frontend_ref="$(docker inspect --format '{{.Config.Image}}' "$old_frontend_container")"
app_domain="$(grep -E '^[[:space:]]*APP_DOMAIN=' "$ENV_FILE" | tail -n 1 | cut -d= -f2- | tr -d "\"'[:space:]")"
[[ "$app_domain" =~ ^[A-Za-z0-9.-]+$ ]] || { echo "APP_DOMAIN is missing or invalid in the deployment environment file." >&2; exit 1; }

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="$BACKUP_DIR/lensee-${timestamp}-${RELEASE_SHA}.dump"
metadata_path="$backup_path.metadata"
rollback_api_ref="lenseeproduction-rollback-api:${RELEASE_SHA}-${timestamp}"
rollback_frontend_ref="lenseeproduction-rollback-frontend:${RELEASE_SHA}-${timestamp}"
rollback_needed=false

rollback() {
  local exit_code=$?
  if [[ "$rollback_needed" != true ]]; then
    exit "$exit_code"
  fi

  trap - EXIT
  echo "Deployment failed; restoring previous application image IDs." >&2
  local rollback_env_file
  rollback_env_file="$(mktemp)"
  local rollback_succeeded=false
  if docker tag "$old_api_image" "$rollback_api_ref" && docker tag "$old_frontend_image" "$rollback_frontend_ref"; then
    awk '!/^[[:space:]]*(LENSEE_HOST_IMAGE|LENSEE_FRONTEND_IMAGE)=/' "$ENV_FILE" > "$rollback_env_file"
    printf '\nLENSEE_HOST_IMAGE=%s\nLENSEE_FRONTEND_IMAGE=%s\n' \
      "$rollback_api_ref" "$rollback_frontend_ref" >> "$rollback_env_file"
    local -a rollback_dc=(docker compose --project-name lenseeproduction --env-file "$rollback_env_file" -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.deploy.yml)
    if "${rollback_dc[@]}" up -d --no-deps lensee.host frontend caddy; then
      for attempt in $(seq 1 60); do
        if curl --fail --silent --max-time 10 --resolve "${app_domain}:443:127.0.0.1" "https://${app_domain}/health" >/dev/null 2>&1 && \
          curl --fail --silent --max-time 10 --resolve "${app_domain}:443:127.0.0.1" "https://${app_domain}/ready" >/dev/null 2>&1; then
          rollback_succeeded=true
          break
        fi
        [[ "$attempt" -lt 60 ]] || break
        sleep 2
      done
    fi
  fi
  rm -f "$rollback_env_file"
  if [[ "$rollback_succeeded" == true ]]; then
    echo "Previous application images restored and readiness checks passed." >&2
  else
    echo "AUTOMATIC APPLICATION ROLLBACK DID NOT VERIFY HEALTH. VPS OPERATOR ACTION IS REQUIRED." >&2
  fi
  echo "Database migrations were not reversed automatically. Backup retained at: $backup_path" >&2
  exit "$exit_code"
}
trap rollback EXIT

"${DC[@]}" config --quiet
if "${DC[@]}" config --images | grep -q '^ghcr.io/'; then
  "${DC[@]}" pull lensee.host migrator frontend
else
  "${DC[@]}" build lensee.host migrator frontend
fi

"${DC[@]}" up -d db
for attempt in $(seq 1 30); do
  if "${DC[@]}" exec -T db pg_isready -U lensee_user -d lensee >/dev/null 2>&1; then break; fi
  [[ "$attempt" -lt 30 ]] || { echo "Database did not become ready." >&2; exit 1; }
  sleep 2
done

"${DC[@]}" exec -T db pg_dump -U lensee_user -d lensee -Fc > "$backup_path"
[[ -s "$backup_path" ]] || { echo "Database backup was empty." >&2; exit 1; }
backup_sha256="$(sha256sum "$backup_path" | awk '{print $1}')"
printf 'release-sha=%s\ncreated-at=%s\nbackup=%s\nsha256=%s\n' \
  "$RELEASE_SHA" "$timestamp" "$backup_path" "$backup_sha256" > "$metadata_path"
printf 'previous-api-image-ref=%s\nprevious-api-image-id=%s\nprevious-frontend-image-ref=%s\nprevious-frontend-image-id=%s\n' \
  "$old_api_ref" "$old_api_image" "$old_frontend_ref" "$old_frontend_image" >> "$metadata_path"

rollback_needed=true
"${DC[@]}" --profile migrate run --rm --no-deps migrator
"${DC[@]}" up -d --no-deps lensee.host frontend caddy

for attempt in $(seq 1 60); do
  api_id="$("${DC[@]}" ps -q lensee.host | head -n 1)"
  frontend_id="$("${DC[@]}" ps -q frontend | head -n 1)"
  api_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$api_id" 2>/dev/null || true)"
  frontend_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$frontend_id" 2>/dev/null || true)"
  if [[ "$api_health" == healthy && "$frontend_health" == healthy ]]; then break; fi
  [[ "$attempt" -lt 60 ]] || { echo "Application services did not become healthy." >&2; exit 1; }
  sleep 2
done

curl --fail --silent --show-error --max-time 10 --resolve "${app_domain}:443:127.0.0.1" "https://${app_domain}/health" >/dev/null
curl --fail --silent --show-error --max-time 10 --resolve "${app_domain}:443:127.0.0.1" "https://${app_domain}/ready" >/dev/null

printf 'release-sha=%s\nbackup=%s\nbackup-sha256=%s\nstatus=success\n' \
  "$RELEASE_SHA" "$backup_path" "$backup_sha256"
rollback_needed=false
trap - EXIT
