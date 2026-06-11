# Day 8 — Final checklist

> Produced by `/audit-day 8` AFTER all tasks are done and verification ran.
> Honest record: all blockers were fixed, rerun, and verified green.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 8 (Wed 2026-06-03) — Trip-Route-Vehicle: Route + RouteStop + AlternativeRoute (`BE_TIMELINE_VU.md:98-105`)
- **Plan**: `docs/handoff/day-8-plan.md`
- **Status**: ✅ READY

## Audit verdict

- **Truth-correct?** ✅ Yes.
  - `PATCH /v1/operator/alternative-routes/{altId}` now behaves as a partial update and preserves omitted stops; explicit `stops: null` is rejected by validation (`UpdateAlternativeRouteRequest.cs:3-85`, `UpdateAlternativeRouteValidator.cs:5-37`, `UpdateAlternativeRouteHandler.cs:35-100`).
  - `DELETE /v1/operator/alternative-routes/{altId}` now returns `{ "isActive": false }` as documented (`OperatorAlternativeRoutesController.cs:54-65`, `VietRide_API_Contract_v1.md:2554-2562`).
  - `RouteStopFareTemplate` creation now normalizes timestamps to UTC before validation/persistence, so offset inputs like `+07:00` no longer crash the API (`CreateRouteStopFareTemplateHandler.cs:37-76`).
  - `PATCH /v1/operator/routes/{id}` now supports explicit `returnRouteId: null` clearing via field-presence tracking (`UpdateRouteRequest.cs:3-28`, `UpdateRouteCommand.cs:5-14`, `UpdateRouteHandler.cs:27-69`).
- **DoD met?** ✅ Yes.
  - The Day-8 future-dated fare-template happy path now returns `201`, overlap returns `422 VALIDATION_ERROR`, and all other Day-8 functional cases pass in the real Gateway E2E.

## DoD result

- [✅] EF migration creates `routes`, `route_stops`, `route_stop_fare_templates`, `alternative_routes`, `alternative_route_stops` matching the Day-8 schema.
  - Evidence: canonical schema lists the 5 tables and constraints (`db-schema/trip-route-vehicle/schema.sql:149-258`); migration `20260610071926_AddTripRoutes.cs` creates all 5 tables plus the required indexes/checks (`:14-252`). EF apply → rollback to previous migration → re-apply passed on temp DB.
- [✅] Operator can create a Route with main intermediate stops + a future-dated fare template; flags enforced.
  - Evidence: final Newman via Gateway created Route (`201`), added RouteStop (`201`), flags invalid returned `422 ROUTE_STOP_FLAGS_INVALID`, valid fare-template create returned `201`, overlap returned `422 VALIDATION_ERROR`.
- [✅] Route create validates station existence, active OperatorStation links for both stations, origin != destination, and optional same-operator return route.
  - Evidence: implementation checks stations first (`CreateRouteHandler.cs:41-45`, `:68-75`), active OperatorStation links (`:77-105`), origin/destination difference (`:108-115`), and return-route ownership (`:118-128`). Newman confirmed same origin/destination `422`, missing station `404 STATION_NOT_FOUND`, and happy create `201`.
- [✅] RouteStop add/remove with `orderIndex`, `allowPickup`, `allowDropoff`; duplicate order returns dedicated `422`; DELETE hard-removes junction row.
  - Evidence: add handler rejects both flags false with `ROUTE_STOP_FLAGS_INVALID` (`AddRouteStopHandler.cs:40-46`) and order conflict with `ROUTE_STOP_ORDER_CONFLICT` (`:92-100`); remove handler calls repository hard delete (`RemoveRouteStopHandler.cs:42-49`); repository uses `DbSet.Remove` (`RouteStopRepository.cs:28-29`). Newman confirmed add `201`, duplicate order `422`, flags invalid `422`.
