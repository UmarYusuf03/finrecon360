# FinRecon360 Setup Guide

This guide helps new developers set up the repo, configure the environment, and run the backend + frontend locally.

## Prerequisites

- Node.js 18+ and npm
- Angular CLI compatible with finrecon360-frontend/package.json
- .NET SDK 8.x
- SQL Server (local instance or container)

## Repo Layout

- finrecon360-frontend/ (Angular app)
- finrecon360-backend-master/finrecon360-backend/ (ASP.NET Core API)
- finrecon360/ (SQL project)

## Environment Setup (Backend)

1. Copy the example environment file:

   finrecon360-backend-master/finrecon360-backend/.env.example -> .env

2. Update values in .env:
   - ConnectionStrings\_\_DefaultConnection
   - Jwt**Key, Jwt**Issuer, Jwt\_\_Audience
   - SYSTEM_ADMIN_EMAIL, SYSTEM_ADMIN_PASSWORD
   - BREVO\_\* values (email provider)
   - STRIPE\_\* values
   - TENANT_DB_TEMPLATE

Note: .env is ignored by git. Do not commit secrets.

## Backend Run

From repo root:

1. cd finrecon360-backend-master/finrecon360-backend
2. dotnet restore
3. dotnet run

On startup, the API applies EF Core migrations and runs seeding.

## Frontend Run

From repo root:

1. cd finrecon360-frontend
2. npm install
3. npm run start

Open http://localhost:4200

## Login and Access

### System Admin

System admin is seeded from env vars:

- SYSTEM_ADMIN_EMAIL
- SYSTEM_ADMIN_PASSWORD

Login via the UI or POST /api/auth/login.

### Tenant Admin

Use the real onboarding flow:

1. Submit tenant registration via UI (public registration) or POST /api/public/tenant-registrations
2. Login as system admin
3. Approve registration in system admin screen
4. Complete onboarding via magic link email
5. Set password and choose plan
6. Complete Stripe checkout
7. Tenant becomes Active and can access tenant routes

## Import Workflow (Tenant Admin)

Routes:

- /app/imports for daily import workflow
- /app/admin/import-architecture for mapping templates
- /app/admin/import-history for import history
- /app/admin/audit-logs for tenant audit logs

## Tests

Backend tests:

cd finrecon360-backend-master

dotnet test

Frontend tests:

cd finrecon360-frontend

npm run test

## Notes

- Tenant admin audit logs: GET /api/admin/audit-logs (tenant-scoped).
- System audit logs: GET /api/system/audit-logs (system admin only).
- If you see build warnings, check angular.json budgets and SCSS sizes.

## Useful Files

- README.md (repo overview)
- DEVREADME.md (developer workflow notes)
- finrecon360-backend-master/finrecon360-backend/README.md
- finrecon360-frontend/README.md
