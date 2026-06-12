# Day 11 — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 11 (SCV-80) — Trip Search API + Trip auto-generation
- **Prior checklist**: docs/handoff/day-10-checklist.md (Day-11 was never planned — work jumped to Day 12). See Sequencing note.
- **Plan status**: APPROVED -- latest PLAN-REVIEW approved the patched plan on 2026-06-12; ready for not-yet-started worker dispatch.
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
      DriverSchedule activation/update where `isActive false -> true` (Day-11 minimal activation
      path under existing `/v1/operator/driver-schedules` prefix), AND weekly (CN 23:00) generates
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
      1107-1179 with Redis seat-lock keys (TTL 10 min, SEAT_LOCK_TTL_MINUTES), trip_seats status
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
  GET /v1/trips/{tripId}/seat-map — VietRide_API_Contract_v1.md lines 1000-1062 (already
  documented; no contract edit needed, verify shapes match).
- Search-filter precedence: BE_TIMELINE_VU.md line 132 says `GET /trips/search?origin=&destination=&date=`
  with filter by operator/time/price, but VietRide_API_Contract_v1.md line 1004 is the higher-precedence
  endpoint contract for the current API. Day 11 implements only `originStationId`,
  `destinationStationId`, `departureDate`, `passengerCount`, and `allowAlongRoutePickup?`. Extra
  operator/time/price query filters are out of Day-11 scope/carryover and require a future API
  contract change before implementation. Existing response fields `operatorId`, `operatorName`,
  `departureDateTime`, and `baseFare` remain returned, but they are not additional filters.
- New internal seam endpoints: GET /internal/v1/trips/{tripId},
  POST /internal/v1/trips/{tripId}/lock-seats, /release-seats, /book-seats —
  VietRide_API_Contract_v1.md lines 1065-1179 (frozen seam, BSOT 13 row 1.8.0). Trip is the server
  side; Booking is the already-built client. No shape change permitted.
- Gateway routes: the existing `/v1/trips` entry (apps/gateway/src/config/routes.ts ~line 112) is
  currently `authRequired: 'user'` — the nest-worker must CHANGE it to `authRequired: 'mixed'` with
  `publicSubpaths: [{ method: 'GET', path: '/v1/trips/search' }]` so search works unauthenticated
  while detail/seat-map stay protected (User JWT). Do NOT add a duplicate prefix entry. Internal
  endpoints are NOT exposed via Gateway (Internal JWT only, service-to-service).
- Events: none. BSOT 7.3 trip event registry (lines 1742-1748) has no trip.trip.generated key;
  Day-11 generation emits no event. Do not invent one.
- Error codes: all pre-exist in BSOT 5.9 (TRIP_NOT_FOUND line 1360, BOOKING_SEAT_UNAVAILABLE line
  1336, BOOKING_TRIP_NOT_BOOKABLE line 1337). No registry edit.
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
| acceptance | dotnet build apps/trip/VietRide.Trip.sln -c Release 0W/0E; dotnet format --verify-no-changes clean; Trip app boots with Hangfire registered against vietride_trip hangfire schema; Redis seat-lock store resolvable from DI (StackExchange.Redis already transitive via Shared.Web); no Version= on any csproj ref (CPM hook passes) |
| source citations | technical_context_v7 lines 354/360-369 (Hangfire per-service schema, not for outbox); BSOT line 2363 (SEAT_LOCK_TTL_MINUTES=10); db-schema/trip-route-vehicle/README.md line 12 (Hangfire jobs in this DB); AGENTS.md Hard invariants (CPM, banned deps) |

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

