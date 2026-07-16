# Day 22 Trip edit and pricing verification evidence

> Evidence state: close-out complete. The reviewed runtime, regression, cleanup, and consolidated
> build/test matrix passed from the same checkout on 2026-07-16 (Asia/Bangkok).

## Reproducible entry points

| Scope | Command | Current result |
|---|---|---|
| Runner syntax | `node --check scripts/run-day22-trip-edit-pricing-local.mjs` | PASS — 2026-07-16 |
| Runner interface | `node scripts/run-day22-trip-edit-pricing-local.mjs --help` | PASS — 2026-07-16 |
| Task-22.0 static gate | `node scripts/run-day22-trip-edit-pricing-local.mjs --static-only` | PASS (gate only; process ends `DIAGNOSTIC/DEFERRED`) — 2026-07-16 |
| Isolated Gateway + live Parcel duplicate-cancellation proof + focused regression + Day-21 regression | `node scripts/run-day22-trip-edit-pricing-local.mjs` | PASS — included in the close-out run on 2026-07-16 |
| Full .NET/TS/static close-out | `node scripts/run-day22-trip-edit-pricing-local.mjs --full-matrix` | PASS — exit 0 in 626.1 s on 2026-07-16 |

The close-out TRX totals were Shared Libraries 81/81, Identity 388/388, Trip 571/571,
Booking 459/459, Payment 88/88, and Parcel 175/175. The focused .NET matrix passed
Trip 185/185, Booking 122/122, and Payment 23/23. Notification focused unit/E2E and the
complete Nx build/lint/test matrix for 10 TypeScript projects passed. The runner stopped only the
nine application containers during the heavy matrix (reducing PostgreSQL connections from 59 to
6), then restarted all nine and waited for every health check to pass.

Prerequisites for either runtime command are Docker, Node/npm, .NET 8, a healthy application
profile from `infra/docker/docker-compose.yml`, applied migrations, and local development JWT
configuration. The runner defaults to Gateway `http://localhost:3000`; override only with
`GATEWAY_BASE_URL`. It reads the existing development signing key at runtime, prints no token or
key material, and commits no secret.

## Static Task-22.0 gate

The `--static-only` run completed without starting the stack and proved:

- The commit named `docs: freeze day 22 trip edit contracts` changes only its approved contract,
  plan, and schema-comment artifacts. `AGENTS.md` is neither tracked nor staged nor in that commit.
- The complete `db-schema/trip-route-vehicle/schema.sql` patch is exactly the whitelisted removal
  of the old static-baseline comment plus addition of the approved static planned-ETA comment. No
  other addition/deletion, DDL, column, constraint, model-snapshot, or migration operation exists.
- The affected contracts retain to-the-dong/pass-through Money semantics and contain no affirmative
  floor-to-1000 rule.
- The contracts contain explicit-pricing precedence `MANUAL_OVERRIDE` → active half-open template
  → `Trip.baseFare`, create no new `TEMPLATE_SNAPSHOT`, reserve `departureDateTime` for
  DriverSchedule `ALL_PENDING`, scope passenger ownership to the Day-22 flows, preserve existing
  route-change behavior, and tolerate physical Hangfire duplicates while deduplicating logically.
- Both cumulative Postman JSON artifacts parse successfully.

## Acceptance-to-evidence map

| Acceptance area | Deterministic evidence owner | Result |
|---|---|---|
| Auth/authz/key before MVC; reserved body/query `422` replay | Live Gateway phase in `run-day22-trip-edit-pricing-local.mjs` | PASS |
| Changed body/path/subject mismatch before MVC | Live Gateway phase | PASS |
| Query key reorder, empty vs absent, repeated order, changed/invalid `applyTo` replay, `/crew` path-only mismatch | Live Gateway phase | PASS |
| Trip and DriverSchedule same-value no-op; persisted schedule and Trip/DriverSchedule audit/Outbox counts unchanged | Live Gateway phase plus direct logical DB assertions | PASS |
| Trip PATCH exact-dong scalar edit, trim, one audit; `departureDateTime` rejected | Live Gateway phase | PASS |
| Explicit-pricing capture; old/new Booking snapshots/refund; omitted legacy callers | Focused `CreateBookingCommandHandlerTests`, `CreateRoundTripBookingCommandHandlerTests`, `TripServiceClientTests`, and Trip `GetTripSnapshotPricingTests` | PASS |
| `MANUAL_OVERRIDE` → active template → base; half-open boundaries | Trip pricing/unit and persistence tests in focused matrix | PASS |
| No new `TEMPLATE_SNAPSHOT`; only explicit override writes `MANUAL_OVERRIDE` | Trip generation/pricing/source tests in focused matrix | PASS |
| Route impact, local races, every seat compatibility cell | `EditTrip*` and `TripVehicleSwap*` controlled tests | PASS |
| FUTURE_ONLY, ALL_PENDING, alias, locks/revalidation, validUntil/isActive, batch rollback | `UpdateDriverSchedule*` controlled tests | PASS |
| Static planned ETA recomputation and too-late boundary | DriverSchedule handler/endpoint controlled tests | PASS |
| CONFIRMED-only MINOR informational event without pending fields | Booking schedule-change unit/integration tests | PASS |
| CONFIRMED-only MEDIUM/MAJOR required event with pending fields/deadline | Booking schedule-change unit/integration tests | PASS |
| Non-CONFIRMED emits neither schedule fact | Booking schedule-change unit/integration tests | PASS |
| Day-removal Trip cancellation exact payload and Booking ownership | Trip/Booking cancellation tests | PASS |
| PENDING_PAYMENT zero refund; CONFIRMED persisted-total refund | Focused Booking cancellation/refund-calculator tests plus Payment cancelled-consumer, refund-handler/job, and wallet-refund tests | PASS |
| Parcel cancellation idempotency | Mandatory live runner phase: same `trip.trip.cancelled` payload/EventId published twice, Parcel queue ACK/drain, one `PENDING_PAYMENT` → `REJECTED`, one rejection Outbox, one rejected-stat increment | PASS |
| Crash-window/DLQ schedule repair before ACK | Booking vehicle/schedule consumer integration tests | PASS |
| Duplicate physical jobs tolerated; locked deterministic re-alert side effect unique | `PendingActionRealertJobTests` and Hangfire registration/integration tests | PASS |
| Seat and MEDIUM/MAJOR re-alert discriminants; mismatch rejected | Booking and Notification focused tests | PASS |
| Passenger/crew ownership; no direct Trip schedule/cancel passenger duplicate | Notification consumer/mapper/module-binding tests | PASS |
| Existing route-change registry/consumer unchanged and green | Complete Notification project test invoked by focused matrix | PASS |
| Event/audit uniqueness and consumer redelivery | Trip/Booking/Notification controlled tests; Parcel duplicate-delivery uniqueness is the live runner proof | PASS |
| Day-21 lifecycle regression | Existing `scripts/run-day21-trip-lifecycle-local.mjs`, invoked by default | PASS |
| Full .NET restore/build/format/TRX-tested six-solution matrix and Nx build/lint/test `--parallel=3 --exclude=VietRide.*` | `--full-matrix` hook mirroring `.github/workflows/ci.yml` | PASS |

