# Dressfield API — Codex Instructions

## Project Overview

ASP.NET Core 9 REST API backend for Dressfield — a Georgian embroidery e-commerce platform.

**Frontend:** [Dressfield](https://github.com/Pistolmani/Dressfield) — Next.js static export on Hostinger
**Backend (this repo):** Azure App Service
**Database:** MySQL 8 via Entity Framework Core (Hostinger)
**Domain:** https://api.dressfield.ge

## Tech Stack

- **Framework:** ASP.NET Core 9 (C#)
- **ORM:** Entity Framework Core + MySQL
- **Auth:** JWT (15min access + 7d refresh) + Google OAuth
- **Payments:** Bank of Georgia iPay (redirect-based, webhook callbacks)
- **Storage:** Azure Blob Storage (designs container) + Local fallback
- **Email:** SMTP outbox pattern via `EmailOutboxWorker` + `PendingEmail` entity
- **Security:** ClamAV file scanner (disabled by default), FluentValidation
- **Logging:** Serilog
- **Docs:** Swagger/Swashbuckle

## Architecture — Clean Architecture (4 layers)

```
Dressfield.API/           → Controllers, middleware, DI config, Program.cs
Dressfield.Application/   → DTOs, service interfaces, FluentValidation validators
Dressfield.Core/          → Domain entities, enums, core interfaces
Dressfield.Infrastructure/→ EF DbContext, migrations, service implementations
```

## Controllers

| Controller | Responsibility |
|---|---|
| `AuthController` | Login, register, refresh token, Google OAuth, reset password |
| `ProductsController` | Product CRUD, variants, images, bulk ops |
| `CartController` | Persistent cart management |
| `OrdersController` | Order creation, status tracking, order history |
| `CustomOrdersController` | Custom embroidery orders with design uploads |
| `PaymentsController` | BOG iPay initiation + webhook callbacks |
| `PromoCodesController` | Promo code CRUD and validation |
| `UploadsController` | File upload with security scanning |
| `AdminDashboardController` | Admin stats and management |
| `AuditLogsController` | User action audit trail |

## Domain Entities

`ApplicationUser`, `RefreshToken`, `Product`, `ProductImage`, `ProductVariant`, `Cart`, `CartItem`, `Order`, `OrderItem`, `OrderStatusLog`, `CustomOrder`, `CustomOrderDesign`, `PromoCode`, `AuditLog`, `PendingEmail`

## Key Patterns

- **Email outbox:** `PendingEmail` entity + `EmailOutboxWorker` background service — emails are queued in DB and sent async, guaranteeing delivery even if SMTP fails mid-request
- **Payment flow:** Redirect-based (user → BOG site → return). Webhook at `/api/payments/callback` updates order status
- **File upload:** Files scanned by ClamAV (if enabled) before storage. Supports Azure Blob or local disk
- **Auth:** Short-lived JWT + refresh token rotation. Google ID token verification for OAuth

## Key Constraints

- **No breaking changes to payment flow** — BOG webhook endpoint path must stay stable
- **Email outbox must not be bypassed** — always queue via `PendingEmail`, never call SMTP directly from controllers
- **Migrations:** Always use EF migrations (`dotnet ef migrations add`), never edit DB schema manually
- **Shipping cost** is configured in `appsettings.json` under `Orders:ShippingCost` (currently 5.00 GEL)

## Deployment

- **CI/CD:** GitHub Actions → Azure App Service (`main_dressfield-api-prod.yml`)
- **Secrets:** All connection strings, API keys, JWT secret in Azure App Service environment variables (not in appsettings)
