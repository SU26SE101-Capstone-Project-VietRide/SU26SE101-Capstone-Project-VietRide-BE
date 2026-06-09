# Day 7 — Final checklist

> Produced by `/audit-day 7` after re-reading the source-of-truth and re-running the verification matrix on 2026-06-09.
> Audit is read-only except this checklist. No code fixes or commits were made by the audit step.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 7 (Tue 2026-06-02) — Trip-Route-Vehicle: Station + OperatorStation + Stop (Jira: SCV-74)
- **Plan**: `docs/handoff/day-7-plan.md`
- **Status**: ✅ READY

## DoD result

Timeline Day 7 states:
- EF migration: Station, OperatorStation, Stop tables (`BE_TIMELINE_VU.md:89-94`).
- `GET /stations/search?q=` autocomplete, station create/link, Stop CRUD stub (`BE_TIMELINE_VU.md:91-94`).
- **DoD**: operator can search → link existing OR create new Station; Stop created with lat/lng/name (`BE_TIMELINE_VU.md:95`).
- **Review**: autocomplete dedupe test (`q=Mien Tay` returns existing canonical Station); Stop coords validated lat ∈ [-90,90] (`BE_TIMELINE_VU.md:96`).

Binary result from the approved Day-7 plan:

- [x] Canonical Day-7 search extension docs synced — ✅ `db-schema/trip-route-vehicle/schema.sql:7-9` creates `pgcrypto`, `unaccent`, `pg_trgm`; README documents `unaccent(name) ILIKE unaccent('%' || q || '%')` and `pg_trgm` placeholder-only rationale at `db-schema/trip-route-vehicle/README.md:9-11`.
- [x] EF migration creates Station/OperatorStation/Stop and canonical inherited outbox schema, and applies/rolls back/re-applies — ✅ Trip initial migration creates `stations`, `operator_stations`, `stops`, `outbox_events`, `outbox_event_status`, and the placeholder `idx_stations_name_trgm ... WHERE FALSE`; temp DB apply → `update 0` → re-apply passed.
- [x] Operator can search then link existing OR create new Station — ✅ SQL search uses `unaccent(name) ILIKE unaccent('%' || q || '%')`; Day-7 Newman local harness through Gateway passed search/link/create/duplicate-nearby flow.
- [x] Duplicate-nearby Station create returns 200 warning without creating a Station — ✅ Newman passed `200` with `data.warning.code = STATION_DUPLICATE_NEARBY`; DB side-effect check after the run shows only one `Day 7 Local%` created station row and no outbox events.
- [x] OperatorStation create and Stop create/update validate caller operator logical FK/status via Identity internal HTTP and no cross-DB FK — ✅ handlers call Identity internal validation; Docker Trip has `IDENTITY_SERVICE_BASE_URL=http://identity:5001`; Newman verified approved writes and non-approved `403 FORBIDDEN`; EF model tests verify no `operator_id` cross-DB FK.
- [x] Stop CREATE/READ/UPDATE under `/v1/operator/stops` — ✅ Newman passed create/get/list/update and OPERATOR_STAFF PATCH `403`; no `HttpDelete` endpoint exists.
- [x] Stop coordinates validated lat ∈ [-90,90], lng ∈ [-180,180] — ✅ Newman request `5a. Invalid stop coordinates return VALIDATION_ERROR` returned `422 VALIDATION_ERROR`.
- [x] Internal lookups `GET /internal/v1/stations/{id}` and `GET /internal/v1/stops/{id}` return raw DTOs with coded 404s — ✅ direct Trip probes returned `200` raw JSON without `success` envelope for existing Station/Stop; missing Station/Stop returned `404 STATION_NOT_FOUND` / `404 STOP_NOT_FOUND` in ADR 0004 error envelope.
- [x] Gateway routes for Station/Stop endpoint families exist and forward user context — ✅ `/v1/stations`, `/v1/operator/stations`, `/v1/operator/stops` route to Trip with operator role union (`apps/gateway/src/config/routes.ts:113-131`); Gateway signs downstream Internal JWT with `sub`, `role`, `operatorId` (`proxy.middleware.ts:253-261`).
- [x] `STATION_NOT_FOUND` / `STOP_NOT_FOUND` coded 404 path shipped — ✅ `CodedNotFoundException` exists and maps to 404 with caller-supplied code; generic `NotFoundException` still maps to `RESOURCE_NOT_FOUND`.
- [x] Required endpoint/error matrix covered — ✅ Trip/Identity/shared unit+integration suites passed; Newman covered timeline Review + main Gateway flow; extra Gateway probes covered duplicate-link idempotency (`200`, mapping count `1→1`) and station create missing coords (`422 VALIDATION_ERROR`).
- [x] dotnet build/format/test clean for Trip, Identity, shared libs — ✅ verification table below.
- [x] API contract + BSOT + Postman updated — ✅ API contract contains Day-7 endpoints (`VietRide_API_Contract_v1.md:2010-2213`); BSOT registries/changelog contain error/internal endpoint sync (`BACKEND_SOURCE_OF_TRUTH.md:1373-1382`, `1680`, `2678`); Postman Day-7 folder executed through Gateway.

