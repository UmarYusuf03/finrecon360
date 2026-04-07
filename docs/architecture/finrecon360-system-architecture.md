# FinRecon360 System Architecture

## Status

This document captures the current target architecture agreed for FinRecon360 based on project discussions.

It is intentionally a design baseline, not a repository audit. Where implementation differs from this document, treat the document as the intended direction unless an explicit later decision supersedes it.

## 1. Purpose

FinRecon360 is a financial reconciliation and close platform for multi-entity businesses. The platform ingests operational and financial data from multiple sources, normalizes it into a canonical internal structure, routes it through approval and reconciliation workflows, and produces controlled journal-ready outcomes and reporting.

At the current target design, the platform is built around:

- Angular frontend
- ASP.NET Core backend
- Hybrid multi-tenancy
- One shared control-plane database
- One separate tenant database per tenant
- Token-driven or magic-link based authentication today
- Future-ready SSO path
- Tenant-scoped RBAC
- ERP-agnostic import pipeline
- Human-confirmed reconciliation
- Audit-driven transaction state management
- Subscription-based tenant provisioning and enforcement

## 2. Core Design Goals

### Isolation

Each tenant has its own database so tenant financial operations remain isolated for security, backup and restore, performance boundaries, and enterprise trust.

### Platform Governance

The control-plane database centralizes:

- tenant onboarding
- approval and rejection
- subscription plan assignment
- enforcement and suspension
- global identity concerns

### ERP Variability

The import model assumes external finance and ERP exports are inconsistent. The solution is a canonical import layer with user-driven mapping, not hard-coded ERP-specific shapes.

### Financial Control

The reconciliation model is intentionally conservative. The system may suggest matches, but human confirmation is required before matches are accepted.

### Extensibility

The architecture separates platform management, tenant administration, operational finance workflows, and reporting so new modules can be added without collapsing those boundaries.

## 3. High-Level Architecture

### Frontend

The Angular application is responsible for:

- authentication screens
- role-aware navigation
- route protection for UX purposes
- tenant admin dashboards
- import and mapping flows
- reconciliation operations
- exception handling
- reporting views

The frontend is not the source of truth for authorization.

Recommended frontend route convention:

- system-admin screens under `/app/system/*`
- tenant-admin screens under `/app/admin/*` or `/app/tenant-admin/*`

### Backend API

The ASP.NET Core backend is responsible for:

- authentication and token validation
- authorization enforcement
- tenant resolution
- control-plane APIs
- tenant operational APIs
- import orchestration
- reconciliation workflows
- journal eligibility rules
- reporting jobs and snapshots

The backend is the enforcement boundary.

Recommended backend route convention:

- control-plane and system-admin routes under `/api/system/*`
- tenant-admin routes under `/api/admin/*` or `/api/tenant-admin/*`

### Control Plane

The shared control-plane database holds platform-wide concerns such as:

- tenant registry
- subscription plans
- subscription lifecycle
- onboarding state
- enforcement actions
- system-admin operations
- global identity records

### Tenant Data Plane

Each tenant database holds only tenant-scoped business operations, such as:

- tenant users
- tenant roles and permissions
- bank accounts
- imported records
- operational transactions
- approvals
- reconciliation data
- journal entries
- reporting snapshots

## 4. Tenancy Model

FinRecon360 uses hybrid multi-tenancy:

- shared control plane for SaaS-wide governance
- isolated tenant databases for operational financial data

This boundary is central to the system. Developers should explicitly classify new features as either control-plane or tenant-scoped before implementation.

## 5. Role Boundaries

### System Admin

System Admin controls the SaaS platform, not daily tenant finance operations.

Responsibilities:

- review tenant registrations
- approve or reject onboarding
- assign plans
- apply enforcement actions
- provision tenant environments
- manage control-plane configuration

### Tenant Admin

Tenant Admin controls a specific tenant's business environment.

Responsibilities:

- manage tenant users
- manage tenant RBAC
- configure bank accounts
- configure imports and mappings
- configure internal workflows
- oversee reconciliation operations

### Global or Public Identity Users

Global identity users are distinct from tenant operational users. They do not belong to tenant operations by default and should not be treated as interchangeable with tenant staff.

That distinction matters in data modeling, authorization, onboarding, and user-management UX.

## 6. Data Strategy

### Control-Plane Database

Typical control-plane entities:

- `Tenants`
- `SubscriptionPlans`
- `TenantSubscriptions`
- `EnforcementActions`
- global users or identities
- tenant registration requests
- payment and subscription metadata

Typical questions answered:

- Is this tenant approved?
- What plan is the tenant on?
- Has this tenant been suspended?
- What limits apply to this tenant?

### Tenant Database

