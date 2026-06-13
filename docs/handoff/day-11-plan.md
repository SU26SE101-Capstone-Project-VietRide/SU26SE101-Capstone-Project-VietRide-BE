# Day 11 — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 11 (SCV-80) — Trip Search API + Trip auto-generation
- **Prior checklist**: docs/handoff/day-10-checklist.md (Day-11 was never planned — work jumped to Day 12). See Sequencing note.
- **Plan status**: PLAN-REVIEW REQUIRED -- patched on 2026-06-13 to resolve the Task 11.4 implementation blocker (seat-lock store owner/token verification). Previous PLAN-REVIEW approval remains historical for completed tasks; this patched remaining-task scope requires reviewer approval before the next worker dispatch.
- **Branch**: feat/day-11-trip-search — NEW branch off main AFTER the SOT v1.11.0 commit lands
  (BSOT 1.11.0 + API Contract + Money change MUST be committed on main first; the plan's v1.10.0
  citations exist only in that commit). Runs branch-parallel with Day 13 and Day 15.

## Sequencing note (READ FIRST — out-of-order build)
Day 12 (Booking seat-lock, SCV-82) was implemented and audited BEFORE Day 11. The Day-12
checklist (docs/handoff/day-12-checklist.md) shipped the Booking side against an explicit
Trip-side stub (apps/booking/src/VietRide.Booking.Infrastructure/Http/DevTripServiceClient.cs)
and deferred the real Trip implementation as carryover CO1 (real Redis seat lock + TTL release)
and CO2 (Redis key-prefix decision). Day 11 is the Trip-owned half of that seam. It must deliver
the trips aggregate + trip_seats/trip_stops/trip_stop_fares schema, the Hangfire generation job,
the FE-facing search/detail/seat-map endpoints, AND the four /internal/v1/trips/... seam endpoints
the Booking stub currently fakes. Flipping Booking onto the real client is a Day-12-carryover
follow-up, NOT in Day-11 write scope — see Open Q3.

## Objective
Deliver Trip search + discovery on the Trip-Route-Vehicle service: the Trip aggregate with
per-trip seat/stop/fare snapshot tables, a Hangfire job that auto-generates Trips 14 days ahead
from active DriverSchedules (idempotent, on-create + activation + weekly CN 23:00), the FE-facing
GET /v1/trips/search, /v1/trips/{id}, /v1/trips/{id}/seat-map, and the frozen internal seat-lock
seam (lock/book/release-seats + GET /internal/v1/trips/{id}) that Booking (Day 12) already
consumes. This unblocks the real (un-stubbed) booking flow and Sprint-3 search-book-pay E2E.

## Success criteria (DoD — binary, verifiable)
- [ ] Hangfire (+ Hangfire.PostgreSql) is wired into Trip with the hangfire schema in
      vietride_trip (technical_context_v7 lines 354/361; per-service, NOT for outbox polling).
- [ ] EF migration creates trips, trip_seats, trip_stops, trip_stop_fares,
      trip_generation_skip_logs per db-schema/trip-route-vehicle/schema.sql (lines 349-474), with
      the two partial unique indexes (uq_trips_driver_departure, uq_trips_vehicle_departure with
      WHERE status NOT IN CANCELLED); migration up + down clean.
- [ ] Trip auto-generation Hangfire job: on DriverSchedule create (immediate one-off), on
      DriverSchedule activation/update where `isActive false -> true` via documented
      `PATCH /v1/operator/driver-schedules/{id}/activate` (minimal activation path under
      existing `/v1/operator/driver-schedules` prefix), AND weekly (CN 23:00) generates
      Trips for the next 14 days matching dayOfWeek; idempotent on (driverUserId,
      departureDateTime) + (vehicleId, departureDateTime); generates trip_seats from
      Vehicle.seatLayoutJson (skip disabled true) and trip_stops from RouteStop (snapshot
      orderIndex/allowPickup/allowDropoff/distanceFromOriginKm + computed estimatedArrivalTime).
      Re-run same day = no duplicate. The activation path is intentionally activation-only and
      MUST NOT implement the broader DriverSchedule edit cascade (technical_context_v7 §6.11.1 / Day-18+ scope).
- [ ] GET /v1/trips/search (originStationId, destinationStationId, departureDate, passengerCount,
      allowAlongRoutePickup?) returns the paged envelope in API contract lines 1000-1037, joins
      Trip-Route-Stations with availableSeats count; no result = empty 200 (NOT 404). Implement
      ONLY these API-contract query params; the timeline's operator/time/price wording is not a
      Day-11 query-param contract unless a future API-contract change adds it.
- [ ] GET /v1/trips/{tripId} returns trip detail (route, stations, stops, seat summary, fare
      summary); GET /v1/trips/{tripId}/seat-map returns the seat array (API contract lines
      1039-1062). 404 TRIP_NOT_FOUND on unknown id.
- [ ] GET /internal/v1/trips/{tripId} returns the raw (un-enveloped) snapshot DTO in API contract
      lines 1065-1097; matches the TripSnapshot shape consumed by Booking ITripServiceClient.
- [ ] POST /internal/v1/trips/{tripId}/lock-seats (all-or-nothing, idempotent on Idempotency-Key),
      /release-seats (idempotent 204), /book-seats (204) implement the seam in API contract lines
      1107-1179 with Redis seat-lock keys (`ttlSeconds` default 600s; Task 11.0 `SeatLock:TtlMinutes` default 10), trip_seats status
      machine AVAILABLE-HELD-BOOKED + HELD-AVAILABLE.
- [ ] TTL release mechanism is concrete and Trip-owned without schema changes: Redis key TTL
      remains the expiry clock; Task 11.4 adds a Hangfire recurring cleanup (every 1 minute) plus
      lock-path reconciliation that checks HELD trip_seats whose
      `seat_lock:{tripId}:{seatNumber}` key no longer exists and flips them HELD -> AVAILABLE.
      Concurrent same-seat lock attempts after expiry resolve to exactly one winner.
- [ ] Errors match the registry: 404 TRIP_NOT_FOUND (BSOT line 1360), 409 BOOKING_TRIP_NOT_BOOKABLE
      (line 1337) / 409 BOOKING_SEAT_UNAVAILABLE (line 1336).
