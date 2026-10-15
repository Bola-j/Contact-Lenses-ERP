# Automatic production CD

For a push to `main`, the deployment chain is:

1. `CI` runs the required fast PR-equivalent checks.
2. If CI succeeds, `Certification Evidence` runs the full certification against
   that exact commit.
3. Only when every certification job passes, GitHub builds and publishes the
   API and frontend images tagged with that same 40-character commit SHA.
4. Only after `Certification Evidence` completes successfully does the
   self-hosted production runner take a database backup, deploy those images,
   and verify health/readiness. Failed application health triggers application
   image rollback.

Nightly/scheduled and manually started certification do not publish release
images or deploy. Only certification triggered by a successful `CI` run on
`main` can publish and deploy. There is no direct deploy trigger that bypasses
the CI-and-certification chain.

The self-hosted runner must have the custom `lensee-production` label and Docker
access as the `lensee` service user. The runner's *name* does not create this
label. In GitHub, open **Settings → Actions → Runners**, edit the registered
runner's labels, and add `lensee-production`. Confirm it is online and retains
the default `self-hosted` and `linux` labels. The deployment script does the following:

1. Checks out the certified commit.
2. Pulls the API and frontend images tagged with that exact commit SHA.
3. Records the currently running application image IDs.
4. Waits for PostgreSQL and creates a compressed `pg_dump` backup under
   `/var/backups/lensee`.
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

## GitHub repository variables

Under **Settings → Secrets and variables → Actions → Variables**, set:

- `LENSEE_PRODUCTION_ENV_FILE` = `/home/lensee/production/.env`
- `LENSEE_PRODUCTION_BACKUP_DIR` = `/var/backups/lensee`

These are paths, not secret values. Keep all contents of `.env` on the VPS.

## First live deployment check

After the runner label and variables are configured, the next commit pushed to
`main` that passes CI will run certification, publish its images, and
automatically start CD. Verify the workflow's exact SHA, backup metadata under
`/var/backups/lensee`, the deploy job's Docker health and `/health` + `/ready`
checks, and the public application behavior. Scheduled/manual certification
does not deploy production.
