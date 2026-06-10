# Day 10 — Final checklist

> Produced by `/audit-day 10` — **independent re-verification** (not trusting the plan's
> Progress tracker or the prior self-report). Every tier below was re-run today (2026-06-10)
> against the real Docker stack; code was re-read against the source-of-truth. Honest record.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 10 ([SCV-78](https://hoangvutran088.atlassian.net/browse/SCV-78); passenger stub [SCV-76](https://hoangvutran088.atlassian.net/browse/SCV-76))
- **Plan**: docs/handoff/day-10-plan.md
- **Branch / commits**: `feat/day-10-outbox-identity` — d9e7694, 2f6582b, ce411b5, 5445ab1, 2d74bc7, 2f86100, 2306a66, 972d677, cadf9ae
- **Status**: ✅ **READY** — all code matches the SOT, all tiers green, and the full
  outbox→broker→consumer chain + the Day-10 Review adversarial case proven LIVE in Docker
  (register → `identity.user.created` PENDING→PUBLISHED → consumed; broker-down →
  FAILED/bounded-retry → broker-up → republished + drained, no loss). Two non-blocking
  carry-overs recorded (tracking image build broken; nest consumer needs a restart to
  re-drain after a broker bounce — at-least-once still preserved).

## DoD result
- [x] **OutboxBackgroundService drains a PENDING row → RabbitMQ `vietride.events` → PUBLISHED** — ✅ proven LIVE: register via Gateway → `vietride_identity.outbox_events` row `identity.user.created` flipped `PENDING→PUBLISHED`, `published_at` set, `retry_count=0`. Single canonical `IOutboxStore` (4 members) in `VietRide.Shared.Persistence.Outbox`, implemented by `OutboxStore`, registered scoped (PersistenceServiceCollectionExtensions). Messaging stub interface deleted; `OutboxEventEnvelope` moved into Persistence; `OutboxBackgroundService` resolves the Persistence interface via the existing Messaging→Persistence ref.
- [x] **Admin approve operator inserts `identity.operator.approved` in the SAME tx; published `{operatorId, approvedAt}`** — ✅ emission + payload + atomicity proven by integration test `AdminOperatorsLifecycleEndpointsTests` (127/127); the live publish path is proven generic by the `identity.user.created` LIVE delivery (identical `IIntegrationEventOutbox`→outbox→worker→exchange path). Event payload record + routing key verified against BSOT §7.3 by code read. (Live admin-approve leg not independently driven — covered by composition.)
- [x] **Register inserts `identity.user.created {userId,role,email,createdAt}`; suspend inserts `identity.operator.suspended {operatorId,suspendedAt}`** — ✅ register row verified LIVE: `{role:PASSENGER, email, userId, createdAt}` — exact §7.3 shape, camelCase. Suspend row + payload verified by integration/unit tests; `OperatorSuspendedIntegrationEvent` payload verified by code read.
- [x] **Broker down → row stays PENDING/FAILED, RetryCount increments; on restart it publishes (at-least-once); retries BOUNDED by RetryCount ≤ MaxRetryCount** — ✅ proven LIVE: stopped `vietride_rabbitmq`, registered → row went `FAILED retry_count=1` with `last_error="None of the specified endpoints were reachable"` (connection-timeout path through `RabbitMqEventPublisher.EnsureChannel`); restarted broker → same row `PUBLISHED` (retry_count preserved at 1). Bounded-retry unit-proven in `VietRide.Shared.Persistence.UnitTests` (`FetchPending_FailedRowExceedingMaxRetry_IsParkedAndNotReturned`).
- [x] **IdempotencyMiddleware placeholder in Shared.Web: same key+same body → cached; same key+different body → 422 IDEMPOTENCY_KEY_MISMATCH** — ✅ unit-verified (70/70 incl. first-write SETNX `When.NotExists` + 24h TTL, same-hash verbatim replay, mismatch 422 ApiResponse envelope, missing-header pass-through, POST/PATCH-only). Redis key `{prefix}:idem:{key}`. NOT wired to any service (by design — Booking/Payment/Parcel absent) → no live E2E (inherent SKIP).
- [x] **GET /v1/passenger/me + GET /v1/passenger/bookings return 200 ApiResponse (bookings empty) via Gateway** — ✅ LIVE via Gateway `:3000`: `/me` authed **200** with verbatim `GetMeResponseDto` (`id,email,displayName,phone,role,operatorId,status,avatarUrl`); `/bookings` authed **200** `{items:[],total:0,page:1,pageSize:20,…}`; both unauthed **401**. Gateway mints Internal JWT (identity accepted the proxied call).
- [x] **dotnet build + format clean on Libs + Identity; NetArchTest layering green; new handler tests pass** — ✅ Release build 0W/0E both solutions; format `--verify-no-changes` clean both; Libs test 74/74; Identity unit 200/200 + integration 127/127 (incl. NetArchTest layering).
- [x] **DoD headline "Tuyên (NestJS) can consume"** — ✅ LIVE: `apps/notification` `IdentityEventsConsumer` (3 durable queues bound to `vietride.events`, zod-validated) logged `Consumed identity.user.created userId=… role=PASSENGER` for the register E2E. 4 distinct user.created events consumed across the session (incl. the broker-recovery one).
- [~] **Sprint 2 demo script ready** — ✅ `docs/handoff/sprint-2-demo-script.md` present (commit cadf9ae); register→verify→login→admin-approve portion demoable. ⚠️ Final leg (operator creates route/vehicle) blocked by Days 8–9 (Route/Vehicle) not implemented — documented in the script as a dependency.

## Tasks completed (independently re-verified against SOT)
- Task 10.0 — Reconcile split `IOutboxStore` → one Persistence interface (+ B2-clean `IIntegrationEventOutbox` in Shared.Application string-based; `OutboxEventEnvelope` moved; `GetUnprocessedAsync` removed; `FetchPendingAsync(batchSize,maxRetryCount,ct)` bounded filter; DateTimeOffset→DateTime bridge; `NextAttemptAt=null`; new `VietRide.Shared.Persistence.UnitTests`) — ✅ code matches acceptance verbatim (d9e7694)
- Task 10.1 — Wire `AddVietRideMessaging` into Identity (Program.cs + appsettings RabbitMq/RabbitMq:Outbox + Api csproj ref) — ✅ container boots, OutboxBackgroundService runs, publishes LIVE (2f6582b)
- Task 10.2 — Emit the 3 events transactionally via `IIntegrationEventOutbox`; Application has NO Shared.Persistence ref (B2-clean) — ✅ verified by code read + LIVE row (ce411b5)
- Task 10.3 — Placeholder Redis `IdempotencyMiddleware` in Shared.Web (not wired) — ✅ 70/70 (5445ab1)
- Task 10.4 — Passenger `/me` + `/bookings` stubs + Gateway `/v1/passenger`→identity + contract/Postman — ✅ LIVE 200/401 (2d74bc7)
- Gap fixes (post-implementation): identity `RabbitMq__HostName=rabbitmq` in compose (2f86100); `IdentityEventsConsumer` + `@vietride/contracts` `identity-events.ts` + notification webpack lib-bundling (972d677); BSOT §13 row 1.6.5 (2306a66); demo script (cadf9ae)

## Changed files
- `libs/dotnet/VietRide.Shared.Persistence/Outbox/` — canonical `IOutboxStore` (4 members), `OutboxStore` (bounded `Status IN (PENDING,FAILED) AND RetryCount ≤ max` filter, `DateTimeOffset→DateTime` via `.UtcDateTime`, `NextAttemptAt=null`), moved `OutboxEventEnvelope`, new `IntegrationEventOutbox`.
- `libs/dotnet/VietRide.Shared.Application/Outbox/IIntegrationEventOutbox.cs` — string-based enqueue abstraction (no Persistence/Messaging ref).
- `libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs` — `using VietRide.Shared.Persistence.Outbox`, `MaxRetryCount` at the `FetchPendingAsync` call site, honest poll-cadence XML-doc; `DependencyInjection/MessagingServiceCollectionExtensions.cs` repointed `<see cref>`; **deleted** stub `Outbox/IOutboxStore.cs`.
- `libs/dotnet/VietRide.Shared.Persistence/DependencyInjection/PersistenceServiceCollectionExtensions.cs` — register `IIntegrationEventOutbox`.
- `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs` + `DependencyInjection/IdempotencyServiceCollectionExtensions.cs` + csproj (`StackExchange.Redis`, no Version).
- `apps/identity/src/VietRide.Identity.Api/Program.cs` + `appsettings.json` (RabbitMq + RabbitMq:Outbox) + Api csproj (Shared.Messaging ref).
- `apps/identity/src/VietRide.Identity.Application/Events/{UserCreated,OperatorApproved,OperatorSuspended}IntegrationEvent.cs` + 3 handlers (Register / ApproveOperator / SuspendOperator).
- `apps/identity/src/VietRide.Identity.Api/Controllers/PassengerController.cs` + `Application/Features/Passenger/GetPassengerBookings/` (`/me` reuses `GetMeQuery`).
- `apps/gateway/src/config/routes.ts` (+ `routes.spec.ts`) — `/v1/passenger` → identity, authRequired `user`.
- `apps/notification/src/identity-events/*` + `app.module.ts` + `webpack.config.js` (workspace-lib bundling); `libs/shared/contracts/src/events/identity-events.ts` (+ spec, index), deleted `user-registered.event.ts`.
- `infra/docker/docker-compose.yml` — identity `RabbitMq__*` env.
- Tests: `tests/dotnet/VietRide.Shared.Persistence.UnitTests/**`, `tests/dotnet/VietRide.Shared.Web.UnitTests/Middleware/**`, Identity `Auth`/`AdminOperatorsLifecycle`/`Passenger` integration + unit handler tests.
- Docs: `VietRide_API_Contract_v1.md` (passenger sections), `BACKEND_SOURCE_OF_TRUTH.md` (§13 row 1.6.5), `docs/api/postman/vietride.postman_collection.json`, `docs/handoff/{day-10-plan.md, sprint-2-demo-script.md}`.

## Verification run (re-run 2026-06-10 by this audit)
| Command | Result | Notes |
|---|---|---|
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | 0 Warning / 0 Error |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | clean (exit 0) |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Shared.Web.UnitTests **70/70** + Shared.Persistence.UnitTests **4/4** = **74/74** (Persistence is Postgres-backed throwaway-DB) |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | 0 Warning / 0 Error |
| `dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes` | ✅ PASS | clean (exit 0) |
| `dotnet test apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | unit **200/200**, integration **127/127** (incl. NetArchTest layering) |
| `nx run gateway:{build,lint,test}` | ✅ PASS | build ok, lint clean, test **65/65** (4 suites, incl. `/v1/passenger` route assertion) |
| `nx run notification:{build,lint,test}` | ✅ PASS | build ok, lint clean, test **7/7** |
| `nx run contracts:lint` | ✅ PASS | clean |
| (combined `nx run-many … gateway,notification,contracts`) | ⚠️ transient | run-many reported build/lint "failed" but **every task passes on individual re-run** (cache/concurrency artifact); tests passed in-line. Recorded as non-blocking. |
| EF migration apply/rollback | ➖ N/A | Day 10 shipped **no migration** (Q4 option (a) — reuse existing `outbox_events`) |
| Full stack `docker compose --profile app up -d --build` | ❌ tracking image | FAILS building **tracking** (`Cannot find module '/workspace/eslint.config.mjs'` during `nx run tracking:build`). Out of Day-10 scope. Worked around: built+started identity, gateway, notification + infra explicitly. |
| Containers healthy | ✅ PASS | identity, gateway, notification, trip, booking, payment, parcel, postgres, pgbouncer, redis, rabbitmq all **healthy** (tracking excluded — broken build) |
| `/health` matrix | ✅ PASS | gateway `:3000`, identity `:5001`, trip `:5002`, booking `:5003`, payment `:5004`, parcel `:5005`, notification `:3002` — all **200** |
| Review artifact validation (Postman) | ✅ PASS | collection present; 2 passenger requests with stub note |
| Review execution — passenger stubs via Gateway | ✅ PASS | `/v1/passenger/me` authed 200 (GetMeResponseDto verbatim), unauth 401; `/v1/passenger/bookings` authed 200 (empty envelope), unauth 401 |
| Review execution — outbox emit→publish→consume via Gateway | ✅ PASS | register 201 → `identity.user.created` row `PENDING→PUBLISHED` on `vietride.events` → `notification` consumed (userId+role); payload exact §7.3 |
| Review execution — broker-down / restart (adversarial) | ✅ PASS | broker down → row `FAILED retry_count=1` (bounded, error captured); broker up → same row `PUBLISHED`; durable queue buffered + drained on consumer restart — **no message loss** |
| Review execution — idempotency duplicate | ➖ SKIP (inherent) | placeholder not wired (Booking/Payment/Parcel absent); covered by unit tests |
| Invariants (CPM / MediatR / Co-Authored-By / EOL) | ✅ PASS | no `Version=` in any changed csproj; MediatR 11.1.0; no `Co-Authored-By` in Day-10 commits; `.cs`=CRLF, `.ts`/`.json`=LF (`git ls-files --eol`) |
| **Day-10 "Review" bullet overall** | ✅ PASS | "event eventually published after restart" ✅ LIVE; "idempotency duplicate returns same response" ✅ unit-only (inherent skip, not wired) |

## Contract / event / schema changes shipped
- **Endpoints (new, stubs):** `GET /v1/passenger/me` (reuses `GetMeResponseDto` verbatim), `GET /v1/passenger/bookings` (empty `PagedResult` envelope) — in `VietRide_API_Contract_v1.md` (§ lines 364/400) + Postman, marked `stub -- item schema finalized in Sprint 3 (SCV-76 / Booking)`.
- **Gateway route:** `/v1/passenger/*` → identity (authRequired `user`).
- **Events:** `identity.user.created`, `identity.operator.approved`, `identity.operator.suspended` — **already in BSOT §7.3 registry** (now implemented; no new keys). `staff.password_set` intentionally **dropped** (Q2: no registry row, no consumer; §7.3 registry > timeline) — authorized deviation, not a gap.
- **Error code:** `IDEMPOTENCY_KEY_MISMATCH` already in BSOT §5.9 (BACKEND_SOURCE_OF_TRUTH.md:1401) — no edit.
- **Schema/migration:** none (Q4 option (a)).
- **§13 changelog cross-check:** ✅ DONE — BSOT §13 row **1.6.5** (2026-06-10) records the Day-10 contract sync (commit 2306a66). No registry/error-code edit needed (events §7.3 + error §5.9 pre-existed).

## Known gaps & carry-over for Day 11
1. **[infra, blocks full `--profile app --build`] tracking image build broken.** `nx run tracking:build` fails resolving `/workspace/eslint.config.mjs` inside the Docker build. Day 10 didn't touch tracking, but it breaks a clean whole-stack `up --build`. Fix the tracking Dockerfile/eslint config (and apply the same `@vietride/*` webpack-bundling pattern used for `notification` if tracking exercises a workspace lib) before tracking is relied on in Docker.
2. **[shared nest-rabbitmq] consumer connection does not auto-recover after a broker restart.** Observed: after `vietride_rabbitmq` bounced, the live `notification` consumer stopped receiving until the **process** was restarted — at which point it re-attached to its **durable** queues and drained the backlog (**at-least-once preserved, no loss**). Add RabbitMQ automatic-connection-recovery (or a reconnect loop) to `@vietride/nest-rabbitmq` `RabbitMqConsumer` so consumption resumes without a restart. Mild — does not affect delivery guarantees.
3. **[scope] Full Sprint-2 demo** needs the operator→route/vehicle leg (Days 8–9 Route/Vehicle, not implemented). Auth + admin-approve portion demoable now.
4. **[minor/forward] Contract `UserRole` zod enum** (`identity-events.ts`) omits `ASSISTANT` (present in .NET `UserRole`). No live risk — `identity.user.created` is PASSENGER-only — but add it before emitting user.created for other roles to avoid a consumer nack.
5. **[coverage] Live admin-approve / suspend not independently driven** — emission+payload+atomicity proven by integration tests; the publish path proven generic by the LIVE `user.created`. If a stronger demo is wanted, drive the operator-register → admin-approve E2E live in Day 11.

## Notes for Day 11 planning
- Outbox delivery is **proven end-to-end in Docker**: identity emits → `vietride.events` → `notification` consumes (durable, at-least-once). Reuse `IIntegrationEventOutbox` (string-based, Shared.Application) as the emit seam for Trip/Booking/Payment events — no Application→Persistence ref needed.
- For any new nest consumer in Docker, reuse the `apps/notification` pattern (`@vietride/contracts` zod schemas + webpack workspace-lib bundling) and address carry-over #2 (auto-recovery) for production reliability.
- `IdempotencyMiddleware` is ready in Shared.Web (`AddVietRideIdempotency(prefix)` + `UseVietRideIdempotency()`); wire into Booking/Payment/Parcel POST/PATCH when those land.
- Passenger `/bookings` returns the canonical `PagedResult<T>` empty envelope; booking **item** schema still open (Sprint 3 / SCV-76).