Typical tenant entities:

- `TenantUsers`
- `Roles`
- `Permissions`
- `Components`
- `Actions`
- `UserRoles`
- `BankAccounts`
- `ImportedRecords`
- `CashTransactions`
- `BankStatementLines`
- `ReconciliationRuns`
- `MatchGroups`
- `Matches`
- `JournalEntries`
- `Approvals`
- `TransactionState`
- `TransactionStateHistory`
- reporting snapshot tables

Typical questions answered:

- What transactions need approval?
- What remains unmatched?
- Which bank rows are pending?
- Which card cashouts are approved but not matched?
- Which journals are eligible to post?

## 7. Authentication and Identity

### Current Direction

The current design assumes token-driven and magic-link based authentication handled by the backend. The typical path is:

1. User initiates login.
2. Backend validates identity state.
3. Backend generates and validates a secure action token or auth token.
4. User completes the login flow.
5. Backend issues session or JWT context.
6. Frontend uses that context for API calls.
7. Backend re-checks authorization on each protected request.

### Future Direction

The architecture should remain compatible with:

- SSO
- external identity providers
- IdentityServer6 or similar orchestration

### Rule

Frontend guards are only UX helpers. Real security lives in backend token validation, tenant resolution, permission resolution, and endpoint authorization.

## 8. Authorization Model

Authorization is tenant-scoped RBAC.

Conceptually:

- a tenant user has one or more roles
- a role maps to permissions
- permissions are modeled around components, actions, and allowed operations

The typical authorization path is:

1. Authenticate caller.
2. Resolve tenant.
3. Load tenant user representation.
4. Resolve tenant roles and permissions from the tenant database.
5. Authorize the requested operation.
6. Execute the operation.

Tenant RBAC belongs in the tenant database, not the control-plane database.

## 9. Tenant Onboarding Lifecycle

The onboarding workflow is cross-cutting and should remain explicit.

1. A business registers or expresses interest.
2. System Admin reviews the registration.
3. The platform approves or rejects the tenant.
4. A subscription plan is assigned.
5. The platform provisions tenant infrastructure.
6. The Tenant Admin completes operational setup.

Provisioning should create:

- control-plane tenant record
- tenant database
- base tenant schema
- seed roles and permissions
- initial tenant admin representation
- operational setup baseline

## 10. Subscription Enforcement

Subscription limits are architectural rules, not optional UI hints.

Examples:

- maximum operational users
- maximum bank accounts
- module availability

Enforcement must happen:

- in backend business logic
- in frontend UX where practical
- during tenant provisioning
- during subsequent updates

If a plan allows 15 users and 4 bank accounts, the platform must block user 16 and bank account 5.

## 11. Import Architecture and Canonical Model

### Problem

ERP and financial source data arrives in inconsistent formats, including:

- varying column names
- varying date formats
- split debit and credit columns or signed values
- inconsistent reference fields
- inconsistent bank account identifiers
- inconsistent descriptions

### Agreed Solution

The system uses a canonical internal schema with a mapping UI.

Typical import flow:

1. Upload source file.
2. Parse headers and preview rows.
3. Let the user map source columns to canonical fields.
4. Normalize values using transformation rules.
5. Validate rows.
6. Persist normalized records.
7. Use normalized data for reconciliation.

### Canonical Attributes

Canonical finance import models should consider fields such as:

- source record id
- transaction date
- posting date
- reference or document number
- description
- account code
- account name
- debit amount
- credit amount
- net amount
- currency
- bank account identifier
- cost center, branch, or department
- source type
- import batch id
- validation flags
- normalized status

The platform should preserve both:

- raw source payload or metadata
- normalized operational representation

That dual record is important for auditability and debugging.

## 12. Transaction Workflow Model

### Cash-In

Payment-gateway based cash-in is treated as a source-to-source reconciliation path, typically matching booking engine data against payment gateway data.

### Cashout

Cashout behavior depends on payment method and this rule is explicit:

- cash cashouts post to journal after approval
- card cashouts require approval and bank-statement matching before journal posting

That rule must exist in:

- domain logic
- workflow state transitions
- journal eligibility checks

### Cash Cashout Path

1. Employee records cashout.
2. Transaction enters pending state.
3. Approval occurs.
4. Journal posting becomes eligible.

### Card Cashout Path

1. Employee records cashout with card details.
2. Transaction enters pending state.
3. Approval occurs.
4. Transaction must be matched to bank statement.
5. Journal posting becomes eligible only after successful match.

## 13. Transaction State and Audit Trail

State is not just a string column. It is an auditable workflow model.

Core entities:

- `TransactionState`
- `TransactionStateHistory`

On creation:

- create the transaction
- create current state as `PENDING`
- write initial history entry

