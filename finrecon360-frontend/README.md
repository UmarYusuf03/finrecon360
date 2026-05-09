# FinRecon360 Frontend

Angular frontend for FinRecon360.

This README describes the frontend as currently implemented, not the full target architecture.

## Development Server

```bash
ng serve
```

Open `http://localhost:4200/`.

## Current Frontend Scope

Implemented frontend areas:

- login, registration, email-verification, password reset, and change-password UX
- onboarding magic-link verification, password setup, and subscription checkout start
- public tenant registration
- system-admin screens for tenant registrations, tenants, plans, and enforcement
- tenant-admin screens for users, roles, permissions, and components
- tenant-admin import architecture screens (canonical schema and mapping-template management)
- imports workbench (upload, parse, map, validate, row correction, commit, delete)
- dashboard, matcher placeholder, and profile surfaces
- permission-aware navigation and route guards

Not yet implemented as full production workflows:

- bank statement import workflow
- transaction approvals workflow
- exception management workflow
- journal posting workflow
- reporting and analytics surfaces matching the target architecture

## Admin Ownership Split

The frontend currently distinguishes:

- system-admin surfaces: tenant registrations, tenants, plans, enforcement
- tenant-admin surfaces: users, roles, permissions, components

This split is implemented in `admin-shell` by checking `user.isSystemAdmin` and permission scope.

Current route split:

- system-admin screens use `/app/system/*`
- tenant-admin screens use `/app/admin/*`

## Auth And Access Model

### Current Auth UX

The frontend supports:

- email/password login
- user registration
- email verification via magic link
- password reset via magic link
- change-password confirmation flow
- tenant onboarding magic link verification

So the current UX is not magic-link-only authentication.

### Route Protection

Frontend guards are UX helpers only. Real enforcement is backend-side.

The current frontend now blocks non-system-admin access when `tenantStatus !== 'Active'` for tenant-scoped areas.

## Current Route Areas

- `/auth/*` for auth and magic-link flows
- `/onboarding/*` for onboarding subscription flow
- `/app/system/*` for system-admin screens
- `/app/admin/*` for tenant-admin screens
- `/app/admin/import-architecture` for canonical schema and mapping-template management
- `/app/admin/import-history` for tenant import history admin view
- `/app/imports` for operational import workbench flow
- `/app/matcher` as the current reconciliation placeholder surface
- `/app/profile`

## Backend Dependency Notes

The frontend expects the current backend onboarding flow:

1. Public tenant registration
2. System-admin approval
3. Tenant onboarding magic link
4. Password setup
5. Stripe checkout
6. Tenant activation

The temporary tenant-admin bypass is no longer part of the supported path.

## Testing

Unit tests:

```bash
ng test --watch=false --browsers=ChromeHeadless
```

Test behavior:

- tests use `src/environments/environment.test.ts`
- `mockApi` is `true` in test runs
- frontend unit tests do not depend on backend `.env` values

## Known Contradictions And Gaps Vs Target Architecture

### 1. Finance UX Is Partially Implemented

The target architecture includes imports, canonical mapping, approvals, reconciliation confirmation, journal gating, and reporting.

Current frontend has working canonical import and mapping-template UX, but reconciliation, approvals, journal posting, bank-statement matching, and reporting remain incomplete or placeholder.

### 2. Global User Concept Is Not Expressed Cleanly In UI

The target design distinguishes global or public users from tenant operational users. The current frontend primarily exposes:

- public registration
- system-admin context
- tenant-admin context

There is no fully realized standalone global-user product area yet.

### 3. Some Shared Labels Still Say "Admin"

The route split is now in place, but some shared component names and labels still use generic "admin" wording. That is naming debt rather than a routing-boundary problem.

## Target Rule Note (Not Yet In UI Workflow)

- cash cashout target: approval should allow journal posting
- card cashout target: approval should require bank-statement match before journal posting

## Implementation status — Missing / Planned frontend work

The frontend documents the intended workflows but several operational surfaces remain placeholder or incomplete. Tracked items:

- Reconciliation/matcher UI: currently a placeholder (`/app/matcher`) — needs UI to confirm system-proposed matches and surface `NeedsBankMatch` items.
- Journal posting UI: `journal-ready` queue exists but final posting flow and confirmation screens are not implemented.
- Reporting dashboards: reporting surfaces are partially stubbed and rely on snapshot jobs (not implemented) for production performance.
- Global/Public user UX: concept exists but there is no dedicated product area yet; the distinction is enforced in backend identity but frontend needs a short spec for pages and flows.

UX assumptions to confirm

- Approval actors: tenant admins assign approvers and approval routing; default screens assume tenant-admin or assigned accountant performs approvals.
- Mapping UX: tenant-admin mapping templates should be available at provisioning; if you prefer full auto-seed, confirm which templates to include by default.

