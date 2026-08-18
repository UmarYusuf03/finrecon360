# Reporting & Analytics — Implementation Plan

Status: **implemented, Phases 0–5 complete (2026-08-18)**. Originally written to be
picked up cold in a new chat/session; that context-free framing is now historical —
this doc is kept as the design record for the reporting layer. See "Phase completion
log" at the end of this file for what was actually built, verification results, and
the handful of deliberate deviations from the plan below (each with reasoning).

## 1. Where this fits

`docs/architecture/finrecon360-system-architecture.md` already specifies a reporting
layer as **Section 17 "Reporting and Analytics"** and **Module 8** in the module
breakdown: precomputed snapshot tables per tenant DB, background jobs computing
aggregates, and a frontend that reads reporting-focused structures instead of hitting
transactional tables directly. This plan is the concrete build-out of that section —
read it first if anything here is ambiguous, it takes precedence on intent.

## 2. Current state (audited, as of 2026-08-17)

What exists today:

- **Dashboard** (`Controllers/Admin/DashboardController.cs`, frontend
  `main/pages/dashboard/dashboard.ts`) — one endpoint, live `COUNT()` queries only. No
  history, no trends, no charts. Tiles: transaction counts by state, match group
  counts, event/exception counts, journal entry and bank account totals.
- **Audit log viewer** (`AdminAuditLogsController`, `AdminTenantAuditLogsController`) —
  filterable (action, entity, user, date range, free-text search) and paginated. The
  closest thing to a real report that exists, but no export and no aggregation.
- **Matcher summary** (`MatchGroupsController` summary endpoint) — same shape as the
  dashboard: live pending/exception/unmatched counts, not a report over time.
- **Cash flow forecast** (`Controllers/Admin/CashFlowForecastController.cs`,
  `Services/CashFlow/CashFlowForecastService.cs`, frontend
  `main/pages/admin/admin-cash-flow-forecast.*`) — built the session before this plan
  was written. The one genuinely forward-looking, report-like feature shipped so far.
  Uses an EWMA over confirmed `JournalReady` transactions plus settlement-lag-adjusted
  pending amounts. Treat this as the reference implementation for "what a report page
  in this codebase looks like" — same visual language, same permission-seeding
  pattern, same controller/service split.

What does **not** exist at all:

- No dedicated reports module/hub (no `ReportsController`, no `/reports` route).
- **Zero export capability anywhere** — no CSV, Excel, or PDF generation on any
  screen. `ClosedXML` is already a backend dependency but is only used to *parse*
  uploaded import files (`Services/ImportFileParser.cs`), never to write output.
- No financial statements. The ledger primitives exist (`JournalEntry`,
  `JournalVoucher`, `ChartOfAccount` with `AccountType` enum:
  Asset/Liability/Equity/Revenue/Expense) and a posting worker
  (`JournalPostingExecutorWorker`), but nothing aggregates them into a Trial Balance,
  P&L, Balance Sheet, or even a browsable General Ledger. The only current view into
  journal data is the raw JournalReady queue.
- No reconciliation trend reporting — no match-rate-over-time, exception-aging, or
  settlement-timing report. Only current-moment counts.
- No snapshot tables, no aggregation background jobs — Module 8 as documented is
  entirely unbuilt.

## 3. Conventions to follow (established this session, keep consistent)

- **Tenant-scoped permissions are seeded via `Services/TenantSchemaMigrator.cs`**, not
  EF migrations — tenant DBs use hand-rolled idempotent SQL migrations
  (`IF COL_LENGTH(...) IS NULL` / `IF NOT EXISTS(...)` guards), each with a
  `yyyyMMddHHmm_Name`-style migration ID constant, added to `ApplyAsync` in order. New
  permission codes must (a) get a `Permissions` insert, (b) get granted to the
  tenant's `ADMIN` role, and (c) be added to `AliasMap`/`ControlPlanePermissions` in
  `Authorization/PermissionHandler.cs` **only if** the permission is genuinely
  control-plane (system-admin) scoped — reporting permissions here are tenant-scoped,
  so they do **not** go in `ControlPlanePermissions`.