On approval or rejection:

- update current state to `APPROVED` or `REJECTED`
- append a history record
- record actor, timestamp, and note

This model supports:

- full audit trail
- controlled transitions
- cleaner reporting
- better debugging

## 14. Reconciliation Model

The agreed model removes confidence-score based auto-trust.

Rules:

- the system may generate candidate matches
- users must confirm matches
- unresolved items remain unmatched or become exceptions

Match types should remain simple:

- `AUTO_CONFIRMED`: system proposed and user confirmed
- `MANUAL`: user matched directly

This keeps reconciliation explainable to users and auditors.

## 15. Matching and Exception Flow

Typical flow:

1. Import normalized operational data.
2. Import bank or payment source data.
3. Generate candidate matches.
4. Present candidates to users.
5. Users confirm or reject.
6. Unresolved items remain as exceptions or unmatched items.
7. Confirmed outcomes may unlock journal posting or reconciliation closure.

For card cashouts, this matching step is mandatory before journal posting.

## 16. Journal Posting

Journal posting is a downstream effect of a validated workflow, not a simple CRUD write.

Examples:

- cash cashout: approval is sufficient
- card cashout: approval alone is insufficient; bank match is required

Developers should treat journal eligibility as a business rule evaluated at the end of a workflow path.

## 17. Reporting and Analytics

The reporting approach uses precomputed snapshots and fact-like reporting tables in each tenant database.

The expected model is:

- background jobs compute aggregates
- snapshot tables store derived KPIs
- frontend reads reporting-focused structures

Benefits:

- faster dashboards
- simpler report queries
- lower load on transactional tables
- clearer tenant isolation

Typical outputs:

- reconciliation status summaries
- unmatched item counts
- approval backlog
- journal posting summaries
- bank account reconciliation progress
- period-based trend KPIs

## 18. Module Breakdown

### Module 1: Platform / Control Plane

- tenant registration
- subscription plans
- tenant approval
- enforcement
- subscription governance

### Module 2: Identity / Authentication

- login flow
- token handling
- magic-link or action-token flows
- session or JWT issuance
- future SSO integration

### Module 3: Tenant Administration

- tenant operational users
- roles and permissions
- components and actions
- bank account setup
- tenant feature visibility

### Module 4: Import and Canonicalization

- file upload
- parsing
- mapping UI
- normalization
- validation
- import batch tracking

### Module 5: Transactions and Approvals

- cash-in and cashout records
- approval workflow
- transaction states
- audit history

### Module 6: Reconciliation and Matching

- source comparison
- candidate matches
- human confirmation
- exceptions
- match groups

### Module 7: Journaling

- journal eligibility rules
- posting workflow
- accounting output structures

### Module 8: Reporting and Analytics

- snapshot jobs
- KPI aggregates
- dashboards
- close-status reporting

## 19. Backend Architectural Style

A layered backend structure remains the recommended direction.

### Domain

Contains business rules and entities such as:

- transaction rules
- matching rules
- approval rules
- journal eligibility rules

### Application

Contains use cases and services such as:

- approve transaction
- import ERP batch
- normalize import
- generate candidate matches
- confirm match
- create tenant
- enforce subscription limits

### Infrastructure

Contains technical implementations such as:

- EF Core repositories
- email integrations
- token storage
- file storage
- background job execution
- logging
- database contexts

### API

Contains:

- controllers
- DTOs
- middleware
- endpoint authorization

This structure helps keep business rules out of controllers.

## 20. Frontend Architectural Guidance

New frontend developers should think in these zones:

### Auth Zone

- login
- token and session handling
- magic-link flows
- future SSO entry points

### Shell and Navigation Zone

- tenant-aware layout
- permission-based navigation
- route guards for UX filtering

### Admin Zone

- user management
- roles and permissions
- bank accounts
- mapping templates

### Operations Zone

- imports
- reconciliation screens
- approvals
- exceptions
- journal-ready items

### Reporting Zone

- dashboards
- trends
- reconciled versus unreconciled views

## 21. Deployment Architecture

### Runtime Components

Recommended production-facing components:

1. Angular frontend deployed as static assets behind a web server or CDN.
2. ASP.NET Core API deployed as a containerized service.
3. Dedicated control-plane relational database.
4. Tenant databases on managed relational infrastructure.
5. Separate background worker or jobs service.
6. Object storage for imports and exports.
7. Email delivery integration, currently Brevo.
8. Monitoring and structured logging.

### Practical Topology

#### Edge Layer

- DNS
- TLS termination
- CDN or reverse proxy

#### App Layer

- Angular frontend
- ASP.NET Core API
- worker service

#### Data Layer

- control-plane database
- tenant database host or cluster
- object storage

