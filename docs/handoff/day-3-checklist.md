# Day 3 — Final checklist

> Produced by `/audit-day 3` after the Day-3 gap-fix pass and final verification.
> Honest record: the earlier audit found TS/Gateway ADR 0004 drift and missing automated happy-path endpoint coverage; those gaps were fixed and re-reviewed before closing.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 3 (Jira: SCV-65) — Identity Service: User + Auth foundation
- **Plan**: `docs/handoff/day-3-plan.md`
- **Status**: ✅ READY

## Audit verdict

- **Truth-correct?** ✅ Yes.
  - Identity runtime auth flow and EF migration are verified green.
  - ADR 0004 is now aligned across .NET + TS/Gateway:
    - Gateway proxy fallback errors emit ADR 0004 envelopes: `UPSTREAM_UNAVAILABLE` at `apps/gateway/src/proxy/proxy.middleware.ts:88-95`, `ROUTE_NOT_FOUND` at `apps/gateway/src/proxy/proxy.middleware.ts:134-143`.
    - `libs/shared/contracts/src/dtos/api-response.ts:14-18` requires `meta.traceId`, matching ADR 0004 and .NET `ApiMeta.TraceId`.
    - `libs/shared/contracts/src/dtos/query-options.ts:29-33` clamps `pageSize` to `1..100`, matching BSOT §5.7 and .NET `QueryOptions`.
    - `libs/shared/nest-common/src/pipes/zod-validation.pipe.ts:20-28` uses BSOT registry code `VALIDATION_ERROR` and preserves issues for `error.fields[]` mapping.
    - `apps/gateway/src/auth/user-jwt.middleware.ts:33-36` uses `AUTH_TOKEN_INVALID`.
    - BSOT §5.9 now registers `UPSTREAM_UNAVAILABLE` (`BACKEND_SOURCE_OF_TRUTH.md:1403`) and changelog entry `1.5.1` records the registry sync.
    - `libs/shared/nest-common/src/filters/api-response-exception.filter.ts:125-126` maps 429 default errors to registered `RATE_LIMITED`, not an unregistered `TOO_MANY_REQUESTS`.
- **DoD met?** ✅ Yes.
  - Timeline DoD (`register → OTP → verify → login → access+refresh`, JWKS serves public key) passed through Gateway.
  - Plan-level automated endpoint-test DoD is now met: `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AuthEndpointsTests.cs` has happy-path tests for register, verify-email, login, refresh, logout and keeps existing error-case tests.
  - Final read-only `dotnet-reviewer` returned `READY`; final read-only `nest-reviewer` returned `READY` after the 429 mapping/stale alias fix.

## DoD result

- [x] ✅ **Architecture baseline deps via CPM** — MediatR v11, FluentValidation, BCrypt.Net-Next, NetArchTest, NSubstitute, StackExchange.Redis are centrally versioned; final targeted checks found no `.csproj` `PackageReference Version=` attributes and no MediatR v12+ declaration.
- [x] ✅ **MediatR pipeline behaviors and Clean Architecture test baseline** — Identity build/tests pass, including unit tests `69/69`; NetArchTest coverage is included in Identity unit tests.
- [x] ✅ **EF migration from empty DB** — fresh DB `vietride_identity_audit_day3_final2` applied `20260531103145_InitIdentityAuth`; schema inspection found 7 Day-3 tables, updated_at triggers for `users`, `oauth_identities`, `refresh_tokens`, `user_devices`, and `pgcrypto`; rollback to `0` succeeded and the temporary DB was dropped.
- [x] ✅ **Password hashing/JWT/JWKS foundation** — Identity tests/build pass; JWKS endpoint test asserts raw JWKS RSA shape and no ApiResponse wrapping.
- [x] ✅ **`POST /v1/auth/register` functional smoke** — final Gateway E2E returned `201` and persisted a registration OTP for audit user `auditday3final20260603235643265@example.com`.
- [x] ✅ **`POST /v1/auth/verify-email` functional smoke** — OTP `482072` fetched from local dev DB, verify returned `200`, and user status became `ACTIVE`.
- [x] ✅ **`POST /v1/auth/login` functional smoke** — login returned `200`, `user.status=ACTIVE`, access token present, refresh token present.
- [x] ✅ **`POST /v1/auth/refresh` functional smoke** — refresh returned `200` and rotated to a different refresh token.
- [x] ✅ **`POST /v1/auth/logout` functional smoke** — logout returned `204` with empty body.
- [x] ✅ **Timeline review bullet** — end-to-end via HTTP/Gateway was run; DB status check returned `ACTIVE|0|t|t` for status, failed-login count, created_at present, updated_at present.
- [x] ✅ **Each new endpoint has happy-path + error-case unit/integration tests** — happy-path endpoint tests were added for register, verify-email, login, refresh, logout; existing error-case tests remain for register/verify/login/logout, and refresh is now touched by integration coverage.
- [x] ✅ **ADR 0004 envelope is consistent across .NET + TS/Gateway** — Gateway fallback errors, TS `ApiMeta`, TS `QueryOptions`, Zod validation, Gateway JWT error code, and default 429 mapping were fixed and reviewed.
- [x] ✅ **Hard invariants held for current audit checks** — `git diff --check` clean; recent commit messages have no `Co-Authored-By`; tracked EOL policy check passed; no `.csproj` package versions; no banned dependency declarations/MediatR v12+ found.