- **Control-plane schema changes** (anything on `AppDbContext` — new snapshot tables
  if they end up control-plane rather than per-tenant, which they should not for this
  plan) use real EF Core migrations (`dotnet ef migrations add`). This plan's snapshot
  tables belong in the **tenant** database, so they go through
  `TenantSchemaMigrator`, not EF migrations.
- **Backend service pattern**: interface + implementation in
  `Services/<Area>/<Name>Service.cs`, registered in `Program.cs` via
  `builder.Services.AddScoped<IThing, Thing>()`. Controllers stay thin — resolve
  tenant DB via the repeated `AuthorizeTenantAdminAsync` pattern seen in every
  `Controllers/Admin/*Controller.cs`, delegate to the service.
- **Background workers**: `BackgroundServices/<Name>HostedService.cs`, registered via
  `builder.Services.AddHostedService<X>()` in `Program.cs`. Follow the
  `SubscriptionOverdueMonitorHostedService` shape — `IServiceScopeFactory`, a fixed
  `RunInterval`, try/catch around each cycle so one bad cycle doesn't kill the loop.
- **Frontend**: standalone components, service in `core/admin-rbac/` (tenant-scoped
  features) with `USE_MOCK_API` branches, page in `main/pages/admin/`, route added to
  `main/main.routes.ts` under the `admin` children array, nav entry added to
  `main/pages/admin/admin-shell.ts`'s `links` array with `scope: 'tenant'`.
- **i18n is mandatory**: every user-facing string needs a translation key added to
  **all three** locale files (`src/assets/i18n/en.json`, `ta.json`, `si.json`) — it's
  fine for `ta`/`si` to carry the English string as a placeholder (that's the existing
  pattern throughout this codebase), but the key must exist in all three or the UI
  shows raw translation keys for non-English locales.
- **Charts**: no charting library is installed and none should be added for anything
  this simple — `admin-cash-flow-forecast.ts` hand-rolls SVG line charts (compute
  points in TS, render a `<path>` via a `d` attribute string). Reuse that approach.
- **Verification bar for every phase**: `dotnet build` + `dotnet test` on the backend
  solution, `ng build --configuration development` + `ng test --watch=false
  --browsers=ChromeHeadless` on the frontend, before considering a phase done. The
  frontend suite has 8 pre-existing unrelated failures (Login/ShellComponent/
  AdminAuditLogs specs with DI/directive setup gaps predating this plan) — the bar is
  "no *new* failures," not "zero failures."

## 4. Phases

Each phase is independently shippable and builds on the last. Sizing is relative
(S/M/L), not a time estimate.

### Phase 0 — Export foundation (S) — ✅ Done

Nothing downstream should build its own export logic; this is the one piece every
later phase reuses.

- Backend: `Services/Export/IReportExporter.cs` with `ToCsv<T>(IEnumerable<T>, ...)`
  and `ToXlsx<T>(IEnumerable<T>, sheetName, ...)`, implemented with `ClosedXML`
  (already a dependency, currently write-unused) for XLSX and a simple manual writer
  for CSV (no need for a new dependency — it's a solved problem: quote fields
  containing commas/quotes/newlines, CRLF line endings). Return `byte[]` +
  content-type, let controllers wrap in `File(...)`.
  Hold off on PDF in this phase — no PDF library is installed, and nothing identified
  in this plan strictly requires PDF over XLSX/CSV. Revisit only if a specific report
  (e.g. an investor-facing statement) needs print-quality formatting.
- Frontend: a small shared `core/services/export.service.ts` — takes a `Blob` +
  filename from an HTTP response (`responseType: 'blob'`) and triggers a browser
  download. A reusable `<app-export-button>` or just a documented pattern (button +
  `(click)` handler) other pages copy.
- Acceptance: one real screen wired end-to-end as the proof — recommend the audit log
  viewer (`AdminTenantAuditLogsController`), since it already has the filter
  parameters that should also scope the export.

### Phase 1 — Export existing screens (S) — ✅ Done

