#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/deploy-update.sh [--branch main] [--no-pull] [--no-cache]

Pulls the latest code, rebuilds production images, runs migrations, and restarts
the app/frontend/proxy services without deleting or recreating the production
database volume.
EOF
}

BRANCH="main"
PULL=true
NO_CACHE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --branch)
      BRANCH="${2:?--branch requires a value}"
      shift 2
      ;;
    --no-pull)
      PULL=false
      shift
      ;;
    --no-cache)
      NO_CACHE=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ ! -f docker-compose.yml || ! -f docker-compose.prod.yml || ! -f docker-compose.deploy.yml ]]; then
  echo "Run this script from the repository root that contains the Docker Compose files." >&2
  exit 1
fi

if [[ ! -f .env ]]; then
  echo ".env is required for production deployment." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "docker compose is required." >&2
  exit 1
fi

DC=(
  docker compose
  --project-name lenseeproduction
  --env-file .env
  -f docker-compose.yml
  -f docker-compose.prod.yml
  -f docker-compose.deploy.yml
)

if [[ "$PULL" == true ]]; then
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "Refusing to pull with a dirty checkout. Commit, stash, or remove local changes first." >&2
    git status --short >&2
    exit 1
  fi

  git fetch origin "$BRANCH"
  git pull --ff-only origin "$BRANCH"
fi

"${DC[@]}" config --quiet

BUILD_ARGS=()
if [[ "$NO_CACHE" == true ]]; then
  BUILD_ARGS+=(--no-cache)
fi

"${DC[@]}" build "${BUILD_ARGS[@]}" lensee.host migrator frontend
"${DC[@]}" up -d db
"${DC[@]}" --profile migrate run --rm --no-deps migrator
"${DC[@]}" up -d --no-deps lensee.host frontend caddy
"${DC[@]}" ps

echo "Deployment update complete. Production database volume was not removed."
