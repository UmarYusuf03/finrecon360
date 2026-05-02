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

## Implementation status — Missing / Planned features

The repository README documents the target design but some runtime modules are documented as target behavior rather than fully implemented code. The items below are intentionally tracked here so readers know what is design-only vs implemented.

- Reconciliation engine (bank/payment matcher): design present; implementation incomplete.
- Bank-statement matching module: design present as a handoff (`NeedsBankMatch`) but matcher implementation is TODO.
- Journal posting executor: the `journal-ready` queue is present but the posting worker is not implemented.
- Reporting snapshot jobs and reporting tables: recommended approach documented; job implementations are TODO.
- Global/Public user product surface: concept documented, but final functional responsibilities should be confirmed (see "Assumptions to confirm").
- Tenant onboarding defaults: some seeding exists but whether all tenant defaults are auto-seeded or require tenant-admin actions should be confirmed.

Assumptions to confirm (short list)

- Approval ownership: Tenant Admin configures approvers and role routing; by default tenant admins assign who can approve, not System Admin.
- Global user purpose: global/public users are distinct from tenant operational users and are not visible in tenant user lists by default.
- Journal posting timing matrix: cash cashouts → post after approval; card cashouts → require bank-statement match after approval before posting.

If you want, I can open a PR that copies these sections into `finrecon360-backend-master/finrecon360-backend/README.md`, `finrecon360-frontend/README.md`, and `docs/architecture/finrecon360-system-architecture.md` (already listed in this repo) so all four docs contain the same implementation-status notes.

## New: "Ironclad" Data Pipeline (Canonical Import + Tiered Matching)

Summary

The repository now documents a stricter import and matching design called the "Ironclad" Data Pipeline. This enforces that no raw source data reaches the matcher without canonical normalization and introduces a tiered matcher that minimizes revenue leakage.

Key points

- All source files (ERP, POS, Payment Gateway, Bank) must pass through the Import & Canonicalization Module.
- The pipeline steps: Upload → Parse & Preview → Map → Normalize → Persist (raw + normalized).
- Tenant DB entities to store: `ImportedRecords`, `TransactionState`, `StateHistory`, and `MatchGroups` (see backend README for field suggestions).
- Matching logic runs in four levels: Operational (POS vs staff input), Sync Audit (POS vs ERP), Sales Match (ERP vs Gateway), and Bank Match (Gateway payout vs bank line). Human confirmation is required before marking `MATCHED` and enabling journal posting.
- Special rule: transactions flagged `SALES_VERIFIED` without a `SettlementID` stay in an Exception/Waiting queue until a future Gateway import provides the ID.

Where to read more

See `finrecon360-backend-master/finrecon360-backend/README.md`, `finrecon360-frontend/README.md`, and `docs/architecture/finrecon360-system-architecture.md` for the full pipeline text and implementation notes.

## Ironclad Hierarchical Matching Workflow (Baseline)

This is the current workflow baseline the docs should reflect for implementation planning.

| Level | Match Type | Source A | Source B | Match Key | Result |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Operational | Staff Manual Input | POS End-of-Day (EOD) | Time + Amount + Ref | `INTERNAL_VERIFIED` |
| 2 | Sync Audit | POS EOD Export | ERP Sales Ledger | `ReferenceNo` / Order ID | `SALES_VERIFIED` |
| 3 | Sales Match | ERP Sales Ledger | Payment Gateway File | `ReferenceNo` / Order ID | `SALES_VERIFIED` or `EXCEPTION` |
| 4 | Bank Match | Gateway Payout Total | Bank Statement | `SettlementID` | `MATCHED` after human confirmation |

Workflow rules

- All external files must be normalized before they can reach matching.
- `ImportedRecords` stores raw payload plus normalized fields.
- `TransactionState` and `TransactionStateHistory` must be append-only and auditable.
- `MatchGroups` are settlement groups used for bank comparison.
- If a Gateway row has no `SettlementID`, it can still reach `SALES_VERIFIED` but must stay in a Waiting/Exception queue until the ID arrives in a later import.
- Card cashouts remain blocked from journal posting until bank matched.
- Cash cashouts can proceed to journal posting after approval, subject to the same audit trail.

Baseline difference to keep in mind

- The existing implementation docs still describe a partial transaction workflow with `Pending`, `NeedsBankMatch`, and `JournalReady` states.
- The new Ironclad baseline adds the POS and ERP audit layers and makes `SettlementID` the explicit bank-match key.

## Ironclad Master Matching Matrix (4 Stages, 6 Events)

The 4 stages describe the system workflow sequence. The 6 events describe the complete business-rule matrix the orchestrator must apply across different money types.

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
- Gateway rows missing `SettlementID` can complete Stage 3 but remain blocked from Stage 4 in the waiting/exception queue.

Implementation note

- The matching engine should be treated as a rule matrix, not a single linear path. This keeps the workflow order fixed while allowing each business rule to target the correct money type.

## Gross/Net/Fee Matching Logic

Data model requirements

- Store `GrossAmount`, `FeeAmount`, and `NetAmount` in `ImportedRecords` for all Payment Gateway sources.
- Map these fields separately in the canonical import pipeline from gateway CSV or Excel files.

Level 3 match: Sales Verification

- Target: ERP Sales Ledger versus Payment Gateway details.
- Matching rule: compare ERP `Amount` with gateway `GrossAmount`.
- Key: `ReferenceNo` / Order ID.
- Success condition: values must match exactly to reach `SALES_VERIFIED`.

Level 4 match: Settlement Verification

- Target: Payment Gateway `MatchGroup` versus Bank Statement.
- Matching rule:
	1. Group all gateway records by `SettlementID`.
	2. Sum the `NetAmount` values for the group.
	3. Compare `Sum(NetAmount)` against the bank statement deposit line.
- Key: `SettlementID`.

Automated journaling rule

- On `MATCHED`, generate an automated adjusting journal entry for the `FeeAmount` total associated with the `SettlementID`.
- Purpose: reconcile the difference between Revenue (`Gross`) and Cash (`Net`) by recording the processing fee as an expense.