Pure leverage of Phase 0, no new data model. Add "Export CSV" / "Export XLSX" to:

- Transactions list (`TransactionsController` / `admin-transactions.ts`) — respect the
  current search/state filters already in the component.
- Bank Accounts list.
- Audit logs (both tenant and system-admin variants) — if not already done as the
  Phase 0 proof screen.
- Match groups list (`MatchGroupsController`).

Each is a new `GET .../export?format=csv|xlsx` endpoint on the existing controller
(reuse existing query/filter logic, skip pagination, cap at a sane row limit e.g.
10,000 with a clear error if exceeded) plus one button in the existing template.

### Phase 2 — Financial statements (L) — ✅ Done

The highest-value gap: real accounting output from data that already exists.

- `Services/Reporting/GeneralLedgerService.cs` — per-`ChartOfAccountId`, list of
  `JournalEntry` rows in a date range with a running balance column. This is the
  simplest report and the foundation the others are computed from.
- `Services/Reporting/TrialBalanceService.cs` — for a given as-of date, sum debits and
  credits per account (need to confirm/establish a debit/credit sign convention on
  `JournalEntry.Amount` — check `JournalPostingExecutorWorker` for the convention
  already in use before inventing a new one), assert the totals balance to zero as a
  built-in data-integrity check surfaced in the report itself.
- `Services/Reporting/IncomeStatementService.cs` (P&L) and
  `Services/Reporting/BalanceSheetService.cs` — group Trial Balance output by
  `ChartOfAccount.AccountType` (Revenue/Expense for P&L; Asset/Liability/Equity for
  Balance Sheet), for a date range (P&L) or as-of date (Balance Sheet).
- One controller, `Controllers/Admin/FinancialReportsController.cs`, four endpoints
  (`/general-ledger`, `/trial-balance`, `/income-statement`, `/balance-sheet`), one
  new tenant permission `ADMIN.FINANCIAL_REPORTS.VIEW` seeded via
  `TenantSchemaMigrator`.
- Frontend: `main/pages/admin/admin-financial-reports.*` — a report picker (four
  tabs/routes under one shell, mirroring the `MatcherShellComponent` pattern for a
  shared nav bar across sibling report pages) with a date-range control and the
  Phase 0 export button on each.
- **Prerequisite check before starting**: confirm `ChartOfAccount` seed data actually
  covers all entry types currently posted (`Data/DbSeeder.cs` /
  `BuildTenantChartOfAccountsCashInSeed` in `TenantSchemaMigrator.cs`) — if postings
  exist with `ChartOfAccountId == null` (the model allows it — see the nullable
  comment in `Models/JournalEntry.cs`), the Trial Balance won't balance and that gap
  needs closing first, either by backfilling a mapping or explicitly reporting an
  "unclassified" bucket rather than silently excluding those entries.

### Phase 3 — Reconciliation trend reporting (M) — ✅ Done

Needs history that doesn't exist yet — match groups and events are current-state
only, so this phase introduces the first real snapshot table (a scoped-down preview
of Phase 4, and a good place to validate the snapshot approach before generalizing
it).

- New tenant table `ReconciliationDailySnapshot` (via `TenantSchemaMigrator`, plain
  `CREATE TABLE IF NOT EXISTS` guard like the rest of that file): date, per-level
  match counts, exception counts, average time-to-match, unmatched-item count.
