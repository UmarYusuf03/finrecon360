# Reconciliation Match Reversal — Implementation Plan

Status: **design ready for implementation, not started**. Written to be picked up cold in a
separate chat/session — every claim below was checked directly against the current source in
`finrecon360-backend-master/finrecon360-backend` (file paths and line-level facts included), not
inferred from memory. Where something still needs to be *confirmed* rather than assumed, it's
called out explicitly under "Open questions."

## 1. Problem

Two existing paths change a match group's state, and neither is safe for every situation:

- `ReconciliationMatchConfirmationService.RejectMatchAsync` (`Services/ReconciliationMatchConfirmationService.cs`)
  resets every linked `ImportedNormalizedRecord.MatchStatus` to `"PENDING"` and sets the group's
  `Status = "Rejected"`. **It has no guard against being called on an already-confirmed,
  journal-posted group** — it doesn't check `IsConfirmed`, doesn't touch `JournalVoucher`/
  `JournalEntry` rows, and doesn't demote a promoted `Transaction` back out of `JournalReady`.
  Calling it today on a confirmed Level4 match would leave a posted GL voucher and a
  `JournalReady` transaction in place while silently unlinking the records that justified them —
  a real, currently-reachable data-integrity bug, not just a missing feature.
- There is no path at all for undoing a match that has already had a journal voucher posted
  against it.

This plan adds a single new operation — reversing a match group regardless of how far it
progressed — that supersedes the confirmed/posted case `RejectMatchAsync` can't safely handle,
without touching the six matching workers or the import pipeline (see §7, "Blast radius").

## 2. Corrections to the earlier proposal draft

An earlier draft of this plan (Parts 1–3, produced by a different session without reading the
worker source directly) got several specifics wrong. Anyone comparing this plan against that
draft should treat *this* document as authoritative — each of these was checked against the
worker source and its unit tests this session:

| Draft claimed | Actually (verified) |
|---|---|
| Level4 missing-settlement-key → `MatchStatus = WAITING` + `ManualReview` event | Silently counted as an Exception — **no event row is created at all**. `ManualReview` events are real but come from a *different* case: 2+ GATEWAY candidates matching the same transaction by date+amount. |
| Level4 Tier3 → creates a match group, `IsConfirmed=false`, `Status=RequiresReview` | Tier3 (unexplained variance, both exact and fee-adjusted amount checks fail) is an Exception with **no group created at all** — confirmed by `BankStatementReconciliationWorkerTests.ExecuteAsync_handles_amount_variance_as_exception`, which asserts the events table stays empty. This behavior looks borrowed from Level7's real Tier3 (which *does* create an unconfirmed group) and misattributed to Level4. |
| Level3 revert target: `LEVEL2_MATCHED` | `LEVEL2_MATCHED` is written onto the **POS** record by Level2, never onto ERP. The ERP record's status after Level2 is plain `MATCHED`. |
| `Transaction.MatchMetadataJson` | Doesn't exist on `Transaction`. That field lives on `ReconciliationMatchGroup` (`MatchMetadataJson`, JSON-serialized `MatchGroupMetadata`, see `Services/Reconciliation/ReconciliationContracts.cs`). |
| `TransactionState.Posted` | Not a real value. The enum (`Models/TransactionEnums.cs`) is `{Pending, Approved, Rejected, NeedsBankMatch, JournalReady}`. |
| Per-level "restore this exact prior status" table | Superseded by §4 below — reuse the pattern `RejectMatchAsync` already uses (reset to `PENDING`, let the idempotent workers re-derive the right status on their next cycle) instead of hand-maintaining a lookup table that has already proven error-prone once. |

## 3. What's already there to build on (don't reinvent these)

- **The "reset to PENDING" pattern already exists and is the right one.** `RejectMatchAsync`
  already does exactly this for every linked record, with no per-level branching:
  ```csharp
  foreach (var record in importedRecords) { record.MatchStatus = "PENDING"; }
  ```
  This independently validates the recommendation in §4 — it's not a new idea, it's extending an
  existing, working pattern to the confirmed/posted case it doesn't yet cover.
- **`ReconciliationEvent`** is the existing audit-trail table (`EventType`, `MatchLevel`, `Details`,
  `ReconciliationMatchGroupId`, `CreatedAt`) — no new table needed for the audit record, same as
  `RejectMatchAsync`'s `"MatchRejected"` event.
