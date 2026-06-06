# Day 5 — Final checklist

> Produced by `/audit-day 5` AFTER all tasks are done and verification ran.
> Honest record: this audit re-read the code/SOT and re-ran the verification matrix against the current on-disk working tree. The working tree includes post-fix uncommitted changes and one untracked validator file; include them when staging the Day-5 deliverable.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 5 (Fri 2026-05-29) — Identity Service: Staff initial password + FCM tokens
- **Plan**: `docs/handoff/day-5-plan.md`
- **Status**: ✅ READY

## Audit verdict

- **Truth-correct?** ✅ Yes for the current working tree.
  - `technical_context_v7` requires `SET_INITIAL_PASSWORD` UUID token TTL 48h and `PENDING_INITIAL_PASSWORD` users cannot login (`SU26SE101_VIETRIDE_technical_context_v7.md:535-541`); implementation generates UUID v4 + `now+48h` (`apps/identity/src/VietRide.Identity.Infrastructure/Security/InitialPasswordTokenService.cs:7-11`), sets password/status through the domain guard (`apps/identity/src/VietRide.Identity.Domain/Entities/User.cs:139-151`), and blocks login with `AUTH_PENDING_INITIAL_PASSWORD` (`apps/identity/src/VietRide.Identity.Application/Features/Auth/Login/LoginCommandHandler.cs:58-60`).
  - API contract documents `POST /v1/auth/set-initial-password`, `POST/DELETE /v1/auth/device-token`, and `POST /v1/operator/users/{userId}/resend-initial-password` with no Idempotency-Key (`VietRide_API_Contract_v1.md:354-547`); Gateway routes expose public set-initial-password and OPERATOR_ADMIN resend (`apps/gateway/src/config/routes.ts:41-75`) and middleware whitelist includes public set-initial-password (`apps/gateway/src/app/app.module.ts:71-77`).
  - `technical_context_v7` requires `POST /v1/auth/device-token`, `DELETE /v1/auth/device-token`, duplicate-token claim transfer, and internal active-token lookup (`SU26SE101_VIETRIDE_technical_context_v7.md:4804-4811`); implementation uses user-scoped lookup including inactive rows, global active lookup, active-list lookup (`apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/UserDeviceRepository.cs:19-47`), reactivation/claim methods (`apps/identity/src/VietRide.Identity.Domain/Entities/UserDevice.cs:35-54`), and the internal controller is Internal-JWT protected (`apps/identity/src/VietRide.Identity.Api/Controllers/InternalUsersController.cs:9-30`).
  - ADR 0004 internal convention is implemented: success wrapping is skipped for `/internal/*` (`libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseResultFilter.cs:53-82`) while Internal JWT auth errors emit an ADR 0004 error envelope (`libs/dotnet/VietRide.Shared.Web/Authentication/InternalJwtAuthenticationHandler.cs:63-99`).
  - Day-5 ActivityLog enum migration matches the accepted plan and db-schema: migration adds `SET_INITIAL_PASSWORD` and `RESEND_INITIAL_PASSWORD` with `suppressTransaction:true` (`apps/identity/src/VietRide.Identity.Infrastructure/Migrations/20260605160617_AddActivityLogActions.cs:13-19`), and schema lists both (`db-schema/identity-user/schema.sql:51-65`).
- **DoD met?** ✅ Yes for the plan-approved narrowed Day-5 scope.
  - The original timeline says Driver/Assistant/OperatorStaff creation → email link → set password → login (`BE_TIMELINE_VU.md:69-77`), but `docs/handoff/day-5-plan.md:12-14` explicitly narrows Day 5 to reusable activation infrastructure exercised through admin-created `SYSTEM_ADMIN`; operator-created staff onboarding is Day 6 carry-over.
  - Static .NET/TS checks, EF migration apply/rollback/re-apply, full app Docker health matrix, Postman/Newman Gateway E2E, DB side-effect checks, and hard invariants all passed in this audit run.

## DoD result

