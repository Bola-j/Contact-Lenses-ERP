# Document audit runbook

Run the audit against the already-running local DEV stack:

```powershell
$env:LENSEE_AUDIT_USERNAME = '<reporting-user>'
$env:LENSEE_AUDIT_PASSWORD = '<password>'
python scripts/run-document-audit.py
```

The command drives the Reports SPA first, selects **All matching rows**, exports every catalogued format/language variant, then checks equivalent direct routes. It writes only hashes, derived document checks, screenshots, and status evidence to `artifacts/overnight/document-audit/<timestamp>/`; raw documents remain in `tmp/document-audit/<timestamp>/`.

The audit deliberately does not seed data, apply migrations, or repair records. A missing operational schema, no eligible document, failed login, or inaccessible report is recorded as `BLOCKED`, never as a pass.

For a populated DEV database, the report is complete only when every row/total has a corresponding database reconciliation query. The current preflight blocks that stage if required module schemas are absent.
