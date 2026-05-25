# ADR-001: Architecture Evaluation — Dressfield API

**Status:** Proposed  
**Date:** 2026-05-21  
**Deciders:** Engineering team  

---

## Context

Dressfield is a Georgian embroidery e-commerce platform with ASP.NET Core 9 backend (clean architecture), MySQL on Hostinger, Azure Blob Storage for design uploads, and Bank of Georgia iPay for payments. The system supports both regular product orders and custom embroidery orders, with guest checkout throughout. This document evaluates the architecture across five areas: the payment saga, the startup configuration, the email system, the upload endpoint, and a few correctness issues.

---

## Decision Area 1: Payment Saga Robustness

### Current state

`OrderService.CreateAsync` follows a saga-like pattern:

1. Open explicit DB transaction
2. Atomically decrement stock per variant (`ExecuteUpdateAsync` with `StockQuantity >= qty` guard)
3. Atomically claim promo code (`ExecuteUpdateAsync` with `UsedCount < MaxUses` guard)
4. Insert order record, `SaveChanges`, `CommitTransaction`
5. **Outside the transaction:** call BOG iPay to create a payment session
6. If BOG fails → `CancelAndRestoreAsync` compensates (cancel order, restore stock, restore promo)
7. If BOG succeeds → update `BogOrderId` and status to `AwaitingPayment`

`HandlePaymentCallbackAsync` uses an atomic `ExecuteUpdateAsync` claim (`AwaitingPayment → PaymentProcessing`) before verifying with BOG — preventing double-processing under concurrent callbacks. It also validates the amount returned by BOG against the stored order total, cancelling and restoring if there's a mismatch.

`AbandonedOrderReaper` runs every 5 minutes and closes two gaps:
- **Pending timeout** (10 min): orders where the BOG session was never created (process crash between steps 4 and 5) — cancels and restores stock.
- **AwaitingPayment timeout** (30 min): checks BOG status before cancelling — picks up approved payments where the callback was missed.

### Issues Identified

**Gap 1 — crash during `CancelAndRestoreAsync`.** If the process crashes after the transaction commits but before the BOG session is created AND during the compensation, the order stays in `Pending` status. The `AbandonedOrderReaper` handles this correctly by cancelling `Pending` orders after the timeout and restoring resources. However, the compensation in `CancelAndRestoreAsync` is not atomic — it issues multiple `ExecuteUpdateAsync` calls for each variant and the promo code sequentially. A crash mid-way leaves a partially-restored state. The `AbandonedOrderReaper`'s `CancelStalePendingOrdersAsync` re-runs the same restore logic, so this self-heals on the next reaper cycle.

**Gap 2 — guest checkout has no idempotency protection.** The `IdempotencyKey` field on `Order` is only set for authenticated users (`!string.IsNullOrEmpty(userId)`). A guest whose network retries a POST to `/api/orders` will create two orders, both with stock decremented. The rate limiter (20/min/IP) reduces the blast radius but doesn't eliminate the problem.

**Gap 3 — `PaymentProcessing` is a terminal-looking dead state.** If the process crashes after claiming the callback (`AwaitingPayment → PaymentProcessing`) but before completing `HandlePaymentCallbackAsync`, the order is stuck in `PaymentProcessing` forever. The `AbandonedOrderReaper` does not handle `PaymentProcessing` orders — it only handles `AwaitingPayment`. This means a crashed callback leaves an order that can never progress or be cleaned up without manual intervention.

### Options Considered

#### Option A: Add `AbandonedOrderReaper` handling for `PaymentProcessing`

Add a `CancelAbandonedPaymentProcessingOrdersAsync` step: for any order stuck in `PaymentProcessing` beyond a short timeout (e.g., 5 minutes), re-verify with BOG and apply the result, or cancel if BOG has no answer.

| Dimension | Assessment |
|-----------|------------|
| Effort | Low — mirrors existing `CancelAbandonedOrdersAsync` logic |
| Risk | Low — uses same atomic claim + BOG verify pattern |
| Coverage | Closes the stuck `PaymentProcessing` dead state |

#### Option B: Add session-level idempotency for guests using fingerprinting

