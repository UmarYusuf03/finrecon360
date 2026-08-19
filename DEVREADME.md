# Developer Setup (FinRecon360)

This file covers local setup and the current development workflow for the repository as implemented now.

## Prerequisites

- Node.js 18+ and npm
- Angular CLI compatible with `finrecon360-frontend/package.json`
- .NET SDK 8.x
- SQL Server local instance or container

## Repo Layout

- `finrecon360-frontend/` Angular application
- `finrecon360-backend-master/finrecon360-backend/` ASP.NET Core API
- `finrecon360/` SQL project

## Backend Setup

1. Copy `finrecon360-backend-master/finrecon360-backend/.env.example` to `.env`.
2. Fill in:
   - `ConnectionStrings__DefaultConnection`
   - `Jwt__Key`
   - `Jwt__Issuer`
   - `Jwt__Audience`
   - `SYSTEM_ADMIN_EMAIL`
   - `SYSTEM_ADMIN_PASSWORD`
   - Brevo settings
   - Stripe settings
   - `TENANT_DB_TEMPLATE`
3. Start the API:

```bash
cd finrecon360-backend-master/finrecon360-backend
dotnet restore
dotnet run
```

On startup the API applies EF Core migrations and runs startup seeding.

## Frontend Setup

```bash
cd finrecon360-frontend
npm install
ng serve
```

Open `http://localhost:4200`.

## Local Access Model

### System Admin

System admin credentials are seeded from environment variables.

1. Set `SYSTEM_ADMIN_EMAIL` and `SYSTEM_ADMIN_PASSWORD` in backend `.env`.
2. Optionally set `SYSTEM_ADMIN_FIRST_NAME` and `SYSTEM_ADMIN_LAST_NAME`.
3. Restart the API.
4. Login through `POST /api/auth/login` or the Angular login screen.

### Tenant Admin

Temporary tenant-admin bypass seeding is no longer part of the supported flow.

Use the real onboarding path:

1. Submit tenant registration through the public registration UI or `POST /api/public/tenant-registrations`.
2. Login as the seeded system admin.
3. Approve the registration from the system-admin tenant-registration flow.
4. Complete onboarding from the magic link email.
5. Set password and select plan.
6. Complete Stripe checkout.
7. Access tenant-scoped routes after the tenant becomes `Active`.

## Access Enforcement Notes

- Frontend route guards now enforce active-tenant access for non-system-admin areas.
- Backend authorization already enforced active-tenant status for tenant-scoped permission checks.
- Control-plane routes use `/api/system/*` and system-admin screens use `/app/system/*`.
- Tenant-admin RBAC routes use `/api/admin/*` and tenant-admin screens use `/app/admin/*`.

## Tests

Backend:

```bash
cd finrecon360-backend-master
dotnet test
```

Frontend:

```bash
cd finrecon360-frontend
ng test --watch=false
```

Frontend unit tests use `src/environments/environment.test.ts` with `mockApi: true`.

## Reconciliation Test Data

To exercise the matching engine and reporting screens against realistic data instead of a few
hand-typed rows, use the `scenario-seeder` tool at the repo root. It spins up a brand-new tenant
and seeds it with transactions/CSV imports covering every matching rule and exception path across
all six reconciliation workers, spread over a ~3-month window.

```bash
cd scenario-seeder
dotnet run -- --verify   # sanity-check with no server needed
```

See [`scenario-seeder/README.md`](scenario-seeder/README.md) for the full test-run vs. demo-run
instructions (the demo run leaves the CSV files for a manual, click-through import instead of
importing them automatically).

## Current Scope Warning

The target architecture describes broader finance workflows such as reconciliation orchestration, journal gating, transaction state history, and cashout branching rules. See `WORKER-INTEGRATION.md` for the current status of the six-level reconciliation and journal posting pipeline.

What is already implemented beyond tenancy/onboarding/RBAC:

- canonical import foundation in backend and frontend (`/app/imports` and `/app/admin/import-architecture`)
- import pipeline stages: upload -> parse -> field mapping -> validation -> normalization commit
- canonical schema and mapping-template management APIs for tenant-admin users
- reporting: CSV/XLSX export on every list screen, financial statements (General Ledger, Trial Balance, Income Statement, Balance Sheet), reconciliation trend charts, a tenant-wide daily KPI snapshot pipeline, a Reports Hub landing page, and weekly emailed report schedules — see `docs/architecture/reporting-implementation-plan.md` for the phase-by-phase detail

What remains target-state:

- cashout workflow branching (cash approval -> journal vs card approval -> bank match -> journal) — see `WORKER-INTEGRATION.md` for current status, this may already be further along than this line suggests
- transaction state and transaction state history entities — see `WORKER-INTEGRATION.md`
