# Day 6 — Final checklist

> Produced by `/audit-day 6`, then updated after remediation of the blocking findings. This checklist reflects the post-fix verification state.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 6 (Mon 2026-06-01) — Operator Service within Identity DB (Jira: SCV-72)
- **Plan**: `docs/handoff/day-6-plan.md`
- **Status**: ✅ READY
- **Audit / remediation date**: 2026-06-08

## Remediation summary

- Fixed the runtime Identity/Npgsql JSONB materialization blocker by mapping the three `Operator` JSONB policy columns through an EF value converter (`string?` domain value ⇄ `JsonElement?` provider value) while preserving PostgreSQL `jsonb` storage.
- Added integration coverage proving `OperatorRepository.GetByIdAsync` materializes JSONB policies as raw JSON strings and that the EF store type remains `jsonb`.
- Made the Day-6 Postman flow self-contained for local Newman execution via `scripts/run-day6-newman-local.js`, which binds to `127.0.0.1`, reads the local dev DB for OTP / SET_INITIAL_PASSWORD tokens, and mints a short-lived SYSTEM_ADMIN JWT from the dev Identity key.
- Added Day-6 `GET /v1/operator/profile` success coverage for both OPERATOR_ADMIN and OPERATOR_STAFF.
- Guarded local-harness-only Postman requests with `localHarnessEnabled`; the normal cumulative collection skips those helper requests unless the local wrapper enables them.
- Re-ran Docker/Gateway Newman Day-6 E2E successfully.

## DoD result

- [x] ✅ **Contract baseline / BSOT registry updated** — API contract documents Day-6 FE-facing and internal endpoints (`VietRide_API_Contract_v1.md:1576-2008`); BSOT records ADR 0004/internal raw-success rule (`BACKEND_SOURCE_OF_TRUTH.md:1235-1240`), error codes (`BACKEND_SOURCE_OF_TRUTH.md:1385-1404`), Day-6 operator status guard/activity-log/reject-subscription rules (`BACKEND_SOURCE_OF_TRUTH.md:1590-1594`), internal endpoint registry (`BACKEND_SOURCE_OF_TRUTH.md:1666-1668`), event registry (`BACKEND_SOURCE_OF_TRUTH.md:1729-1730`), and changelog row (`BACKEND_SOURCE_OF_TRUTH.md:2678`).
- [x] ✅ **Operator self-register → admin approve → operator login** — Newman via Gateway passed: self-register `201` → OTP fetched from local DB → verify-email `200` → approve `200` → operator admin login `200`.
- [x] ✅ **System Admin manual create → set-initial-password → login** — Newman via Gateway passed: admin-create `201` → SET_INITIAL_PASSWORD token fetched from local DB → set-initial-password `200` → operator admin login `200`.
- [x] ✅ **Approve/reject/suspend lifecycle implemented** — lifecycle implementation/tests pass; Day-6 runtime approve passed `200`; no Day-6 operator lifecycle Outbox events were emitted.
- [x] ✅ **Non-APPROVED operator login guard implemented** — `LoginCommandHandler.cs:92-98` re-checks OPERATOR_ADMIN/OPERATOR_STAFF operator status after credential success and before token issuance; Identity tests pass and approved operator login passed at runtime.
- [x] ✅ **Operator-created DRIVER/ASSISTANT/OPERATOR_STAFF flow** — Newman via Gateway passed: OPERATOR_ADMIN created OPERATOR_STAFF `201` → SET_INITIAL_PASSWORD token fetched → set-initial-password `200` → OPERATOR_STAFF login `200`.
- [x] ✅ **Operator profile read/update + policy JSONB Review case** — OPERATOR_ADMIN PATCH profile/policies returned `200`; OPERATOR_ADMIN GET profile returned `200`; OPERATOR_STAFF GET profile returned `200`; OPERATOR_STAFF PATCH profile returned `403 FORBIDDEN`.
- [x] ✅ **Internal operator/subscription/usage endpoints implemented and tested** — Identity integration tests passed 121/121, including internal endpoint coverage.
- [x] ✅ **EF migration applies/rolls back/re-applies** — Day-6 migration apply/rollback/re-apply passed during audit; remediation changed EF mapping only and did not change schema/migration.
- [x] ✅ **Build/format/test/TS/Postman gate** — deterministic build/format/.NET/TS gates passed; Docker stack health passed; Day-6 Newman E2E passed 19/19 requests and 35/35 assertions.

