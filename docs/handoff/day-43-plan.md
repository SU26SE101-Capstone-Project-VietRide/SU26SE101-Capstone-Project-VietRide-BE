# Day 43 — Reliability hardening: Outbox + Idempotency review

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 43 (Jira: SCV-131)
- **Prior checklist**: `docs/handoff/day-42-checklist.md` (`not found`)
- **Plan status**: APPROVED — các quyết định Day 43 đã khóa cho mục tiêu Day 41–43; Task 43.0 đồng bộ SOT trước implementation

## Objective
Day 43 hardens delivery and retry safety for the transactional Outbox, idempotency enforcement, and scheduled-job operations. It introduces a durable, reviewable terminal path for exhausted Outbox publishes, verifies every in-scope mutation in Booking, Payment, and Parcel has correct idempotency behavior, and exposes job scheduling health without exposing Hangfire administration. The baseline task resolves source-of-truth conflicts before code is dispatched; it is not permission to invent the missing API contracts.

## Success criteria (DoD — binary, verifiable)
- [ ] An Outbox publish failure reaches a durable `OutboxDLQ` record after the approved retry threshold; its original event identity, type, payload, failure metadata, and terminal time are retained for review without duplicate terminal records.
- [ ] A `SYSTEM_ADMIN` can retrieve the approved, paginated `GET /admin/outbox/dlq` representation with ADR 0004 envelopes; unauthorized users and other tenants cannot access it.
- [ ] The approved audit inventory proves 100% idempotency coverage for every Booking, Payment, and Parcel mutation in scope, including replay, different-body mismatch, and in-flight handling.
- [ ] Each approved Hangfire-owning service exposes `GET /internal/jobs/status` with last run, next run, and the approved lag semantics, protected by Internal JWT and not proxied as a public Gateway route.
- [ ] Chaos verification stops RabbitMQ, proves Outbox rows remain durable, restarts the broker, and proves eligible rows drain; exhausted rows are observable in DLQ rather than silently parked.
- [ ] All touched .NET solutions build, format, and test successfully; migrations apply from an empty database and roll back cleanly.

## Contract changes
The Day 43 timeline requires two routes that do not exist in `VietRide_API_Contract_v1.md`; neither route nor the DLQ persistence contract may be implemented until Task 43.0 resolves the open questions.

- Add `GET /v1/admin/outbox/dlq` (or the human-approved service-qualified alternative): owning service, authorization, pagination/filter/sort allow-list, response DTO, error responses, and Gateway route only when the chosen route is FE-facing.
- Add `GET /internal/jobs/status`: owning service(s), Internal-JWT authorization, raw internal success DTO, job identifier/status fields, UTC timestamps, `nextRun`, and precisely defined `lag`; no Gateway exposure unless explicitly approved.
- Define the durable `outbox_dlq` table/record, retry boundary, original-event retention, uniqueness rule, and operator action scope. Do not add a new RabbitMQ event or error code unless Task 43.0 updates the registries.
- Reconcile `BE_TIMELINE_VU.md` Day 43 (`> 5 retries`) with `BACKEND_SOURCE_OF_TRUTH.md` §10.3 and the shared `OutboxOptions.MaxRetryCount` default (`10`), then synchronize the API contract, BSOT, canonical DDL, EF migrations, and code to the approved decision.

## Tasks

### Task 43.0 — Pre-reqs / architecture baseline: reconcile DLQ, idempotency, and job-health contracts
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `BE_TIMELINE_VU.md` only if the human-approved retry wording must be reconciled; `BACKEND_SOURCE_OF_TRUTH.md`; `VietRide_API_Contract_v1.md`; `db-schema/{identity-user,trip-route-vehicle,booking,payment-wallet,parcel}/schema.sql` and matching `README.md` only for the approved publisher scope; `apps/gateway/src/config/routes.ts` only if the approved DLQ route is public; `docs/handoff/day-43-plan.md` only to replace resolved open questions with the human decision. |
| forbidden scope | All production code, generated EF migrations, `.env`, secrets, package/dependency changes, unrelated contracts/docs, NestJS apps, git operations; do not create a new routing key/error code merely to fill a gap. |
| depends on | —. Tasks 43.1–43.8 depend on this gate; no code task is parallel-safe before the decisions are recorded. |
| invariant flags | LF for `.md`/`.sql`; ADR 0004 envelope; UPPER_SNAKE_CASE registry discipline; internal successes are raw DTOs; no cross-DB FK; Outbox remains transactional and polled by `BackgroundService`, never Hangfire; Gateway forwards headers but does not own idempotency. |
| acceptance | Human decisions resolve Q1–Q4, including retry boundary, publisher/DLQ ownership, admin-list contract, mutation inventory/exemptions, and job-health owner/lag definition. All affected SOT documents agree before code work starts; no unapproved API surface, event, schema, or implementation change is made. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §5.6, §7.4, §9.8, §10.1–10.3; `VietRide_API_Contract_v1.md` (no Day-43 routes found); `libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxOptions.cs`; `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs`. |

