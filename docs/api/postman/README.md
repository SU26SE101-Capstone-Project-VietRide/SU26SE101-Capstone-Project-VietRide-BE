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

Day-7 station/stop adversarial cases are covered by the deterministic local harness below, so the
collection no longer depends on pre-supplied reviewer values for the required cross-operator and
non-approved checks:

```bash
node scripts/run-day7-newman-local.js
# or
npm run postman:day7:local
```

The helper seeds local-only Identity/Trip data, mints short-lived JWTs from the dev Identity key,
and provides the required variables at runtime. Never commit real token values.

Day-8 route/route-stop/fare-template/alternative-route adversarial cases are covered by a matching
local harness. It seeds deterministic local Identity/Trip records, mints short-lived JWTs in-process,
and runs only the cumulative collection's Day-8 folder through the Gateway (`http://localhost:3000`):

```bash
node scripts/run-day8-newman-local.js
# or
npm run postman:day8:local
```

The Day-8 helper seeds/mints the folder's required runtime values automatically, including
`operatorAdminAccessToken`, `operatorUserAccessToken`, `nonApprovedOperatorAccessToken`,
`operatorId`, `day8OriginStationId`, `day8DestinationStationId`,
`day8AlternativeDestinationStationId`, `day8MissingStationId`, `day8StopId`,
`day8SecondStopId`, and `day8CrossOperatorRouteId`. Never commit real token values.

Day-9 vehicle/driver-schedule adversarial cases use the same local-harness pattern. It seeds
approved + second-operator Identity/Trip data, mints short-lived JWTs in-process, and runs only the
cumulative collection's Day-9 folder through the Gateway (`http://localhost:3000`):

```bash
node scripts/run-day9-newman-local.js
# or
npm run postman:day9:local
```

The Day-9 helper supplies `operatorAdminAccessToken`, `operatorUserAccessToken`,
`day9OtherOperatorAccessToken`, `operatorId`, `day9RouteId`, `day9CrossOperatorVehicleId`,
`day9StandardVehicleTypeId`, `day9UnknownVehicleTypeId`, `day9DriverUserId`, and
`day9AssistantUserId`. The folder verifies the 3 system VehicleType seed rows (45/9/40), Vehicle
happy path and validation errors, tenant-hidden Vehicle reads, and DriverSchedule conflict handling.
Never commit real token values.

Day-11 trip search/detail/seat-map audit coverage uses a deterministic local harness. It seeds
approved Identity users plus prerequisite Trip config data only (Saigon → Can Tho stations,
operator stations, route, stop/fare template, vehicle seats A01/A02, and an inactive Monday
DriverSchedule), mints short-lived JWTs in-process, runs only the Day-11 activation/search/detail/
seat-map folder through the Gateway (`http://localhost:3000`), then verifies the activation-
generated Trip DB side effects after Newman:

```bash
node scripts/run-day11-newman-local.js
# or
npm run postman:day11:local
```

The Day-11 helper supplies `operatorAdminAccessToken`, `passengerAccessToken`,
`day11DriverScheduleId`, `day11OriginStationId`, `day11DestinationStationId`, and
`day11MissingStationId`, plus a runtime `day11DepartureDate` aligned to the next local ICT service
day. The folder stays Gateway-only: it activates the seeded DriverSchedule,
performs public trip search plus an empty-result adversarial search, and reads trip detail/seat-map
as a passenger. After Newman, the harness polls for exactly one activation-generated scheduled/
boarding Trip with generated A01/A02 seats, trip stops, and stop fares, then calls Trip service
internal endpoints directly (`http://localhost:5002` by default) with `X-Internal-Auth` to verify
lock/release/book/unavailable seat semantics. The harness prints `Day-11 generation evidence:` and
`Day-11 internal seam evidence:` lines for checklist handoff; JWTs and lock tokens are not printed
in full. Never commit real token values.

If you run the Day-8 folder manually without the helper, provide equivalent local values:

- `operatorAdminAccessToken` — a valid `OPERATOR_ADMIN` JWT for an `APPROVED`, active operator.
- `operatorUserAccessToken` — a valid operator user JWT for the same approved operator.
- `nonApprovedOperatorAccessToken` — a valid operator JWT for a non-`APPROVED` or inactive operator;
  the request must return exact `403 FORBIDDEN`.
- `operatorId` — the approved operator id that owns the Day-8 test data.
- `day8OriginStationId` and `day8DestinationStationId` — two active Station ids available to the
  approved operator; the origin/destination equality case must return `422`.
- `day8AlternativeDestinationStationId` — an active Station id used by AlternativeRoute create cases.
- `day8MissingStationId` — a syntactically valid Station id that does not exist; the request must
  return exact `404 STATION_NOT_FOUND`.
- `day8StopId` and `day8SecondStopId` — active Stop ids owned by the approved operator and valid for
  the Day-8 route-stop / alternative-route flow.
- `day8CrossOperatorRouteId` — a Route id owned by another operator; the request must return exact
  `404 ROUTE_NOT_FOUND`.

Or import both files into the Postman app (Collection + Environment) and run the folders.

The Day-17 carry-over and Day-18 driver folders run through Gateway on `{{baseUrl}}`. Run Day 18
reproducibly against the local Docker stack with `npm run postman:day18:local`. The helper seeds two
trips assigned to one driver plus a CONFIRMED booking/passenger, mints short-lived development
DRIVER/PASSENGER JWTs, runs Newman, and verifies the persisted BOARDED side effect without printing
tokens or secrets. For a manual run, provide valid `passengerAccessToken`,
`operatorAdminAccessToken`, `driverAccessToken`, and (when testing the same flow as an assistant)
`assistantAccessToken`.
Day 17 also needs a confirmed `day17BookingId`. Day 18 needs assigned `day18TripId` and
`day18OtherTripId` fixtures, plus `day18PassengerRecordId` and `day18BookingCode` from a confirmed
booking on the assigned trip. `day18OtherTripId` must identify a different trip assigned to the
same driver or assistant; otherwise the wrong-trip cases stop at authorization with `403` instead
of reaching the intended `422 BOOKING_NOT_FOR_THIS_TRIP`. The committed environment contains
placeholders only; never commit real JWTs or fixture secrets.

## Day 21 trip lifecycle

Run the deterministic Day-21 lifecycle verification against a healthy local stack from the
repository root:

```powershell
npm run postman:day21:local
```

The helper creates unique origin/destination Stations, Route, VehicleType, Vehicle, an isolated
`BOARDING` Trip assigned to a driver and assistant, four Booking fixtures (`CONFIRMED`,
`PARTIAL_NO_SHOW`, `NO_SHOW`, and `CANCELLED`), and one `LOADED` Parcel. It never borrows
pre-existing fixtures. It generates short-lived JWTs and UUID-v4 idempotency keys at runtime,
sends every public lifecycle action through Gateway `:3000`, and never prints credentials.
Bounded database polling is used only to verify transactional Outbox rows and eventual
Parcel/Booking consumer effects. The runner also publishes one duplicate `trip.trip.completed`
delivery and waits for acknowledgement plus drain on every bound queue before proving consumer
idempotency. In `finally` it removes and verifies its full database fixture graph and every exact
Redis `trip:idem:<uuid>` record it generated. Any assertion or cleanup failure exits non-zero.

The collection folder `Driver - Day 21 trip lifecycle` is an importable manual view of the two
public no-body endpoints. Manual execution requires a disposable already-`BOARDING` fixture plus
runtime values for `day21TripId`, `day21DriverAccessToken`, `day21AssistantAccessToken`, and fresh
UUID-v4 start/complete idempotency keys. The committed environment contains placeholders only; the
authoritative reproducible path is the helper command above. Deterministic in-flight pending
behavior is covered by Task 21.1's controlled middleware integration test, and job timing
boundaries are covered by Task 21.4's fake-clock integration tests; this live runner does not use
timing races, clock overrides, or job-control backdoors.

## Day 22 Trip editing, pricing, cascades, and cancellation

