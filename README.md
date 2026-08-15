# Lensee Production

Lensee Production is the deployable, integration-ready variant of the Lensee optical-retail management system. It brings catalog, inventory, CRM, operations, payments, reporting, notifications, and role-based identity together in an Arabic-first web application, with production-oriented configuration for database migration, data-protection keys, rate limiting, reverse proxies, and optional Shopify synchronization.

## Technology

- ASP.NET Core and .NET 8
- PostgreSQL 17 with Entity Framework Core and Npgsql
- MediatR modules, JWT authentication, and Swagger/OpenAPI
- Static HTML/CSS/JavaScript frontend served by Nginx
- Docker Compose deployment stack
- xUnit backend tests, Playwright end-to-end tests, and Postman collections

## Repository layout

```text
backend/       ASP.NET Core host, modules, migrations, and tests
frontend/      Arabic-first browser client
database/      Database assets
deploy/        Deployment configuration
docs/          Product and technical documentation
e2e/           Browser workflow tests
postman/       API collections
scripts/       Operational and bootstrap helpers
```

The backend modules cover Identity, Catalog, Inventory, CRM, Operations, Payments, Reporting, Notifications, and shared kernel code. The host includes optional Shopify webhook and inventory synchronization support.

## Run locally

### Prerequisites

- Docker Desktop with Docker Compose

### Start the stack

1. Create a local environment file and replace all placeholders before use:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Build and start the services:

   ```powershell
   docker compose up --build
   ```

3. Browse to `http://localhost:3001`. The API is available at `http://localhost:5000`; PostgreSQL is mapped to port `8181` for local development.

To stop services, use `docker compose down`. Do not append `-v` unless intentionally resetting local database data.

## Configuration

`.env.example` documents the required database and JWT settings as well as the production-facing controls:

- automatic migration and one-time schema baselining;
- rate-limit thresholds;
- trusted reverse-proxy network;
- optional Shopify store, webhook, and cash-on-delivery settings.

Keep production secrets in the deployment secret store. Never commit live credentials, webhook secrets, or signed integration tokens.

## Validation

Run the backend suite from the repository root:

```powershell
dotnet test backend/Lensee.Tests/Lensee.Tests.csproj
```

Additional verification assets are in `postman/` and `e2e/`.

## License

No license is currently declared. Treat the source as proprietary unless a license is added.