### Task 43.1 — Shared durable Outbox DLQ transition and publisher behavior
| Field | Value |
|---|---|
| stack/owner | dotnet / shared persistence + messaging |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event (not applicable; do not publish a new event) |
| owned files (write set) | `libs/dotnet/VietRide.Shared.Persistence/Outbox/OutboxEvent.cs`; `IOutboxStore.cs`; `OutboxStore.cs`; new shared `OutboxDlq` entity/envelope/store methods under `libs/dotnet/VietRide.Shared.Persistence/Outbox/`; `libs/dotnet/VietRide.Shared.Persistence/VietRideDbContextBase.cs` and persistence DI/configuration only as required; `libs/dotnet/VietRide.Shared.Messaging/Outbox/{OutboxOptions.cs,OutboxBackgroundService.cs}`; focused shared-library tests under `libs/dotnet/**/tests/**` or the established consuming test project. |
| forbidden scope | Service-local API/controller code, service migrations/schema files, RabbitMQ consumer DLQ configuration, Hangfire jobs, idempotency code, `.env`, secrets, new dependencies, git operations. |
| depends on | 43.0. Parallel-safe with 43.3 after 43.0 because write sets are disjoint. |
| invariant flags | CRLF `.cs`; original Outbox write stays in the business transaction; terminal move is atomic/idempotent and preserves immutable event id/type/payload, retry count, last error, created/terminal timestamps; publish success remains at-least-once; no event deletion before durable terminal persistence; retry comparison exactly matches the approved Q1 boundary; no Hangfire use for polling. |
| acceptance | Unit tests force repeated publisher failures and prove exactly one terminal DLQ record at the approved boundary, no terminal record below it, no duplicate on a repeated worker tick, and unchanged successful publish behavior. The worker no longer leaves an exhausted event merely parked in `FAILED`; shared projects build and format cleanly. |
| source citations | `BACKEND_SOURCE_OF_TRUTH.md` §7.4, §10.3; `SU26SE101_VIETRIDE_technical_context_v7.md` §Outbox (lines 3494–3501, 4593–4596); `OutboxBackgroundService.cs`; `OutboxStore.cs`; `OutboxEvent.cs`; `AGENTS_DOTNET.md` Outbox/EF rules. |

### Task 43.2 — Service DLQ persistence rollout and approved admin-review endpoint
| Field | Value |
|---|---|
| stack/owner | dotnet / approved Outbox-publishing services |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration + add-endpoint |
| owned files (write set) | For each service explicitly approved in Q2: its `DbContext.cs`, EF configuration/DI files, new reversible migration under `apps/<service>/src/VietRide.<Service>.Infrastructure/Migrations/`, matching `db-schema/<service-schema>/schema.sql` and `README.md`, and focused persistence tests. For the approved API owner only: new DLQ CQRS query/DTO/validator/handler files, its `AdminOutboxDlqController.cs`, API/integration tests, `VietRide_API_Contract_v1.md`, and `apps/gateway/src/config/routes.ts` only if Task 43.0 approves a public route. |
| forbidden scope | Shared publisher algorithm from 43.1; unrelated business mutations/events; RabbitMQ broker topology; Hangfire configuration; idempotency implementation; `.env`, secrets, package changes, git operations. |
| depends on | 43.0, 43.1. Parallel-safe with 43.4–43.6 only after each service's write set is assigned; default serial because migrations and API ownership are undecided before Q2. |
| invariant flags | CRLF `.cs`; LF schema/docs; EF migration `Up()`/`Down()` and canonical DDL match; one service DB only per DLQ table, never a cross-DB FK; SYSTEM_ADMIN-only list with ADR 0004 and approved `QueryOptions` allow-list; payload is treated as sensitive operational data and never logged; no write/replay/purge admin action unless separately approved. |
| acceptance | Fresh migration creates only the approved DLQ objects and rollback removes only those objects; an exhausted shared event is queryable through the approved endpoint; paging/filtering and authorization tests prove no non-admin access and no unbounded result set; Swagger/Gateway behavior match the approved contract. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §4.2, §5.6–5.8, §7.4, §10.3; `db-schema/booking/schema.sql:418-432`; `db-schema/payment-wallet/schema.sql:469-483`; `db-schema/parcel/schema.sql:278-292`; `AGENTS_DOTNET.md` EF/API rules. |