The cumulative collection now contains `Operator - Day 22 trip edit and schedule cascade`, a
Gateway-only manual view of the public Trip and DriverSchedule PATCH contracts. It deliberately
does not publish internal Booking-impact or Trip-snapshot endpoints. Manual execution needs a
disposable `SCHEDULED` Trip and DriverSchedule owned by the runtime `OPERATOR_ADMIN` token; replace
all `day22*` placeholders with fresh values and UUID-v4 keys.

The authoritative reproducible entry point is the deterministic local runner:

```powershell
# Prerequisite: current Docker application profile is healthy and migrations are applied.
docker compose -f infra/docker/docker-compose.yml --profile app up -d --build

# Diagnostic Task-22.0 artifact/schema/contract gate; no containers required and never a close-out PASS.
node scripts/run-day22-trip-edit-pricing-local.mjs --static-only

# Gateway + live Parcel duplicate-event proof + focused tests + Day-21 regression + cleanup;
# this diagnostic mode does not produce a close-out PASS without the full matrix.
node scripts/run-day22-trip-edit-pricing-local.mjs

# Reviewer close-out: also run all six .NET solutions and the complete Nx matrix.
node scripts/run-day22-trip-edit-pricing-local.mjs --full-matrix
```

The runner creates a unique Route/Vehicle/Trip/DriverSchedule graph and short-lived local JWTs at
runtime. Public requests go only through `GATEWAY_BASE_URL` (default `http://localhost:3000`).
Direct database access is limited to deterministic setup, bounded logical-effect evidence, and
cleanup. The runner also publishes the same `trip.trip.cancelled` payload twice with one `EventId`
through RabbitMQ, waits for both Parcel acknowledgements and a drained queue, then proves the one
supported `PENDING_PAYMENT` → `REJECTED` transition, one rejection Outbox event, and one rejected
statistics increment. A `finally` block removes and verifies every owned row and exact Redis
idempotency key on both success and failure. Its zero-count proof includes seeded Trip dependencies, Trip audits,
DriverSchedule audits, Parcel/ParcelStats rows, and matching seeded-id/event-type Outbox rows;
tokens and development signing material are never printed or written.

The live Gateway phase proves authentication/authorization/key checks precede MVC, reserved
body/query `422` replay, body/path/subject/query fingerprint mismatches, query-key canonicalization,
empty-versus-absent and repeated-value ordering, no-op behavior, exact-dong scalar Trip edits,
Trip-PATCH departure rejection, invalid-`applyTo` reserved/replayed `422`, DriverSchedule persisted
no-op state plus unchanged Trip/DriverSchedule audit and Outbox counts, and a `/crew` reuse whose
query/body fingerprint is otherwise identical so only the path differs. Focused controlled test
projects cover the complete pricing precedence/window matrix,
legacy omitted-pricing behavior, seat compatibility matrix and races, FUTURE_ONLY/ALL_PENDING batch
rules, static ETA recomputation, Booking creation/client snapshot capture,
Booking schedule/cancellation ownership, Payment cancellation/refund behavior,
crash/DLQ scheduling repair, and locked deterministic pending-action re-alert execution.
Parcel cancellation idempotency is owned by the mandatory live RabbitMQ phase described above,
not by the focused test projects.

Hangfire assertions are intentionally logical: the durable pending-action row and deterministic
Outbox/Notification side effect derived from `pendingActionId` must be unique. Physical duplicate
Hangfire jobs are permitted and are never counted as a failure. The focused Notification run also
reruns existing route-change registry/consumer tests unchanged, and the Day-21 runner is executed
as a mandatory regression unless a developer explicitly supplies `--skip-day21` for diagnosis.
Every filtered .NET hook writes a deterministic TRX result and fails if its filter executes zero
tests or reports any failure. `--static-only`, `--skip-targeted`, and `--skip-day21` are diagnostic
modes and cannot produce close-out PASS; `--full-matrix` rejects either skip flag and mirrors the
six-solution and TypeScript CI command matrix. After the live and focused phases, the full-matrix
mode temporarily stops only the nine application containers to release their database pools. Its
`finally` path restarts exactly those containers, waits for every health check, and leaves
Postgres, PgBouncer, Redis, and RabbitMQ running throughout.