### Task 11.2 — Trip auto-generation Hangfire job (on-create + activation + weekly CN 23:00, idempotent)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Application/Abstractions/Jobs/ITripGenerationJobScheduler.cs (Application scheduler abstraction; DriverSchedule handlers depend on this, never on Hangfire/Infrastructure); new apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripGenerationSkipLogRepository.cs; new apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/GenerateTripsForScheduleCommand.cs + Handler + Validator; new TripGenerationService.cs under the same folder (seats from Vehicle.seatLayoutJson, stops from RouteStop, estimatedArrivalTime via the Q5-resolved deterministic fallback chain - see acceptance/invariants); new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/TripGenerationJob.cs (Hangfire job executor that sends GenerateTripsForScheduleCommand via MediatR; no generation/domain logic); new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/HangfireTripGenerationJobScheduler.cs (implements ITripGenerationJobScheduler using Hangfire BackgroundJob/RecurringJob APIs); new apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/TripGenerationSkipLogRepository.cs; edit apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (register ITripGenerationJobScheduler + ITripGenerationSkipLogRepository only); edit ONLY the existing apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/CreateDriverScheduleHandler.cs to enqueue via ITripGenerationJobScheduler after a successful schedule commit (no validation change); add minimal activation-only path in existing DriverSchedules feature/controller: new apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/ActivateDriverScheduleCommand.cs + ActivateDriverScheduleHandler.cs + ActivateDriverScheduleValidator.cs (or a single validator-free command if no request body); edit apps/trip/src/VietRide.Trip.Api/Controllers/OperatorDriverSchedulesController.cs to add activation-only action `PATCH /v1/operator/driver-schedules/{id}/activate` under the existing `v1/operator/driver-schedules` route prefix (no broader edit body); if the Api project uses request records for no-body actions, add only the minimal request file under apps/trip/src/VietRide.Trip.Api/Controllers/Requests/; register recurring job in apps/trip/src/VietRide.Trip.Api/Program.cs (job registration block only); new tests under apps/trip/tests/VietRide.Trip.UnitTests/Features/TripGeneration/ and DriverSchedules activation tests |
| forbidden scope | .env, secrets; db-schema (read-only); other services; libs; apps/gateway (existing Gateway prefix already routes `/v1/operator/driver-schedules/**` to Trip); git ops; do NOT reference Hangfire.* or Infrastructure from VietRide.Trip.Application (use ITripGenerationJobScheduler); do NOT implement full DriverSchedule edit/cascade (departureTime/dayOfWeek/driver/assistant/vehicle/validUntil updates with FUTURE_ONLY/ALL_PENDING are technical_context_v7 §6.11.1 / Day-18+ scope); activation path may only transition `isActive false -> true` and enqueue generation after successful commit; do NOT touch DriverSchedule conflict-validation logic beyond reusing the existing active-schedule conflict check for activation; do NOT add auto-BOARDING / auto-COMPLETED jobs (Day-21 scope); do NOT emit any event (no trip.trip.generated key exists) |
| depends on | 11.0, 11.1 |
| invariant flags | CRLF; idempotency = check (driverUserId, departureDateTime) AND (vehicleId, departureDateTime) before INSERT (re-run no dup); Trip.source = AUTO_FROM_SCHEDULE; skip seats where disabled true in seatLayoutJson; TripStop.estimatedArrivalTime static, never recomputed after generate; trips.estimated_arrival_time (Q5 RESOLVED) = deterministic fallback chain: (1) departureDateTime + Route.estimatedDurationMinutes; (2) if NULL, departureDateTime + max(RouteStop.estimatedDurationFromOriginMinutes); (3) if neither available, REFUSE generation/DriverSchedule activation with a validation error — NO invented default; Money to-the-đồng on base_fare snapshot (copy verbatim, no rounding — BSOT v1.11.0); MediatR v11 |
| acceptance | Application layer has no Hangfire.* or Infrastructure references; DriverSchedule create enqueues via ITripGenerationJobScheduler only after SaveChanges succeeds; activation-only command/action exists and changes only `isActive false -> true`, returns DriverScheduleDto in ApiResponse envelope, and enqueues via ITripGenerationJobScheduler only after the activation commit succeeds; activating an already-active schedule is idempotent/no duplicate generation enqueue (or returns the unchanged DTO without a new enqueue); activation reuses active-schedule conflict checks so enabling a conflicting row fails with 409 TRIP_DRIVER_CONFLICT and does not enqueue; recurring CN 23:00 job is registered through Infrastructure/Hangfire and executes GenerateTripsForScheduleCommand via MediatR; unit test: schedule with dayOfWeek 2 and 4 generates Trips only for matching dates in next 14 days; idempotent test re-run same day = 0 new rows (DoD trip generation idempotent); seats generated = totalSeats minus disabled count; trip_stops snapshot orderIndex/allow flags/distance; estimated_arrival_time uses the Q5 fallback chain - test: Route.estimatedDurationMinutes set -> arrival = departure+duration; Route.estimatedDurationMinutes NULL but RouteStops present -> arrival = departure+max(estimatedDurationFromOriginMinutes); neither available -> generation refuses with a validation error (no default value persisted); build + format clean; at least 1 happy + 1 skip-path test (e.g. missing vehicle creates a TripGenerationSkipLog row via ITripGenerationSkipLogRepository with reason) + 1 activation trigger test; NetArchTest green |
| source citations | technical_context_v7 lines 3666-3707 (on-create/update activation + weekly generation algorithm, idempotent check + vehicle conflict), lines 3709-3800 (full edit cascade exists but is NOT Day-11 activation-only scope), line 1989 (TripSeat from seatLayoutJson skip disabled), line 3600 (TripStop.estimatedArrivalTime formula); BE_TIMELINE_VU.md lines 134 and 136 (Day-11 generation on DriverSchedule activation + Sunday 23:00, DoD); VietRide_API_Contract_v1.md lines 3064-3085 (DriverScheduleDto includes `isActive`), 3087-3118 (existing create contract; Day-9 no generation), 3113 (TRIP_DRIVER_CONFLICT); existing apps/trip/src/VietRide.Trip.Api/Controllers/OperatorDriverSchedulesController.cs line 12 (`v1/operator/driver-schedules` route prefix) and apps/gateway/src/config/routes.ts line 152 (existing Gateway prefix); schema.sql lines 462-470 (trip_generation_skip_logs); existing Entities/DriverSchedule.cs + RouteStop.cs; repo discovery 2026-06-12: CreateDriverScheduleHandler.cs exists, no activation handler exists under Features/DriverSchedules/ before this task |

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
| owned files (write set) | new apps/trip/src/VietRide.Trip.Api/Controllers/InternalTripsController.cs (route internal/v1/trips, Authorize with InternalJwtAuthenticationExtensions.Scheme); new apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/GetTripSnapshot/ (Query, Handler, InternalTripSnapshotDto); .../Internal/Trips/LockSeats/ (Command, Handler, Validator, LockSeatsResult); .../Internal/Trips/BookSeats/ (Command, Handler); .../Internal/Trips/ReleaseSeats/ (Command, Handler); request DTOs under .../Internal/Trips/Requests/; new apps/trip/src/VietRide.Trip.Application/Abstractions/SeatLock/IExpiredSeatLockReleaser.cs; new apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/ReleaseExpiredSeatLocks/ (Command + Handler or equivalent single-responsibility Application service); new apps/trip/src/VietRide.Trip.Infrastructure/SeatLock/ExpiredSeatLockReleaser.cs; new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/ExpiredSeatLockReleaseJob.cs; apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (only if DI registration is needed); apps/trip/src/VietRide.Trip.Infrastructure/Jobs/HangfireServiceCollectionExtensions.cs and/or apps/trip/src/VietRide.Trip.Api/Program.cs (only to register the recurring expired-lock cleanup); seat-lock orchestration via ISeatLockStore (from 11.0) + trip_seats transitions; new tests under apps/trip/tests/VietRide.Trip.UnitTests/Features/Internal/Trips/ + apps/trip/tests/VietRide.Trip.IntegrationTests/Internal/Trips/. (Q2 RESOLVED -- the BSOT §9.9 row was already patched to `seat_lock:{tripId}:{seatNumber}` owner Trip in BSOT changelog v1.10.0; NO BSOT edit in this task. The worker MUST NOT touch BACKEND_SOURCE_OF_TRUTH.md.) |
| forbidden scope | .env, secrets; db-schema; BACKEND_SOURCE_OF_TRUTH.md + VietRide_API_Contract_v1.md (SOT already reconciled in v1.10.0 -- read-only); apps/booking (do NOT edit the Booking client or stub -- flipping Booking off the stub is a Day-12-carryover follow-up, see Open Q3); other services; libs; apps/gateway (internal endpoints are NOT gatewayed); git ops; do NOT emit events on the seat path (technical_context_v7 6.10 -- sync HTTP only, no event on seat path); do NOT add `lock_expires_at`, `seat_lock_token`, `hold_owner_id`, or any other schema column/table for TTL release unless SOT/schema are changed first |
| depends on | 11.0 (Redis seat-lock store), 11.1 (trip_seats). Request/response shapes FROZEN to apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients/ITripServiceClient.cs + DevTripServiceClient.cs (read-only reference) |
| invariant flags | CRLF; Internal JWT scheme only (HS256, audience vietride-internal); raw DTO (no ApiResponse envelope) on GET internal trip 200 -- envelope only on errors (API contract 1.6.2); idempotency required on lock-seats (replay same Idempotency-Key returns same seatLockToken); all-or-nothing lock; release/book idempotent; Redis key TTL 10 min is the only expiry clock (no DB expiry column); TTL auto-release = Trip-side recurring Hangfire cleanup every 1 minute scans HELD trip_seats in batches and flips rows to AVAILABLE when Redis `seat_lock:{tripId}:{seatNumber}` no longer exists; lock-seats also runs the same reconciliation for requested seats before checking availability so an expired HELD row is lockable immediately after TTL; book-seats treats missing/expired Redis key as 409 BOOKING_SEAT_UNAVAILABLE and must not book; seat status machine AVAILABLE-HELD-BOOKED |
| acceptance | GET internal trip returns the raw snapshot (API contract 1072-1110) matching Booking TripSnapshot, INCLUDING the `returnRouteId` field (uuid | null) per the v1.10.0-patched snapshot (line 1097; cites technical_context_v7 line 1750) so Booking's Day-13 ROUTE_RETURN_NOT_CONFIGURED 422 guard can validate real data; lock-seats all-or-nothing (one seat unavailable means none locked, 409 BOOKING_SEAT_UNAVAILABLE with error.fields); trip not SCHEDULED returns 409 BOOKING_TRIP_NOT_BOOKABLE; release-seats idempotent 204; book-seats flips HELD to BOOKED 204 only while every requested seat's Redis key still exists and is owned by the seatLockToken; concurrency test: 2+ concurrent locks on same seat yields exactly one winner (DoD CO1); TTL tests: after Redis TTL elapses, (a) lock-seats on the same seat first reconciles the missing `seat_lock:` key and can acquire a new lock, and (b) the recurring cleanup command/job flips a stale HELD row to AVAILABLE without adding schema columns; Internal JWT required (tampered returns 401); build + format clean; at least 1 happy + 1 error each |
| source citations | VietRide_API_Contract_v1.md lines 975-998 (seam ownership), 984-990 (lock/book/release lifecycle + Redis TTL), 1065-1179 (4 endpoints + request/response + error codes), 1126-1127 (default TTL = 600s), 1151-1153 (release HELD -> AVAILABLE), 1167-1168 and 1181-1184 (book requires unexpired token; expired token returns 409), 1097/1106-1109 (returnRouteId field + notes, v1.10.0-patched, cites technical_context_v7 line 1750); technical_context_v7 lines 3386-3399 (sync seat-lock saga, Redis key, TTL); BE_TIMELINE_VU.md lines 141-146 (Redis lock, status machine, timeout release review); db-schema/trip-route-vehicle/schema.sql lines 407-419 (trip_seats has status only for lock state; no expiry/token columns); BSOT line 2363 (SEAT_LOCK_TTL_MINUTES=10), lines 1336-1337/1360 (error codes); BSOT §9.9 line 2151 (Redis-namespace row ALREADY patched to `seat_lock:{tripId}:{seatNumber}` owner Trip, changelog v1.10.0 -- implement this prefix, no BSOT edit); apps/booking ITripServiceClient.cs + DevTripServiceClient.cs (frozen contract); existing InternalStationsController.cs (Internal JWT auth pattern) |

