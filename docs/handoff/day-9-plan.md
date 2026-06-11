# Day 9 - Plan

> Produced by manager. Gated by reviewer (PLAN-REVIEW) before any worker runs.

- Timeline ref: BE_TIMELINE_VU.md -> Day 9 (Thu 2026-06-04) - Trip-Route-Vehicle: VehicleType + Vehicle + DriverSchedule skeleton (BE_TIMELINE_VU.md:107-113). No Jira key listed.
- Prior checklist: docs/handoff/day-8-checklist.md (found - READY; no blocker/carry-over. Day-8 notes confirm Day-9 may consume Route data; keep no-events decision - Outbox/integration events remain Day-10 scope; db-schema/trip-route-vehicle/schema.sql is canonical for the 3 new tables).
- Plan status: DRAFT -> (reviewer) APPROVED / REVISION-REQUIRED
- Open questions: ALL RESOLVED by human (OQ-1..OQ-5). Decisions baked into the tasks below; see "Resolved decisions" and "Carryover" sections. No outstanding blockers.

## Objective
Deliver the Trip Vehicle layer on top of Day-8 Route: a system-seeded VehicleType catalog (3 platform types), operator-scoped Vehicle CRUD with seat-layout validation (totalSeats == seats.length, unique seatNumber), and a DriverSchedule skeleton create endpoint (persist row + app-layer driver/time conflict check, NO Hangfire trip generation). Third Trip migration (3 new tables); assignment backbone Day-11 Trip search/auto-generation consumes. First JSONB-validated write in Trip; first DriverSchedule conflict rule.

## Success criteria (DoD - binary, verifiable)
- [ ] EF migration creates vehicle_types, vehicles, driver_schedules matching schema.sql:263-345; applies on empty DB, reverts to the Day-8 migration, re-applies (BE_TIMELINE_VU.md:108).
- [ ] The 3 system VehicleType rows (STANDARD_BUS, LIMOUSINE, SLEEPER_BUS, is_system_defined=true) seeded with fixed UUIDs ...0101/...0102/...0103 and seat counts 45/9/40 (seed.sql:9-35; RESOLVED OQ-5: timeline 16-seat prose superseded); idempotent (BE_TIMELINE_VU.md:108-109; README:100).
- [ ] Operator creates a Vehicle with valid seatLayoutJson; backend validates totalSeats == seats.length and no duplicate seatNumber -> invalid layout 422 with error.fields (BE_TIMELINE_VU.md:110,113; technical_context_v7:1976-1989). RESOLVED OQ-3: these two rules (plus totalSeats == request.totalSeats) are the FULL v1 requirement; deeper geometry is out-of-scope v2 backlog, NO follow-up day.
- [ ] Vehicle write validates vehicleTypeId exists+active (unknown -> 404 VEHICLE_TYPE_NOT_FOUND, RESOLVED OQ-1) and licensePlate uniqueness among non-soft-deleted rows (uq_vehicles_license_plate WHERE deleted_at IS NULL, schema:303-304).
- [ ] DriverSchedule create persists a row (route + driver + optional assistant/vehicle + dayOfWeek JSON + departureTime + valid window); conflict (same driver, overlapping dayOfWeek + same departureTime + overlapping valid window) -> 409 TRIP_DRIVER_CONFLICT; NO trip generation (BE_TIMELINE_VU.md:111,113; technical_context_v7:3557-3580).
- [ ] All write endpoints tenant-scoped; cross-operator Vehicle read/write -> 404 VEHICLE_NOT_FOUND (RESOLVED OQ-1); non-APPROVED/inactive operator write -> 403 (mirrors Day-8 StopWriteEligibilityGuard).
- [ ] Gateway forwards Day-9 operator endpoints to TRIP_BASE_URL with user auth + operator-role union; Gateway TS build/lint/test green.
- [ ] dotnet build / dotnet format --verify-no-changes / dotnet test (incl. NetArchTest layering) green for Trip + shared libs; Gateway TS build/lint/test green.

