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
  Uses two separate EWMAs over confirmed `JournalReady` transactions (split by instant vs
  pending flows). For near-term projections, it strictly relies on known pending amounts
  instead of the trend to avoid double-counting. Treat this as the reference implementation
  for "what a report page in this codebase looks like" — same visual language, same
  permission-seeding pattern, same controller/service split.

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

## 7. Post-launch fix — Balance Sheet retained-earnings roll-up (2026-08-25)

Section 6's Phase 2 log entry above recorded, as a conscious decision, that "Balance
Sheet deliberately does not assert Assets = Liabilities + Equity (no
retained-earnings roll-up exists in this ledger yet; asserting it would be dishonest,
not just incomplete)." That gap is closed as of this fix — the identity now holds.

**Why it has to hold**: a trial balance across *all* accounts (Asset, Liability,
Equity, Revenue, Expense) balances to zero by construction — every `JournalVoucher`
is required to sum to zero before it's allowed to post, so total debits equal total
credits across the whole chart of accounts, always. A Balance Sheet is not that full
trial balance; it's a *subset* — Asset/Liability/Equity only, with Revenue/Expense
reported separately on the Income Statement instead. `BalanceSheetService` was
building that subset by dropping every `JournalEntry` whose account resolved to
Revenue or Expense (`accountsById` was pre-filtered to only the three balance-sheet
types, so any Revenue/Expense entry silently missed the dictionary lookup and hit a
`continue`). Removing entries from one side of an equation that balanced before you
removed them is exactly how you stop it balancing — the shortfall was always
precisely equal to net income/loss for the period, because that's what Revenue minus
Expense *is*.

**The standard accounting fix — closing entries**: a real general ledger handles this
by *closing the books* at period end: Revenue and Expense are temporary ("nominal")
accounts that get zeroed out via closing journal entries, with their net effect
(Net Income = Revenue − Expenses) swept into a permanent equity account called
Retained Earnings. That's not an approximation of the identity — it *is* the
identity; Retained Earnings exists specifically so Assets = Liabilities + Equity
keeps holding once Revenue/Expense drop off the Balance Sheet.

**What changed**: this ledger has no separate closing-entry posting step (Revenue and
Expense accounts are never actually zeroed out in the data), so `BalanceSheetService`
now computes the equivalent roll-up dynamically at read time — sum every Revenue and
Expense account's net activity from inception through `asOfUtc`, and surface it as
one synthetic `RETAINED-EARNINGS` line in the Equity section (new constant in
`Dtos/Reporting/FinancialReportDtos.cs`, alongside the existing `UnclassifiedAccount`
synthetic-line pattern from Phase 2). Same-pass fix: a `JournalEntry` whose
`ChartOfAccountId` no longer resolves to a real row (e.g. a deleted account) now
falls into the existing `Unclassified` bucket instead of being silently dropped —
the account-type filter that caused the Revenue/Expense bug was the same filter
masking this case.

**Verified** against the QA tenant (`qa-test-02@example.com`): before the fix,
Assets 364,476 vs. Liabilities+Equity 372,526 (short by exactly 8,050, the tenant's
net loss for the period per the Income Statement). After: Equity now includes
`RETAINED-EARNINGS: -8,050`, and Assets (364,476) = Liabilities (372,526) + Equity
(-8,050) exactly. `dotnet test` unaffected (180/180 passing).

## 8. Phase 6 — Cash Flow Report (actual) & Trial Balance demotion (✅ Done, 2026-08-25)

**Status: implemented.** Written so a fresh session with no prior
chat history can execute this without re-deriving the reasoning below.

### Why

This tenant's product is a cash/settlement reconciliation system — POS terminals,
bank deposits, card-gateway settlements, cash agents — not a company running debt,
equity, inventory, or payroll through a full general ledger. Trial Balance's premise
(does the whole multi-account chart sum to zero) is real and correct today (see
Section 6/7 above), but it isn't something an operator running this kind of business
actually checks day to day. What they'd actually look at is **cash flow**: how much
money moved into and out of the bank account, by day and by channel, and what the
running cash position is. That's the report this phase adds, and it demotes Trial
Balance to a secondary/export-only view rather than a headline report.

This does **not** touch journal posting. `JournalEntry`/`JournalVoucher` stay exactly
as they are — they remain the audit-trail source of truth every report (including
this new one) reads from. See `WORKER-INTEGRATION.md` for the posting pipeline itself
if changes there ever become relevant.

Do not confuse this with `CashFlowForecastController`/`CashFlowForecastService`
(already shipped, lives at `/app/reports/cash-flow-forecast`) — that's a **forward-looking
projection** of upcoming settlements. This phase is a **historical/actual** statement
of cash already moved, and belongs alongside General Ledger/Income Statement/Balance
Sheet in the Financial Reports shell, not next to the forecast page.