- [✅] AlternativeRoute CRUD enforces max 2 active per main route and DELETE deactivates with contract-correct response.
  - Evidence: create path enforces active max-2 (`CreateAlternativeRouteHandler.cs:75-84`) and deactivate path sets `IsActive=false` (`DeactivateAlternativeRouteHandler.cs:39-41`); Newman confirmed 1st create `201`, 2nd create `201`, 3rd active create `422`, deactivation freed a slot, and DELETE response matched `{ "isActive": false }`.
- [✅] Write endpoints are tenant-scoped to caller operator; cross-operator Route returns `404 ROUTE_NOT_FOUND`; non-approved/inactive operator write returns `403`.
  - Evidence: final Newman confirmed cross-operator route `404 ROUTE_NOT_FOUND` and non-approved operator write `403 FORBIDDEN`; route handlers use `GetOwnedByIdAsync` and `StopWriteEligibilityGuard` (`AddRouteStopHandler.cs:35-52`, `CreateRouteStopFareTemplateHandler.cs:41-52`, `CreateAlternativeRouteHandler.cs:40-46`).
- [✅] Gateway forwards `/v1/operator/routes` and `/v1/operator/alternative-routes` to `TRIP_BASE_URL` with user auth and operator-role union.
  - Evidence: source route table has both prefixes with `target: env.TRIP_BASE_URL`, `authRequired: 'user'`, roles `OPERATOR_ADMIN` + `OPERATOR_STAFF` (`apps/gateway/src/config/routes.ts:132-143`); longest-prefix match stays in place (`routes.ts:198-203`); tests cover both families (`routes.spec.ts:171-200`). Runtime bundle was verified after no-cache rebuild/recreate.
- [✅] Static deterministic checks for Trip + shared libs + Gateway/TS suite are green.
  - Evidence: Trip build/format/test pass (`106/106` unit, `16/16` integration); shared libs build/format/test pass (`66/66`); TS build/lint/test pass.
- [✅] Day-8 real running app E2E through Gateway passes liveness and functional E2E.
  - Evidence: final container/health matrix all `200/healthy`; `npm run postman:day8:local` executed `19` requests, `38` assertions, `0` failures.

## Tasks completed

- Task 8.0a — Contract + BSOT registry sync — ✅
  - Contract has Day-8 Route section (`VietRide_API_Contract_v1.md:2215-2612`); BSOT registers new codes (`BACKEND_SOURCE_OF_TRUTH.md:1380-1383`) and changelog entry (`BACKEND_SOURCE_OF_TRUTH.md:2681`).
- Task 8.G — Gateway routes — ✅
  - Source + route tests pass; runtime route bundle verified after no-cache image rebuild.
- Task 8.0 — Domain entities + EF config + migration — ✅
  - Migration/EF checks passed and model matches canonical DDL for Day-8 tables.
- Task 8.1 — Route CRUD with `returnRouteId` + tenant scope — ✅
  - Main create/list/get/update checks pass, and PATCH now supports explicit clear of `returnRouteId`.
- Task 8.2 — RouteStop add/remove — ✅
  - Dedicated error codes and hard-delete behavior implemented; E2E order-conflict Review bullet passed.
- Task 8.3 — RouteStopFareTemplate — ✅
  - Future-dated pricing, overlap rejection, and UTC normalization now pass in the real E2E.
- Task 8.4 — AlternativeRoute CRUD — ✅
  - Max-2 active behavior works and DELETE/PATCH now match the contract.
- Task 8.5 — Postman Day-8 route flow — ✅
  - Collection parses and the live Newman run is green.

## Changed files

Changed file scope identified from the Day-8 work and audit updates:

- `VietRide_API_Contract_v1.md` — Day-8 Trip Route Management endpoints, DTO/error examples, role matrix, delete semantics.
- `BACKEND_SOURCE_OF_TRUTH.md` — Day-8 error registry rows and changelog/version bump.
- `apps/gateway/src/config/routes.ts` — `/v1/operator/routes` and `/v1/operator/alternative-routes` proxy entries.
- `apps/gateway/src/config/routes.spec.ts` — Day-8 route-table tests.
- `apps/trip/src/VietRide.Trip.Domain/Entities/{Route,RouteStop,RouteStopFareTemplate,AlternativeRoute,AlternativeRouteStop}.cs` — Day-8 domain entities.
- `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/*Route*.cs` — EF mappings for Day-8 tables, indexes, checks, and query filters.
- `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/20260610071926_AddTripRoutes*.cs`, `TripDbContextModelSnapshot.cs` — second Trip migration and snapshot.
- `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs` — Day-8 DbSets.
- `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/{Route,RouteStop,RouteStopFareTemplate,AlternativeRoute}Repository.cs` — persistence operations.
- `apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` — repository DI registration.
- `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/I{Route,RouteStop,RouteStopFareTemplate,AlternativeRoute}Repository.cs` — application repository contracts.
- `apps/trip/src/VietRide.Trip.Application/Features/Routes/**` — Route CQRS use cases and DTO mapping.
- `apps/trip/src/VietRide.Trip.Application/Features/RouteStops/**` — RouteStop add/remove use cases and DTO mapping.
- `apps/trip/src/VietRide.Trip.Application/Features/RouteStopFareTemplates/**` — fare-template create/list use cases and DTO mapping.
- `apps/trip/src/VietRide.Trip.Application/Features/AlternativeRoutes/**` — AlternativeRoute CRUD use cases and DTO mapping.
- `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorRoutesController.cs` — Route/RouteStop/FareTemplate/AlternativeRoute nested endpoints.
- `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorAlternativeRoutesController.cs` — AlternativeRoute PATCH/DELETE endpoints.
- `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/*Route*.cs` — Day-8 request DTOs.
- `apps/trip/tests/VietRide.Trip.UnitTests/**` — Day-8 handler/domain unit tests.
- `apps/trip/tests/VietRide.Trip.IntegrationTests/Persistence/**` — Day-8 persistence/integration tests.
- `libs/dotnet/VietRide.Shared.Application/Exceptions/ApplicationExceptions.cs` — shared coded validation exception support.
- `libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseExceptionFilter.cs` and tests — ADR 0004 error filter support.
- `docs/api/postman/vietride.postman_collection.json` — Day-8 Newman folder.
- `docs/api/postman/README.md` — Day-8 local harness notes.
- `docs/api/postman/vietride.local.postman_environment.json` — Day-8 placeholders for manual runs.
- `scripts/run-day8-newman-local.js` — local Day-8 seed/token/Newman harness.
- `package.json` — `postman:day8:local` script.
- `docs/handoff/day-8-checklist.md` — final audit checklist.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | Build succeeded; `0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | No output; command exited successfully. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | Unit `106/106`, integration `16/16`, skipped `0`. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Build succeeded; `0 Warning(s) 0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | No output; command exited successfully. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Shared Web unit tests `66/66`, failed `0`, skipped `0`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | 10 TS projects + 1 dependent task succeeded. Tracking build emitted one existing source-map-loader warning for generated Prisma client map, but Nx target succeeded. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | 14 TS projects linted successfully (all from cache). |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | 10 TS projects + 1 dependent task succeeded; visible counts unchanged and green. |
| `node -e "JSON.parse(...postman collection...); JSON.parse(...environment...); console.log('postman json ok')"` | ✅ PASS | Printed `postman json ok`. |
| `dotnet ef database update -p apps/trip/src/VietRide.Trip.Infrastructure -s apps/trip/src/VietRide.Trip.Api` with `TRIP_DESIGN_CONNECTION=...Database=vietride_trip_day8_audit...` | ✅ PASS | Applied initial + Day-8 migration on temp DB. |
| `dotnet ef database update 20260608111138_InitialTripStationsStops -p apps/trip/src/VietRide.Trip.Infrastructure -s apps/trip/src/VietRide.Trip.Api` | ✅ PASS | Reverted Day-8 migration cleanly. |
| `dotnet ef database update -p apps/trip/src/VietRide.Trip.Infrastructure -s apps/trip/src/VietRide.Trip.Api` | ✅ PASS | Re-applied Day-8 migration successfully. Temp DB later dropped. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app build --no-cache gateway trip` | ✅ PASS | Rebuilt Day-8 app images with no cache to avoid stale runtime artifacts. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --no-deps --force-recreate gateway trip tracking rag` | ✅ PASS | Recreated app containers; runtime bundle checks showed gateway Day-8 route strings and Trip DLL Day-8 controllers present. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | Final app + infra containers all up/healthy where applicable. |
| Health matrix: `Invoke-WebRequest` for gateway 3000, identity 5001, trip 5002, booking 5003, payment 5004, parcel 5005, tracking 3001, notification 3002, rag 3003, plus gateway `/v1/*/health` routes | ✅ PASS | All checked endpoints returned HTTP `200`. |
| Review artifact validation: Postman collection/environment parse | ✅ PASS | Collection and env JSON parse; Day-8 folder and variables present. |
| Review execution against Docker/local stack: `npm run postman:day8:local` | ✅ PASS | Final run: `19` requests, `38` assertions, `0` failures. |
| Day-8 Review bullet overall | ✅ PASS | Required adversarial and happy paths both passed in the live E2E. |
| CPM invariant: grep for `<PackageReference ... Version=` in `*.csproj` | ✅ PASS | No files found; `.csproj` package refs do not carry versions. |
| Banned dependency / MediatR invariant | ✅ PASS | No banned package dependency found in manifests; MediatR remains pinned v11.1.0. |
| `git log --format=%B -10` / Co-Authored-By scan | ✅ PASS | Recent 10 commit messages printed; no `Co-Authored-By` trailer present. |
| `git ls-files --eol` for Day-8 changed files | ✅ PASS | `.cs/.csproj/.sln/.props` entries are `eol=crlf`; `.ts/.json/.md/.js` entries are `eol=lf`. |