Hash the guest's order payload (contact email + item list + address) into a short-lived idempotency key stored in a separate table or as a unique index. Rate-limit + return the existing order if the fingerprint matches within 60 seconds.

| Dimension | Assessment |
|-----------|------------|
| Effort | Medium — requires a migration and fingerprinting logic |
| Risk | Low false-positive rate if the hash covers enough fields |
| Coverage | Prevents guest double-submit on network retry |

### Recommendation

**Fix `PaymentProcessing` stuck orders now (Option A)** — this is a correctness bug that causes permanent data corruption for any callback that arrives during a restart. The fix is a small addition to `AbandonedOrderReaper.RunAsync`.

**Defer guest idempotency (Option B)** until there's evidence of real double-submit incidents. Guest checkout is inherently lower-trust; the rate limiter provides a reasonable backstop for now.

### Consequences

- Adding `PaymentProcessing` to the reaper requires deciding the right timeout (5 minutes is safe — real BOG verifications complete in <2s).
- Guest idempotency would require a new DB column or table plus careful handling of the "is this a retry?" detection.

---

## Decision Area 2: `Program.cs` vs `WebApplicationBuilderExtensions.cs` Drift

### Current state

The repository has two registration paths:

- `Program.cs` — the canonical, up-to-date setup. Registers everything: `ICartService`, `IAdminDashboardService`, `IBogTokenProvider`, Polly retry policies, `AbandonedOrderReaper` as a hosted service, `IFileSecurityScanner` (ClamAV/NoOp), the "upload" and "status" rate limiters, health checks, `JsonStringEnumConverter`, `AddProblemDetails`, and security headers.
- `Dressfield.API/Extensions/WebApplicationBuilderExtensions.cs` — an earlier refactoring stub that is missing all of the above additions. If anyone were to switch `Program.cs` to use the extension methods, the application would fail to start because `IBogTokenProvider`, `ICartService`, `IAdminDashboardService`, and others would be unresolved.

### Options Considered

#### Option A: Delete `WebApplicationBuilderExtensions.cs`

Remove the stale file entirely. `Program.cs` is the single source of truth and is not hard to read.

#### Option B: Finish the migration — move everything into extension methods, use them from `Program.cs`

Complete the extension-method refactor so that `Program.cs` becomes a thin orchestrator of composable setup blocks.

| Dimension | Assessment |
|-----------|------------|
| Readability | `Program.cs` becomes ~20 lines of `builder.AddXxx()` calls |
| Testability | Each extension method can have integration tests for its registration |
| Effort | Medium — move and test all registrations |

#### Option C: Keep `Program.cs` as-is, add a comment marking the Extensions file as abandoned

Cheapest, but leaves a trap for the next developer.

### Recommendation

**Delete `WebApplicationBuilderExtensions.cs` (Option A) now.** It's actively dangerous — a partial picture of the DI setup that will mislead any developer who looks at it. Completing the full migration to extension methods (Option B) is worth doing as a separate cleanup task when time allows, but the immediate action is to remove the dead file.

### Consequences

- Deleting the file removes the false impression of a clean extension-method architecture.
- If the extension-method refactor is eventually finished, `Program.cs` will be easier to reason about.

---

## Decision Area 3: Email Templates Embedded in Service Classes

### Current state

`OrderService` contains two full HTML email templates as C# interpolated strings directly inside `QueueConfirmationEmail` and `QueueShippingEmail`. These are ~60–80 lines of inline HTML each. `CustomOrderService` likely follows the same pattern. Template changes require modifying service code and redeploying the API.

### Options Considered

#### Option A: Keep inline templates (current)

Simple, no extra files. Templates are close to the data that populates them.

**Cons:** Email design changes require a code deployment. The templates can't be edited by a non-developer. Templates are not reusable across services. Long methods make unit testing harder.

#### Option B: Move templates to `.html` files embedded as assembly resources

Templates live in `Dressfield.Infrastructure/EmailTemplates/*.html` with placeholder tokens (`{{OrderId}}`, `{{CustomerName}}`). A lightweight `IEmailTemplateRenderer` interface replaces the inline string building.

