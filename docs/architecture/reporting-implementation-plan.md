# Reporting & Analytics — Implementation Plan

Status: planned, not started. Written to be picked up cold in a new chat/session — it
does not assume any prior conversation context.

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

### Phase 0 — Export foundation (S)

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

### Phase 1 — Export existing screens (S)

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

### Phase 2 — Financial statements (L)

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

### Phase 3 — Reconciliation trend reporting (M)

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

### Phase 4 — Generalize into Module 8 (L)

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

### Phase 5 — Scheduled/emailed reports (stretch, M)

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
