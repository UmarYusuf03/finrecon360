# PayHere Integration Debugging — Full Context

## Project
`finrecon360-backend` (ASP.NET Core 8 / C#) + `finrecon360-frontend` (Angular), in `/Users/umairhassan/Desktop/current`. PayHere (Sri Lankan payment gateway) is used for subscription billing checkout.

## Original symptom
Clicking "Change Plan" in the app redirected the user to PayHere's public marketing homepage (`payhere.lk`) instead of a payment form.

---

## Bug #1 — FIXED: GET vs POST (this was the actual cause of the homepage-redirect symptom)

**Root cause:** PayHere's `/pay/checkout` endpoint only reads form-POST fields. The code was building a GET query-string URL and navigating the browser straight to it (`window.location.href = checkoutUrl`). PayHere silently ignores GET requests to that path and falls through to serving its homepage — no error, no redirect status.

**Confirmed by:** curling the identical field set as GET (bounces to `payhere.lk` homepage) vs POST (renders PayHere's real "Pay with PayHere" checkout page).

**Fix applied (NOT yet committed to git):**
- `finrecon360-backend/Services/PayHereCheckoutService.cs` — `CreateCheckoutSessionAsync` now caches the checkout field set in `IMemoryCache` (20 min TTL) keyed by orderId. `TryGetCheckoutLaunchHtml` (previously a stub always returning `false`) now renders that cached data as a real auto-submitting `<form method="POST" action="...">`.
- `finrecon360-backend/Services/PaymentCheckoutService.cs` — `CreateCheckoutSessionAsync` now returns `/api/payments/payhere/checkout/{orderId}` (the previously-dead `PayHereCheckoutController.LaunchCheckout` route) instead of the raw PayHere GET URL.
- `finrecon360-backend/Program.cs` — added `builder.Services.AddMemoryCache();`.
- `finrecon360-backend.Tests/PayHereCheckoutServiceTests.cs` — updated constructor calls for the new `IMemoryCache` dependency, added tests for the launch-HTML behavior.
- No frontend changes needed — `admin-subscription.ts`'s `resolveCheckoutUrl` already handled relative URLs correctly.
- **Verified:** 195/195 backend tests pass. Confirmed live in browser — checkout now actually reaches PayHere's real payment page instead of bouncing home.

## Bug #2 — FIXED: plan currency silently dropped

**Root cause:** `PaymentCheckoutService.CreateCheckoutSessionAsync` accepted a `currency` parameter (from `plan.Currency`) but never passed it anywhere. `PayHereCheckoutService.CreateCheckoutSessionAsync` didn't even have a currency parameter — it always hardcoded `PAYHERE_CURRENCY` from `.env` (`LKR`), silently ignoring whatever the plan's actual currency was.

**Discovered by:** querying the live dev database directly (`FinRecon360` on `localhost,1433`) — Starter/Growth/Enterprise plans had `Currency=USD` even though an earlier migration (`20260511150000_UpdatePlansCurrencyToLKR`) had already run against this exact database (confirmed via `dotnet ef migrations list`, not "pending"). Someone re-entered them as USD afterward, likely through a plan-editing screen.

**Fix applied:**
- `IPayHereCheckoutService.CreateCheckoutSessionAsync` now takes an optional `string? currency = null` parameter that overrides the `.env` default when provided.
- `PaymentCheckoutService` now passes `currency` through instead of dropping it.
- New migration `finrecon360-backend/Migrations/20260827120000_UpdatePaidPlansCurrencyToLKR.cs` — applied to the dev DB — relabels Starter/Growth/Enterprise to `Currency='LKR'` (same relabel-only approach as the original migration, no FX conversion, per user's explicit choice when asked).
- Added 2 new tests for the currency plumbing. All 195 tests still pass.

**⚠️ Flagged, NOT resolved — needs a decision:** PriceCents on those plans (4900 / 14900 / 49900) were clearly set with USD amounts in mind ($49/$149/$499). Relabeling only the currency to LKR (no conversion) means Starter is now literally **Rs. 49.00** — a few US cents. This is almost certainly not the intended real-world price. Needs a business decision on what the actual LKR prices should be, then a data update (not something I should decide unilaterally).

---

## Bug #3 — UNRESOLVED: "Unauthorized payment request" (this is the live blocker)

After fixing Bug #1, checkout correctly reaches PayHere's real payment page — but PayHere itself rejects the request with **"Unauthorized payment request"** ("This is a merchant's error. Please inform this error to your Merchant to get it resolved.").

### Account details
- Merchant ID: `1235636`, business name "finrecon360", sandbox dashboard at `sandbox.payhere.lk`
- `.env` at `finrecon360-backend-master/finrecon360-backend/.env`
- `PAYHERE_MERCHANT_SECRET_MODE="Base64"` — secret is stored base64-encoded in `.env`, decoded before hashing
- `PAYHERE_CHECKOUT_BASE_URL="https://sandbox.payhere.lk/pay/checkout"`
- `PAYHERE_NOTIFY_URL` is currently an ngrok tunnel (`https://leverage-baggie-circular.ngrok-free.dev/api/webhooks/payhere`) — ngrok free-tier URLs rotate on restart, will need updating if the tunnel was restarted
- Current `PAYHERE_MERCHANT_SECRET` in `.env` is the 3rd regenerated secret, tied to the `localhost` Domain-type entry

### Exhaustive elimination testing performed (all via curl replicating the real C# code's exact output, byte-for-byte, plus one real-browser test)

| # | Test | Result |
|---|---|---|
| 1 | Correct hash vs. deliberately wrong hash | **Byte-identical response** (6631 bytes both times) — hash is never actually validated |
| 2 | Hash field omitted entirely | Same identical response as #1 |
| 3 | Deliberately wrong `merchant_id` | **Different, specific error**: "Merchant ID is incorrect for the Sandbox environment" — confirms 1235636 IS recognized as valid |
| 4 | Omitting `notify_url` | **Different error with a code**: "Something went wrong. Error code: 031327082647" — proves PayHere's system does emit codes for some failures; "Unauthorized payment request" is a separate, code-less category |
| 5 | 3 separately-generated secrets across 2 domain registrations (`localhost` Domain-type ×2 secrets, `127.0.0.1:5279` App-type ×1 secret — both shown Active) | Identical rejection every time |
| 6 | PayHere's own official sample data verbatim (samanp@gmail.com, 0771234567, "Saman Perera", real address) instead of placeholders | Identical rejection — rules out field validation |
| 7 | Amount varied Rs. 49 to Rs. 5000 | No effect |
| 8 | Referer/Origin headers — with port, bare hostname exact match, absent entirely | No effect |
| 9 | Session cookie (GET first to capture one, then POST with it) | PayHere doesn't even issue a cookie on GET; no effect |
| 10 | `notify_url` = real ngrok URL vs. generic `https://example.com/notify` | Identical — **rules out ngrok as the cause** |
| 11 | `/pay/authorize` (separate API, same merchant_id/hash mechanism) | Identical "Unauthorized payment request" |
| 12 | Official JS SDK (`payhere.startPayment()`, real browser, iframe-based — architecturally different from raw form POST) | **Identical rejection** |
| 13 | Settings → API Keys (App ID/App Secret) | Confirmed via official docs: unrelated OAuth credential system for a different API family (Charging/Capture/Subscription/Retrieval), not applicable here |
| 14 | Manually-created Payment Link (`sandbox.payhere.lk/pay/of9ce3c0c`) via dashboard | **WORKS** — HTTP 200, real payment page renders, no error. Proves the account itself can process payments |
| 15 | Payment Link amount/tracking override via URL params (`?amount=`, `?custom_1=`, `?order_id=`) | No effect — links are fully static per-link, not usable for dynamic per-subscription billing |

### Conclusion
Every payment-initiation method using **our own** merchant_id + hash (Checkout API, Authorize API, JS SDK) fails identically — regardless of which of 3 secrets/2 domain registrations, regardless of transport (raw POST vs. real browser SDK). Only PayHere's own self-generated Payment Links work. This points at an **account-level flag on PayHere's side**, unrelated to domain, hash, credentials, or our code. Not visible or fixable via any dashboard area explored (Integrations/Domains, Settings/API Keys, Account, Payment Links).

### Support message (drafted, ready to send — check with user whether it was sent)

```
Subject: Sandbox Checkout API / Authorize API / JS SDK all rejecting with "Unauthorized payment request" — account and Payment Links work fine

Merchant ID: 1235636 (sandbox, business name "finrecon360")
Reference for lookup: x-correlation-id: seBSkv6wv1, order_id FINALREFTEST1, Thu, 27 Aug 2026 07:35:13 GMT

Stack: ASP.NET Core 8 (.NET 8) / C# backend, server-side hash generation, MD5 per your documented algorithm:
hash = to_upper_case(md5(merchant_id + order_id + amount + currency + to_upper_case(md5(merchant_secret))))

Issue: Every payment initiation method we've tried returns "Unauthorized payment request":
- Form POST to https://sandbox.payhere.lk/pay/checkout
- Form POST to https://sandbox.payhere.lk/pay/authorize
- The official JavaScript SDK (payhere.startPayment(), loaded from https://www.payhere.lk/lib/payhere.js, sandbox: true) — tested in a real browser, not simulated

By contrast, a manually-created Payment Link (sandbox.payhere.lk/pay/of9ce3c0c) works correctly and renders a real payment page.

Diagnostics performed:
1. Correct hash vs. deliberately wrong hash → byte-identical response — hash is never actually validated.
2. Deliberately wrong merchant_id → different, specific error ("Merchant ID is incorrect for the Sandbox environment") — confirms 1235636 is recognized.
3. Omitting notify_url → different error entirely ("Something went wrong. Error code: 031327082647") — confirms your system emits specific codes for some failures, just not this one.
4. 3 separately-generated Merchant Secrets across 2 Domain/App integrations (localhost Domain-type, 127.0.0.1:5279 App-type), both Active — identical rejection.
5. Your own documented sample customer data used verbatim — identical rejection, rules out field validation.
6. Varied amount, Referer/Origin, notify_url (ngrok vs. generic HTTPS vs. omitted), session cookies — no effect.

Ask: Every merchant-authenticated payment method (Checkout API, Authorize API, JS SDK) is rejected before any request content is even evaluated, while PayHere-generated Payment Links work fine on the same account. This strongly suggests an account-level flag gating merchant-initiated payment requests specifically — separate from whatever "Active" Domain/App status reflects. Can you check what's different about this account relative to one where Checkout API works normally? The correlation ID above should locate the exact rejected request in your logs.
```

### PayHere support's reply

From Yasith Chandula, Bhasha Support:

```
The error has occurred due to either an invalid hash code or an unrecognized domain.

Please verify the following.

Hash Generation Accuracy
Ensure that the hash is being generated correctly at the time of sending the payload. The parameter values used for generating the hash must exactly match the parameter values included in the payload.

Correct Domain Usage
Confirm that the PayHere Payment Gateway is being initiated from the same domain registered in the Integration section of your PayHere Merchant Portal.

Valid Merchant Secret
Ensure that you are using the correct Merchant Secret associated with the registered domain. To avoid formatting issues, use the copy button next to the Merchant Secret when copying it.

Referer Header Presence
We have received reports from other merchants where the Referer header was not present in the request. Please note that PayHere relies on the Referer header to validate requests against the configured domain.
Ensure that the Referer header is included in your request headers.
Avoid using HTML meta tags such as,
<meta name="referrer" content="no-referrer" />
<meta name="referrer" content="no-referrer-when-downgrade" />
as these may suppress the Referer header.
If the referrer is not present in the request header, PayHere will look for the notify_url domain to determine the origin. Setting the notify_url domain to match the request origin URL fixed the issue, confirming that the problem was related to the referrer.

Single Domain Record Enforcement
Please ensure that only one record per domain exists in the Integration section of the PayHere Merchant Portal. Multiple entries for the same domain are not supported and may lead to validation issues.

--
Regards,
Yasith Chandula
Bhasha Support
```

### Our follow-up reply (drafted — confirm whether sent)

```
Thanks for the detailed checklist — I went through all four points specifically:

1. Hash accuracy — reconfirmed correct, byte-for-byte, using the exact algorithm you specified.

2. Domain usage — found and removed a duplicate localhost record, exactly matching your point about single-record enforcement. Rejection was identical before and after removing it.

3. Merchant Secret — reconfirmed fresh via the copy button each time. Tested with 4 separate secrets across 4 different domain/app registrations total (localhost ×2, 127.0.0.1:5279, and localhost:5279 — the exact host:port my request actually originates from).

4. Referer header — tested extensively with an explicit, exactly-matching Referer and Origin header (http://localhost:5279) set on the request. Identical "Unauthorized payment request" with or without it present, and regardless of which of the 4 registered domains' secret is used.

All four combinations, with and without a matching Referer, return the exact same rejection — before any request content appears to be evaluated (confirmed separately: a deliberately wrong hash returns a byte-identical response to a correct one, and a deliberately wrong merchant_id returns a completely different, specific error, so the account is clearly being identified correctly). Could someone check server-side whether there's an account-level flag blocking this merchant from initiating payments, separate from domain/app registration status? Happy to provide a fresh correlation ID from a live request if that helps you look it up.
```

### Still not done (once Bug #3 is resolved)
- Full payment → webhook (`PayHereWebhooksController`) → subscription activation flow has never been tested end-to-end (no checkout has ever succeeded to trigger it).
- Plan pricing decision from Bug #2 (see above) still needs resolving.
- Bug #1 and #2 code changes are uncommitted — ask before committing/pushing.

### Key files
- `finrecon360-backend-master/finrecon360-backend/Services/PayHereCheckoutService.cs`
- `finrecon360-backend-master/finrecon360-backend/Services/PaymentCheckoutService.cs`
- `finrecon360-backend-master/finrecon360-backend/Controllers/Payments/PayHereCheckoutController.cs`
- `finrecon360-backend-master/finrecon360-backend/Controllers/Webhooks/PayHereWebhooksController.cs`
- `finrecon360-backend-master/finrecon360-backend/Options/PayHereOptions.cs`
- `finrecon360-backend-master/finrecon360-backend/Controllers/Admin/AdminSubscriptionController.cs`
- `finrecon360-backend-master/finrecon360-backend/Services/SubscriptionService.cs`
- `finrecon360-frontend/src/app/main/pages/admin/admin-subscription.ts`
- `finrecon360-backend-master/finrecon360-backend/.env`
- `finrecon360-backend-master/finrecon360-backend.Tests/PayHereCheckoutServiceTests.cs`
- `finrecon360-backend-master/finrecon360-backend/Migrations/20260827120000_UpdatePaidPlansCurrencyToLKR.cs`
- DB connection (dev, local SQL Server in Docker): `Server=localhost,1433;Database=FinRecon360;User Id=sa;Password=19884@Zcc;TrustServerCertificate=True;`
