# Day 9 — Final checklist

> Produced by `/audit-day 9` and updated after fixing the Day-9 blockers found by the first audit.
> Honest record: the initial audit was ❌ BLOCKED; the rows below are the post-fix verification run.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 9 (Thu 2026-06-04) — Trip-Route-Vehicle: VehicleType + Vehicle + DriverSchedule skeleton
- **Plan**: docs/handoff/day-9-plan.md
- **Status**: ✅ READY

## DoD result

- ✅ **EF migration creates VehicleType/Vehicle/DriverSchedule tables.** `20260611044831_AddTripVehiclesAndDriverSchedules` creates `vehicle_types`, `vehicles`, `driver_schedules`, `vehicle_status`, indexes/checks/FKs, and a reversible `Down()`; audit migration apply/down/re-apply and fresh temp-DB apply/rollback passed.
- ✅ **3 system VehicleType rows are seeded.** Fresh temp DB verification returned `LIMOUSINE:9`, `SLEEPER_BUS:40`, `STANDARD_BUS:45`; current DB side-effect query returned `vehicle_type_seed=3`.
- ✅ **Operator can create Vehicle with a valid seat layout.** `npm run postman:day9:local` post-fix: `POST /v1/operator/vehicles` returned `201 Created`; DB side-effect query returned `vehicles=1` for `DAY9-LOCAL-01`.
- ✅ **Vehicle write validates seatLayout schema, duplicate licensePlate, unknown VehicleType, and tenant isolation.** Newman post-fix: invalid seat layout `422`, duplicate license plate `422`, unknown vehicle type `404 VEHICLE_TYPE_NOT_FOUND`, cross-operator vehicle GET `404 VEHICLE_NOT_FOUND`.
- ✅ **DriverSchedule row stored with active conflict check.** Code now accepts request `isActive`, persists it, and checks driver conflict only for active schedules. Newman post-fix: first schedule create `201 Created`, conflict case `409 TRIP_DRIVER_CONFLICT`; DB side-effect query returned `schedules=1` for the test driver/time.
- ✅ **Day-9 Review bullet executed against Docker/local stack.** `npm run postman:day9:local` executed 8 requests and 16 assertions with 0 failures, covering seatLayout JSON schema validation and same-driver/same-slot conflict.
- ✅ **Gateway forwards Day-9 endpoints at runtime.** Gateway route regression tests pass; Docker E2E via Gateway hit Trip handlers for `/v1/vehicle-types`, `/v1/operator/vehicles`, `/v1/operator/driver-schedules` instead of `ROUTE_NOT_FOUND`.
- ✅ **Full app health matrix passes.** All service `/health` endpoints returned HTTP `200`, including tracking after Docker runtime fixes.

## Tasks completed

- Task 9.0a — Contract + BSOT registry sync — ✅
- Task 9.G — Gateway routes — ✅
- Task 9.0 — Domain entities + EF config + seed + migration — ✅
- Task 9.1 — VehicleType read + Vehicle CRUD — ✅
- Task 9.2 — DriverSchedule create + conflict check — ✅
- Task 9.3 — Postman Day-9 flow — ✅

## Changed files

- `VietRide_API_Contract_v1.md` — Day-9 VehicleType, Vehicle, DriverSchedule API contract.
- `BACKEND_SOURCE_OF_TRUTH.md` — Day-9 error registry/changelog entries.
- `apps/gateway/src/config/routes.ts`, `apps/gateway/src/config/routes.spec.ts` — Day-9 Gateway route-table entries and route tests.
- `apps/gateway/src/proxy/proxy.middleware.spec.ts` — regression test that Day-9 route families proxy to Trip instead of `ROUTE_NOT_FOUND`.
- `apps/gateway/Dockerfile` — Docker build now includes root ESLint config and builds `nest-common` before Gateway so runtime shared-lib resolution is deterministic.
- `apps/tracking/Dockerfile` — Docker runtime now replaces dangling `@vietride/*` workspace symlinks with compiled shared libs and copies the generated Prisma client/native query engine to the path Prisma searches.
- `apps/trip/src/VietRide.Trip.Domain/Entities/{VehicleType,Vehicle,DriverSchedule}.cs` — Day-9 domain entities; `DriverSchedule.Create` now preserves requested `isActive`.
- `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/{VehicleType,Vehicle,DriverSchedule}Configuration.cs` — EF mappings.
- `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`, `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/20260611044831_AddTripVehiclesAndDriverSchedules*`, `TripDbContextModelSnapshot.cs` — Day-9 schema migration/snapshot.
- `apps/trip/src/VietRide.Trip.Application/Features/VehicleTypes/**`, `Features/Vehicles/**`, `Features/DriverSchedules/**` — CQRS handlers/validators/DTOs/mappers; DriverSchedule command now includes `IsActive` and inactive creation skips active-conflict checks.
- `apps/trip/src/VietRide.Trip.Api/Controllers/{VehicleTypesController,OperatorVehiclesController,OperatorDriverSchedulesController}.cs` and request DTOs — HTTP endpoints; DriverSchedule request now includes `IsActive`.
- `apps/trip/tests/VietRide.Trip.UnitTests/**`, `apps/trip/tests/VietRide.Trip.IntegrationTests/Persistence/**` — Day-9 unit/integration coverage, including active/inactive DriverSchedule regressions.
- `docs/api/postman/vietride.postman_collection.json`, `docs/api/postman/vietride.local.postman_environment.json`, `docs/api/postman/README.md`, `scripts/run-day9-newman-local.js`, `package.json` — Day-9 Newman flow/script.
- `docs/handoff/day-9-checklist.md` — final audit/closure record.

