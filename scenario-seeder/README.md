# scenario-seeder

A standalone .NET console tool that fills a **brand-new** finrecon360 tenant with reconciliation
test data covering every matching rule and exception path the app has, so the reporting screens
and the matching engine can actually be exercised end to end instead of tested against a handful
of hand-typed rows.

It does this by acting as a real API client — logging in, creating a tenant the same way a real
customer signup would, creating real `Transaction` records, and pushing real CSV files through the
real import pipeline. It never touches the database directly, so everything it creates is
normalized/validated exactly the way production traffic would be.

## What it actually creates

- **A brand-new tenant**, end to end: registration → system-admin approval → onboarding →
  trial-plan activation → tenant-admin login. Nothing is shared with any existing tenant.
- **2 bank accounts** ("Primary Operating Account" and "Payroll Settlement Account" — the second
  one exists purely to host the "bank deposit landed in the wrong account" exception cases).
- **A couple of banking holidays**, placed on exactly the dates needed to prove the Level7
  settlement-window math accounts for holidays and not just weekends.
- **24 staff-entered Transactions** (cash-in shift handovers, card cash-outs), created and
  approved via the real Transactions API — these can't come from a CSV import, there's no import
  path for them.
- **5 CSV source files** — `POS.csv`, `ERP.csv`, `GATEWAY.csv`, `BANK.csv`, `POS_SETTLEMENT.csv`
  (plus `BANK_SECONDARY.csv`, a second bank-statement file tied to the second bank account) —
  written to `--out-dir`, and by default also imported through the real
  upload → parse → map → validate → commit pipeline.

Every distinct matching rule and exception across the six reconciliation workers that actually run
(`OperationalMatchWorker`, `PosErpSyncAuditWorker`, `ErpGatewaySalesMatchWorker`,
`BankStatementReconciliationWorker`, `SettlementMatchWorker`, `PosSettlementMatchWorker` — see
`WORKER-INTEGRATION.md`) gets **two** scenario instances, spread across a ~3-month window. See the
top-of-file comment in `ScenarioCatalog.cs` for the full list and the reasoning behind each one.

One thing it deliberately does **not** cover: a Level1 "Transaction has a blank description"
exception. The Transactions API itself rejects a blank description, so that code path in the
matching worker can't be reached without writing to the database directly — which this tool
avoids on principle. It's documented in `ScenarioCatalog.cs` rather than faked.

## What it does *not* do

It does not trigger matching. `ReconciliationCycleHostedService` runs on its own unattended
5-minute timer per tenant; this tool only gets the data into a `COMMITTED`/approved state so that
cycle has something to work on the next time it fires.

## Prerequisites

The backend API and its SQL Server database need to be running locally first — see
[`SETUP.md`](../SETUP.md) / [`DEVREADME.md`](../DEVREADME.md) at the repo root for how to start
them. This tool talks to `http://localhost:5279` by default (override with `--base-url`).

You also need the system admin credentials from the backend's `.env`
(`SYSTEM_ADMIN_EMAIL` / `SYSTEM_ADMIN_PASSWORD`) — approving a new tenant registration is a
control-plane action, so a brief system-admin login is part of the bootstrap sequence before the
tool switches to the new tenant's own admin account for everything else.

## Running it

Every run creates a **new, independent tenant** (its own database) — there's no shared state
between runs, so a "run this again for the demo" never touches an earlier "test" run. Just give
each run its own `--admin-email` / `--business-name` / `--out-dir`.

### 1. Sanity-check the data generator (no server needed)

```bash
cd scenario-seeder
dotnet run -- --verify
```

Builds the scenario catalog in memory and checks for the two things that would silently produce
bad test data: two transactions sharing a date (which would confuse Level4's amount+date gateway
lookup), and anything ending up dated after today. Prints row counts per source type. Safe to run
any time, doesn't touch the network.

### 2. Test run — fully automatic

Creates the tenant and imports everything itself; by the time it exits, the data is already
`COMMITTED` and just waiting for the next background matching cycle.

```bash
cd scenario-seeder
dotnet run -- \
  --system-admin-email admin@yourdomain.com \
  --system-admin-password fantasia@123 \
  --business-name "QA Test Co" \
  --admin-email qa-test-01@example.com \
  --tenant-password "TestPass!2026" \
  --out-dir ./out/test-run
```

When it finishes, log into the app (`http://localhost:4200`) with the `--admin-email` /
`--tenant-password` you chose — also saved to `out/test-run/tenant-session.json` — and give the
background worker a few 5-minute cycles (or restart the API, which also runs one 10 seconds after
startup) before checking the reconciliation reports.

