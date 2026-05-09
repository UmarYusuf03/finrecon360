# Credit Sales and Receivables Workflow

## Purpose

This document defines the intended implementation for credit sales, receivables, payment-of-due events, bad debt write-off, and partial recovery in Finrecon360.

The key rule is:

- A sale is **not** classified as credit because a POS or ERP sales record failed to match.
- Credit sales are **explicitly selected and recorded inside Finrecon360** by an accountant or authorized user.
- POS is used for operational sales matching and settlement evidence, not for deciding credit-sale status.

## Core Principles

1. **System of record for credit status**
   - Finrecon360 is the source of truth for whether a sale is immediate or credit.
   - Credit sale status is manually assigned in the application.
   - A list of credit sales is maintained by the system and used for receivable tracking, aging, write-off, and recovery.

2. **Matching is evidence-driven**
   - EOD POS matching validates operational completeness and amounts.
   - It does not infer credit status.
   - Missing or unmatched transactions are reconciliation exceptions, not implicit credit sales.

3. **Receivables are separate from sales capture**
   - Credit sale recording and receivable payment recording are separate workflow lanes.
   - Payments of due may arrive later in cash, card, cheque, bank transfer, or installments.
   - Each receipt is matched against open receivables, not against the original sales matching lane.

4. **Bad debt is explicit**
   - Once a receivable ages out or is deemed uncollectible, accountants may write it off as bad debt.
   - Partial recovery after write-off must still be supported.
   - Bad debt and recovery must post journal entries and preserve audit history.

## Business Model

### Transaction Classes

- **Immediate Sale**
  - Paid at the time of sale.
  - Reconciled through POS, ERP, gateway, and bank workflows.

- **Credit Sale**
  - Manually marked in Finrecon360 by an accountant.
  - Added to the credit-sales list.
  - Creates an open receivable balance.

- **Payment of Due**
  - A later receipt against an existing credit sale.
  - Can be partial or full.
  - May be paid by cash, card, cheque, bank transfer, or other supported receipt method.

- **Bad Debt**
  - An open receivable that is written off.
  - Can later be partially or fully recovered.

### Important Distinction

Do not infer credit sale status from:

- missing POS line items,
- missing ERP gateway match,
- failed EOD matching,
- missing bank settlement,
- or timing gaps alone.

Credit sales must be recorded explicitly in the system.

## Recommended Data Model

### 1. Import Batch Metadata

Add a batch-level workflow dimension in addition to `SourceType`.

Suggested fields:

- `SourceType`
  - Example values: `POS`, `ERP`, `GATEWAY`, `BANK`
- `WorkflowType`
  - Example values:
    - `ImmediateSales`
    - `CreditSales`
    - `ReceivablePayments`
    - `BadDebtWriteOff`
    - `BadDebtRecovery`
    - `ReferenceOnly`

This keeps the source system separate from the business purpose of the import.

### 2. Credit Sale Registry

Create a receivables-focused entity or view for manually selected credit sales.

Suggested fields:

- `CreditSaleId`
- `TransactionId` or `InvoiceNumber`
- `CustomerCode`
- `SaleDate`
- `OriginalAmount`
- `OutstandingAmount`
- `Currency`
- `Status` (`Open`, `PartiallyPaid`, `Settled`, `WrittenOff`, `Recovered`)
- `CreatedByUserId`
- `CreatedAt`
- `UpdatedAt`
- `WriteOffReason`
- `WriteOffAt`

### 3. Receivable Payment Allocation

Model each payment separately from the original sale.

Suggested fields:

- `ReceivablePaymentId`
- `CreditSaleId`
- `PaymentDate`
- `PaymentAmount`
- `PaymentMethod`
- `ReferenceNumber`
- `SourceImportBatchId`
- `AllocatedByUserId`
- `AllocatedAt`
- `IsPartial`

### 4. Bad Debt and Recovery

Suggested fields:

- `BadDebtEventId`
- `CreditSaleId`
- `EventType` (`WriteOff`, `Recovery`)
- `Amount`
- `Reason`
- `PostedAt`
- `PostedByUserId`
- `JournalEntryId`

## Workflow Architecture

### A. Immediate Sales Flow

1. Import POS or ERP sales data.
2. Normalize rows into canonical form.
3. Commit the batch.
4. Run EOD sales matching.
5. Match sales to gateway/bank evidence when applicable.
6. Post journal entries for confirmed immediate sales.

This workflow handles normal immediate-settlement sales.

### B. Credit Sale Flow

1. Accountant manually selects a sale as credit in Finrecon360.
2. The sale is added to the credit-sales registry.
3. Outstanding balance is opened in receivables.
4. The sale does **not** participate in the normal EOD POS cash/card matching as a credit inference.
5. Later payments are imported or entered as payment-of-due events.
6. The system allocates each payment to one or more open receivables.
7. Receivable status changes to partially paid or settled.

### C. Payment of Due Flow

