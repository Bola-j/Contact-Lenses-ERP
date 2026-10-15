# Automatic production CD

The production CD workflow runs only after the `CI` workflow and then the
`Certification Evidence` workflow succeed for the same commit on `main`.

The self-hosted runner must have the `lensee-production` label and Docker
access as the `lensee` service user. The deployment script does the following:

1. Checks out the certified commit.
2. Pulls the API and frontend images tagged with that exact commit SHA.
3. Records the currently running application image IDs.
4. Waits for PostgreSQL and creates a compressed `pg_dump` backup.
5. Runs the explicit migrator.
6. Starts the API, frontend, and Caddy services.
7. Verifies Docker health plus `/health` and `/ready`.
8. Restores the previous application image IDs if startup or health checks fail.

Database migrations are not reversed automatically. Restore the recorded dump
using the database recovery runbook after reviewing the migration and taking a
fresh safety backup.

## Required repository variables

Configure these variables in the repository's production deployment settings:

| Variable | Example | Meaning |
| --- | --- | --- |
| `LENSEE_PRODUCTION_ENV_FILE` | `/home/lensee/production/.env` | Runner-readable copy of the existing VPS-only production environment file |
| `LENSEE_PRODUCTION_BACKUP_DIR` | `/var/backups/lensee` | VPS directory for compressed database backups and metadata |

The environment file must already contain the required production values,
including `APP_DOMAIN`, `TLS_EMAIL`, `DB_PASSWORD`, `JWT_SECRET`,
`FRONTEND_API_BASE_URL`, and `CORS_ALLOWED_ORIGINS`. Do not commit that file or
copy its contents into GitHub Actions logs.

## VPS preparation

Run once as root, using the actual paths selected for this installation:

```bash
install -d -o lensee -g lensee -m 750 /home/lensee/production
install -o root -g lensee -m 640 /root/Lensee/.env /home/lensee/production/.env
install -d -o lensee -g lensee -m 750 /var/backups/lensee
test -f /home/lensee/production/.env
sudo -u lensee docker ps
```

Before enabling unattended deployment, run one `workflow_dispatch` deployment
against a known-good commit and verify the backup file, service health, public
readiness endpoint, and application behavior. Only then keep the automatic
`workflow_run` trigger enabled.
