# Developer Setup (FinRecon360)

This file focuses on local setup, testing, and day-to-day development. Existing README files in subfolders cover general usage; this adds missing setup and seeding details.

## Prerequisites
- Node.js 18+ and npm
- Angular CLI (same major as `package.json` / project README)
- .NET SDK 8.x
- SQL Server (local instance or Docker)

## Repo Layout
- `finrecon360-frontend/` Angular app
- `finrecon360-backend-master/finrecon360-backend/` .NET API
- `finrecon360/` SQL project

## Backend Setup
1. Create a local `.env` from the example:
   - Copy `finrecon360-backend-master/finrecon360-backend/.env.example` to `.env`
   - Fill in Brevo settings, `SYSTEM_ADMIN_EMAIL`, `SYSTEM_ADMIN_PASSWORD`, and the `ConnectionStrings__DefaultConnection` value
   - Set `Jwt__Key`, `Jwt__Issuer`, and `Jwt__Audience`
2. Run the API:

```bash
cd finrecon360-backend-master/finrecon360-backend
dotnet restore
dotnet run
```

On startup the API applies EF Core migrations and runs database seeding.

## Frontend Setup
```bash
cd finrecon360-frontend
npm install
ng serve
```
Open `http://localhost:4200`.

## Seeding and Admin Access
The backend seeds roles, permissions, components, and actions on startup. System admin credentials are seeded from environment variables.

Steps to get an admin user locally:
1. Set `SYSTEM_ADMIN_EMAIL` and `SYSTEM_ADMIN_PASSWORD` in `.env`.
2. Optionally set `SYSTEM_ADMIN_FIRST_NAME` and `SYSTEM_ADMIN_LAST_NAME`.
3. Start or restart the API so `DbSeeder` creates/updates the system admin and assigns the `ADMIN` role.
4. Login via `POST api/auth/login` using the seeded email/password.

## Temporary Tenant Admin Bypass (No Stripe)
Use this only for local development while Stripe onboarding is not configured.

What it does:
- Seeds an active tenant and tenant admin user.
- Seeds a temporary active subscription with a plan limited to 5 users (`TEMP-SEED-5-USERS`).
- Creates/provisions tenant DB and tenant-scoped user directory for the seeded tenant admin.
- Enforces the 5-user cap when tenant admin uses `POST /api/admin/users`.

How to enable:
1. In backend `.env`, set:
   - `TEMP_BYPASS_SEED_TENANT_ADMIN=true`
   - `TEMP_BYPASS_TENANT_NAME`
   - `TEMP_BYPASS_TENANT_ADMIN_EMAIL`
   - `TEMP_BYPASS_TENANT_ADMIN_PASSWORD`
   - optional `TEMP_BYPASS_TENANT_ADMIN_FIRST_NAME`, `TEMP_BYPASS_TENANT_ADMIN_LAST_NAME`
2. Keep `TENANT_DB_TEMPLATE` valid for your SQL Server.
3. Restart backend (`dotnet run`) to apply seed.
4. Login as seeded tenant admin through normal login API/UI.

Tenant RBAC note:
- Tenant admin manages users/roles/permissions/components/actions within that tenant.
- Those objects are persisted in each tenant DB (not in global control-plane RBAC tables).
- System admin manages tenant lifecycle (registration/approval/plans/enforcement), not tenant-internal RBAC.

Rollback later (when Stripe flow is active):
1. Set `TEMP_BYPASS_SEED_TENANT_ADMIN=false`.
2. Restart backend.
3. Use standard tenant registration -> approval -> onboarding -> Stripe activation flow.

## Frontend Payment-Gate Toggle (Temporary)
RBAC remains enforced in frontend route guards (roles + permissions).  
For local pre-Stripe testing, tenant-status blocking is temporarily disabled.

Current temporary state:
- In `src/app/core/auth/access.guard.ts`, `enforceTenantActiveStatus = false`.
- This means non-admin users are not blocked purely because `tenantStatus !== Active`.

How to roll back to strict paid-only access:
1. Edit `src/app/core/auth/access.guard.ts`.
2. Set `enforceTenantActiveStatus = true`.
3. Rebuild/restart frontend.

Expected strict behavior after rollback:
- Non-admin routes are denied unless `tenantStatus === 'Active'`.
- Admin area access still depends on admin permissions.

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

Frontend test notes:
- Karma tests run with `src/environments/environment.test.ts` (configured in `angular.json` test `fileReplacements`).
- `environment.test.ts` sets `mockApi: true` so service unit tests do not depend on live backend APIs.
- Backend `.env` flags such as `TEMP_BYPASS_SEED_TENANT_ADMIN` do not affect frontend unit test outcomes.

## Development Notes
- `.env` is local-only and ignored by git. Use `.env.example` as the template.
- Use env vars to configure DB and JWT settings (see `.env.example`).
- If SQL Server is not local, adjust the connection string accordingly.