I can add small UI placeholders and a `matcher` confirmation page scaffold if you want a minimal end-to-end demo of the `NeedsBankMatch` -> `JournalReady` flow.

## Ironclad Data Pipeline — Frontend responsibilities

Overview

The frontend is the mapping and confirmation surface for the Ironclad pipeline. It must ensure that users only submit normalized records and that human confirmation is captured for final bank matches.

Key UI flows

- Import workbench: upload, parse & preview, map (column → canonical field), normalize preview, and commit.
- Exception/Waiting queue view: surface `SALES_VERIFIED` items missing `SettlementID` and allow tenant staff to annotate or attach missing `SettlementID` when available.
- Settlement grouping UI: display `MatchGroup` details (members, Net Total, fees) before bank match suggestion.
- Bank match confirmation: show system-proposed matches and require human confirmation to mark `MATCHED` and unlock journal posting.

UX notes

- Mapping templates should be saved per tenant and re-usable for repeated ERP/Gateway formats.
- The final bank-match UI must record the confirmer (`ActorID`) and `ChangeNote` into `StateHistory` for audit.

If you want, I can scaffold a `matcher-confirmation` page under `src/app/matcher/` that calls `GET /api/admin/transactions/needs-bank-match` and `POST /api/admin/match-groups/{id}/confirm`.

## Ironclad Hierarchical Workflow in the UI

The frontend should expose the four-level workflow as clear, tenant-scoped screens and queue views.

| Level | UI Surface | User Action | Target Outcome |
| :--- | :--- | :--- | :--- |
| 1 | Staff input / operational import screen | Capture manual input and POS EOD preview | `INTERNAL_VERIFIED` |
| 2 | Import workbench mapping screen | Map POS EOD exports to ERP sales ledger fields | `SALES_VERIFIED` |
| 3 | Sales verification screen | Compare ERP sales against gateway records by `ReferenceNo` | `SALES_VERIFIED` or `EXCEPTION` |
| 4 | Matcher confirmation screen / bank queue | Confirm gateway payout totals against bank statement lines using `SettlementID` | `MATCHED` after human confirmation |

UI rules

- No raw source row should go directly to a matcher or journal screen; the import workbench must normalize first.
- The waiting queue should show gateway rows missing `SettlementID` and make it obvious they are blocked from bank matching.
- The bank-match confirmation screen should show the candidate `MatchGroup`, total amount, fees, and the confirmer audit trail.
- Existing pages can stay as they are, but this baseline is what the next development phase should implement and polish.

## Ironclad Master Matching Matrix (UI view)

The UI should present the 4 workflow stages as a timeline and the 6 events as the actual rule cards users can act on.

| Stage | Event | UI Surface | User Action | Target Outcome |
| :--- | :--- | :--- | :--- | :--- |
| 1 | Operational Match | Staff import / operational queue | Confirm staff vs POS EOD | `INTERNAL_VERIFIED` |
| 2 | Sync Audit | Import mapping review | Confirm POS EOD vs ERP sync | Accounting sync confirmed |
| 3 | Sales Match | Sales verification queue | Confirm ERP vs gateway by `ReferenceNo` | `SALES_VERIFIED` or `EXCEPTION` |
| 4 | Settlement Match | Bank-match confirmation screen | Confirm gateway payout total vs bank statement by `SettlementID` | `MATCHED` |
| 4 | Expense Match | Card cashout approval / bank match screen | Confirm approved card cashout against bank statement | Must match before posting |
| 4 | Collection Match | Physical card receipt review screen | Confirm physical card receipt settlement against bank statement | Must match before posting |

UI routing rules

- POS imports should send users to the Stage 1 review surface.
- ERP imports should surface Stage 2 and Stage 3 as a chained review.
- Gateway imports should first show Stage 3 sales checks, then Stage 4 settlement checks.
- Bank imports should open the Stage 4 confirmation queues for settlement, expense, and collection rules.

## Gross/Net/Fee Matching Logic (UI implications)

Data shown in the import workbench

- `GrossAmount`, `FeeAmount`, and `NetAmount` should appear as separate canonical fields for Payment Gateway imports.
- The preview/mapping screen should show these values independently so users can verify the gateway extract before commit.

Stage 3 UI rule

- Sales Verification should compare ERP `Amount` to gateway `GrossAmount` using `ReferenceNo`.
- If the values do not match exactly, the UI should surface the discrepancy as `EXCEPTION` rather than allowing a silent pass.

Stage 4 UI rule

- Settlement confirmation should show `SettlementID`, the grouped `NetAmount` sum, and the matching bank deposit line.
- Once a `MATCHED` state is confirmed, the UI should surface the fee adjustment as a generated journal item so accounting can review the expense entry.