### 3. Demo run — CSVs left for a live manual import

Same tenant/data setup, but stops short of importing the 5 CSV files — they're written to
`--out-dir` and left there for you to import by hand through the UI, so a supervisor can watch a
real import happen instead of finding it already done.

```bash
cd scenario-seeder
dotnet run -- \
  --system-admin-email admin@yourdomain.com \
  --system-admin-password fantasia@123 \
  --business-name "Demo Co" \
  --admin-email demo-01@example.com \
  --tenant-password "DemoPass!2026" \
  --out-dir ./out/demo-run \
  --skip-import
```

The tool prints exactly what to select in the UI for each file when it finishes:

| File | Source type | Bank account |
|---|---|---|
| `POS.csv` | `POS` | — |
| `ERP.csv` | `ERP` | — |
| `GATEWAY.csv` | `GATEWAY` | — |
| `BANK.csv` | `BANK` | Primary Operating Account (ID printed at run time) |
| `BANK_SECONDARY.csv` | `BANK` | Payroll Settlement Account (ID printed at run time) |
| `POS_SETTLEMENT.csv` | `POS_SETTLEMENT` | — |

For each: **Upload** the file → **Parse** → **Mapping** (every canonical field maps to the
identically-named CSV column, since these files were generated with canonical headers already) →
**Validate** → **Commit**. Matching only starts once a file is `COMMITTED`, so do this — or let the
background cycle run — with enough lead time before you actually need the reports populated on
camera.

### All options

```bash
dotnet run -- --help
```

| Flag | Required | Meaning |
|---|---|---|
| `--system-admin-email` | yes | From the backend's `.env` |
| `--system-admin-password` | yes | From the backend's `.env` |
| `--business-name` | yes | New tenant's display name |
| `--admin-email` | yes | New tenant admin's login — must not already exist |
| `--tenant-password` | yes | Password to set for that tenant admin |
| `--base-url` | no | Default `http://localhost:5279` |
| `--business-type` | no | `VEHICLE_RENTAL` (default) or `ACCOMMODATION` |
| `--out-dir` | no | Default `./out/<timestamp>` |
| `--anchor-end` | no | Last date of the 3-month window, `yyyy-MM-dd`. Default: 2 days before today |
| `--skip-import` | no | Write CSVs only, don't auto-import them (see "Demo run" above) |
| `--verify` | no | Dry-run data-generator check, no network calls (see step 1 above) |

## How it works, mechanically

1. **Bootstraps a tenant** the same way a real signup does: `POST /api/public/tenant-registrations`
   → log in as system admin → approve the registration → verify the onboarding magic link → set
   the tenant admin's password → activate the free trial plan → log in as the tenant admin. From
   here every call carries that tenant admin's JWT.
2. **Creates the 2 bank accounts and the banking holidays** via their normal admin APIs.
3. **Builds the whole scenario catalog** in memory (`ScenarioCatalog.BuildAll`) — every scenario's
   dates, amounts, and reference/settlement keys are computed once, up front.
4. **Creates and approves the 24 Transactions** via `POST /api/admin/transactions` +
   `.../approve`, exactly like a staff member clicking through the UI would.
5. **For each of the 5 source types**: serializes its rows to CSV, writes the file to disk, and
   (unless `--skip-import`) pushes it through `POST /api/imports` (upload) →
   `.../parse` → `.../mapping` → `.../validate` → `.../commit`. If validation fails on any row,
   the tool stops immediately and prints exactly which row and why, rather than silently
   committing something malformed.
6. Exits. Matching itself happens later, on the app's own schedule.

## Source layout

| File | Responsibility |
|---|---|
| `Program.cs` | CLI argument parsing, `--verify` mode, top-level orchestration |
| `TenantBootstrapper.cs` | The registration → approval → onboarding → activation flow |
| `SeedRunner.cs` | Bank accounts, holidays, transactions, and the 5-file import loop |
| `ScenarioCatalog.cs` | Every scenario definition — the actual test-data spec |
| `ScenarioModels.cs` | Shared row/date-cursor bookkeeping used while building the catalog |
| `BusinessDayMath.cs` | A byte-for-byte mirror of the backend's business-day/holiday-window math, so Level7 dates land exactly where the server expects them |
| `ApiClient.cs` | Thin JWT-aware HTTP wrapper (JSON + multipart upload) |
| `ImportRow.cs` | The canonical CSV row shape + writer |
| `Dtos.cs` | Client-side mirrors of the backend's request/response shapes |
