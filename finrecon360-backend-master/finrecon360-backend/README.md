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

### Implementation Notes From Today

- Tenant admin RBAC pages are working again after hardening tenant schema preparation in `TenantSchemaMigrator`.
- The schema migrator now drops and recreates `IX_ImportedNormalizedRecords_TransactionDate` around the `TransactionDate` alteration to avoid hidden dependency failures.
- Import architecture already supports mapping templates, canonical schema versioning, validation, and row correction.
- Invalid import rows are preserved in `ImportedRawRecords` with `NormalizationStatus` and `NormalizationErrors`, and they can be corrected through `PUT /api/imports/{id}/raw-records/{rawRecordId}`.
- The frontend already has screens for import workbench, import architecture admin, journal-ready, and needs-bank-match, so the above flows are available in the UI.

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

## Implementation status — Missing / Planned backend work

This backend README documents the current API shape and target workflow. Several backend workers and modules are intentionally not implemented yet and are tracked here for clarity:

- Matcher / bank-statement reconciliation engine: the `NeedsBankMatch` handoff state exists; the matcher that consumes that queue and confirms matches is TODO.
- Journal posting executor: the journal-ready queue exists (`GET /api/admin/transactions/journal-ready`) but the worker/process that creates and posts journal entries is not implemented.
- Reporting snapshot jobs: reporting architecture and snapshot table recommendations exist; job implementations are TODO.
- Tenant migration/migrator orchestration: tenant schema migrator exists for provisioning, but a robust cross-tenant migration orchestration process is a planned improvement.

Assumptions and confirmations needed

- Global/Public user role and product surface needs a short spec; currently they are treated as separate from tenant operational users.
- Tenant setup defaults: some seeds are created on provisioning; confirm which defaults should be fully auto-seeded vs tenant-admin configurable.
- Approval ownership: tenant admins configure approvers; the system assumes tenant admins assign approvers and routing rather than System Admin acting as approver.

Business rule matrix (encode these in domain logic)

- Cash cashout: Approved -> `JournalReady` (eligible for journal posting).
- Card cashout: Approved -> `NeedsBankMatch` -> (after bank match) `JournalReady` -> posting.

If you want, I can add TODO markers and example scaffold code for the matcher and journal worker in `Services/` and a tenant-migrator in `Services/Migrations/`.

## Ironclad Data Pipeline — Import & Tiered Matching (Detailed)

Overview

This project documents the "Ironclad" pipeline: a canonical import-first approach followed by a four-level workflow to ensure operational input is verified, POS/ERP sync is audited, sales are verified, and bank lines are confirmed before journal posting.

Import pipeline responsibilities (backend)

- Accept uploads (`CSV`/`Excel`) and persist raw payloads and metadata.
- Parse headers and provide preview data to the frontend mapping UI.
- Apply tenant-configured mapping templates and normalization rules (dates, sign handling, trimming, currency normalization).
- Persist both `ImportedRecords.RawPayload` and the normalized record used by the matcher.

Suggested tenant DB entities (primary fields)

- `ImportedRecords`: `RawPayload` (JSON), `ReferenceNo`, `SettlementID`, `Amount`, `Date`, `SourceType`, `BatchID`.
- `TransactionState`: `CurrentStatus`, `TransactionID`, `TenantID` (states include: `PENDING`, `SALES_VERIFIED`, `EXCEPTION`, `NEEDS_SETTLEMENT`, `MATCHED`).
- `StateHistory`: `ActorID`, `Timestamp`, `OldState`, `NewState`, `ChangeNote`.
- `MatchGroups`: `GroupType`, `CreatedDate`, `TotalAmount`, `IsConfirmed`, `SettlementID`.

Tiered matching logic (backend workflow)

1. Sales Verification (Level 1)
  - Trigger: import of ERP sales ledger + payment gateway file.
  - Rule: exact `ReferenceNo` (order id) match between ERP and Gateway.
  - Outcome: matched items -> `SALES_VERIFIED`; missing gateway -> `EXCEPTION` (alert: "Revenue Uncollected").

2. Settlement Grouping (Level 2)
  - Trigger: payment gateway record with `SettlementID`.
  - Rule: group transactions by `SettlementID`, compute Net Total (Gross - Fees).
  - Outcome: create `MatchGroup` representing the settlement batch for final bank matching.

3. Final Bank Match (Level 3)
  - Trigger: bank statement import.
  - Rule: compare `MatchGroup.NetTotal` against bank statement line amount.
  - Outcome: system suggests a match; human confirms; state becomes `MATCHED` and transaction(s) become eligible for journal posting.

Waiting / Exception behavior

- If a Gateway-imported record lacks a `SettlementID`, set `SALES_VERIFIED` but place it in an Exception/Waiting queue. It may progress only when a later Gateway import supplies the `SettlementID` or a manual reconciliation action supplies it.

Operational notes

- The matcher should be implemented as a background worker that consumes normalized records and the needs-match queues.
- Human confirmation is mandatory for the final bank match to protect against false positives and revenue leakage.
- Journal posting remains gated until `MATCHED` state is reached (or another domain rule explicitly allows posting).