## Verification run

| Command | Result | Notes |
|---|---:|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | Build succeeded; `0 Warning(s)`, `0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | PASS | Exit 0; no formatting changes. |
| `$env:VIETRIDE_TRIP_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev'; dotnet test apps/trip/VietRide.Trip.sln -c Release` | PASS | Unit `150/150`, integration `19/19`. The env var matches the current Docker Postgres credentials. |
| `npx nx run gateway:test --skip-nx-cache` | PASS | Gateway `70/70` tests passed; includes Day-9 proxy-route regression. |
| `npx nx run tracking:build --configuration=production --skip-nx-cache` | PASS | Build passed; known Prisma source-map warning only. |
| `npx nx run-many -t build --all --exclude="VietRide.*" --skip-nx-cache` | PASS | All TS/Nest projects built successfully. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 TS projects linted successfully. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | Gateway `70/70`, contracts `27/27`, notification `7/7`, tracking `29/29`, rag `2/2`; no failures. Existing Jest worker-leak warnings only. |
| `docker build -f apps/tracking/Dockerfile --target runtime -t vietride-tracking-runtime-check .` + runtime dependency probes | PASS | Verified `/app/src/generated/tracking-prisma-client/libquery_engine-linux-musl-openssl-3.0.x.so.node` exists and `require('@vietride/nest-common')` + generated Prisma client load in the image. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | Full app stack rebuilt/started; Google OAuth env warnings only. Tracking was rebuilt again after the Prisma engine Dockerfile fix. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | PASS | All app + infra containers healthy/up, including `vietride_tracking Up ... (healthy)`. |
| Health matrix `curl.exe http://localhost:<port>/health` for gateway/identity/trip/booking/payment/parcel/tracking/notification/rag | PASS | `gateway 200`, `identity 200`, `trip 200`, `booking 200`, `payment 200`, `parcel 200`, `tracking 200`, `notification 200`, `rag 200`. |
| `npm run postman:day9:local` | PASS | Newman executed 8 requests, 8 test scripts, 16 assertions; `0` failures. Statuses observed: vehicle types `200`, vehicle create `201`, invalid layout `422`, duplicate plate `422`, unknown type `404`, cross-operator GET `404`, schedule create `201`, schedule conflict `409`. |
| DB side effects via `docker exec vietride_postgres psql -U vietride -d vietride_trip -Atc ...` | PASS | `vehicles=1`, `schedules=1`, `vehicle_type_seed=3`; confirms one successful vehicle/schedule row and no duplicate conflict schedule. |
| `git diff --check` | PASS | No whitespace errors. |
| Hard invariants: CPM/banned deps/MediatR scan | PASS | No `.csproj` `PackageReference Version=`, no banned dependency declarations, no MediatR v12+. |
| Hard invariants: `git ls-files --eol` | PASS | Expected CRLF/LF attributes held for tracked file classes. |
| Read-only review: `dotnet-reviewer` | PASS | Approved Trip `.NET` blocker fix; no findings. |
| Read-only review: `nest-reviewer` | PASS | Approved Gateway/Tracking Docker/runtime fixes; no findings. |

## Contract / event / schema changes shipped

- **Endpoints documented/implemented and verified through Gateway**:
  - `GET /v1/vehicle-types`
  - `POST /v1/operator/vehicles`
  - `GET /v1/operator/vehicles`
  - `GET /v1/operator/vehicles/{id}`
  - `PATCH /v1/operator/vehicles/{id}`
  - `POST /v1/operator/driver-schedules`
- **Gateway routes verified**: `/v1/operator/vehicles`, `/v1/operator/driver-schedules`, `/v1/vehicle-types` → `TRIP_BASE_URL` with user auth + `[OPERATOR_ADMIN, OPERATOR_STAFF]` role union.
- **Migration shipped**: `20260611044831_AddTripVehiclesAndDriverSchedules` creates `vehicle_types`, `vehicles`, `driver_schedules`, `vehicle_status`, indexes/checks/FKs, and seeds 3 system VehicleTypes.
- **Error codes shipped in BSOT**: `VEHICLE_NOT_FOUND` and `VEHICLE_TYPE_NOT_FOUND` appended to §5.9 with §13 changelog row `1.9.0`.
- **Events**: none. Day 9 correctly does not add Outbox/integration events.
- **Cross-check**: new error registry/changelog update was done; no new event registry entry needed.

## Known gaps & carry-over for Day 10

- No Day-9 blocker remains after the post-fix verification run.
- Driver/assistant user existence, role, and operator validation remains intentionally deferred to Day 11 as documented in `docs/handoff/day-9-plan.md`.
- Local Trip integration tests need `VIETRIDE_TRIP_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev'` when using the current Docker Postgres credentials.

## Notes for Day 10 planning

- Day 10 Sprint 2 demo can rely on the operator vehicle leg: Gateway E2E now proves vehicle create and driver-schedule conflict behavior.
- Preserve the Day-9 decision of **no events** for Vehicle/DriverSchedule; Outbox work begins Day 10, but Day-9 handlers should not emit events retroactively unless the SOT changes.