## Tasks completed

- Task 6.0a — Contract baseline: API contract + BSOT registry — ✅ code/docs align at registry level.
- Task 6.0 — Operator + Subscription domain, EF config, migration, Starter seed — ✅ build/test/EF migration verification passed; JSONB runtime mapping fixed without schema drift.
- Task 6.1 — Operator self-register + System-Admin manual create — ✅ runtime Gateway/Newman happy paths passed.
- Task 6.2 — Approve / reject / suspend lifecycle — ✅ implementation/tests pass; approve runtime path passed; no Day-6 Outbox emission.
- Task 6.2b — Login block for non-APPROVED operator users — ✅ implementation/tests pass; approved runtime login paths passed.
- Task 6.3 — Operator-created Driver/Assistant/OperatorStaff creation — ✅ OPERATOR_STAFF create/set-password/login runtime path passed.
- Task 6.4 — Operator profile read/update with policy JSONB — ✅ OPERATOR_ADMIN update/read and OPERATOR_STAFF read/403-update runtime cases passed.
- Task 6.5 — Internal operator/subscription endpoints — ✅ integration tests pass.
- Task 6.6 — Gateway operator-profile route — ✅ route/spec/lint/test pass.
- Task 6.7 — Postman/final docs sync — ✅ collection parses, local Day-6 wrapper runs successfully, and local-only helper requests are skipped unless explicitly enabled.

## Changed files

- `apps/identity/src/VietRide.Identity.Api/Controllers/AdminOperatorsController.cs` — admin operator endpoints including list/lifecycle wiring.
- `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IOperatorRepository.cs` — operator repository contract expanded.
- `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListOperators/ListOperatorsQuery.cs` — admin list query.
- `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListOperators/ListOperatorsQueryHandler.cs` — admin list handler.
- `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListOperators/ListOperatorsQueryValidator.cs` — list query validation.
- `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListOperators/OperatorListItemDto.cs` — list response DTO.
- `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/OperatorConfiguration.cs` — JSONB policy value converter fix.
- `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OperatorRepository.cs` — list/filter/sort implementation.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AdminOperatorsLifecycleEndpointsTests.cs` — lifecycle endpoint coverage.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/DevicesEndpointsTests.cs` — integration fixture/test updates.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/InternalOperatorsEndpointsTests.cs` — internal operator endpoint coverage.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/OperatorUsersEndpointsTests.cs` — operator-user/resend/counter coverage.
- `apps/identity/tests/VietRide.Identity.IntegrationTests/Persistence/OperatorRepositoryPersistenceTests.cs` — JSONB materialization/store-type regression tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/Auth/ResendInitialPasswordCommandHandlerTests.cs` — resend guard coverage.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/Internal/Operators/InternalOperatorHandlersTests.cs` — internal handler tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/OperatorUsers/CreateOperatorUserCommandHandlerTests.cs` — operator-user handler tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/Operators/ProfileOperatorProfileHandlerTests.cs` — profile handler tests.
- `apps/identity/tests/VietRide.Identity.UnitTests/Application/Operators/ListOperatorsQueryHandlerTests.cs` — list handler tests.
- `docs/api/postman/README.md` — documents Day-6 local Newman wrapper.
- `docs/api/postman/vietride.local.postman_environment.json` — removes committed token placeholder value and adds local harness flags.
- `docs/api/postman/vietride.postman_collection.json` — adds local-harness guarded steps and profile GET success/adversarial coverage.
- `scripts/run-day6-newman-local.js` — local-only Day-6 Newman helper/wrapper.
- `docs/handoff/day-6-checklist.md` — this post-fix checklist.

