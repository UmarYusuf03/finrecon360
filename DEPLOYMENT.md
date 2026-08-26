# FinRecon360 — Demo Deployment Guide

**Goal:** get the whole system running on a public HTTPS URL for demo day, at **zero cost**, with
no functionality removed.

This is a **demonstration deployment**, not a production one. That distinction is what makes it
free: it lets us use SQL Server Developer Edition (free, licensed for dev/test only) and student
cloud credit instead of paid tiers.

---

## 1. The shape of it

Everything runs on **one Ubuntu virtual machine**, served from **one hostname**.

```
                 https://finrecon360.example.com
                              │
                          ┌───▼────┐
                          │ nginx  │   :80 / :443  (public)
                          └───┬────┘
              ┌───────────────┴───────────────┐
              │                               │
        /  (everything)                    /api/*
              │                               │
      ┌───────▼────────┐            ┌─────────▼─────────┐
      │ Angular files  │            │ Kestrel (.NET 8)  │  127.0.0.1:5000
      │ /var/www/…web  │            │ systemd service   │  (localhost only)
      └────────────────┘            └─────────┬─────────┘
                                              │
                                    ┌─────────▼─────────┐
                                    │ SQL Server 2022   │  127.0.0.1:1433
                                    │ Developer Edition │  (localhost only)
                                    └───────────────────┘
```

### Why one VM instead of managed services

These are not preferences — they are constraints the code imposes. Each one rules out the
"obvious" cheap PaaS option:

| What the code does | Why PaaS breaks | Why the VM is fine |
|---|---|---|
| Runs **5 always-on background workers** (`Program.cs:187-191`), concurrency-guarded by an in-process dictionary | Free App Service tiers idle the app after ~20 min; scaling past 1 instance double-runs every worker | systemd never idles a service, and one instance is guaranteed |
| Calls **`CREATE DATABASE` at runtime** to provision each tenant | Managed DB services (incl. Azure SQL free tier) forbid this | We own the SQL instance |
| Writes uploads to **`App_Data/imports/{tenantId}`** on local disk (`ImportsController.cs:812`) | PaaS filesystems are ephemeral | A real directory on a real disk |
| Uses **hand-written T-SQL migrations** (`COL_LENGTH`, `sys.indexes`, filtered indexes) | Not portable to Postgres/MySQL, so Heroku/Render/Supabase are out | Full SQL Server engine |

### Why one origin is worth insisting on

Serving the SPA and the API from the same hostname isn't just tidier — it deletes three pieces
of work:

- `environment.production.ts` already ships `apiBaseUrl: ''`. Under same-origin that is
  **correct, not a bug** — the browser calls `/api/...` on its own host and nginx proxies it.
  **Leave it as an empty string.**
- **CORS does not apply at all.** There is no cross-origin request to permit.
- **One** origin to register with Google, and nginx's `try_files` handles SPA deep-link fallback.

---

## 2. Cost: $0

