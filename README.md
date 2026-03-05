# FinRecon360

Monorepo for the FinRecon360 frontend, backend, and SQL project.

Quick links:
- Developer setup and local seeding: `DEVREADME.md`
- Backend API contract and usage notes: `finrecon360-backend-master/finrecon360-backend/README.md`
- Frontend details: `finrecon360-frontend/README.md`

Notes:
- Secrets are not committed. Configure local env vars using `.env.example` in the backend folder.
- This repo no longer uses git submodules; the frontend is tracked directly.
- Temporary local bypass is available to seed an active tenant admin (without Stripe checkout) for development. See `DEVREADME.md` and backend README for setup and rollback steps.
- Frontend unit tests use a dedicated test environment (`mockApi: true`) and are not controlled by backend temp bypass flags.