- [x] ✅ **Reusable initial-password activation through admin-create** — Gateway E2E run `20260606170333069`: seeded bootstrap SYSTEM_ADMIN activated via `POST /v1/auth/set-initial-password` (`200`), then `POST /v1/admin/users` returned `201` for a passwordless SYSTEM_ADMIN (`PENDING_INITIAL_PASSWORD`); DB had active `SET_INITIAL_PASSWORD` token + `SET_INITIAL_PASSWORD` ActivityLog count `1`; consume returned `200` with `status=ACTIVE`; login returned `200`; DB state `ACTIVE|true` for `password_hash`.
- [x] ✅ **Expired token rejected per BSOT** — Gateway/Newman `POST /v1/auth/set-initial-password` with expired token returned HTTP `400`, `error.code=AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED` (BSOT §5.9 wins over old timeline 410 wording).
- [x] ✅ **Resend initial-password link** — OPERATOR_ADMIN same-operator resend returned HTTP `200` with `dataKeys=userId,status,expiresAt`; DB side effects were `1|true|1` = one active new token, old token marked used, one `RESEND_INITIAL_PASSWORD` activity log; consuming old token returned HTTP `400`, `AUTH_INITIAL_PASSWORD_TOKEN_INVALID`.
- [x] ✅ **Device-token register/reactivate/duplicate claim** — Gateway E2E `POST /v1/auth/device-token` returned HTTP `200` with `dataKeys=userDeviceId,fcmToken,platform,isActive`; DELETE retained row inactive (`1|false`); re-register restored the same row (`1|true`); duplicate token owned by another user was claimed by caller A with DB state `1|7256e334-4ab8-4165-a7a2-526ee318f9ca|true`.
- [x] ✅ **DELETE device-token 204 empty body** — Gateway/Newman `DELETE /v1/auth/device-token` returned HTTP `204`; DB row retained with `is_active=false`. Additional valid-token invalid-payload probes for `{}`, `{ "fcmToken": "" }`, and whitespace token returned HTTP `422` with `VALIDATION_ERROR` envelope.
- [x] ✅ **Internal active device-token endpoint** — direct Identity `GET /internal/v1/users/{userId}/device-tokens` with valid Internal JWT returned HTTP `200`, raw list shape (`isEnvelope=False`), and included the active token only; no-auth request returned HTTP `401` with `success:false`, `error.code=AUTH_TOKEN_INVALID`, and `meta.traceId`.
- [x] ✅ **`PENDING_INITIAL_PASSWORD` cannot login** — Gateway/Newman login before initial password returned HTTP `403`, `error.code=AUTH_PENDING_INITIAL_PASSWORD`.
- [x] ✅ **ActivityLog enum migration/schema** — EF apply → rollback-to-previous → re-apply succeeded; DB enum query returned `RESEND_INITIAL_PASSWORD` and `SET_INITIAL_PASSWORD`; schema file lists both. `Down()` is intentionally no-op for Postgres enum values; EF rollback command completed and re-apply completed.
- [x] ✅ **Build/format/test + endpoint coverage/Gateway/Swagger** — Identity build `0 Warning(s) 0 Error(s)`, Identity tests `unit 138/138`, `integration 62/62`; shared libs build `0 Warning(s) 0 Error(s)`, Shared.Web tests `57/57`; TS Nx build/lint/test all succeeded; Gateway tests include route/access-gate coverage for Day-5 route changes; controllers include `ProducesResponseType` annotations.
- [x] ✅ **Hard invariants held** — `git diff --check` clean; no `.csproj` `PackageReference Version=`; targeted banned-dependency declarations absent; all commits have no `Co-Authored-By` trailer; worktree EOL policy check returned `WORKTREE_EOL_BAD_COUNT=0`.

## Tasks completed

- Task 5.0 — Architecture baseline: initial-password token service, UserDevice repository lookups, email-link method, ActivityLog enum migration — ✅ verified.
- Task 5.1 — `set-initial-password` command/handler/endpoint — ✅ verified by code + Gateway E2E.
- Task 5.2 — UserDevice register/delete with reactivation + claim transfer — ✅ verified by tests + Gateway/DB E2E.
- Task 5.3 — OPERATOR_ADMIN tenant-isolated resend initial-password link — ✅ verified by code + Gateway/DB E2E.
- Task 5.4 — Internal active device-token endpoint for Notification — ✅ verified by direct Internal-JWT probe.
- Task 5.5a — Block `PENDING_INITIAL_PASSWORD` login — ✅ verified by Gateway E2E.
- Task 5.5b — Gateway public set-initial-password + OPERATOR_ADMIN resend route — ✅ verified by route table/code + Nx tests + Gateway E2E.
- Task 5.6a — Wire token generation/email/activity log into admin-create — ✅ verified by Gateway/DB E2E.
- Task 5.6b — API contract/BSOT/Postman sync — ✅ verified by SOT read, Postman JSON parse, and runtime contract probes.

## Changed files

Day-5 committed-range diff from `bf1d02d..HEAD` spans Identity, Gateway, shared web, SOT/docs, db-schema, and Postman:

