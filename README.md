# Dressfield API

ASP.NET Core 9 REST API for Dressfield, a Georgian embroidery e-commerce platform. Handles catalog, cart, orders, custom-embroidery uploads, and Bank of Georgia iPay payments.

Frontend lives at [Pistolmani/Dressfield](https://github.com/Pistolmani/Dressfield). Production API: `https://api.dressfield.ge`.

## Tech stack

- **Framework:** ASP.NET Core 9 (C#)
- **ORM:** Entity Framework Core + MySQL 8
- **Auth:** JWT (15-min access + 7-day refresh) with Google OAuth
- **Payments:** Bank of Georgia iPay — redirect flow with webhook callbacks
- **Storage:** Azure Blob Storage (designs container), local disk fallback
- **Email:** SMTP via outbox pattern (`EmailOutboxWorker` + `PendingEmail` entity)
- **Security:** ClamAV file scanning (optional), FluentValidation
- **Logging:** Serilog
- **Docs:** Swagger / Swashbuckle

## Architecture

Clean Architecture, four projects:

```
Dressfield.API/            Controllers, middleware, DI, Program.cs
Dressfield.Application/    DTOs, service interfaces, validators
Dressfield.Core/           Domain entities, enums, core interfaces
Dressfield.Infrastructure/ EF DbContext, migrations, service implementations
```

## Controllers

| Controller | Responsibility |
|---|---|
| `AuthController` | Login, register, refresh, Google OAuth, password reset |
| `ProductsController` | Product CRUD, variants, images, bulk ops |
| `CartController` | Persistent cart |
| `OrdersController` | Order creation, status, history |
| `CustomOrdersController` | Custom embroidery orders with design uploads |
| `PaymentsController` | BOG iPay initiation + webhook callbacks |
| `PromoCodesController` | Promo code CRUD and validation |
| `UploadsController` | File upload with security scanning |
| `AdminDashboardController` | Admin stats and management |
| `AuditLogsController` | User action audit trail |

## Local development

```bash
cp Dressfield.API/appsettings.Development.example.json Dressfield.API/appsettings.Development.json
# fill in the DB connection string, JWT secret, BOG keys, etc.

dotnet restore
dotnet ef database update --project Dressfield.Infrastructure --startup-project Dressfield.API
dotnet run --project Dressfield.API
```

Swagger UI: `https://localhost:5001/swagger`.

Add a migration:

```bash
dotnet ef migrations add <Name> --project Dressfield.Infrastructure --startup-project Dressfield.API
```

Tests:

```bash
dotnet test
```

## Key patterns

- **Email outbox** — never call SMTP directly from controllers; queue a `PendingEmail`. `EmailOutboxWorker` drains the queue so mail is delivered even if SMTP is down mid-request.
- **Payment flow** — user is redirected to the BOG hosted page and returns via redirect. Order status is authoritative only after the `/api/payments/callback` webhook fires. Do not change that path.
- **File upload** — scanned by ClamAV when enabled, then persisted to Azure Blob (or local disk in dev).
- **Auth** — short-lived JWT plus rotating refresh tokens; Google ID tokens are verified server-side.

## Configuration

Secrets (DB connection string, JWT signing key, BOG credentials, SMTP, Google OAuth) come from environment variables in production — not from `appsettings.json`. `Orders:ShippingCost` in `appsettings.json` controls shipping (currently 5.00 GEL).

## Deployment

GitHub Actions deploys `main` to Azure App Service via [.github/workflows/main_dressfield-api-prod.yml](.github/workflows/main_dressfield-api-prod.yml).