| Dimension | Assessment |
|-----------|------------|
| Effort | Low — move HTML to files, write a simple `string.Replace` renderer |
| Testability | Template rendering is unit-testable independently of order logic |
| Designer-friendliness | HTML files can be opened/edited without touching C# |

#### Option C: Use a proper templating engine (Scriban, Fluid, RazorLight)

Full Liquid/Razor template engine supporting loops, conditionals, and inheritance.

| Dimension | Assessment |
|-----------|------------|
| Effort | Medium — adds a dependency, more abstraction |
| Power | Item-list loops in templates instead of LINQ-generated HTML strings |
| Overkill? | Yes for the current 2-3 email types |

### Recommendation

**Option B** — extract templates to embedded HTML files. The current inline approach works but will become a maintenance burden as email designs evolve (and they always do). A `string.Replace`-based renderer is 20 lines of code and eliminates the deploy-per-template-change coupling.

The item list HTML (the `Select`-generated `<tr>` strings) should become a loop token in the template, making it easier to redesign the order summary table without touching C# code.

### Consequences

- Migration is mechanical — move HTML, replace C# string interpolation with token replacement.
- Future template changes don't require a code review or deployment.
- If templates grow significantly in complexity, upgrading to Scriban/Fluid is a single interface swap.

---

## Decision Area 4: Unauthenticated Upload Endpoint and Orphan Files

### Current state

`POST /api/upload/design` accepts files from any anonymous user (by design — guests submit custom orders). The endpoint validates content type, file extension, and magic bytes, and optionally runs ClamAV. Files are uploaded to Azure Blob Storage's `designs` container. There is no record of uploaded files until a `CustomOrder` is created referencing the URL. ClamAV is disabled by default (`Security:ClamAv:Enabled = false`).

### Issues Identified

**Orphaned blobs accumulate indefinitely.** Any user can upload a 10MB image and walk away. There is no `DesignUpload` entity tracking the file URL, no garbage collection, and no link between a blob and the custom order that should reference it. Over time this silently inflates Azure Storage costs.

**ClamAV is off by default in production.** The startup guard logs a warning in non-Development environments but doesn't fail:
```
Log.Warning("ClamAV scanning is disabled. Set Security:ClamAv:Enabled=true in production.");
```
This means the production endpoint accepts unscanned files unless someone actively enables ClamAV. For a platform where users upload design images, a passive warning is insufficient.

### Options Considered

#### Option A: Track uploads and schedule a cleanup job

Add a `DesignUpload` entity (`Id`, `BlobUrl`, `UploadedAt`, `ClaimedByCustomOrderId`). When a custom order is created, link the uploads. The `AbandonedOrderReaper` (or a new `OrphanBlobReaper`) deletes unclaimed blobs older than 24 hours by calling Azure Blob's delete API.

| Dimension | Assessment |
|-----------|------------|
| Effort | Medium — migration, new entity, cleanup in the reaper |
| Coverage | Eliminates blob accumulation |
| Risk | Low — delete only blobs never claimed by an order |

#### Option B: Pre-signed upload URLs

Instead of routing the upload through the API, generate a pre-signed Azure Blob SAS URL from the API and return it to the client. The client uploads directly to Azure. Orphan cleanup is still needed but the API is no longer in the file-transfer path.

| Dimension | Assessment |
|-----------|------------|
| Effort | Medium |
| API bandwidth | Eliminates large file transfer through the API server |
| Security | Scope SAS URLs to a single blob name + expiry |

#### Option C: Convert ClamAV warning to a hard startup failure in production

Change the non-dev warning to a `throw new InvalidOperationException(...)` (matching the pattern used for missing `Jwt:Secret`, `Admin:Password`, and `AzureStorage:ConnectionString`).

### Recommendation

**Option C (ClamAV enforcement) immediately** — the pattern is already established in `Program.cs` for other critical configuration: fail fast in non-Development if a security control is off. Apply the same to ClamAV.

**Option A (orphan tracking) as a medium-term item** — add a `DesignUpload` table, link it to `CustomOrder`, and add a cleanup step to the existing `AbandonedOrderReaper`. This is a single-service change with no new infrastructure.

Option B (pre-signed URLs) is worth revisiting if API bandwidth becomes a cost concern, but is premature now.

### Consequences