If you want, I can scaffold: `Services/Matcher/SettlementGrouper.cs`, `Services/Matcher/BankMatcher.cs`, and a simple worker `Workers/JournalPosterWorker.cs` that reads `JournalReady` items and creates journal entries (no accounting entries implemented yet).

## Ironclad Hierarchical Workflow Baseline (Target Design)

The backend implementation still has the current transaction states and queues documented above, but the target baseline now includes four matching levels and an explicit Waiting state for gateway rows with missing settlement identifiers.

| Level | Match Type | Source A | Source B | Match Key | Target Result |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Operational | Staff Manual Input | POS End-of-Day (EOD) | Time + Amount + Ref | `INTERNAL_VERIFIED` |
| 2 | Sync Audit | POS EOD Export | ERP Sales Ledger | `ReferenceNo` / Order ID | `SALES_VERIFIED` |
| 3 | Sales Match | ERP Sales Ledger | Payment Gateway File | `ReferenceNo` / Order ID | `SALES_VERIFIED` or `EXCEPTION` |
| 4 | Bank Match | Gateway Payout Total | Bank Statement | `SettlementID` | `MATCHED` after human confirmation |

Target backend state rules

- `ImportedRecords` should preserve `RawPayload` plus normalized fields for audit and replay.
- `TransactionState` should track the active state per workflow item; `TransactionStateHistory` should be append-only.
- `MatchGroups` should group gateway sales by `SettlementID` and store aggregate settlement totals.
- Gateway rows without a `SettlementID` may remain `SALES_VERIFIED` but must be blocked in the Waiting/Exception queue until a later import provides the settlement identifier.
- Only bank-confirmed items should be eligible for journal posting in the final stage.

Implementation difference to remember

- Current code and current README sections still expose the partial finance workflow (`Pending`, `NeedsBankMatch`, `JournalReady`).
- The Ironclad baseline is the stronger target design for planning the next development work, especially the matcher and journal worker.

## Ironclad Master Matching Matrix (4 Stages, 6 Events)

The backend orchestrator should treat the workflow as a fixed sequence of 4 stages, with 6 business-rule events distributed across those stages.

| Stage | Event | Purpose | Comparison | Key | Expected Result |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Operational Match | Verify staff entries against POS EOD | Staff Manual Input ↔ POS EOD | Time + Amount + Ref | `INTERNAL_VERIFIED` and booking reference inherited |
| 2 | Sync Audit | Ensure POS made it into the books | POS EOD ↔ ERP Sales Ledger | `ReferenceNo` / Order ID | Accounting sync confirmed |
| 3 | Sales Match | Prove sale was charged | ERP Sales Ledger ↔ Payment Gateway File | `ReferenceNo` / Order ID | `SALES_VERIFIED` or `EXCEPTION` |
| 4 | Settlement Match | Confirm online payout landed in bank | Gateway Payout Totals ↔ Bank Statement | `SettlementID` | `MATCHED` after human confirmation |
| 4 | Expense Match | Gate approved card cashout before journal posting | Approved Card Cashout ↔ Bank Statement | Auth Code / Ref | Must match before posting |
| 4 | Collection Match | Confirm physical card receipt settlement | Physical Card Receipt ↔ Bank Statement | Settlement / receipt ref | Matched before posting |

Orchestrator rules

- If `SourceType` is POS, run Stage 1.
- If `SourceType` is ERP, run Stages 2 and 3.
- If `SourceType` is Gateway, run Stages 3 and 4.
- If `SourceType` is Bank, run Stage 4 events.
- Stage 4 branches by payment type: online payout, card cashout, or physical card receipt.
- Gateway rows missing `SettlementID` may complete Stage 3 but remain blocked from Stage 4 in the waiting/exception queue.

Implementation note

- The matcher should be implemented as a rule matrix with stage routing, not a single one-path workflow, so the same orchestrator can validate multiple money flows correctly.

## Gross/Net/Fee Matching Logic

Data model requirements

- `ImportedRecords` must store `GrossAmount`, `FeeAmount`, and `NetAmount` for all Payment Gateway sources.
- The canonical import pipeline must map these fields separately from the gateway CSV/Excel payload.

Level 3 match: Sales Verification

- Target: ERP Sales Ledger versus Payment Gateway details.
- Matching rule: compare ERP `Amount` with gateway `GrossAmount`.
- Key: `ReferenceNo` / Order ID.
- Success condition: exact value match to reach `SALES_VERIFIED`.

Level 4 match: Settlement Verification

- Target: Payment Gateway `MatchGroup` versus Bank Statement.
- Matching rule:
  1. Group gateway rows by `SettlementID`.
  2. Sum the `NetAmount` values for the group.
  3. Compare `Sum(NetAmount)` against the bank statement deposit line.
- Key: `SettlementID`.

Automated journaling rule

- When a match reaches `MATCHED`, generate an adjusting journal entry for the `FeeAmount` total associated with the `SettlementID`.
- The journal entry exists to capture the processing fee as an expense while reconciling Gross revenue to Net cash received.

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