- `apps/identity/src/VietRide.Identity.Api/Controllers/AuthController.cs` — added public `POST /v1/auth/set-initial-password`.
- `apps/identity/src/VietRide.Identity.Api/Controllers/DevicesController.cs` + request DTOs — added `POST/DELETE /v1/auth/device-token`.
- `apps/identity/src/VietRide.Identity.Api/Controllers/OperatorUsersController.cs`, `CurrentUserClaims.cs` — added OPERATOR_ADMIN resend route and `operatorId` claim extraction.
- `apps/identity/src/VietRide.Identity.Api/Controllers/InternalUsersController.cs` — added Internal-JWT active device-token lookup.
- `apps/identity/src/VietRide.Identity.Application/Features/Auth/SetInitialPassword/**` — consumes initial-password token.
- `apps/identity/src/VietRide.Identity.Application/Features/Auth/ResendInitialPassword/**` — tenant-isolated resend flow.
- `apps/identity/src/VietRide.Identity.Application/Features/Devices/**` — register/remove/list device-token handlers and DTOs.
- `apps/identity/src/VietRide.Identity.Application/Features/Auth/Login/LoginCommandHandler.cs` — blocks pending-initial-password login.
- `apps/identity/src/VietRide.Identity.Application/Features/Admin/CreateAdminUser/CreateAdminUserCommandHandler.cs` — generates token, sends/logs email link, writes `SET_INITIAL_PASSWORD` ActivityLog.
- `apps/identity/src/VietRide.Identity.Application/Abstractions/**`, `.../Repositories/**` — initial-password token service, email-link DTO/method, token/device repository methods.
- `apps/identity/src/VietRide.Identity.Domain/Entities/User.cs`, `UserDevice.cs`, `Enums/ActivityLogAction.cs` — initial-password transition, device reactivation/claim methods, enum actions.
- `apps/identity/src/VietRide.Identity.Infrastructure/**` — DI, logging email, repositories, initial-password token service, migration `20260605160617_AddActivityLogActions`.
- `apps/identity/tests/**` — unit/integration coverage for admin-create, set-initial-password, resend, devices, internal endpoint, persistence/DI/activity logs.
- `apps/gateway/src/config/routes.ts`, `apps/gateway/src/app/app.module.ts`, route/access-gate specs — Gateway routing and auth gate updates.
- `libs/dotnet/VietRide.Shared.Web/**`, `tests/dotnet/VietRide.Shared.Web.UnitTests/**` — `RESOURCE_NOT_FOUND` mapping, internal raw-success convention, Internal JWT error envelope.
- `VietRide_API_Contract_v1.md`, `BACKEND_SOURCE_OF_TRUTH.md`, `docs/adr/0004-api-response-envelope.md`, `db-schema/identity-user/schema.sql`, `docs/api/postman/vietride.postman_collection.json`, `docs/handoff/day-5-plan.md` — SOT/ADR/schema/Postman/handoff updates.

Current post-fix working tree also contains these uncommitted/untracked audit-relevant changes that must be included in the Day-5 deliverable:

- Modified: `BACKEND_SOURCE_OF_TRUTH.md`, `docs/adr/0004-api-response-envelope.md`, `libs/dotnet/VietRide.Shared.Web/Authentication/InternalJwtAuthenticationHandler.cs`, `libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseResultFilter.cs`, `tests/dotnet/VietRide.Shared.Web.UnitTests/Filters/ApiResponseResultFilterTests.cs`.
- Modified Identity fix/test files: `DevicesController.cs`, `ResendInitialPasswordCommandHandler.cs`, `ResendInitialPasswordResponseDto.cs`, `RegisterDeviceTokenCommandHandler.cs`, `RegisterDeviceTokenResponseDto.cs`, `DevicesEndpointsTests.cs`, `InternalUsersEndpointsTests.cs`, `OperatorUsersEndpointsTests.cs`, `ResendInitialPasswordCommandHandlerTests.cs`, `RegisterDeviceTokenCommandHandlerTests.cs`, `RemoveDeviceTokenCommandHandlerTests.cs`.
- Untracked: `apps/identity/src/VietRide.Identity.Application/Features/Devices/RemoveDeviceToken/RemoveDeviceTokenCommandValidator.cs`.
- Untracked: `docs/handoff/day-5-checklist.md` (this file).

## Verification run

