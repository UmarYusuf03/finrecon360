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
- basic dashboard and profile surfaces

The codebase does not yet implement the full finance-operational target described in the architecture baseline. In particular, canonical ERP import, transaction state history, cashout workflow control, reconciliation orchestration, journal posting rules, and reporting snapshots are not present as working backend modules today.

## Important Contradictions To Keep In View

- Subscription enforcement does not yet match the target design. The current `Plan` model exposes `MaxAccounts`, and tenant user creation currently uses that field as the user-cap check.
- The target design treats global or public users as distinct from tenant operational users. The current implementation still centers on one global `Users` table plus tenant membership links, so the distinction is not modeled as cleanly as the target architecture describes.
- Finance workflow modules in the architecture document are target-state only at this point; the current repository mainly implements onboarding, tenancy, and RBAC foundations.

## Notes

- Secrets are not committed. Use `finrecon360-backend-master/finrecon360-backend/.env.example` as the local template.
- The temporary tenant-admin bypass has been removed from the documented and current code path. Local access now depends on the normal seeded system admin flow plus the real registration, approval, onboarding, and Stripe-based activation flow.
- Control-plane routes now use `/api/system/*` and system-admin screens use `/app/system/*`. Tenant-admin routes remain under `/api/admin/*` and `/app/admin/*`.
