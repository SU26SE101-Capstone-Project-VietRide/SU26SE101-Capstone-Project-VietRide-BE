# Day 11 — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 11 (SCV-80) — Trip Search API + Trip auto-generation
- **Prior checklist**: docs/handoff/day-10-checklist.md (Day-11 was never planned — work jumped to Day 12). See Sequencing note.
- **Plan status**: APPROVED — PLAN-REVIEW ran 2026-06-12 (REVISION-REQUIRED), all findings patched same day (SOT-commit precondition added; Gateway /v1/trips auth-mode change specified; seat-map geometry source specified; Postman ownership resolved as post-merge step; BSOT 5.9 citations corrected; Money rule updated to BSOT v1.11.0)
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
from active DriverSchedules (idempotent, on-create + weekly CN 23:00), the FE-facing
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
- [ ] Trip auto-generation Hangfire job: on DriverSchedule create/activate (immediate one-off) AND
      weekly (CN 23:00) generates Trips for the next 14 days matching dayOfWeek; idempotent on
      (driverUserId, departureDateTime) + (vehicleId, departureDateTime); generates trip_seats from
      Vehicle.seatLayoutJson (skip disabled true) and trip_stops from RouteStop (snapshot
      orderIndex/allowPickup/allowDropoff/distanceFromOriginKm + computed estimatedArrivalTime).
      Re-run same day = no duplicate.
- [ ] GET /v1/trips/search (originStationId, destinationStationId, departureDate, passengerCount,
      allowAlongRoutePickup?) returns the paged envelope in API contract lines 1000-1037, joins
      Trip-Route-Stations with availableSeats count; no result = empty 200 (NOT 404).
- [ ] GET /v1/trips/{tripId} returns trip detail (route, stations, stops, seat summary, fare
      summary); GET /v1/trips/{tripId}/seat-map returns the seat array (API contract lines
      1039-1062). 404 TRIP_NOT_FOUND on unknown id.
- [ ] GET /internal/v1/trips/{tripId} returns the raw (un-enveloped) snapshot DTO in API contract
      lines 1065-1097; matches the TripSnapshot shape consumed by Booking ITripServiceClient.
- [ ] POST /internal/v1/trips/{tripId}/lock-seats (all-or-nothing, idempotent on Idempotency-Key),
      /release-seats (idempotent 204), /book-seats (204) implement the seam in API contract lines
      1107-1179 with Redis seat-lock keys (TTL 10 min, SEAT_LOCK_TTL_MINUTES), trip_seats status
      machine AVAILABLE-HELD-BOOKED + HELD-AVAILABLE.
- [ ] Redis 10-min TTL expiry releases a HELD seat back to AVAILABLE (CO1) and concurrent same-seat
      lock attempts resolve to exactly one winner.
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