## Contract / event / schema changes shipped

- **REST endpoints documented/implemented under Trip/Gateway:**
  - `POST /v1/operator/routes`
  - `GET /v1/operator/routes`
  - `GET /v1/operator/routes/{id}`
  - `PATCH /v1/operator/routes/{id}`
  - `POST /v1/operator/routes/{id}/stops`
  - `DELETE /v1/operator/routes/{id}/stops/{stopId}`
  - `POST /v1/operator/routes/{id}/fare-templates`
  - `GET /v1/operator/routes/{id}/fare-templates`
  - `POST /v1/operator/routes/{id}/alternative-routes`
  - `GET /v1/operator/routes/{id}/alternative-routes`
  - `PATCH /v1/operator/alternative-routes/{altId}`
  - `DELETE /v1/operator/alternative-routes/{altId}`
- **Gateway routes:** `/v1/operator/routes`, `/v1/operator/alternative-routes` → `TRIP_BASE_URL`, user auth, roles `OPERATOR_ADMIN` + `OPERATOR_STAFF`.
- **Migration:** `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/20260610071926_AddTripRoutes.cs`.
- **New tables:** `routes`, `route_stops`, `route_stop_fare_templates`, `alternative_routes`, `alternative_route_stops`.
- **New error codes:**
  - `ROUTE_STOP_ORDER_CONFLICT` — 422
  - `ROUTE_STOP_FLAGS_INVALID` — 422
  - `ALTERNATIVE_ROUTE_LIMIT_EXCEEDED` — 422
- **Events:** none shipped for Day 8; Outbox wiring/events remain Day 10 scope.
- **BSOT registry/changelog:** done — error rows at `BACKEND_SOURCE_OF_TRUTH.md:1380-1383`; changelog at `BACKEND_SOURCE_OF_TRUTH.md:2681`.

## Known gaps & carry-over for Day 9

- none

## Notes for Day 9 planning

- Day 9 can consume Route and AlternativeRoute data for Vehicle/DriverSchedule now that Day 8 is green.
- Keep Day-8 no-events decision: Outbox/integration events remain Day 10 scope.
- For local Trip integration tests, keep `VIETRIDE_TRIP_TEST_CONNECTION_STRING` aligned with the compose DB when rerunning from a dev shell.