## Tasks completed

- Task 7.0 — Trip service architecture baseline (MediatR + Infrastructure DI + NetArchTest) — ✅
- Task 7.0a — Contract + BSOT + canonical schema sync for Day 7 endpoints — ✅
- Task 7.0b — Shared coded 404 exception path — ✅
- Task 7.1 — Station/OperatorStation/Stop domain + EF config + first Trip migration — ✅
- Task 7.1b — Trip Identity internal client + operator logical-FK validation — ✅
- Task 7.2 — Station search + link/create endpoints — ✅
- Task 7.3 — Stop CRU endpoints — ✅
- Task 7.4 — Internal station/stop lookup endpoints — ✅
- Task 7.5 — Gateway routes for station/stop families — ✅
- Task 7.6 — Postman collection: Day-7 station/stop flow — ✅

## Changed files

Key shipped artifacts audited in this close-out:

- `apps/trip/src/VietRide.Trip.Domain/Entities/Station.cs` — canonical Station entity.
- `apps/trip/src/VietRide.Trip.Domain/Entities/OperatorStation.cs` — operator-to-station mapping entity.
- `apps/trip/src/VietRide.Trip.Domain/Entities/Stop.cs` — operator-owned Stop entity.
- `apps/trip/src/VietRide.Trip.Application/Features/Stations/**` — Station search and link/create CQRS, validators, DTO/warning shape.
- `apps/trip/src/VietRide.Trip.Application/Features/Stops/**` — Stop create/list/get/update CQRS, validators, tenant isolation and write-eligibility guard.
- `apps/trip/src/VietRide.Trip.Application/Features/Internal/**` — internal Station/Stop raw lookup queries.
- `apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/IIdentityInternalClient.cs` — Identity logical-FK/write-eligibility abstraction.
- `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/IdentityInternalClient.cs` — calls `GET /internal/v1/operators/{operatorId}` and maps failures per BSOT logical-FK rule.
- `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/InternalJwtTokenFactory.cs` — Trip outbound Internal JWT signer.
- `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/{Station,OperatorStation,Stop}Configuration.cs` — EF snake_case/schema/index constraints.
- `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/{Station,OperatorStation,Stop}Repository.cs` — persistence/search/list implementation.
- `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/20260608111138_InitialTripStationsStops*.cs` and `TripDbContextModelSnapshot.cs` — first Trip migration/snapshot.
- `apps/trip/src/VietRide.Trip.Infrastructure/Design/TripDbContextDesignFactory.cs` — design-time Npgsql enum mapping for shared outbox status.
- `apps/trip/src/VietRide.Trip.Api/Controllers/StationsController.cs` — `GET /v1/stations/search`.
- `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorStationsController.cs` — `POST /v1/operator/stations` single link/create endpoint.
- `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorStopsController.cs` — Stop CRU under `/v1/operator/stops`.
- `apps/trip/src/VietRide.Trip.Api/Controllers/InternalStationsController.cs` and `InternalStopsController.cs` — internal raw lookup endpoints.
- `apps/gateway/src/config/routes.ts` — Trip station/operator-station/operator-stop Gateway route entries.
- `apps/gateway/src/proxy/proxy.middleware.ts` — downstream Internal JWT user-context forwarding (`sub`, `role`, `operatorId`) verified.
- `libs/dotnet/VietRide.Shared.Application/Exceptions/ApplicationExceptions.cs` — `CodedNotFoundException`.
- `libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseExceptionFilter.cs` — coded 404 mapping while preserving generic `RESOURCE_NOT_FOUND` fallback.
- `libs/dotnet/VietRide.Shared.Persistence/**` — canonical shared `outbox_events` mapping used by inherited DbContexts.
- `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/20260609023114_RenameOutboxMessagesToOutboxEvents*.cs` and snapshot/design factory — Identity migration/design-time alignment for canonical shared outbox table.
- `docs/api/postman/vietride.postman_collection.json` and `docs/api/postman/README.md` — cumulative Postman Day-7 Gateway flow and local harness documentation.
- `scripts/run-day7-newman-local.js` and `package.json` — deterministic local Newman runner (`npm run postman:day7:local`).
- `infra/docker/docker-compose.yml` — production-like full app profile runtime config, including Trip → Identity internal base URL and all app health endpoints.
- `db-schema/trip-route-vehicle/schema.sql` and `README.md` — canonical schema/extension/outbox documentation.
- `VietRide_API_Contract_v1.md` and `BACKEND_SOURCE_OF_TRUTH.md` — Day-7 API/registry/changelog sync.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build "apps/trip/VietRide.Trip.sln" -c Release` | PASS | `Build succeeded. 0 Warning(s) 0 Error(s).` |
| `dotnet build "apps/identity/VietRide.Identity.sln" -c Release` | PASS | `Build succeeded. 0 Warning(s) 0 Error(s).` |
| `dotnet build "libs/dotnet/VietRide.Libs.sln" -c Release` | PASS | `Build succeeded. 0 Warning(s) 0 Error(s).` |
| `dotnet format "apps/trip/VietRide.Trip.sln" --verify-no-changes` | PASS | No output; exit code 0. |
| `dotnet format "apps/identity/VietRide.Identity.sln" --verify-no-changes` | PASS | No output; exit code 0. |
| `dotnet format "libs/dotnet/VietRide.Libs.sln" --verify-no-changes` | PASS | No output; exit code 0. |
| `$env:VIETRIDE_TRIP_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev'; dotnet test "apps/trip/VietRide.Trip.sln" -c Release --logger "console;verbosity=minimal"` | PASS | Unit: 50/50 passed; Integration: 6/6 passed; 0 failed/skipped. Includes NetArchTest layering. |
| `dotnet test "apps/identity/VietRide.Identity.sln" -c Release --logger "console;verbosity=minimal"` | PASS | Unit: 198/198 passed; Integration: 121/121 passed; 0 failed/skipped. |
| `dotnet test "libs/dotnet/VietRide.Libs.sln" -c Release --logger "console;verbosity=minimal"` | PASS | Shared Web unit tests: 65/65 passed; 0 failed/skipped. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects + dependency task succeeded. Tracking Prisma source-map warning remains non-fatal. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 TS projects succeeded; Nx cache used for all lint tasks. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | TS tests succeeded: gateway 64, tracking 29, contracts 21, notification 2, rag 2; no-test libs exited 0; 0 failed. |
| `dotnet ef migrations has-pending-model-changes -p "apps/trip/src/VietRide.Trip.Infrastructure" -s "apps/trip/src/VietRide.Trip.Api"` | PASS | `No changes have been made to the model since the last migration.` Non-fatal host-probing warning about missing `INTERNAL_JWT_SECRET`; design-time factory continued. |
| `dotnet ef migrations has-pending-model-changes -p "apps/identity/src/VietRide.Identity.Infrastructure" -s "apps/identity/src/VietRide.Identity.Api"` | PASS | `No changes have been made to the model since the last migration.` |
| `$env:TRIP_DESIGN_CONNECTION='Host=localhost;Port=5432;Database=vietride_trip_audit_7;Username=vietride;Password=vietride_dev'; dotnet ef database update ...; dotnet ef database update 0 ...; dotnet ef database update ...` | PASS | Trip temp DB applied `20260608111138_InitialTripStationsStops`, reverted to `0`, and re-applied. Inspect showed `operator_stations`, `outbox_events`, `stations`, `stops`, `__ef_migrations_history`; no `outbox_messages`. |
| `$env:IDENTITY_DESIGN_CONNECTION='Host=localhost;Port=5432;Database=vietride_identity_audit_7;Username=vietride;Password=vietride_dev'; dotnet ef database update ...; dotnet ef database update 0 ...; dotnet ef database update ...` | PASS | Identity temp DB applied all migrations through `20260609023114_RenameOutboxMessagesToOutboxEvents`, reverted to `0`, and re-applied. Inspect showed canonical `outbox_events`. |
| `docker compose --env-file ".env" -f "infra/docker/docker-compose.yml" --profile app up -d --build` | PASS | Full app stack rebuilt/started. Compose warned missing Google OAuth env vars; not relevant to Day-7 Trip flow. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | PASS | 13 containers healthy/up: gateway, identity, trip, booking, payment, parcel, tracking, notification, rag, postgres, pgbouncer, redis, rabbitmq. |
| `/health` matrix via `Invoke-WebRequest` against ports `3000,5001,5002,5003,5004,5005,3001,3002,3003` | PASS | All HTTP 200: gateway, identity, trip, booking, payment, parcel, tracking, notification, rag. |
| `node -e "JSON.parse(...collection...); JSON.parse(...env...); JSON.parse(...package.json...); console.log('json ok')"` | PASS | Printed `json ok`. |
| `node --check "scripts/run-day7-newman-local.js"` | PASS | Syntax check exit code 0. |
| `node "scripts/run-day7-newman-local.js"` | PASS | Real Gateway E2E via Postman/Newman: 15 requests, 15 test scripts, 31 assertions, 0 failed. Covered station search `q=Mien Tay`, empty `q` 422, station link/create, duplicate-nearby warning, Stop create/get/list/update, invalid coords 422, missing/cross-operator/role/non-approved adversarial cases. Tokens redacted/not recorded. |
| Extra Gateway probe for required matrix gaps | PASS | Duplicate link: `POST /v1/operator/stations` returned 200 and DB mapping count stayed `1→1`; station create missing coords returned `422 VALIDATION_ERROR`. Token minted in-process and not printed. |
| Direct Trip internal endpoint probe with short-lived Internal JWT | PASS | Existing Station and Stop returned 200 raw JSON without `success` envelope; missing Station/Stop returned `404 STATION_NOT_FOUND` / `404 STOP_NOT_FOUND`. Token minted in-process and not printed. |
| DB side-effect check after Day-7 Newman | PASS | `stations`: one `Bến xe Miền Tây`, one created Day-7 station; `operator_stations` for approved operator: 2 mappings; Day-7 stops: 1; Trip `outbox_events`: 0 (Day 7 emits no events). |
| Review artifact validation | PASS | Cumulative Postman collection/env parse; Day-7 folder exists and executed through Gateway using `scripts/run-day7-newman-local.js`. |
| Review execution against Docker/local stack | PASS | Timeline Review bullet executed: `q=Mien Tay` returned existing canonical `Bến xe Miền Tây` through Gateway; invalid Stop coords returned `422 VALIDATION_ERROR`. |
| Day-7 Review bullet overall | PASS | Execution-required checks actually ran; no skip. |
| CPM invariant: `git grep -n -E '<PackageReference[^>]+Version=' -- '*.csproj'` | PASS | No `.csproj` `PackageReference Version=` attributes. |
| Banned dependency declarations in manifests | PASS | No banned dependency declaration in `Directory.Packages.props`, `package.json`, or `.csproj` manifests. Broad grep hits are docs/comments/hook rules only. |
| `git log --format=%B -n 50 | Select-String -Pattern 'Co-Authored-By'` | PASS | No `Co-Authored-By` trailer in last 50 commits. |
| `git ls-files --eol ...` policy check | PASS | Tracked `.cs/.csproj/.sln/.props/.targets` use CRLF worktree; tracked `.ts/.tsx/.js/.json/.yml/.yaml/.md/.sh` use LF worktree. |
| Untracked Day-7 artifact EOL probe | PASS | New Identity migration `.cs` files CRLF-only; `docs/handoff/day-7-checklist.md` and `scripts/run-day7-newman-local.js` LF-only. |