### Task 11.2 — Trip auto-generation Hangfire job (on-create + weekly CN 23:00, idempotent)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/GenerateTripsForScheduleCommand.cs + Handler + Validator; new TripGenerationService.cs under the same folder (seats from Vehicle.seatLayoutJson, stops from RouteStop, estimatedArrivalTime via the Q5-resolved deterministic fallback chain — see acceptance/invariants); new apps/trip/src/VietRide.Trip.Infrastructure/Jobs/TripGenerationJob.cs (Hangfire BackgroundJob.Enqueue on-create + RecurringJob CN 23:00); edit ONLY the DriverSchedule create/activate handler(s) under apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/ to enqueue (no validation change); register recurring job in apps/trip/src/VietRide.Trip.Api/Program.cs (job registration block only); new tests under apps/trip/tests/VietRide.Trip.UnitTests/Features/TripGeneration/ |
| forbidden scope | .env, secrets; db-schema (read-only); other services; libs; apps/gateway; git ops; do NOT touch DriverSchedule conflict-validation logic (Day-9, frozen); do NOT add auto-BOARDING / auto-COMPLETED jobs (Day-21 scope); do NOT emit any event (no trip.trip.generated key exists) |
| depends on | 11.0, 11.1 |
| invariant flags | CRLF; idempotency = check (driverUserId, departureDateTime) AND (vehicleId, departureDateTime) before INSERT (re-run no dup); Trip.source = AUTO_FROM_SCHEDULE; skip seats where disabled true in seatLayoutJson; TripStop.estimatedArrivalTime static, never recomputed after generate; trips.estimated_arrival_time (Q5 RESOLVED) = deterministic fallback chain: (1) departureDateTime + Route.estimatedDurationMinutes; (2) if NULL, departureDateTime + max(RouteStop.estimatedDurationFromOriginMinutes); (3) if neither available, REFUSE generation/DriverSchedule activation with a validation error — NO invented default; Money to-the-đồng on base_fare snapshot (copy verbatim, no rounding — BSOT v1.11.0); MediatR v11 |
| acceptance | unit test: schedule with dayOfWeek 2 and 4 generates Trips only for matching dates in next 14 days; idempotent test re-run same day = 0 new rows (DoD trip generation idempotent); seats generated = totalSeats minus disabled count; trip_stops snapshot orderIndex/allow flags/distance; estimated_arrival_time uses the Q5 fallback chain — test: Route.estimatedDurationMinutes set → arrival = departure+duration; Route.estimatedDurationMinutes NULL but RouteStops present → arrival = departure+max(estimatedDurationFromOriginMinutes); neither available → generation refuses with a validation error (no default value persisted); build + format clean; at least 1 happy + 1 skip-path test (e.g. missing vehicle creates a TripGenerationSkipLog row with reason); NetArchTest green |
| source citations | technical_context_v7 lines 3666-3707 (2-trigger generation algorithm + idempotent check + vehicle conflict), line 1989 (TripSeat from seatLayoutJson skip disabled), line 3600 (TripStop.estimatedArrivalTime formula); Q5-resolved trip-level arrival fallback chain (Route.estimatedDurationMinutes nullable per Route.cs:20 → max RouteStop.estimatedDurationFromOriginMinutes → refuse, no default); line 3113 (Day-9 explicitly does NOT generate); schema.sql lines 462-470 (trip_generation_skip_logs); existing Entities/DriverSchedule.cs + RouteStop.cs |