#### Integration Layer

- Brevo
- payment gateway APIs
- booking engine APIs
- ERP import files
- bank statement import channels

## 22. Environment Strategy

Recommended environments:

- local
- dev
- staging or UAT
- production

### Local

Developers run:

- Angular frontend
- backend API
- local database or test container
- optional local worker

### Dev

Shared integration environment for daily development.

### Staging or UAT

Should closely mirror production for:

- onboarding tests
- migration tests
- subscription enforcement tests
- import mapping tests
- reconciliation dry runs

### Production

Hosts live tenant workloads.

## 23. CI/CD Expectations

### Frontend Pipeline

- install dependencies
- lint
- test
- build
- publish artifact
- deploy to static hosting target

### Backend Pipeline

- restore
- build
- test
- static analysis when available
- build container image
- push image
- deploy

### Database Pipeline

- generate and review EF migrations
- apply migrations in controlled stages
- run smoke checks

### Worker Pipeline

- build and deploy independently if the worker is a separate service

## 24. Migration and Provisioning Strategy

### Control-Plane Database

A standard controlled migration process is acceptable.

### Tenant Databases

Tenant database migration needs dedicated orchestration for:

- database creation at tenant approval time
- base schema application
- rollout of future schema versions
- retries and partial failure handling

Recommended pattern:

- keep migration history per tenant database
- run a tenant database migrator job or process
- version tenant schemas explicitly
- do not rely only on API startup for broad tenant migration

### Provisioning Workflow

When a tenant is approved:

1. Create the tenant record in control plane.
2. Assign plan and limits.
3. Create tenant database.
4. Apply base schema.
5. Seed roles, permissions, and configuration.
6. Create or link initial tenant admin.
7. Mark tenant as provisioned.
8. Notify onboarding success.

Provisioning must be idempotent and traceable.

## 25. Secrets and Configuration

Production secrets should not live only in `appsettings`.

Use secure configuration for:

- JWT signing keys
- database connection strings
- Brevo API keys
- payment provider secrets
- object storage credentials
- future SSO credentials

Examples:

- Azure Key Vault
- AWS Secrets Manager
- HashiCorp Vault
- Doppler

## 26. Security Rules

Immediate must-haves:

- backend-enforced authorization
- tenant-aware service and data access
- signed and secure auth tokens
- short-lived or one-time action tokens
- audit trail on approvals and state changes
- import validation before persistence into operational flows
- secure file handling
- rate limiting for public auth and onboarding flows

### Tenant Isolation Rule

Every tenant-scoped query and command must be tenant-aware. Missing tenant scoping should be treated as a serious bug category.

## 27. Observability

Production operation should include:

- structured logs with tenant id, user id, and correlation id
- failed import diagnostics
- reconciliation run audit logs
- background job status visibility
- API performance metrics
- alerting for failed provisioning or failed migrations

Without this, hybrid multi-tenant finance operations become difficult to support safely.

## 28. Developer Onboarding Mental Model

New developers should learn the system in this order:

1. Understand the boundary between control plane and tenant operations.
2. Understand the distinction between global identities and tenant operational users.
3. Understand the business lifecycle:
   - tenant approval
   - provisioning
   - tenant setup
   - import
   - approval
   - reconciliation
   - journal posting
   - reporting
4. Understand the hard business rules:
   - card cashout requires bank match before journal posting
   - cash cashout can post after approval
   - reconciliation requires human confirmation
   - transaction state changes must be auditable

## 29. Architectural Pressure Points

Areas to watch:

- tenant database migration complexity
- mapping UX complexity in canonical imports
- RBAC sprawl if permissions are over-modeled too early
- background job orchestration pressure
- match explainability for end users and auditors

## 30. Recommended Delivery Phases

### Phase 1: Practical MVP

- Angular static frontend
- one ASP.NET Core API service
- one worker service
- managed control-plane database
- managed tenant database host
- object storage
- Brevo integration
- partially automated tenant provisioning

### Phase 2: Production Hardening

- full CI/CD
- tenant migration orchestration
- improved monitoring
- stronger import observability
- better retryable jobs
- stronger staging parity

### Phase 3: Scale and Enterprise Hardening

- container orchestration
- independent service scaling
- secret rotation hardening
- tenant-aware operations dashboards
- stronger reporting infrastructure

## 31. One-Sentence Summary

FinRecon360 is a hybrid multi-tenant reconciliation platform where a shared control plane governs tenants and subscriptions, each tenant operates on isolated data in its own database, imported finance data is normalized into a canonical model, workflow state is audit-tracked, reconciliation is human-confirmed, and journal posting is gated by explicit business rules including mandatory bank matching for card cashouts.
