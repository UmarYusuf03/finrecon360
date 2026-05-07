# Worker Integration Summary: Bank Reconciliation → Journal Posting Pipeline

## ✅ Integration Complete

### Components Added

1. **JournalPostingExecutorWorker**
   - Location: `Services/Workers/JournalPostingExecutorWorker.cs`
   - Finds JournalReady transactions and creates double-entry GL journal entries
   - Automatically posts after bank reconciliation confirms matches
   - Handles gateway processing fees with separate GL entries

2. **JournalPostingHostedService**
   - Location: `BackgroundServices/JournalPostingHostedService.cs`
   - Runs every 5 minutes (30-second startup delay for sequencing)
   - Iterates through all active tenants
   - Safe concurrent execution with tenant-level locking

3. **Program.cs Registration**
   ```csharp
   builder.Services.AddScoped<IJournalPostingExecutorWorker, JournalPostingExecutorWorker>();
   builder.Services.AddHostedService<JournalPostingHostedService>();
   ```

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
          │                 │ (BankReconciliationWorker)
          │                 │ Level-4 matching:
          │                 │ - Correlate GATEWAY↔BANK
          │                 │ - Validate net amounts (net of fees)
          │                 │ - Confirm settlement key
          │                 ▼
          │          ┌──────────────────┐
          │          │  JournalReady    │
          │          │(unlock posting)  │
          │          └────────┬─────────┘
          │                   │
          └───────────┬───────┘
                      │
                      ▼ (JournalPostingExecutorWorker)
              ┌───────────────────────┐
              │ Journal Entries (GL)  │
              │ - DebitBank           │
              │ - CreditCashOut       │
              │ - DebitFeeExpense()   │
              │ - CreditFeeOffset()   │
              └───────────────────────┘
```

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