## Verification run

| Command / check | Result | Notes |
|---|---:|---|
| `dotnet build "apps/identity/VietRide.Identity.sln" -c Release` | ✅ PASS | `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| `dotnet format "apps/identity/VietRide.Identity.sln" --verify-no-changes` | ✅ PASS | Exit 0; no output. |
| `dotnet test "apps/identity/VietRide.Identity.sln" -c Release --no-build --logger "trx;LogFileName=identity-sln-day6-fix.trx" --results-directory "C:\Users\user\AppData\Local\Temp\opencode"` | ✅ PASS | Unit `198/198`; integration `121/121`; NetArchTest/layering included in Identity test projects. |
| `dotnet test "apps/identity/VietRide.Identity.sln" --filter "FullyQualifiedName~OperatorRepositoryPersistenceTests"` | ✅ PASS | Targeted JSONB regression tests passed `2/2` during fix review. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | TS build succeeded for 10 projects; Nx cache used for all 10 tasks. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | TS lint succeeded for 14 projects; Nx cache used for all 14 tasks. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | TS test target succeeded for 10 projects; projects with tests reported 81 tests total (`notification` 2, `tracking` 2, `rag` 2, `gateway` 54, `contracts` 21); no-test libs exited 0 via `--passWithNoTests`. |
| `dotnet ef database update` → rollback to `20260605160617_AddActivityLogActions` → re-apply | ✅ PASS | Passed during audit for `20260606191136_AddOperatorSubscriptionBaseline`; remediation did not add/change migrations. |
| `docker compose --env-file .env -f "infra/docker/docker-compose.yml" --profile app up -d --build identity gateway` | ✅ PASS | Identity/Gateway rebuilt with code and Postman harness fixes; compose warned unset bootstrap/Google env vars, but containers became healthy. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | App + infra containers were `Up`/healthy after rebuild. |
| `/health` matrix via `Invoke-WebRequest` for ports 3000, 5001, 5002, 5003, 5004, 5005, 3001, 3002, 3003 | ✅ PASS | All returned HTTP `200`: gateway, identity, trip, booking, payment, parcel, tracking, notification, rag. |
| `node -e "const fs=require('fs'); JSON.parse(fs.readFileSync('docs/api/postman/vietride.postman_collection.json','utf8')); JSON.parse(fs.readFileSync('docs/api/postman/vietride.local.postman_environment.json','utf8')); console.log('POSTMAN_JSON_OK')"` | ✅ PASS | Collection and env JSON parse after changes. |
| `node --check "scripts/run-day6-newman-local.js"` | ✅ PASS | Local Newman helper syntax valid. |
| `node "scripts/run-day6-newman-local.js"` | ✅ PASS | Day-6 Newman via Docker/Gateway passed: 19 requests, 35 assertions, 0 failures. Statuses included register `201`, verify `200`, approve `200`, logins `200`, profile update/read `200`, admin-create `201`, operator-user create `201`, staff read `200`, staff PATCH `403`. |
| DB side-effect check for latest Newman run (`BRN-7991856877-SELF`, `BRN-7991856877-ADMIN`) | ✅ PASS | Self operator `APPROVED`/subscription `ACTIVE`/`current_operator_users=1`; admin-created operator `APPROVED`/subscription `ACTIVE`/`current_operator_users=2`. |
| DB user side-effect check for latest Newman run | ✅ PASS | Self OPERATOR_ADMIN, admin-created OPERATOR_ADMIN, and OPERATOR_STAFF all `ACTIVE`; OPERATOR_STAFF linked to admin-created operator. |
| Outbox check for `identity.operator.approved` / `identity.operator.suspended` | ✅ PASS | `0` rows, matching Day-6 defer-to-Day-10 decision. |
| `git diff --check` | ✅ PASS | No whitespace errors. |
| `node .githooks/pre-commit-check.mjs; if ($LASTEXITCODE -eq 0) { "HOOK_INVARIANTS_PASS" } ...` | ✅ PASS | Hook invariant script printed `HOOK_INVARIANTS_PASS`; covers CPM/no banned deps/MediatR v12+ guard scope. |
| `git ls-files --eol` policy check script | ✅ PASS | Printed `EOL_EXPECTED_OK` for tracked files. New `.js/.json/.md` files were verified as LF by reviewer. |
| `dotnet-reviewer` review of JSONB fix | ✅ PASS | APPROVE; no blockers/should-fix/nits. |
| `reviewer` re-review of Postman/helper fixes | ✅ PASS | APPROVE; previous local-harness SHOULD-FIX resolved. |

## Contract / event / schema changes shipped

- **FE-facing endpoints shipped/contracted**:
  - `POST /v1/operators/register`
  - `GET /v1/admin/operators`
  - `POST /v1/admin/operators`
  - `POST /v1/admin/operators/{operatorId}/approve`
  - `POST /v1/admin/operators/{operatorId}/reject`
  - `POST /v1/admin/operators/{operatorId}/suspend`
  - `POST /v1/operator/users`
  - `POST /v1/operator/users/{userId}/resend-initial-password`
  - `GET /v1/operator/profile`
  - `PATCH /v1/operator/profile`
- **Internal endpoints shipped/contracted**:
  - `GET /internal/v1/operators/{operatorId}`
  - `GET /internal/v1/operators/{operatorId}/subscription`
  - `POST /internal/v1/operators/{operatorId}/usage/increment`
- **Gateway route shipped**: `/v1/operator/profile` → Identity, user auth, roles `OPERATOR_ADMIN|OPERATOR_STAFF`; `/v1/operator/users` remains OPERATOR_ADMIN-only.
- **Schema migration shipped**: `20260606191136_AddOperatorSubscriptionBaseline` adds full operator/subscription baseline and Starter Free-Trial seed. Post-fix JSONB converter did not change schema or require a migration.
- **Postman/harness shipped**: cumulative collection includes local-harness guarded Day-6 token lookup steps and profile GET coverage; `scripts/run-day6-newman-local.js` is the local audit runner for Day-6.
- **Error codes**: no new error code invented; Day-6 uses existing BSOT registry codes (`OPERATOR_DUPLICATE_REGISTRATION`, `OPERATOR_DUPLICATE_TAX_CODE`, `AUTH_EMAIL_ALREADY_REGISTERED`, `AUTH_PHONE_ALREADY_REGISTERED`, `SUBSCRIPTION_LIMIT_EXCEEDED`, `VALIDATION_ERROR`, `FORBIDDEN`, etc.).
- **Events**: `identity.operator.approved` and `identity.operator.suspended` are registered in BSOT, but Day 6 intentionally does not emit them; Day 10 owns Outbox wiring.
- **BSOT cross-check**: Day-6 registry/changelog is present (`BACKEND_SOURCE_OF_TRUTH.md:2678`). No missing new event/error/convention registry entry was found.

## Known gaps & carry-over for Day 7

- SUSPENDED → APPROVED reactivation remains deferred.
- Paid subscription plan pick / `PENDING_PAYMENT` onboarding remains deferred to Sprint 5 / Day 37.
- Day 10 must wire Outbox publication for `identity.operator.approved` / `identity.operator.suspended`.
- The Day-6 local Newman helper is intentionally local-only. Normal full collection runs skip helper requests unless `localHarnessEnabled=true`; for Day-6 audit use `node scripts/run-day6-newman-local.js`.

## Notes for Day 7 planning

- Day 6 runtime operator onboarding is now verified through the real Gateway/Docker stack.
- Day 7 Trip service may rely on Identity internal operator/subscription lookup and usage increment; Identity integration tests are green after the JSONB runtime fix.
- If a future audit starts from a clean DB with no SYSTEM_ADMIN, the Day-6 helper can create a local ACTIVE SYSTEM_ADMIN row only for token/FK purposes; this is not a production bootstrap path.