| Command / check | Result | Notes |
|---|---:|---|
| `git status --short --untracked-files=all` | ✅ PASS | Working tree is intentionally not clean; 16 modified files + 2 untracked files, including the Day-5 post-fix validator and this checklist. |
| `git diff --name-status bf1d02d..HEAD` | ✅ PASS | Confirmed Day-5 committed-range write-set across Identity, Gateway, shared web, docs/SOT/schema/Postman. |
| `git diff --check` | ✅ PASS | No whitespace errors. |
| `node -e "JSON.parse(...vietride.postman_collection.json...); JSON.parse(...vietride.local.postman_environment.json...)"` | ✅ PASS | `Postman collection+env JSON parse OK`. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile infra up -d` | ✅ PASS | Infra started; warnings only for unset bootstrap/Google env vars. |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes` | ✅ PASS | No output / exit 0. |
| `dotnet test apps/identity/VietRide.Identity.sln -c Release --no-restore` | ✅ PASS | Unit `138/138`; integration `62/62`; total `200/200`. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | No output / exit 0. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-restore` | ✅ PASS | `VietRide.Shared.Web.UnitTests` `57/57`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | 10 TS/Nest projects succeeded; Nx cache used for 10/10 tasks. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | 14 TS/Nest projects succeeded; Node `DEP0180` warning only; Nx cache used for 14/14 tasks. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | 10 projects succeeded; Gateway `49/49`, contracts `21/21`, notification/tracking/rag `2/2` each, no-test libs exit 0; Nx cache used for 10/10 tasks. |
| `dotnet ef database update -p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api` | ✅ PASS | Build succeeded; design-time factory path continued after host warning (`INTERNAL_JWT_SECRET must be ≥32 chars`); DB already up to date before rollback. |
| `dotnet ef database update 20260604093220_AddActivityLogs -p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api` | ⚠️ PASS | EF printed `Reverting migration '20260605160617_AddActivityLogActions'. Done.` `Down()` is intentionally no-op for Postgres enum values, so enum labels remain by design. |
| `dotnet ef database update -p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api` | ✅ PASS | Re-applied `20260605160617_AddActivityLogActions`. |
| `docker exec vietride_postgres psql ... activity_log_action` | ✅ PASS | Enum query returned `RESEND_INITIAL_PASSWORD` and `SET_INITIAL_PASSWORD`. |
| `select "MigrationId" from vietride_identity.__ef_migrations_history ...` | ✅ PASS | Returned `20260605160617_AddActivityLogActions`; initial unqualified history-table query failed, corrected with schema-qualified table. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | ✅ PASS | App images built/started; warnings only for unset bootstrap/Google env vars. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | 13 containers up/healthy: gateway, identity, trip, booking, payment, parcel, tracking, notification, rag, pgbouncer, rabbitmq, postgres, redis. |
| Health matrix: Gateway root + Gateway service health routes + direct service/worker `/health` | ✅ PASS | HTTP `200`: gateway-root, gateway-identity, gateway-trip, gateway-booking, gateway-payment, gateway-parcel, identity-direct, trip-direct, booking-direct, payment-direct, parcel-direct, tracking-direct, notification-direct, rag-direct. |
| `C:\Users\user\AppData\Local\Temp\opencode\day5-e2e.ps1` | ✅ PASS | `E2E_OVERALL=PASS failures=0 runId=20260606170333069`; exercised admin-create → token → set password → login, expired token, pending-login block, resend revoke/regenerate, device delete/reactivate, duplicate claim, internal active-token raw list; tokens redacted. |
| Review artifact validation | ✅ PASS | Postman collection/env JSON parse OK; collection contains Day-5 requests for set-initial-password, device-token POST/DELETE, admin-create, login, resend. |
| Review execution against Docker/local stack | ✅ PASS | Execution-required Review bullet ran against real Docker stack via Gateway/Newman + DB checks. Token TTL expired case returned `400 AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED`; FCM duplicate-claim DB state `1|7256e334-4ab8-4165-a7a2-526ee318f9ca|true`. |
| Day-5 Review bullet overall | ✅ PASS | Expired-token adversarial case and duplicate-claim FCM case both executed and passed. No external-credential skip. |
| Direct internal no-auth probe: `GET http://localhost:5001/internal/v1/users/000.../device-tokens` without `X-Internal-Auth` | ✅ PASS | HTTP `401`; body `success:false`, `statusCode:401`, `error.code=AUTH_TOKEN_INVALID`, `meta.traceId` present. |
| Valid-token invalid DELETE payload probes through Gateway | ✅ PASS | `{}`, `{ "fcmToken": "" }`, and `{ "fcmToken": "   " }` returned HTTP `422` with `VALIDATION_ERROR` envelope. Earlier invalid-token probe correctly returned auth `401`, then validation was re-run with a valid token. |
| `git grep -n '<PackageReference[^>]*Version=' -- '*.csproj'` | ✅ PASS | `CPM_OK no csproj PackageReference Version`. |
| Targeted banned dependency declaration grep in `*.csproj`, `Directory.Packages.props`, `package.json` | ✅ PASS | `NO_BANNED_DEP_DECLARATIONS_TARGETED`; broad text grep only finds an allowed documentation comment mentioning deferred observability tools. |
| Commit trailer check: `git log --format=%B --all | Select-String ('Co-' + 'Authored-By')` | ✅ PASS | `NO_COAUTHORED_TRAILER_ALL_COMMITS`. |
| `git ls-files --eol` worktree policy script | ✅ PASS | `WORKTREE_EOL_BAD_COUNT=0` for .NET CRLF and TS/JSON/MD/YAML/SH LF worktree policy. |
| Outbox/event scope grep for `staff.password_set`, `IOutboxStore`, `EventType` under `apps/identity` + BSOT | ✅ PASS | No Day-5 `staff.password_set`/integration event was added; Outbox mentions are only existing EF base/model snapshot mappings. |