## Contract / event / schema changes shipped

- **FE-facing endpoints**:
  - `GET /v1/stations/search?q=&city?=&province?=` — operator auth, `q` required, accent-insensitive `unaccent` contains matching.
  - `POST /v1/operator/stations` — single link/create endpoint; `stationId` link branch; station-fields create branch; duplicate-nearby warning; no Day-7 `Idempotency-Key` requirement.
  - `POST /v1/operator/stops`, `GET /v1/operator/stops`, `GET /v1/operator/stops/{id}`, `PATCH /v1/operator/stops/{id}` — Stop CRU, no DELETE/disable/replacement write path in Day 7.
- **Internal endpoints**:
  - `GET /internal/v1/stations/{id}` and `GET /internal/v1/stops/{id}` — internal auth, raw success DTO, coded ADR 0004 error envelope on 404.
- **Gateway routes**:
  - `/v1/stations`, `/v1/operator/stations`, `/v1/operator/stops` route to Trip with operator role union; Trip enforces method-level Stop write role.
- **Error codes**:
  - Reused/confirmed `STATION_NOT_FOUND`, `STATION_DUPLICATE_NEARBY`, `STOP_NOT_FOUND`; no new Day-7 error code invented.
- **Events**:
  - None emitted by Day-7 business handlers.
