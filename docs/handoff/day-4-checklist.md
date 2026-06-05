# Day 4 — Final checklist

> Produced by `/audit-day 4` as a read-only audit. The audit wrote only this checklist.
> Honest record: progress tracker rows in `docs/handoff/day-4-plan.md` were ignored and the code/docs were re-read against the source-of-truth hierarchy.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 4 — Identity Service: Google OAuth + Complete Phone + Admin bootstrap (no Jira key in timeline)
- **Plan**: `docs/handoff/day-4-plan.md`
- **Status**: ✅ READY

## Audit verdict

- **Truth-correct?** ✅ Yes.
  - Day-4 behavior matches the source-of-truth hierarchy for Google OAuth/linking, phone-required Gateway enforcement, complete-profile, `GET /v1/users/me`, System Admin bootstrap/admin-created users, `activity_logs`, API contract updates, error registry/changelog, and Gateway routing.
  - Gateway admin operator routing is truth-correct in the current tree: `/v1/admin/operators` routes to Identity with `SYSTEM_ADMIN` RBAC (`apps/gateway/src/config/routes.ts:53-58`), and route tests cover `/v1/admin/operators/{id}/approve` (`apps/gateway/src/config/routes.spec.ts:56-75`) against top SOT/API contract entries (`SU26SE101_VIETRIDE_technical_context_v7.md:599`; `VietRide_API_Contract_v1.md:1371-1394`).
- **DoD met?** ✅ Yes.
  - Every Day-4 DoD bullet in `BE_TIMELINE_VU.md:60-67` is covered by code evidence and verification commands.
  - The Day-4 Review bullet is satisfied: the collection covers all three auth paths, Docker/Gateway execution passed for email/password and admin-created paths, the Google endpoint negative path passed, and the real Google OAuth happy path is recorded as an accepted `SKIP` because no external `GOOGLE_ID_TOKEN` was available in the audit environment.
  - Identity build/format/test, shared libs build/format/test, Gateway lint/build/test, empty-DB EF migration apply, local health smoke matrix, Docker-backed functional E2E for feasible Day-4 paths, Postman artifact JSON check, and hard-invariant checks all passed.

## DoD result