## Dispatch order
1. Task 11.0 (baseline — Hangfire + Redis seat-lock seam) — blocks all below. Q1 RESOLVED (Hangfire.AspNetCore APPROVED, free MIT 1.8.x); shared CPM `<PackageVersion>` with Day-15 Task 15.5 — check Directory.Packages.props before adding to avoid duplicate-entry merge conflict.
2. Task 11.1 (Trip aggregate + migration) — depends 11.0.
3. Task 11.2 (Hangfire generation) and Task 11.4 (internal seam) both depend on 11.1.
   - Parallel-safe = yes for feature folders (Features/TripGeneration + Jobs + skip-log repo vs
      Features/Internal/Trips), BUT Task 11.2 now owns
      InfrastructureServiceCollectionExtensions.cs for ITripGenerationJobScheduler +
      ITripGenerationSkipLogRepository registration and 11.4 may also need DI/Hangfire cleanup
      registration - run SERIAL in the current tree to avoid a DI-registration merge conflict; STOP
      and ask if a truly shared file needs both. (Q2 RESOLVED - 11.4 implements the
      `seat_lock:{tripId}:{seatNumber}` prefix; TTL auto-release is Trip-side recurring cleanup +
      lock-path reconciliation using Redis key expiry; no BSOT/schema edit needed.)