- **Schema/migration**:
  - Trip first migration creates Station/OperatorStation/Stop plus canonical inherited `outbox_events` table.
  - Identity includes a reversible migration to rename/reshape inherited `outbox_messages` to canonical `outbox_events`, keeping shared .NET persistence aligned with BSOT/db-schema.
- **BSOT registry/changelog**:
  - Error/internal endpoint registry and §13 changelog row are present (`BACKEND_SOURCE_OF_TRUTH.md:1373-1382`, `1680`, `2678`).

## Known gaps & carry-over for Day 8

- No Day-7 blocker remains.
- Compose still warns that `GOOGLE_OAUTH_CLIENT_ID` / `GOOGLE_OAUTH_CLIENT_SECRET` are unset; this is unrelated to Day-7 Trip Station/Stop E2E and remains an external OAuth credential concern.
- `dotnet ef migrations has-pending-model-changes` for Trip logs a non-fatal host-probing warning if `INTERNAL_JWT_SECRET` is not supplied; EF continues via the design-time factory and reports no pending model changes.
- Optional hardening for the cumulative Postman artifact: add separate collection requests for duplicate-link idempotency and station-create missing-coordinates. The behavior was verified in this audit by unit tests and an extra Gateway probe, so this is not a Day-7 closure blocker.

## Notes for Day 8 planning

- Day 8 Route/RouteStop can proceed on top of Station/Stop. Use `npm run postman:day7:local` (or `node scripts/run-day7-newman-local.js`) whenever Day-8 changes might regress Day-7 Station/Stop through Gateway.
- If Day 8 adds Trip migrations, keep inherited shared outbox as `outbox_events`; do not reintroduce `outbox_messages`.
- Route/RouteStop implementation should reuse the internal Station/Stop lookup contract and tenant/operator validation behavior established here.
