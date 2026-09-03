#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/reset-admin-password.sh [--username admin] [--full-name "Lensee Admin"] [--no-primary]

Resets or creates an active Admin user in the production Docker Compose database.
The password is read from the terminal without echoing it, so it is not stored in shell history.
EOF
}

USERNAME="admin"
FULL_NAME="Lensee Admin"
PRIMARY=true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --username)
      USERNAME="${2:?--username requires a value}"
      shift 2
      ;;
    --full-name)
      FULL_NAME="${2:?--full-name requires a value}"
      shift 2
      ;;
    --no-primary)
      PRIMARY=false
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

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required to generate the PBKDF2 password hash." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "docker compose is required." >&2
  exit 1
fi

read -r -s -p "New password for ${USERNAME}: " PASSWORD
echo
read -r -s -p "Confirm password: " PASSWORD_CONFIRM
echo

if [[ "$PASSWORD" != "$PASSWORD_CONFIRM" ]]; then
  echo "Passwords do not match." >&2
  exit 1
fi

if [[ ${#PASSWORD} -lt 8 ]]; then
  echo "Password must be at least 8 characters." >&2
  exit 1
fi

PASSWORD_HASH="$(PASSWORD="$PASSWORD" python3 - <<'PY'
import base64
import hashlib
import os

password = os.environ["PASSWORD"].encode("utf-8")
salt = os.urandom(16)
key = hashlib.pbkdf2_hmac("sha256", password, salt, 100000, dklen=32)
print(
    "pbkdf2-sha256.100000."
    + base64.b64encode(salt).decode("ascii")
    + "."
    + base64.b64encode(key).decode("ascii")
)
PY
)"
unset PASSWORD PASSWORD_CONFIRM

DC=(
  docker compose
  --project-name lenseeproduction
  --env-file .env
  -f docker-compose.yml
  -f docker-compose.prod.yml
  -f docker-compose.deploy.yml
)

if [[ "$PRIMARY" == true ]]; then
  PRIMARY_SQL="update identity.users set is_primary_admin = false where is_primary_admin;"
  PRIMARY_VALUE="true"
  PRIMARY_MESSAGE=" as the primary administrator"
else
  PRIMARY_SQL=""
  PRIMARY_VALUE="false"
  PRIMARY_MESSAGE=""
fi

"${DC[@]}" exec -T db psql \
  -v ON_ERROR_STOP=1 \
  -v username="$USERNAME" \
  -v full_name="$FULL_NAME" \
  -v password_hash="$PASSWORD_HASH" \
  -v primary_value="$PRIMARY_VALUE" \
  -U lensee_user \
  -d lensee <<SQL
begin;
${PRIMARY_SQL}
with upserted_user as (
  insert into identity.users (
    username,
    password_hash,
    full_name,
    role,
    location_id,
    is_active,
    is_primary_admin
  )
  values (
    :'username',
    :'password_hash',
    :'full_name',
    'Admin',
    null,
    true,
    :'primary_value'::boolean
  )
  on conflict (upper(btrim(username))) do update
  set password_hash = excluded.password_hash,
      full_name = excluded.full_name,
      role = 'Admin',
      location_id = null,
      is_active = true,
      is_primary_admin = excluded.is_primary_admin
  returning id
)
delete from identity.refresh_tokens
where user_id in (select id from upserted_user);
commit;
SQL

echo "Admin user '${USERNAME}' password reset${PRIMARY_MESSAGE}; existing sessions were revoked."
