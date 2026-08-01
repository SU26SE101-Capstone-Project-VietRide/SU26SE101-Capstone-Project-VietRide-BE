# Day 36 — Final checklist

> Produced by `/audit-day 36` after the reopened Shuttle reliability repair and independent
> verification on 2026-08-01.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 36 (SCV-116)
- **Plan**: `docs/handoff/day-36-plan.md`
- **Repair plan**: `docs/handoff/day-36-43-fe-gap-repair-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] A supported Station Shuttle selection creates the confirmed request fan-out exactly once.
  The real-app E2E produced five confirmed Bookings, 15 Tickets, and 15 unique
  `ShuttlePassenger` manifests.
- [x] Operator A can list only its pending Shuttle requests through the Gateway; the cross-tenant
  query remains isolated.
- [x] The operator can create a ShuttleTrip and atomically assign the selected subset; replay does
  not duplicate assignments or manifests.
- [x] A non-Shuttle Station is rejected with `SHUTTLE_STATION_NOT_SUPPORTED`; the Stop and
  authoritative `trip_stops` fixture also proves the supported Stop snapshot path.
- [x] Shuttle tracking uses the isolated Shuttle room. Socket join, GPS/ETA, warning and cutoff
  assertions all passed.
- [x] Inbox processing is atomic: the handler fan-out and Inbox marker commit or roll back
  together, replay is idempotent, and no confirmation message remains in the DLQ.

## Tasks completed

- Task 36.0 — contract, ownership and cross-service baseline — ✅
- Task 36.1 — Shuttle aggregate and persistence baseline — ✅
- Task 36.2 — Booking confirmation fan-out seam — ✅
- Task 36.3 — operator query and manual dispatch — ✅
- Task 36.4 — Shuttle tracking authorization and room isolation — ✅
- Task 36.5 — cross-service acceptance suite — ✅
- Reopen repair R1 — ambient Inbox transaction plus UUID-v4 harness keys — ✅
- Audit fixture repair — deterministic Stop snapshot and active Shuttle subscription — ✅

## Changed files

- `apps/trip/src/VietRide.Trip.Infrastructure/Messaging/BookingShuttleConfirmedIntegrationEventHandler.cs`
  — joins the Inbox ambient unit-of-work transaction.
- `apps/trip/tests/VietRide.Trip.IntegrationTests/Persistence/ShuttlePersistenceIntegrationTests.cs`
  — atomic commit, rollback and replay coverage.
- `scripts/day36-idempotency-keys.mjs` and tests — stable per-label UUID-v4 keys.
- `scripts/day36-stop-snapshot-fixture.mjs` and tests — active Stop and authoritative TripStop
  fixture.
- `scripts/day36-subscription-fixture.mjs` and tests — active Shuttle-enabled operator plan and
  subscription quotas.
- `scripts/run-day36-shuttle-e2e.mjs` — repaired fixture prerequisites while preserving the
  original acceptance assertions.
- `docs/handoff/day-36-plan.md` — reopening addendum; prior history was not rewritten.

## Verification run

| Command / check | Result | Notes |
| --- | --- | --- |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | 0 warnings, 0 errors |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | PASS | No changes |
| Trip tests | PASS | Unit 581/581; integration 264/264 |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | 0 warnings, 0 errors |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | No changes |
| Booking tests | PASS | Unit 535/535; integration 209/209 |
| Full TS lint/test/build matrix | PASS | All 10 TS projects and dependencies completed |
| Day 36 Node helper tests | PASS | 6/6 across key, Stop and subscription fixtures |
| `npm run e2e:day36` | PASS | 10/10 checkpoints in 204.4s: seed, Gateway REST, 15-manifest fan-out, subset dispatch, notification, socket/ETA, warning/cutoff, race invariant, DB assertions, cleanup |
| Production-like `/health` matrix | PASS | Gateway and ports 5001–5005/3001–3003 all returned HTTP 200; all app and infra containers healthy |
| EF migration gate | N/A | The reopening repair added or changed no migration/schema object |
| Hard invariants | PASS | CPM, banned deps, no co-author trailer, diff-check and changed-file EOL all passed |
| Day 36 Review bullet | PASS | Unsupported Shuttle Station rejection executed in the real E2E; no manifest persisted |

## Contract / event / schema changes shipped

No new Day 36 endpoint, event, migration or schema object was introduced by the reopening repair.
The existing `booking.shuttle_confirmed` payload and manifest states are unchanged. The repair only
makes its consumer transaction atomic and the acceptance harness deterministic.

## Known gaps & carry-over for Day 37

- None for Day 36 closure.
- The two FE-authored untracked planning documents remain intentionally untouched and uncommitted.

## Notes for Day 37 planning

The Day 36 E2E now owns deterministic subscription prerequisites. Future subscription schema
changes must update `day36-subscription-fixture.mjs` and its focused test together.
