Brevo (Sendinblue) setup notes for finrecon360-backend

1) Create a dedicated API key
- Go to Brevo dashboard > SMTP & API > API keys.
- Create a new API v3 key for transactional email usage.
- Name it something like `finrecon360-backend-server`.
- Paste the key into the backend `.env` as `BREVO_API_KEY`.

2) Verify sender email or domain
- In Brevo dashboard > Senders & domains, verify the email address or domain you will use as `BREVO_SENDER_EMAIL`.
- Verifying the domain is preferred (reduces deliverability issues).

3) Approve security alerts and allowlisting
- Brevo may send a security email to the sender address when an API key is used from a new/unknown IP.
- Open that email and approve the activity, or sign into Brevo dashboard > Security to view recent activity.
- If you have a static server IP, consider allowlisting it in Brevo settings.

4) Use separate keys per environment
- Use separate API keys for development, staging, and production.
- Do NOT reuse a production key in local development.

5) Recommended dashboard checks
- Check `Transactional > Events` to see API call details and errors.
- If you see blocked requests, the event will contain the Brevo error JSON which can be correlated with backend logs.

6) Troubleshooting from backend
- Enable backend logs (console or file) and look for `Brevo email failed` warnings; they now include Brevo's response body.
- If you see messages about "unknown IP" or "security alert", follow the approve/allowlist steps above and rotate the key if needed.

7) Optional: SMTP fallback
- If desired, implement an SMTP fallback (less preferred) to another provider. Prefer REST API for template support.

If you'd like, I can add a short README snippet to the root `SETUP.md` linking to this file.