### Scope

1. New backend report: actual Cash Flow, sourced from posted `JournalEntry` rows
   against Asset-type ("cash") accounts.
2. Demote Trial Balance in the frontend nav — keep the backend endpoint, service, and
   tests untouched (the data is correct and useful as an accountant-facing export).
3. Update every doc that currently describes the reporting feature set (checklist at
   the end of this section) — **do not consider this phase done until that checklist
   is complete.**

### Backend implementation

Mirror the existing `Services/Reporting/*` pattern exactly — same folder, same
constructor-injection style as `GeneralLedgerService`/`TrialBalanceService`.

- **`Dtos/Reporting/FinancialReportDtos.cs`** — add:
  ```csharp
  public record CashFlowDayDto(DateTime Date, decimal OpeningBalance, decimal CashIn, decimal CashOut, decimal ClosingBalance);

  public record CashFlowResponse(
      DateTime FromUtc, DateTime ToUtc, IReadOnlyList<CashFlowDayDto> Days,
      decimal TotalCashIn, decimal TotalCashOut, decimal NetChange, decimal UnclassifiedAmount);
  ```
- **`Services/Reporting/CashFlowReportService.cs`** (new — `ICashFlowReportService`/`CashFlowReportService`):
  - Source: `JournalEntry` rows whose `ChartOfAccountId` resolves to an
    `AccountType.Asset` account. Filter by `AccountType.Asset` the way
    `BalanceSheetService` does — don't hardcode `1000-BANK` — so a tenant with more
    than one cash/bank account still works correctly.
  - Opening balance for the range = sum of all matching entries with
    `PostedAt < fromUtc` (same opening-balance approach as
    `GeneralLedgerService.GetAsync` — reuse that logic rather than re-deriving it if
    it extracts cleanly; don't force a shared helper if the grouping keys end up
    different enough to make it awkward).
  - Group remaining entries by calendar day (`PostedAt.Date`) within
    `[fromUtc, toUtc]`. Per day: `CashIn` = sum of positive `Amount` values (Asset
    accounts are debit-normal, so a debit is cash arriving); `CashOut` = sum of the
    absolute value of negative `Amount` values. `ClosingBalance` = previous day's
    closing + that day's net; `OpeningBalance` = previous day's closing.
  - **Include days with zero activity** inside the range (`CashIn = 0, CashOut = 0`,
    balance carried forward unchanged) — a cash flow report with silent gaps in the
    day sequence reads as "we don't know," not "nothing happened."
  - Decide explicitly whether `Unclassified` (null `ChartOfAccountId`) entries count
    toward cash in/out. Recommendation: **include them** — a cash account is defined
    by what actually happened to cash, not by whether someone remembered to map an
    account code — and surface the total separately via `UnclassifiedAmount` on the
    response, following this file's own established Unclassified-bucket convention
    (Section 6, Phase 2) rather than silently dropping or silently including without
    disclosure.
- **`Controllers/Admin/FinancialReportsController.cs`** — add `GET /cash-flow`
  (`fromUtc`/`toUtc` query params, same default-range logic as `general-ledger`) and
  `GET /cash-flow/export` (same shape as the other `/export` endpoints via
  `IReportExporter`). Reuses the existing `ADMIN.FINANCIAL_REPORTS.VIEW` permission —
  no new permission needed.
- **`Services/Reporting/ScheduledReportRenderer.cs`** — add a `"CashFlow"` case to
  both `IsKnownReportType` and the `RenderAsync` switch (mirror the `"TrialBalance"`
  case: call the new service with `(weekAgo, now)`, export via `IReportExporter`) so
  it's schedulable like the other statements. Update the `<summary>` doc comment that
  lists recognized values.
- Register `ICashFlowReportService`/`CashFlowReportService` in DI — find the spot by
  grepping `AddScoped<ITrialBalanceService` in `Program.cs` and add the new service
  alongside it.
- **Tests**: add `CashFlowReportServiceTests.cs` (or extend an existing reporting
  test file) covering: opening balance carries in correctly across the range
  boundary, a zero-activity day still appears in the output, the Unclassified
  decision above is actually implemented as decided, and multi-day running-balance
  math is correct. Full suite must stay green — baseline is 180/180 (see
  `WORKER-INTEGRATION.md`); this phase should only add tests, never remove or weaken
  one.

### Frontend implementation

- **Models** (wherever `GeneralLedgerReport`/`TrialBalanceReport` interfaces live,
  likely `core/admin-rbac/models.ts`) — add `CashFlowDay`/`CashFlowReport`
  interfaces matching the new DTOs.