- **`JournalVoucher.ReconciliationMatchGroupId`** (nullable `Guid`, `Models/JournalVoucher.cs`) is
  already the exact link needed to find every voucher posted because of a given match group,
  regardless of *which* worker posted it (`CardCashoutPromoter`/`JournalPostingExecutorWorker` for
  Level4, `PosSettlementPoster` for Level7, or a manual `POST .../post-journal` call for others).
  One query (`db.JournalVouchers.Where(v => v.ReconciliationMatchGroupId == groupId)`) covers
  every case — no level-specific journal logic required.
- **`MatchGroupMetadata.TransactionId`** (`Services/Reconciliation/ReconciliationContracts.cs`,
  parsed via `MatchGroupMetadata.TryParse(group.MatchMetadataJson)`) is how `CardCashoutPromoter`
  already identifies which `Transaction` a Level4 group promoted. Reversal should look the
  transaction up the same way, not re-derive it by amount/date proximity.
- **`JournalVoucher.Status`** already has a 3-state model (`Pending | Posted | Failed`) — a
  reversal only needs to act on vouchers actually in `Posted` state.

## 4. Design

### 4.1 Reset-to-PENDING instead of a per-level restore table

Every one of the six matching workers already queries strictly by `MatchStatus == PENDING` (or a
level-specific "already handled" status) and is idempotent — Level7 has an explicit test proving
a second run against already-matched data changes nothing. So instead of computing "what status
was this record in immediately before this specific match," reversal resets every linked record
straight to `PENDING` and lets the normal 5-minute cycle re-derive whatever status it should
actually have. One extra hop through a level that would trivially re-confirm the same intermediate
status (e.g. Level2 re-marking an ERP record `MATCHED` before Level3 re-checks it) is a negligible
cost against never having a hand-maintained table drift out of sync with six independently-evolving
workers — which is exactly how the earlier draft got two levels wrong in one pass.

### 4.2 Journal reversal: Storno, never delete

Keep this part of the earlier draft as-is — it's correct and matches how a posted GL entry should
never be touched:

- For each `Posted` `JournalVoucher` linked to the group, create a new voucher with mirrored
  entries at negated `Amount`, same `ChartOfAccountId`/`Currency`, `EntryType` suffixed or tagged
  to mark it as a reversal (the codebase doesn't currently have a dedicated "Reversal" `EntryType`
  — see open question in §8).
  - Sum-to-zero as a JS-side / posting-service check, same as `JournalPostingExecutorWorker`
    already does before it commits an original voucher — a reversal that doesn't balance should
    fail loudly the same way an original posting does, not silently commit.
- Set the original voucher's `Status` — there's no `IsReversed` flag on `JournalVoucher` today, so
  either add one (schema/migration change) or represent "reversed" purely by the presence of a
  reversing voucher pointing back at it (needs a `ReversesVoucherId` column either way to make
  "has this already been reversed" queryable without joining on amounts). **This needs one small
  migration** — see §6 step 2.

### 4.3 Transaction demotion (Level4 only)

- Look up `MatchGroupMetadata.TransactionId` from `group.MatchMetadataJson`.
- If found and the transaction is `JournalReady`: set `TransactionState = NeedsBankMatch`, append
  a `TransactionStateHistory` row (same pattern `CardCashoutPromoter` already uses for the forward
  direction), clear `ApprovedAt`/`ApprovedByUserId`? — **no**, approval and matching are separate
  state transitions in this codebase (`Approve` → `NeedsBankMatch`/`JournalReady`; matching is a
  later step) — reversal should undo the *match*, not the *approval*. Only the match-driven
  state change gets undone.
- Level6/Level7 groups aren't linked to a `Transaction` at all (confirmed: Level6 and Level7 match
  imported records against each other, not against internal `Transaction` rows) — this step is a
  no-op for those levels, not a special case to branch on.

### 4.4 Concurrency: `JournalPostingExecutorWorker` runs independently

`JournalPostingExecutorWorker` (`Services/Workers/JournalPostingExecutorWorker.cs`) watches for
`JournalReady` transactions **on its own schedule**, decoupled from match confirmation — a
transaction can sit `JournalReady` for one worker cycle before this worker posts its voucher. That
means at the moment a user clicks "reverse," the group's journal state could be: not yet posted,
posted, or *mid-posting* (rare race). The reversal service must re-read `IsJournalPosted` and the
actual `JournalVoucher` rows fresh, inside its own DB transaction, immediately before deciding
what to reverse — not trust a value read earlier in the request (e.g. from a list screen).