- [ ] Trip build + dotnet format --verify-no-changes clean; NetArchTest layering green; new
      handler/job/seat-lock tests pass (>=1 happy + >=1 error each); Gateway routes for the new
      /v1/trips/* exist; Swagger renders. Postman additions for the Day-11 endpoints are a
      POST-MERGE step (after this branch merges to main), NOT owned by any Day-11 task — the
      collection JSON is also written by Day-13 Task 13.4 and JSON does not 3-way merge; whichever
      day merges second re-applies its Postman additions on the merged collection.

## Contract changes
- New REST endpoints (FE-facing): GET /v1/trips/search, GET /v1/trips/{tripId},
  GET /v1/trips/{tripId}/seat-map - VietRide_API_Contract_v1.md lines 1000-1062 (already
  documented; no contract edit needed, verify shapes match).
- Search-filter precedence: BE_TIMELINE_VU.md line 132 says `GET /trips/search?origin=&destination=&date=`
  with filter by operator/time/price, but VietRide_API_Contract_v1.md line 1004 is the higher-precedence
  endpoint contract for the current API. Day 11 implements only `originStationId`,
  `destinationStationId`, `departureDate`, `passengerCount`, and `allowAlongRoutePickup?`. Extra
  operator/time/price query filters are out of Day-11 scope/carryover and require a future API
  contract change before implementation. Existing response fields `operatorId`, `operatorName`,
  `departureDateTime`, and `baseFare` remain returned, but they are not additional filters.
- New DriverSchedule activation endpoint (MUST be documented by Task 11.2-pre before Task 11.2 code
  dispatch): `PATCH /v1/operator/driver-schedules/{id}/activate`.
  - Auth: `OPERATOR_ADMIN` (same Day-9 DriverSchedule write matrix); existing Gateway prefix
    `/v1/operator/driver-schedules` already routes to Trip, so no Gateway route change is expected.
  - Request: no body. `Idempotency-Key` is not required; behavior is resource-state idempotent.
  - Response: `200 ApiResponse<DriverScheduleDto>`; if the schedule is already active, return the
    current DTO and MUST NOT enqueue duplicate generation.
  - Errors: `403 FORBIDDEN` for missing operator scope / non-APPROVED or inactive operator;
    `404 RESOURCE_NOT_FOUND` for missing or cross-operator DriverSchedule unless Task 11.2-pre
    explicitly adds a dedicated registered code; `409 TRIP_DRIVER_CONFLICT` for active schedule
    conflicts; `422 VALIDATION_ERROR` with field details for driver/assistant role+operator mismatch,
    upstream Identity lookup validation failure, or the Q5 estimated-arrival precondition.
  - Scope: activation only (`isActive false -> true`) and generation trigger; full edit/cascade
    remains technical_context_v7 section 6.11.1 / Day-18+ scope.
- Day-9 carryover role/operator validation (MUST be documented by Task 11.2-pre before Task 11.2 code
  dispatch): DriverSchedule create and activation validate `driverUserId` exists in Identity with
  `role=DRIVER` and `operatorId` equal to the caller operator; `assistantUserId` remains nullable but,
  when present, must exist with `role=ASSISTANT` and the same `operatorId`. Failures use
  `422 VALIDATION_ERROR` with `error.fields` (`driverUserId` / `assistantUserId`), not a new error
  code unless the SOT patch explicitly registers one.
- Internal Identity lookup contract needed for that validation (MUST be documented by Task 11.2-pre if
  still absent from the API contract): `GET /internal/v1/users/{userId}`, Internal JWT only, raw
  success DTO `{ id, role, operatorId, status }`, ADR-0004 error envelope on errors, no Gateway route.
  BSOT section 7.2 already registers the path, but repo discovery shows Identity currently exposes only
  `/internal/v1/users/{userId}/device-tokens`; Task 11.2 implementation must either consume an
  existing approved lookup or add the minimal Identity endpoint described by the SOT patch.
- New internal seam endpoints: GET /internal/v1/trips/{tripId},
  POST /internal/v1/trips/{tripId}/lock-seats, /release-seats, /book-seats -
  VietRide_API_Contract_v1.md lines 1065-1179 (frozen seam, BSOT 13 row 1.8.0). Trip is the server
  side; Booking is the already-built client. No shape change permitted.
- Gateway routes: the existing `/v1/trips` entry (apps/gateway/src/config/routes.ts ~line 112) is
  currently `authRequired: 'user'` - the nest-worker must CHANGE it to `authRequired: 'mixed'` with
  `publicSubpaths: [{ method: 'GET', path: '/v1/trips/search' }]` so search works unauthenticated
  while detail/seat-map stay protected (User JWT). Do NOT add a duplicate prefix entry. Internal
  endpoints are NOT exposed via Gateway (Internal JWT only, service-to-service). The activation
  endpoint above is covered by the existing `/v1/operator/driver-schedules` Gateway prefix.
- Events: none. BSOT 7.3 trip event registry (lines 1742-1748) has no trip.trip.generated key;
  Day-11 generation emits no event. Do not invent one.
- Error codes: keep to existing BSOT 5.9 rows unless Task 11.2-pre registers a deliberate new row.
  Current Day-11 code tasks use TRIP_NOT_FOUND (line 1360), BOOKING_SEAT_UNAVAILABLE (line 1336),
  BOOKING_TRIP_NOT_BOOKABLE (line 1337), TRIP_DRIVER_CONFLICT (line 1365), FORBIDDEN (line 1410),
  VALIDATION_ERROR (line 1406), and RESOURCE_NOT_FOUND (line 1409). Do not emit the unregistered
  `DRIVER_SCHEDULE_NOT_FOUND` code unless the SOT patch adds it first.
- Migration: one Trip EF migration (vietride_trip) for the 5 tables + Hangfire schema bootstrap
  (Hangfire creates its own hangfire.* tables at runtime).

## Tasks

### Task 11.0 — Trip architecture baseline: Hangfire + Redis seat-lock seam wiring (DO FIRST)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | Directory.Packages.props (add Hangfire.AspNetCore + Hangfire.PostgreSql as `<PackageVersion>`, free MIT 1.8.x line — Q1 APPROVED; **shared CPM entries with Day-15 Task 15.5 (day-15-plan.md OQ-3a). Same-tree rule: whichever lands first adds them, the other is a no-op (check before adding). BRANCH-PARALLEL rule: feat/day-11 and feat/day-15 are separate branches — if neither has merged, BOTH branches will carry the same `<PackageVersion>` additions and the SECOND branch to merge resolves a trivial duplicate-line conflict at merge time (keep ONE copy of each entry; identical 1.8.x pins on both sides by agreement). If Day-15 merged first, this task's CPM edit becomes a no-op after rebase**); apps/trip/src/VietRide.Trip.Api/Program.cs; apps/trip/src/VietRide.Trip.Api/appsettings.json + appsettings.Development.json; apps/trip/src/VietRide.Trip.Api/VietRide.Trip.Api.csproj; apps/trip/src/VietRide.Trip.Infrastructure/VietRide.Trip.Infrastructure.csproj; apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs; new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/HangfireServiceCollectionExtensions.cs; new apps/trip/src/VietRide.Trip.Application/Abstractions/SeatLock/ISeatLockStore.cs; new apps/trip/src/VietRide.Trip.Infrastructure/SeatLock/RedisSeatLockStore.cs |
| forbidden scope | .env, secrets; db-schema/** (read-only canonical DDL); other services (apps/identity, apps/booking, apps/payment, apps/parcel, apps/gateway, apps/tracking, apps/notification, apps/rag); libs/**; git ops; do NOT add OpenTelemetry/Prometheus/Grafana/Tempo/Loki or MediatR v12+; do NOT use Hangfire for outbox polling (technical_context_v7 line 369) |
| depends on | — |
| invariant flags | CRLF on .cs/.csproj/.props; CPM: no Version= on the new PackageReference (version only as PackageVersion in Directory.Packages.props); MediatR v11; Hangfire schema lives in vietride_trip (per-service, NOT a shared DB); banned-dep guard (Hangfire NOT banned; OTel/Prom/MediatR-v12 are); pin free MIT Hangfire 1.8.x line ONLY — Hangfire Pro/Ace (commercial) forbidden |
| acceptance | dotnet build apps/trip/VietRide.Trip.sln -c Release 0W/0E; dotnet format --verify-no-changes clean; Trip app boots with Hangfire registered against vietride_trip hangfire schema; Redis seat-lock store resolvable from DI (StackExchange.Redis already transitive via Shared.Web); Trip appsettings expose `SeatLock:TtlMinutes` default 10 and RedisSeatLockStore consumes that option as the Trip-owned runtime TTL source; no Version= on any csproj ref (CPM hook passes) |
| source citations | technical_context_v7 lines 354/360-369 (Hangfire per-service schema, not for outbox); VietRide_API_Contract_v1.md internal lock-seats section, lines 1126-1127 (`ttlSeconds` default = 600s / `SEAT_LOCK_TTL_MINUTES * 60`); BACKEND_SOURCE_OF_TRUTH.md §11.3 Trip-Route-Vehicle env registry (`SEAT_LOCK_TTL_MINUTES=10`) is the Trip-owned source for `SeatLock:TtlMinutes`; Task 11.0 appsettings/config seam in this plan (`SeatLock:TtlMinutes`, default 10, consumed by RedisSeatLockStore); db-schema/trip-route-vehicle/README.md line 12 (Hangfire jobs in this DB); AGENTS.md Hard invariants (CPM, banned deps). Do not cite the Booking env block as Trip-owned config for this task. |

### Task 11.1 — Trip aggregate + TripSeat/TripStop/TripStopFare/TripGenerationSkipLog domain + EF migration
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Domain/Entities/Trip.cs, TripSeat.cs, TripStop.cs, TripStopFare.cs, TripGenerationSkipLog.cs (+ enums TripStatus, TripSource, TripSeatStatus, TripSeatType, TripStopStatus, TripGenerationSkipReason); apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs (add DbSets); new apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/Trip*Configuration.cs (one per entity); new migration under apps/trip/src/VietRide.Trip.Infrastructure/Migrations/ (+ Designer + TripDbContextModelSnapshot.cs update); new apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripRepository.cs + ITripSeatRepository.cs + ITripStopRepository.cs; new apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/Trip*Repository.cs; register in InfrastructureServiceCollectionExtensions.cs |
| forbidden scope | .env, secrets; db-schema/** (read-only); other services; libs/**; apps/gateway; git ops; do NOT add Hangfire/search/endpoint code here; do NOT add shuttle_*/incidents/outbox_events tables (out of Day-11 scope) |
| depends on | 11.0 |
| invariant flags | CRLF on .cs; snake_case schema; Trip is NOT soft-deletable (schema has no deleted_at on trips — status enum governs lifecycle); Money = BIGINT VND to-the-đồng (base_fare/fare_from_this_stop via Money.FromRaw — NO floor-1000, BSOT v1.11.0); no cross-DB FK (operator_id/driver_user_id/assistant_user_id logical FKs, no DB FK); partial unique indexes filtered on status not CANCELLED; CPM |
| acceptance | migration dotnet ef database update clean from current Trip schema; down reverts cleanly; the 5 tables + enums + uq_trips_driver_departure/uq_trips_vehicle_departure/uq_trip_seats_trip_seat/uq_trip_stops_trip_order present; trips.base_fare BIGINT; CHECK constraints (chk_trips_base_fare_non_negative, chk_trips_cargo_counters_non_negative, chk_trip_stop_fares_fare_non_negative) created; build + format clean; NetArchTest layering green |
| source citations | db-schema/trip-route-vehicle/schema.sql lines 15-37 (enums), 349-474 (5 tables + indexes + CHECKs + COMMENTs); db-schema/trip-route-vehicle/README.md lines 29-53; AGENTS.md Domain conventions (Money BIGINT, soft-delete, logical FK); existing apps/trip/.../Entities/Vehicle.cs + Route.cs (mirror BaseEntity/validation) |