4. Task 11.3 (FE endpoints + Gateway) — depends 11.1, 11.2 (needs generated trips to search).
   Serial after 11.4 (shared DI registration file). Execute as 11.3a dotnet-worker then 11.3b
   nest-worker, with separate reviewers, but keep one intended Task-11.3 commit after both approve;
   dotnet-worker MUST NOT edit apps/gateway and nest-worker MUST NOT edit apps/trip.

## Progress tracker
> Orchestrator bookkeeping — informational only, NOT audit evidence. /audit-day re-verifies.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 11.0 | done | APPROVE | 2026-06-12 | Patch round: Redis key uses canonical `seat_lock:{tripId}:{seatNumber}`; pending human verify. |
| 11.1 | done | APPROVE | 2026-06-12 | Patch rounds fixed row_version drift, skip-log schedule index, and TripSeat state machine; pending human verify. |
| 11.2 | todo | — | — | — |
| 11.3 | todo | — | — | Execute as 11.3a dotnet + 11.3b Gateway (separate dispatch/review, one intended 11.3 commit). |
| 11.4 | todo | — | — | Trip side of Day-12 seam (CO1/CO2). Q2 RESOLVED (`seat_lock:` prefix, owner Trip; no BSOT edit). Snapshot now exposes returnRouteId (Day-13 CO3) |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions
> All Day-11 open questions RESOLVED by the human (Vũ) on 2026-06-12. Kept here with their
> resolutions for the audit trail; [BLOCKS …] markers removed.
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