### 4.5 Naming: don't call the new endpoint "unmatch"

`MatchGroupsController` already has `GET /api/admin/match-groups/unmatched` meaning "BANK records
with no match group yet" (`ReconciliationMatchConfirmationService.GetUnmatchedQueueAsync`) — an
unrelated, pre-existing concept. Reusing "unmatch" for "undo an existing match" would collide with
that vocabulary in the same controller. Use **`reverse`** (`POST
/api/admin/match-groups/{id}/reverse`) instead.

## 5. Two existing controllers overlap — resolve before adding a third path

`Controllers/Admin/MatchGroupsController.cs` (`api/admin/match-groups`, backed by
`IReconciliationMatchConfirmationService`) and `Controllers/Admin/ReconciliationController.cs`
(`api/admin/reconciliation`, which also exposes its own `match-groups/{id}/confirm`) both appear to
implement match confirmation independently. **Before writing the reversal endpoint, confirm which
one the frontend actually calls** (grep `finrecon360-frontend/src` for the route) so the new
`reverse` endpoint goes on the controller that's actually live, rather than creating a third,
possibly-dead parallel path. This wasn't resolved in this session — see §8.

## 6. Step-by-step implementation checklist

1. **Resolve §5** — grep the frontend for which controller's confirm/reject routes are actually
   wired to the Matcher UI. Put `reverse` on that one.
2. **Migration**: add `ReversesVoucherId` (nullable `Guid`, FK to `JournalVoucher`) to
   `JournalVoucher`. Optionally add a `"Reversal"` value to whatever constrains `EntryType` today
   (check — `JournalEntry.EntryType` is a free-text `string`, so this may just be a new literal,
   not a schema change).
3. **New service**, alongside the existing one rather than folding into it (reversal is a
   materially different operation from confirm/reject — different failure modes, needs the journal
   layer):
   ```csharp
   public interface IReconciliationMatchReversalService
   {
       Task<ReversalResult> ReverseMatchGroupAsync(
           TenantDbContext db, Guid matchGroupId, string reason, Guid reversedByUserId, CancellationToken ct);
   }

   public record ReversalResult(bool Success, string? Error, bool JournalReversed, bool TransactionDemoted);
   ```
   Implementation, in one DB transaction:
   - Load the group with `.Include(g => g.MatchedRecords)`. 404 if missing.
   - Re-check `IsJournalPosted` fresh (§4.4) and query `JournalVouchers.Where(v =>
     v.ReconciliationMatchGroupId == matchGroupId && v.Status == "Posted" && v.ReversesVoucherId == null)`
     — the `ReversesVoucherId == null` guard prevents double-reversing.
   - For each posted voucher found: build and save a mirrored reversal voucher (§4.2).
   - Parse `MatchGroupMetadata.TryParse(group.MatchMetadataJson)`; if `TransactionId` is set and
     that transaction is `JournalReady`, demote it (§4.3) with a `TransactionStateHistory` row.
   - Reset every linked `ImportedNormalizedRecord.MatchStatus` to `"PENDING"` — copy
     `RejectMatchAsync`'s existing loop verbatim, don't reimplement it.
   - Delete (or mark inactive, if the schema needs the join history — check
     `ReconciliationMatchedRecord` for any other consumer expecting it to persist) the
     `ReconciliationMatchedRecord` rows linking this group.
   - Set `group.Status = "Reversed"`, `group.IsConfirmed = false`.
   - Insert `ReconciliationEvent { EventType = "MatchReversed", MatchLevel = group.MatchLevel,
     Details = $"Reversed by user {reversedByUserId}: {reason}" }` — mirrors `RejectMatchAsync`'s
     existing `"MatchRejected"` event exactly.
   - `SaveChangesAsync` once, at the end, so a failure partway rolls back everything.
4. **Endpoint** on the controller chosen in step 1:
   ```csharp
   public record ReverseMatchRequest(string Reason);

   [HttpPost("{id:guid}/reverse")]
   [RequirePermission("ADMIN.RECONCILIATION.CONFIRM")]   // reuse the existing confirm/reject permission
   public async Task<ActionResult<ReversalResult>> ReverseMatch(
       Guid id, [FromBody] ReverseMatchRequest request, CancellationToken ct)
   ```
   Require non-blank `Reason`, same validation `RejectMatchAsync` already applies.
