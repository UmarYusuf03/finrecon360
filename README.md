# FinRecon360

Monorepo for the FinRecon360 frontend, backend, and SQL project.

## Quick Links

- Developer setup: `DEVREADME.md`
- Backend implementation notes: `finrecon360-backend-master/finrecon360-backend/README.md`
- Frontend implementation notes: `finrecon360-frontend/README.md`
- Target architecture baseline: `docs/architecture/finrecon360-system-architecture.md`

## Current Repository State

The codebase currently implements these areas:

- control-plane auth and identity
- system-admin plan, tenant registration, tenant management, and enforcement flows
- tenant provisioning and tenant database creation
- tenant-scoped RBAC and tenant user management
- onboarding via magic link, password setup, and Stripe checkout activation
- canonical import foundation (upload, parse, map, validate, normalize, commit)
- basic dashboard and profile surfaces

The codebase still does not implement the full finance-operational target described in the architecture baseline. In particular, transaction state history, cashout workflow control, reconciliation orchestration, journal posting rules, and reporting snapshots are not present as working backend modules today.

## Important Contradictions To Keep In View

- Subscription enforcement now separates `MaxUsers` (tenant operational user cap) and `MaxAccounts` (bank account cap) in the `Plan` model and user-creation enforcement path.
- Global/public identity separation is now explicitly modeled through `UserType` (`GlobalPublic`, `TenantOperational`, `SystemAdmin`) with tenant-assignment guards and controlled conversion for onboarding/admin assignment flows.
- Finance workflow modules are only partially implemented at this point. Canonical import is present, but reconciliation, transaction-state history, cashout workflow gating, journal posting orchestration, and reporting snapshots are still target-state.

## Role Boundaries (Current vs Target)

- System Admin: implemented for plan, tenant registration review, tenant lifecycle enforcement, and platform governance.
- Tenant Admin: implemented for tenant user/RBAC/component/action administration and import architecture ownership.
- Global/Public User: explicitly classified via `UserType.GlobalPublic`; tenant operational users are classified separately as `UserType.TenantOperational`.

## Target Rules Tracked But Not Yet Implemented

- Cash cashout target rule: approval should be sufficient for journal posting.
- Card cashout target rule: approval should require bank-statement match before journal posting.
- Transaction lifecycle target rule: use `TransactionState` + `TransactionStateHistory` instead of single ad hoc status fields.

## Notes

- Secrets are not committed. Use `finrecon360-backend-master/finrecon360-backend/.env.example` as the local template.
- The temporary tenant-admin bypass has been removed from the documented and current code path. Local access now depends on the normal seeded system admin flow plus the real registration, approval, onboarding, and Stripe-based activation flow.
- Control-plane routes now use `/api/system/*` and system-admin screens use `/app/system/*`. Tenant-admin routes remain under `/api/admin/*` and `/app/admin/*`.
