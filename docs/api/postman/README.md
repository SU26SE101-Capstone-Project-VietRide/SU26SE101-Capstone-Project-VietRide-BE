# VietRide — Postman collection

This is the **single cumulative** Postman collection for VietRide — the graded deliverable the
external reviewer runs (`BE_TIMELINE_VU.md`: _"external reviewer runs full Postman collection
without errors"_). It also doubles as the **tier-5 real-app E2E** for `/audit-day` and `/verify`.

- `vietride.postman_collection.json` — the collection, organized by domain folders. **Grow this file
  per PR** (timeline: _"update Postman collection"_); do **not** add per-day `day-N-*.json` files.
- `vietride.local.postman_environment.json` — local environment: `baseUrl=http://localhost:3000`
  plus per-run placeholders. Externally-supplied secrets (`googleIdToken`,
  `systemAdminAccessToken`) are placeholders — fill them at run time, never commit a real token.

## Run with Newman (CLI)

```bash
# bring the stack up first (see /audit-day tier 4 or /smoke-test)
npx newman run docs/api/postman/vietride.postman_collection.json \
  -e docs/api/postman/vietride.local.postman_environment.json
```

Day-6 operator onboarding needs local-only OTP / SET_INITIAL_PASSWORD token lookup because those
secrets are intentionally not returned by production API responses. For a self-contained local
Day-6 audit run, use the helper wrapper instead of pasting tokens manually:

```bash
node scripts/run-day6-newman-local.js
```

The helper binds only `127.0.0.1`, reads the local dev database, mints a short-lived SYSTEM_ADMIN
JWT from the dev Identity key, and passes `localHarnessEnabled=true` to Newman. The helper requests
inside the cumulative collection are skipped unless that variable is enabled, so the normal full
collection remains runnable with externally supplied secrets/placeholders.

Or import both files into the Postman app (Collection + Environment) and run the folders.

## Notes

- Requests hit the **Gateway** (`:3000`) using the real resource-prefixed routes
  (`/v1/auth/...`, `/v1/users/...`, `/v1/admin/...`) — see `apps/gateway/src/config/routes.ts`.
- Flows needing a real external credential (e.g. the Google OAuth path needs a real `googleIdToken`)
  are **SKIP** in an audit when that credential is unavailable — see the `/audit-day` Review-bullet
  scoring rule.
- Redact tokens/secrets when pasting run output into a checklist or PR.