Record reviewer output in
[`docs/handoff/evidence/day-22-trip-edit-pricing.md`](../../handoff/evidence/day-22-trip-edit-pricing.md).
The close-out rows in that file must match an exit-zero `--full-matrix` execution from the same
checkout.

## Day 23 schedule-change journey

The collection folder `Day 23 - Schedule change journey` is a Gateway-only manual companion to
the focused runner. It contains the canonical DriverSchedule `ALL_PENDING` producer and passenger
`/resolve` route; it intentionally contains no dedicated Trip schedule route, `/accept` or
`/reject` alias, internal clock route, or job-control route. Runtime JWTs and fixture identifiers
are placeholders. Use only isolated local fixtures and remove all owned rows/side effects after a
manual run.

```powershell
node --check scripts/run-day23-schedule-change-local.mjs
node --test --test-reporter=tap scripts/run-day23-schedule-change-local.test.mjs
node scripts/run-day23-schedule-change-local.mjs --focused
```

`--focused` validates both Postman JSON artifacts and the retained Task 23.3-23.8 TRX/Jest
manifest, then runs a concrete isolated journey. It seeds unique Identity/Trip/Booking fixtures,
issues short-lived runtime JWTs, calls DriverSchedule `ALL_PENDING` and passenger `/resolve` only
through the Gateway, and inspects bounded PostgreSQL Outbox/Notification plus RabbitMQ queue state.
The live matrix asserts exact PENDING_PAYMENT/MINOR/MEDIUM/MAJOR action/event cardinality, frozen
refund metadata, an explicit lock-handshake idempotency race whose first request must settle before
fixture cleanup, and a >24-hour deadline-precision
case. Its `finally` path tracks and removes the complete owned DB/Redis graph and proves zero rows/keys after both
success and partial-setup/runtime failure. It never waits for a business deadline. Exhaustive
two-hour equality, projection CAS/quarantine, timeout equality/phases/races, restart, and RabbitMQ
MessageId behavior remain owned by the real-PostgreSQL/frozen-clock suites delivered in Tasks
23.3-23.8. This preserves the production clock and avoids a hidden test endpoint. See
[`docs/handoff/evidence/day-23-schedule-change.md`](../../handoff/evidence/day-23-schedule-change.md)
for the acceptance-to-evidence map.

To execute the authoritative local E2E matrix in dependency order, use this exact command from the
repository root:

```powershell
npm run postman:full:local
```

The required cumulative stages are Sprint-3 D11 through D19 followed by Day 21; their exact
folder/harness, seam mode, assertions, fixture ownership, and exclusion policy live in
[`docs/handoff/day-20-e2e-matrix.md`](../../handoff/day-20-e2e-matrix.md). It starts the application
profile, runs real seams where documented, temporarily enables the documented Booking development
stubs where required, restores real integrations, and runs Day 21 last. The command exits non-zero if any required
stage is missing, fails, or is skipped without a recorded human-approved exclusion. It creates and
cleans its own local fixtures, so an external reviewer does not need hidden fixture IDs, JWTs, or
pre-populated Postman environment values. Google OAuth is outside the Sprint-3 matrix and still
requires a real external Google ID token.

For the reviewer-facing Sprint-3 sequence and the VNPay execution boundary, see
[`docs/handoff/sprint-3-demo-script.md`](../../handoff/sprint-3-demo-script.md). The VNPay coverage
uses a signed local IPN simulation; it is not a real bank or merchant-sandbox transaction.

## Notes

- Requests hit the **Gateway** (`:3000`) using the real resource-prefixed routes
  (`/v1/auth/...`, `/v1/users/...`, `/v1/admin/...`) — see `apps/gateway/src/config/routes.ts`.
- Flows needing a real external credential (e.g. the Google OAuth path needs a real `googleIdToken`)
  are **SKIP** in an audit when that credential is unavailable — see the `/audit-day` Review-bullet
  scoring rule.
- Redact tokens/secrets when pasting run output into a checklist or PR.