The controlled suites are intentionally used for exhaustive matrix cells, concurrency barriers,
fault injection, and clock boundaries. The live phase is reserved for public Gateway middleware and
transactional smoke evidence; it does not expose an internal endpoint or add a test backdoor.

## Postman artifacts

The cumulative collection adds `Operator - Day 22 trip edit and schedule cascade`. Its requests are
limited to:

- `PATCH /v1/operator/trips/{tripId}`;
- `PATCH /v1/operator/driver-schedules/{id}?applyTo=...`;
- deprecated `PATCH /v1/operator/driver-schedules/{id}/crew`.

The environment adds placeholders only. The authoritative runner generates runtime-only UUID-v4
keys, fixture ids, and JWTs; no internal `/internal/v1/*` request appears in the Day-22 folder.

## Logical Hangfire evidence rule

Physical Hangfire job count is not an assertion. A broker redelivery or commit-to-ensure repair may
schedule more than one physical job. Verification must instead prove that each execution locks and
rechecks the unresolved pre-deadline action and that the deterministic re-alert Outbox identity
derived from `pendingActionId` persists exactly one durable Outbox/Notification side effect.

When the runtime matrix is executed, record the pending-action id, logical Outbox/event id, final
Outbox status, and durable Notification dedupe row; do not paste tokens, RabbitMQ credentials, or a
physical Hangfire job count.

## Cleanup evidence template

The runner seeds a unique Trip dependency graph and records every UUID-v4 idempotency key it owns.
Its `finally` block deletes matching seeded-id/event-type Trip and Parcel Outbox rows,
Trip/DriverSchedule audit rows, ParcelStats and Parcel fixtures, then Trips, DriverSchedules, Route,
Vehicle, Stations, VehicleType, and exact Redis keys. It then independently includes all those
database categories in the zero-count assertion.

| Cleanup check | Result | Evidence |
|---|---|---|
| Success-path fixture cleanup | PASS | Close-out line: `PASS | Day-22 fixture cleanup verified` |
| Failure-path cleanup | PASS | Two non-zero full-matrix diagnostic executions still emitted the same cleanup PASS line before the final green run |
| Day-21 nested-run cleanup | PASS | Close-out line: `PASS | Day-21 fixture cleanup verified` |

## Reviewer close-out record

| Item | Value |
|---|---|
| Checkout/commit | `feat/day22-trip-edit-pricing` at pre-artifact HEAD `6d87aa1`; this file is delivered by the following Task-22.13 commit |
| Runtime date/timezone | 2026-07-16; Asia/Bangkok |
| Application image ids | Recorded below |
| `--full-matrix` exit | 0; 626.1 s |
| Day-21 regression exit | 0; fixture cleanup PASS |
| Route-change Notification regression | PASS; focused Notification unit and E2E suites |
| Cleanup result | PASS on success and observed failure paths; all nine application containers restored healthy |
| Carry-over | None |

Application image ids used by the live phase:

- `gateway`: `sha256:7b566d685d54c40f0883f6eeb26268d53283c13b42f49c2f79d4166f5d9b3c47`
- `identity`: `sha256:e4d709d036a23c12eaf80d750c55910f713baa371e3df0cddce275ff9943f0ae`
- `trip`: `sha256:e5d7da31dac2e37197663467b7c768b0e934c67c1b593cd99ccb27a472b0cbe2`
- `booking`: `sha256:3e3cc9d68d001c9142bef76ef5b071b16eb67422057ce4cbdbded69bd18211d7`
- `payment`: `sha256:7d0130820d8a5905d802f534600d5c647521f46a6a6cf6833c0173690f0c1083`
- `parcel`: `sha256:6d65672013c38e50038e253182a29e395a3e6de56b72f88a08b807c0ebee09a3`
- `tracking`: `sha256:079e457110136867ed43908e69bcdd158be994b18df75de2c0281d12722b08dd`
- `notification`: `sha256:c997f7e8b30dd48874bf40bc9f357b2330e1230070e02fab825aae3474f02774`
- `rag`: `sha256:ee6471ecc3f90b7abbf54c31cf54df1d5809699bbf7f3171e2ca6e9f80a28583`