5. **Frontend**: an "Reverse" action on confirmed match-group cards (Matcher UI), gated behind a
   confirmation dialog requiring the reason text — mirror however the existing Reject dialog is
   built (find it in `finrecon360-frontend`, don't design a new pattern).
6. **Tests** (see §9 for the specific scenarios) — likely a new
   `ReconciliationMatchReversalServiceTests.cs` alongside the existing worker test files, following
   their pattern (in-memory/test `TenantDbContext`, seed a confirmed+posted group, call the
   service, assert on resulting state).

## 7. Blast radius — confirmed not to affect anything else

This is additive only: a new service, a new endpoint, one new nullable column on `JournalVoucher`.
It does not rename or repurpose any `MatchStatus` value already in use, and it doesn't touch the
tenant registration/onboarding flow, the Transactions API, or the import pipeline
(upload/parse/map/validate/commit) — the only surfaces the `scenario-seeder` tool
(`scenario-seeder/`, repo root) depends on. Existing tooling and tests built against those surfaces
are unaffected. A reversed group's records simply re-enter the normal matching waterfall on the
next cycle, the same as any freshly-imported record.

## 8. Open questions for whoever picks this up

- Which controller (§5) is actually wired to the frontend — `MatchGroupsController` or
  `ReconciliationController`? Needs a frontend-side grep to answer; not resolved here.
- Does `JournalEntry.EntryType` need a formal `"Reversal"` literal recognized elsewhere (reports,
  exports) or is it purely descriptive today? Check `Controllers/Admin/ReconciliationReportsController.cs`
  and any GL export code for switch/filter statements keyed on `EntryType`.
- Should `ReconciliationMatchedRecord` rows be hard-deleted on reversal, or soft-marked? Check
  whether anything else queries that join table expecting historical (not just current) links —
  if reporting ever shows "what was this record matched to before it got reversed," hard-delete
  would lose that.
- Confirm `MatchGroupMetadata.TransactionId` is in fact always populated at group-creation time
  for every Level4 tier (Tier1/Tier2/ambiguous-etc.), not just Tier2 where `CardCashoutPromoter`'s
  own comment calls it "required." If any Level4 path creates a group without it, the fallback
  amount-proximity lookup (`|t.Amount - group.MatchedAmount| < 0.01m`, hardcoded not
  tenant-configurable, per `CardCashoutPromoter`) would need to be reused for demotion too, and it
  is a *weaker* signal than the forward-direction lookup — worth confirming rather than assuming.
- Confirm permission name: this plan assumes `ADMIN.RECONCILIATION.CONFIRM` covers reversal too
  (reusing confirm/reject's existing gate). If reversal should be a stricter, separate permission
  (e.g. requiring a controller/finance-lead role rather than whoever can confirm matches day to
  day), that's a product decision, not a technical one — flag it rather than assume.

## 9. Testing plan

Cover, at minimum, one scenario per branch actually exercised by §6 step 3:

- Reverse a **confirmed, not-yet-journal-posted** group (e.g. a Level6 group, which per current
  code never auto-posts) → records back to `PENDING`, group `Status = "Reversed"`, no journal
  voucher touched (there isn't one), event logged.
- Reverse a **confirmed, journal-posted** Level4 Tier1 group → reversing voucher created and sums
  to zero with the original, linked transaction demoted `JournalReady → NeedsBankMatch` with a
  `TransactionStateHistory` row, records back to `PENDING`.
- Reverse a **confirmed, journal-posted** Level7 Tier1/2 group (posted via `PosSettlementPoster`
  at creation time, not via `JournalPostingExecutorWorker`) → same journal-reversal path, no
  transaction to demote (Level7 has none).
- Attempt to reverse the **same group twice** → second call is a no-op or explicit
  already-reversed error, not a duplicate reversing voucher (`ReversesVoucherId == null` guard).
- Attempt to reverse with a **blank reason** → rejected the same way `RejectMatchAsync` rejects a
  blank rejection reason today.
- Reverse a group while `JournalPostingExecutorWorker`'s posting for the same transaction is
  simulated as in-flight (§4.4) — confirms the fresh re-read inside the transaction actually
  prevents the race rather than just documenting that it should.
