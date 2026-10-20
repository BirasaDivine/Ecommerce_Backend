# Ecommerce Backend

An ASP.NET Core Web API for an e-commerce inventory and order management platform.

## Stack

- ASP.NET Core 10 Web API
- EF Core 10 with SQL Server 
- ASP.NET Core Identity for users/roles
- JWT bearer authentication
- NUnit + `WebApplicationFactory` for integration tests, EF Core InMemory for service-level tests
- Scalar for interactive API docs in Development (`/scalar/v1`)
- Azure Service Bus (local emulator) for asynchronous order processing
- Docker + Docker Compose for the full local stack

## Roles

- **Admin** — manages categories, products, variants, and collections.
- **User** — any authenticated account; can purchase products.
- **Public (unauthenticated)** — can browse and search products only.

## Getting started

### Prerequisites

- .NET 10 SDK
- SQL Server or SQL Server LocalDB (the default connection string targets `(localdb)\mssqllocaldb`)

### Configuration

`appsettings.json` holds the connection string and non-secret JWT settings (`Issuer`, `Audience`,
`ExpiryMinutes`). `appsettings.Development.json` ships a placeholder `Jwt:Key` for local development
only — it must not be reused anywhere else. For a real environment, override it with user secrets
or an environment variable instead of committing a key:

```bash
cd Ecommerce_Backend
dotnet user-secrets set "Jwt:Key" "<a long random string>"
```

Each configuration section is bound to an options class in `Configuration/` (`JwtOptions`,
`ServiceBusOptions`, `DatabaseOptions`, `HealthCheckSettingsOptions`, `SeedAdminOptions`) and
validated at startup, so the app fails fast with a clear message if, for example, `Jwt:Key` is
missing or shorter than 32 characters, or `ServiceBus:ConnectionString` is unset.

In Development only, a `SeedAdmin:Email` / `SeedAdmin:Password` pair (defaulting to
`admin@ecommerce.local` / `Admin123!`) is seeded on startup along with the `Admin` and `User` roles.
This seeding does not run outside Development.

`appsettings.json` also holds the non-secret `ServiceBus:QueueName`. `ServiceBus:ConnectionString`
follows the same rule as `Jwt:Key` . For local development it points at the Azure
Service Bus emulator , which accepts the fixed, non-secret development connection string:

```bash
cd Ecommerce_Backend
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
```

| Setting | Default | Purpose |
|---|---|---|
| `Database:MigrateOnStartup` | `false` | Apply pending EF Core migrations when the app starts (enabled in Docker Compose) |
| `HealthChecks:TimeoutSeconds` | `5` | Per-check timeout for the health endpoints (1–120) |

### Run everything with Docker Compose

The compose file runs the API, its SQL Server database, the Service Bus emulator and the emulator's
`sql-edge` backing store. Secrets come from a git-ignored `.env` file:

```bash
cp .env.example .env   # then replace the placeholder passwords and JWT key
docker compose up --build
```

- The API listens on `http://localhost:8080` (`API_PORT` to change it); Scalar is at `/scalar/v1`.
- The API container only starts once SQL Server answers queries and the emulator reports healthy.
- It runs with `Database__MigrateOnStartup=true`, so a fresh database volume gets its schema on first
  start, and then seeds the `Admin`/`User` roles and the admin from `SEED_ADMIN_EMAIL` /
  `SEED_ADMIN_PASSWORD`. EF Core takes a database lock while migrating, so concurrent instances are
  safe. Outside Compose the setting defaults to off, keeping migrations against a real database a
  deliberate step.
- The image is a multi-stage build (SDK to publish, `aspnet` runtime to run) that runs as the base
  image's non-root `app` user and contains only the API project.
- Database data lives in the `db-data` volume; `docker compose down -v` wipes it.

**Why SQL Server in Docker:** the EF Core provider and all existing migrations target SQL Server, and
the current search and uniqueness checks rely on its case-insensitive default collation. Switching to
PostgreSQL would have meant regenerating every migration and changing that behaviour, for a smaller
image. The `sql-edge` container is separate: it is required by the Service Bus emulator itself and is
not the app's database.

### Local Service Bus emulator only

Order placement publishes to Azure Service Bus, consumed by an in-process background service. To run
just the emulator (e.g. for `dotnet run` or the integration tests), with `.env` in place:

```bash
docker compose up -d servicebus-emulator
```

This starts `sql-edge` (the emulator's backing store) and `servicebus-emulator`, pre-configured with a
session-enabled `order-placed` queue (see `docker/servicebus-emulator/Config.json`). The emulator speaks
the real `Azure.Messaging.ServiceBus` protocol, so the app code doesn't know the difference.

### Health checks

| Endpoint | Checks | Use |
|---|---|---|
| `GET /health/live` | Process is up (no dependency checks) | Liveness probe |
| `GET /health/ready` | SQL Server reachable, `order-placed` queue reachable | Readiness probe |

Both return `200` when healthy and `503` otherwise, with a JSON status per check (no exception
details, since the endpoints are anonymous).

### Run

```bash
cd Ecommerce_Backend
dotnet ef database update
dotnet run
```

### Test

```bash
dotnet test
```

The order/checkout tests talk to the real Service Bus emulator on `localhost:5672`, so start it first
(see above); everything else runs against the EF Core in-memory provider.

