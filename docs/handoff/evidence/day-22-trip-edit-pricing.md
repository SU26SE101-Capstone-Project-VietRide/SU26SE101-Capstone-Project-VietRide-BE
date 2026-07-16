# Day 22 Trip edit and pricing verification evidence

> Evidence state: close-out complete. The reviewed runtime, regression, cleanup, and consolidated
> build/test matrix passed from the same checkout on 2026-07-16 (Asia/Bangkok).

## Reproducible entry points

| Scope | Command | Current result |
|---|---|---|
| Runner syntax | `node --check scripts/run-day22-trip-edit-pricing-local.mjs` | PASS — 2026-07-16 |
| Runner interface | `node scripts/run-day22-trip-edit-pricing-local.mjs --help` | PASS — 2026-07-16 |
| Task-22.0 static gate | `node scripts/run-day22-trip-edit-pricing-local.mjs --static-only` | PASS (gate only; process ends `DIAGNOSTIC/DEFERRED`) — 2026-07-16 |
| Isolated Gateway pricing/refund + live Parcel duplicate-cancellation proof + focused regression + Day-21 regression | `node scripts/run-day22-trip-edit-pricing-local.mjs` | PASS — included in the close-out run on 2026-07-16 |
| Full .NET/TS/static close-out | `node scripts/run-day22-trip-edit-pricing-local.mjs --full-matrix` | PASS — exit 0 in 734.6 s on 2026-07-16 |

The close-out TRX totals were Shared Libraries 81/81, Identity 388/388, Trip 573/573,
Booking 459/459, Payment 88/88, and Parcel 175/175. The focused .NET matrix passed
Trip 187/187, Booking 122/122, and Payment 23/23. Notification focused unit/E2E and the
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
| Explicit-pricing capture; old/new Booking snapshots/refund; omitted legacy callers | Real Gateway old/new/cancel/refund flow, focused Booking client/handler tests, and PostgreSQL `GetTripSnapshotRelationalTests` for explicit/omitted `pricingAt` | PASS |
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
fault injection, and clock boundaries. The live phase covers public Gateway middleware plus the
transactional old/new Booking and asynchronous refund chain; it exposes no internal endpoint and
adds no test backdoor.

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

The runner seeds unique Identity, Wallet, Trip, Booking, Payment, Notification, and Parcel fixtures
and records every UUID-v4 idempotency key and runtime Booking id it owns. Its `finally` block removes
owned rows and Outbox/ledger/audit side effects across all six databases, restores the exact pre-run
PlatformWallet balance/version, deletes exact Redis keys, and independently asserts every category
is clean.

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
| `--full-matrix` exit | 0; 734.6 s |
| Day-21 regression exit | 0; fixture cleanup PASS |
| Route-change Notification regression | PASS; focused Notification unit and E2E suites |
| Cleanup result | PASS on success and observed failure paths; all nine application containers restored healthy |
| Carry-over | None |

Application image ids used by the live phase:

- `gateway`: `sha256:41e12426207c5cc287b668353046de97e0fc44393af84e445f6c0bc17f0e6ad7`
- `identity`: `sha256:89e5c68aae0e499ac0571236fb9029a686b6dc493904438cb41330055829c7b9`
- `trip`: `sha256:e314e7adf9ce34321d8cfedb1f368c9deee3d88e16f7f53a67691424ba8bb61d`
- `booking`: `sha256:105bcce3635a522256958271d1a7a69e1f3e009d66e13174d9b42ba691341775`
- `payment`: `sha256:3d91def4f4fe7caad9819f8a6c2351fca13a0878e6405e8028bde1f68b76ee33`
- `parcel`: `sha256:c6fb4c70eae30e1716ae6b2e28fff815fe42c1f02489cefcaaf5e6ae94db0e1d`
- `tracking`: `sha256:2f7246a56cf9f6f1c27451ecadf2f0dcf581eced4928157d17e6d7dc468b1bd5`
- `notification`: `sha256:e55088079db8d9fa4d2c09d953015cf68f90c0c612f4f0a736de6fd85ed7c9a0`
- `rag`: `sha256:590809435cbceb90fb328014b612f6f55300068990ad4609ab5eacf0c4f83f39`