### Task 11.3 — FE-facing endpoints: trip search + detail + seat-map + Gateway routes
| Field | Value |
|---|---|
| stack/owner | dotnet (+ Gateway route split to TS sub-step — see Dispatch order) |
| implement agent | dotnet-worker (controllers/handlers); Gateway route edit dispatched to nest-worker |
| review agent | dotnet-reviewer (.NET); nest-reviewer (Gateway route) |
| skill | add-endpoint |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Api/Controllers/TripsController.cs (route /v1/trips); new apps/trip/src/VietRide.Trip.Application/Features/Trips/SearchTrips/ (Query, Handler, Validator, SearchTripsResult, SearchTripItem); .../Features/Trips/GetTripDetail/ (Query, Handler, TripDetailDto — Q4-resolved field set: ApiResponse-wrapped projection of the internal snapshot fields (API Contract ~1072-1109) + stops + seat summary + fare breakdown); .../Features/Trips/GetTripSeatMap/ (Query, Handler, TripSeatMapDto); mappers under .../Features/Trips/; new integration tests under apps/trip/tests/VietRide.Trip.IntegrationTests/Trips/. Gateway sub-step (nest-worker, separate dispatch): apps/gateway/src/config/routes.ts (+ routes.spec.ts) — CHANGE the existing `/v1/trips` entry (~line 112) from `authRequired: 'user'` to `authRequired: 'mixed'` + `publicSubpaths: [{ method: 'GET', path: '/v1/trips/search' }]`; do NOT add a duplicate prefix entry |
| forbidden scope | .env, secrets; db-schema; other services; libs; git ops; the internal seam endpoints (Task 11.4 owns internal/v1/trips); the dotnet-worker MUST NOT edit apps/gateway (Gateway route = separate nest-worker dispatch) |
| depends on | 11.1 (entities), 11.2 (generated trips to search). Parallel-safe with 11.4 = no (shared InfrastructureServiceCollectionExtensions.cs DI + Features namespace neighborhood) |
| invariant flags | CRLF for .cs / LF for .ts (Gateway); ApiResponse envelope (ADR 0004) with meta.traceId; search no-result = empty 200 not 404; availableSeats = count of trip_seats with status AVAILABLE; auth: search optional, detail/seat-map protected (User JWT); MediatR v11 |
| acceptance | GET /v1/trips/search returns the paged shape (API contract 1006-1037) with correct availableSeats; no-match query returns 200 empty items (DoD); GET /v1/trips/{id} returns the Q4-resolved TripDetailDto = ApiResponse-wrapped projection of the internal snapshot fields (API Contract ~1072-1109) + stops + seat summary + fare breakdown; /seat-map matches contract 1039-1062 — NOTE: trip_seats has NO row/col/deck columns (schema.sql 407-419); the seat-map handler MUST load row/col/deck geometry by joining Trip -> Vehicle and parsing Vehicle.seatLayoutJson (match on seatNumber), merged with trip_seats status — do NOT invent a migration; unknown id returns 404 TRIP_NOT_FOUND; Gateway proxies /v1/trips with search public (mixed + publicSubpaths) and detail/seat-map protected; Swagger renders all three; build + format clean; at least 1 happy + 1 error integration test each |
| source citations | VietRide_API_Contract_v1.md lines 1000-1062 (search/detail/seat-map shapes); BSOT 5.4/5.5 (ApiResponse envelope), ADR 0004; technical_context_v7 lines 1981-1983 (seat-map fields row/col/deck/type); existing apps/gateway/src/config/routes.ts (mirror /v1/passenger Day-10 entry); timeline Day-11 Review (search no result returns empty 200 not 404) |

