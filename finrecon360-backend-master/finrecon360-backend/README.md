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
- tenant-admin bank account management
- tenant-admin transaction capture
- transaction approval and rejection with state history
- journal-ready transaction queue
- needs-bank-match transaction queue
- `api/me` tenant resolution and permission hydration

Not yet implemented as finance-operational modules:

- bank statement matching
- full cash-in and cashout workflow orchestration
- human-confirmed reconciliation engine
- journal posting execution workflow
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
- `BankAccounts`
- `Transactions`
- `TransactionStateHistories`
- import staging and mapping tables

Tenant finance tables are created by `TenantSchemaMigrator` when a tenant DB is initialized or opened through the tenant DB factory.

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
- `GET/POST/PUT/DELETE /api/admin/bank-accounts`
- `GET/POST /api/admin/transactions`
- `GET /api/admin/transactions/{id}`
- `POST /api/admin/transactions/{id}/approve`
- `POST /api/admin/transactions/{id}/reject`
- `GET /api/admin/transactions/{id}/history`
- `GET /api/admin/transactions/journal-ready`
- `GET /api/admin/transactions/needs-bank-match`

## Member 3 Tenant Finance Modules

### Bank Accounts

Bank accounts are tenant-scoped business data. They are stored in each tenant database, not the control-plane database.

Current backend endpoints:

- `GET /api/admin/bank-accounts`
- `GET /api/admin/bank-accounts/{id}`
- `POST /api/admin/bank-accounts`
- `PUT /api/admin/bank-accounts/{id}`
- `DELETE /api/admin/bank-accounts/{id}` soft-deactivates the account

Required permissions:

- `ADMIN.BANK_ACCOUNTS.VIEW`
- `ADMIN.BANK_ACCOUNTS.MANAGE`

### Transactions

Transactions are tenant-scoped and are linked optionally to a bank account.

Current transaction fields include:

- amount
- transaction date
- description
- transaction type: `CashIn` or `CashOut`
- payment method: `Cash` or `Card`
- optional bank account
- current state
- approval/rejection metadata

Validation rules:

- `Amount` must be greater than zero.
- `TransactionDate` is required.
- `Card` transactions require `BankAccountId`.
- `Cash` transactions may have `BankAccountId` null or populated.

Required permissions:

- `ADMIN.TRANSACTIONS.VIEW`
- `ADMIN.TRANSACTIONS.MANAGE`

### Transaction States

Current states:

- `Pending`: transaction was created and is waiting for approval.
- `Approved`: retained as an enum value, but current approval flow moves approved items into the next operational state.
- `Rejected`: transaction was rejected with a reason.
- `NeedsBankMatch`: approved card cash-out that must be matched before journal posting.
- `JournalReady`: approved transaction ready for journal posting.

Every create, approval, and rejection writes a `TransactionStateHistory` record.

Transaction history can be read with:

- `GET /api/admin/transactions/{id}/history`

### Approval And Rejection Flow

Approval rules:

- Only `Pending` transactions can be approved.
- Approved `CashOut` + `Card` transactions move to `NeedsBankMatch`.
- Other approved transactions move to `JournalReady`.

Rejection rules:

- Only `Pending` transactions can be rejected.
- A rejection reason is required.
- Rejected transactions move to `Rejected`.

### Lifecycle Flow

- Create transaction: starts at `Pending`.
- Approve cash transaction: moves directly to `JournalReady`.
- Approve card `CashOut`: moves to `NeedsBankMatch`.
- `NeedsBankMatch` is a handoff state for the matcher module and does not appear in the journal-ready queue.
- Reject transaction: moves to `Rejected`.

### Journal-Ready Queue

The journal-ready queue is read-only for now:

- `GET /api/admin/transactions/journal-ready`

It returns transactions where `TransactionState == JournalReady`, ordered by transaction date and creation time. Journal posting itself is not implemented yet.

### Needs-Bank-Match Queue

The needs-bank-match queue is read-only for now:

- `GET /api/admin/transactions/needs-bank-match`

It returns transactions where `TransactionState == NeedsBankMatch`. This is the handoff point for future matcher/reconciliation work; this module does not confirm matches or move those transactions to `JournalReady`.

### Tenant Admin Vs System Admin

System admins manage the control plane. They do not automatically have access to tenant finance data.

To use tenant finance endpoints, the logged-in user must:

- be authenticated
- resolve to an active tenant
- exist in control-plane `TenantUsers`
- exist in the tenant database `TenantUsers`
- have the required tenant permissions in the tenant database

Use a tenant admin user created during tenant approval/onboarding for finance testing. If you need a system admin to test tenant finance locally, explicitly add that user to the tenant and tenant database instead of relying on `IsSystemAdmin`.

### Local Setup Checklist

For Bank Accounts and Transactions to work locally:

- The tenant must be `Active`.
- The tenant database must exist and be reachable through `TENANT_DB_TEMPLATE`.
- The tenant user must exist in the control-plane `TenantUsers` table.
- The same user must exist in the tenant database `TenantUsers` table.
- The tenant admin role must have `ADMIN.BANK_ACCOUNTS.*` and `ADMIN.TRANSACTIONS.*` permissions.
- Send `X-Tenant-Id` when testing if the user has multiple tenant memberships.