## Tasks completed

- Task 3.0 — Architecture baseline: CPM deps + MediatR pipeline behaviors + NetArchTest layering — ✅ verified by build/test/invariant checks.
- Task 3.1 — Domain: User aggregate + sub-entities + enums + value objects — ✅ build/test green.
- Task 3.2 — Infrastructure: EF configurations + DbContext DbSets + repositories + migration — ✅ empty-DB apply/schema inspection/rollback verified.
- Task 3.3 — Auth infrastructure: BCrypt + RS256 + JWKS + email stub — ✅ build/test and live E2E verified.
- Task 3.4 — Application CQRS features + API controllers — ✅ runtime E2E green and endpoint happy-path tests added.
- Task 3.5 — Register `AUTH_OTP_RATE_LIMIT_EXCEEDED` + shared exceptions — ✅ registry includes `AUTH_OTP_RATE_LIMIT_EXCEEDED`.
- Task 3.6 — `EfUnitOfWork` + `AddVietRideDbContext` wiring — ✅ build/test green.
- Task 3.7 — SOT docs ApiResponse rollout — ✅ ADR/API/BSOT updated; `UPSTREAM_UNAVAILABLE` registry sync added as BSOT `1.5.1`.
- Task 3.8 — .NET shared ApiResponse envelope filters/types — ✅ .NET envelope type has non-null `ApiMeta.TraceId` and Identity tests pass.
- Task 3.9 — TS shared contracts + Nest response/exception wrappers — ✅ TS/Gateway drift fixed and reviewed READY.
- Task 3.10 — Identity tests/controller annotations migrated to envelope — ✅ happy-path and error-case endpoint coverage present; Swashbuckle annotations reviewed READY.
- Task 3.11 — AGENTS/agent invariant line — ✅ no audit blocker found.

## Changed files

Current working tree is not clean and includes Day-3 implementation plus docs/plan artifacts.

- `git diff --name-only` count after final audit: **56 tracked files**.
- `git ls-files --others --exclude-standard` count after final audit: **11 untracked files**.
- Untracked files:
  - `apps/gateway/src/auth/user-jwt.verifier.ts`
  - `apps/gateway/src/proxy/proxy.middleware.spec.ts`
  - `apps/identity/src/VietRide.Identity.Application/Abstractions/IFailedLoginPersister.cs`
  - `apps/identity/src/VietRide.Identity.Application/Abstractions/ILoginLockoutCounter.cs`
  - `apps/identity/src/VietRide.Identity.Application/Abstractions/IRefreshTokenFamilyRevoker.cs`
  - `apps/identity/src/VietRide.Identity.Infrastructure/Security/FailedLoginPersister.cs`
  - `apps/identity/src/VietRide.Identity.Infrastructure/Security/RedisLoginLockoutCounter.cs`
  - `apps/identity/src/VietRide.Identity.Infrastructure/Security/RefreshTokenFamilyRevoker.cs`
  - `docs/handoff/day-3-checklist.md`
  - `docs/handoff/day-4-plan.md`
  - `docs/vietride-context-diagram.drawio`

Grouped by delivered area:

- `BACKEND_SOURCE_OF_TRUTH.md` — BSOT registry/changelog sync, including `UPSTREAM_UNAVAILABLE`.
- `Directory.Build.props`, `Directory.Packages.props`, service `.sln`/`.csproj` files — architecture baseline, CPM/package wiring, shared project references.
- `libs/dotnet/VietRide.Shared.*` — MediatR behaviors, shared persistence/unit-of-work, ApiResponse/PagedResult/QueryOptions/filter infrastructure, tests.
- `apps/identity/src/VietRide.Identity.Domain/**` — Day-3 auth domain entities/enums/value behavior.
- `apps/identity/src/VietRide.Identity.Application/**` — auth CQRS commands/handlers/validators/DTOs and security abstractions.
- `apps/identity/src/VietRide.Identity.Infrastructure/**` — EF configurations/repositories/migration/snapshot, BCrypt, RS256/JWKS, Redis OTP/login-lockout, refresh-token security, logging email stub.
- `apps/identity/src/VietRide.Identity.Api/**` — Auth/JWKS controllers, API wiring/config.
- `apps/identity/tests/**` — domain/application/security/API tests; happy-path endpoint coverage added in `AuthEndpointsTests.cs`.
- `apps/gateway/**`, `libs/shared/**` — Gateway auth/proxy/routes and TS shared contracts/Nest common response handling; ADR 0004 drift fixed.
- `infra/docker/docker-compose.yml` — runtime infra/health wiring changes observed in working tree.
- `VietRide_API_Contract_v1.md`, `docs/adr/0004-api-response-envelope.md`, handoff docs — SOT/ADR/handoff updates.

## Verification run

| Command / check | Result | Notes |
|---|---:|---|
| `dotnet test "apps/identity/VietRide.Identity.sln" -c Release` | ✅ PASS | Identity integration `15/15`, unit `69/69`; total `84` passed. |
| `dotnet build "apps/identity/VietRide.Identity.sln" -c Release` | ✅ PASS | Build succeeded, `0 Warning(s)`, `0 Error(s)`. |
| `dotnet format "apps/identity/VietRide.Identity.sln" --verify-no-changes` | ✅ PASS | No changes reported. |
| `dotnet build "libs/dotnet/VietRide.Libs.sln" -c Release` | ✅ PASS | Build succeeded, `0 Warning(s)`, `0 Error(s)`. |
| `dotnet format "libs/dotnet/VietRide.Libs.sln" --verify-no-changes` | ✅ PASS | No changes reported. |
| `dotnet test "libs/dotnet/VietRide.Libs.sln" -c Release --no-build` | ✅ PASS | Shared.Web tests `55/55`. |
| `dotnet build/format/test "apps/trip/VietRide.Trip.sln"` | ✅ PASS | Build `0W/0E`; Unit `1/1`, Integration `2/2`. |
| `dotnet build/format/test "apps/booking/VietRide.Booking.sln"` | ✅ PASS | Build `0W/0E`; Unit `1/1`, Integration `2/2`. |
| `dotnet build/format/test "apps/payment/VietRide.Payment.sln"` | ✅ PASS | Build `0W/0E`; Unit `1/1`, Integration `2/2`. |
| `dotnet build/format/test "apps/parcel/VietRide.Parcel.sln"` | ✅ PASS | Build `0W/0E`; Unit `1/1`, Integration `2/2`. |
| `npx nx run gateway:test --skip-nx-cache` | ✅ PASS | Gateway tests `16/16`; output includes expected proxy-test log lines. |
| `npx nx run gateway:lint` | ✅ PASS | No lint errors. |
| `npx nx run gateway:build` | ✅ PASS | Webpack compiled successfully. |
| `npx nx run contracts:test --skip-nx-cache` | ✅ PASS | Contracts tests `21/21`. |
| `npx nx run contracts:lint && npx nx run contracts:build` | ✅ PASS | No lint/build errors. |
| `npx nx run nest-common:test --passWithNoTests --skip-nx-cache` | ✅ PASS | No tests found, exit `0`. |
| `npx nx run nest-common:lint && npx nx run nest-common:build` | ✅ PASS | No lint/build errors. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | 10 TS projects built. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | 14 TS projects linted. Node warning only: `DEP0180 DeprecationWarning: fs.Stats constructor is deprecated`. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | 10 TS projects tested; Gateway `16/16`, contracts `21/21`. |
| EF fresh DB apply/inspect/rollback on `vietride_identity_audit_day3_final2` | ✅ PASS | Applied `20260531103145_InitIdentityAuth`, found 7 tables + 4 updated_at triggers + `pgcrypto`; rollback to `0` succeeded; temp DB dropped. |
| `docker compose -f "infra/docker/docker-compose.yml" --profile app up -d --build gateway` with valid `INTERNAL_JWT_SECRET` | ✅ PASS | Gateway and app dependency services rebuilt/recreated; containers became healthy. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | Gateway, Identity, Trip, Booking, Payment, Parcel, PgBouncer, Tracking, Notification, RAG, Postgres, Redis, RabbitMQ all healthy/up. |
| HTTP health matrix: Gateway `/health`, Gateway `/v1/identity/health`, services `:5001..:5005/health` | ✅ PASS | All returned HTTP `200`. |
| `curl.exe -s -i "http://localhost:3000/does-not-exist-day3-audit"` | ✅ PASS | Returned HTTP `404` ADR 0004 envelope with `error.code="ROUTE_NOT_FOUND"` and `meta.traceId`. |
| Final Day-3 auth E2E via Gateway | ✅ PASS | User `auditday3final20260603235643265@example.com`: register `201`; OTP `482072`; verify `200`; login `200`; refresh `200` rotated; logout `204`; DB `ACTIVE|0|t|t`. |
| Final `dotnet-reviewer` review | ✅ READY | No blocker/should-fix/nit; confirmed happy-path tests + Swashbuckle annotations. |
| Final `nest-reviewer` review | ✅ READY | Confirmed TS/Gateway ADR 0004 fixes, registry mapping, and stale alias cleanup. |
| `git diff --check` | ✅ PASS | No output. |
| `git log --format=%B -n 20 | Select-String -Pattern "Co-Authored-By"` | ✅ PASS | No output. |
| Targeted grep: `<PackageReference[^>]*Version=` in `*.csproj` | ✅ PASS | No files found. |
| Targeted grep: banned deps / MediatR v12+ in `*.csproj`, `Directory.Packages.props`, `package.json` | ✅ PASS | No dependency declarations found. |
| Tracked EOL policy check via `git ls-files --eol` + Node script | ✅ PASS | `EOL check PASS`. |