- Making ClamAV mandatory in production requires the ClamAV service to be available on Azure. The easiest path is a sidecar container on Azure Container Apps, or a separate Azure Container Instance.
- The orphan cleanup requires a migration (new `DesignUpload` table) and changes to both `UploadsController` and `CustomOrderService`.

---

## Decision Area 5: Startup Migration Safety

### Current state

`Program.cs` calls `db.Database.MigrateAsync()` at startup, wrapped in:

```csharp
catch (Exception ex) when (IsDatabaseUnavailable(ex))
{
    Log.Warning(ex, "Database unavailable during startup. Continuing without migration/seed.");
}
```

This means if the database is down at startup, the application boots without running migrations. If the deployment includes a schema-breaking migration (adding a NOT NULL column, renaming a column), requests will fail with EF errors while the app is running. The `IsDatabaseUnavailable` check catches `DbException`, `SocketException`, and `TimeoutException` — broadly enough that transient connection issues during a rolling restart would silently skip migrations.

### Options Considered

#### Option A: Keep current behaviour (swallow DB-down exceptions)

The intention is to allow a graceful degradation when MySQL isn't ready yet (e.g., cold start). But "continue without migrating" is only safe if there are no pending schema changes.

#### Option B: Fail fast on DB-unavailable during startup, rely on Azure health check for readiness

Remove the catch, let the process crash if the DB is down. Azure App Service will restart and retry. The health check (`/api/health` with DB ping) prevents routing traffic until the app is healthy.

#### Option C: Separate migration from application startup — run migrations as an Azure pre-deployment slot swap step or release script

Migrations run in a one-off container/script before the new app version is deployed. The application startup does not run migrations at all.

| Dimension | Assessment |
|-----------|------------|
| Safety | Highest — migrations run once, deterministically, before traffic shifts |
| Multi-instance | Safe — only one migration runner, not one per replica |
| CI/CD effort | Medium — requires Azure deployment pipeline change |

### Recommendation

**Medium-term: implement Option C** — run migrations via `dotnet ef database update` as a pre-swap step in the Azure deployment pipeline (GitHub Actions `main_dressfield-api-prod.yml`), and remove `MigrateAsync` from `Program.cs`. This mirrors the recommendation made for Pasukhi.

**Short-term: narrow the exception catch** to only `SocketException` (DB not reachable at all), and convert `DbException` and `TimeoutException` into a hard failure. A partially-connected database should not silently skip migrations.

### Consequences

- Azure CI/CD pipeline requires a migration step before the App Service swap.
- Removing `MigrateAsync` from `Program.cs` simplifies startup and removes the race condition risk on multi-instance deploys.

---

## Summary of Action Items

| Priority | Item | Effort |
|----------|------|--------|
| **Now** | Add `PaymentProcessing` dead-state handling to `AbandonedOrderReaper` | 1h |
| **Now** | Delete `WebApplicationBuilderExtensions.cs` | 5 min |
| **Now** | Convert ClamAV "warning in prod" to startup hard failure | 15 min |
| **Soon** | Add `DesignUpload` entity + orphan cleanup in `AbandonedOrderReaper` | 3h |
| **Soon** | Extract email templates to embedded HTML resource files | 2h |
| **Medium** | Move migrations to Azure pre-deployment release script | 2h |
| **Later** | Guest checkout idempotency fingerprinting | 1 day |
| **Later** | Pre-signed URL upload flow (if bandwidth cost emerges) | 1 day |

---

## Decision Log

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Add `PaymentProcessing` reaper step | Crash during callback leaves permanently stuck orders with no self-heal path |
| 2 | Delete `WebApplicationBuilderExtensions.cs` | Out-of-date file actively misleads; `Program.cs` is the real source of truth |
| 3 | Make ClamAV mandatory in production | Existing pattern enforces all other security config; scanning should be no different |
| 4 | Track uploads with `DesignUpload` entity | Silent blob accumulation is a cost leak; reaper can clean cheaply |
| 5 | Extract email templates to HTML resource files | Template design should not require API deployment |
| 6 | Migrate to pre-deployment migration script | Startup `MigrateAsync` is unsafe across multiple instances and silently skippable |