| Component | Source | Cost |
|---|---|---|
| Virtual machine | [Azure for Students](https://azure.microsoft.com/en-us/free/students) — **$100 credit, no credit card**, verify with university email or GitHub Student Pack | $0 out of pocket |
| SQL Server 2022 | **Developer Edition** — free, full Enterprise feature set, licensed for dev/test | $0 |
| TLS certificate | Let's Encrypt via certbot | $0 |
| Domain name | Free subdomain (DuckDNS / nip.io), or the free `.me` from GitHub Student Pack | $0 |
| Transactional email | Brevo free tier (300 emails/day) | $0 |
| Payments | PayHere **sandbox** — and see §5, the demo path avoids it entirely | $0 |

**Credit burn:** a B2s VM is roughly **$30–40/month** if left running (check the pricing
calculator for your region — Southeast Asia or Central India are nearest to us).

> **Deallocate the VM when you are not using it.** Azure bills compute only while the machine
> runs. Stopping it between work sessions takes the burn to roughly **$8/month**, so the $100
> comfortably covers the rest of the project. Use **Standard HDD**, not Premium SSD — storage
> bills whether the machine runs or not.

### Sizing

**Do not pick a 2 GB VM.** SQL Server refuses to install below 2 GB and needs headroom above
that, before Kestrel and nginx get anything. **B2s (2 vCPU / 4 GB)** is the realistic floor.

---

## 3. How the work splits

Four tracks. **A and B need no cloud account and can start immediately** — they're the ones to
hand off first.

| Track | What it is | Blocked by | Can start now? |
|---|---|---|---|
| **A** | Code changes for deployability | nothing | ✅ yes |
| **B** | Accounts + external service registration | nothing | ✅ yes |
| **C** | Building the server | A + B | after A, B |
| **D** | Verification + demo rehearsal | C | after C |

---

## TRACK A — Code changes

> **Owner:** ______  **No cloud account needed.** Do this in a branch and PR it as normal.

### A1. Connection strings → SQL authentication ⚠️ blocking

This is the **one genuinely blocking code change**. Both connection strings currently use
`Trusted_Connection=True`, which authenticates as a logged-in *Windows* account. There is no
Windows account on an Ubuntu VM, so the app cannot connect at all until this changes.

These live in `.env` (not committed), so the change is to `.env.example` and to the deployed
`.env`:

```dotenv
ConnectionStrings__DefaultConnection="Server=localhost;Database=FinRecon360;User Id=finrecon_app;Password=<secret>;TrustServerCertificate=True;"
TENANT_DB_TEMPLATE="Server=localhost;Database=FinRecon360_Tenant_{tenantId};User Id=finrecon_app;Password=<secret>;TrustServerCertificate=True;"
```

`TrustServerCertificate=True` is acceptable here because the connection never leaves localhost.

### A2. Handle the reverse proxy ⚠️ gotcha

`Program.cs:456` calls `app.UseHttpsRedirection()` in Production. Behind nginx, Kestrel only ever
sees plain HTTP on `127.0.0.1:5000`, so the app cannot tell that the *user's* request was HTTPS.
Depending on configuration this either silently no-ops or issues redirects that break every API
call through the proxy. Don't leave it to chance.

Add forwarded-headers handling **before** `UseHttpsRedirection()`:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // The only proxy in front of us is nginx on this same machine.
    KnownNetworks = { }, KnownProxies = { }
});
```

(`using Microsoft.AspNetCore.HttpOverrides;`)

The nginx config in Track C sets `X-Forwarded-Proto` to match.

### A3. Persist DataProtection keys

`Program.cs:193` calls `AddDataProtection()` with no persisted key store, so keys land in the
service user's home directory — which a systemd service user may not have. Point it at a real
path so a restart doesn't regenerate them:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("finrecon360-backend")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/www/finrecon-api/keys"));
```

Low risk either way (auth is JWT-based), but it removes a class of confusing post-restart bugs.

### A4. Confirm the production build is clean

```bash
cd finrecon360-frontend && npm run build
```

```bash
cd finrecon360-backend-master && dotnet test
```

Both are currently green (**178/178 tests**). Keep them that way — the size-budget *warnings* in
the frontend build are pre-existing and non-fatal.

### ✅ Track A is done when

- [ ] `.env.example` uses SQL auth in both connection strings
- [ ] Forwarded headers handled before HTTPS redirection
- [ ] DataProtection keys persisted to a real path
- [ ] `dotnet test` and `npm run build` both pass

---

## TRACK B — Accounts and external services

> **Owner:** ______  **No code needed.** Collect the values into a shared secure note; Track C
> pastes them into `.env`.

### B1. Azure for Students

Sign up at <https://azure.microsoft.com/en-us/free/students>. Verify with the university email;
if that's rejected, verify with the GitHub Student Developer Pack instead. **No credit card is
required** — and that's what guarantees we cannot be accidentally billed.

Deliverable: an active subscription with $100 credit.

### B2. A hostname

Pick one and record it. Google sign-in **will not work without HTTPS**, and HTTPS needs a name:

- Free subdomain: DuckDNS, or `nip.io` (which needs no registration — `<ip>.nip.io` just resolves)
- Or the free `.me` domain from the GitHub Student Pack

Deliverable: a hostname we control, e.g. `finrecon360.duckdns.org`.

### B3. Google OAuth client

In Google Cloud Console → *APIs & Services* → *Credentials*, on the existing OAuth client, add
the new origin to **Authorised JavaScript origins**:

```
https://<our-hostname>
```

Keep `http://localhost:4200` in the list so local development still works.