### Task 43.3 — Shared idempotency enforcement and auditable route inventory
| Field | Value |
|---|---|
| stack/owner | dotnet / shared web |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs`; `libs/dotnet/VietRide.Shared.Web/DependencyInjection/IdempotencyServiceCollectionExtensions.cs`; any existing shared API error/filter helper needed to return the approved missing/pending responses; new focused shared-web tests; a machine-readable or test-owned approved endpoint inventory under the existing Booking/Payment/Parcel test convention, selected in Task 43.0. |
| forbidden scope | Business handler behavior, service controller route redesign, VNPay HMAC verification, new Redis/NuGet dependency, Outbox/DLQ code, Hangfire code, `.env`, secrets, git operations. |
| depends on | 43.0. Parallel-safe with 43.1 after 43.0; Tasks 43.4–43.6 depend on this task. |
| invariant flags | CRLF `.cs`; Redis key `<service>:idem:{uuid-v4}` with 24h TTL; canonical replay preserves HTTP status and body; same key plus different request body returns 422 `IDEMPOTENCY_KEY_MISMATCH`; in-flight duplicate returns approved 409 `IDEMPOTENCY_REQUEST_PENDING`; approved required mutation methods reject absent/invalid headers without invoking business code; no cache of 5xx; callback/internal exemptions follow Q3 rather than being guessed. |
| acceptance | Tests cover required method selection (including approved PUT/DELETE behavior), missing/invalid UUID header, first request, same-body replay, different-body mismatch, concurrent/in-flight duplicate, Redis SETNX race, response content type/body preservation, and 5xx non-cache policy. The inventory is executable and fails when an approved in-scope mutation is uncovered. |
| source citations | `BACKEND_SOURCE_OF_TRUTH.md` §5.6, §5.9, §9.8; `AGENTS.md` Quick references; `AGENTS_DOTNET.md` Idempotency; `IdempotencyMiddleware.cs`; `IdempotencyServiceCollectionExtensions.cs`; `RequireIdempotencyKeyAttribute.cs`. |

### Task 43.4 — Booking mutation idempotency coverage audit and remediation
| Field | Value |
|---|---|
| stack/owner | dotnet / Booking |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint (not applicable; preserve approved endpoints) |
| owned files (write set) | `apps/booking/src/VietRide.Booking.Api/Program.cs`; only approved mutation controllers from `AdminVouchersController.cs`, `BookingsController.cs`, `BoardingController.cs`, `InternalVouchersController.cs`, `AdminCampaignsController.cs`, `OperatorVouchersController.cs`, and `OperatorVoucherConsentsController.cs`; focused Booking unit/integration tests under `apps/booking/tests/VietRide.Booking.{UnitTests,IntegrationTests}/`; Booking entries in the approved inventory. |
| forbidden scope | Booking domain pricing/state-machine redesign, schema/migrations, Payment/Parcel/Trip code, Gateway, shared middleware edits after 43.3, Outbox/DLQ work, `.env`, secrets, dependencies, git operations. |
| depends on | 43.0, 43.3. Parallel-safe with 43.5 and 43.6 because service write sets are disjoint. |
| invariant flags | Thin-controller/CQRS boundary unchanged; every Q3-approved mutation has a required idempotency key and replay-safe response; explicitly preserve approved behavior-idempotent actions and excluded internal/read routes; 24h Booking key namespace; ADR 0004 errors; no tenant bypass or duplicate booking/voucher/consent side effect. |
| acceptance | The approved controller inventory is exhaustive and test-enforced; each in-scope Booking mutation proves one business invocation for repeated same-body keys, 422 mismatch for a different body, approved missing-key rejection, and no duplicate external/internal side effect. Booking build, format, unit tests, integration tests, and NetArchTest pass. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §5.6; `apps/booking/src/VietRide.Booking.Api/Program.cs`; Booking controller files listed above; `apps/booking/tests/VietRide.Booking.IntegrationTests/{CreateBookingIntegrationTests,CancelBookingIntegrationTests,EditPickupIntegrationTests,EditDropoffIntegrationTests}.cs`. |

### Task 43.5 — Payment mutation idempotency coverage audit and remediation
| Field | Value |
|---|---|
| stack/owner | dotnet / Payment |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint (not applicable; preserve approved endpoints) |
| owned files (write set) | `apps/payment/src/VietRide.Payment.Api/Program.cs`; only approved mutation controllers from `WalletController.cs`, `InternalWalletController.cs`, `InternalPaymentsController.cs`, `VnPayIpnController.cs`, and `VnPayBookingIpnController.cs`; focused Payment unit/integration tests under `apps/payment/tests/VietRide.Payment.{UnitTests,IntegrationTests}/`; Payment entries in the approved inventory. |
| forbidden scope | VNPay signing/return semantics except the approved Q3 idempotency boundary; payment ledger/domain calculations; schema/migrations; Booking/Parcel code; shared middleware edits after 43.3; Outbox/DLQ and job-health changes; `.env`, secrets, dependencies, git operations. |
| depends on | 43.0, 43.3. Parallel-safe with 43.4 and 43.6; Task 43.7 must wait because both may edit Payment `Program.cs`. |
| invariant flags | Internal and public mutations follow the Q3 inventory; VNPay IPN deduplication/HMAC remains correct and is not accidentally converted to a client `Idempotency-Key` requirement; Payment Redis namespace/24h replay behavior; same-key/different-body and pending errors remain canonical; no double wallet debit, top-up credit, or charge record. |
| acceptance | Tests cover every approved Payment mutation plus the explicit VNPay callback exemption/alternative scheme; replay never repeats a ledger movement, mismatch returns 422, concurrent duplicate returns approved 409, and missing required key has no business effect. Payment build, format, unit and integration suites pass. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §5.6, §5.9, §9.8; `apps/payment/src/VietRide.Payment.Api/Program.cs`; `WalletController.cs`; `InternalWalletController.cs`; `InternalPaymentsController.cs`; `VnPayIpnController.cs`; `VnPayBookingIpnController.cs`. |

### Task 43.6 — Parcel mutation idempotency coverage audit and remediation
| Field | Value |
|---|---|
| stack/owner | dotnet / Parcel |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint (not applicable; preserve approved endpoints) |
| owned files (write set) | `apps/parcel/src/VietRide.Parcel.Api/Program.cs`; `Filters/RequireIdempotencyKeyAttribute.cs`; only approved mutation controllers from `AssistantParcelsController.cs`, `InternalParcelsController.cs`, `OperatorParcelRouteFaresController.cs`, `OperatorParcelsController.cs`, `ParcelsController.cs`, and `ParcelDeliveryController.cs`; focused tests under `apps/parcel/tests/VietRide.Parcel.{UnitTests,IntegrationTests}/`; Parcel entries in the approved inventory. |
| forbidden scope | Parcel lifecycle/capacity/refund business rules, schema/migrations, Trip/Booking/Payment code, shared middleware edits after 43.3, Outbox/DLQ and job-health work, `.env`, secrets, dependencies, git operations. |
| depends on | 43.0, 43.3. Parallel-safe with 43.4 and 43.5; Task 43.7 waits because both may edit Parcel `Program.cs`. |
| invariant flags | Required mutations use the Parcel 24h Redis namespace; delivery-token public route and internal operational routes follow explicit Q3 authorization/exemption decisions; preserve current `RequireIdempotencyKeyAttribute` validation semantics or consolidate only when behavior stays canonical; no duplicate parcel, charge/refund, capacity counter change, or state transition. |
| acceptance | An exhaustive, test-enforced Parcel mutation inventory passes: valid replay returns the original response with no repeated side effect; changed body returns 422; concurrent duplicate returns approved 409; absent required header invokes no business handler; all approved exemptions remain covered. Parcel build, format, unit and integration suites pass. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §5.6, §5.9, §9.8; `apps/parcel/src/VietRide.Parcel.Api/Program.cs`; `RequireIdempotencyKeyAttribute.cs`; Parcel controller files listed above; `AGENTS_DOTNET.md` Idempotency/Test rules. |

### Task 43.7 — Internal Hangfire job-status endpoint
| Field | Value |
|---|---|
| stack/owner | dotnet / approved Hangfire-owning services |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | For each service approved in Q4: its `Program.cs`, new `InternalJobsController.cs`, CQRS query/DTO/handler files under its Application project, Hangfire status adapter/query files under Infrastructure, DI wiring, and focused API/unit/integration tests. Existing confirmed starting points are `apps/payment/src/VietRide.Payment.Api/Program.cs` and `apps/parcel/src/VietRide.Parcel.Api/Program.cs`; Booking/Identity/Trip paths are included only if Q4 confirms their registration scope. `VietRide_API_Contract_v1.md` is updated for the finalized internal contract. |
| forbidden scope | Hangfire dashboard exposure, public Gateway route, job schedule/cron changes, business job handlers, Outbox polling, new dependency, `.env`, secrets, DLQ/idempotency code, git operations. |
| depends on | 43.0, 43.5, 43.6. Parallel-safe with 43.2 only if it does not own the same service API files; otherwise serial. |
| invariant flags | CRLF `.cs`; Internal JWT only; successful `/internal/*` payload raw while errors keep ADR 0004 envelope; UTC timestamps; no sensitive Hangfire arguments/connection strings exposed; `lastRun`, `nextRun`, and `lag` obey Q4 definition; status reads do not mutate job state or schedule. |
| acceptance | Authorized internal callers receive one approved DTO row per registered job with stable job id, last run, next run, and lag; unauthenticated/user-token callers are rejected; missing/never-run/failed jobs return the approved representation without 500; unit tests mock Hangfire storage and integration tests verify route/auth/envelope behavior. Existing jobs still register with the same cron/worker configuration. |
| source citations | `BE_TIMELINE_VU.md` Day 43; `BACKEND_SOURCE_OF_TRUTH.md` §4.5, §6.4, §10.1, §10.3; `SU26SE101_VIETRIDE_technical_context_v7.md:360-366,4596-4602`; Payment and Parcel `Program.cs`; `AGENTS_DOTNET.md` Internal JWT/API/Health rules. |

### Task 43.8 — Reliability chaos and regression verification
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | smoke-test |
| owned files (write set) | Focused automated tests under the approved shared, Booking, Payment, Parcel, and DLQ-owning service test projects; the existing local test harness/script only when it already supports RabbitMQ outage/restart; `docs/handoff/day-43-plan.md` Progress tracker only. |
| forbidden scope | Feature redesign, production broker configuration, service schedules, schema changes, `.env`, secrets, new packages, unrelated test cleanup, git operations. |
| depends on | 43.1–43.7. Parallel-safe = no; final operational gate. |
| invariant flags | Tests use isolated local/test infrastructure and no production credentials; broker outage proves durable DB retention, not an in-memory assertion; consumers remain idempotent under at-least-once delivery; no test weakens authorization, tenant isolation, response envelopes, or 24h idempotency semantics. |
| acceptance | Automated or reproducible local chaos sequence: persist an Outbox event, stop RabbitMQ before publication, observe retry/durability, restart RabbitMQ, observe one eventual publish; separately exceed the approved threshold and observe one DLQ record plus admin-query visibility. Run approved build/format/test matrix for shared/Booking/Payment/Parcel and migration smoke tests; record any environmental limitation explicitly. |
| source citations | `BE_TIMELINE_VU.md` Day 43 Review; `BACKEND_SOURCE_OF_TRUTH.md` §7.4, §10.3, §12 testing; `AGENTS_DOTNET.md` Build/Test; `libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs`. |

## Dispatch order
1. Task 43.0 → mandatory decision/contract gate; do not dispatch implementation until Q1–Q4 are resolved.
2. Task 43.1 and Task 43.3 → parallel-safe after 43.0 because shared persistence/messaging and shared web write sets are disjoint.
3. Task 43.2 → after 43.1; serialize with any task that receives the same service API ownership.
4. Tasks 43.4, 43.5, and 43.6 → after 43.3; parallel-safe because Booking, Payment, and Parcel write sets are disjoint.
5. Task 43.7 → after 43.5 and 43.6 to avoid `Program.cs` collisions; include additional services only as decided in Q4.
6. Task 43.8 → final reliability gate after all implementation tasks.

## Progress tracker
> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task` (Step 3) with the task's review verdict. **Informational only — NOT audit evidence.** `/audit-day` MUST re-verify every task independently against the SOT; it must never treat a completed row (or a worker self-report) as proof. A row is bookkeeping, not a passed audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 43.0 | done | APPROVED (full audit) | 2026-07-20 | Q1–Q4 đã khóa và đồng bộ API contract/BSOT/schema. |
| 43.1 | done | APPROVED (full audit) | 2026-07-20 | Shared terminal transition sau lần thất bại thứ sáu, atomic/unique DLQ và publisher tests đã pass. |
| 43.2 | done | APPROVED (full audit) | 2026-07-20 | DLQ cho Identity/Trip/Booking/Payment/Parcel/Tracking, facade Identity và migration gate đã pass. |
| 43.3 | done | APPROVED (full audit) | 2026-07-20 | Shared idempotency v2 và inventory verifier đã pass. |
| 43.4 | done | APPROVED (full audit) | 2026-07-20 | Booking inventory: 7 controller, 24 mutation, 1 exemption. |
| 43.5 | done | APPROVED (full audit) | 2026-07-20 | Payment inventory: 6 controller, 14 mutation, 3 exemption. |
| 43.6 | done | APPROVED (full audit) | 2026-07-20 | Parcel inventory: 6 controller, 20 mutation, 0 exemption. |
| 43.7 | done | APPROVED (full audit) | 2026-07-20 | Internal job health cho 5 Hangfire service, Internal JWT và lag semantics đã pass. |
| 43.8 | done | APPROVED (full audit) | 2026-07-20 | RabbitMQ outage/drain, terminal DLQ, degraded source, cursor và full regression gates đã pass. |

Legend: todo / in progress / done (reviewer APPROVED + human `/verify`) / done-with-carryover / blocked

## Quyết định contract đã khóa

- Mọi publisher trong scope, gồm Identity, Trip, Booking, Payment, Parcel và Tracking, đều chuyển event sang DLQ bền vững sau lần publish thất bại thứ sáu (`retry_count > 5`). Không đổi lịch backoff ngoài terminal boundary.
- Mỗi service giữ DLQ trong database của chính mình; Identity sở hữu facade `GET /v1/admin/outbox/dlq`. Facade dùng `SYSTEM_ADMIN`, cursor tổng hợp có service/cursor component, trả `200` kèm `unavailableServices` khi một nguồn tạm thời không truy cập được, và không trả partial totals giả. Không có replay/purge ở v1.
- Payload DLQ chỉ phục vụ review, giữ event id/type/payload, retry count, last error, created/failed/terminal timestamps; payload không ghi log. Marker unique theo `(service,event_id)` bảo đảm không có terminal duplicate.
- Idempotency audit bao phủ mọi POST/PATCH/PUT/DELETE của Booking, Payment và Parcel. VNPay IPN callback giữ HMAC/deduplication hiện hữu và là exemption có kiểm thử; delivery-token public action và internal mutation chỉ được exemption khi đã có cơ chế deduplication tương đương trong inventory. Missing, invalid UUID-v4, mismatch và in-flight dùng error contract hiện hành.
- Mỗi service đang sở hữu Hangfire (`Identity`, `Trip`, `Booking`, `Payment`, `Parcel`) expose service-local `GET /internal/jobs/status`, chỉ Internal JWT, không qua Gateway và không mở dashboard. DTO gồm job id, status, lastRun, nextRun, lagSeconds; lag là `max(0, nowUtc - nextRunUtc)` cho job quá hạn, `null` khi chưa có next run/disabled. Endpoint không đổi schedule/readiness.
- Chaos gate phải chứng minh RabbitMQ outage giữ Outbox trong Postgres, broker restart drain được event eligible, và event vượt threshold xuất hiện đúng một DLQ row/query được từ Identity facade.

## Reopening addendum — 2026-07-31

Day 43's original DLQ, job-status, idempotency middleware, and E2E delivery remain historical
completed work. Closure is reopened because the executable idempotency inventory drifted after
later endpoint merges. This addendum does not alter the prior tracker or audit evidence.

The approved repair in `docs/handoff/day-36-43-fe-gap-repair-plan.md` first fixes discovery
semantics for absolute action routes and `[NonAction]`, then freezes the final inventory only after
the new internal Payment redirect lookup, Booking refund consumer, and two read-only lookup
clients exist. The final gate reruns the inventory verifier, Day 43 reliability E2E, impacted
regression, and writes a fresh `docs/handoff/day-43-checklist.md`.

### Final inventory freeze — 2026-08-01

The reopened audit freezes the exact mutation surface after the later endpoint merges and the
Payment redirect-history work:

| Service | Total | Required | Exempt |
| --- | ---: | ---: | ---: |
| Identity | 35 | 30 | 5 |
| Trip | 56 | 53 | 3 |
| Booking | 27 | 26 | 1 |
| Payment | 15 | 11 | 4 |
| Parcel | 31 | 30 | 1 |
| Notification | 3 | 3 | 0 |
| RAG | 13 | 12 | 1 |
| **Total** | **180** | **165** | **15** |

The cross-system inventory now distinguishes source-level registration sites from registrations
created by mapped binding arrays:

- 45 .NET `IIntegrationEventHandler<T>` registrations.
- 14 Notification `.subscribe(...)` source callsites, expanding to 73 runtime RabbitMQ binding
  registrations (five callsites map binding collections; nine are direct registrations).
- 28 .NET outbound POST/PUT/PATCH/DELETE-style HTTP callsites. Nineteen target
  idempotency-required mutations and nine are exact read-only exemptions across eight files. The
  post-merge additions are Payment's Identity financial projection lookup, two Trip revenue
  analytics batch lookups, and Parcel's Trip summary batch lookup.

The provisional repair-plan baseline of 22 outbound callsites and four exemption files omitted two
older `PostAsJsonAsync` callsites. Merging `origin/main` at `5b00b313` then added the UI-gap policy,
analytics and projection surfaces, raising the executable baseline to 180/165/15, 45 handlers and
28/9 outbound callsites/exemptions. The inventory uses callsite tokens rather than whole-file
exemptions so mixed clients cannot exempt mutation callsites accidentally.

## Open questions đã đóng

Các Q1–Q4 cũ bên dưới được giữ làm lịch sử của bản draft; quyết định ở mục trên là SOT hiện hành và không còn là blocker.

**Q1 — Outbox retry boundary.** Day 43 says events failed `> 5 retries` enter `OutboxDLQ`; `BACKEND_SOURCE_OF_TRUTH.md` §10.3 and the shared `OutboxOptions` currently use 10. Should the terminal transition occur on the fifth failed publish (`retry_count = 5`) or after a sixth failed publish (`retry_count > 5`), and does this Day replace the global maximum for every publisher service?

**Q2 — DLQ scope and admin owner.** Every publisher has its own Outbox table, but `GET /admin/outbox/dlq` has no service prefix or aggregation contract. Is DLQ required for Identity, Trip, Booking, Payment, and Parcel, and should the list be one endpoint per service, a Booking/Payment/Parcel-only rollout, or an explicitly designed aggregated admin surface? Confirm role, pagination/filter fields, payload visibility, and whether Day 43 includes any replay/purge action (the timeline asks for review only).

**Q3 — Meaning of “ALL mutation endpoints.”** The timeline/AGENTS rule says all POST/PATCH/PUT/DELETE mutations, while BSOT §5.6 enumerates a narrower set and the shared middleware currently only processes POST/PATCH with an optional key. Confirm the exact inventory and exemptions, especially VNPay IPN callbacks (HMAC/idempotency path), public delivery-token actions, Internal-JWT mutations, and behavior-idempotent actions. Confirm whether missing or non-UUID keys must be rejected with the existing `VALIDATION_ERROR` shape.

**Q4 — Job-health scope and lag semantics.** `/internal/jobs/status` is absent from the API contract. Which services must expose it (all Hangfire-owning services or only currently registered Payment/Parcel), who may call it, and how is `lag` calculated for recurring, delayed, never-run, failed, and disabled jobs? Confirm whether an overdue job must affect HTTP status/readiness or is reported in a successful status DTO only.