### Postman Testing Flow

Use a tenant-admin JWT and include:

- `Authorization: Bearer <token>`
- `X-Tenant-Id: <tenant-guid>`

Create a cash transaction:

```http
POST /api/admin/transactions
Content-Type: application/json

{
  "amount": 1250.00,
  "transactionDate": "2026-04-27T00:00:00Z",
  "description": "Cash receipt",
  "bankAccountId": null,
  "transactionType": "CashIn",
  "paymentMethod": "Cash"
}
```

Approve the cash transaction. It should become `JournalReady`:

```http
POST /api/admin/transactions/{transactionId}/approve
Content-Type: application/json

{
  "note": "Approved for journal posting"
}
```

Create a card cash-out transaction:

```http
POST /api/admin/transactions
Content-Type: application/json

{
  "amount": 500.00,
  "transactionDate": "2026-04-27T00:00:00Z",
  "description": "Card vendor payment",
  "bankAccountId": "<bank-account-guid>",
  "transactionType": "CashOut",
  "paymentMethod": "Card"
}
```

Approve the card cash-out. It should become `NeedsBankMatch`:

```http
POST /api/admin/transactions/{transactionId}/approve
Content-Type: application/json

{
  "note": "Approved, pending bank match"
}
```

Check the journal-ready queue:

```http
GET /api/admin/transactions/journal-ready
```

Only `JournalReady` transactions should appear. `NeedsBankMatch` transactions should not appear.

Check the needs-bank-match queue:

```http
GET /api/admin/transactions/needs-bank-match
```

Only `NeedsBankMatch` transactions should appear.

Check a transaction lifecycle history:

```http
GET /api/admin/transactions/{transactionId}/history
```

### Frontend Routes

Current tenant-admin frontend routes:

- `/app/admin/bank-accounts`
- `/app/transactions`
- `/app/transactions/journal-ready`
- `/app/transactions/needs-bank-match`

Old `/app/admin/transactions`, `/app/admin/journal-ready`, and `/app/admin/needs-bank-match` URLs redirect to the new workflow routes.

### Member 3 - Transactions & Approval Workflow (Final)

Member 3 owns tenant-scoped transaction capture, approvals, state history, and queue visibility. These pages now live in a dedicated Transactions module instead of the Admin configuration area.

Features:

- Transaction creation for `CashIn` and `CashOut`.
- Approval and rejection workflow.
- Transaction states: `Pending`, `JournalReady`, `NeedsBankMatch`, and `Rejected`.
- State transition rule: `CashOut` with `Cash` moves to `JournalReady` after approval.
- State transition rule: `CashOut` with `Card` moves to `NeedsBankMatch` after approval.
- Edit is allowed only while a transaction is `Pending`.
- Transaction history tracks lifecycle transitions, timestamps, user IDs, and notes.

UI improvements:

- Transactions moved out of Admin into `/app/transactions`.
- Workflow navigation: Transactions / Journal Ready / Needs Bank Match.
- Clickable `JournalReady` and `NeedsBankMatch` state badges route to the matching queue.
- Journal Ready supports Date / Amount sorting and Year / Month filtering.
- Transaction tables use modern, softer styling consistent with the FinRecon360 theme.

Validation:

- Transaction date is validated with a minimum of `2000-01-01` and a maximum of today.
- Card transactions require a bank account.
- Save and workflow actions prevent duplicate submissions while requests are in progress.
- Approved and rejected transactions are immutable.

Audit:

- The Transaction History modal shows amount, transaction date, type/method, current state, transitions, timestamps, changed-by user ID, and notes.
- State changes are append-only through `TransactionStateHistory`.

Notes:

- Approved transactions cannot be edited.
- Corrections must be handled through future reversal or adjustment transactions to preserve audit integrity.

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

### 1. Global User Separation Is Not Fully Modeled

The target design describes global or public users as conceptually distinct from tenant operational users. The current backend uses a shared global `Users` table and links those records into tenant membership. That is workable, but it is not a strict separate-identity model.

### 2. Subscription Limits Do Not Match The Intended Semantics

`Plan` currently exposes `MaxAccounts`. Tenant user creation currently reads that field to enforce the tenant user cap. This is a mismatch between model name and actual behavior.

### 3. Finance Workflows Are Partially Built

The architecture baseline documents:

- reconciliation
- full cashout state rules
- journal gating
- reporting snapshots

Bank accounts, basic transaction capture, approval/rejection, transaction history, and a journal-ready queue are present. Bank matching, journal posting execution, reconciliation, and reporting snapshots are still pending.

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

## Local Development Notes

- Startup runs control-plane EF migrations automatically outside testing and design-time environments.
- Startup seeding creates roles, permissions, components, actions, and the configured system admin.
- Temporary tenant bypass seeding has been removed. Use the real registration, approval, onboarding, and PayHere flow for tenant activation.
