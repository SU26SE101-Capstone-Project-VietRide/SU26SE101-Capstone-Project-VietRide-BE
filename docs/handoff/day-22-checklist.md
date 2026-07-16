# Day 22 — Final checklist

> Produced by `/audit-day 22` after an independent source/code audit, a rebuilt Docker stack,
> the complete static matrix, and a fresh manual attempt at the timeline Review flow.
> The initial audit exposed a production runtime failure. The remediation described below fixes
> that failure and reruns the complete close-out matrix, including the real Gateway pricing/refund flow.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 22 — Trip edit snapshot + Pricing rules
- **Plan**: `docs/handoff/day-22-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] ✅ Canonical `PATCH /v1/operator/trips/{tripId}` is limited to `baseFare`, `notes`, `vehicleId`, and `routeId`, uses UUID-v4 idempotency and the field-aware lifecycle matrix, and preserves no-op semantics. The rebuilt-stack runner observed `200` for same-value and real scalar edits, with no no-op audit/event and exactly one `TRIP_EDITED` audit for the real change; exhaustive handler/endpoint tests are green.
- [x] ✅ Booking captures and sends one explicit `pricingAt`; the production Trip snapshot repository now uses its mapped `(TripId, StopId)` composite key and no longer orders by the ignored `Id`. Real PostgreSQL tests cover both explicit and omitted `pricingAt` paths.
- [x] ✅ The rebuilt Gateway flow completes `book old fare → edit fare → book new fare → cancel old → refund`: the old Booking remains `200000`, the new Booking is `211111`, and Payment refunds exactly the original persisted `200000` total.
- [x] ✅ Route changes are tenant-scoped, reject `PENDING_PAYMENT|CONFIRMED` impact and local HELD/BOOKED races, and otherwise rebuild stops/fares/planned ETA atomically. Controlled Trip unit/integration coverage is green.
- [x] ✅ Vehicle-swap compatibility, HELD/BOOKED/BOARDING conflict precedence, strict deadline, seat preservation, and Booking-owned `PENDING_SEAT_ASSIGNMENT` behavior pass the focused and full Trip/Booking suites.
- [x] ✅ Full DriverSchedule PATCH requires `applyTo`; `FUTURE_ONLY`, `ALL_PENDING`, alias, query-aware fingerprinting, deterministic preflight/locks, and atomic rollback pass controlled tests. The live runner observed `200` no-op/replay behavior and `422 VALIDATION_ERROR` for `ALL_PENDING + vehicleId:null`.
- [x] ✅ The day-removal cancellation implementation, duplicate Parcel delivery, and the real asynchronous Booking→Payment refund leg all pass with exact ledger, Outbox, and immutable Booking snapshot assertions.
- [x] ✅ Trip, Booking, Payment, Parcel, Notification, shared contracts, Gateway, Day-11, and Day-21 regressions pass together with the tier-5 business E2E and verified fixture cleanup.

## Tasks completed

- Task 22.0 — Freeze HTTP, event, audit, pricing, and cascade contracts — ✅ source and static artifact boundary audit passed.
- Task 22.1 — Booking Trip-edit impact seam — ✅ implementation and Booking/Trip tests passed.
- Task 22.2 — Trip notes and Trip/DriverSchedule audit persistence — ✅ migration and tests passed.
- Task 22.3 — Fare source and database overlap guard — ✅ migration apply/down/reapply, pending-model, and constraint tests passed.
- Task 22.4 — Booking pricing clock and immutable money snapshots — ✅ controlled and real Gateway old/new/refund snapshot flows passed.
- Task 22.5 — Effective fare resolution and no new template snapshots — ✅ composite-key repository fix and real PostgreSQL explicit/omitted-pricing tests passed.
- Task 22.6 — Shared TypeScript Day-22 contracts — ✅ focused contracts and full Nx matrix passed.
- Task 22.7 — Vehicle-swap lock primitives and mutation service — ✅ controlled unit/integration matrix passed.
- Task 22.8 — Canonical Trip PATCH — ✅ live Gateway scalar/no-op/idempotency checks and controlled matrix passed.
- Task 22.9 — Seat-reassignment actions and Booking Hangfire re-alert — ✅ controlled unit/integration and logical-dedupe checks passed.
- Task 22.10 — DriverSchedule PATCH and deprecated crew alias — ✅ live middleware/query checks and controlled matrix passed.
- Task 22.11 — Booking schedule/cancellation handling — ✅ controlled tests and the full live Booking→Payment refund leg passed.
- Task 22.12 — Notification ownership and mapping — ✅ focused Notification and full Nx suites passed.
- Task 22.13 — Cross-cutting verification artifacts — ✅ the authoritative runner now executes the full timeline snapshot-integrity chain and refuses close-out unless it passes.

## Changed files

Day-22 range `v1.28.0..0a07d84` contains 189 tracked files:

- `VietRide_API_Contract_v1.md`, `BACKEND_SOURCE_OF_TRUTH.md`, `SU26SE101_VIETRIDE_technical_context_v7.md` — Day-22 HTTP, pricing, event, ownership, audit, error, and cascade truth; BSOT bumped to `1.30.0` with a §13 row.
- `apps/trip/**` (91 files) — pricing snapshot, Trip PATCH, DriverSchedule cascade, vehicle swap, audit/fare persistence, two EF migrations, repositories, events, and tests.
- `apps/booking/**` (58 files) — edit-impact endpoint, one-clock pricing client, immutable snapshot tests, schedule/cancellation consumers, pending actions, Booking-owned events, Hangfire re-alert, and tests.
- `apps/notification/**` (14 files), `libs/shared/contracts/**` (9 files) — exact Day-22 event contracts, bindings, passenger/crew ownership, mappers, and tests.
- `libs/dotnet/VietRide.Shared.Web/Idempotency/IdempotencyFingerprint.cs` — normalized query participation in the shared idempotency fingerprint.
- `apps/identity/tests/**` (4 files) — deterministic full-matrix fixture isolation/cleanup hardening; no Identity production behavior changed.
- `db-schema/trip-route-vehicle/{schema.sql,README.md}` — canonical notes/audit/fare-source/overlap schema synchronization; the Task-22.0-only schema patch was independently confirmed comment-only.
- `infra/docker/docker-compose.yml` — Booking Hangfire/runtime configuration.
- `docs/api/postman/**`, `scripts/run-day22-trip-edit-pricing-local.mjs`, `docs/handoff/evidence/day-22-trip-edit-pricing.md`, `docs/handoff/day-22-plan.md` — cumulative artifacts, runner, evidence, and plan.
- The pre-existing untracked `docs/handoff/day-22-sequential-execution-prompt.md` was not opened for editing, staged, deleted, or otherwise touched by this audit.

Remediation adds the following focused changes without altering the frozen Day-22 contracts:

- `ITripStopFareRepository` and `TripStopFareRepository` now model the mapped `(TripId, StopId)` key and use only mapped ordering columns.
- `GetTripSnapshotRelationalTests` exercises the real repository/handler against PostgreSQL for explicit `pricingAt` and omitted legacy pricing.
- The Day-22 runner owns isolated Identity/Booking/Payment/Notification fixtures and proves old/new fare immutability plus the exact asynchronous refund ledger/Outbox chain.
- The Day-11 runner treats legacy `trip_stop_fares` as informational, so generated Trips with zero new `TEMPLATE_SNAPSHOT` rows remain valid.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build libs/dotnet/VietRide.Libs.sln --no-restore -c Release` | PASS | Fresh recheck: `0 Warning(s), 0 Error(s)`; full runner also restored/built/formatted/tested it. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; Messaging `4/4`, Persistence `4/4`, Web `73/73` — total `81/81`. |
| `dotnet build apps/identity/VietRide.Identity.sln --no-restore -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; unit `243/243`, integration `145/145`. |
| `dotnet build apps/trip/VietRide.Trip.sln --no-restore -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; unit `416/416`, integration `157/157` — total `573/573`, including architecture, migration/constraint, and the two new relational snapshot cases. |
| `dotnet build apps/booking/VietRide.Booking.sln --no-restore -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; unit `385/385`, integration `74/74`. |
| `dotnet build apps/payment/VietRide.Payment.sln --no-restore -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; unit `68/68`, integration `20/20`. |
| `dotnet build apps/parcel/VietRide.Parcel.sln --no-restore -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes --no-restore` + full tests | PASS | Format exit `0`; unit `156/156`, integration `19/19`. |
| Focused Day-22 .NET filters from `node scripts/run-day22-trip-edit-pricing-local.mjs --full-matrix` | PASS | Trip unit `156/156` + integration `31/31`; Booking unit `105/105` + integration `17/17`; Payment unit `17/17` + integration `6/6`; total `332/332`. |
| Focused shared-contract/Notification Jest runs | PASS | Shared Day-22 contracts `23/23`; Notification `8` suites and `16/16` tests. |
| `npx nx run-many --target=build --all --parallel=3 --exclude=VietRide.*` | PASS | All 10 TS projects and dependent tasks succeeded. |
| `npx nx run-many --target=lint --all --parallel=3 --exclude=VietRide.*` | PASS | Exit `0`. |
| `npx nx run-many --target=test --all --parallel=3 --exclude=VietRide.* --ci --passWithNoTests` | PASS | Nx succeeded for 10 projects and 3 dependent tasks; emitted Jest summary `17` suites, `74/74` tests. Expected negative-path logs/open-handle warning did not change exit `0`. |
| `dotnet ef migrations list -p apps/trip/src/VietRide.Trip.Infrastructure -s apps/trip/src/VietRide.Trip.Api --no-build` | PASS | Latest Day-22 migrations: `20260715104549_AddTripEditAuditPersistence`, `20260715114601_AddFareSourceAndWindowGuard`. |
| `dotnet ef database update 20260714092342_AddTripAuditLogs ...` | PASS | Reverted `AddFareSourceAndWindowGuard` and `AddTripEditAuditPersistence` cleanly. |
| `dotnet ef database update ...` | PASS | Re-applied both Day-22 migrations cleanly. A fresh-from-empty audit DB was not required because history was not squashed/reordered/edited; fresh constraint behavior is covered in the green Trip integration suite. |
| `dotnet ef migrations has-pending-model-changes ...` | PASS | `No changes have been made to the model since the last migration.` EF emitted two existing sentinel/default advisories for `OutboxEvent.Status` and `Vehicle.Status`; no pending-model failure. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | Rebuilt current images and started Gateway, 5 .NET services, 3 workers, Postgres, Redis, RabbitMQ, and PgBouncer; all 13 containers healthy/up. |
| `/health` matrix before and after the full matrix | PASS | HTTP `200`: Gateway `:3000`, Identity `:5001`, Trip `:5002`, Booking `:5003`, Payment `:5004`, Parcel `:5005`, Tracking `:3001`, Notification `:3002`, RAG `:3003`. |
| `node scripts/run-day22-trip-edit-pricing-local.mjs --full-matrix` | PASS | Exit `0` in `734.6s`; in addition to auth/idempotency/edit and duplicate Parcel checks, the runner created a `200000` Booking, edited Trip fare to `211111`, created a second Booking at the new fare, cancelled the old Booking, and observed an exact `200000` wallet/platform refund with one cancelled and one refunded Outbox event. All fixtures were removed and 9 app containers were restored healthy. |
| Day-21 regression invoked by the Day-22 full runner | PASS | Driver lifecycle, Parcel transition, Booking completion/duplicate behavior, and Day-21 fixture cleanup completed inside the exit-0 close-out run. |
| Review artifact validation | PASS | Postman collection/environment parse; Day-22 folder contains only Gateway operator Trip/DriverSchedule requests; static Task-22.0 boundary and contradiction checks pass. Artifact presence is not runtime proof. |
| `node scripts/run-day11-newman-local.js` | PASS | Generated a SCHEDULED Trip with `tripStopFaresCount=0`, completed all `5/5` requests and `10/10` assertions plus the internal seat-lock/book/reject seam, then cleaned deterministic Identity/Trip fixtures. |
| Real Gateway pricing/refund Review (owned by the Day-22 runner) | **PASS** | Both Bookings returned `201`; the old persisted `base_fare|total_amount` remained `200000|200000`, the new snapshot was `211111|211111`, cancellation preview was `200000`, and the asynchronous final state was old `REFUNDED`, new `CONFIRMED`, wallet `788889`, with exactly one matching refund credit/debit and lifecycle Outbox event each. |
| Review cleanup/state check | **PASS** | Identity, Booking, Payment, Notification, Parcel, Trip, Redis idempotency keys, and the pre-run PlatformWallet state were all verified clean/restored. |
| Day-22 Review bullet overall | **PASS** | The required `book → operator edits fare → cancel → refund uses original fare` runtime test passes through Gateway and the real service/database/event chain. |
| Hard invariants | PASS | No `.csproj` `Version=` attributes; approved Hangfire packages are centrally pinned (`Hangfire.AspNetCore 1.8.23`, `Hangfire.PostgreSql 1.8.6`); MediatR remains `11.1.0`; no banned dependency declarations; no `Co-Authored-By` in `v1.28.0..HEAD`; Day-22 tracked files match `.gitattributes` working-tree EOL; `git diff --check` passes; `AGENTS.md` remains ignored/untracked; only the original checkout exists (no worktree). |

## Contract / event / schema changes shipped

- Added canonical `PATCH /v1/operator/trips/{tripId}` and full `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`; retained `/crew` as a one-release deprecated alias.
- Added raw internal `GET /internal/v1/bookings/trips/{tripId}/edit-impact?operatorId=...` and optional explicit-pricing `GET /internal/v1/trips/{tripId}?pricingAt=...` semantics.
- Registered `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST`, `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`, `TRIP_VEHICLE_SWAP_TOO_LATE`, and `DRIVER_SCHEDULE_EDIT_TOO_LATE`.
- Registered `trip.trip.vehicle_swapped`, froze `trip.trip.schedule_changed` and `trip.trip.cancelled`, and added `booking.booking.seat_reassignment_required`, `booking.booking.schedule_change_informational`, `booking.booking.schedule_change_required`, and `booking.booking.pending_action_realerted` with Booking-owned passenger notification semantics.
- Added `trips.notes`, append-only `driver_schedule_audit_logs`, `trip_stop_fares.source`, PostgreSQL `btree_gist`, and the half-open fare-window exclusion constraint through `20260715104549_AddTripEditAuditPersistence` and `20260715114601_AddFareSourceAndWindowGuard`.
- Added Booking-owned PostgreSQL Hangfire re-alert scheduling with logical `pendingActionId` dedupe and deterministic re-alert Outbox identity.
- BSOT v1.30.0 contains the Day-22 error/event/job/convention registries and §13 changelog row. No Day-22 registration is missing.

## Known gaps & carry-over for Day 23

- No Day-22 release blocker remains after remediation and the complete close-out rerun.
- The EF pending-model command still emits the two pre-existing sentinel/default advisories for `OutboxEvent.Status` and `Vehicle.Status`; it exits `0` with no pending model changes and is not a Day-22 regression.

## Notes for Day 23 planning

- Day 23 may proceed from a green Day-22 baseline; preserve the exact Day-22 event payloads, ownership split, query-aware idempotency, immutable money snapshots, and runner coverage.
- The existing untracked `docs/handoff/day-22-sequential-execution-prompt.md` belongs to the human and remained untouched.
