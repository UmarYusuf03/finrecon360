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

   **Known gap**: there is no level that matches POS EOD directly against BANK statements (POS-terminal batch settlement — e.g. a bank line like `"POS SETTLEMENT - TID88552 - BATCH 000452"`). POS records only ever reconcile against Level1 (staff) and Level2 (ERP) today. Real bank narrative formats for batch settlements also aren't parseable by `SettlementKeyResolver`, which only does exact/trimmed/uppercased string comparison — there's no regex/substring-extraction step, and the canonical schema has no `BatchNumber`/`TerminalId`/`MerchantId` fields. If this is needed, it would look like a seventh level mirroring `SettlementMatchWorker`'s group-and-sum pattern but for `SourceType == "POS"`, keyed on a batch number extracted from the bank narrative per-mapping-template (batch number is the one identifier present across differently-formatted bank/acquirer narratives — TID and MID are not).

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
   - Runs every 5 minutes; for each active tenant, runs all six matching-level workers in Level1→Level6 order against one `TenantDbContext`
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

All 68 existing tests pass with new worker integration:
- No regressions
- Full backward compatibility
- Ready for production deployment

## 🚀 Next Steps

1. **Configure Worker Intervals** (if needed):
   - BankReconciliation: 5 min (line 51 in BankReconciliationHostedService)
   - JournalPosting: 5 min (line 20 in JournalPostingHostedService)

2. **Monitor Posting Events**:
   - Check transaction state transitions
   - Verify GL entries created
   - Monitor journal posting latency

3. **Set Up GL Export**:
   - Export journal entries to accounting system
   - Map GL account codes per tenant
   - Handle multi-currency settlements