The client ID is **not a secret** (it's served to the browser), so it's fine to share within the
team.

Deliverable: `GOOGLE_CLIENT_ID`.

### B4. Brevo (email)

Magic-link onboarding is how a tenant's first user sets their password — tenant registration is
not demonstrable without working email. Brevo's free tier (300/day) is plenty.

Deliverable: `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SENDER_NAME`, and the three template
IDs (`MAGICLINK_VERIFY`, `MAGICLINK_RESET`, `MAGICLINK_CHANGE`).

### B5. PayHere sandbox — *optional, see §5*

Only needed to demonstrate **paid** checkout. The free TRIAL plan does not touch PayHere at all.

Deliverable (if doing it): `PAYHERE_MERCHANT_ID`, `PAYHERE_MERCHANT_SECRET`, and confirmation of
whether the secret is raw or base64 (`PAYHERE_MERCHANT_SECRET_MODE` — getting this wrong silently
breaks both checkout hashing *and* webhook verification).

### ✅ Track B is done when

- [ ] Azure subscription active
- [ ] Hostname chosen and controllable
- [ ] Google origin registered
- [ ] Brevo key + template IDs collected
- [ ] All values in a shared secure note (**not** committed to git)

---

## TRACK C — Build the server

> **Owner:** ______  **Needs:** Track A merged, Track B values in hand.

### C1. Create the VM

- **Image:** Ubuntu Server 22.04 LTS
- **Size:** B2s (2 vCPU, 4 GB)
- **Disk:** Standard HDD, 32 GB
- **Public IP:** Static (keeps the hostname and webhook URL stable across restarts)
- **Inbound ports:** **22, 80, 443 only**

> ⚠️ **Never open 1433.** SQL Server must stay bound to localhost. If you need SSMS from your
> laptop, tunnel it: `ssh -L 1433:localhost:1433 user@<vm-ip>`

Then point the Track B hostname at the VM's public IP (an `A` record).

### C2. Install SQL Server 2022 Developer Edition

```bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | sudo gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list | sudo tee /etc/apt/sources.list.d/mssql-server-2022.list
sudo apt-get update && sudo apt-get install -y mssql-server
```

Set it up **non-interactively as Developer edition** — free, and unlike Express it has no 10 GB
per-database cap and no 1.4 GB memory ceiling, which matters because every tenant gets its own
database:

```bash
sudo MSSQL_PID=Developer ACCEPT_EULA=Y MSSQL_SA_PASSWORD='<strong-sa-password>' /opt/mssql/bin/mssql-conf -n setup
```

Install the command-line tools:

```bash
curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/prod.list | sudo tee /etc/apt/sources.list.d/mssql-release.list
sudo apt-get update && sudo ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
```

### C3. Create the application login

The app needs `dbcreator` — and **only** `dbcreator` — because tenant provisioning issues
`CREATE DATABASE` at runtime. There is no reason to run as `sa`.

```sql
CREATE LOGIN finrecon_app WITH PASSWORD = '<strong-app-password>';
ALTER SERVER ROLE dbcreator ADD MEMBER finrecon_app;
```

> The app creates the control-plane database itself on first run and grants itself ownership of
> what it creates. No manual database or table creation is needed — see C6.

### C4. Install the .NET 8 ASP.NET Core runtime

The project targets **`net8.0`**. Install the **8.0** runtime specifically — a newer SDK on a dev
machine does not mean the VM gets the right runtime.

```bash
sudo apt-get install -y aspnetcore-runtime-8.0
```

### C5. Publish the API

Build on your machine and copy the output up (the VM doesn't need the SDK):

```bash
dotnet publish finrecon360-backend-master/finrecon360-backend -c Release -o ./publish
```

Copy `./publish` to `/var/www/finrecon-api` on the VM, then create the `.env` file **in that same
directory**.

> ⚠️ **`.env` must live in the service's working directory.** `Program.cs:33` loads it from
> `Directory.GetCurrentDirectory()`, and uploads go to `App_Data/` relative to the same place.
> The systemd `WorkingDirectory=` below is what makes both resolve correctly.

Lock it down — it holds the SA-adjacent DB password, the JWT signing key, and the Brevo key:

```bash
sudo chown finrecon:finrecon /var/www/finrecon-api/.env && sudo chmod 600 /var/www/finrecon-api/.env
```

### C6. Run it under systemd

A unit file is what gives us restart-on-failure and start-on-boot — which is what keeps the five
background workers alive.

`/etc/systemd/system/finrecon-api.service`:

```ini
[Unit]
Description=FinRecon360 API
After=network.target mssql-server.service

[Service]
WorkingDirectory=/var/www/finrecon-api
ExecStart=/usr/bin/dotnet /var/www/finrecon-api/finrecon360-backend.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=finrecon-api
User=finrecon
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now finrecon-api
sudo journalctl -u finrecon-api -f
```

**On first start the app migrates and seeds itself** (`Program.cs:432-441`): it creates the
control-plane schema, seeds permissions and components, seeds the **four subscription plans**
(TRIAL / STARTER / GROWTH / ENTERPRISE), and creates the system admin from `SYSTEM_ADMIN_EMAIL` /
`SYSTEM_ADMIN_PASSWORD`. Watch the logs to confirm it completes without error.

### C7. Build the SPA and put nginx in front

```bash
cd finrecon360-frontend && npm run build
```

Copy `dist/finrecon360-frontend/browser/` to `/var/www/finrecon-web` on the VM.

`/etc/nginx/sites-available/finrecon360`:

```nginx
server {
    listen 80;
    server_name <our-hostname>;

    root /var/www/finrecon-web;
    index index.html;

    # Bank statement / POS imports are multi-MB. nginx defaults to 1 MB and would
    # reject them with a 413 before the API ever sees the request.
    client_max_body_size 25M;

    location /api/ {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }

    # SPA fallback: without this, refreshing on any deep link returns 404.
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/finrecon360 /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

### C8. TLS

```bash
sudo apt-get install -y certbot python3-certbot-nginx
sudo certbot --nginx -d <our-hostname>
```

certbot rewrites the nginx config for 443 and sets up auto-renewal. **This is required, not
cosmetic** — Google Identity Services refuses to run over plain HTTP.

### C9. Final `.env` values

Once the hostname and TLS are live, make sure these point at the real origin:

```dotenv
FRONTEND_BASE_URL="https://<our-hostname>"
```

> `FRONTEND_BASE_URL` does double duty: it drives the CORS policy (`Program.cs:285-297`) **and**
> it's the base for magic-link URLs in emails. Even though same-origin makes the CORS part
> irrelevant, a wrong value here sends users a broken password-setup link.

If demoing paid checkout, also:

```dotenv
PAYHERE_RETURN_URL="https://<our-hostname>/onboarding/success"
PAYHERE_CANCEL_URL="https://<our-hostname>/onboarding/cancel"
PAYHERE_NOTIFY_URL="https://<our-hostname>/api/webhooks/payhere"
```

`PAYHERE_NOTIFY_URL` is called by PayHere's servers over the public internet, and it's what
actually activates a paid tenant.

### ✅ Track C is done when

- [ ] `https://<hostname>` loads the login page
- [ ] `systemctl status finrecon-api` shows active/running
- [ ] Logs show migration + seeding completed
- [ ] Only 22/80/443 open; 1433 not reachable externally

---

## TRACK D — Verify and rehearse

> **Owner:** ______

### D1. Walk the whole path on the deployed instance

**This run has never happened anywhere** — not locally, not in CI. Do it on the VM, not on a
laptop, and do it before demo day rather than during it.

1. [ ] Register a tenant → **choose the TRIAL plan**
2. [ ] Receive the magic link by email, set a password
3. [ ] Confirm the tenant database was actually created (`SELECT name FROM sys.databases`)
4. [ ] Sign in with **Google SSO**
5. [ ] Sign in with **email + password**
6. [ ] Create a bank account
7. [ ] Create a transaction **with a reference number**, approve it
8. [ ] Upload a bank statement, run it through import → map → validate → commit
9. [ ] Confirm a match group on the matcher screen
10. [ ] Confirm a journal entry appears
11. [ ] Open the reports (Trial Balance, General Ledger) and check they're populated

Anything that breaks here is a **finding, not a failure** — it's exactly what this rehearsal is
for. Log it and fix it while there's still time.

### D2. Demo-day operational checklist

- [ ] VM started (if you've been deallocating it) — **allow 5 minutes**, SQL Server takes time
- [ ] `systemctl status finrecon-api` green
- [ ] Certificate not expired (`sudo certbot certificates`)
- [ ] Demo tenant + demo data seeded and re-checked
- [ ] Sample import files on hand
- [ ] Fallback tunnel tested (§4)

---

## 4. Fallback: Cloudflare Tunnel

Worth setting up regardless. A **Cloudflare Tunnel** gives the app running on your own laptop a
public HTTPS address — free, through NAT, no port forwarding, nothing opened on your router.
Because the address is genuinely public with a real certificate, **Google sign-in and PayHere
webhooks both work through it.**

```bash
cloudflared tunnel --url http://localhost:4200
```

It is **not** a substitute for deploying — it needs your laptop running and on the network. But
it is excellent insurance: if anything is wrong with the VM on the morning of the demo, you
present from the machine you've been building on all along.

Test it once, in advance. A fallback you've never run is not a fallback.

---

## 5. Demo shortcut worth knowing

**The TRIAL plan skips the payment gateway entirely.**

`OnboardingController.cs:175-194` branches on `PriceCents <= 0` and activates the tenant
immediately — no checkout session, no PayHere call, no webhook wait:

> *"Free plans (the trial included) never touch the payment gateway — activate the tenant
> immediately instead of generating a checkout session for $0."*

So the **entire tenant-registration-to-working-system flow is demonstrable with no PayHere
configuration at all.** Track B5 is genuinely optional.

Configure PayHere sandbox only if the paid checkout path is itself part of what you're presenting.

---

## 6. Environment variable reference

Full list for the deployed `.env`. Values marked 🔑 are secret — never commit them (`.env` is
gitignored at `.gitignore:17`, keep it that way).

| Variable | Notes |
|---|---|
| `ConnectionStrings__DefaultConnection` | 🔑 SQL auth, `Server=localhost` |
| `TENANT_DB_TEMPLATE` | 🔑 SQL auth, keep the `{tenantId}` placeholder |
| `Jwt__Key` | 🔑 long random string |
| `Jwt__Issuer` / `Jwt__Audience` / `Jwt__ExpiresMinutes` | defaults are fine |
| `FRONTEND_BASE_URL` | `https://<hostname>` — CORS **and** email links |
| `SYSTEM_ADMIN_EMAIL` / `SYSTEM_ADMIN_PASSWORD` | 🔑 seeded on first run |
| `ADMIN_EMAILS` | comma-separated |
| `BREVO_API_KEY` | 🔑 |
| `BREVO_SENDER_EMAIL` / `BREVO_SENDER_NAME` | |
| `BREVO_TEMPLATE_ID_MAGICLINK_*` | three template IDs |
| `MAGICLINK_*` | expiry / attempts / cooldown — defaults fine |
| `GOOGLE_CLIENT_ID` | not secret; button hides while unset |
| `GOOGLE_HOSTED_DOMAIN` | leave empty to allow any Google account |
| `GOOGLE_ALLOW_AUTO_PROVISIONING` | `true` for the demo |
| `ONBOARDING_TOKEN_*` | defaults fine |
| `PAYHERE_*` | optional — see §5 |
| `PAYMENT_ALLOW_LOCAL_BYPASS` | **must stay `false`** in Production (it is ignored there anyway) |

---

## 7. Known gotchas, collected

Each of these has bitten a deployment like this before. They're all cheap to avoid and expensive
to diagnose:

| # | Gotcha | Consequence if missed |
|---|---|---|
| 1 | `Trusted_Connection=True` | App cannot connect to SQL at all |
| 2 | Missing `WorkingDirectory=` in systemd | `.env` not loaded; uploads land in the wrong place |
| 3 | nginx `client_max_body_size` left at default | Statement imports fail with 413 |
| 4 | No `try_files` fallback | Refreshing any deep link 404s |
| 5 | `UseHttpsRedirection` without forwarded headers | Redirect loops or broken API calls behind the proxy |
| 6 | Port 1433 exposed publicly | SQL Server open to the internet |
| 7 | 2 GB VM | SQL Server won't install |
| 8 | Changing `apiBaseUrl` from `''` | Breaks the same-origin setup that makes everything else simple |
| 9 | Premium SSD | Bills while the VM is deallocated |
| 10 | Forgetting to deallocate | Burns the $100 in ~3 months instead of ~12 |

---

## Sources

- [Azure for Students — free account](https://azure.microsoft.com/en-us/free/students)
- [Azure for Students — offer details](https://azure.microsoft.com/en-us/pricing/offers/ms-azr-0170p)
- [Install SQL Server on Ubuntu](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-ubuntu)
- [SQL Server on Linux — installation guidance and editions](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup)
