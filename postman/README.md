# Lensee Postman Smoke Suite

This folder contains the generated full PRD-MVP Postman/Newman smoke suite.

## Run From PowerShell

Start the API container first:

```powershell
docker compose up -d --build lensee.host
```

Run the full suite and prepare deterministic auth/location data:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-postman-tests.ps1 -BaseUrl http://localhost:5000 -AdminPassword Admin123! -PrepareAuthData
```

The runner regenerates the collection/environment, seeds dev users and locations, runs Newman, and writes JSON results to:

```text
postman/results/
```

## Import Manually In Postman

Import these two files:

```text
postman/Lensee_PRD_MVP_Full.postman_collection.json
postman/Lensee_Local.postman_environment.json
```

Use environment `Lensee Local`, then run the collection.

## Seed Lens Catalog Through HTTP

The lens/catalog seed is a separate Postman collection that uses the API, not SQL. It logs in as Admin and creates categories, brands, products, and SKUs through catalog endpoints, so the backend SKU generator remains the source of truth.

Run with Newman:

```powershell
npm run postman:seed-lenses
```

The seed creates hundreds of SKUs. The collection throttles each API call to stay under the default backend limit of 120 requests per minute, and still waits/retries if it receives `429 Too Many Requests`. You can tune both delays:

```powershell
npx newman run postman/Lensee_Lens_Seed.postman_collection.json `
  --env-var baseUrl=http://localhost:5000 `
  --env-var adminUsername=admin `
  --env-var adminPassword=Admin123! `
  --env-var requestThrottleMs=550 `
  --env-var rateLimitBackoffMs=65000
```

The seed uses this category structure:

- `Lenses`
- `Lenses > Medical Lenses`
- `Lenses > Colored Lenses`
- `Solution`

The seed creates:

- Plain Medical Lens Box: Clear Vision, color `Plain`, 3 pieces/pack, sealed-pack-only.
- Plain Medical Lens Vial: Clear Vision, color `Plain`, 1 piece/pack, sealed-pack-only.
- Clear Vision Colored Medical Lens Pack: 12 colors, 2 pieces/pack, single-piece.
- Clear Vision Multi-Purpose Solution: sample sizes `60ml`, `120ml`, `250ml`, `360ml`.

Expected SKU counts after a successful run:

- Plain Medical Lens Box: 75 active SKUs.
- Plain Medical Lens Vial: 75 active SKUs.
- Clear Vision Colored Medical Lens Pack: 708 active SKUs.
- Clear Vision Multi-Purpose Solution: 4 active SKUs.

Double-word colors remain readable in generated SKUs, for example:

```text
CV-CM-M05-GALAXYGRAY-PACK2
CV-CM-M05-SELENAGRAY-PACK2
```

## Coverage

The suite covers auth, users, catalog, CRM, inventory, operations, payments, return/change/write-off, stocktake, reports/PDF/CSV, notifications/alerts, and role authorization checks.
