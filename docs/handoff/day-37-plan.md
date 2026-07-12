# Day 37 — Subscription lifecycle

- **Timeline ref**: BE_TIMELINE_VU.md → Day 37 (SCV-118)
- **Prior checklist**: `docs/handoff/day-36-checklist.md` (`not found`)
- **Plan status**: APPROVED (user-approved implementation override)

## Objective

Hoàn thiện vòng đời subscription cho operator: plan có giới hạn tài nguyên/module, upgrade thanh toán, hết hạn vẫn đọc được nhưng các write boundary bị chặn, và cảnh báo ngưỡng sử dụng. Identity vẫn là owner của `SubscriptionPlan` và `OperatorSubscription`; Payment chỉ sở hữu giao dịch/VNPay và phát integration event qua Outbox. Kế hoạch này cũng bổ sung verification E2E tự seed theo yêu cầu, nhưng chỉ chạy trong môi trường local/E2E cô lập. Day 38 mới chịu trách nhiệm Invoice và settlement.

## Success criteria (DoD — binary, verifiable)

- [ ] OperatorSubscription chuyển đúng các trạng thái canonical `PENDING_APPROVAL | ACTIVE | PENDING_PAYMENT | EXPIRED | CANCELLED`; Starter trial bắt đầu khi operator được approve, không có enum `TRIAL`.
- [ ] Operator có thể xem dữ liệu sau expiry, nhưng mọi mutation được Day 37 đưa vào phạm vi enforcement trả `402 SUBSCRIPTION_EXPIRED`; module bị tắt trả `403 SUBSCRIPTION_MODULE_DISABLED` trước side effect.
- [ ] Tạo resource vượt giới hạn trả `422 SUBSCRIPTION_LIMIT_EXCEEDED`; concurrent request không vượt hard limit; một mức sử dụng từ dưới 80% sang đạt/vượt 80% chỉ tạo một cảnh báo cho resource/kỳ.
- [ ] Upgrade có `Idempotency-Key`, không tạo nhiều payment hay nhiều lần activate khi retry/IPN/event bị giao lặp; transaction không dùng cross-DB FK hoặc distributed transaction.
- [ ] VNPay subscription payment hợp lệ kích hoạt subscription theo contract đã phê chuẩn và phát event qua Outbox; Invoice không nằm trong Day 37.
- [ ] `VEHICLE_SUBSTITUTION` không kiểm, không tăng `maxTripsPerMonth`; auto generation bị quota chặn ghi `TripGenerationSkipLog` và event `subscription.limit.trip_skipped`.
- [ ] E2E local cô lập tự seed, gọi Gateway/service thật, kiểm tra DB/Outbox, và chạy xanh các scenario Day 37; không truncate/xóa dữ liệu dev chung.

## Contract changes

Task 37.0 phải ratify rồi mới thực hiện code. Những thay đổi dự kiến cần được ghi đầy đủ vào API contract/BSOT trước khi dispatch worker:

- Public Identity endpoints: `GET /v1/operator/subscription`, `GET /v1/operator/subscription-plans`, `POST /v1/operator/subscription/upgrade`, và các admin plan endpoints cần thiết để System Admin quản lý plan. Day 37 chỉ hỗ trợ VNPay; endpoint timeline cũ `/operator/subscription/pay` và `paymentMethod=WALLET` không được implement song song. OperatorWallet-based WALLET là carry-over của Day 38.
- Upgrade hỗ trợ `MONTHLY` và `YEARLY`: Identity chụp price snapshot từ plan ở server; activation tính kỳ mới bằng `AddMonths(1)` hoặc `AddYears(1)`. Payment internal contract Identity → Payment tạo/expire payment subscription; Payment sở hữu payment state và phát event, Identity sở hữu subscription lifecycle. Giữ `POST /internal/v1/operators/{operatorId}/usage/increment` tương thích consumer hiện hữu và bổ sung durable idempotent allocation keyed theo `resourceId`, explicit release và reconciliation an toàn; không dùng distributed transaction.
- Error canonical: `SUBSCRIPTION_LIMIT_EXCEEDED` 422, `SUBSCRIPTION_MODULE_DISABLED` 403, `SUBSCRIPTION_EXPIRED` 402, `SUBSCRIPTION_PAYMENT_PENDING` 409. Mọi mutation public phải dùng `Idempotency-Key` theo BSOT §5.6.
- Gateway route cho family `/v1/operator/subscription*` tới Identity và route/admin plan tương ứng; không expose `/internal/v1/*`.
- EF migration/schema Identity chỉ bổ sung cột/index/constraint thật sự cần sau khi billing period, payment reference và job ownership được ratify; canonical DDL phải đồng bộ migration. Payment không có cross-DB FK đến Identity.

