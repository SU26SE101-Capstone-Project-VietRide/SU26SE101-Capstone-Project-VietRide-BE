# Day 29 - Final checklist

> Produced by `/audit-day 29` after fixing the blocking Notification fixtures and independently rerunning the complete Day-29 verification matrix. Plan tracker state and earlier task reports were not treated as audit evidence.

- **Timeline ref**: `BE_TIMELINE_VU.md` - Day 29, Sprint 4 integration buffer
- **Plan**: `docs/handoff/day-29-plan.md` (`APPROVED`)
- **Audited scope**: `main@eea9f509..e657d37e` (7 commits, 49 committed Day-29 files), plus the final Notification regression-fixture correction
- **Status**: READY

## DoD result

- [x] The isolated Gateway scenario completed the required three-parcel lifecycle: three `PENDING -> LOADED` mutations, assigned-driver start, all three parcels moving to `IN_TRANSIT`, selected-stop arrival, exactly one unload, and assigned-assistant completion. The fresh run passed 277 assertions and verified fixture cleanup.
- [x] `parcel.parcel.loaded`, `trip.trip.started`, `parcel.parcel.unloaded`, and `trip.cargo.threshold_crossed` reached their intended runtime consumers. The run correlated producer Outbox ids, RabbitMQ `MessageId`, Notification rows, recipients, and canonical dedupe keys without duplicate writes.
- [x] Notification registers every Sprint 4 notification-facing routing key: `trip.trip.boarding_started`, `trip.cargo.threshold_crossed`, `parcel.parcel.created`, `parcel.parcel.loaded`, `parcel.parcel.unloaded`, `parcel.parcel.review_requested`, and `parcel.parcel.auto_rejected`. The focused registration E2E passed 1/1.
- [x] The public assistant-load seam matches the frozen contract: UUID-v4 idempotency key, strict `{ tripId, parcelCode }`, JWT-derived assistant/operator identity, crew and tenant enforcement, ADR-0004 envelope, and canonical `403/404/409/422` behavior. No public/manual Trip-create endpoint was added.
- [x] Retry and convergence behavior is covered by stable cargo mutation identity and behavior-idempotent Trip loading. Parcel, Trip, shared .NET, and Notification suites all passed; same-key replays created no duplicate state, cargo, Outbox, or Notification writes.
- [x] Day-29 Review passed: all seven Sprint 4 Notification routing keys are registered, the live Trip/Parcel -> RabbitMQ -> Notification path passed, and the final full NestJS regression is green.

## Tasks completed

- Task 29.1 - Freeze Sprint 4 HTTP/event contracts - PASS.
- Task 29.2 - Expose assigned-assistant parcel load - PASS.
- Task 29.3 - Migrate Trip producer to `trip.cargo.threshold_crossed` - PASS.
- Task 29.4 - Canonicalize Parcel auto-rejected payloads - PASS.
- Task 29.5 - Wire and harden Notification consumers - PASS. The stale `parcel.parcel.loaded` test fixtures now use the strict Day-29 payload and canonical payload `eventId` dedupe identity.
- Task 29.6 - Prove the three-parcel lifecycle - PASS (277 fresh assertions plus scoped cleanup).

## Changed-file summary

- `BACKEND_SOURCE_OF_TRUTH.md`, `VietRide_API_Contract_v1.md` - assistant-load contract, event registry, and BSOT `1.39.0` changelog.
- `libs/shared/contracts/src/events/**` - strict Trip cargo-threshold and Parcel Sprint 4 event contracts.
- `apps/gateway/src/config/{routes.ts,routes.spec.ts}` - longest-prefix `/v1/assistant/parcels` routing to Parcel.
- `apps/trip/src/**`, `apps/trip/tests/**/Day29*` - atomic cargo/Outbox mutation, canonical threshold event, and producer/repository/transaction evidence.
- `apps/parcel/src/**`, `apps/parcel/tests/**/Day29*` - public assistant-load endpoint, loaded/auto-rejected events, and retry/race/transaction coverage.
- `tests/dotnet/VietRide.Shared.{Messaging,Persistence}.UnitTests/**/Day29*` - Outbox/publisher restart identity evidence.
- `apps/notification/src/notifications/**` - Sprint 4 bindings, strict schemas, recipient resolution, mapping, canonical dedupe, poison handling, retry handling, and corrected loaded-event fixtures.
- `scripts/run-day29-sprint4-e2e.mjs`, its assertion test, package scripts, and `docs/handoff/day-29-sprint4-evidence.md` - repeatable runtime proof.