1. A payment event is captured from POS, manual entry, cash receipt, bank transfer, cheque, or another approved import.
2. The payment is treated as a separate event, not a sale.
3. The payment is matched against the open receivable list.
4. Partial payments are allowed.
5. Remaining balance stays open until fully paid or written off.

### D. Bad Debt Flow

1. Accountant reviews aged receivables.
2. A receivable is marked as bad debt.
3. The system posts a write-off journal entry.
4. The receivable moves to `WrittenOff`.
5. If recovery happens later, a recovery event is recorded.
6. The system posts a recovery journal entry and updates the receivable balance.

## Matching Rules

### 1. EOD POS Matching

Purpose:

- verify operational completeness,
- detect missing or duplicate sales lines,
- compare totals,
- ensure import integrity.

It should not:

- infer credit sale status,
- convert a missing line into a credit sale,
- or auto-create receivables.

### 2. Credit Sale Matching

Purpose:

- match manually selected credit sales to later receipts,
- allocate partial installments,
- reconcile AR balances,
- support write-off and recovery.

Matching keys may include:

- invoice number,
- customer code,
- receipt reference,
- amount,
- date window,
- outstanding balance tolerance.

### 3. Payment-of-Due Matching

Purpose:

- match later payments against existing receivables,
- not against the original sales matching lane.

Supported payment paths:

- cash at a later date,
- card at a later date,
- bank transfer,
- cheque,
- mixed or partial settlement.

## Suggested Import Types

Keep `SourceType` unchanged and add `WorkflowType` or equivalent.

Recommended batch categories:

- `POS` + `ImmediateSales`
- `ERP` + `ImmediateSales`
- `ERP` + `CreditSales`
- `ERP` + `ReceivablePayments`
- `BANK` + `Settlement`
- `GATEWAY` + `Settlement`
- `ERP` + `BadDebtWriteOff`
- `ERP` + `BadDebtRecovery`

This avoids overloading the meaning of ERP itself.

## Suggested UI Changes

### Import Workbench

Add controls for accountants such as:

- `No Accounts Receivable` toggle
- `Credit Sale` batch mode
- `Payment of Due` batch mode
- `Bad Debt Write-Off` batch mode
- `Bad Debt Recovery` batch mode

Suggested behavior:

- If `No Accounts Receivable` is selected, the batch is treated as immediate-only.
- If a batch is explicitly marked as credit-related, it enters the receivable lane.
- UI should never infer credit based on missing matches.

### Accountant Actions

Add screens/actions for:

- create credit sale manually,
- list open credit sales,
- record payment of due,
- allocate partial payments,
- write off bad debt,
- record recovery after write-off,
- review aging buckets.

## Implementation Plan

### Phase 1: Data Model

- Add batch workflow metadata.
- Add credit sale registry.
- Add receivable payment allocation.
- Add bad debt/recovery event tables or records.

### Phase 2: Import Routing

- Extend import workbench and commit flow.
- Route batches by `SourceType` + `WorkflowType`.
- Keep matching lanes isolated.

### Phase 3: Receivable Processing

- Implement credit sale creation.
- Implement payment-of-due allocation.
- Support installment settlement.

### Phase 4: Journal Posting

- Post write-offs.
- Post recoveries.
- Preserve a full audit trail.

### Phase 5: UI and Reporting

- Add credit sale list view.
- Add aging dashboard.
- Add bad debt and recovery reporting.

## Gaps To Solve

These are the main gaps that still need implementation work.

1. **Workflow metadata**
   - The current import flow routes mostly by `SourceType`.
   - It needs a batch-level workflow purpose.

2. **Credit sale registry**
   - Manual credit selection needs its own persisted model.
   - The current transaction model does not fully capture receivable lifecycle.

3. **Receivable allocation rules**
   - Partial payments and many-to-one allocations need explicit logic.
   - Overpayments and underpayments need policy decisions.

4. **Bad debt lifecycle**
   - Write-off and recovery must post journals and remain auditable.
   - Recovered bad debt must reduce the written-off balance correctly.

5. **Matching keys**
   - Credit sales need strong keys like invoice number and customer code.
   - If those are missing, manual review should be required.

6. **UI support**
   - Accountants need a list of credit sales and a receivable queue.
   - The current import workbench needs batch-type selection.

7. **Exception separation**
   - Operational missing sales and receivable items should not share the same queue.
   - They must be split to avoid false assumptions.

## Design Rules To Preserve

- Never infer credit sale status from mismatch alone.
- Never let POS EOD matching decide receivable lifecycle.
- Treat payment-of-due as a separate event.
- Keep cash/card immediate sales separate from receivables.
- Support partial payment, write-off, and recovery as first-class workflows.

## Summary

Finrecon360 should support two independent lanes:

1. **Operational sales matching** for immediate settlement and completeness checks.
2. **Receivables management** for manually selected credit sales, payment-of-due events, bad debt, and recovery.

That separation keeps matching meaningful and prevents the reconciliation engine from incorrectly classifying missing transactions as credit sales.