- **`core/admin-rbac/financial-reports.service.ts`** — add `getCashFlow(fromUtc,
  toUtc)` and `exportCashFlow(fromUtc, toUtc, format)`, mirroring
  `getGeneralLedger`/`exportGeneralLedger` (lines 20-28 today) exactly.
- **New page** `main/pages/admin/admin-cash-flow-report.{ts,html}` — start from a
  copy of `admin-general-ledger.ts`/`.html` (date-range picker, load/export,
  `admin-financial-reports.scss`), since Cash Flow is date-range-based like General
  Ledger and Income Statement, not as-of-date like Trial Balance/Balance Sheet.
- **`main.routes.ts`** (financial-reports children, currently ~lines 195-224): add a
  `cash-flow` route pointing at the new component; change the shell's default
  redirect from `redirectTo: 'general-ledger'` to `redirectTo: 'cash-flow'` — it's
  now the featured report, so it's what a tenant should land on first.
- **`admin-financial-reports-shell.html`** nav (lines 10-15 today) — reorder so Cash
  Flow leads and Trial Balance trails, visually demoted:
  ```html
  <a routerLink="cash-flow" routerLinkActive="active">{{ 'FINANCIAL_REPORTS.NAV.CASH_FLOW' | translate }}</a>
  <a routerLink="general-ledger" routerLinkActive="active">{{ 'FINANCIAL_REPORTS.NAV.GENERAL_LEDGER' | translate }}</a>
  <a routerLink="income-statement" routerLinkActive="active">{{ 'FINANCIAL_REPORTS.NAV.INCOME_STATEMENT' | translate }}</a>
  <a routerLink="balance-sheet" routerLinkActive="active">{{ 'FINANCIAL_REPORTS.NAV.BALANCE_SHEET' | translate }}</a>
  <a routerLink="trial-balance" routerLinkActive="active" class="reports-nav__secondary">{{ 'FINANCIAL_REPORTS.NAV.TRIAL_BALANCE' | translate }}</a>
  ```
  Add a `.reports-nav__secondary` style (visually muted/smaller) in
  `admin-financial-reports.scss`. **Do not delete** the Trial Balance route,
  component, or backend endpoint — it stays reachable, just no longer a peer to the
  other three.
- **i18n** — this app ships three locales: `src/assets/i18n/en.json`, `si.json`
  (Sinhala), `ta.json` (Tamil). Add `FINANCIAL_REPORTS.NAV.CASH_FLOW` and any new
  column/label keys to **all three** files, not just `en.json` — copy the existing
  `TRIAL_BALANCE` key as the pattern. Get an actual Sinhala/Tamil translation; if
  none is available, ask the user rather than leaving the English string in those
  files.
- **`admin-reports-hub.{ts,html}`** — if it links directly to `trial-balance` as the
  headline Financial Reports link, repoint it to `cash-flow`.

### Verification

Same method used earlier in this project to verify the Balance Sheet fix — don't
just rely on `dotnet test`:

- `cd finrecon360-backend-master && dotnet test` — must stay green, count ≥ 180.
- `cd finrecon360-frontend && ng build --configuration development` clean; `ng test
  --watch=false` — same 8 pre-existing unrelated failures noted throughout this file,
  never more.
- Live check against the QA tenant (`qa-test-02@example.com` / `TestPass!2026`,
  tenant "QA Test Co 2"): log in via `POST /api/auth/login`, call `GET
  /api/admin/financial-reports/cash-flow`, and confirm `TotalCashIn - TotalCashOut`
  for the full range matches the `1000-BANK` account's net (debit − credit) on the
  Trial Balance report for the same period — same underlying ledger data, just
  reshaped, so they must agree exactly.

### Post-implementation doc checklist — required, not optional

The user has explicitly asked that this not be skipped. Do not end the session until
every item below is done:

1. **This file** — flip this section's heading to `✅ Done` and append a completion
   log entry below it, in the style of Section 6: what shipped, any deviations from
   this plan and why, test counts before/after.
2. **`DEVREADME.md`** (repo root, "What is already implemented" bullet list) —
   currently: "...financial statements (General Ledger, Trial Balance, Income
   Statement, Balance Sheet)...". Update to name Cash Flow as a headline statement
   and Trial Balance as secondary, e.g.: "...financial statements (General Ledger,
   Cash Flow, Income Statement, Balance Sheet, plus Trial Balance as a secondary
   accounting-export view)...".
3. **`finrecon360-frontend/README.md`** (line ~29) — same list currently reads
   "(General Ledger, Trial Balance, Income Statement, Balance Sheet)"; update the
   same way as item 2.
4. **`finrecon360-backend-master/finrecon360-backend/README.md`** (line ~30) — same
   list, same update.
