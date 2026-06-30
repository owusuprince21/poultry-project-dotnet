# Poultry Farm Migration Plan: Django/Next.js to ASP.NET Core, Scalar, and Blazor

## Current System Snapshot

The existing project is split into:

- `backend/`: Django + Django REST Framework API, SQLite, token auth, Django Channels WebSockets, Jazzmin admin.
- `frontend/`: Next.js 15 + React 18 + Tailwind + shadcn/Radix-style components.

The main backend app is `farm_management`. Its current business areas are:

- Companies and multi-tenant company scoping.
- Users, roles, password changes, blocking/unblocking, and TOTP 2FA.
- Batches and batch variants by bird type/color.
- Egg production, egg inventory, and egg sales.
- Feed stock lots, feed consumption, and stock summaries.
- Medication and debeaking schedules.
- Bird health events and bird sales.
- Dashboard statistics and real-time activity notifications.

The frontend currently consumes the API through `frontend/lib/api.ts`, which is a useful migration contract. Preserve these endpoint shapes initially unless there is a deliberate versioned API change.

## Recommended Target Architecture

Use a solution-based .NET structure:

```text
poultry-farm.sln
src/
  PoultryFarm.Api/              ASP.NET Core Web API, Scalar UI, auth, SignalR
  PoultryFarm.Application/      Use cases, DTOs, validation, interfaces
  PoultryFarm.Domain/           Entities, enums, domain rules
  PoultryFarm.Infrastructure/   EF Core, Identity, persistence, integrations
  PoultryFarm.Blazor/           Blazor frontend with MudBlazor and Tailwind
tests/
  PoultryFarm.UnitTests/
  PoultryFarm.IntegrationTests/
```

Recommended stack:

- ASP.NET Core Web API for backend.
- EF Core with PostgreSQL for production. SQLite can remain local/dev only.
- ASP.NET Core Identity for users, roles, password rules, lockout, and 2FA.
- JWT bearer auth or secure cookie auth. If mobile/API clients are expected, use JWT.
- SignalR for real-time activity notifications, replacing Django Channels.
- OpenAPI via `Microsoft.AspNetCore.OpenApi` or Swashbuckle, with Scalar UI for API docs.
- Blazor Web App or Blazor WebAssembly + hosted API. For an enterprise internal system, Blazor Web App with interactive server/components is a strong default.
- MudBlazor for enterprise UI components.
- Tailwind for layout utilities, spacing, responsive behavior, and custom visual polish.

## Domain Model Mapping

Map the Django models into C# entities:

- `Company`
- `ApplicationUser`
- `Batch`
- `BatchVariant`
- `EggProduction`
- `FeedConsumption`
- `Medication`
- `DebeakingSchedule`
- `FeedStock`
- `FeedStockLot`
- `EggSale`
- `EggSaleItem`
- `BirdHealthEvent`
- `BirdSale`

Important rules to preserve:

- Batch numbers are generated per company using arrival year/month plus sequence.
- Batch totals are derived from variant totals.
- Variant current count cannot exceed initial count.
- Egg production converts crates/pieces into egg totals, with 30 pieces per crate.
- Egg production belongs to the variant's batch/company.
- Egg sales recalculate item line totals and sale grand totals.
- Feed consumption deducts stock lots transactionally and restores stock on delete/update.
- Dead bird health events reduce variant current count and restore it on update/delete.
- Bird sales are only allowed for batches marked as sold and reduce variant current count.
- Non-system users must be scoped to their company.

## API Contract To Rebuild First

Recreate these existing endpoint groups before changing the frontend:

- `POST /api/auth/login/`
- `POST /api/auth/login/2fa/verify/`
- `POST /api/auth/logout/`
- `GET /api/auth/user/`
- `POST /api/users/change_password/`
- `GET/POST /api/auth/2fa/status|setup|verify-setup|disable/`
- `/api/companies/`
- `/api/users/`
- `/api/batches/`
- `/api/batches/active/`
- `/api/batches/egg-sale-options/`
- `/api/batches/bird-sale-options/`
- `/api/batches/{id}/revenue/`
- `/api/egg-production/`
- `/api/egg-production/stock/`
- `/api/egg-inventory/`
- `/api/egg-sales/`
- `/api/bird-sales/`
- `/api/feed-consumption/`
- `/api/feed-stock-lots/`
- `/api/feed-stock-lots/summary/`
- `/api/medications/`
- `/api/medications/{id}/mark_completed/`
- `/api/debeaking/`
- `/api/debeaking/{id}/mark_completed/`
- `/api/bird-health-events/`

Use `/api/v1/...` for the .NET implementation if you are willing to update the Blazor client while migrating.

## Blazor Frontend Mapping

Current Next.js feature components map naturally to Blazor pages/components:

- `DashboardOverview.tsx` -> `Pages/Dashboard.razor`
- `BatchManagement.tsx` -> `Pages/Batches.razor`
- `EggProduction.tsx` -> `Pages/EggProduction.razor`
- `FeedManagement.tsx` -> `Pages/Feed.razor`
- `InventoryDisplay.tsx` -> `Pages/Inventory.razor`
- `MedicationTracker.tsx` -> `Pages/Medication.razor`
- `DebeakingSchedule.tsx` -> `Pages/Debeaking.razor`
- `MortilityManagement.tsx` -> `Pages/BirdHealth.razor`
- `ReportCenter.tsx` -> `Pages/Reports.razor`
- `ProfilingManagement.tsx` -> `Pages/Users.razor`
- `SettingsManagement.tsx` -> `Pages/Settings.razor`
- `NotificationBell.tsx` -> SignalR-backed notification component.

MudBlazor should carry data grids, dialogs, forms, tabs, nav drawer, snackbars, date pickers, charts, and confirmation flows. Tailwind should be used carefully around MudBlazor, mainly for page layout and utility styling, not to fight component internals.

## Enterprise Hardening Checklist

- Replace SQLite with PostgreSQL.
- Add strict tenant isolation at query/service level.
- Add database transactions around stock and bird-count mutations.
- Use optimistic concurrency tokens on stock lots, variants, sales, and health events.
- Add audit fields: created/updated/deleted by and timestamps.
- Add soft delete where records are financial or operational history.
- Add structured logging with Serilog.
- Add health checks for DB and real-time hub.
- Add validation with FluentValidation.
- Add authorization policies: SystemAdmin, CompanyAdmin, Worker.
- Add integration tests for inventory, bird count, auth, and tenancy.
- Add OpenAPI examples and Scalar docs.
- Add CI pipeline for build, test, formatting, and migrations.
- Add Docker Compose for API, Blazor app, PostgreSQL, and optional Redis.

## Suggested Migration Phases

1. Freeze the Django API contract and document request/response DTOs from `frontend/lib/api.ts`.
2. Create the .NET solution and domain entities.
3. Implement EF Core migrations against PostgreSQL.
4. Implement auth, roles, company scoping, and 2FA.
5. Rebuild high-risk business flows first: feed stock, egg sales, bird health, bird sales.
6. Add Scalar UI and integration tests for API compatibility.
7. Build Blazor shell with MudBlazor navigation, auth guard, and API client.
8. Port feature pages one module at a time.
9. Add SignalR notifications.
10. Run parallel validation against the existing Django data before switching over.

## Recommended First Implementation Milestone

Start with a vertical slice:

- Companies
- Users/auth
- Batches
- Batch variants
- Dashboard stats

This gives the new app its tenant model, identity model, navigation shell, and first useful Blazor screen. After that, add inventory and sales flows with strong tests.
