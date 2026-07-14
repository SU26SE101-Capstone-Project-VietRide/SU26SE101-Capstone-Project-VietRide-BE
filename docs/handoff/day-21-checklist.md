# Day 21 — Final checklist

> Produced by `/audit-day 21` after an independent source/code audit and fresh verification attempts.
> Produced after the complete static matrix and a final Gateway E2E rerun.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 21 — Trip lifecycle automation (SCV-98)
- **Plan**: `docs/handoff/day-21-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] ✅ `SCHEDULED → BOARDING` is implemented at `departureDateTime - 30 minutes`, with the `trip.trip.boarding_started` Outbox event and fake-clock boundary/duplicate-job tests in `TripLifecycleJobIntegrationTests`.
- [x] ✅ Assigned Driver start is implemented at `POST /v1/driver/trips/{tripId}/start`; the fresh Gateway E2E observed `200`, exact replay `200`, key mismatch `422 IDEMPOTENCY_KEY_MISMATCH`, and a fresh post-transition key `409 TRIP_INVALID_TRANSITION`.
- [x] ✅ Delayed `BOARDING → IN_PROGRESS` fallback is implemented with the documented `departureDateTime + 30 minutes` condition and fake-clock tests.
- [x] ✅ Role/assignment enforcement is implemented; fresh Gateway E2E observed assistant and unassigned-driver start denials as `403 FORBIDDEN`.
- [x] ✅ Start/complete endpoints opt in to shared UUID-v4 idempotency and expose the ADR-0004 `200` response DTOs. The E2E observed malformed-key `422`, replay, subject/path mismatch, and post-transition behavior.
- [x] ✅ Assigned Driver/Assistant completion is implemented as one Trip-local transaction with completion Outbox and `TRIP_COMPLETED_MANUAL` audit row; fresh E2E observed assistant completion `200` and exactly one audit row.
- [x] ✅ `IN_PROGRESS → COMPLETED` fallback is implemented after ETA + 30 minutes with fake-clock/race coverage in Trip tests.
- [x] ✅ Parcel subscribes idempotently to `trip.trip.started`; fresh Gateway E2E observed the seeded `LOADED` parcel move to `IN_TRANSIT`.
- [x] ✅ Booking subscribes idempotently to `trip.trip.completed`; fresh Gateway E2E observed only eligible bookings complete and their history source is `COMPLETE_ON_TRIP_COMPLETED`.
- [x] ✅ Duplicate completion was acknowledged and drained in the fresh E2E; it made no additional Booking transition/history.
- [x] ✅ The complete static matrix is green when run per target: Shared Libraries `81/81`; Trip unit `256/256`, integration `115/115`; Parcel unit `156/156`, integration `19/19`; Booking unit `338/338`, integration `50/50`; TS suite `17` suites and `74/74` tests.

## Tasks completed

- Task 21.0 — lifecycle HTTP/job/event/audit contract baseline — ✅ source audit passed.
- Task 21.1 — shared idempotency hardening — ✅ source audit, Shared Libraries static suite, and Gateway behavior passed.
- Task 21.2 — Trip audit persistence and migration — ✅ source audit and migration apply/rollback/reapply passed.
- Task 21.3 — manual lifecycle endpoints and transactional events — ✅ source audit and Gateway behavior passed.
- Task 21.4 — fake-clock lifecycle jobs — ✅ source audit and Trip unit/integration suite passed.
- Task 21.5 — Parcel TripStarted consumer — ✅ source audit and Gateway E2E passed.
- Task 21.6 — Booking TripCompleted consumer — ✅ source audit and Gateway E2E passed.
- Task 21.7 — cross-service lifecycle verification — ✅ fresh Gateway E2E passed.

## Changed files

- `VietRide_API_Contract_v1.md`, `BACKEND_SOURCE_OF_TRUTH.md` — Driver lifecycle endpoint contracts, idempotency semantics, error/event/job/audit registries, and BSOT v1.29.0 changelog.
- `libs/dotnet/VietRide.Shared.Web/**` — opted-in shared idempotency marker, fingerprinting, reservation/replay middleware.
- `apps/trip/**` — guarded start/complete CQRS endpoints, lifecycle jobs, Outbox events, append-only audit aggregate, EF migration `20260714092342_AddTripAuditLogs`, and tests.
- `apps/parcel/**` — idempotent `trip.trip.started` consumer to update `LOADED` parcels.
- `apps/booking/**` — idempotent `trip.trip.completed` consumer and `COMPLETE_ON_TRIP_COMPLETED` status history.
- `db-schema/trip-route-vehicle/{schema.sql,README.md}` — canonical `trip_audit_logs` DDL/documentation.
- `docs/api/postman/**`, `scripts/run-day21-trip-lifecycle-local.mjs` — reproducible Gateway lifecycle execution and evidence.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build` / `dotnet format --verify-no-changes` / `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | PASS | Build `0 Warning(s), 0 Error(s)`; format PASS; unit tests Messaging `4/4`, Persistence `4/4`, Web `73/73` (total `81/81`). |
| `dotnet build` / `dotnet format --verify-no-changes` / `dotnet test apps/trip/VietRide.Trip.sln -c Release` | PASS | Build `0 Warning(s), 0 Error(s)`; format PASS; unit `256/256`, integration `115/115`. |
| `dotnet build` / `dotnet format --verify-no-changes` / `dotnet test apps/parcel/VietRide.Parcel.sln -c Release` | PASS | Build `0 Warning(s), 0 Error(s)`; format PASS; unit `156/156`, integration `19/19`. |
| `dotnet build` / `dotnet format --verify-no-changes` / `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | Build `0 Warning(s), 0 Error(s)`; format PASS; unit `338/338`, integration `50/50`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects plus 3 dependent tasks succeeded; existing third-party source-map/webpack warnings only. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | Exit `0`; existing Notification lint has 2 non-null assertion warnings and 0 errors. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | 17 Jest suites, `74/74` tests passed; known test logging/open-handle warning did not affect exit `0`. |
| `dotnet ef database update ...` (Trip) | PASS | Database already at latest migration. |
| `dotnet ef database update 20260713090000_AddShuttleBackend ...` | PASS | Reverted `20260714092342_AddTripAuditLogs` cleanly. |
| `dotnet ef database update ...` (Trip, reapply) | PASS | Re-applied `20260714092342_AddTripAuditLogs` cleanly. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | App stack was rebuilt/started; `docker ps` showed Gateway, 5 .NET services, 3 workers, Postgres, Redis, RabbitMQ and PgBouncer healthy/up. |
| `/health` matrix | PASS | HTTP `200`: Gateway `:3000`; Identity `:5001`; Trip `:5002`; Booking `:5003`; Payment `:5004`; Parcel `:5005`; Tracking `:3001`; Notification `:3002`; RAG `:3003`. |
| `node scripts/run-day21-trip-lifecycle-local.mjs` | PASS | Final rerun passed 24 checks: authorization/idempotency, start/complete Outbox, Parcel `LOADED → IN_TRANSIT`, eligible Booking completion/history, duplicate no-op, and verified cleanup. Tokens were redacted. |
| Review artifact validation | PASS | Collection/environment, README, Day-21 runner, and lifecycle evidence artifact exist; Gateway base URL is local and no secret/token is committed. |
| Review execution against Docker/local stack | PASS | The runner executed against the rebuilt Docker stack through Gateway. The first sandboxed attempt was blocked only from `docker exec` cleanup; rerun with Docker access passed fully. |
| Day-21 Review bullet overall | PASS | Fake-clock boundary tests are present in Trip integration tests; the real Gateway run proved `TripStarted` reaches Parcel correctly. |
| Hard invariants | PASS | No `PackageReference Version=` attributes; no `Co-Authored-By` in Day-21 commits; code audit found no new banned dependency or MediatR v12+. Tracked files remain governed by `.gitattributes`; this audit introduced no code changes. |

## Contract / event / schema changes shipped

- Added `POST /v1/driver/trips/{tripId}/start` and `/complete` with HTTP `200` ADR-0004 envelopes and UUID-v4 `Idempotency-Key` semantics.
- Registered `TRIP_INVALID_TRANSITION` (`409`), retained routing keys `trip.trip.boarding_started`, `trip.trip.started`, and `trip.trip.completed`, and registered Booking history source `COMPLETE_ON_TRIP_COMPLETED`.
- Added `trip_audit_logs` via `20260714092342_AddTripAuditLogs`; apply/rollback/reapply was verified.
- BSOT registry and §13 changelog were updated in v1.29.0, so no unregistered Day-21 event/error/convention remains.

## Known gaps & carry-over for Day 22

- No Day-21 verification gap remains.
- Existing explicit carry-over remains: `ArriveTripStopCommandHandler.cs` still uses `INVALID_TRIP_STATUS` outside Day-21 ownership; its next owning task must migrate it to `TRIP_INVALID_TRANSITION` as planned.

## Notes for Day 22 planning

- Preserve the Day-21 lifecycle runner as a regression command whenever Trip, Booking, Parcel, shared idempotency, Gateway auth/proxy, Outbox, or RabbitMQ behavior changes.
- Day 22 should not alter lifecycle endpoint response/idempotency semantics or event payloads without a contract/BSOT update and this E2E regression.