5. **`README.md`** (repo root, line ~28) — already generic ("financial statements,
   reconciliation trend charts...") and names no individual reports; confirmed no
   edit needed here as of this writing, but re-check it hasn't changed since.
6. Grep the repo for `"Trial Balance"` and `"trial-balance"` across `*.md` files one
   more time before finishing, to catch anything this list missed.

## 9. Phase 6 completion log (2026-08-25)

Built as specified, with two notes.

**Backend**: `Dtos/Reporting/FinancialReportDtos.cs` (`CashFlowDayDto`/`CashFlowResponse`),
`Services/Reporting/CashFlowReportService.cs`, two new endpoints on
`FinancialReportsController` (`GET cash-flow`, `GET cash-flow/export`), a `"CashFlow"`
case added to `ScheduledReportRenderer` (`IsKnownReportType` and `RenderAsync`, plus the
`ReportSchedule.ReportType` doc comment and the frontend schedule-type dropdown so it's
actually reachable end-to-end, not just accepted by the renderer), and DI registration in
`Program.cs`. Four new tests in `FinancialReportingServicesTests.cs` covering opening-balance
carry-across-the-boundary, zero-activity days, Unclassified entries counting toward daily
totals while being disclosed separately, and multi-day running-balance math; two more
`InlineData("CashFlow", ...)` cases added to the pre-existing
`ScheduledReportRendererTests.cs` (its `CreateRenderer()` helper already took a
`CashFlowReportService` argument when this phase started — found only via `dotnet build`
succeeding despite my search missing the file initially — but its parameterized cases hadn't
been extended to actually exercise the new type). `dotnet test`: 180 → 186, all green.

**Frontend**: `CashFlowDay`/`CashFlowReport` models, `getCashFlow`/`exportCashFlow` on
`FinancialReportsService`, new `admin-cash-flow-report.{ts,html}` page (copied from
General Ledger's date-range shape), `cash-flow` route added as the shell's default
redirect target, nav reordered with Trial Balance visually demoted via a new
`.reports-nav__secondary` style, `CashFlow` added to the report-schedules type dropdown.
`ng build --configuration development`: clean. `ng test --watch=false`: 76/76 passing —
the 8 pre-existing unrelated failures this file previously tracked as baseline (Login/
ShellComponent/AdminAuditLogs DI/directive gaps) are gone as of this session, resolved by
work elsewhere in the branch, not by this phase. No new failures either way.

**Deviation — si/ta translations**: the plan asked for real Sinhala/Tamil translations of
the new Cash Flow strings rather than the usual English-placeholder fallback used
elsewhere in `si.json`/`ta.json`. Asked the user directly; they chose best-effort machine
translation over the English placeholder or supplying their own strings. The Sinhala and
Tamil text now in both locale files for the `CASH_FLOW` keys, the `NAV.CASH_FLOW` label,
and the two updated `COPY` strings is machine-translated and **not verified by a native
speaker** — flag for review before this ships to Sinhala/Tamil-speaking users, particularly
the accounting terminology (opening/closing balance, unclassified-entries note).

**Verification**: `dotnet test` and `ng build`/`ng test` bars above, plus the two checks
the plan specified against the QA tenant (`qa-test-02@example.com`, tenant "QA Test Co 2")
and one beyond it:
- `GET /api/admin/financial-reports/cash-flow` over the tenant's full history:
  `TotalCashIn − TotalCashOut = 364,476.00`, `UnclassifiedAmount = 0`, exactly matching
  `1000-BANK`'s net (debit − credit) on the Trial Balance report for the same period.
- `GET /api/admin/financial-reports/cash-flow/export?format=csv` returns a 200 with one
  row per day, zero-activity days included.
- Browser-driven check (Playwright against the running dev server, not in the plan's
  ask but done for a UI-affecting change per this project's own verification norms):
  logged in as the QA tenant, confirmed `/app/reports/financial-reports` redirects to
  `cash-flow`, the nav renders Cash Flow first/active and Trial Balance last/muted, the
  stat tiles and day-by-day table render the same figures as the API check above, and
  zero browser console or network errors.

**Doc checklist**: all six items done — this file, `DEVREADME.md`,
`finrecon360-frontend/README.md`, `finrecon360-backend-master/finrecon360-backend/README.md`
updated to name Cash Flow as a headline statement and Trial Balance as secondary; root
`README.md` re-checked and confirmed still generic, no edit needed; final repo-wide grep
for `"Trial Balance"`/`"trial-balance"` found one file outside this list,
`.implementation-roadmap.md` — a dated 2026-08-18 status log, left untouched as a frozen
historical record (same treatment this plan file gives its own Section 6 log).
