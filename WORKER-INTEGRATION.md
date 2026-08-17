# Worker Integration Summary: Six-Level Reconciliation → Journal Posting Pipeline

## ✅ Integration Complete (2026-08-17 update)

**Status change from the original version of this doc**: the workers below were previously implemented but never actually running — nothing was registered in DI, so `BankReconciliationHostedService` (the only hosted service that existed at the time) and every worker class sat unused. Five of the six matching levels (Operational, Sync-Audit, Sales, Collection, Settlement) had no hosted service driving them at all. This has been fixed: all seven workers are now registered in `Program.cs`, and a single `ReconciliationCycleHostedService` drives all six matching levels in order every cycle. See "DI Wiring" below for what changed and why.

### Components

1. **The six matching-level workers** (`Services/Workers/*.cs`), all sharing the signature `Task<TResult> ExecuteAsync(Guid tenantId, TenantDbContext tenantDb, CancellationToken ct)`:

   | Level | Worker | Matches |
   |---|---|---|
   | Level1 | `OperationalMatchWorker` | Staff manual entry ↔ POS EOD |
   | Level2 | `PosErpSyncAuditWorker` | POS EOD ↔ ERP sales ledger |
   | Level3 | `ErpGatewaySalesMatchWorker` | ERP sales ledger ↔ Payment gateway |
   | Level4 | `BankStatementReconciliationWorker` | Approved card cashout ↔ Bank statement |
   | Level5 | `CollectionMatchWorker` | Physical card-in ↔ Bank statement |
   | Level6 | `SettlementMatchWorker` | Gateway payout (many records) ↔ Bank statement (one deposit) |
   | Level7 | `PosSettlementMatchWorker` | POS-terminal batch settlement (POS EOD) ↔ Bank statement |

   **Level7 detail** (2026-08-18 addition — closes the gap this doc used to flag here): matches
   POS EOD batches against BANK deposits using identifiers (`BatchNumber`/`TerminalId`/`MerchantId`)
   extracted from narrative bank descriptions at import time by `Services/Reconciliation/PosIdentifierExtractor.cs`
   (regex per `ImportMappingTemplate.ExtractionPatternsJson`, since raw bank narratives — e.g.
   `"POS SETTLEMENT - TID88552 - BATCH 000452"` — aren't parseable by exact-string comparison).
   Runs as a four-tier waterfall, each tier a grouped-sum comparison that naturally produces 1:1,
   many:1 (consolidated), or 1:many (split-network settlement, e.g. Amex separate from Visa/MC)
   depending on how many records land on each side of a shared key:
   - **Tier1** — `BatchNumber`, exact. Auto-confirms.
   - **Tier2** — `TerminalId` + calendar date (POS side only — BANK's date won't align due to
     settlement lag, reconciled via a T+N business-day window instead,
     `Services/Reconciliation/BusinessDayCalculator.cs`, admin-maintained holiday list in
     `BankingHolidays`). Auto-confirms.
   - **Tier3** — `MerchantId` + calendar date, same asymmetric shape as Tier2 but broader (the
     last-resort tier). Never auto-confirms — always requires human review via the existing
     `ReconciliationMatchConfirmationService` confirm flow.
   - **Tier4** — anything left gets a `MatchNotFound` event, same as every other level.

   A key match whose amounts don't reconcile within tolerance is a distinct `Variance`/`Exception`
   for that specific batch, not something a looser tier is allowed to retry — blending it into a
   broader aggregate would risk masking a real discrepancy. Confirmed/auto-confirmed matches post
   a 3-line balanced voucher (`Services/Reconciliation/PosSettlementPoster.cs`: DEBIT Bank net,
   DEBIT MDR fee, CREDIT POS Clearing gross) against three `ChartOfAccount` rows, one of them
   (`6000-POSCLEARING`) newly seeded for this level.

2. **JournalPostingExecutorWorker**
   - Location: `Services/Workers/JournalPostingExecutorWorker.cs`
   - Finds JournalReady transactions and creates a `JournalVoucher` + double-entry `JournalEntry` rows, each posted against a real `ChartOfAccount` row
   - Verifies the voucher's entries sum to zero before posting — rejects (logs + counts as failed) rather than committing an unbalanced voucher
   - Automatically posts after bank reconciliation confirms matches
   - Handles gateway processing fees with separate GL entries

3. **JournalPostingHostedService**
   - Location: `BackgroundServices/JournalPostingHostedService.cs`
   - Runs every 5 minutes (30-second startup delay so it runs after a reconciliation cycle)
   - Iterates through all active tenants
   - Safe concurrent execution with tenant-level locking

4. **ReconciliationCycleHostedService** (new — replaces the old single-worker `BankReconciliationHostedService`)
   - Location: `BackgroundServices/ReconciliationCycleHostedService.cs`
   - Runs every 5 minutes; for each active tenant, runs all seven matching-level workers in Level1→Level7 order against one `TenantDbContext`
   - Each worker runs in its own try/catch so one worker's failure doesn't block the rest of the cycle
   - `BankReconciliationHostedService` (which only ever ran Level4) was deleted — its job is now one step of this cycle

5. **DI Wiring** (`Program.cs`) — this is what was actually missing before:
   ```csharp
   builder.Services.AddScoped<IReconciliationSettingsProvider, ReconciliationSettingsProvider>();
   builder.Services.AddScoped<OperationalMatchWorker>();
   builder.Services.AddScoped<PosErpSyncAuditWorker>();
   builder.Services.AddScoped<ErpGatewaySalesMatchWorker>();
   builder.Services.AddScoped<BankStatementReconciliationWorker>();
   builder.Services.AddScoped<CollectionMatchWorker>();
   builder.Services.AddScoped<SettlementMatchWorker>();
   builder.Services.AddScoped<IJournalPostingExecutorWorker, JournalPostingExecutorWorker>();

   builder.Services.AddHostedService<ReconciliationCycleHostedService>();
   builder.Services.AddHostedService<JournalPostingHostedService>();
   ```

6. **Schema**: `ReconciliationMatchGroups`, `ReconciliationMatchedRecords`, `ReconciliationEvents`, `JournalEntries` were declared on `TenantDbContext` but were never actually created in tenant SQL Server databases — the hand-rolled `SqlServerTenantSchemaMigrator` (tenant DBs don't use EF Core migrations) had no `CREATE TABLE` for any of them. Worker unit tests only ever passed because they run against EF's InMemory provider, which fabricates schema regardless of what's deployed. This is now fixed with real migrations in `Services/TenantSchemaMigrator.cs`, along with two columns (`MatchStatus`, `SettlementKey`) that every worker queries but that were also missing from `ImportedNormalizedRecords`.

7. **Chart of accounts seed gap, found and fixed**: `JournalPostingExecutorWorker` posts a `CreditCashIn` entry for direct (non-card) CashIn transactions, but the original four-account seed only covered `DebitBank`/`CreditCashOut`/`DebitFeeExpense`/`CreditFeeOffset` — there was no account for `CreditCashIn`. Every CashIn transaction's journal posting threw `KeyNotFoundException` inside the worker's try/catch and silently counted as `failed`, forever. Fixed by seeding a fifth account (`3000-CASHIN`, "Cash-In Clearing", mirroring `2000-CASHOUT`'s role for the opposite cash direction) via a new migration (`202608170006_TenantChartOfAccountsCashInSeed`), and by making the entry-type→account lookup use `TryGetValue` instead of an indexer so a future gap like this fails safe (null `ChartOfAccountId`) instead of throwing. Covered by a new test, `ExecuteAsync_posts_balanced_voucher_for_direct_cash_cashin`.

### Known duplication to clean up (introduced by merging with `upstream/main`)

A parallel branch (merged into this one via `Merge remote-tracking branch 'upstream/main' into umair`) independently built its own canonical settlement-key resolver and match-status vocabulary in `Services/Reconciliation/ReconciliationContracts.cs` (namespace `finrecon360_backend.Services.Reconciliation`), including a `MatchGroupMetadata` record that replaces ad-hoc anonymous-object JSON for match-group metadata. This is a real improvement — `BankStatementReconciliationWorker` and `JournalPostingExecutorWorker` now use it, and it fixed a genuine metadata-shape mismatch that used to exist between the import path and the posting worker.

However, this now coexists with the pre-existing versions of the same concepts:

- `Services/SettlementKeyResolver.cs` (namespace `finrecon360_backend.Services`) vs `Services/Reconciliation/ReconciliationContracts.cs`'s `SettlementKeyResolver` (namespace `finrecon360_backend.Services.Reconciliation`) — two classes, same name, same purpose, nearly identical logic.
- `Services/Workers/ReconciliationConstants.cs`'s `MatchStatuses` (namespace `finrecon360_backend.Services.Workers`, values `Pending/Matched/Waiting/Exception/Level2Matched/Level3Matched/Level6Matched/SalesVerified`) vs `ReconciliationContracts.cs`'s `MatchStatuses` (namespace `finrecon360_backend.Services.Reconciliation`, values `Pending/InternalVerified/SalesVerified/Exception/Waiting/Matched`) — different vocabularies for the same field.

`CollectionMatchWorker`, `ErpGatewaySalesMatchWorker`, `OperationalMatchWorker`, `PosErpSyncAuditWorker`, `SettlementMatchWorker`, and `ReconciliationController` still reference the older `Services`/`Services.Workers` versions; `BankStatementReconciliationWorker` and `JournalPostingExecutorWorker` reference the newer `Services.Reconciliation` versions. The project currently builds and all tests pass because none of these files imports both namespaces in a way that collides — but consolidating onto one vocabulary is real follow-up work, not done here, because the two `MatchStatuses` enums encode different design decisions (per-level granularity vs. a flatter scheme) and migrating the five still-on-the-old-version workers means verifying each level's idempotency-guard semantics (`MatchStatus != X`) still hold under the new values — that's a deliberate design choice for a human to make, not something to silently pick.

### Workflow Pipeline

```
Transaction Flow:
┌─────────────┐
│  Pending    │ (initial state after creation)
└──────┬──────┘
       │ (approval)
       ▼
┌─────────────────────────────────────┐
│      Cash vs Card Decision          │
└─────────┬───────────────┬───────────┘
          │               │
    (Cash/CashIn)   (Card CashOut)
          │               │
          ▼               ▼
   ┌──────────────┐  ┌──────────────────────┐
   │JournalReady  │  │  NeedsBankMatch      │
   │(post to GL   │  │(wait for bank        │
   │ immediately) │  │ statement match)     │
   └──────────────┘  └──────┬───────────────┘
          │                 │
          │                 │ (BankStatementReconciliationWorker,
          │                 │  runs on ReconciliationCycleHostedService)
          │                 │ Level-4 matching:
          │                 │ - Correlate GATEWAY↔BANK
          │                 │ - Validate net amounts (net of fees)
          │                 │ - Create UNCONFIRMED match group
          │                 ▼
          │          ┌──────────────────────────┐
          │          │  Still NeedsBankMatch —   │
          │          │  a human must confirm the │
          │          │  match group before it    │
          │          │  promotes the transaction │
          │          └────────┬─────────────────┘
          │                   │ (ReconciliationMatchConfirmationService,
          │                   │  triggered via ReconciliationController)
          │                   ▼
          │          ┌──────────────────┐
          │          │  JournalReady    │
          │          └────────┬─────────┘
          │                   │
          └───────────┬───────┘
                      │
                      ▼ (JournalPostingExecutorWorker)
              ┌────────────────────────────────┐
              │ JournalVoucher + JournalEntries │
              │ (verified to sum to zero)       │
              │ - DebitBank      → ChartOfAccount│
              │ - CreditCashOut  → ChartOfAccount│
              │ - DebitFeeExpense()              │
              │ - CreditFeeOffset()              │
              └────────────────────────────────┘
```

This diagram shows only the card-cashout path (Level4 → journal). Levels 1, 2, 3, 5, and 6 run independently on the same cycle, each producing their own `ReconciliationMatchGroup`/`ReconciliationEvent` rows for their respective source pairs — see the level table above.

---

## 🎯 Fee Handling Explained

### The Problem You Identified

When a payment gateway processes transactions:
1. **Customer charged**: GrossAmount (what the merchant charges)
2. **Merchant receives**: GrossAmount - ProcessingFee = NetAmount
3. **Bank deposits**: NetAmount (the actual settlement to the merchant's bank account)

So yes, **the amounts differ** between import sources.

### How Finrecon Handles This

#### 1. **Import-Time Extraction**
```
GATEWAY Import Record:
  ├─ GrossAmount: 1,000 LKR (what was charged)
  ├─ ProcessingFee: 30 LKR (gateway fee)
  └─ NetAmount: 970 LKR (what merchant gets)

BANK Statement Record:
  └─ NetAmount: 970 LKR (what actually deposited)
```

#### 2. **BankStatementReconciliationWorker Matching**
The reconciliation already handles fees correctly:

```csharp
// From BankStatementReconciliationWorker.cs
var gatewayNetTotal = linkedGateway.NetAmount;  // 970 LKR

var bankAggregate = matchingBankRecords
    .Aggregate(
        new { NetTotal = 0m, FeeTotal = 0m, ... },
        (acc, br) => new {
            NetTotal = acc.NetTotal + br.NetAmount,  // 970 LKR
            FeeTotal = acc.FeeTotal + (br.ProcessingFee ?? 0m),  // 30 LKR
            ...
        });

// Reconciliation compares net amounts (net of fees)
if (Math.Abs(bankAggregate.NetTotal - gatewayNetTotal) > Tolerance)
    // Match fails if net amounts don't align
```

**Key: The 0.01 Tolerance** accounts for:
- Rounding differences from currency conversions
- Accounting adjustments
- Minor variances in settlement calculations

#### 3. **Journal Posting with Fee Split**
When `JournalPostingExecutorWorker` posts entries:

```csharp
// Entry 1: Bank deposit (net amount received)
DebitBank:      970 LKR
CreditCashOut: (970) LKR

// Entry 2: Gateway fee expense (if applicable)
DebitFeeExpense:    30 LKR
CreditFeeOffset:   (30) LKR
```

**GL Result:**
```
GL Accounts:
├─ Bank (Asset):           +970 LKR  [what was deposited]
├─ CashOut (Liability):    -970 LKR  [merchant obligation settled]
├─ FeeExpense (Expense):   +30 LKR   [cost of payment processing]
└─ FeeOffset (Contra):     -30 LKR   [offset to report net settlement]
```

---

## 📊 Example Scenario

### Scenario: Card Cashout with PayHere Gateway

**Transaction Created:**
```
Amount: 10,000 LKR
Type: CashOut
Method: Card (requires bank reconciliation)
Status: Pending → Approved → NeedsBankMatch
```

**GATEWAY Import (PayHere reconciliation file):**
```
Gross Amount:    10,000 LKR
Processing Fee:  300 LKR
Net Amount:      9,700 LKR (what PayHere will settle)
Settlement Key:  MERCHANT_ACCT|TXN_12345
```

**BANK Statement (merchant's bank):**
```
Net Deposit:     9,700 LKR (actual bank deposit received)
Settlement Key:  MERCHANT_ACCT|TXN_12345
```

**BankReconciliationWorker:**
1. Finds transaction in NeedsBankMatch
2. Matches GATEWAY net (9,700) to BANK deposit (9,700) ✓
3. Confirms with settlement key MERCHANT_ACCT|TXN_12345
4. Creates ReconciliationMatchGroup + ReconciliationMatchedRecords
5. Updates transaction → JournalReady

**JournalPostingExecutorWorker:**
1. Finds transaction in JournalReady
2. Extracts settlement metadata:
   - bankNetTotal: 9,700
   - processingFeeAdjustment: 300
3. Creates 4 GL entries:
   - DR Bank 9,700
   - CR CashOut (9,700)
   - DR FeeExpense 300
   - CR FeeOffset (300)
4. Posts all entries atomically
5. Downstream GL export shows:
   - Net settlement: 9,700 LKR
   - Fee deduction: 300 LKR
   - Gross transaction: 10,000 LKR

---

## 🔄 Workflow Guarantees

### Atomicity
- All journal entries for a transaction post together or not at all
- No partial posting (all-or-nothing semantics)

### Idempotency
- Workers detect already-processed transactions
- Safe to run repeatedly without duplicating entries
- Concurrent executions per tenant prevented by locking

### Audit Trail
- State history recorded in `TransactionStateHistories`
- All GL entries linked to source records
- Matches include detailed metadata (amounts, fees, settlement keys)

### Fee Reconciliation
- Fees captured at import time
- Fee amounts stored in match metadata
- Separate GL entries for transparent fee tracking
- No hidden deductions; all splits visible in GL

---

## ✅ Test Results

All 129 tests pass (`finrecon360-backend.Tests`), including coverage for all seven matching levels, ambiguous-match detection, tenant-configurable tolerances, bank-account scoping, balanced journal-voucher posting (including the CreditCashIn fix above), identifier extraction against real bank narrative formats, business-day settlement-window arithmetic, and the Level7 waterfall's tier boundaries (1:1, many:1, 1:many, variance, no-match, window-exclusion, idempotency).

## 🚀 Next Steps / Known Follow-Ups

1. **Consolidate the duplicate `SettlementKeyResolver`/`MatchStatuses` classes** — see "Known duplication to clean up" above. Still not done as of the Level7 addition; new Level7 code deliberately builds on the canonical `Services.Reconciliation` versions to avoid making this worse.
2. **Level7 admin UI** — the backend (matching, `ExtractionPatternsJson` on mapping templates, `banking-holidays` CRUD, `SettlementDateWindowDays` setting) is done; no frontend surfaces it yet.
3. **Configure worker intervals** (if needed): both `ReconciliationCycleHostedService` and `JournalPostingHostedService` currently hardcode a 5-minute `RunInterval` (a `static readonly TimeSpan` at the top of each class) — not yet tenant-configurable.
4. **Monitor Posting Events**:
   - Check transaction state transitions
   - Verify GL entries created
   - Monitor journal posting latency
5. **Set Up GL Export**:
   - Export journal entries to accounting system
   - Map GL account codes per tenant
   - Handle multi-currency settlements