## Tasks

### Task 37.0 — Pre-reqs / subscription architecture baseline and contract ratification

| Field | Value |
|---|---|
| stack/owner | cross-cutting / BE lead |
| implement agent | worker |
| review agent | reviewer |
| skill | none |
| owned files (write set) | `BE_TIMELINE_VU.md`; `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `docs/handoff/day-37-plan.md`; `apps/gateway/src/config/routes.ts` only after the approved paths/auth roles are recorded |
| forbidden scope | `.env`, secrets, `.agents/**`, `.codex/**`, `.claude/**`, application feature code, migrations, package manifests, git operations, new dependencies |
| depends on | none |
| invariant flags | LF Markdown/TS; ADR 0004 envelope; UUID `Idempotency-Key` with 24h replay semantics; Money is BIGINT VND; no cross-DB FK/transaction; internal JWT only for `/internal/v1/*`; do not invent a `TRIAL` status |
| acceptance | Các quyết định ratified bên dưới đã được ghi vào API contract/BSOT; API contract có exact request/response/auth/error shapes; BSOT event/job registries name one owner and one payload per event; timeline’s obsolete `TRIAL`, `429`, and `/pay` wording is reconciled to the ratified source; Gateway routes are added only for ratified public endpoints. |
| source citations | `BE_TIMELINE_VU.md` Day 37; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.5, especially lines 769-896; `VietRide_API_Contract_v1.md` internal subscription contract lines 2999-3062; `BACKEND_SOURCE_OF_TRUTH.md` §§5.6, 5.9, 7.2, 7.3, 10.1 |

### Task 37.1 — Identity plan catalog, lifecycle, and subscription API

| Field | Value |
|---|---|
| stack/owner | dotnet / Identity |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `ef-migration`, `add-endpoint`, `add-integration-event` where the ratified contract requires them |
| owned files (write set) | `apps/identity/src/VietRide.Identity.Domain/Entities/SubscriptionPlan.cs`; `apps/identity/src/VietRide.Identity.Domain/Entities/OperatorSubscription.cs`; `apps/identity/src/VietRide.Identity.Domain/Enums/`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/ISubscriptionPlanRepository.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IOperatorSubscriptionRepository.cs`; new `apps/identity/src/VietRide.Identity.Application/Features/Subscriptions/`; `apps/identity/src/VietRide.Identity.Api/Controllers/`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/SubscriptionPlanConfiguration.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/OperatorSubscriptionConfiguration.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/SubscriptionPlanRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OperatorSubscriptionRepository.cs`; new Identity migration under `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/`; `db-schema/identity-user/schema.sql`; `db-schema/identity-user/README.md`; Identity unit/integration tests |
| forbidden scope | Payment, Trip, Parcel, Gateway, Notification, RAG code; `.env`/secrets; deleting or reseeding existing plans; new NuGet package; git operations |
| depends on | 37.0 |
| invariant flags | CRLF `.cs`; LF SQL/Markdown; MediatR v11; CPM with no `Version=`; `SubscriptionPlan.StarterPlanId` and starter limits remain stable; Money uses `Money`/BIGINT VND; subscription is not soft-deletable; tenant derives from JWT; System Admin only for plan administration; no cross-DB FK |
| acceptance | Plan create/update/deactivate and operator read/upgrade APIs exactly match the ratified contract; inactive plan cannot start a new upgrade; server derives price/limits/modules; guarded transitions reject invalid lifecycle changes; existing register/approve/reject flows preserve `PENDING_APPROVAL → ACTIVE` and `PENDING_APPROVAL → CANCELLED`; migration has reversible `Down()` and canonical DDL matches; unit/integration tests cover status transition, auth/tenant scope, plan inactivity, idempotency replay and upgrade conflict. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` §4.4 and §4.5c.1; `db-schema/identity-user/schema.sql` `subscription_plans`/`operator_subscriptions`; `VietRide_API_Contract_v1.md` lines 2601, 2706, 2731, 2761, 2999-3062; `BACKEND_SOURCE_OF_TRUTH.md` §§5.6, 5.9, 6.1 |

### Task 37.2 — Subscription payment orchestration and VNPay completion

| Field | Value |
|---|---|
| stack/owner | dotnet / Identity + Payment |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-endpoint`, `add-integration-event` |
| owned files (write set) | ratified Identity subscription feature files from Task 37.1; new `apps/identity/src/VietRide.Identity.Infrastructure/Messaging/SubscriptionPayment*`; `apps/payment/src/VietRide.Payment.Domain/Entities/Payment.cs`; `apps/payment/src/VietRide.Payment.Application/Features/Internal/Payments/`; `apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/PaymentRepository.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/VnPay/`; `apps/payment/src/VietRide.Payment.Api/Controllers/VnPayBookingIpnController.cs` or a new subscription-specific IPN controller selected by Task 37.0; `apps/payment/src/VietRide.Payment.Api/Program.cs`; Payment unit/integration tests; Payment migration/schema only if Task 37.0 ratifies a new durable dedupe/reference field |
| forbidden scope | Invoice/PDF, OperatorWallet, settlement, PassengerWallet semantics, external VNPay credentials, Gateway, Notification/RAG code, `.env`/secrets, new NuGet package, git operations |
| depends on | 37.0, 37.1 |
| invariant flags | CRLF `.cs`; payment amount comes from Identity’s server-side plan snapshot, never client input; `reference_type=SUBSCRIPTION`; payment and PlatformWallet credit are idempotent/atomic in Payment’s DB; Outbox event is written in the same local transaction; valid VNPay signature/amount/reference required; no cross-DB FK/transaction |
| acceptance | An `OPERATOR_ADMIN` upgrade creates/replays exactly one pending subscription payment and returns only the ratified redirect response; valid IPN transitions payment once and produces one activation event; duplicate request, IPN, and event do not create duplicate payment, PlatformWallet credit, activation, or event; failed/expired payment never activates; tests exercise signature failure, amount/reference mismatch, replay and Identity consumer idempotency. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` §4.5c.1 “PENDING_PAYMENT lifecycle” and §4.5e; `db-schema/payment-wallet/schema.sql` `payments` and `platform_wallet_transaction_ref`; `BACKEND_SOURCE_OF_TRUTH.md` §§5.6, 7.3; `VietRide_API_Contract_v1.md` ADR 0004 conventions |

### Task 37.3 — Lifecycle jobs and notification handoff

| Field | Value |
|---|---|
| stack/owner | dotnet / Identity lifecycle job owner; Payment owns payment state/event; Notification is coordination-only |
| implement agent | worker |
| review agent | reviewer |
| skill | `add-integration-event` |
| owned files (write set) | `apps/identity/src/VietRide.Identity.Infrastructure/Jobs/`; `apps/identity/src/VietRide.Identity.Api/Program.cs`/DI registration; Identity subscription repositories/features/messaging; Payment internal API client/contract required for expiry only; corresponding Identity/Payment unit/integration tests |
| forbidden scope | direct reads/writes of another service database; Invoice/PDF; settlement; Gateway; Notification/RAG NestJS code; `.env`/secrets; new TS/NuGet dependencies; git operations |
| depends on | 37.0, 37.1, 37.2 |
| invariant flags | CRLF `.cs`, LF `.ts`; Hangfire schedule uses ICT/`SE Asia Standard Time` as ratified; jobs are retry-safe with conditional state updates; exactly-once notification intent via durable sent marker/outbox; RabbitMQ at-least-once consumer idempotency; expired subscription preserves read access |
| acceptance | Identity owns scheduled expiry, T-3 warning, 24-hour pending warning, seven-day pending revert, and month-boundary trip-counter reset with the ratified cron; it calls Payment through the ratified internal API when a pending payment must expire. Retries and two concurrent job executions do not duplicate a transition or warning; revert restores the correct prior/default plan; tests use controllable clock/timezone and prove expiry read/write behavior. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` lines 836-896; `BACKEND_SOURCE_OF_TRUTH.md` §7.3 and §10.1; `db-schema/identity-user/schema.sql` warning/reset columns; `db-schema/payment-wallet/schema.sql` Hangfire comment |

### Task 37.4 — Quota enforcement in Identity and Trip

| Field | Value |
|---|---|
| stack/owner | dotnet / Identity + Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-endpoint`, `add-integration-event` only if Task 37.0 ratifies a new allocation/release contract |
| owned files (write set) | Identity internal subscription DTO/query/increment feature under `apps/identity/src/VietRide.Identity.Application/Features/Internal/Operators/`; `apps/identity/src/VietRide.Identity.Api/Controllers/InternalOperatorsController.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OperatorSubscriptionRepository.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/IIdentityInternalClient.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/ExternalClients/IdentityInternalClient.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Vehicles/CreateVehicleHandler.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Routes/CreateRouteHandler.cs`; `apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/TripGenerationService.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/SubstituteVehicleCommandHandler.cs`; Trip/Identity unit and integration tests |
| forbidden scope | changing plan limits in seed; direct cross-DB access; billing/payment implementation; Parcel/RAG/Notification/Gateway code; deleting/suspending resource semantics unless the ratified allocation contract explicitly includes release; `.env`/secrets; git operations |
| depends on | 37.0, 37.1 |
| invariant flags | CRLF `.cs`; atomically enforce limits in Identity; `422 SUBSCRIPTION_LIMIT_EXCEEDED`; no negative/duplicate usage increments; internal JWT; no cross-DB FK/transaction; tenant/operator ownership; `VEHICLE_SUBSTITUTION` never checks/increments monthly trip usage; outbox routing key `subscription.limit.trip_skipped` |
| acceptance | Existing user creation keeps its same-DB transactional quota guard; Vehicle, Route and auto-generated Trip use the ratified idempotent quota protocol without overshoot under concurrent requests; error/rollback paths do not silently leak or double-count quota; auto generation logs `SUBSCRIPTION_LIMIT_EXCEEDED` with its required non-null `driverScheduleId` and publishes the skip event; substitution remains exempt; unit/integration/concurrency tests cover hard limit, 79→80% warning, duplicate retry, auto generation and month boundary. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` §4.5a-c.0, especially lines 734-764; `VietRide_API_Contract_v1.md` lines 2999-3062; `BACKEND_SOURCE_OF_TRUTH.md` §§5.9, 7.2, 7.3; `db-schema/trip-route-vehicle/schema.sql` `trip_generation_skip_reason` |

### Task 37.5 — Module/write-boundary enforcement and Gateway exposure

| Field | Value |
|---|---|
| stack/owner | cross-cutting / Parcel + Trip + Gateway; RAG is NestJS-owner handoff |
| implement agent | worker |
| review agent | reviewer |
| skill | `add-endpoint` for ratified public service routes; `vietride-nest-event` only if an event consumer is ratified |
| owned files (write set) | `apps/parcel/src/VietRide.Parcel.Application/Abstractions/ServiceClients/IIdentityServiceClient.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Http/IdentityServiceClient.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Http/DevIdentityServiceClient.cs`; `apps/parcel/src/VietRide.Parcel.Application/Features/Parcels/Create/CreateParcelCommandHandler.cs`; ratified Trip write eligibility/client files under `apps/trip/src/VietRide.Trip.Application/` and `apps/trip/src/VietRide.Trip.Infrastructure/`; `apps/gateway/src/config/routes.ts`; affected Parcel/Trip/Gateway tests; RAG contract handoff note only |
| forbidden scope | Payment financial transition code; Identity domain/schema other than its published internal DTO/endpoint from Tasks 37.1/37.4; Notification delivery internals; all RAG NestJS implementation; new TS/NuGet package; `.env`/secrets; git operations |
| depends on | 37.0, 37.1, 37.4 |
| invariant flags | LF TS and CRLF C#; no TS dependency without explicit approval; downstream service is authoritative for its write boundary; `enableParcel|enableShuttle|enableRag` is checked from Identity’s internal subscription contract; `403 SUBSCRIPTION_MODULE_DISABLED` before persistence/payment/outbox side effect; expired writes return 402; Gateway is only defense-in-depth; tenant isolation/internal JWT |
| acceptance | Parcel creation resolves the trip’s operator and refuses disabled Parcel before Parcel/Payment/cargo side effects; applicable Trip Shuttle write boundary enforces the ratified module flag when that Day 36 write boundary exists; all public subscription/admin routes have Gateway auth/role mapping and internal routes remain private; tests prove disabled module, expired subscription, wrong tenant/role and no-side-effect behavior. RAG receives a precise contract handoff but no Day 37 NestJS implementation is claimed. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` §4.5a-b; `BACKEND_SOURCE_OF_TRUTH.md` §§5.9 and 7.2; `VietRide_API_Contract_v1.md` internal subscription DTO lines 2999-3062; `AGENTS_NESTJS.md` routing/auth and testing rules |

### Task 37.6 — Automated deterministic seed and full E2E verification

| Field | Value |
|---|---|
| stack/owner | cross-cutting / verification |
| implement agent | worker |
| review agent | reviewer |
| skill | `smoke-test` |
| owned files (write set) | new `scripts/run-day37-subscription-e2e.mjs`; `scripts/run-full-e2e-local.mjs`; targeted test fixtures under `apps/identity/tests/`, `apps/payment/tests/`, `apps/trip/tests/`, `apps/parcel/tests/`; new or extended isolated compose project/profile explicitly named `day37-e2e` under `infra/docker/`; `docs/handoff/day-37-plan.md` progress/verification notes only |
| forbidden scope | production compose/configuration; destructive reset/truncate of developer DB; hardcoded credentials or real VNPay calls; business source code not needed for testability; `.env`/secrets; new npm/NuGet dependencies; git operations |
| depends on | 37.1, 37.2, 37.3, 37.4, 37.5 |
| invariant flags | LF JS/Markdown; test stack checks `/health` and `/ready`; reproducible fixed identifiers/natural keys in the separate `day37-e2e` compose project/database/profile; Gateway path plus real internal HTTP/RabbitMQ/Outbox boundaries; signed local VNPay IPN using injected test-only configuration; cleanup is scoped to the isolated E2E database/project only |
| acceptance | Script starts or verifies the separate `day37-e2e` compose project/database/profile, seeds idempotently, drives Gateway flows, asserts API envelopes plus persisted Identity/Payment/Trip/Parcel/Outbox state, prints machine-readable pass/fail, and leaves no E2E fixture in shared dev data. It covers: trial approval; VNPay upgrade/duplicate IPN/event; expired read vs write; 79→80% warning and concurrent hard limit; disabled Parcel before side effect; 24h pending warning/7d revert; monthly trip boundary; and substitution exemption. Existing solution build, format, unit/integration tests and Gateway lint/test/build run before E2E. |
| source citations | User-approved Day 37 plan requirement for self-seeded E2E; `scripts/run-day14-voucher-e2e.mjs`; `scripts/run-full-e2e-local.mjs`; `infra/docker/docker-compose.yml`; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.5 |

## Dispatch order

1. Task 37.0 is a hard gate. Do not dispatch implementation until the ratified decisions below are recorded in source-of-truth documents.
2. `37.1` follows 37.0.
3. `37.2` follows 37.1; `37.4` may start after 37.1 only when the quota protocol decision from 37.0 is landed.
4. `37.3` follows the ratified lifecycle/payment event contract and `37.2`.
5. `37.5` follows the published Identity read/quota contract and 37.4. Parcel/Trip sub-work is parallel-safe only after each has a disjoint write set and a dedicated stack reviewer; RAG is a NestJS-owner handoff.
6. `37.6` is serial and runs after all feature tasks. It is verification evidence, not a substitute for unit/integration tests.

## Progress tracker

> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task` (Step 3) with the task's review verdict. Informational only; `/audit-day` independently re-verifies all source-of-truth requirements.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 37.0 | ✅ done | APPROVED | 2026-07-12 | Contract, BSOT, timeline, Gateway routes and route tests updated |
| 37.1 | ⬜ todo | — | — | Identity owner |
| 37.2 | ⬜ todo | — | — | Payment/VNPay |
| 37.3 | ⬜ todo | — | — | Jobs/events |
| 37.4 | ⬜ todo | — | — | Quota enforcement |
| 37.5 | ⬜ todo | — | — | Module boundaries/Gateway |
| 37.6 | ⬜ todo | — | — | Isolated E2E |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Decisions ratified

1. **Payment method and public shape**: Day 37 is VNPay-only. The public endpoint is `POST /v1/operator/subscription/upgrade`. OperatorWallet-based `WALLET` activation is an explicit Day 38 carry-over; no Wallet payment path is implemented in Day 37.
2. **Billing period and price snapshot**: Upgrade supports `MONTHLY` and `YEARLY`. Identity captures the selected plan price server-side in the upgrade attempt/payment reference; a successful activation advances the period with `AddMonths(1)` or `AddYears(1)` respectively.
3. **Cross-service quota consistency**: Resource services use durable, idempotent quota allocations keyed by `resourceId`, explicit idempotent release after failed local persistence, and safe reconciliation of orphan allocations. There is no cross-DB or distributed transaction.
4. **Lifecycle job ownership**: Identity owns subscription expiry, warnings, auto-revert and monthly quota lifecycle jobs. Payment owns payment state and its integration event; Identity calls Payment only through the ratified internal API to expire a pending payment.
5. **Notification and RAG scope**: Notification consumer work is coordination-only for Day 37. RAG subscription enforcement is handed off to the NestJS owner; no Day 37 NestJS implementation is included unless separately assigned.
6. **E2E isolation**: E2E runs in a separate compose project/database/profile named `day37-e2e`. Seed and cleanup operate only there; shared developer databases are never truncated or reset.