## Contract / event / schema changes shipped

- **FE-facing endpoints shipped/wired**:
  - `POST /v1/auth/set-initial-password` — public, no Idempotency-Key, ADR 0004 envelope success `{ userId, status }`.
  - `POST /v1/operator/users/{userId}/resend-initial-password` — authenticated OPERATOR_ADMIN, tenant-isolated by `operatorId`, no Idempotency-Key, ADR 0004 envelope success `{ userId, status, expiresAt }`.
  - `POST /v1/auth/device-token` — authenticated user, no Idempotency-Key, ADR 0004 envelope success `{ userDeviceId, fcmToken, platform, isActive }`.
  - `DELETE /v1/auth/device-token` — authenticated user, no Idempotency-Key, `204 No Content` empty body.
- **Internal endpoint shipped**: `GET /internal/v1/users/{userId}/device-tokens` with Internal JWT; success returns raw active-token list, errors use ADR 0004 error envelope.
- **Gateway routes shipped**: explicit public `/v1/auth/set-initial-password`; authenticated `/v1/operator/users` with `requiredRoles: ['OPERATOR_ADMIN']`.
- **Schema/migration shipped**: `20260605160617_AddActivityLogActions` appends `SET_INITIAL_PASSWORD` and `RESEND_INITIAL_PASSWORD` to `activity_log_action`; `db-schema/identity-user/schema.sql` updated.
- **Events**: none shipped; no Outbox event added. `staff.password_set` remains deferred to Day 10 per timeline/plan.
- **Error codes**: no new error code; reused BSOT §5.9 codes. `RESOURCE_NOT_FOUND` NotFound mapping is implemented/tested.
- **BSOT registry/changelog**: `BACKEND_SOURCE_OF_TRUTH.md` has `1.6.1` Day-5 contract sync and `1.6.2` internal response convention clarification. No new event registry row required.

## Known gaps & carry-over for Day 6

- Day 6 must implement operator-created Driver/Assistant/OperatorStaff creation and reuse the Day-5 initial-password token/email flow; Day 5 intentionally verified the set-password/login round-trip through admin-created `SYSTEM_ADMIN` plus seeded OPERATOR_ADMIN resend E2E only (`docs/handoff/day-5-plan.md:12-14`).
- Day-5 post-audit nit fix added collection-level assertions for the Day-5 Postman items (`set-initial-password`, device-token POST/DELETE, admin-create, pending-login block, resend) so Newman reports the critical response shapes directly.
- Day-5 post-audit nit fix added safe `.env.example` bootstrap guidance for local audits without committing secrets. The earlier E2E still used direct DB prerequisite seeding because the local `.env` was intentionally not edited here.
- Runtime EF `operators` table currently has only `id`, while `db-schema/identity-user/schema.sql` documents the fuller Operator shape. This is outside Day-5 implementation scope but should be resolved by Day 6 Operator work before relying on operator fields.

## Notes for Day 6 planning

- Reuse `IInitialPasswordTokenService`, `IEmailService.SendAccountCreatedLinkAsync`, and the Day-5 `POST /v1/auth/set-initial-password` endpoint for operator-created Driver/Assistant/OperatorStaff onboarding.
- Keep Day-5 endpoint idempotency semantics unchanged: no Idempotency-Key unless BSOT §5.6 changes.
- Preserve internal HTTP convention: `/internal/*` success raw DTO/list; internal errors standardized ADR 0004 envelope.
- Keep response shapes verified here (`resend: userId/status/expiresAt`, `device-token: userDeviceId/fcmToken/platform/isActive`) to avoid FE contract drift.