- [x] ✅ **Google OAuth new email creates an ACTIVE Google passenger and token bundle** — valid Google token is verified with configured audience and verified email (`apps/identity/src/VietRide.Identity.Infrastructure/Security/GoogleIdTokenVerifier.cs:25-67`); new email calls `User.CreateGoogleAccount` and adds `OAuthIdentity` (`apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin/GoogleLoginCommandHandler.cs:46-80`); the domain factory sets `Phone = null`, `PasswordHash = null`, `Role = PASSENGER`, `Status = ACTIVE` (`apps/identity/src/VietRide.Identity.Domain/Entities/User.cs:71-90`); access tokens include `hasPhone=false` when phone is null (`apps/identity/src/VietRide.Identity.Infrastructure/Security/RsaAccessTokenService.cs:52-58`). Matches SOT Google rules (`SU26SE101_VIETRIDE_technical_context_v7.md:1316-1319`, `:4708-4711`).
- [x] ✅ **Google OAuth auto-link for an existing email/password account** — if provider subject is not linked but email exists, the handler loads the existing user, checks it can login, creates an `OAuthIdentity`, and issues tokens (`GoogleLoginCommandHandler.cs:55-80`, `:87-107`), matching SOT auto-link rule (`SU26SE101_VIETRIDE_technical_context_v7.md:4708-4711`).
- [x] ✅ **Google OAuth repeat for an already-linked subject logs in normally** — provider-subject lookup returns the linked user (`GoogleLoginCommandHandler.cs:50-55`), then records login and issues access + refresh tokens (`GoogleLoginCommandHandler.cs:87-107`).
- [x] ✅ **Invalid/forged/expired/unverified Google token → `401 AUTH_GOOGLE_TOKEN_INVALID`** — verifier rejects blank/invalid/missing-claim/unverified-email tokens (`GoogleIdTokenVerifier.cs:29-60`); handler maps Google invalid JWT failures to `UnauthorizedException("AUTH_GOOGLE_TOKEN_INVALID")` (`GoogleLoginCommandHandler.cs:122-141`); API contract documents the `401` (`VietRide_API_Contract_v1.md:256-264`); BSOT registry and changelog include the new code (`BACKEND_SOURCE_OF_TRUTH.md:1314-1320`, `:2668-2671`).
- [x] ✅ **Passenger with `phone IS NULL` is blocked on non-whitelisted Gateway endpoints** — top SOT requires Gateway-level `403 AUTH_PHONE_REQUIRED` for `phone IS NULL` + `role=PASSENGER` (`SU26SE101_VIETRIDE_technical_context_v7.md:1320-1333`); Gateway checks `role === 'PASSENGER'`, false/missing `hasPhone`, and non-whitelisted path before returning `AUTH_PHONE_REQUIRED` (`apps/gateway/src/proxy/proxy.middleware.ts:115-125`); specs cover boolean false, string false, absent claim, and mixed-route protection (`apps/gateway/src/proxy/proxy.access-gates.spec.ts:164-231`, `:368-389`).
- [x] ✅ **Phone-gate whitelist passes** — whitelist includes `/health`, `/ready`, `GET /v1/users/me`, `POST /v1/users/me/complete-profile`, `POST /v1/auth/logout`, and `POST /v1/auth/refresh` (`apps/gateway/src/proxy/proxy.middleware.ts:85-96`), matching SOT whitelist (`SU26SE101_VIETRIDE_technical_context_v7.md:1328-1333`); specs cover these bypasses, including refresh (`apps/gateway/src/proxy/proxy.access-gates.spec.ts:268-324`).
- [x] ✅ **`hasPhone` claim is emitted in all access tokens and Gateway reads it** — `RsaAccessTokenService.IssueToken` unconditionally adds claim `hasPhone` based on `user.Phone is not null` (`RsaAccessTokenService.cs:52-58`); all Identity access-token paths use that service; Gateway accepts boolean/string `true` only (`apps/gateway/src/proxy/proxy.middleware.ts:67-69`, `:115-125`).
- [x] ✅ **`POST /v1/users/me/complete-profile` sets a valid unused E.164 phone, writes `COMPLETE_PROFILE`, and returns required errors without OTP** — top SOT requires VN E.164, duplicate conflict, already-set validation error, response `{ userId, phone, message }`, and audit log (`SU26SE101_VIETRIDE_technical_context_v7.md:1335-1354`); contract documents the endpoint/errors (`VietRide_API_Contract_v1.md:266-319`); controller is thin and authenticated (`apps/identity/src/VietRide.Identity.Api/Controllers/UsersController.cs:38-59`); handler returns `400 AUTH_PHONE_INVALID_FORMAT`, `409 AUTH_PHONE_ALREADY_REGISTERED`, `422 VALIDATION_ERROR`, updates phone, and writes `ActivityLogAction.COMPLETE_PROFILE` (`apps/identity/src/VietRide.Identity.Application/Features/Users/CompleteProfile/CompleteProfileCommandHandler.cs:29-67`).
- [x] ✅ **`GET /v1/users/me` returns caller profile** — API contract lists `id`, `email`, `displayName`, `phone`, `role`, `operatorId`, `status`, `avatarUrl` (`VietRide_API_Contract_v1.md:321-352`); DTO and handler return those fields (`apps/identity/src/VietRide.Identity.Application/Features/Users/GetMe/GetMeResponseDto.cs:3-12`; `GetMeQueryHandler.cs:20-31`); controller route and Swashbuckle annotations exist (`UsersController.cs:27-35`).
- [x] ✅ **`POST /v1/admin/users` creates passwordless `SYSTEM_ADMIN` pending initial password and rejects non-admins** — top SOT requires subsequent System Admin creation via `POST /v1/admin/users`, status `PENDING_INITIAL_PASSWORD`, and Day-5 deferral for token/email (`SU26SE101_VIETRIDE_technical_context_v7.md:1380-1384`); controller is `v1/admin/users` and sends MediatR (`apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs:14-55`); handler enforces caller role `SYSTEM_ADMIN`, accepts only role `SYSTEM_ADMIN`, rejects duplicate email, and creates a passwordless pending admin (`apps/identity/src/VietRide.Identity.Application/Features/Admin/CreateAdminUser/CreateAdminUserCommandHandler.cs:18-49`; `apps/identity/src/VietRide.Identity.Domain/Entities/User.cs:96-115`); Gateway routes `/v1/admin/users` with `SYSTEM_ADMIN` RBAC (`apps/gateway/src/config/routes.ts:59-64`).
- [x] ✅ **Bootstrap admin startup seeder exists, is cost-12/idempotent, and an ACTIVE System Admin exists in the running stack** — top SOT requires env vars, bcrypt cost 12, ACTIVE status, and idempotency (`SU26SE101_VIETRIDE_technical_context_v7.md:1362-1378`); `Program.cs` invokes the seeder at startup (`apps/identity/src/VietRide.Identity.Api/Program.cs:41-48`); seeder reads `SYSTEM_ADMIN_BOOTSTRAP_*`, hashes with BCrypt cost `12`, inserts `SYSTEM_ADMIN`/`ACTIVE`, and skips if one already exists (`apps/identity/src/VietRide.Identity.Infrastructure/Seed/BootstrapAdminSeeder.cs:26-56`; `apps/identity/src/VietRide.Identity.Infrastructure/Seed/EfSystemAdminBootstrapStore.cs:15-36`); unit tests cover skip, missing env, cost-12 hash, and run-twice idempotency (`apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/BootstrapAdminSeederTests.cs:12-80`); runtime DB query showed one ACTIVE `SYSTEM_ADMIN` (`verify.admin@vietride.app`) plus one admin-created `PENDING_INITIAL_PASSWORD` user, and Identity logs show bootstrap skips when a System Admin exists.
- [x] ✅ **`activity_logs` schema/domain/migration exists and applies from an empty DB** — canonical schema has `activity_log_action` with `COMPLETE_PROFILE` and `activity_logs` table/indexes (`db-schema/identity-user/schema.sql:51-66`, `:284-297`); EF enum/DbSet registration exists (`apps/identity/src/VietRide.Identity.Infrastructure/IdentityDbContext.cs:24-33`, `:40-55`); EF configuration maps snake_case columns, enum type, indexes, and restrict FK (`apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/ActivityLogConfiguration.cs:11-56`); migration creates table/enum and reverses them in `Down()` (`apps/identity/src/VietRide.Identity.Infrastructure/Migrations/20260604093220_AddActivityLogs.cs:15-85`); empty audit DB migration apply passed.
- [x] ✅ **Each new endpoint has happy-path and error-case tests; build/format/test are green** — Google tests include happy path and invalid token (`apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AuthEndpointsTests.cs:136`, `:300`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/GoogleLoginCommandHandlerTests.cs:108`, `:165`); complete-profile tests cover happy/invalid/missing/duplicate/already-set cases (`apps/identity/tests/VietRide.Identity.IntegrationTests/Api/UsersEndpointsTests.cs:65-116`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Users/CompleteProfileCommandHandlerTests.cs:12-105`); admin-create tests cover happy/non-admin/duplicate (`apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AdminUsersEndpointsTests.cs:32-59`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Admin/CreateAdminUserCommandHandlerTests.cs:11-74`); Gateway gate/routing specs cover phone gate, RBAC, Google route, and admin operator routing (`apps/gateway/src/proxy/proxy.access-gates.spec.ts:105-389`; `apps/gateway/src/config/routes.spec.ts:33-75`). Verification commands below passed.
- [x] ✅ **Each new public endpoint has Gateway route entry and Swashbuckle annotations** — `/v1/auth/google` is public in Gateway and Nest public middleware whitelist (`apps/gateway/src/config/routes.ts:39-45`; `apps/gateway/src/app/app.module.ts:71-79`) and has API annotations (`apps/identity/src/VietRide.Identity.Api/Controllers/AuthController.cs:98-107`); `/v1/users` and `/v1/admin/users` routes exist (`apps/gateway/src/config/routes.ts:46`, `:59-64`); User/Admin controller annotations exist (`UsersController.cs:27-49`; `AdminUsersController.cs:26-37`).
- [x] ✅ **Timeline Review bullet: Postman collection covering all 3 auth paths** — the Day-4 auth paths were folded into the cumulative collection `docs/api/postman/vietride.postman_collection.json` (relocated from `docs/handoff/`), which parses as valid JSON and contains folders for email/password, Google OAuth, and admin-created System Admin. Docker-backed execution passed for the email/password path and admin-created path; the Google endpoint negative path passed (`401 AUTH_GOOGLE_TOKEN_INVALID`); the real Google OAuth happy path is an accepted `SKIP` under the verification matrix because no external `GOOGLE_ID_TOKEN` was available.
- [x] ✅ **Hard invariants held** — no `.csproj` `PackageReference Version=` attributes; no actual banned dependency declarations; MediatR remains v11.1.0 (`Directory.Packages.props:59-61`); `Google.Apis.Auth` is through CPM and was explicitly allowed in Day-4 plan; `git diff --check`, tracked EOL, untracked EOL, and recent `Co-Authored-By` checks passed.

## Tasks completed

- Task 4.0 — Architecture baseline: ActivityLog entity/repository, Google verifier abstraction, bootstrap admin startup seeder — ✅ implemented, tested, and runtime current stack has ACTIVE `SYSTEM_ADMIN`.
- Task 4.1 — Domain: User factory for Google accounts + complete profile + admin-created user — ✅ implemented and covered by tests.
- Task 4.2 — Application: Google OAuth login command + `hasPhone` claim — ✅ implemented and covered by tests.
- Task 4.3 — Infrastructure: Google ID-token verifier using `Google.Apis.Auth` — ✅ implemented with audience validation and verified-email guard.
- Task 4.4 — Application + API: complete-profile + GET users/me + admin create-user — ✅ implemented with thin controllers and handler-level authorization.
- Task 4.5 — EF migration for `activity_logs` table + `activity_log_action` enum — ✅ migration exists, is reversible, matches canonical schema, and applies to an empty audit DB.
- Task 4.6 — Gateway phone-required enforcement + RBAC + Google route — ✅ implemented; `/v1/admin/operators*` truth-correct route is present; lint/build/test pass.
- Task 4.7 — API + contract + BSOT registry + timeline correction — ✅ Day-4 endpoints are documented; `AUTH_GOOGLE_TOKEN_INVALID` is in BSOT registry and changelog; top SOT error list is synced.
- Task 4.8 — Env + docker config for Google OAuth + admin bootstrap — ✅ placeholders/env wiring landed; no real secrets observed during audit.

## Changed files

Observed via `git diff --name-status HEAD` and `git ls-files --others --exclude-standard` during audit:

- `BACKEND_SOURCE_OF_TRUTH.md` — Day-4 error registry/changelog update for `AUTH_GOOGLE_TOKEN_INVALID`.
- `SU26SE101_VIETRIDE_technical_context_v7.md` — top-SOT error-list sync.
- `VietRide_API_Contract_v1.md` — Day-4 Identity endpoint contract sections.
- `apps/gateway/src/app/app.module.ts` — public whitelist includes Google OAuth route.
- `apps/gateway/src/auth/user-jwt.verifier.ts` — User JWT verifier/gate support.
- `apps/gateway/src/config/routes.ts` — Gateway route table for Google, users, admin users/operators, and cross-service admin prefixes.
- `apps/gateway/src/config/routes.spec.ts` — route-table tests for admin ownership/longest-prefix behavior.
- `apps/gateway/src/proxy/proxy.middleware.ts` — RBAC, phone-required gate, and ADR 0004 error envelope behavior.
- `apps/gateway/src/proxy/proxy.access-gates.spec.ts` — RBAC/phone/mixed-route/Google-route tests.
- `apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs` — `POST /v1/admin/users`.
- `apps/identity/src/VietRide.Identity.Api/Controllers/Requests/CompleteProfileRequest.cs` — complete-profile request DTO.
- `apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin/GoogleLoginCommandHandler.cs` — Google login/link/create flow and invalid-token mapping.
- `apps/identity/src/VietRide.Identity.Application/Features/Users/CompleteProfile/CompleteProfileCommand.cs` — complete-profile command.
- `apps/identity/src/VietRide.Identity.Application/Features/Users/CompleteProfile/CompleteProfileCommandHandler.cs` — phone validation/update/activity-log behavior.
- `apps/identity/src/VietRide.Identity.Application/Features/Users/CompleteProfile/CompleteProfileCommandValidator.cs` — complete-profile validation.
- `apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` — Day-4 DI registrations.
- `apps/identity/src/VietRide.Identity.Infrastructure/Security/GoogleIdTokenVerifier.cs` — Google ID token verifier with audience and verified-email guard.
- `apps/identity/src/VietRide.Identity.Infrastructure/Seed/BootstrapAdminSeeder.cs` — startup seeder logic.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AdminUsersEndpointsTests.cs` — admin users endpoint integration tests.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/UsersEndpointsTests.cs` — users endpoint integration tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/GoogleLoginCommandHandlerTests.cs` — Google login handler tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/Users/CompleteProfileCommandHandlerTests.cs` — complete-profile handler tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/GoogleIdTokenVerifierTests.cs` — Google verifier tests.
- `db-schema/_global/README.md`, `db-schema/_global/SCHEMA_REVIEW_REPORT.md`, `db-schema/identity-user/README.md`, `db-schema/identity-user/schema.sql`, `docs/SECURITY.md` — schema/security docs and activity log/bootstrap alignment notes.
- **Untracked but required for Day 4 source/tests**: `apps/identity/src/VietRide.Identity.Infrastructure/Seed/EfSystemAdminBootstrapStore.cs`, `apps/identity/src/VietRide.Identity.Infrastructure/Seed/ISystemAdminBootstrapStore.cs`, `apps/identity/src/VietRide.Identity.Infrastructure/Seed/SystemAdminBootstrapUser.cs`, `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/BootstrapAdminSeederTests.cs`.
- **Untracked handoff artifacts**: `docs/api/postman/vietride.postman_collection.json` (relocated from `docs/handoff/day-4-auth-paths.postman_collection.json`), `docs/handoff/day-4-checklist.md`.

## Verification run

| Command / check | Result | Notes |
|---|---:|---|
| `dotnet build "apps/identity/VietRide.Identity.sln" -c Release` | ✅ PASS | Build succeeded; `0 Warning(s)`, `0 Error(s)`; elapsed `00:00:07.14`. |
| `dotnet format "apps/identity/VietRide.Identity.sln" --verify-no-changes` | ✅ PASS | No output; no formatting changes required. |
| `dotnet test "apps/identity/VietRide.Identity.sln" -c Release` | ✅ PASS | Unit `100/100` and Integration `25/25` passed; total `125` passed. NetArchTest dependency rules are part of the Identity test suite. |
| `dotnet build "libs/dotnet/VietRide.Libs.sln" -c Release` | ✅ PASS | Build succeeded; `0 Warning(s)`, `0 Error(s)`; elapsed `00:00:03.99`. |
| `dotnet format "libs/dotnet/VietRide.Libs.sln" --verify-no-changes` | ✅ PASS | No output; no formatting changes required. |
| `dotnet test "libs/dotnet/VietRide.Libs.sln" -c Release` | ✅ PASS | Shared.Web UnitTests `55/55` passed. |
| `npx jest --config apps/gateway/jest.config.cts --runInBand --ci --json --outputFile C:\Users\user\AppData\Local\Temp\opencode\gateway-jest-day4.json` | ✅ PASS | Exit `0`; Jest JSON summary: `4/4` suites, `42/42` tests, `success=true`. |
| `npx nx run gateway:lint --skip-nx-cache` | ✅ PASS | Nx reported successful `gateway:lint`; `RESULT:GATEWAY_LINT_EXIT=0`. |
| `npx nx run gateway:build --skip-nx-cache` | ✅ PASS | Nx reported successful `gateway:build` and `nest-common:build`; webpack compiled successfully; `RESULT:GATEWAY_BUILD_EXIT=0`. |
| `dotnet ef database update -p "apps/identity/src/VietRide.Identity.Infrastructure" -s "apps/identity/src/VietRide.Identity.Api"` | ✅ PASS | Existing local DB was already up to date. EF printed a non-blocking host-service warning about `INTERNAL_JWT_SECRET` then continued via design-time context; result: `No migrations were applied. The database is already up to date. Done.` |
| Temp empty-DB EF apply: create `vietride_identity_audit_day4`, set `IDENTITY_DESIGN_CONNECTION`, run `dotnet ef database update -p "apps/identity/src/VietRide.Identity.Infrastructure" -s "apps/identity/src/VietRide.Identity.Api"`, drop temp DB | ✅ PASS | Build succeeded; applied `20260531103145_InitIdentityAuth` and `20260604093220_AddActivityLogs`; temp audit DB dropped afterwards. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` and `docker compose -f "infra/docker/docker-compose.yml" ps` | ✅ PASS | App/infra containers were running and healthy: gateway, identity, trip, booking, payment, parcel, tracking, notification, rag, pgbouncer, postgres, rabbitmq, redis. Compose emitted warnings for shell env defaults, but running containers had required values. |
| `/health` matrix via `Invoke-WebRequest`: `http://localhost:3000/health`, `:5001/health`, `:5002/health`, `:5003/health`, `:5004/health`, `:5005/health` | ✅ PASS | All returned HTTP `200`. |
| Runtime bootstrap DB query: `docker exec vietride_postgres psql -U vietride -d vietride_identity -c "SELECT email, display_name, role, status FROM vietride_identity.users WHERE role = 'SYSTEM_ADMIN' ORDER BY status, email;"` | ✅ PASS | Output included one ACTIVE bootstrap System Admin (`verify.admin@vietride.app`) and one `PENDING_INITIAL_PASSWORD` admin-created user. |
| Runtime bootstrap log check: `docker logs vietride_identity 2>&1 | findstr /I /C:"Bootstrapped initial SYSTEM_ADMIN" /C:"System admin bootstrap skipped" /C:"SYSTEM_ADMIN"` | ✅ PASS | Logs include `System admin bootstrap skipped because a SYSTEM_ADMIN user already exists.`, confirming idempotent skip on later startup with existing admin. |
| `node -e "const fs=require('fs'); JSON.parse(fs.readFileSync('docs/api/postman/vietride.postman_collection.json','utf8')); console.log('RESULT:POSTMAN_JSON_PASS')"` | ✅ PASS | Printed `RESULT:POSTMAN_JSON_PASS`. (collection relocated to `docs/api/postman/`) |
| `powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\user\AppData\Local\Temp\opencode\day4-e2e-httpclient.ps1` | ✅ PASS | Docker-backed Gateway E2E feasible Day-4 paths passed. Final run stamp `20260605152954070`: email/password path `register 201 → verify-email 200 → login 200 → GET /v1/users/me 200 → refresh 200 → logout 204`; no-phone PASSENGER gate path `register 201 → verify 200 → login 200 → GET /v1/bookings 403 AUTH_PHONE_REQUIRED → GET /v1/users/me 200 → complete-profile 200`; admin-created path `admin fixture register/verify/login 201/200/200 → promoted to SYSTEM_ADMIN in audit DB fixture → POST /v1/admin/users 201 → DB SYSTEM_ADMIN\|PENDING_INITIAL_PASSWORD\|passwordless=true → created-admin login before initial password 401`; invalid Google token `POST /v1/auth/google` returned `401 AUTH_GOOGLE_TOKEN_INVALID`; script printed `RESULT:DAY4_DOCKER_E2E_PASS` and `RESULT:E2E_EXIT=0`. |
| E2E DB state check for final run `20260605152954070` | ✅ PASS | `docker exec vietride_postgres psql ...` showed four expected audit users: email passenger ACTIVE/passworded, no-phone fixture passenger ACTIVE/passworded after complete-profile, audit SYSTEM_ADMIN fixture ACTIVE/passworded, and created admin `SYSTEM_ADMIN|PENDING_INITIAL_PASSWORD|passwordless=t`; `activity_logs` has one `COMPLETE_PROFILE` row for the no-phone fixture user. |
| Day-N `Review` bullet from timeline | ✅ PASS | Postman collection artifact covers all 3 auth paths and parses. Docker-backed execution passed for email/password and admin-created paths, and the Google endpoint negative path (`invalid.audit.token`) returned `401 AUTH_GOOGLE_TOKEN_INVALID`. Real Google OAuth happy path recorded as `SKIP` because no external `GOOGLE_ID_TOKEN` was available in the audit environment; this skip is accepted under the verification matrix. |
| Read-only specialist review — Identity | ✅ APPROVE | Dotnet reviewer approved Identity code/docs against Day-4 scope and SOT. |
| Read-only specialist review — Gateway | ✅ APPROVE | Nest reviewer approved Gateway Day-4 scope; only non-blocking nits noted for method-scoped public route strictness/test-table coverage. |
| `git diff --check` | ✅ PASS | No whitespace errors. |
| `git log --format=%B -10 | findstr /R /C:"Co-Authored-By"` | ✅ PASS | Printed `RESULT:NO_COAUTHORED_BY`. |
| `git grep -n "Version=" -- "*.csproj"` | ✅ PASS | Printed `RESULT:NO_CSPROJ_VERSION_ATTR`. |
| Actual banned dependency declaration grep in manifests for AutoMapper/OpenTelemetry/Prometheus/Grafana/Tempo/Loki | ✅ PASS | Printed `RESULT:NO_BANNED_DEP_DECLARATIONS`. |
| `git grep -n "MediatR" -- Directory.Packages.props` | ✅ PASS | Shows MediatR v11.1.0 only (`Directory.Packages.props:59-61`). |
| Tracked worktree EOL policy check via `git ls-files --eol` | ✅ PASS | Printed `RESULT:EOL_POLICY_PASS`. |
| Untracked EOL check | ✅ PASS | Printed `RESULT:UNTRACKED_EOL_POLICY_PASS`; untracked `.cs` files are CRLF and untracked `.json`/`.md` files are LF. |

## Contract / event / schema changes shipped

- **Endpoints shipped / documented / routed**:
  - `POST /v1/auth/google` — API contract `VietRide_API_Contract_v1.md:223-264`; API action `apps/identity/src/VietRide.Identity.Api/Controllers/AuthController.cs:98-113`; Gateway route `apps/gateway/src/config/routes.ts:39-45`; Nest public whitelist `apps/gateway/src/app/app.module.ts:71-79`.
  - `POST /v1/users/me/complete-profile` — API contract `VietRide_API_Contract_v1.md:266-319`; API action `apps/identity/src/VietRide.Identity.Api/Controllers/UsersController.cs:44-59`; Gateway `/v1/users` route `apps/gateway/src/config/routes.ts:46`.
  - `GET /v1/users/me` — API contract `VietRide_API_Contract_v1.md:321-352`; API action `UsersController.cs:27-35`.
  - `POST /v1/admin/users` — API contract `VietRide_API_Contract_v1.md:354-401`; API action `apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs:32-55`; Gateway route `apps/gateway/src/config/routes.ts:59-64`.
  - `/v1/admin/operators*` routing remains truth-correct for existing contract/SOT admin operator endpoints: `apps/gateway/src/config/routes.ts:53-58`.
- **Error registry shipped**:
  - `AUTH_GOOGLE_TOKEN_INVALID` added to BSOT §5.9 (`BACKEND_SOURCE_OF_TRUTH.md:1314-1320`) and BSOT §13 changelog version `1.6.0` (`BACKEND_SOURCE_OF_TRUTH.md:2668-2671`). Required BSOT registry + changelog update: ✅ done.
  - Top SOT error-code list includes `AUTH_GOOGLE_TOKEN_INVALID` (`SU26SE101_VIETRIDE_technical_context_v7.md:4650-4652`).
- **Schema/migration shipped**:
  - EF migration `20260604093220_AddActivityLogs` creates `activity_log_action` enum and `vietride_identity.activity_logs` table (`apps/identity/src/VietRide.Identity.Infrastructure/Migrations/20260604093220_AddActivityLogs.cs:15-75`) and drops them in `Down()` (`:78-85`).
  - Canonical schema has matching enum/table/indexes (`db-schema/identity-user/schema.sql:51-66`, `:284-297`). Empty-DB migration apply passed.
  - `db-schema/identity-user/seed.sql` intentionally does not insert bootstrap System Admin; startup seeder owns that (`db-schema/identity-user/seed.sql:31`).
- **Events**:
  - No Day-4 Outbox event intentionally shipped. `identity.user.created` remains deferred to Day 10 for all three creation flows — email register, Google OAuth auto-create, and admin-created users — per `docs/handoff/day-4-plan.md:65-67` and `:306-310`.
- **Config shipped**:
  - `.env.example`, Docker compose, and Identity appsettings include `SYSTEM_ADMIN_BOOTSTRAP_*` and `GOOGLE_OAUTH_*` placeholders/wiring (`.env.example:70-95`; `infra/docker/docker-compose.yml:133-142`; `apps/identity/src/VietRide.Identity.Api/appsettings.json:14-21`). No real secrets observed in committed templates.
- **Review artifact shipped**:
  - `docs/api/postman/vietride.postman_collection.json` (relocated from `docs/handoff/`) covers email/password, Google OAuth, and admin-created System Admin paths and parses as valid JSON.

## Known gaps & carry-over for Day 5

- No Day-4 blocker remains; Day 4 is READY based on current audit.
- **Commit hygiene before final commit**: required Day-4 source/test files are still untracked in the working tree: `EfSystemAdminBootstrapStore.cs`, `ISystemAdminBootstrapStore.cs`, `SystemAdminBootstrapUser.cs`, `BootstrapAdminSeederTests.cs`, plus Day-4 handoff artifacts. Stage them intentionally before committing.
- **Accepted verification skip**: real Google OAuth happy-path E2E requires an external `GOOGLE_ID_TOKEN`, so it is recorded as `SKIP` under the verification matrix. Automated tests cover mocked Google verifier paths plus verifier unit behavior; Docker E2E covered the invalid-token path and the no-phone passenger Gateway gate/complete-profile flow. Optional follow-up: re-run `C:\Users\user\AppData\Local\Temp\opencode\day4-e2e-httpclient.ps1` with `GOOGLE_ID_TOKEN` set.
- **Gateway non-blocking nit**: public auth route entries are prefix-based/partly `ALL` in middleware for some existing auth endpoints; downstream Identity still enforces methods, but method-aware route entries can be tightened later if desired.
- **Day 5 scope**: implement deferred `SET_INITIAL_PASSWORD` token generation + email send/consume flow for passwordless admin/operator/driver/assistant account creation.
- **Day 10 scope**: emit `identity.user.created` Outbox events for all three user creation paths: email registration, Google OAuth auto-create, and admin-created users.

## Notes for Day 5 planning

- Day 5 can proceed from Day 4: Identity/Gateway verification is green, Docker-backed feasible auth flows pass through Gateway, `activity_logs` migration applies from empty DB, local health matrix is green, and the external Google-token E2E is recorded as an accepted skip.
- Keep Gateway generated `FORBIDDEN`, `AUTH_PHONE_REQUIRED`, `UPSTREAM_UNAVAILABLE`, and `ROUTE_NOT_FOUND` errors in the ADR 0004 `ApiResponse` envelope.
- Keep Gateway mixed-route public subpaths explicit; do not regress `/v1/operators/register`, `/v1/admin/operators*`, or VNPay IPN public routes while adding future routes.
- Keep `POST /v1/users/me/complete-profile` aligned with top SOT: no OTP, no token bundle in response, `400 AUTH_PHONE_INVALID_FORMAT` for missing/blank/bad phone, `409 AUTH_PHONE_ALREADY_REGISTERED` for duplicate phone, and `422 VALIDATION_ERROR` only for already-set phone.
