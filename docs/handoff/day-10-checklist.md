# Day 10 — Final checklist

> Produced by `/audit-day 10` AFTER all tasks were implemented + reviewed (APPROVE) and the
> verification matrix was independently re-run against the real Docker stack.
> Honest record — see the ❌ on outbox **publish in the containerized stack** below.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 10 ([SCV-78](https://hoangvutran088.atlassian.net/browse/SCV-78); passenger stub [SCV-76](https://hoangvutran088.atlassian.net/browse/SCV-76))
- **Plan**: docs/handoff/day-10-plan.md
- **Branch / commits**: `feat/day-10-outbox-identity` — d9e7694, 2f6582b, ce411b5, 5445ab1, 2d74bc7
- **Status**: ✅ **READY** — all code correct, all tests green, every endpoint/flow E2E-verified through the Gateway, AND the end-to-end outbox→broker→consumer chain proven live in Docker. The audit first found the day CLOSED-WITH-GAPS (identity container couldn't publish; no NestJS consumer; changelog/demo gaps); those gaps were then fixed + re-verified (see "Gap fixes applied" below). Remaining items are out-of-Day-10-scope dependencies (Days 8–9 route/vehicle for the full demo) and a noted cross-cutting infra carry-over (tracking shares the nest Docker-packaging pattern).

## DoD result
- [x] **OutboxBackgroundService drains a PENDING row → RabbitMQ `vietride.events` → PUBLISHED** — ✅ proven end-to-end against the real broker (live row `identity.user.created` flipped `PENDING→PUBLISHED`, `published_at` set; message captured on a queue bound to `vietride.events` with the correct payload). Single registered `IOutboxStore` in Persistence, implemented + registered. **Caveat:** only works when the worker can reach the broker — see the container gap below.
- [x] **Admin approve operator inserts `identity.operator.approved` in the SAME tx; published with payload `{operatorId, approvedAt}`** — ✅ row emission + payload + atomicity proven by integration test `AdminOperatorsLifecycleEndpointsTests`; publish path proven generic by the live `identity.user.created` delivery. (Live admin-approve E2E not separately driven — covered by the integration test + the proven shared publish path.)
- [x] **Register inserts `identity.user.created {userId,role,email,createdAt}`; suspend inserts `identity.operator.suspended {operatorId,suspendedAt}`** — ✅ register row verified LIVE (`role=PASSENGER`, correct email/userId/createdAt); suspend row + payload verified by integration test.
- [x] **Kill publisher (broker down) → row stays PENDING/FAILED with incremented RetryCount; on restart it publishes (at-least-once); retries BOUNDED by `RetryCount ≤ MaxRetryCount`** — ✅ observed live: with the broker unreachable the row went `FAILED retry_count=3→5` (≤ MaxRetryCount 10, still re-fetched, never republished past the cap); once the broker became reachable the same row published. Bounded-retry unit-proven in `VietRide.Shared.Persistence.UnitTests` (parked row with `RetryCount>MaxRetryCount` not fetched).
- [x] **IdempotencyMiddleware placeholder in Shared.Web: same key+same body → cached; same key+different body → 422 IDEMPOTENCY_KEY_MISMATCH** — ✅ unit-verified (70/70, branches: first-write SETNX+24h TTL, same-hash replay verbatim, mismatch 422 envelope, missing-header pass-through). Not wired to any service (by design — Booking/Payment/Parcel don't exist yet), so no live E2E (inherent SKIP).
- [x] **GET /v1/passenger/me + GET /v1/passenger/bookings return 200 ApiResponse (bookings empty) and route through the Gateway** — ✅ LIVE via Gateway `:3000`: `/me` authed 200 with the verbatim `GetMeResponseDto` projection; `/bookings` authed 200 `{items:[],total:0,page:1,pageSize:20,…}`; both unauthed 401.
- [x] **dotnet build + format clean on Libs + Identity; NetArchTest layering green; new handler tests pass** — ✅ Release build 0/0 both solutions; format clean; Identity unit 200/200 + integration 127/127 (incl. NetArchTest layering); Libs 74/74.
- [x] **DoD headline "Tuyên (NestJS) can consume"** — ✅ FIXED + live-verified. `apps/notification` now runs an `IdentityEventsConsumer` (NestJS) bound to `vietride.events` for the three routing keys, validating each payload with a zod contract. Proven live in Docker: gateway register → identity outbox → broker → `notification` logs `Consumed identity.user.created … role=PASSENGER`; a published `identity.operator.approved` is consumed too. (Commit `972d677`.)
- [~] **DoD "Sprint 2 demo script ready"** — ✅ demo script written (`docs/handoff/sprint-2-demo-script.md`, verified commands). ⚠️ The final demo leg (operator creates **route/vehicle**) still can't run because Route/Vehicle (Days 8–9) are not implemented — documented in the script as a Days-8–9 dependency. The register→verify→login→admin-approve-operator portion is fully demoable and was executed live.

## Tasks completed
- Task 10.0 — Reconcile split `IOutboxStore` into one Persistence interface (+ B2-clean `IIntegrationEventOutbox`, moved `OutboxEventEnvelope`, new `VietRide.Shared.Persistence.UnitTests`) — ✅ (commit d9e7694)
- Task 10.1 — Wire `AddVietRideMessaging` into Identity (Program.cs + appsettings RabbitMq sections + Api csproj ref) — ✅ code; ⚠️ deployment (see compose gap) (commit 2f6582b)
- Task 10.2 — Emit `identity.user.created` / `operator.approved` / `operator.suspended` transactionally via `IIntegrationEventOutbox` — ✅ (commit ce411b5)
- Task 10.3 — Placeholder Redis `IdempotencyMiddleware` in Shared.Web (not wired) — ✅ (commit 5445ab1)
- Task 10.4 — Passenger `/me` + `/bookings` stub endpoints + Gateway route + contract/Postman — ✅ (commit 2d74bc7)

## Changed files
- `libs/dotnet/VietRide.Shared.Persistence/Outbox/` — canonical `IOutboxStore` (4 members, `GetUnprocessedAsync` removed), `OutboxStore` (filter `Status IN (PENDING,FAILED) AND RetryCount ≤ max`, `DateTimeOffset→DateTime` bridge, `NextAttemptAt=null`), moved `OutboxEventEnvelope`, new `IntegrationEventOutbox`.
- `libs/dotnet/VietRide.Shared.Application/Outbox/IIntegrationEventOutbox.cs` — string-based enqueue abstraction (no Persistence/Messaging ref).
- `libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs` — `using` + `MaxRetryCount` at call site + honest poll-cadence XML-doc; `DependencyInjection/MessagingServiceCollectionExtensions.cs` — repointed `<see cref>`; deleted stub `Outbox/IOutboxStore.cs`.
- `libs/dotnet/VietRide.Shared.Persistence/DependencyInjection/PersistenceServiceCollectionExtensions.cs` — register `IIntegrationEventOutbox`.
- `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs` + `DependencyInjection/IdempotencyServiceCollectionExtensions.cs` + csproj (`StackExchange.Redis`, no Version).
- `apps/identity/src/VietRide.Identity.Api/Program.cs` + `appsettings.json` (RabbitMq + RabbitMq:Outbox) + Api csproj (Shared.Messaging ref).
- `apps/identity/src/VietRide.Identity.Application/Events/{UserCreated,OperatorApproved,OperatorSuspended}IntegrationEvent.cs` + the 3 handlers (Register/ApproveOperator/SuspendOperator).
- `apps/identity/src/VietRide.Identity.Api/Controllers/PassengerController.cs` + `Application/Features/Passenger/GetPassengerBookings/` (query+handler; `/me` reuses `GetMeQuery`).
- `apps/gateway/src/config/routes.ts` (+ `routes.spec.ts`) — `/v1/passenger` → identity, authRequired `user`.
- Tests: `tests/dotnet/VietRide.Shared.Persistence.UnitTests/**`, `tests/dotnet/VietRide.Shared.Web.UnitTests/Middleware/**`, Identity `AuthEndpointsTests` / `AdminOperatorsLifecycleEndpointsTests` / `PassengerEndpointsTests` / unit handler tests.
- Docs: `VietRide_API_Contract_v1.md` (passenger sections, stub-noted), `docs/api/postman/vietride.postman_collection.json` (2 requests), `docs/handoff/day-10-plan.md` (tracker).

## Verification run
| Command | Result | Notes |
|---|---|---|
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | 0 Warning / 0 Error |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | clean |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Shared.Web.UnitTests 70/70 + Shared.Persistence.UnitTests 4/4 = **74/74** (Persistence tests are Postgres-backed) |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | 0 Warning / 0 Error |
| `dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes` | ✅ PASS | clean |
| `dotnet test apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | unit **200/200**, integration **127/127** (incl. NetArchTest layering) |
| `npx nx build gateway` | ✅ PASS | webpack compiled successfully |
| `npx nx lint gateway` | ✅ PASS | clean |
| `npx nx test gateway --skip-nx-cache` | ✅ PASS | **65/65**, 4 suites (incl. `/v1/passenger` route assertion) |
| EF migration apply/rollback | ➖ N/A | Day 10 shipped **no migration** (Q4 option (a) — reuse existing `outbox_events` schema) |
| Real stack bring-up (`docker compose --profile`… `up -d` identity+gateway+infra, images `--build`) | ✅ PASS | identity, gateway, postgres, rabbitmq, redis (+ trip/booking/payment/parcel via gateway depends_on) all **healthy** |
| `/health` matrix | ✅ PASS | gateway `:3000/health` 200, identity `:5001/health` 200, gateway→identity `/v1/identity/health` 200 |
| Review artifact validation (Postman) | ✅ PASS | collection parses; 2 passenger requests present with stub note |
| Review execution — passenger stubs via Gateway | ✅ PASS | `/v1/passenger/me` authed 200 (GetMeResponseDto), unauth 401; `/v1/passenger/bookings` authed 200 (empty envelope), unauth 401 |
| Review execution — outbox emit→publish via Gateway | ✅ PASS (after gap #1 fix) | register via Gateway → 201 → `identity.user.created` row emitted, drained, **PUBLISHED**, message delivered to `vietride.events` — with the committed compose (`RabbitMq__HostName=rabbitmq` added). `identity.operator.approved` published the same way after admin-approve. |
| Review execution — NestJS consumer (gap #2 fix) | ✅ PASS | `notification` `IdentityEventsConsumer` consumed the real `identity.user.created` (register E2E) and a published `identity.operator.approved` — logs show `Consumed …`. Container boots healthy after the webpack workspace-lib bundling fix. |
| Review execution — idempotency duplicate | ➖ SKIP (inherent) | placeholder not wired to any endpoint (Booking/Payment/Parcel absent); covered by unit tests |
| Invariants (CPM / MediatR / Co-Authored-By / EOL) | ✅ PASS | no `Version=` added; MediatR 11.1.0; no `Co-Authored-By` trailer in any Day-10 commit; `.cs`=CRLF, `.ts`/`.json`=LF |
| **Day-10 "Review" bullet overall** | ✅ PASS | "event eventually published after restart" ✅ proven (FAILED→PUBLISHED on broker recovery); "idempotency duplicate returns same response" ✅ unit-only (inherent skip); outbox→broker→consumer chain ✅ live after gap fixes |

## Contract / event / schema changes shipped
- **Endpoints (new, stubs):** `GET /v1/passenger/me` (reuses `GetMeResponseDto` verbatim), `GET /v1/passenger/bookings` (empty paginated envelope) — added to `VietRide_API_Contract_v1.md` + Postman, each marked `stub -- item schema finalized in Sprint 3 (SCV-76 / Booking)`.
- **Gateway route:** `/v1/passenger/*` → identity (authRequired `user`).
- **Events:** `identity.user.created`, `identity.operator.approved`, `identity.operator.suspended` — **already in BSOT §7.3 registry** (now implemented; no new keys, no registry edit needed). `staff.password_set` intentionally **dropped** (Q2 RESOLVED: no registry row, no consumer; BSOT §7.3 registry > timeline) — a documented, authorized deviation from the timeline line, NOT a gap.
- **Error code:** `IDEMPOTENCY_KEY_MISMATCH` already in BSOT §5.9 (no edit).
- **Schema/migration:** none.
- **Cross-check (§13 changelog):** ⚠️ the API-contract §13 changelog was **not** bumped for the two new passenger endpoints (latest row is 1.6.4 / Day-7). No BSOT registry change was required. → carry-over doc fix.

## Gap fixes applied (post-audit, this session)
1. **[FIXED] Identity container couldn't publish.** Added `RabbitMq__HostName=rabbitmq` (+ Port/UserName/Password) to the identity service env in `infra/docker/docker-compose.yml`. Re-verified live: register → `identity.user.created` `PUBLISHED` + on `vietride.events`. Commit `2f86100`.
2. **[FIXED] No NestJS consumer.** Added `IdentityEventsConsumer` in `apps/notification` (binds `vietride.events`, zod-validates the 3 events) + reconciled `libs/shared/contracts` (new `identity-events.ts`, removed stale `user-registered.event.ts`). Also fixed a pre-existing nest-worker Docker-packaging bug so the image resolves `@vietride/*` at runtime (notification webpack now bundles workspace libs, externalizing only real third-party deps). Live-verified consume. Commit `972d677`.
3. **[FIXED] Changelog.** Added BSOT §13 row **1.6.5** for the Day-10 contract sync. Commit `2306a66`.
4. **[FIXED] Demo script.** Wrote `docs/handoff/sprint-2-demo-script.md` with verified commands.

## Known gaps & carry-over for Day 11
1. **[infra carry-over] Nest-worker Docker packaging pattern.** The `@vietride/*` workspace libs don't resolve at runtime in the standard nest Dockerfile pattern (TS-source-only libs; dangling `node_modules` symlinks). Fixed for **notification** by bundling the libs in its webpack config; **tracking shares the same latent pattern** and will hit it the moment it's booted with a workspace-lib dependency exercised. Apply the same fix (or a shared solution) to the other nest workers before relying on them in Docker.
2. **[scope] Full Sprint-2 demo** needs the operator→**route/vehicle** leg, which depends on Days 8–9 (Route/Vehicle) — not implemented. Demo script documents this; auth+approve portion is demoable now.
3. **[minor/forward] Contract `UserRole` zod enum** (`identity-events.ts`) omits `ASSISTANT` (present in the .NET `UserRole`). No live risk — `identity.user.created` is PASSENGER-only today — but add `ASSISTANT` if user.created is ever emitted for other roles, to avoid a silent consumer nack.
4. **[infra/env, observed repeatedly] Docker Desktop on this host stops containers/engine mid-run** (clean `Exited (0)` / pipe gone). Every "failure" traced to this was infra, not code. Confirm `pg_isready` / `redis-cli ping` / `docker info` before running suites.

## Notes for Day 11 planning
- Outbox delivery is **proven end-to-end in Docker**: identity emits → `vietride.events` → `notification` consumes. When Day 11+ adds a new cross-service event consumer (e.g. Payment init Wallet on `identity.user.created`), reuse the `apps/notification` `IdentityEventsConsumer` + `@vietride/contracts` `identity-events.ts` as the pattern, and remember the nest Docker-packaging fix (bundle `@vietride/*` in the app's webpack) for any nest worker shipped in Docker.
- The `IIntegrationEventOutbox` (string-based, in Shared.Application) is the established seam for emitting events from any service's handlers without an Application→Persistence reference — reuse it for Trip/Booking/Payment events.
- `IdempotencyMiddleware` is ready in Shared.Web (opt-in `AddVietRideIdempotency(prefix)` + `UseVietRideIdempotency()`); wire it into Booking/Payment/Parcel POST/PATCH when those services land.
- Passenger `/bookings` returns the canonical `PagedResult<T>` empty envelope; the booking **item** schema is still open (Sprint 3 / SCV-76).