### Task 11.2-pre - Patch DriverSchedule activation + Identity user-lookup SOT (docs-only, DO BEFORE 11.2)
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `VietRide_API_Contract_v1.md` (document DriverSchedule activation endpoint and Day-11 role/operator validation; document internal Identity user lookup if absent); `BACKEND_SOURCE_OF_TRUTH.md` (update section 7.2 internal endpoint registry details if needed, keep error registry aligned, add section 13 changelog/version row - this repo uses BSOT section 13 as the docs changelog). |
| forbidden scope | .env, secrets; code under apps/** and libs/**; db-schema/**; docs/handoff/day-11-plan.md after this patch; other docs not listed; git ops. This is a docs/SOT patch only - do NOT implement endpoints or clients here. |
| depends on | 11.0, 11.1 (already completed). Must land before Task 11.2 code dispatch. Parallel-safe = no because it resolves the PLAN-REVIEW gate for remaining tasks. |
| invariant flags | LF for .md; API contract outranks BSOT for endpoint shape; no invented columns/enums; ADR 0004 envelope for FE-facing activation success/errors; internal Identity success DTO raw (no ApiResponse envelope) and errors via ADR 0004; no Gateway exposure for internal Identity lookup; no new error code unless added to BSOT section 5.9 in the same SOT patch. |
| acceptance | API contract explicitly documents `PATCH /v1/operator/driver-schedules/{id}/activate`: Auth `OPERATOR_ADMIN`, no request body, `Idempotency-Key` not required, behavior-idempotent already-active returns current `DriverScheduleDto` without duplicate generation enqueue, `200 ApiResponse<DriverScheduleDto>`, Gateway impact = existing `/v1/operator/driver-schedules` prefix only/no new route, full edit cascade out of scope. API contract updates DriverSchedule create from "role validation deferred" to Day-11 validation: driver must be Identity user `role=DRIVER` under caller operator, assistant nullable but if present must be `role=ASSISTANT` under caller operator. API contract/BSOT document or clarify `GET /internal/v1/users/{userId}` as Internal-JWT-only raw lookup `{ id, role, operatorId, status }` if still absent. Errors documented with existing registry rows: `403 FORBIDDEN`, `404 RESOURCE_NOT_FOUND` for missing/cross-operator DriverSchedule unless a dedicated code is registered, `409 TRIP_DRIVER_CONFLICT`, `422 VALIDATION_ERROR` for user role/operator mismatch and Identity logical-FK validation failures. BSOT section 13 changelog/version row records the activation endpoint + Day-9 carryover closure. Markdown renders. |
| source citations | Human decisions 2026-06-13 (PLAN-REVIEW blockers: activation endpoint SOT + Day-9 carryover validation); VietRide_API_Contract_v1.md lines 2778-2789 (DriverSchedule write role matrix + no Day-9 Idempotency-Key + operator write guard), 3064-3085 (`DriverScheduleDto`), 3087-3118 (create contract and prior Day-9 deferral), 3113 (`TRIP_DRIVER_CONFLICT`); BACKEND_SOURCE_OF_TRUTH.md lines 1667-1674 (`GET /internal/v1/users/{userId}` registry + operator endpoints), 1365/1406/1409/1410 (TRIP_DRIVER_CONFLICT, VALIDATION_ERROR, RESOURCE_NOT_FOUND, FORBIDDEN), section 13 changelog pattern; docs/handoff/day-9-plan.md lines 27 and 154-155 (Day-9 carryover: Identity user lookup unbuilt; validate driver/assistant role+operator in Day 11); db-schema/_global/cross-service-references.md lines 24-26 and db-schema/trip-route-vehicle/README.md lines 93-94 (DriverSchedule logical FKs to Identity); technical_context_v7 lines 3557-3580 and 3666-3707 (DriverSchedule assignment + activation generation trigger). |

### Task 11.2 - Trip auto-generation Hangfire job (on-create + activation + weekly CN 23:00, idempotent)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | **Trip generation core:**<br>- new `apps/trip/src/VietRide.Trip.Application/Abstractions/Jobs/ITripGenerationJobScheduler.cs`<br>- new `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripGenerationSkipLogRepository.cs`<br>- new `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripStopFareRepository.cs`<br>- new/edit `apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/**` (`GenerateTripsForScheduleCommand`/Handler/Validator + `TripGenerationService`)<br>- new `apps/trip/src/VietRide.Trip.Infrastructure/Jobs/TripGenerationJob.cs` and `HangfireTripGenerationJobScheduler.cs`<br>- new `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/TripGenerationSkipLogRepository.cs` and `TripStopFareRepository.cs`<br>- edit `apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (register scheduler + repositories only).<br>**DriverSchedule trigger/activation + validation:**<br>- edit `apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/CreateDriverScheduleHandler.cs` to validate driver/assistant role+operator and enqueue only after successful commit<br>- add/edit `apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/ActivateDriverScheduleCommand.cs`, `ActivateDriverScheduleHandler.cs`, `ActivateDriverScheduleValidator.cs`<br>- edit `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorDriverSchedulesController.cs` for `PATCH {id}/activate` (no body)<br>- edit `apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/IIdentityInternalClient.cs` and `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/IdentityInternalClient.cs` to add minimal user role/operator lookup/validation methods following existing operator-validation pattern.<br>**Identity internal lookup (only because discovery shows `GET /internal/v1/users/{userId}` is registered but not implemented):**<br>- edit `apps/identity/src/VietRide.Identity.Api/Controllers/InternalUsersController.cs`<br>- new `apps/identity/src/VietRide.Identity.Application/Features/InternalUsers/GetInternalUser/**` (query/handler/DTO/validator if needed)<br>- edit `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IUserRepository.cs` and `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/UserRepository.cs` only if no suitable read method exists.<br>**Tests:**<br>- `apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/**`, `Features/TripGeneration/**`, `ExternalClients/IdentityInternalClientTests.cs`<br>- `apps/identity/tests/VietRide.Identity.UnitTests/**` and/or `apps/identity/tests/VietRide.Identity.IntegrationTests/**` for the internal user lookup. |
| forbidden scope | .env, secrets; db-schema (read-only); services other than apps/trip and the minimal apps/identity files explicitly listed above; libs; apps/gateway (existing Gateway prefix already routes `/v1/operator/driver-schedules/**` to Trip); BACKEND_SOURCE_OF_TRUTH.md and VietRide_API_Contract_v1.md (Task 11.2-pre owns SOT docs; if not completed, STOP); git ops; do NOT reference Hangfire.* or Infrastructure from VietRide.Trip.Application (use ITripGenerationJobScheduler); do NOT implement full DriverSchedule edit/cascade (departureTime/dayOfWeek/driver/assistant/vehicle/validUntil updates with FUTURE_ONLY/ALL_PENDING are technical_context_v7 section 6.11.1 / Day-18+ scope); activation path may only transition `isActive false -> true` and enqueue generation after successful commit; do NOT expand conflict-validation beyond the SOT-patched create/activation role+operator validation and existing active-schedule conflict check; do NOT add auto-BOARDING / auto-COMPLETED jobs (Day-21 scope); do NOT emit any event (no trip.trip.generated key exists) |
| depends on | 11.2-pre, 11.0, 11.1. Parallel-safe = no in the current tree because it may touch Trip DI plus minimal Identity internal lookup files; run serial before 11.4/11.3. |
| invariant flags | CRLF; MediatR v11; controller calls MediatR.Send only; Internal JWT for Identity internal lookup; no cross-DB FK (Identity user/operator references remain HTTP/logical validation only); driver/assistant validation is app-layer via Identity internal client (`driverUserId` must be `DRIVER` under caller operator; nullable `assistantUserId`, when present, must be `ASSISTANT` under caller operator); validation failures use `422 VALIDATION_ERROR` with field details unless Task 11.2-pre registers a dedicated code; activation endpoint has no request body and no Idempotency-Key requirement, but is behavior-idempotent when already active; idempotency for Trip generation = check (driverUserId, departureDateTime) AND (vehicleId, departureDateTime) before INSERT (re-run no dup); Trip.source = AUTO_FROM_SCHEDULE; skip seats where disabled true in seatLayoutJson; TripStop.estimatedArrivalTime static, never recomputed after generate; trip_stop_fares snapshot only current/effective RouteStopFareTemplate rows (`effectiveFrom <= generation instant < effectiveUntil`, treating NULL effectiveUntil as open-ended) and excludes expired/future templates; trips.estimated_arrival_time (Q5 RESOLVED) = deterministic fallback chain: (1) departureDateTime + Route.estimatedDurationMinutes; (2) if NULL, departureDateTime + max(RouteStop.estimatedDurationFromOriginMinutes); (3) if neither available, REFUSE generation/DriverSchedule activation with a validation error -- NO invented default; Money to-the-dong on base_fare and TripStopFare.fareFromThisStop snapshots (copy verbatim, no rounding/flooring -- BSOT v1.11.0); no EF migration/schema changes in Task 11.2 |
| acceptance | SOT patch 11.2-pre is present before code changes. Application layer has no Hangfire.* or Infrastructure references. Identity exposes (or already has) `GET /internal/v1/users/{userId}` with Internal JWT, raw success DTO `{ id, role, operatorId, status }`, and ADR-0004 error envelope on errors; Trip `IdentityInternalClient` consumes it using the existing internal-client/Internal-JWT pattern. DriverSchedule create validates operator write eligibility, then validates `driverUserId` is Identity role `DRIVER` under the caller operator and `assistantUserId` (when non-null) is role `ASSISTANT` under the same operator; wrong role/operator/missing upstream user maps to `422 VALIDATION_ERROR` with `error.fields` and no schedule insert/enqueue. DriverSchedule create enqueues via ITripGenerationJobScheduler only after SaveChanges succeeds. `PATCH /v1/operator/driver-schedules/{id}/activate` exists with no body, returns `ApiResponse<DriverScheduleDto>`, changes only `isActive false -> true`, validates the same driver/assistant role+operator rules before activation, enqueues via ITripGenerationJobScheduler only after activation commit succeeds, and an already-active schedule returns the current DTO without a duplicate enqueue. Missing/cross-operator schedule uses only SOT-registered not-found code (`RESOURCE_NOT_FOUND` unless 11.2-pre added a dedicated code). Activation reuses active-schedule conflict checks so enabling a conflicting row fails with 409 TRIP_DRIVER_CONFLICT and does not enqueue. Recurring CN 23:00 job is registered through Infrastructure/Hangfire and executes GenerateTripsForScheduleCommand via MediatR. Unit test: schedule with dayOfWeek 2 and 4 generates Trips only for matching dates in next 14 days. Idempotent test re-run same day = 0 new rows (DoD trip generation idempotent). Seats generated = totalSeats minus disabled count. trip_stops snapshot orderIndex/allow flags/distance. Generated trips copy active/current RouteStopFareTemplate exceptions into trip_stop_fares with Money copied verbatim to TripStopFare.fareFromThisStop; expired templates and future-dated templates are not copied; add unit tests covering active copied vs expired/future excluded. estimated_arrival_time uses the Q5 fallback chain - test: Route.estimatedDurationMinutes set -> arrival = departure+duration; Route.estimatedDurationMinutes NULL but RouteStops present -> arrival = departure+max(estimatedDurationFromOriginMinutes); neither available -> generation refuses with a validation error (no default value persisted). Build + format clean for both impacted solutions (`apps/trip/VietRide.Trip.sln` and, if Identity files are edited, `apps/identity/VietRide.Identity.sln`); at least 1 happy + 1 skip-path test (e.g. missing vehicle creates a TripGenerationSkipLog row via ITripGenerationSkipLogRepository with reason) + 1 activation trigger test + role/operator validation tests for driver and assistant; NetArchTest green. |
| source citations | technical_context_v7 lines 3557-3580 (DriverSchedule assignment), 3666-3707 (on-create/update activation + weekly generation algorithm, idempotent check + vehicle conflict), lines 3709-3800 (full edit cascade exists but is NOT Day-11 activation-only scope), line 1989 (TripSeat from seatLayoutJson skip disabled), line 3600 (TripStop.estimatedArrivalTime formula), line 4560 (RouteStopFareTemplate current/effective entries copied by Hangfire/manual create to TripStopFare); db-schema/trip-route-vehicle/README.md lines 23 and 48 (RouteStopFareTemplate exception-only + effective window), line 31 (TripStopFare copy from active RouteStopFareTemplate at generate), line 54 (TripStopFare composite PK/exception only), lines 93-94 (DriverSchedule Identity logical FKs); db-schema/_global/cross-service-references.md lines 24-26 (DriverSchedule operator/driver/assistant HTTP validation); db-schema/trip-route-vehicle/schema.sql lines 202-219 (route_stop_fare_templates columns/effective index), lines 447-457 (trip_stop_fares schema/check), 462-470 (trip_generation_skip_logs); BE_TIMELINE_VU.md lines 134 and 136 (Day-11 generation on DriverSchedule activation + Sunday 23:00, DoD); VietRide_API_Contract_v1.md lines 2778-2789 (DriverSchedule role matrix/write guard), 3064-3085 (DriverScheduleDto includes `isActive`), 3087-3118 (existing create contract), 3113 (TRIP_DRIVER_CONFLICT) plus Task 11.2-pre patched activation/user-lookup sections; BACKEND_SOURCE_OF_TRUTH.md lines 1667-1674 (`GET /internal/v1/users/{userId}` registry), 1103 (HTTP validate logical FK at write), 1365/1406/1409/1410 (TRIP_DRIVER_CONFLICT/VALIDATION_ERROR/RESOURCE_NOT_FOUND/FORBIDDEN); docs/handoff/day-9-plan.md lines 27 and 154-155 (Day-9 carryover to Day 11 role/operator validation); existing apps/trip/src/VietRide.Trip.Api/Controllers/OperatorDriverSchedulesController.cs line 12 (`v1/operator/driver-schedules` route prefix) and apps/gateway/src/config/routes.ts line 152 (existing Gateway prefix); existing apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/IIdentityInternalClient.cs + apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/IdentityInternalClient.cs (operator-validation pattern to extend); existing apps/identity/src/VietRide.Identity.Api/Controllers/InternalUsersController.cs lines 11 and 21 (internal users prefix currently only has device-token endpoint); existing apps/trip/src/VietRide.Trip.Domain/Entities/RouteStopFareTemplate.cs + TripStopFare.cs, apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IRouteStopFareTemplateRepository.cs, apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/RouteStopFareTemplateRepository.cs, Entities/DriverSchedule.cs + RouteStop.cs |

### Task 11.3 — FE-facing endpoints: trip search + detail + seat-map + Gateway routes
> **Dispatch model:** execute as two separate dispatches/reviews (11.3a dotnet, then 11.3b nest) but keep the human's intended **single Task-11.3 commit** after both sub-steps are approved. Do not mix .NET and Gateway edits in one worker dispatch.

#### Task 11.3a — Implement Trip FE-facing endpoints
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Api/Controllers/TripsController.cs (route /v1/trips); new apps/trip/src/VietRide.Trip.Application/Features/Trips/SearchTrips/ (Query, Handler, Validator, SearchTripsResult, SearchTripItem); .../Features/Trips/GetTripDetail/ (Query, Handler, TripDetailDto — Q4-resolved field set: ApiResponse-wrapped projection of the internal snapshot fields (API Contract ~1072-1109) + stops + seat summary + fare breakdown); .../Features/Trips/GetTripSeatMap/ (Query, Handler, TripSeatMapDto); mappers under .../Features/Trips/; new integration tests under apps/trip/tests/VietRide.Trip.IntegrationTests/Trips/ |
| forbidden scope | .env, secrets; db-schema; other services; libs; apps/gateway (11.3b owns Gateway); git ops; the internal seam endpoints (Task 11.4 owns internal/v1/trips) |
| depends on | 11.1 (entities), 11.2 (generated trips to search). Parallel-safe with 11.4 = no (shared InfrastructureServiceCollectionExtensions.cs DI + Features namespace neighborhood) |
| invariant flags | CRLF for .cs; ApiResponse envelope (ADR 0004) with meta.traceId; search no-result = empty 200 not 404; availableSeats = count of trip_seats with status AVAILABLE; search filters = API-contract query params only (`originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`) -- do NOT add operatorId/time/price query params in Day 11; auth intent: search public at Gateway, detail/seat-map protected (User JWT); MediatR v11 |
| acceptance | GET /v1/trips/search returns the paged shape (API contract 1006-1037) with correct availableSeats; no-match query returns 200 empty items (DoD); Swagger/validation expose only the API-contract query params (`originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`) -- timeline operator/time/price filters are not implemented until a future API-contract patch adds them; GET /v1/trips/{id} returns the Q4-resolved TripDetailDto = ApiResponse-wrapped projection of the internal snapshot fields (API Contract ~1072-1109) + stops + seat summary + fare breakdown; /seat-map matches contract 1039-1062 -- NOTE: trip_seats has NO row/col/deck columns (schema.sql 407-419); the seat-map handler MUST load row/col/deck geometry by joining Trip -> Vehicle and parsing Vehicle.seatLayoutJson (match on seatNumber), merged with trip_seats status -- do NOT invent a migration; unknown id returns 404 TRIP_NOT_FOUND; Swagger renders all three; Trip build + format clean; at least 1 happy + 1 error integration test each |
| source citations | VietRide_API_Contract_v1.md lines 1000-1062 (search/detail/seat-map shapes), especially line 1004 (current search query params); BE_TIMELINE_VU.md line 132 (lower-precedence operator/time/price wording -- carryover unless contract changes); AGENTS.md Source-of-truth hierarchy (API contract wins over timeline for endpoint shape); BSOT 5.4/5.5 (ApiResponse envelope), ADR 0004; technical_context_v7 lines 1981-1983 (seat-map fields row/col/deck/type); timeline Day-11 Review (search no result returns empty 200 not 404) |

#### Task 11.3b — Update Gateway route for public trip search
| Field | Value |
|---|---|
| stack/owner | nest |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files (write set) | apps/gateway/src/config/routes.ts; apps/gateway/src/config/routes.spec.ts |
| forbidden scope | .env, secrets; all .NET files; apps/trip; other services; libs; git ops; do NOT add a duplicate `/v1/trips` prefix entry |
| depends on | 11.3a (can be reviewed separately but commit together with 11.3a). Parallel-safe = yes versus .NET tasks after 11.3a is approved because write set is Gateway-only |
| invariant flags | LF for .ts; Gateway remains a thin proxy; existing `/v1/trips` route changes from `authRequired: 'user'` to `authRequired: 'mixed'` with `publicSubpaths: [{ method: 'GET', path: '/v1/trips/search' }]`; detail and seat-map remain protected User JWT; internal endpoints are NOT exposed via Gateway |
| acceptance | Gateway proxies `/v1/trips/search` without User JWT; `/v1/trips/{id}` and `/v1/trips/{id}/seat-map` still require User JWT; route tests cover longest-prefix/mixed-auth behavior; TS lint/tests for Gateway route config pass; final handoff note says 11.3a+11.3b are one intended Task-11.3 commit |
| source citations | apps/gateway/src/config/routes.ts line 112 (existing `/v1/trips` entry), line 152 (existing `/v1/operator/driver-schedules` prefix proving no Gateway edit needed for Task 11.2 activation); routes.spec.ts existing route-config tests; VietRide_API_Contract_v1.md lines 1000-1062; ADR 0002 (Gateway thin proxy) |

### Task 11.4 — Internal seat-lock seam: GET internal trip + lock/book/release-seats (Trip side of Day-12 seam)
> **Rationale (not scope drift):** although Day-11's title emphasizes Trip search/generation, the current plan already records that Day 12 shipped Booking against a Trip stub and deferred the real Trip seat-lock implementation as CO1/CO2. Task 11.4 is the Trip-owned prerequisite seam that replaces that stub later; it implements only the frozen Trip server endpoints and explicitly does **not** flip Booking off the stub in Day 11.

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Api/Controllers/InternalTripsController.cs (route internal/v1/trips, Authorize with InternalJwtAuthenticationExtensions.Scheme); new apps/trip/src/VietRide.Trip.Api/Filters/RequireIdempotencyKeyAttribute.cs (lock-seats Idempotency-Key filter); new apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/GetTripSnapshot/ (Query, Handler, InternalTripSnapshotDto); .../Internal/Trips/LockSeats/ (Command, Handler, Validator, LockSeatsResult); .../Internal/Trips/BookSeats/ (Command, Handler); .../Internal/Trips/ReleaseSeats/ (Command, Handler); request DTOs under .../Internal/Trips/Requests/; new apps/trip/src/VietRide.Trip.Application/Abstractions/SeatLock/IExpiredSeatLockReleaser.cs; new apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/ReleaseExpiredSeatLocks/ (Command + Handler or equivalent single-responsibility Application service); new apps/trip/src/VietRide.Trip.Infrastructure/SeatLock/ExpiredSeatLockReleaser.cs; new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/ExpiredSeatLockReleaseJob.cs; apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (only if DI registration is needed); apps/trip/src/VietRide.Trip.Infrastructure/Jobs/HangfireServiceCollectionExtensions.cs and/or apps/trip/src/VietRide.Trip.Api/Program.cs (only to register the recurring expired-lock cleanup); apps/trip/src/VietRide.Trip.Application/Abstractions/SeatLock/ISeatLockStore.cs and apps/trip/src/VietRide.Trip.Infrastructure/SeatLock/RedisSeatLockStore.cs (edits limited to adding owner/token verification support needed by lock/book/release semantics; preserve Task 11.0 baseline existence-check/TTL behavior); seat-lock orchestration via ISeatLockStore + trip_seats transitions; libs/dotnet/VietRide.Shared.Application/Exceptions/ApplicationExceptions.cs (only to add a coded 409 conflict exception carrying field-level ValidationErrors, e.g. CodedConflictException; preserve existing exceptions); libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseExceptionFilter.cs (only to map that coded conflict exception to HTTP 409 with error.fields); apps/trip/src/VietRide.Trip.Infrastructure/Jobs/ExpiredSeatLockReleaseJobRegistrationHostedService.cs if needed to satisfy one-class-per-file; new tests under apps/trip/tests/VietRide.Trip.UnitTests/Features/Internal/Trips/ + apps/trip/tests/VietRide.Trip.IntegrationTests/Internal/Trips/. (Q2 RESOLVED -- the BSOT §9.9 row was already patched to `seat_lock:{tripId}:{seatNumber}` owner Trip in BSOT changelog v1.10.0; NO BSOT edit in this task. The worker MUST NOT touch BACKEND_SOURCE_OF_TRUTH.md.) |
| forbidden scope | .env, secrets; db-schema; BACKEND_SOURCE_OF_TRUTH.md + VietRide_API_Contract_v1.md (SOT already reconciled for the Trip-owned TTL registry in BSOT v1.11.2 and the frozen API seam -- read-only during Task 11.4 implementation); apps/booking (do NOT edit the Booking client or stub -- flipping Booking off the stub is a Day-12-carryover follow-up, see Open Q3); other services; libs/** except the two explicitly owned shared files (`libs/dotnet/VietRide.Shared.Application/Exceptions/ApplicationExceptions.cs` and `libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseExceptionFilter.cs`); no other shared-lib edits; apps/gateway (internal endpoints are NOT gatewayed); git ops; do NOT emit events on the seat path (technical_context_v7 6.10 -- sync HTTP only, no event on seat path); do NOT add `lock_expires_at`, `seat_lock_token`, `hold_owner_id`, or any other schema column/table for TTL release unless SOT/schema are changed first |
| depends on | 11.0 (Redis seat-lock store), 11.1 (trip_seats). Request/response shapes FROZEN to apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients/ITripServiceClient.cs + DevTripServiceClient.cs (read-only reference) |
| invariant flags | CRLF; Internal JWT scheme only (HS256, audience vietride-internal); controllers stay thin (InternalTripsController calls MediatR/filter path only; no hand-rolled ApiResponse/error JSON); one class per file; no nested filter classes in InternalTripsController.cs; raw DTO (no ApiResponse envelope) on GET internal trip 200 -- envelope only on errors (API contract 1.6.2); lock-seats 200 MUST return API-contract ApiResponse envelope; GET internal trip 200 remains raw DTO; release-seats and book-seats remain 204 empty body; idempotency required on lock-seats (missing/blank Idempotency-Key enforced via filter/exception path, not manual action JSON; replay same Idempotency-Key returns same seatLockToken); all-or-nothing lock; release/book idempotent; Redis key TTL is the only expiry clock (no DB expiry column), sourced from `ttlSeconds` default 600s in the API contract and the Trip-owned BSOT §11.3 Trip-Route-Vehicle env registry (`SEAT_LOCK_TTL_MINUTES=10`) via the Task 11.0 Trip config seam (`SeatLock:TtlMinutes`, default 10); TTL auto-release = Trip-side recurring Hangfire cleanup every 1 minute scans HELD trip_seats in batches and flips rows to AVAILABLE when Redis `seat_lock:{tripId}:{seatNumber}` no longer exists; lock-seats also runs the same reconciliation for requested seats before checking availability so an expired HELD row is lockable immediately after TTL; book-seats verifies each Redis key exists and is owned by the provided seatLockToken; missing/expired keys or wrong-token ownership are treated as unavailable with 409 BOOKING_SEAT_UNAVAILABLE through the global exception filter with error.fields and must not book; shared-lib edits are limited to adding a reusable coded 409 exception with field-level ValidationErrors and its ApiResponseExceptionFilter mapping; no behavior changes to existing exception arms; seat status machine AVAILABLE-HELD-BOOKED |
| acceptance | GET internal trip returns the raw snapshot (API contract 1072-1110) matching Booking TripSnapshot, INCLUDING the `returnRouteId` field (uuid | null) per the v1.10.0-patched snapshot (line 1097; cites technical_context_v7 line 1750) so Booking's Day-13 ROUTE_RETURN_NOT_CONFIGURED 422 guard can validate real data; lock-seats success response MUST return the API-contract ApiResponse envelope, while GET internal trip 200 remains raw DTO; lock-seats all-or-nothing (one seat unavailable means none locked, BOOKING_SEAT_UNAVAILABLE goes through the global exception filter as HTTP 409 with error.fields); missing/blank Idempotency-Key for lock-seats is rejected via filter/exception path with the global envelope/error shape, not manual action JSON; trip not SCHEDULED returns 409 BOOKING_TRIP_NOT_BOOKABLE; release-seats idempotent 204; book-seats flips HELD to BOOKED 204 only while every requested seat's Redis key still exists and is owned by the seatLockToken; wrong seatLockToken is treated exactly like an unavailable/expired lock and returns 409 BOOKING_SEAT_UNAVAILABLE without booking; tests cover both token mismatch and missing/expired key paths; concurrency test: 2+ concurrent locks on same seat yields exactly one winner (DoD CO1); TTL tests: after Redis TTL elapses, (a) lock-seats on the same seat first reconciles the missing `seat_lock:` key and can acquire a new lock, and (b) the recurring cleanup command/job flips a stale HELD row to AVAILABLE without adding schema columns; Internal JWT required (missing and tampered return 401); integration/auth tests cover Internal JWT missing/tampered 401 and at least 1 happy + 1 error for each seam endpoint; build + format clean |
| source citations | VietRide_API_Contract_v1.md lines 975-998 (seam ownership), 984-990 (lock/book/release lifecycle + Redis TTL), 1065-1179 (4 endpoints + request/response + error codes), 1126-1127 (default TTL = 600s / `SEAT_LOCK_TTL_MINUTES * 60`), 1151-1153 (release HELD -> AVAILABLE), 1167-1168 and 1181-1184 (book requires unexpired token; expired token returns 409), 1097/1106-1109 (returnRouteId field + notes, v1.10.0-patched, cites technical_context_v7 line 1750); BACKEND_SOURCE_OF_TRUTH.md §11.3 Trip-Route-Vehicle env registry (`SEAT_LOCK_TTL_MINUTES=10`) is the Trip-owned runtime config source for TTL; technical_context_v7 lines 3386-3399 (sync seat-lock saga, Redis key, TTL); BE_TIMELINE_VU.md lines 141-146 (Redis lock, status machine, timeout release review); db-schema/trip-route-vehicle/schema.sql lines 407-419 (trip_seats has status only for lock state; no expiry/token columns); BSOT lines 1336-1337/1360 (error codes only); BSOT §9.9 line 2151 (Redis-namespace row ALREADY patched to `seat_lock:{tripId}:{seatNumber}` owner Trip, changelog v1.10.0 -- implement this prefix, no BSOT edit). Booking-side `SEAT_LOCK_TTL_MINUTES` remains client/default-only and is not the Trip-owned TTL registry. apps/booking ITripServiceClient.cs + DevTripServiceClient.cs (frozen contract); existing InternalStationsController.cs (Internal JWT auth pattern) |

## Dispatch order
1. Task 11.0 (baseline — Hangfire + Redis seat-lock seam) — blocks all below. Q1 RESOLVED (Hangfire.AspNetCore APPROVED, free MIT 1.8.x); shared CPM `<PackageVersion>` with Day-15 Task 15.5 — check Directory.Packages.props before adding to avoid duplicate-entry merge conflict.
2. Task 11.1 (Trip aggregate + migration) — depends 11.0.
3. Task 11.2-pre (docs-only SOT/contract patch) must run before Task 11.2 code dispatch and before any remaining code task that depends on the patched contract.
4. Task 11.2 (Hangfire generation + DriverSchedule activation/role validation) and Task 11.4 (internal seam) both depend on 11.1; Task 11.2 also depends on 11.2-pre.
   - Parallel-safe = yes for feature folders (Features/TripGeneration + Jobs + skip-log repo vs
      Features/Internal/Trips), BUT Task 11.2 owns
      InfrastructureServiceCollectionExtensions.cs for ITripGenerationJobScheduler +
      ITripGenerationSkipLogRepository + ITripStopFareRepository registration and may also touch
      apps/identity minimal internal lookup files for the Day-9 carryover; 11.4 may also need DI/Hangfire cleanup
      work - run SERIAL in the current tree to avoid DI-registration or cross-solution review conflicts; STOP
      and ask if a truly shared file needs both. (Q2 RESOLVED - 11.4 implements the
      `seat_lock:{tripId}:{seatNumber}` prefix; TTL auto-release is Trip-side recurring cleanup +
      lock-path reconciliation using Redis key expiry; no schema edit needed, and the Trip-owned TTL env registry is already patched in BSOT v1.11.2.)
5. Task 11.3 (FE endpoints + Gateway) — depends 11.1, 11.2 (needs generated trips to search).
   Serial after 11.4 (shared DI registration file). Execute as 11.3a dotnet-worker then 11.3b
   nest-worker, with separate reviewers, but keep one intended Task-11.3 commit after both approve;
   dotnet-worker MUST NOT edit apps/gateway and nest-worker MUST NOT edit apps/trip.

## Progress tracker
> Orchestrator bookkeeping — informational only, NOT audit evidence. /audit-day re-verifies.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 11.0 | done | APPROVE | 2026-06-12 | Patch round: Redis key uses canonical `seat_lock:{tripId}:{seatNumber}`; pending human verify. |
| 11.1 | done | APPROVE | 2026-06-12 | Patch rounds fixed row_version drift, skip-log schedule index, and TripSeat state machine; pending human verify. |
| 11.2-pre | done | APPROVE | 2026-06-13 | Docs-only SOT/contract patch for activation endpoint + Day-9 carryover role/operator validation; added after PLAN-REVIEW blockers. |
| 11.2 | done | APPROVE | 2026-06-13 | Multiple patch rounds: activation contract, Identity role/operator validation, recurring job, TripStopFare snapshots, and in-run idempotency fixed; pending human verify. |
| 11.3 | todo | — | — | Execute as 11.3a dotnet + 11.3b Gateway (separate dispatch/review, one intended 11.3 commit). |
| 11.4 | todo | — | — | Trip side of Day-12 seam (CO1/CO2). Q2 RESOLVED (`seat_lock:` prefix, owner Trip; no BSOT edit). Snapshot now exposes returnRouteId (Day-13 CO3) |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions
> All Day-11 open questions are RESOLVED by the human (Vu): Q1-Q7 on 2026-06-12 and
> Q8-Q9 on 2026-06-13. Kept here with their resolutions for the audit trail; [BLOCKS] markers removed.
1. **Q1 — RESOLVED, APPROVED.** `Hangfire.AspNetCore` is approved for Directory.Packages.props
   (alongside pre-approved `Hangfire.PostgreSql`, BSOT §2.1). Pin the free MIT 1.8.x line;
   Hangfire Pro/Ace (commercial) forbidden. Same decision already made for Day 15 (day-15-plan.md
   OQ-3a) — the CPM `<PackageVersion>` entries are SHARED: whichever of Task 11.0 / Task 15.5
   lands first adds them, the other becomes a no-op. Check Directory.Packages.props before adding
   to avoid a duplicate-entry merge conflict.
2. **Q2 — RESOLVED.** Redis seat-lock key = `seat_lock:{tripId}:{seatNumber}`, owner Trip (API
   Contract + BSOT changelog 1.8.0 win over the old §9.9 row). The BSOT §9.9 row was ALREADY
   patched to read `seat_lock:{tripId}:{seatNumber}` / owner Trip (BSOT changelog v1.10.0, line
   2151). No BSOT edit remains in Task 11.4 — it simply implements the `seat_lock:` prefix.
3. **Q3 — RESOLVED (default confirmed).** Retiring the Booking DevTripServiceClient stub (flipping
   Booking onto the real Trip internal endpoints) is NOT in Day-11 scope; it stays a
   Day-12-carryover follow-up. Task 11.4 remains forbidden from editing apps/booking.
4. **Q4 — RESOLVED.** GET /v1/trips/{tripId} `TripDetailDto` = ApiResponse-wrapped projection of
   the internal snapshot fields (API Contract ~1072-1109) + stops + seat summary + fare breakdown
   (the plan's stated default). Committed in Task 11.3 acceptance.
5. **Q5 — RESOLVED.** Deterministic fallback chain for `trips.estimated_arrival_time`:
   (1) `departureDateTime + Route.estimatedDurationMinutes`; (2) if NULL,
   `departureDateTime + max(RouteStop.estimatedDurationFromOriginMinutes)`; (3) if neither
   available, REFUSE DriverSchedule activation / trip generation with a validation error (NO
   invented default). Committed in Task 11.2 acceptance.
6. **Q6 -- RESOLVED.** Task 11.4 TTL auto-release uses existing SOT/schema only: Redis key TTL is
   the canonical expiry clock; Trip adds a recurring Hangfire cleanup every 1 minute plus
   lock-path reconciliation to release HELD trip_seats whose `seat_lock:{tripId}:{seatNumber}` key
   no longer exists. No schema columns/tables are added.
7. **Q7 -- RESOLVED.** Day-11 trip search implements the API Contract query params only
   (`originStationId`, `destinationStationId`, `departureDate`, `passengerCount`,
   `allowAlongRoutePickup?`). Timeline operator/time/price filters are carryover/out of scope until
   a future API-contract change adds explicit query params.
8. **Q8 -- RESOLVED (human decision 2026-06-13).** Day 11 introduces a documented activation
   endpoint instead of leaving DriverSchedule activation undefined: `PATCH
   /v1/operator/driver-schedules/{id}/activate`, OPERATOR_ADMIN, no body, no Idempotency-Key
   requirement, `200 ApiResponse<DriverScheduleDto>`, already-active returns the current DTO and
   does not enqueue duplicate generation, existing Gateway prefix covers the route. Task 11.2-pre
   patches the SOT/contract before Task 11.2 code dispatch.
9. **Q9 -- RESOLVED (human decision 2026-06-13).** Day-9 carryover role/operator validation is in
   Day 11: DriverSchedule create and activation validate driver `role=DRIVER` and assistant
   `role=ASSISTANT` (when supplied) under the caller operator via the existing/internal Identity
   client pattern; if the Identity lookup contract/endpoint is still absent, Task 11.2-pre
   documents it and Task 11.2 may add the minimal Identity internal lookup implementation.


### Scope addition (from Day-13 CO3)
- The internal trip snapshot DTO (Task 11.4, GET /internal/v1/trips/{tripId}) must now expose
  `returnRouteId` (uuid | null). The API Contract internal snapshot (~lines 1096-1109) was
  ALREADY patched to include it (line 1097; cites technical_context_v7 line 1750 — Booking
  validates 422 ROUTE_RETURN_NOT_CONFIGURED). Added to Task 11.4 acceptance.
- OWNERSHIP SPLIT (cross-day, resolves the PLAN-REVIEW blocker): Task 11.4 owns ONLY the Trip
  server side of the field; the Booking-side `TripSnapshot.ReturnRouteId` record field (
  apps/booking/.../ServiceClients/ITripServiceClient.cs + DevTripServiceClient.cs stub) is owned
  by **Day-13 Task 13.0** — Task 11.4 stays forbidden from apps/booking. The serialized JSON
  property name is `returnRouteId` on both sides (frozen seam).