- `BackgroundServices/ReconciliationSnapshotHostedService.cs` — runs once daily
  (stagger it away from `ReconciliationCycleHostedService`/
  `JournalPostingHostedService`'s existing intervals), computes yesterday's rollup
  from `ReconciliationMatchGroup`/`ReconciliationEvent`, upserts one row per tenant
  per day.
- `Controllers/Admin/ReconciliationReportsController.cs` — date-range query over the
  snapshot table (cheap — it's already aggregated, no need to touch transactional
  tables at read time, which is the entire point per Section 17).
- Frontend: trend charts using the same hand-rolled-SVG approach as the cash flow
  forecast — match rate over time, exception aging, unmatched backlog trend.

### Phase 4 — Generalize into Module 8 (L) — ✅ Done (with deviations, see completion log)

Once Phase 3 proves the snapshot pattern works, generalize it into what Section 17
actually describes: a tenant-wide KPI fact table, not just a reconciliation-specific
one.

- Broaden `ReconciliationDailySnapshot` into a general `TenantDailySnapshot` (or keep
  them as separate tables per domain if that ends up cleaner once Phase 3 is real —
  decide based on how Phase 3 actually shakes out, don't over-design this now)
  covering the full "typical outputs" list from Section 17: reconciliation status
  summaries, unmatched item counts, approval backlog (pending transaction count/age),
  journal posting summaries, bank account reconciliation progress, period-based trend
  KPIs.
- A single daily aggregation hosted service (or extend Phase 3's) populating all of
  it in one pass per tenant.
- A **Reports Hub** — `/app/admin/reports` — the first real "dedicated reports
  module" landing page, linking out to Financial Reports (Phase 2), Reconciliation
  Trends (Phase 3/4), Cash Flow Forecast (already shipped), and the Dashboard,
  presented as one coherent reporting section instead of scattered pages. This is
  also the point where the existing live-count `DashboardController` should start
  reading from the snapshot table instead of running `COUNT()` queries live, per the
  stated Section 17 benefit ("lower load on transactional tables") — a
  backward-compatible internal swap, not a breaking change to the dashboard API.

### Phase 5 — Scheduled/emailed reports (stretch, M) — ✅ Done (without plan-gating, see completion log)

Not blocking, but a natural extension once Phase 2–4 exist, and ties into the
subscription-revenue ideas discussed earlier this project (a plan-gated premium
feature is a legitimate way to differentiate Growth/Enterprise tiers).

- A tenant-configurable schedule (e.g. "email me the reconciliation summary every
  Monday") stored per tenant, a hosted service checking due schedules, rendering the
  relevant report via Phase 0's exporter, and sending it through the already-wired
  `IEmailSender`/`BrevoEmailSender`.
- Explicitly out of scope until 2–4 exist — there's nothing to schedule yet.

## 5. Suggested order of attack

Phase 0 → 1 → 2 → 3 → 4 → 5, in that order, with 2 and 3 swappable if Financial
Statements turns out to need the Chart of Accounts prerequisite check resolved first
(that could push it behind Phase 3). Do not start Phase 4 before Phase 3 has shipped
and been used for at least one real reporting period — generalizing a pattern that's
only been designed on paper, not exercised, tends to guess wrong about the shape.

## 6. Phase completion log (2026-08-18)

All six phases were built in one continuous run rather than across separate real
reporting periods — the user explicitly asked to proceed straight through Phase 4
despite the "wait for a real period" note above. Recorded here so a future reader
knows that caution was consciously overridden, not missed.

**Verification, every phase**: `dotnet build` clean, `dotnet test` full suite green
(128 → 167 tests over the course of the six phases), `ng build --configuration
development` clean, `ng test --watch=false --browsers=ChromeHeadless` — 66
passing / 8 failing throughout, the same 8 pre-existing unrelated failures called out
in Section 3 above (never 9+, confirmed via `git stash` diff before/after each phase).

### What shipped, file-by-file, is in the Controllers/Services/Dtos/BackgroundServices
listed inline throughout Sections 4.0–4.5 above — this section covers only where the
implementation diverged from what's written there, and why.

**Phase 0/1** — built exactly as specified. `Services/Export/ReportExporter.cs`,
`core/services/export.service.ts`, export endpoints + buttons on Transactions, Bank
Accounts, both Audit Log variants, and Match Groups (pending + unmatched queues).

**Phase 2** — built as specified, with one correctness finding along the way: the
Chart-of-Accounts prerequisite check (Section 4.2) turned up real unclassified
activity — `ReconciliationController`'s two manual posting endpoints
(`PostJournalFromTransaction`, `PostJournalFromMatchGroup`) never set
`ChartOfAccountId` at all. Rather than touch live posting code in a reporting-only
phase, every report (General Ledger, Trial Balance, Income Statement, Balance Sheet)
surfaces those entries as a distinct "Unclassified" line/bucket instead of silently
dropping them — the plan's own explicitly-sanctioned fallback. Balance Sheet
deliberately does not assert Assets = Liabilities + Equity (no retained-earnings
roll-up exists in this ledger yet; asserting it would be dishonest, not just
incomplete). A real bug was caught by the compiler here, not by review:
`Dictionary<Guid?, T>` throws `ArgumentNullException` on a null key at runtime —
exactly the Unclassified case — fixed in `GeneralLedgerService` with a regression
test.

**Phase 3** — built as specified. `ReconciliationDailySnapshot` (one row per
`SnapshotDate` × `MatchLevel`), `ReconciliationSnapshotWorker`,
`ReconciliationSnapshotHostedService`, `ReconciliationReportsController`, and a
`matcher-trends` page added as a sibling tab to the Matcher shell's existing
Material-styled pages (Events/Waiting Queue/Sales Verification) rather than the older
Tailwind-styled drill-down pages (`matcher-queue`/`matcher-unmatched`) — the new page
is a nav-level sibling to the former, not a drill-down like the latter.

**Phase 4** — one design decision and one deliberate deviation:

- *Design decision the plan left open*: kept `TenantDailySnapshot` as a **separate**
  table from `ReconciliationDailySnapshot` rather than broadening the latter, per the
  plan's own "decide based on how Phase 3 actually shakes out" allowance. None of
  Module 8's remaining outputs (approval backlog, journal posting summary, bank
  reconciliation progress) are naturally per-`MatchLevel`, so forcing them into that
  shape would mean repeating the same tenant-wide number on every level row.
- *Deviation*: the plan says `DashboardController` "should start reading from the
  snapshot table instead of running `COUNT()` queries live." This was **not** done for
  the existing `/summary` endpoint. Most of its fields (pending approvals,
  needs-bank-match, journal-ready) are current-moment queue sizes an operator needs
  accurate to the second — a once-daily snapshot would show yesterday's queue as
  today's, which actively misleads rather than merely going stale. And for the
  cumulative counts, summing N days of snapshot deltas is asymptotically *slower* than
  the existing indexed `COUNT(*)` as a tenant's history grows — the literal swap would
  have been a performance regression dressed up as compliance with this doc. Instead,
  a new, additive `GetTrend` endpoint reads the snapshot tables for historical
  context, and `/summary` is untouched.
- Reports Hub shipped as specified: `/app/admin/reports`, linking to Financial
  Reports, Reconciliation Trends, Cash Flow Forecast, Dashboard, and (once it existed)
  Report Schedules.

**Phase 5** — built as specified, with one necessary extension and one scope cut:

- *Necessary extension*: `IEmailSender` was template-only (`SendTemplateAsync`, a
  fixed Brevo template ID + params, no attachment field) — there was no way to
  "send it through the already-wired `IEmailSender`" as written without adding
  attachment support. Added `SendWithAttachmentAsync` as a new interface method
  (existing call sites — magic links, onboarding, password reset — untouched) and
  implemented it in `BrevoEmailSender` and the test double `FakeEmailSender`.
- *Scope cut*: no plan-gating (Growth/Enterprise tier restriction on scheduled
  reports). The plan mentions this as motivating context ("ties into the
  subscription-revenue ideas discussed earlier"), not a hard requirement, and it's a
  meaningfully separate piece of work — a new `Plan` column via a real control-plane
  EF migration, an admin plan-editor UI diff, and enforcement wiring — that deserves
  its own explicit decision rather than being bundled silently into an already-large
  combined delivery. The scheduling feature itself is fully built and available to
  every tenant today.
- Weekly-only cadence (`DayOfWeek` + a fixed 06:00 UTC delivery hour), no PUT-update
  on an existing schedule's report type/format/day/recipient (delete and recreate
  instead) — both explicit scope simplifications for a "stretch" phase, not oversights.