## Resolved decisions (human-confirmed; baked into tasks)
- RESOLVED OQ-1 (option a): Task 9.0a adds TWO new BSOT 5.9 rows in the Trip group near the existing TRIP_* codes (BACKEND_SOURCE_OF_TRUTH.md:1359-1364): VEHICLE_NOT_FOUND (404) + VEHICLE_TYPE_NOT_FOUND (404), mirroring Day-8 ROUTE_NOT_FOUND. Tenant-isolation 404 for a vehicle uses VEHICLE_NOT_FOUND; unknown vehicleTypeId uses VEHICLE_TYPE_NOT_FOUND. Verified: those two codes are absent at BSOT:1359-1364.
- RESOLVED OQ-2: endpoint paths are /v1/operator/vehicles (Vehicle CRUD), /v1/operator/driver-schedules (DriverSchedule create), /v1/vehicle-types (VehicleType read, operator read-union). Verified: the existing generic /v1/vehicles entry (routes.ts:145, authRequired user, NO roles) is a different prefix and is NOT touched; /v1/operator/vehicles resolves to the new entry by longest-prefix-wins.
- RESOLVED OQ-3 (minimal == spec-complete): v1 seat-layout validation = exactly totalSeats == seats.length + no duplicate seatNumber (technical_context_v7:1988), plus totalSeats == request.totalSeats; seatLayoutJson stored opaque JSONB. This is the FULL v1 requirement - NO follow-up day. Deeper geometric validation (row/col/deck/aisle coherence, vehicleTypeCode match) is out-of-scope v2 backlog, intentionally not on the timeline.
- RESOLVED OQ-4 (SKIP at Day 9, CARRYOVER to Day 11): Day-9 DriverSchedule persists driver_user_id / assistant_user_id as logical-FK UUIDs WITHOUT validating existence/role/operator. Verified: Identity has NO GET /internal/v1/users/{userId} implemented (registered in BSOT 7.2:1666 but unbuilt - InternalUsersController.cs only exposes GET /internal/v1/users/{userId}/device-tokens). v1 risk is low (operator picks from own tenant staff, operator_id-scoped). Role-validation of driver/assistant is explicitly DEFERRED to Day 11, not silently dropped. See Carryover.
- RESOLVED OQ-5: VehicleType seed seat counts follow seed.sql (STANDARD_BUS=45, LIMOUSINE=9, SLEEPER_BUS=40); timeline "16-seat" prose superseded (db-schema #6 > timeline #5). Verified seed.sql:9-35.

## Contract changes
Day-9 endpoints are NOT yet in VietRide_API_Contract_v1.md (verified - no Vehicle/VehicleType/DriverSchedule section; only one passing mention at :978). Contract authored in Task 9.0a.

- VehicleType: GET /v1/vehicle-types (read catalog - 3 system types + operator-custom; operator read-union). [RESOLVED OQ-2.]
- Vehicle: POST /v1/operator/vehicles, GET /v1/operator/vehicles, GET /v1/operator/vehicles/{id}, PATCH /v1/operator/vehicles/{id}. [RESOLVED OQ-2.]
- DriverSchedule: POST /v1/operator/driver-schedules (skeleton create only - just persist row, BE_TIMELINE_VU.md:111). [RESOLVED OQ-2.] Day-9 does NOT include DriverSchedule list/get/edit (edit cascade is Day-22, BE_TIMELINE_VU.md:240).
- Per-method roles (DECISION - mirror Day-8 OperatorRoutesController): Gateway entry carries union [OPERATOR_ADMIN, OPERATOR_STAFF] (proxy only); controllers enforce via [Authorize(Roles=...)] like Day-8 (OperatorRoutesController.cs:18-19): WRITE (POST/PATCH) = OPERATOR_ADMIN only; READ (GET) = OPERATOR_ADMIN + OPERATOR_STAFF. VehicleType read is operator read-union. Override only if human wants STAFF write (re-opens 9.0a + controllers).
- Error codes - reuse existing rows + add two new. TRIP_DRIVER_CONFLICT (409, BSOT:1362) for DriverSchedule conflict; VALIDATION_ERROR (422) + error.fields for seat-layout/licensePlate; FORBIDDEN (403). NEW (RESOLVED OQ-1, added in 9.0a): VEHICLE_NOT_FOUND (404) + VEHICLE_TYPE_NOT_FOUND (404). No seat-layout-specific code (RESOLVED OQ-3 - seat-layout failures stay VALIDATION_ERROR + error.fields).
- Events: NONE from Day-9 handlers (Outbox wiring is Day-10, Day-8 checklist:144,154). Do NOT add an integration event.
- Gateway: NEW operator vehicle + driver-schedule prefixes + VehicleType read prefix. The existing generic /v1/vehicles entry (routes.ts:145, authRequired user, NO roles) is unrelated; Day-9 operator endpoints under /v1/operator/... need role-scoped entries (Task 9.G). Do NOT modify the generic /v1/vehicles entry.
- Migration: third Trip migration (after 20260610071926_AddTripRoutes); keep outbox table outbox_events; do NOT touch Day-7/Day-8 migrations.

## Tasks

### Task 9.0a - Contract + BSOT registry sync for Day-9 (DO FIRST)
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | VietRide_API_Contract_v1.md (append Day-9 VehicleType/Vehicle/DriverSchedule section); BACKEND_SOURCE_OF_TRUTH.md (RESOLVED OQ-1: add 5.9 rows VEHICLE_NOT_FOUND 404 + VEHICLE_TYPE_NOT_FOUND 404 in the Trip group near :1359-1364 + 13 changelog row + version bump per 13 rules) |
| forbidden scope | any code (apps/**, libs/**), db-schema/** (canonical - do not edit), .env/secrets, git ops. Do NOT invent columns/enums - derive field names from schema.sql:263-345 camelCase + seatLayoutJson contract technical_context_v7:1976-1989, mirroring Day-8 contract style. Add EXACTLY the two new error codes named above (VEHICLE_NOT_FOUND, VEHICLE_TYPE_NOT_FOUND) - no more. |
| depends on | none (all OQ resolved) |
| invariant flags | LF/.md . ApiResponse envelope (ADR 0004) . BSOT 5.6 (Idempotency-Key) / 5.7 (pagination) / 5.8 conventions . reuse TRIP_DRIVER_CONFLICT (409); seat-layout/licensePlate failures stay VALIDATION_ERROR + error.fields |
| acceptance | New section documents all Day-9 endpoints (paths /v1/vehicle-types, /v1/operator/vehicles[/{id}], /v1/operator/driver-schedules) with auth roles + request/response in ADR-0004 envelope; seatLayoutJson documented EXACTLY per technical_context_v7:1976-1989 (version, vehicleTypeCode, totalSeats, rows, cols, decks, aisles[].afterCol, seats[] with seatNumber/row/col/deck/type enum STANDARD|SLEEPER_LOWER|SLEEPER_UPPER|VIP|DRIVER_AREA/isWindow/isAisle/disabled); seat-layout rules == totalSeats==seats.length + unique seatNumber ONLY (RESOLVED OQ-3, full v1 scope) as 422 VALIDATION_ERROR + error.fields; licensePlate uniqueness (soft-delete-aware); VehicleType catalog read (3 system types, is_system_defined blocks delete app-layer); DriverSchedule create documents dayOfWeek JSON 1=Mon..7=Sun, departureTime local-ICT TIME, valid window, conflict -> 409 TRIP_DRIVER_CONFLICT, NO trip generation, driver/assistant role validation DEFERRED to Day 11 (RESOLVED OQ-4); tenant 404 VEHICLE_NOT_FOUND, unknown vehicleTypeId 404 VEHICLE_TYPE_NOT_FOUND; per-method role matrix; new 5.9 codes VEHICLE_NOT_FOUND + VEHICLE_TYPE_NOT_FOUND + 13 changelog + version bump; markdown renders; no invented column/enum |
| source citations | schema.sql:263-345; seed.sql:9-35; README:60-101; BSOT:1359-1364; technical_context_v7:530,545,1976-1989,3557-3580; BE_TIMELINE_VU.md:107-113; docs/handoff/day-8-plan.md (Task 8.0a pattern) |

### Task 9.G - Gateway routes for Day-9 operator vehicle + driver-schedule
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | nest-worker |
| review agent | nest-reviewer (or /code-review) |
| skill | (none) |
| owned files (write set) | apps/gateway/src/config/routes.ts (append ProxyRoute entries in Trip/Vehicle block); apps/gateway/src/config/routes.spec.ts (Day-9 route-table tests mirroring Day-8) |
| forbidden scope | any other route entry (do NOT modify generic /v1/vehicles, /v1/driver, /v1/assistant, /v1/operator/routes, or any other prefix), apps/trip/** / libs/**, .env/secrets, db-schema/**, contract/BSOT docs, git ops |
| depends on | 9.0a (paths confirmed by RESOLVED OQ-2) |
| invariant flags | LF/.ts . no new dependency . mirror Day-8 /v1/operator/routes entry EXACTLY (routes.ts:133-138): target=env.TRIP_BASE_URL, authRequired=user, requiredRoles=[OPERATOR_ADMIN, OPERATOR_STAFF], no rewriteTo . finer per-method roles enforced by Trip controllers . VehicleType read /v1/vehicle-types is operator read-union (requiredRoles [OPERATOR_ADMIN, OPERATOR_STAFF]) |
| acceptance | routes.ts has Day-9 operator entries (/v1/operator/vehicles, /v1/operator/driver-schedules, /v1/vehicle-types per RESOLVED OQ-2), each target env.TRIP_BASE_URL, authRequired user, requiredRoles [OPERATOR_ADMIN, OPERATOR_STAFF], in the Trip/Vehicle block; matchRoute on /v1/operator/vehicles/... resolves to new entry (longest-prefix-wins, routes.ts:199-205); generic /v1/vehicles unchanged; Gateway nx build/lint/test green; no other prefix changed |
| source citations | apps/gateway/src/config/routes.ts:133-147 (Trip/Vehicle block), :133-138 (/v1/operator/routes template), :145 (generic /v1/vehicles - do NOT touch), :199-205 (matchRoute); docs/handoff/day-8-plan.md Task 8.G |

### Task 9.0 - Domain entities + EF config + seed + third Trip migration (VehicleType, Vehicle, DriverSchedule)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | scaffold-aggregate (Vehicle aggregate root; VehicleType + DriverSchedule entities) + ef-migration |
| owned files (write set) | apps/trip/src/VietRide.Trip.Domain/Entities/VehicleType.cs, .../Vehicle.cs, .../DriverSchedule.cs; apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/VehicleTypeConfiguration.cs, .../VehicleConfiguration.cs, .../DriverScheduleConfiguration.cs; apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs (add DbSets only); apps/trip/src/VietRide.Trip.Infrastructure/Migrations/* (new migration + seed HasData/SQL for 3 VehicleType rows + regenerated snapshot); apps/trip/tests/VietRide.Trip.UnitTests/** (entity/config/model tests) |
| forbidden scope | any other service, .env/secrets, existing Day-7/Day-8 entities/configs (Station/Stop/Route/RouteStop/AlternativeRoute), shared libs, db-schema/**, contract/BSOT docs, apps/gateway/**, git ops. Do NOT add Application/Api code. Do NOT reintroduce outbox_messages. Do NOT edit Day-7/Day-8 migrations. |
| depends on | 9.0a |
| invariant flags | CRLF/.cs . CPM no Version= . no cross-DB FK (operator_id, driver_user_id, assistant_user_id plain UUID, no REFERENCES; vehicle_type_id FK same-DB OK; route_id/vehicle_id in driver_schedules same-DB OK) . soft-delete via deleted_at only - present on Vehicle ONLY (schema:295); VehicleType + DriverSchedule have NO deleted_at . IActivatable where is_active exists (all three) . Vehicle has NO Money column (cargo weights DECIMAL kg) . keep outbox table outbox_events |
| acceptance | 3 entities match canonical columns/types EXACTLY: vehicle_types (code, display_name, estimated_passenger_luggage_kg_per_seat INT?, default_seat_count INT?, is_system_defined, is_active; uq_vehicle_types_code), vehicles (operator_id, vehicle_type_id FK ON DELETE RESTRICT, license_plate, seat_layout_json JSONB, total_seats, max_cargo_weight_kg DECIMAL(8,2)?, max_cargo_volume_m3 DECIMAL(8,2)?, status vehicle_status enum ACTIVE/MAINTENANCE/OFF_DUTY/RETIRED, is_active, deleted_at; uq_vehicles_license_plate WHERE deleted_at IS NULL, idx_vehicles_operator_status WHERE is_active, idx_vehicles_vehicle_type_id; CHECK total_seats>0, cargo_weight non-negative), driver_schedules (operator_id, route_id FK ON DELETE RESTRICT, vehicle_id FK ON DELETE SET NULL nullable, driver_user_id, assistant_user_id?, day_of_week JSONB, departure_time TIME, valid_from DATE, valid_until DATE?, is_active; 4 indexes; CHECK valid_until>=valid_from); seed inserts 3 system VehicleType rows with fixed UUIDs from seed.sql + seat counts 45/9/40 (RESOLVED OQ-5) + idempotent; migration applies on empty DB after Day-8, reverts to 20260610071926_AddTripRoutes, re-applies; dotnet build/format/test + NetArchTest green |
| source citations | schema.sql:33 (vehicle_status enum), :263-345 (3 tables + indexes + checks + comments); seed.sql:9-35; README:73-84,93-94,100; technical_context_v7:1976-1989 (seatLayoutJson - stored as opaque JSONB at DB layer); ADR 0003; apps/trip/.../Migrations/20260610071926_AddTripRoutes.cs; apps/trip/.../Entities/Stop.cs (ISoftDeletable+IActivatable) |

### Task 9.1 - VehicleType catalog read + Vehicle CRUD with seat-layout validation + tenant scope
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IVehicleTypeRepository.cs, .../IVehicleRepository.cs; apps/trip/src/VietRide.Trip.Application/Features/VehicleTypes/** (ListVehicleTypes query/handler/DTO/mapper); apps/trip/src/VietRide.Trip.Application/Features/Vehicles/** (Create/Update commands+handlers+validators, List/Get queries+handlers, VehicleDto, VehicleMapper, seat-layout validation helper + seatLayout DTO model); apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/VehicleTypeRepository.cs, .../VehicleRepository.cs; apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (add repo DI only); apps/trip/src/VietRide.Trip.Api/Controllers/OperatorVehiclesController.cs, .../VehicleTypesController.cs; apps/trip/src/VietRide.Trip.Api/Controllers/Requests/CreateVehicleRequest.cs, .../UpdateVehicleRequest.cs; apps/trip/tests/VietRide.Trip.UnitTests/Features/Vehicles/**, .../VehicleTypes/**; apps/trip/tests/VietRide.Trip.IntegrationTests/Persistence/** (Vehicle/VehicleType) |
| forbidden scope | DriverSchedule code (Task 9.2), domain entities/EF configs/migration (Task 9.0 - consume read-only), other services, shared libs, db-schema/**, contract/BSOT docs, apps/gateway/**, .env/secrets, git ops. Do NOT change IIdentityInternalClient signature; reuse StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync. Do NOT touch Route/Stop features. |
| depends on | 9.0, 9.0a |
| invariant flags | CRLF/.cs . controller calls MediatR.Send only . [Authorize(Roles=...)] per-method (WRITE=OPERATOR_ADMIN, READ=ADMIN+STAFF) . tenant scope: Vehicle queries filtered by caller operatorId; cross-operator GET/PATCH -> 404 VEHICLE_NOT_FOUND (RESOLVED OQ-1; mirror routeRepository.GetOwnedByIdAsync) . operator write-eligibility via StopWriteEligibilityGuard (403/422) . ApiResponse envelope (ADR 0004) . seat-layout validation = 422 VALIDATION_ERROR + error.fields, EXACTLY totalSeats==seats.length + unique seatNumber + totalSeats==request.totalSeats (RESOLVED OQ-3, no deeper geometry) . licensePlate uniqueness soft-delete-aware . no Money column on Vehicle |
| acceptance | GET /v1/vehicle-types returns catalog (3 system + operator-active custom) in list/paged ADR-0004 envelope; POST /v1/operator/vehicles validates operator write-eligibility (403), vehicleTypeId exists+active (unknown -> 404 VEHICLE_TYPE_NOT_FOUND, RESOLVED OQ-1), seatLayout rules (the two mandated + totalSeats==request.totalSeats) -> invalid 422 + error.fields, licensePlate unique among non-deleted -> 201 VehicleDto; GET /v1/operator/vehicles lists own-operator (paged BSOT 5.7); GET /v1/operator/vehicles/{id} own-operator only (cross-op 404 VEHICLE_NOT_FOUND); PATCH /v1/operator/vehicles/{id} partial (own-operator, OPERATOR_ADMIN) re-validates seat layout if provided; >=1 happy + >=1 error unit test per handler; build/format/test green |
| source citations | technical_context_v7:1976-1989 (seatLayout contract + validation), :530,545 (Vehicle fields); schema.sql:263-309; README:73-74; apps/trip/src/VietRide.Trip.Application/Features/Routes/CreateRouteHandler.cs:34-66, .../Stops/StopWriteEligibilityGuard.cs; apps/trip/src/VietRide.Trip.Api/Controllers/OperatorRoutesController.cs:14-53; BE_TIMELINE_VU.md:110,113 |

### Task 9.2 - DriverSchedule skeleton create + driver/time conflict check (no trip generation)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IDriverScheduleRepository.cs; apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/** (CreateDriverScheduleCommand/Handler/Validator, DriverScheduleDto, DriverScheduleMapper); apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/DriverScheduleRepository.cs; apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (add DriverSchedule repo DI only - shared with 9.1; see Dispatch order); apps/trip/src/VietRide.Trip.Api/Controllers/OperatorDriverSchedulesController.cs; apps/trip/src/VietRide.Trip.Api/Controllers/Requests/CreateDriverScheduleRequest.cs; apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/** |
| forbidden scope | Vehicle/VehicleType code (Task 9.1 - may reference IVehicleRepository/IRouteRepository read-only for FK existence), domain entities/EF configs/migration (Task 9.0), Hangfire/trip-generation (out of Day-9 scope - BE_TIMELINE_VU.md:111), other services, shared libs, db-schema/**, contract/BSOT docs, apps/gateway/**, .env/secrets, git ops. Do NOT add DriverSchedule list/get/edit endpoints. Do NOT change IIdentityInternalClient (driver/assistant role-validation is DEFERRED to Day 11 per RESOLVED OQ-4 - do NOT invent an Identity call, do NOT silently drop the requirement; persist the UUIDs as logical FK). |
| depends on | 9.0, 9.0a (shares InfrastructureServiceCollectionExtensions.cs with 9.1 - run serial after 9.1) |
| invariant flags | CRLF/.cs . controller calls MediatR.Send only . [Authorize(Roles=OPERATOR_ADMIN)] for create . tenant scope: schedule pinned to caller operatorId; routeId own-operator active (routeRepository.ExistsActiveOwnedByOperatorAsync) else 404 ROUTE_NOT_FOUND; vehicleId (nullable) own-operator else 404 VEHICLE_NOT_FOUND . operator write-eligibility via StopWriteEligibilityGuard . conflict (same driver_user_id, overlapping dayOfWeek set, equal departureTime, overlapping [valid_from, valid_until], is_active=true) -> 409 TRIP_DRIVER_CONFLICT . departureTime stored as local-ICT TimeOnly/TIME, dayOfWeek JSONB int array 1-7 . NO Outbox event . NO Hangfire enqueue . driver/assistant existence+role+operator validation DEFERRED to Day 11 (RESOLVED OQ-4 - persist driver_user_id/assistant_user_id as opaque UUIDs, NO Identity lookup) |
| acceptance | POST /v1/operator/driver-schedules (OPERATOR_ADMIN) validates operator write-eligibility (403), routeId own-operator active (404 ROUTE_NOT_FOUND), optional vehicleId own-operator (404 VEHICLE_NOT_FOUND), dayOfWeek non-empty ints 1-7, valid_until>=valid_from -> persists driver_schedules row + 201 DriverScheduleDto; driver_user_id/assistant_user_id stored WITHOUT existence/role check (RESOLVED OQ-4, deferred Day 11); second create for SAME driver with overlapping dayOfWeek + same departureTime + overlapping valid window -> 409 TRIP_DRIVER_CONFLICT; NO Trip rows generated, NO Hangfire job, NO Outbox event (assert in handler/tests); >=1 happy + >=1 conflict + >=1 validation unit test; build/format/test green |
| source citations | technical_context_v7:3557-3580 (DriverSchedule scope, recurring pattern; round-trip pairing NOT Day-9 auto-created), :3666-3690 (auto-generate NOT Day-9); schema.sql:314-345; README:75-76,83-84,93-94 (conflict at app/Trip layer - NO unique index on driver_schedules); BSOT:1362 (TRIP_DRIVER_CONFLICT 409), :1666 (GET /internal/v1/users/{userId} registered but unbuilt - RESOLVED OQ-4); BE_TIMELINE_VU.md:111,113; apps/trip/src/VietRide.Trip.Application/Features/Routes/CreateRouteHandler.cs |

### Task 9.3 - Postman Day-9 vehicle + driver-schedule flow
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer (or /code-review) |
| skill | smoke-test |
| owned files (write set) | docs/api/postman/vietride.postman_collection.json (add Day-9 folder); docs/api/postman/README.md (Day-9 notes); docs/api/postman/vietride.local.postman_environment.json (Day-9 placeholders); scripts/run-day9-newman-local.js (mirror scripts/run-day8-newman-local.js); package.json (add postman:day9:local script only) |
| forbidden scope | any apps/**/libs/** source, db-schema/**, contract/BSOT docs, .env/secrets, other postman day folders, git ops. Do NOT alter Day-7/Day-8 postman folders or scripts. |
| depends on | 9.0, 9.0a, 9.1, 9.2, 9.G |
| invariant flags | LF/.json/.js/.md . no new dependency (reuse newman from Day-8) . all requests through Gateway (/v1/...), never direct to Trip . mirror scripts/run-day8-newman-local.js seed/token approach |
| acceptance | Collection + environment JSON parse; Day-9 folder exercises: list vehicle-types (3 system types present, counts 45/9/40), create vehicle happy (201), invalid seat layout (422), duplicate licensePlate (422), unknown vehicleTypeId (404 VEHICLE_TYPE_NOT_FOUND), cross-operator vehicle GET (404 VEHICLE_NOT_FOUND), create driver-schedule happy (201), driver conflict (409 TRIP_DRIVER_CONFLICT); npm run postman:day9:local green (0 failures) against local Docker stack after Trip+Gateway rebuilt; README documents the run |
| source citations | scripts/run-day8-newman-local.js; docs/api/postman/README.md; docs/handoff/day-8-plan.md Task 8.5; BE_TIMELINE_VU.md:113 (Review: seatLayout JSON schema validation; conflict test -> 409) |

## Dispatch order
1. 9.0a (contract+BSOT sync) - parallel-safe no (docs; gates everything). All OQ resolved - dispatch directly.
2. 9.G (gateway) - parallel-safe yes (disjoint write set: gateway TS only). May run alongside 9.0/9.1/9.2.
3. 9.0 (entities+EF+seed+migration) - parallel-safe no (foundation; touches DbContext+Migrations).
4. 9.1 (VehicleType read + Vehicle CRUD) - parallel-safe no re 9.2 (both edit InfrastructureServiceCollectionExtensions.cs). Run after 9.0.
5. 9.2 (DriverSchedule create) - parallel-safe no re 9.1 (shared DI file). Run after 9.1 to avoid clobbering the DI block; if human wants parallel, split the DI edit into a tiny cross-cutting follow-up.
6. 9.3 (Postman E2E) - parallel-safe no (depends on all above + running stack). Run last.

Recommended serial order: 9.0a -> 9.G -> 9.0 -> 9.1 -> 9.2 -> 9.3.

## Progress tracker
> Orchestrator bookkeeping - updated after each /implement-task (Step 3). Informational only - NOT audit evidence. /audit-day re-verifies independently.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 9.0a | done | APPROVE | 2026-06-11 | Approved after 2 patch rounds; human /verify pending. |
| 9.G | done | APPROVE | 2026-06-11 | Strong-Codex diff review approved; gateway test/lint/build green; human /verify pending. |
| 9.0 | todo | - | - | - |
| 9.1 | todo | - | - | - |
| 9.2 | todo | - | - | - |
| 9.3 | todo | - | - | - |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Carryover (forward to later days)
- Day 11 - OQ-4 follow-up (driver/assistant validation, DEFERRED from Day 9):
  1. Identity implements GET /internal/v1/users/{userId} returning {id, role, operatorId, status} (registered in BSOT 7.2:1666 but currently unbuilt - InternalUsersController.cs only exposes GET /internal/v1/users/{userId}/device-tokens).
  2. Trip DriverSchedule create (and/or trip-generation) validates driver_user_id is DRIVER-role + assistant_user_id is ASSISTANT-role + both belong to the schedule's operator, via that endpoint (extend IIdentityInternalClient at that time).
  - v1 risk is LOW: operator picks from its own tenant staff and the schedule is operator_id-scoped, so an out-of-tenant or wrong-role UUID is unlikely in practice. Day-9 persists driver_user_id/assistant_user_id as opaque logical-FK UUIDs without validation; this is an explicit deferral, not a silent drop.

## Open questions
(none - OQ-1..OQ-5 all resolved by human; decisions baked into the tasks above and recorded under "Resolved decisions". OQ-4's deferred work is tracked under "Carryover" -> Day 11.)
