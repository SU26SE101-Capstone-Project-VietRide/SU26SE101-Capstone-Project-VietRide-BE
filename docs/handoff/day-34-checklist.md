# Day 34 — Final checklist

> Produced by `/audit-day 34` after blocker remediation, an independent SOT/code audit,
> the full verification matrix, and a fresh Gateway/Newman runtime flow.
> Task tracker approvals were not used as audit evidence.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 34 (Jira: SCV-114)
- **Plan**: `docs/handoff/day-34-plan.md`
- **Status**: ✅ PASS

## DoD result

- [x] One authorized substitution on an `IN_PROGRESS` Trip creates exactly one dedicated
  replacement and returns `oldTripStatus=DISRUPTED`, `newTripStatus=BOARDING`,
  `transferStatus=QUEUED`, and five affected Passengers.
- [x] Same-key substitution replay returns the original `substitutionId` and `newTripId`.
  Cross-tenant access returns `404 TRIP_NOT_FOUND`.
- [x] Five eligible `BOARDED` Passengers create exactly five immutable transfer rows.
  A four-seat replacement preserves one null `newSeatNumber` without a sentinel.
- [x] Three bodyless Driver confirmation calls produce exactly
  `3 CONFIRMED / 2 PENDING_CONFIRM`; same-key replay preserves the original result.
- [x] Trip and Booking facts satisfy
  `payload.eventId == Outbox row id == RabbitMQ MessageId`.
- [x] `notifyPassengers=false` still emits the Booking fact and creates zero
  `VEHICLE_SUBSTITUTED` notifications.
- [x] A legacy persisted booking code is readable by the consumer while `BookingCode.Parse`
  remains strict for new public input.

## Truth-correctness result

- [x] `SU26SE101_VIETRIDE_technical_context_v7.md` now describes the exact frozen
  BookingTransfer confirmation fields and makes both seat-history fields nullable.
- [x] The API contract, BSOT endpoint/error/event registries, DDL, migration, timeline, and
  implementation agree on the two public endpoints, internal impact seam, status rules,
  event payloads, and Outbox identity.
- [x] BSOT remains version `1.44.0` with the Day-34 changelog row.
- [x] The cumulative Postman collection now contains a seven-request `Day34` folder covering
  substitution, replay, tenant isolation, three Passenger confirmations, and confirmation replay.
- [x] `scripts/run-day34-vehicle-substitution-e2e.mjs` owns deterministic isolated fixtures,
  invokes the public mutations through Gateway/Newman, verifies database/Outbox/notification
  evidence, and cleans up in `finally`.

## Blockers remediated

- [x] Added `VEHICLE_SUBSTITUTION_TRIGGERED` to the frozen Trip audit-action test.
- [x] Made Booking migration lifecycle tests create, migrate, and drop unique isolated databases
  without requiring `BOOKING_DESIGN_CONNECTION`.
- [x] Suppressed EF Core's cumulative `ManyServiceProvidersCreatedWarning` only in the
  `Testing` environment for Trip and Booking `WebApplicationFactory` suites.
- [x] Added a persistence-only `BookingCode.Restore` boundary and regression coverage for legacy
  rows; new-input parsing was not weakened.
- [x] Removed all four Day-34 lint warnings from shared contracts and Gateway tests.
- [x] Re-ran Notification with `--detectOpenHandles --runInBand`; all 31 suites / 178 tests
  passed without an open-handle report.
- [x] Confirmed the former Trip “hang” was compounded Redis connection timeout while Redis was
  stopped. With the audit infrastructure healthy, the full suite completes in about 3.5 minutes.

## Verification matrix

| Command / check | Result | Evidence |
|---|---|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | 0 warnings, 0 errors |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | PASS | Exit 0 |
| Trip unit tests | PASS | 537/537 |
| Trip integration tests | PASS | 247/247 in 3m25s |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | 0 warnings, 0 errors |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | Exit 0 |
| Booking unit tests | PASS | 482/482 |
| Booking integration tests | PASS | 191/191 in 2m56s |
| Booking Day-34 migration lifecycle tests | PASS | 2/2, self-owned unique databases |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | All 10 projects plus dependencies |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | No Day-34 warnings |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | All projects; Gateway 178/178 |
| Notification Jest `--runInBand --detectOpenHandles` | PASS | 31 suites / 178 tests |
| Day-34 migration latest → prior → latest | PASS | Revert and re-apply succeeded |
| Docker app-profile rebuild | PASS | All nine app images built; 13 containers healthy |
| `/health` matrix | PASS | Gateway plus eight services returned HTTP 200 (9/9) |
| Postman collection JSON / Day34 folder | PASS | Parses; seven requests present |
| `npm run postman:day34:local` | PASS | 7 requests / 7 assertions, 0 failures |
| Persisted runtime evidence | PASS | `5 transfers / 3 confirmed / 2 pending / 1 null seat`; both Outbox identities; 0 suppressed notifications |
| Fixture cleanup | PASS | Isolated Day-34 Trip/Booking/notification evidence removed |
| CPM / banned deps / MediatR / commit trailer | PASS | No invariant violations |
| EOL policy / `git diff --check` | PASS | CRLF .NET, LF TS/JSON/MD/MJS; no whitespace errors |

Known webpack/Prisma source-map warnings remain baseline dependency-output warnings and did not
affect build or test results.

## Contract / event / schema changes shipped

- Public endpoints:
  - `POST /v1/operator/trips/{tripId}/substitute-vehicle`
  - `POST /v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm`
- Internal endpoint:
  - `GET /internal/v1/bookings/trips/{tripId}/vehicle-substitution-impact?operatorId=...`
- Events:
  - `trip.trip.vehicle_substituted`
  - `booking.booking.transferred`
  - canonical `trip.trip.disrupted {hasSubstitution:true}`
- Error:
  - additive `409 TRIP_NOT_SUBSTITUTABLE`; existing `422 TRIP_NOT_IN_PROGRESS` preserved.
- Booking schema:
  - nullable `passengers.seat_number`
  - nullable original/new BookingTransfer seat history
  - confirmation status and confirmation actor/time columns
  - unique Passenger/original/new Trip index
  - migration `20260725172003_AddVehicleSubstitutionTransfers`

## Day-close result

Day 34 satisfies its DoD, Review bullets, SOT truth-correctness gate, full regression matrix,
runtime Gateway/Postman evidence, and repository invariants. There are no Day-34 blockers to carry
into Day 35.

The unrelated untracked user file `API-Response.md` was not modified.
