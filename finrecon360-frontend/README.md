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
