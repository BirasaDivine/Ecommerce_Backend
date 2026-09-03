# Ecommerce Backend

An ASP.NET Core Web API for an e-commerce inventory and order management platform: role-based JWT
authentication, product/category/variant/collection management, and a purchase endpoint that
decrements stock transactionally.

## Stack

- ASP.NET Core 10 Web API
- EF Core 10 with SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`)
- ASP.NET Core Identity for users/roles
- JWT bearer authentication
- NUnit + `WebApplicationFactory` for integration tests, EF Core InMemory for service-level tests
- Scalar for interactive API docs in Development (`/scalar/v1`)

## Roles

- **Admin** — manages categories, products, variants, and collections.
- **User** — any authenticated account; can purchase products.
- **Public (unauthenticated)** — can browse and search products only. Stock is exposed as a status
  (`IN_STOCK` / `LOW_STOCK` / `OUT_OF_STOCK`), never as an exact quantity.

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

The app fails fast at startup if `Jwt:Key` is missing.

In Development only, a `SeedAdmin:Email` / `SeedAdmin:Password` pair (defaulting to
`admin@ecommerce.local` / `Admin123!`) is seeded on startup along with the `Admin` and `User` roles.
This seeding does not run outside Development.

### Run

```bash
cd Ecommerce_Backend
dotnet ef database update
dotnet run
```

Browse `/scalar/v1` for interactive API docs.

### Test

```bash
dotnet test
```

## API overview

| Resource | Endpoint | Access |
|---|---|---|
| Auth | `POST /api/auth/register`, `POST /api/auth/login` | Public |
| Categories | `GET /api/categories`, `GET /api/categories/{id}` | Public |
| Categories | `POST/PUT/DELETE /api/categories...` | Admin |
| Products | `GET /api/products?name=&maxPrice=`, `GET /api/products/{id}` | Public |
| Products | `POST /api/products` | Admin |
| Variants | `GET/POST /api/variants...`, `PATCH /api/variants/{sku}/stock` | Admin |
| Collections | `GET /api/collections`, `GET /api/collections/{id}` | Public |
| Collections | `POST /api/collections`, `POST /api/collections/{id}/products` | Admin |
| Orders | `POST /api/orders` (buy), `GET /api/orders/{id}` | Authenticated (own orders, or Admin) |

## Notable design decisions

- **Category hierarchy**: categories self-reference via `ParentCategoryId`; a name must be unique
  among siblings at the same level, and a product must be assigned to a terminal (leaf) category.
- **Variant SKUs**: unique across the whole platform, enforced both at the application level (a
  friendly `400` before hitting the database) and with a database unique index as the actual
  guarantee under concurrent writes.
- **Stock masking**: `Variant.Quantity` is never serialized to a public response. Public product
  reads return a computed `StockStatus` instead; the endpoints that expose the raw quantity
  (`/api/variants/...`) are Admin-only.
- **Purchases**: `Quantity` on `Variant` doubles as an EF Core optimistic-concurrency token, so two
  simultaneous purchases against the same variant can't both succeed against stale stock — one
  retries against the freshly reloaded row instead of overselling.
- **Product + variant creation**: a product and its variants are inserted in one `SaveChangesAsync`
  call, so a validation failure on any variant (e.g. a duplicate SKU) rolls back the whole product.
