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

`appsettings.json` holds the connection string (`Database:ConnectionString`) and non-secret JWT settings (`Issuer`, `Audience`,
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
| `Database:ConnectionString` | LocalDB `EcommerceDb` | SQL Server connection string (`Database__ConnectionString` as an environment variable) |
| `Api:Enabled` | `true` | Serve the HTTP API (controllers, auth, seeding). The `consumer` compose service sets it to `false` |
| `Consumer:Enabled` | `true` | Run the order-processing consumer. The `api` compose service sets it to `false` |
| `HealthChecks:TimeoutSeconds` | `5` | Per-check timeout for the health endpoints (1–120) |

### Run everything with Docker Compose

The compose file runs these services. Secrets come from a git-ignored `.env` file:

| Service | Role |
|---|---|
| `migrator` | One-off job: applies the EF Core migrations with a migration bundle, then exits |
| `api` | HTTP API only (`Consumer__Enabled=false`) |
| `consumer` | Same image with the HTTP API switched off (`Api__Enabled=false`); processes the `order-placed` queue |
| `db` | SQL Server 2022 with the `db-data` volume |
| `servicebus-emulator`, `sql-edge` | Local Service Bus and its backing store |
| `servicebus-emulator-ready` | One-shot probe that waits for the emulator's `/health` |

```bash
cp .env.example .env   # then replace the placeholder passwords and JWT key
docker compose up --build
```

- The API listens on `http://localhost:8080` (`API_PORT` to change it); Scalar is at `/scalar/v1`.
  The consumer publishes no port; it only has the health endpoints inside the network.
- Start-up order: `db` healthy, then `migrator` runs to completion, then `api` and `consumer` start
  (once the emulator's `/health` returns 200). So the tables exist before anything serves traffic,
  and a failed migration stops the stack instead of crash-looping the API.
- The API seeds the `Admin`/`User` roles and the admin from `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`
  in Development. The consumer does not seed and needs no JWT key.
- The image is a multi-stage build (SDK to publish, `aspnet` runtime to run) that runs as the base
  image's non-root `app` user and contains only the API project. The `migrator` image is a separate
  target of the same Dockerfile (`runtime-deps` plus the self-contained migration bundle).
- Starting a process with both `Api__Enabled=false` and `Consumer__Enabled=false` fails at startup.
- Database data lives in the `db-data` volume; `docker compose down -v` wipes it.

Check the stack with the scripts in `scripts/` (PowerShell; they read the admin login from `.env`):

```powershell
./scripts/smoke-test.ps1              # starts the stack, checks it, stops the database, tears everything down
./scripts/e2e-consumer-recovery.ps1   # with the stack running: stop only the consumer, buy, restart it
```

**Why SQL Server in Docker:** the EF Core provider and all existing migrations target SQL Server, and
the current search and uniqueness checks rely on its case-insensitive default collation. Switching to
PostgreSQL would have meant regenerating every migration and changing that behaviour, for a smaller
image. The `sql-edge` container is separate: it is required by the Service Bus emulator itself and is
not the app's database.

### Local Service Bus emulator only

Order placement publishes to Azure Service Bus; the consumer role (a background service) processes the
messages. To run just the emulator (e.g. for `dotnet run` or the integration tests), with `.env` in place:

```bash
docker compose up -d servicebus-emulator
```

This starts `sql-edge` (the emulator's backing store) and `servicebus-emulator`, pre-configured with a
session-enabled `order-placed` queue, plus `order-placed-tests` for the integration tests (see `docker/servicebus-emulator/Config.json`). The emulator speaks
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
dotnet tool restore   # once: installs the pinned dotnet-ef
dotnet ef database update
dotnet run
```

### Test

```bash
dotnet test
```

The integration tests need the Service Bus emulator running (see above). They use their own
`order-placed-tests` queue, so a running `api` or `consumer` container never competes with them for
messages. Without that separation, the container took the tests' messages and the tests' orders stayed
`Pending`.

The order/checkout tests talk to the real Service Bus emulator on `localhost:5672`, so start it first
(see above); everything else runs against the EF Core in-memory provider.

