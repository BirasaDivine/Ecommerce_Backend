# CLAUDE.md

ASP.NET Core 10 Web API (EF Core / SQL Server, Identity + JWT) for e-commerce inventory and orders. `Ecommerce_Backend/` is the API, `Ecommerce_Backend.Tests/` is NUnit. See `README.md` and `docs/*_EXPLAINED.md` for detail.

## Commands

```bash
dotnet tool restore                      # once: pins dotnet-ef
dotnet build Ecommerce_Backend.slnx
docker compose up -d servicebus-emulator # needed by order tests; needs .env (cp .env.example .env)
dotnet test Ecommerce_Backend.slnx
dotnet test --filter "FullyQualifiedName~OrderTelemetryTests"   # single class/test
docker compose up -d --build --wait      # full stack: db, migrator, api, consumer, observability
dotnet ef migrations has-pending-model-changes   # CI fails if the model changed without a migration
```

- Never run `scripts/make-ci-env.sh` in the repo root: it overwrites `.env`.
- Secrets (`Jwt:Key`, `ServiceBus:ConnectionString`) come from user secrets or env vars.

## Structure

- One image, two roles: `AppRoles` (`Api:Enabled`, `Consumer:Enabled`) picks what is registered. The `api` service serves HTTP; the `consumer` service runs the same image with `Api__Enabled=false`. Service registration is in `Extensions/*Extensions.cs`; config sections are validated options classes in `Configuration/`.
- Purchase flow: `POST /api/orders` saves the order as `Pending`, publishes `OrderPlaced` (with W3C `traceparent`) to a session-enabled Service Bus queue and returns `202`. `Messaging/OrderProcessingService` in the consumer checks stock and sets `Confirmed` or `Rejected`. One purchase is one trace across both services. `POST /api/orders/{id}/cancel` sets a `Pending` order to `Cancelled`; `Order.Status` is a concurrency token so the cancel and the consumer cannot both resolve one order (the loser gets `409` or skips the message).
- Tests: only order/checkout tests use the real Service Bus emulator (own `order-placed-tests` queue); everything else uses EF InMemory.

## Conventions

- Controllers, not minimal APIs. DTOs (`DTOs/`) at the API boundary, never EF entities.
- API tests: NUnit with `WebApplicationFactory` (`CustomWebApplicationFactory`).
- `ILogger` message templates, never string interpolation.
- No personal data (emails, tokens, names) in logs or span attributes. Metric tags must be low-cardinality (no IDs or free text). Telemetry must never break the shop.
- Never edit a migration that has already been applied; add a new one.
- Grafana provisioning expands `${...}`, so write literal templates as `$${...}`.
- Docs avoid em dashes.
