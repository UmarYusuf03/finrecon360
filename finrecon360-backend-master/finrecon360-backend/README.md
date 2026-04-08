# FinRecon360 Backend

This README describes the backend as it is currently implemented in this repository. It is not a statement that the full target architecture has already been built.

For target-state design, see `../../docs/architecture/finrecon360-system-architecture.md`.

## Current Functional Scope

Implemented backend areas:

- control-plane users and system admin seeding
- JWT login
- magic-link flows for email verification, password reset, change-password, and tenant onboarding
- public tenant registration
- system-admin review of tenant registrations
- tenant provisioning with one tenant database per tenant
- Stripe checkout for subscription activation
- tenant and user enforcement
- tenant-scoped RBAC in tenant databases
- tenant-admin management of users, roles, permissions, components, and actions
- `api/me` tenant resolution and permission hydration

Not yet implemented as finance-operational modules:

- canonical ERP import pipeline
- transaction state and transaction state history
- cash-in and cashout workflow orchestration
- bank statement matching
- human-confirmed reconciliation engine
- journal posting workflow
- reporting snapshot jobs and reporting tables

## Architecture Boundaries In Code

### Control Plane

Current control-plane entities include:

- `Users`
- `Roles`
- `Permissions`
- `RolePermissions`
- `Tenants`
- `TenantRegistrationRequests`
- `TenantDatabases`
- `Plans`
- `Subscriptions`
- `PaymentSessions`
- `EnforcementActions`
- `MagicLinkTokens`

### Tenant Database

Each tenant database currently contains RBAC and tenant-directory structures:

- `TenantUsers`
- `Roles`
- `Permissions`
- `RolePermissions`
- `AppComponents`
- `PermissionActions`
- `UserRoles`

At present, tenant databases do not yet contain the finance-operational tables described in the architecture baseline.

## Auth And Onboarding Flow

### Auth

The backend supports:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/verify-email-link`
- `POST /api/auth/request-password-reset-link`
- `POST /api/auth/confirm-password-reset-link`
- `POST /api/auth/request-change-password-link`
- `POST /api/auth/confirm-change-password-link`

This means the current implementation is password-based login with magic-link support for verification and password-management flows. It is not a pure magic-link login system.

### Tenant Onboarding

The current onboarding path is:

1. `POST /api/public/tenant-registrations`
2. System admin reviews through `api/system/tenant-registrations`
3. Approval provisions:
   - control-plane tenant record
   - tenant DB
   - tenant DB schema
   - initial tenant-admin membership
4. Tenant admin receives onboarding magic link
5. Tenant admin sets password through `api/onboarding/set-password`
6. Tenant admin selects plan and creates Stripe checkout through `api/onboarding/subscriptions/checkout`
7. Stripe webhook activates the subscription and tenant

## Authorization Model

### System-Admin And Tenant-Admin Split

The repository already separates permissions conceptually:

- control-plane permissions such as tenant registrations, plans, tenants, and enforcement
- tenant-scoped permissions such as users, roles, components, and permissions

The route split now reflects that boundary:

- control-plane routes use `/api/system/*`
- tenant-admin routes use `/api/admin/*`

### Tenant Resolution

Tenant resolution is handled by:

- `X-Tenant-Id` when supplied
- fallback membership resolution when not supplied

For users with multiple tenant memberships, fallback resolution prefers:

1. active tenants
2. tenant-admin memberships
3. latest activation or creation

### Tenant Status Gating

Backend auth and authorization enforce active-tenant access for tenant-scoped routes.

- JWT validation skips tenant-status checks for `/api/admin`, `/api/system`, `/api/public`, `/api/onboarding`, `/api/auth`, `/api/webhooks`, and `/api/me`
- tenant-scoped permission checks in `PermissionHandler` require `TenantStatus.Active`

## Current API Areas

### Public

- `POST /api/public/tenant-registrations`
- `GET /api/public/plans`

### Onboarding

- `POST /api/onboarding/magic-link/verify`
- `POST /api/onboarding/set-password`
- `POST /api/onboarding/subscriptions/checkout`
- `POST /api/webhooks/stripe`

### Tenant Admin

- `GET/POST/PUT /api/admin/users`
- `PUT /api/admin/users/{userId}/roles`
- `POST /api/admin/users/{userId}/deactivate`
- `POST /api/admin/users/{userId}/activate`
- `GET/POST/PUT /api/admin/roles`
- `GET/POST/PUT /api/admin/components`
- `GET/POST/PUT /api/admin/actions`
- `GET /api/admin/permissions`

### System Admin

- `GET/POST /api/system/tenant-registrations/*`
- `GET /api/system/tenants`
- `PUT /api/system/tenants/{tenantId}/admins`
- `POST /api/system/tenants/{tenantId}/suspend`
- `POST /api/system/tenants/{tenantId}/ban`
- `POST /api/system/tenants/{tenantId}/reinstate`
- `GET/POST/PUT /api/system/plans`
- `POST /api/system/enforcement/tenants/{tenantId}/users/{userId}/*`

## Known Contradictions And Gaps Vs Target Architecture

### 1. Global User Separation Is Not Fully Modeled

The target design describes global or public users as conceptually distinct from tenant operational users. The current backend uses a shared global `Users` table and links those records into tenant membership. That is workable, but it is not a strict separate-identity model.

### 2. Subscription Limits Do Not Match The Intended Semantics

`Plan` currently exposes `MaxAccounts`. Tenant user creation currently reads that field to enforce the tenant user cap. This is a mismatch between model name and actual behavior.

### 3. Finance Workflows Are Not Built Yet

The architecture baseline documents:

- canonical imports
- reconciliation
- cashout state rules
- journal gating
- reporting snapshots

Those are not present as working backend modules in the current codebase.

### 4. Auth Direction Is Mixed

The target narrative describes a magic-link or token-driven direction. The current backend supports onboarding and verification magic links, but normal login remains email-plus-password with JWT issuance.

## Environment Variables

See `.env.example` for the current template.

Important values:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audience`
- `SYSTEM_ADMIN_EMAIL`
- `SYSTEM_ADMIN_PASSWORD`
- `TENANT_DB_TEMPLATE`
- Brevo settings
- Stripe settings

## Local Development Notes

- Startup runs control-plane EF migrations automatically outside testing and design-time environments.
- Startup seeding creates roles, permissions, components, actions, and the configured system admin.
- Temporary tenant bypass seeding has been removed. Use the real registration, approval, onboarding, and Stripe flow for tenant activation.
