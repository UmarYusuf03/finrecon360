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
- Google single sign-on, alongside email/password login (see "Authentication" below)
- system-admin plan, tenant registration, tenant management, and enforcement flows
- tenant provisioning and tenant database creation
- tenant-scoped RBAC and tenant user management
- onboarding via magic link, password setup, and PayHere checkout activation
- canonical import foundation (upload, parse, map, validate, normalize, commit)
- bank accounts, and the transaction workflow with approval routing and append-only state history
- the six-stage matching engine, driven per tenant by `ReconciliationCycleHostedService`
- human confirmation of match groups, and journal posting via `JournalPostingHostedService`
- dashboard and profile surfaces, backed by real aggregation over tenant data

What remains outstanding is listed under "Known gaps" below. The largest is that the
import-to-posting path has not yet been exercised end to end on real files.

## Authentication

Two sign-in routes reach the same session:

- **Email and password**, with magic-link verification and password reset.
- **Google single sign-on**, on both the login and sign-up screens.

The SSO flow is verified server-side. `GoogleIdTokenValidator` checks the ID token against
Google's published signing keys and validates the issuer, the audience, and the expiry. The
audience check is the security-critical one: without it a token minted for any other Google
application would be accepted here. Sign-ins where Google reports the email as unverified are
rejected, because the email is what an existing account is matched on.

Accounts are resolved by the provider's immutable subject identifier first, falling back to
email, so an account survives the user changing their address at Google. Newly provisioned SSO
accounts are always `GlobalPublic` with no elevated flags — an external provider establishes
identity, never privilege — and the same active-account gate as the password login applies.

Set `GOOGLE_CLIENT_ID` in the backend `.env` to enable it. When it is unset the Google button
is hidden rather than shown and failing.

## Known gaps

- The import → match → confirm → post path has not been run end to end on real files.
- Level-4 matching correlates on date and amount; `Transaction.ReferenceNumber` is now stored
  again, so reference-first matching is possible but not yet switched on.
- Credit sales and receivables (see `CREDIT_SALES_RECEIVABLES_WORKFLOW_README.md`) are
  specified but not implemented.
- No subscription expiry sweep: `CurrentPeriodEnd` is set at checkout and never read.
- The reconciliation and journal-posting hosted services sweep every tenant on a timer and
  guard concurrency with an in-process dictionary, so exactly one API instance may run.

## Important Contradictions To Keep In View

- Subscription enforcement now separates `MaxUsers` (tenant operational user cap) and `MaxAccounts` (bank account cap) in the `Plan` model and user-creation enforcement path.
- Global/public identity separation is now explicitly modeled through `UserType` (`GlobalPublic`, `TenantOperational`, `SystemAdmin`) with tenant-assignment guards and controlled conversion for onboarding/admin assignment flows.
- Finance workflow modules are now implemented rather than target-state: canonical import,
  transaction-state history, cashout workflow gating, the matching stages, and journal posting
  orchestration all exist as working modules. What has not happened yet is a single run of the
  whole path on real files.

## Role Boundaries (Current vs Target)

- System Admin: implemented for plan, tenant registration review, tenant lifecycle enforcement, and platform governance.
- Tenant Admin: implemented for tenant user/RBAC/component/action administration and import architecture ownership.
- Global/Public User: explicitly classified via `UserType.GlobalPublic`; tenant operational users are classified separately as `UserType.TenantOperational`.

## Workflow Rules (Implemented)

These were previously tracked as targets and are now enforced in code:

- **Cash cashout:** approval alone makes it journal-ready. Enforced in `TransactionService`
  approval routing, and posted by `JournalPostingExecutorWorker` without requiring a match group.
- **Card cashout:** approval routes to `NeedsBankMatch`. The worker *proposes* a Level-4 match;
  a person confirms it on the matcher screen, and only that confirmation promotes the
  transaction to `JournalReady`. The posting worker refuses unconfirmed groups.
- **Transaction lifecycle:** `TransactionState` plus append-only `TransactionStateHistory`,
  written in the same transaction as the state change so the two cannot diverge.

## Notes

- Secrets are not committed. Use `finrecon360-backend-master/finrecon360-backend/.env.example` as the local template.
- The temporary tenant-admin bypass has been removed from the documented and current code path. Local access now depends on the normal seeded system admin flow plus the real registration, approval, onboarding, and PayHere-based activation flow.
- Control-plane routes now use `/api/system/*` and system-admin screens use `/app/system/*`. Tenant-admin routes remain under `/api/admin/*` and `/app/admin/*`.