### Task 11.4 — Internal seat-lock seam: GET internal trip + lock/book/release-seats (Trip side of Day-12 seam)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | new apps/trip/src/VietRide.Trip.Api/Controllers/InternalTripsController.cs (route internal/v1/trips, Authorize with InternalJwtAuthenticationExtensions.Scheme); new apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/GetTripSnapshot/ (Query, Handler, InternalTripSnapshotDto); .../Internal/Trips/LockSeats/ (Command, Handler, Validator, LockSeatsResult); .../Internal/Trips/BookSeats/ (Command, Handler); .../Internal/Trips/ReleaseSeats/ (Command, Handler); request DTOs under .../Internal/Trips/Requests/; seat-lock orchestration via ISeatLockStore (from 11.0) + trip_seats transitions; new tests under apps/trip/tests/VietRide.Trip.UnitTests/Features/Internal/Trips/ + apps/trip/tests/VietRide.Trip.IntegrationTests/Internal/Trips/. (Q2 RESOLVED — the BSOT §9.9 row was already patched to `seat_lock:{tripId}:{seatNumber}` owner Trip in BSOT changelog v1.10.0; NO BSOT edit in this task. The worker MUST NOT touch BACKEND_SOURCE_OF_TRUTH.md.) |
| forbidden scope | .env, secrets; db-schema; BACKEND_SOURCE_OF_TRUTH.md + VietRide_API_Contract_v1.md (SOT already reconciled in v1.10.0 — read-only); apps/booking (do NOT edit the Booking client or stub — flipping Booking off the stub is a Day-12-carryover follow-up, see Open Q3); other services; libs; apps/gateway (internal endpoints are NOT gatewayed); git ops; do NOT emit events on the seat path (technical_context_v7 6.10 — sync HTTP only, no event on seat path) |
| depends on | 11.0 (Redis seat-lock store), 11.1 (trip_seats). Request/response shapes FROZEN to apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients/ITripServiceClient.cs + DevTripServiceClient.cs (read-only reference) |
| invariant flags | CRLF; Internal JWT scheme only (HS256, audience vietride-internal); raw DTO (no ApiResponse envelope) on GET internal trip 200 — envelope only on errors (API contract 1.6.2); idempotency required on lock-seats (replay same Idempotency-Key returns same seatLockToken); all-or-nothing lock; release/book idempotent; Redis key TTL 10 min; seat status machine AVAILABLE-HELD-BOOKED |
| acceptance | GET internal trip returns the raw snapshot (API contract 1072-1110) matching Booking TripSnapshot, INCLUDING the `returnRouteId` field (uuid | null) per the v1.10.0-patched snapshot (line 1097; cites technical_context_v7 line 1750) so Booking's Day-13 ROUTE_RETURN_NOT_CONFIGURED 422 guard can validate real data; lock-seats all-or-nothing (one seat unavailable means none locked, 409 BOOKING_SEAT_UNAVAILABLE with error.fields); trip not SCHEDULED returns 409 BOOKING_TRIP_NOT_BOOKABLE; release-seats idempotent 204; book-seats flips HELD to BOOKED 204; concurrency test: 2+ concurrent locks on same seat yields exactly one winner (DoD CO1); TTL test: HELD seat returns to AVAILABLE after expiry (CO1); Internal JWT required (tampered returns 401); build + format clean; at least 1 happy + 1 error each |
| source citations | VietRide_API_Contract_v1.md lines 975-998 (seam ownership), 1065-1179 (4 endpoints + request/response + error codes), 1097/1106-1109 (returnRouteId field + notes, v1.10.0-patched, cites technical_context_v7 line 1750); technical_context_v7 lines 3386-3399 (sync seat-lock saga, Redis key, TTL); BSOT line 2363 (SEAT_LOCK_TTL_MINUTES=10), lines 1336-1337/1360 (error codes); BSOT §9.9 line 2151 (Redis-namespace row ALREADY patched to `seat_lock:{tripId}:{seatNumber}` owner Trip, changelog v1.10.0 — implement this prefix, no BSOT edit); apps/booking ITripServiceClient.cs + DevTripServiceClient.cs (frozen contract); existing InternalStationsController.cs (Internal JWT auth pattern) |

## Dispatch order
1. Task 11.0 (baseline — Hangfire + Redis seat-lock seam) — blocks all below. Q1 RESOLVED (Hangfire.AspNetCore APPROVED, free MIT 1.8.x); shared CPM `<PackageVersion>` with Day-15 Task 15.5 — check Directory.Packages.props before adding to avoid duplicate-entry merge conflict.
2. Task 11.1 (Trip aggregate + migration) — depends 11.0.
3. Task 11.2 (Hangfire generation) and Task 11.4 (internal seam) both depend on 11.1.
   - Parallel-safe = yes for feature folders (Features/TripGeneration + Jobs vs
     Features/Internal/Trips), BUT both may register services in
     InfrastructureServiceCollectionExtensions.cs — run SERIAL in the current tree to avoid a
     DI-registration merge conflict; STOP and ask if a truly shared file needs both. (Q2 RESOLVED
     — 11.4 implements the `seat_lock:{tripId}:{seatNumber}` prefix; no BSOT edit needed.)
4. Task 11.3 (FE endpoints + Gateway) — depends 11.1, 11.2 (needs generated trips to search).
   Serial after 11.4 (shared DI registration file). The Gateway route addition is a SEPARATE
   nest-worker dispatch (LF, TS) — the dotnet-worker MUST NOT edit apps/gateway.

## Progress tracker
> Orchestrator bookkeeping — informational only, NOT audit evidence. /audit-day re-verifies.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 11.0 | todo | — | — | Hangfire CPM add APPROVED (Q1) — shared `<PackageVersion>` with Day-15 Task 15.5; first to land adds, other no-op (avoid duplicate-entry conflict) |
| 11.1 | todo | — | — | — |
| 11.2 | todo | — | — | — |
| 11.3 | todo | — | — | Gateway route = separate nest-worker dispatch |
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
