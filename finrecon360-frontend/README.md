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

Reporting and analytics surfaces are now implemented: export buttons (CSV/XLSX) on Transactions, Bank Accounts, Audit Logs, and Match Groups; a Financial Reports section (General Ledger, Cash Flow, Income Statement, Balance Sheet, plus Trial Balance as a secondary accounting-export view); a Reconciliation Trends page under Matcher; a Reports Hub landing page (`/app/admin/reports`); and a Report Schedules page for weekly emailed reports. See `../docs/architecture/reporting-implementation-plan.md`.

Not yet implemented as full production workflows:

- bank statement import workflow
- transaction approvals workflow
- exception management workflow
- journal posting workflow

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
- Google single sign-on, on both the login and sign-up screens
- user registration
- email verification via magic link
- password reset via magic link
- change-password confirmation flow
- tenant onboarding magic link verification

So the current UX is not magic-link-only authentication.

### Google Sign-In

`GoogleSsoService` loads Google Identity Services and renders Google's own button. The browser
never inspects the returned token — anything it concluded would be unverifiable, and the
backend re-derives every claim from the signature. The button is only shown when
`GET /api/auth/sso/config` reports SSO as configured, so a deployment without
`GOOGLE_CLIENT_ID` hides it rather than offering a control that always fails.

Password and Google sign-in share one post-authentication routine, so the follow-up `/api/me`
call that resolves roles, permissions, and tenant cannot drift between them. Signing up with
Google and signing in with Google are the same server operation: the account is provisioned on
first arrival.

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
5. PayHere checkout
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

Current frontend has working canonical import and mapping-template UX. Reporting is implemented (see the "Current Frontend Scope" section above); reconciliation, approvals, journal posting, and bank-statement matching status should be checked against `WORKER-INTEGRATION.md` and the Matcher UI directly rather than assumed incomplete from this note.

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