## Contract / event / schema changes shipped

- **Endpoints shipped / wired through Gateway**:
  - `POST /v1/auth/register`
  - `POST /v1/auth/verify-email`
  - `POST /v1/auth/login`
  - `POST /v1/auth/refresh`
  - `POST /v1/auth/logout`
  - `GET /v1/.well-known/jwks.json`
- **Schema/migration shipped**:
  - `20260531103145_InitIdentityAuth` creates Day-3 Identity auth tables plus minimal `operators` stub and inherited `outbox_messages` table.
  - Empty-DB apply, schema inspection, rollback, and cleanup were verified.
- **Error/contract registry**:
  - `AUTH_OTP_RATE_LIMIT_EXCEEDED` exists in BSOT §5.9.
  - `UPSTREAM_UNAVAILABLE` was added to BSOT §5.9 Generic group with HTTP `502` and changelog `1.5.1`.
  - ADR 0004/API contract says errors must use `ApiResponse` envelope and `application/problem+json` is dropped; Gateway/TS implementation now matches.
- **Events**: no Day-3 outbox integration event intentionally emitted; plan defers `identity.user.created`/Outbox publisher work to later Identity/Notification scope.

## Known gaps & carry-over for Day 4

- No Day-3 blocker remains after the gap-fix pass and final review.
- Working tree hygiene before commit/PR: there are 56 tracked changed files and 11 untracked files, including `docs/handoff/day-4-plan.md` and `docs/vietride-context-diagram.drawio`. Stage/split intentionally.
- Operator stub carry-over: `operators` is intentionally Day-3 PK-only stub to satisfy `users.operator_id` FK. Full `operators` schema/audit columns remain later Identity/operator scope.

## Notes for Day 4 planning

- Keep ADR 0004 invariant: success in `.data`, errors in `.error.code`, status line authoritative; JWKS and service `/health` stay raw/exempt.
- Keep Gateway-generated FE-facing fallback errors enveloped; Gateway pass-through still forwards downstream service envelopes verbatim.
- Timeline text says `PENDING_VERIFICATION → ACTIVE`, but higher-priority API/schema/code use `PENDING_EMAIL_VERIFICATION → ACTIVE`; keep the latter unless SOT is changed.
- Do not add dependencies without explicit approval; final invariant checks passed without new banned deps.