## Verification run

| Command / gate | Result | Evidence |
|---|---|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | PASS | No changes required. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release --no-build -- RunConfiguration.MaxCpuCount=1` | PASS | Unit 528/528; integration 202/202; 0 skipped. |
| `dotnet build apps/parcel/VietRide.Parcel.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes` | PASS | No changes required. |
| `dotnet test apps/parcel/VietRide.Parcel.sln -c Release --no-build -- RunConfiguration.MaxCpuCount=1` | PASS | Unit 191/191; integration 42/42; 0 skipped. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | PASS | No changes required. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-build -- RunConfiguration.MaxCpuCount=1` | PASS | Messaging 23/23; Persistence 27/27; Web 90/90. |
| EF/Prisma migration roundtrip | SKIP | Day 29 changed and shipped no migration; migration history is unchanged. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | All 10 TS projects and 3 dependent tasks succeeded. Existing generated/source-map warnings are non-fatal. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | All 14 applicable projects; 0 errors and 13 warnings. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | All 10 TS projects and 3 dependent tasks succeeded; Notification 29/29 suites and 162/162 tests. |
| `npx nx test notification --runInBand --skip-nx-cache` | PASS | Targeted uncached verification: 29/29 suites, 162/162 tests. |
| Day-29 runner assertion test, Node syntax checks, and Prettier checks | PASS | TAP 1/1; both runner files parse; all scoped artifacts match Prettier. |
| Focused Day-29 Notification consumer E2E | PASS | 1/1 test; all seven routing keys asserted individually. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d` | PASS | Existing audited app images reused because the remediation changed tests only; all app and infra containers are healthy. |
| Gateway and direct health matrix | PASS | 14/14 endpoints returned HTTP 200. |
| `node scripts/run-day29-sprint4-e2e.mjs` | PASS | 277 assertions covering lifecycle, authorization, validation/status conflicts, replay behavior, DB state, Outbox, RabbitMQ identity, Notification recipients/dedupe, and cleanup. |
| `node scripts/run-day21-trip-lifecycle-local.mjs` | PASS | Earlier affected Trip lifecycle regression passed end-to-end, including duplicate-event no-op and cleanup. |
| Day-29 Review bullet overall | PASS | Live event flow, all-key registration, and full Notification regression are green. |
| EOL, CPM, dependency, MediatR, trailer, and `git diff --check` gates | PASS | 50 scoped files follow EOL policy; no `.csproj` versions, banned dependencies, MediatR v12+, `Co-Authored-By`, or whitespace/EOF errors. |

## Contract, event, and schema result

- Endpoint: `POST /v1/assistant/parcels/{parcelId}/load`, authenticated `ASSISTANT`, UUID-v4 `Idempotency-Key`, strict `{ tripId, parcelCode }`, response `{ parcelId, parcelCode, status: "LOADED" }`.
- Routing key: `trip.cargo.threshold_crossed` replaces legacy `trip.cargo_near_full`. Payload: `{ eventId, occurredAt, tripId, operatorId, loadedWeightKg, maxCargoWeightKg, percentFull }`.
- Reconciled events: `parcel.parcel.loaded { eventId, occurredAt, parcelId, tripId, actualWeightKg, userIds[] }` and `parcel.parcel.auto_rejected { eventId, occurredAt, parcelId, parcelCode, operatorId, userId, tripId, refundAmount }`.
- Identity invariant: `eventId == OutboxEvent.id == RabbitMQ MessageId` was confirmed at runtime.
- Errors exercised: `FORBIDDEN`, `PARCEL_NOT_FOUND`, `INVALID_STATUS`, idempotency validation/mismatch behavior, and stop-arrival validation.
- Schema/migration: none.
- BSOT registry and changelog: updated in version `1.39.0`; no unregistered Day-29 error, event, or convention was found.

## Gaps and handoff

- No blocking Day-29 gap remains.
- The final test-only remediation and this checklist are currently uncommitted and should be included in the Day-29 closeout commit.
- The unrelated pre-existing deletion of `BE-GAPS (1).md` was preserved and excluded from this audit.

## Final decision

Day 29 is **READY** for closeout/push. The prior blocker was fully resolved and every required build, format, test, runtime, health, event-flow, regression, and repository-invariant gate passed.
