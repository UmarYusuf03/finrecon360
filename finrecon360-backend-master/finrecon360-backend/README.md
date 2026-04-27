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
- PayHere checkout for subscription activation
- tenant and user enforcement
- tenant-scoped RBAC in tenant databases
- tenant-admin management of users, roles, permissions, components, and actions
- canonical import pipeline foundation (upload, parse, mapping, validation, normalization, commit)
- tenant-admin import architecture APIs (canonical schema and mapping-template management)
- `api/me` tenant resolution and permission hydration

Not yet implemented as finance-operational modules:

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

Each tenant database currently contains RBAC, tenant-directory, and import-foundation structures:

- `TenantUsers`
- `Roles`
- `Permissions`
- `RolePermissions`
- `AppComponents`
- `PermissionActions`
- `UserRoles`
- `ImportBatches`
- `ImportedRawRecords`
- `ImportedNormalizedRecords`
- `ImportMappingTemplates`

At present, tenant databases include canonical import tables, but do not yet contain full reconciliation, transaction-state-history, and journal-orchestration tables described in the architecture baseline.

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
6. Tenant admin selects plan and creates PayHere checkout through `api/onboarding/subscriptions/checkout`
7. PayHere webhook activates the subscription and tenant

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
- `POST /api/webhooks/payhere`

### Tenant Admin

- `GET/POST/PUT /api/admin/users`
- `PUT /api/admin/users/{userId}/roles`
- `POST /api/admin/users/{userId}/deactivate`
- `POST /api/admin/users/{userId}/activate`
- `GET/POST/PUT /api/admin/roles`
- `GET/POST/PUT /api/admin/components`
- `GET/POST/PUT /api/admin/actions`
- `GET /api/admin/permissions`

### Imports

- `POST /api/imports` (upload CSV/XLSX)
- `GET /api/imports` (history)
- `POST /api/imports/{id}/parse`
- `POST /api/imports/{id}/mapping`
- `POST /api/imports/{id}/validate`
- `POST /api/imports/{id}/commit`
- `GET /api/imports/{id}/validation-rows`
- `PUT /api/imports/{id}/raw-records/{rawRecordId}`
- `GET /api/imports/active-template`
- `DELETE /api/imports/{id}`

### Tenant Admin Import Architecture

- `GET /api/admin/import-architecture/overview`
- `GET /api/admin/import-architecture/canonical-schema`
- `GET/POST /api/admin/import-architecture/mapping-templates`
- `PUT/DELETE /api/admin/import-architecture/mapping-templates/{templateId}`
- `DELETE /api/admin/import-architecture/mapping-templates/{templateId}/hard`
- `POST /api/admin/import-architecture/batches`
- `GET /api/admin/import-architecture/batches/{batchId}`
- `POST /api/admin/import-architecture/batches/{batchId}/raw-records`
- `POST /api/admin/import-architecture/batches/{batchId}/normalized-records`

### System Admin

- `GET/POST /api/system/tenant-registrations/*`
- `GET /api/system/tenants`
- `PUT /api/system/tenants/{tenantId}/admins`
- `POST /api/system/tenants/{tenantId}/suspend`
- `POST /api/system/tenants/{tenantId}/ban`
- `POST /api/system/tenants/{tenantId}/reinstate`
- `GET/POST/PUT /api/system/plans`
- `POST /api/system/enforcement/tenants/{tenantId}/users/{userId}/*`

## Known Gaps Vs Target Architecture

### 1. Finance Workflows Are Not Built Yet

The architecture baseline documents:

- reconciliation
- cashout state rules
- journal gating
- reporting snapshots

Canonical import foundations are now present in the current codebase. Reconciliation, cashout-state workflows, journal gating, and reporting snapshots are still not present as complete backend modules.

### 2. Auth Direction Is Mixed

The target narrative describes a magic-link or token-driven direction. The current backend supports onboarding and verification magic links, but normal login remains email-plus-password with JWT issuance.

## Alignment Updates Implemented

- Subscription limits: `Plan` now models both `MaxUsers` and `MaxAccounts`, and tenant user creation enforces `MaxUsers`.
- Global/public separation: `UserType` now classifies identities as `GlobalPublic`, `TenantOperational`, or `SystemAdmin`, and tenant assignment flows enforce these boundaries.

## Target Business Rules Tracked But Not Yet Implemented

- cash cashout target rule: approval should permit journal posting
- card cashout target rule: approval should require successful bank-statement match before journal posting
- transaction audit target rule: transition tracking should be modeled through `TransactionState` and `TransactionStateHistory`

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
- PayHere settings

### PayHere configuration

The onboarding flow creates a PayHere checkout session and expects PayHere to call back the webhook.

- **Required**:
  - `PAYHERE_MERCHANT_ID`
  - `PAYHERE_MERCHANT_SECRET`
  - `PAYHERE_CHECKOUT_BASE_URL` (sandbox default is used if omitted)
  - `PAYHERE_RETURN_URL` (frontend URL PayHere redirects to after payment)
  - `PAYHERE_CANCEL_URL` (frontend URL PayHere redirects to on cancel)
  - `PAYHERE_NOTIFY_URL` (backend webhook endpoint, typically `.../api/webhooks/payhere`)
  - `PAYHERE_CURRENCY` (default `LKR`)
- **Local/dev only**:
  - `PAYMENT_ALLOW_LOCAL_BYPASS=true` allows onboarding to activate a subscription locally when the payment provider is not configured. This bypass is ignored in production.

## Local Development Notes

- Startup runs control-plane EF migrations automatically outside testing and design-time environments.
- Startup seeding creates roles, permissions, components, actions, and the configured system admin.
- Temporary tenant bypass seeding has been removed. Use the real registration, approval, onboarding, and PayHere flow for tenant activation.
