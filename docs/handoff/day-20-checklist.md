# Day 20 — Final checklist

> Produced by `/audit-day 20` after an independent source/code audit and fresh verification run.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 20 — Sprint 3 buffer + demo prep (no Jira key)
- **Plan**: `docs/handoff/day-20-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] ✅ Gateway-level passenger E2E is green. Fresh `npm run postman:full:local` exited `0` with `14/14` required execution steps PASS; the run covered D11–D19 through `http://localhost:3000`, including signed VNPay local-IPN top-up/replay, booking payment, cancellation/refund, and operator monitor.
- [x] ✅ The cumulative runner invokes every named Day-11–18 stage and Day-19 monitor in dependency order, rejects absent/unapproved skipped stages, and each runner reports deterministic cleanup. The audit run observed cleanup PASS lines, including D16 retained-trip cleanup and D18 cross-day child-before-parent cleanup.
- [x] ✅ Evidence-led defects are closed with focused Booking implementation/tests and harness-only repairs recorded in `docs/handoff/day-20-e2e-matrix.md`; no unresolved external/pre-existing blocker remains.
- [x] ✅ External-review handoff is reproducible: `docs/api/postman/README.md`, the importable collection/environment, authoritative matrix, and `docs/handoff/sprint-3-demo-script.md` specify the single command and require no hidden fixture IDs or tokens.
- [x] ✅ Sprint-3 demo script is present at the human-approved Markdown destination and covers registration/login, local VNPay IPN simulation, search, booking/payment, cancellation/refund, tenant-scoped monitor, expected command/evidence, and exclusions.

## Tasks completed

- Task 20.0 — Pre-reqs / architecture baseline: freeze Sprint-3 E2E matrix and runner boundaries — ✅
- Task 20.1 — Implement deterministic passenger journey and operator-monitor E2E run — ✅
- Task 20.2 — Evidence-led Sprint-3 bug sweep and regression closure — ✅
- Task 20.3 — Sprint-3 demo deck and external-review handoff — ✅

## Changed files

- `docs/handoff/day-20-e2e-matrix.md` — authoritative D11–D19 coverage, runner modes, fixture ownership, defect evidence, and closure records.
- `scripts/run-full-e2e-local.mjs`, `scripts/run-day20-sprint3-e2e-local.mjs`, and Day-11–19 runners — deterministic full-matrix orchestration, real/stub boundary control, forced-failure cleanup, and redacted PASS/FAIL output.
- `docs/api/postman/vietride.postman_collection.json`, `docs/api/postman/vietride.local.postman_environment.json`, `docs/api/postman/README.md`, `package.json` — executable local collection/handoff with placeholders only and the documented entry points.
- `apps/booking/src/VietRide.Booking.Api/Controllers/BookingsController.cs` and `GetBookingStatus` CQRS files/tests — Booking-owned status poll used by the real VNPay confirmation flow; owner/matching-operator authorization and coded denial behavior covered by tests.
- `VietRide_API_Contract_v1.md` — duplicate booking-poll declaration removed and canonical status-poll ownership documented.
- `scripts/run-day9-newman-local.js`, `scripts/run-day18-crossday-local.mjs` — deterministic prerequisite/cleanup corrections for the D18 cross-day flow.
- `docs/handoff/sprint-3-demo-script.md` — review-ready Markdown demo handoff.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`.
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | Exit `0`; no formatting changes required.
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | Unit `328/328`; integration `48/48`. Initial pre-stack run could not connect to PostgreSQL; rerun after the app stack was healthy passed.
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects plus 3 dependencies succeeded. Existing third-party source-map/webpack warnings were emitted, with exit `0`.
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | Exit `0`; existing Notification lint output has 2 non-null assertion warnings and 0 errors.
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | 17 Jest suites, `74/74` tests passed; projects without tests exited cleanly.
| EF migration apply / rollback | SKIP | No migration was shipped or changed by Day 20; the day changed E2E/handoff plus the Booking poll implementation/contract only.
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | Current code rebuilt/started. Gateway, 5 .NET services, 3 workers, Postgres, Redis, RabbitMQ, and PgBouncer were up/healthy.
| `/health` matrix | PASS | HTTP `200`: Gateway `:3000`; Identity `:5001`; Trip `:5002`; Booking `:5003`; Payment `:5004`; Parcel `:5005`; Tracking `:3001`; Notification `:3002`; RAG `:3003`.
| `npm run postman:full:local` | PASS | Fresh Docker/Gateway execution exit `0`; D11, D12, D13, D14, D15, D16, D17, D18, D19 and D18-crossday all passed; `TOTAL: 14/14 stages passed`.
| Review artifact validation | PASS | Collection/environment are JSON-importable, base URL is `http://localhost:3000`, tokens/secrets are empty/placeholders, and README/demo script name the same runnable command.
| Review execution against Docker/local stack | PASS | The full collection/matrix executed through Gateway, including Day-20 passenger journey legs and Day-19 own-tenant monitor/foreign-tenant denial. D15 observed IPN `200`, replay `200`, invalid signature `401`; D18 observed no-PII manifest, wrong-trip guard, boarded persistence, and cleanup.
| Day-20 Review bullet overall | PASS | An external reviewer can use `npm run postman:full:local` with no hidden fixture state; fresh audit execution completed without errors.
| Hard invariants | PASS | No `.csproj` `PackageReference Version=`, no banned dependency/MediatR v12+, no `Co-Authored-By` in Day-20 commits, and tracked working-tree EOLs conform to `.gitattributes` (C# CRLF; JS/JSON/MD LF).

## Contract / event / schema changes shipped

- Canonical Booking status poll `GET /v1/bookings/{bookingId}` was clarified/implemented for its passenger owner or matching operator tenant; response is Booking-owned `{ bookingId, status }`. Missing/non-owner passenger access is `404 BOOKING_NOT_FOUND`; cross-tenant operator access is `403 FORBIDDEN`.
- No new Gateway route, event-routing key, error code, migration, schema change, or dependency was shipped. Therefore no BSOT registry/changelog addition was required for Day 20.

## Known gaps & carry-over for Day 21

- No Day-20 implementation or verification gap blocks Day 21.
- VNPay evidence is intentionally a signed local-IPN simulation, not a real bank transaction or merchant-sandbox account. This is documented in the matrix/demo script and does not exclude any required Sprint-3 stage.

## Notes for Day 21 planning

- Keep `npm run postman:full:local` as the regression entry point when touching Booking, Payment, Trip, Gateway auth/proxy, or shared E2E seams.
- The final runner restores real Booking mode after stub-backed stages and cleans its fixtures; do not rely on direct invocation of an individual stub-stage runner after a real-seam stage.
