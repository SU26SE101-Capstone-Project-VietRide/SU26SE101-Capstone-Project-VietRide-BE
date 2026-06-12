# Day 15 - Plan

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 15 - Payment & Wallet: Wallet + VNPay top-up (Jira: SCV-88)
- **Branch**: feat/day-15-payment-wallet — NEW branch off main AFTER the SOT v1.11.0 commit lands (HEAD baseline updated 2026-06-12: Day 12 is MERGED to main via PR #13, commit 565f662; the earlier b64f82e reference was stale). Day 15 proceeds INDEPENDENTLY of Day 11/13 — runs branch-parallel with feat/day-11-trip-search and feat/day-13-booking-edits; only shared-file collision is Directory.Packages.props (see Task 15.5 cross-branch rule).
- **Prior checklist**: docs/handoff/day-14-checklist.md -> `not found` (Days 13 & 14 not yet implemented — Day 13 plan approved and running in parallel). Per OQ-0 RESOLVED, Day 15 does NOT carry over Day 13/14 work and does NOT depend on it; Booking<->Payment integration is DEFERRED to Day 16.
- **Plan status**: APPROVED — PLAN-REVIEW ran 2026-06-12 (REVISION-REQUIRED), all findings patched same day (HEAD baseline corrected; CPM cross-branch rule added; wallet-bootstrap E2E precondition stated; 15.2a marker-interface constraint added; invoices FK deferral note; BSOT/routes.ts citations corrected; PagedResult shape fixed; Money rule updated to BSOT v1.11.0)

## Objective
Stand up the Payment & Wallet service (currently a bare scaffold - only PingController + empty PaymentDbContext) to its first real slice: passenger Wallet + VNPay top-up. Day 15 delivers the EF schema for 7 tables (wallets, wallet_transactions, top_up_requests, platform_wallets, platform_wallet_transactions, payments, outbox_events), the architecture baseline (MediatR behaviors + idempotency + NetArchTest + Outbox + Hangfire - none wired in Payment yet), a NEW shared-lib inbound-consumer abstraction in VietRide.Shared.Messaging (Task 15.2a, first .NET consumer), wallet auto-bootstrap on identity.user.created, the POST /v1/wallet/top-up VNPay redirect flow, the VNPay top-up IPN webhook that credits the wallet idempotently (IPN = SOLE business source of truth; NO backend Return URL endpoint per OQ-1 - FE owns the VNPay return page and polls GET /v1/wallet), the 15-min top-up timeout job (TopUpRequest PENDING -> EXPIRED via Hangfire), and the wallet read endpoints. This unblocks Day 16 (booking payment + refund + Booking<->Payment integration), which reuses Wallet debit/credit + PlatformWallet holding.

## Success criteria (DoD - binary, verifiable)
- [ ] EF migration InitPaymentSchema creates wallets, wallet_transactions, top_up_requests, platform_wallets, platform_wallet_transactions, payments, outbox_events in schema vietride_payment; dotnet ef database update runs clean from empty DB and Down() reverses. (db-schema/payment-wallet/schema.sql)
- [ ] Passenger tops up 100k via VNPay sandbox -> on IPN success Wallet balance += 100k and a WalletTransaction with immutable balance_before/balance_after is recorded. (BE_TIMELINE_VU Day 15 DoD)
- [ ] Replay the same IPN twice -> idempotent (second call does NOT double-credit); dedupe via payment:vnpay_ipn:{vnpTxnRef} + top_up_requests.status guard. (BE_TIMELINE_VU Day 15 Review; BSOT 9.9 line 2154)
- [ ] All money is BIGINT VND, no decimals; top-up amount min 10,000 VND enforced. (BSOT 9.10; schema chk_top_up_requests_amount_min)
- [ ] Wallet auto-created (UPSERT idempotent) when identity.user.created is consumed. (BSOT 7.3 line 1733.) E2E precondition VERIFIED: Identity's Outbox publisher IS wired and live (RegisterCommandHandler enqueues via IIntegrationEventOutbox; AddVietRideMessaging registers OutboxBackgroundService in Identity Program.cs — BSOT changelog 1.6.5), so a real register against a running stack publishes the event. In CI without Identity running, verify by publishing a test identity.user.created message directly to exchange vietride.events and asserting the wallet row.
- [ ] GET /v1/wallet and GET /v1/wallet/transactions return the ApiResponse envelope; both require a user JWT. (API Contract Payment & Wallet, lines 1537/1557)
- [ ] TopUpRequest auto-fails (-> EXPIRED) 15 min after PENDING via a scheduled job. (BSOT 8.7 line 1939; Jobs TopUpExpiredJob line 2248)
- [ ] dotnet build apps/payment/VietRide.Payment.sln -c Release clean; dotnet format --verify-no-changes reports no changes; dotnet test green incl. NetArchTest layering.

## Contract changes
- REST (already in API Contract Payment & Wallet + already routed in Gateway routes.ts) - implement, do NOT add Gateway routes:
  - POST /v1/wallet/top-up (auth required, idempotency required) -> 201 {topUpRequestId, status:PENDING, paymentRedirectUrl} (Contract line 1511; Gateway /v1/wallet authRequired user, routes.ts ~line 198).
  - GET /v1/wallet -> 200 {userId, balance, currency} (Contract line 1537).
  - GET /v1/wallet/transactions (query from?,to?,type?,page?,pageSize?) -> 200 paged (Contract line 1557).
  - POST /v1/payments/vnpay-topup-ipn (PUBLIC - no Internal JWT, Gateway forwards verbatim; HMAC-SHA512 verify in service) (BSOT line 891/1147; Gateway publicSubpaths routes.ts ~lines 193-196).
  - NO backend VNPay Return URL endpoint (OQ-1 RESOLVED). VNPAY_RETURN_URL points to the FE app (https://app.vietride.app/payments/return, BSOT line 2389); the browser returns to FE, FE reads the VNPay query params and polls GET /v1/wallet. IPN is the SOLE business source of truth. Do NOT add any endpoint not already in the API contract.
- Events - Payment PRODUCES payment.wallet.credited {userId, amount, referenceType, referenceId} with referenceType=TOP_UP on successful top-up (BSOT 7.3 line 1757; OQ-2 RESOLVED - this is the canonical key). Payment CONSUMES identity.user.created for Wallet bootstrap (BSOT 7.3 line 1733). The timeline's topup.succeeded is INFORMAL/WRONG - there is no such routing key in BSOT 7.3; do NOT add it. The Day-15 checklist (/audit-day) MUST record this timeline erratum (timeline says topup.succeeded; canonical = payment.wallet.credited).
- Error codes (all already in BSOT 5.9 - no new codes): WALLET_TOP_UP_AMOUNT_TOO_LOW (422, line 1359), WALLET_TOP_UP_FAILED (502, line 1358), PAYMENT_VNPAY_ERROR (502, line 1353), PAYMENT_SIGNATURE_INVALID (401, line 1356), PAYMENT_ALREADY_PROCESSED (409, line 1355), PAYMENT_TIMEOUT (408, line 1354).
- DB migration: new InitPaymentSchema in apps/payment/.../Infrastructure/Migrations. No cross-DB FK (all user_id/operator_id are logical FKs).
- New dependencies (OQ-3 RESOLVED):
  - Hangfire APPROVED (OQ-3a): add Hangfire.AspNetCore + Hangfire.PostgreSql (and Hangfire.Core if the transitive pin is needed) as <PackageVersion> entries in Directory.Packages.props (CPM - NO Version= on the csproj PackageReference). Pin the free MIT core line 1.8.x. Hangfire Pro/Ace are FORBIDDEN (commercial). Hangfire storage schema = vietride_payment.hangfire (BSOT line 1131). The CPM addition is assigned to Task 15.5 (the only task that needs Hangfire); Task 15.0 must NOT add Hangfire to CPM.
  - Inbound consumer = SHARED-LIB abstraction (OQ-3b RESOLVED): added to libs/dotnet/VietRide.Shared.Messaging as NEW Task 15.2a (blocks 15.2). NO new NuGet package - RabbitMQ.Client 6.8.1 is already in CPM (Directory.Packages.props line 41). The abstraction reuses the existing IRabbitMqConnectionFactory + RabbitMqOptions (libs/dotnet/VietRide.Shared.Messaging/RabbitMq/*) and mirrors the NestJS consumer semantics of apps/notification/src/identity-events/identity-events.consumer.ts (durable named queue per purpose, binding key(s) on exchange vietride.events, manual ack, dead-letter on handler failure).

## Tasks

### Task 15.0 - Payment architecture baseline (DO FIRST; blocks every feature task)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) - mirror the Booking baseline (commit 42fb9b2) |
| owned files (write set) | apps/payment/src/VietRide.Payment.Api/Program.cs ; apps/payment/src/VietRide.Payment.Application/ApplicationAssemblyMarker.cs (new) ; apps/payment/src/VietRide.Payment.Application/VietRide.Payment.Application.csproj ; apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (new) ; apps/payment/src/VietRide.Payment.Infrastructure/Http/InternalJwtTokenFactory.cs (new) ; apps/payment/src/VietRide.Payment.Infrastructure/VietRide.Payment.Infrastructure.csproj ; apps/payment/src/VietRide.Payment.Api/VietRide.Payment.Api.csproj ; apps/payment/src/VietRide.Payment.Api/appsettings.json ; apps/payment/tests/VietRide.Payment.UnitTests/Architecture/LayeringTests.cs (new) ; apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj |
| forbidden scope | EF entities/migrations (15.1) ; any endpoint/handler/consumer (15.2/15.2a-15.6) ; libs/dotnet/VietRide.Shared.Messaging/** (the inbound-consumer abstraction is Task 15.2a, not 15.0) ; other services (apps/identity,apps/trip,apps/booking,apps/parcel,apps/gateway) ; db-schema/** ; .env/secrets ; Directory.Packages.props (no new versions here - Polly/Redis/RabbitMQ/MediatR/FluentValidation/NetArchTest already present; Hangfire belongs to Task 15.5 per OQ-3a) ; git ops ; DO NOT wire inbound messaging in Program.cs in 15.0 - the inbound consumer is registered by 15.2 (consuming the 15.2a shared abstraction), NOT by the baseline. Mirror the Booking baseline (apps/booking Program.cs), which is publish-only and has NO inbound-consumer registration - do not add one in 15.0 |
| depends on | - |
| invariant flags | CRLF/.cs ; CPM no Version= on PackageReference ; MediatR v11 ; Clean Architecture layering (Domain->nothing, App->Domain, Infra->Domain+App, Api->App+Infra) |
| acceptance | dotnet build apps/payment/VietRide.Payment.sln -c Release clean ; AddVietRideMediatRBehaviors + AddVietRideIdempotency(payment) + AddVietRideDbContext<PaymentDbContext> + AddVietRideSharedWeb wired in Program.cs mirroring apps/booking/.../Program.cs ; Redis singleton + Internal JWT provider registered in Infrastructure DI mirroring Booking ; NetArchTest LayeringTests present and green ; dotnet format --verify-no-changes clean |
| source citations | Booking baseline apps/booking/src/VietRide.Booking.Api/Program.cs lines 22-37 ; apps/booking/.../InfrastructureServiceCollectionExtensions.cs ; apps/booking/tests/.../Architecture/LayeringTests.cs ; BSOT 9.8 (idempotency) lines 2117-2134 ; AGENTS.md Clean Architecture dependency direction |

### Task 15.1 - Payment domain entities + EF mapping + InitPaymentSchema migration
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration (for the migration step) |
| owned files (write set) | apps/payment/src/VietRide.Payment.Domain/Entities/*.cs (Wallet, WalletTransaction, TopUpRequest, PlatformWallet, PlatformWalletTransaction, Payment) ; apps/payment/src/VietRide.Payment.Domain/Enums/PaymentReferenceType.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/PaymentMethod.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/PaymentStatus.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/TopUpRequestStatus.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/WalletTransactionType.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/WalletTransactionRef.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/PlatformWalletTransactionType.cs ; apps/payment/src/VietRide.Payment.Domain/Enums/PlatformWalletTransactionRef.cs (payment_reference_type/payment_method/payment_status enums are in Day-15 scope because the payments table ships in Day 15; map all 8) ; apps/payment/src/VietRide.Payment.Domain/ValueObjects/*.cs (only if new VO needed; prefer Shared.Kernel Money) ; apps/payment/src/VietRide.Payment.Infrastructure/PaymentDbContext.cs ; apps/payment/src/VietRide.Payment.Api/Program.cs (add PaymentDbContext.ConfigurePostgresTypes wiring) ; apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Configurations/*.cs (new) ; apps/payment/src/VietRide.Payment.Infrastructure/Migrations/* (new InitPaymentSchema) |
| forbidden scope | endpoints/handlers/consumers/jobs (15.2-15.6) ; tables NOT in Day-15 scope - do NOT add any EF entity, IEntityTypeConfiguration, or DbSet<> for the 6 out-of-scope tables: operator_wallets, operator_wallet_transactions, operator_ledger_entries, operator_trip_settlements, invoices, refund_failure_logs (schema defines them but they come in Day 16+; the InitPaymentSchema migration must NOT create these 6 tables; NOTE: invoices has a hard FK `payment_id REFERENCES payments(id)` — deferring invoices is SAFE because payments ships today and the FK resolves when invoices is created in a later Day-16+ migration; do NOT pre-create invoices or any FK stub) ; do NOT map a full-schema EF model ; db-schema/** (read-only) ; other services ; .env ; git ops |
| depends on | 15.0 |
| invariant flags | CRLF/.cs ; Money BIGINT VND (Shared.Kernel Money / Money.FromRaw, no decimals) ; immutable ledgers (wallet_transactions/platform_wallet_transactions: no update, no soft-delete) ; wallets/platform_wallets use [ConcurrencyCheck] on row_version (BSOT 9.7), NOT soft-delete ; no cross-DB FK (user_id/operator_id logical only) ; snake_case schema vietride_payment |
| acceptance | entities + EF configs match db-schema/payment-wallet/schema.sql columns/enums/constraints EXACTLY (wallets natural PK = user_id, no synthetic id; CHECK balance>=0; wallet_transactions amount>0; top_up_requests amount>=10000; unique uq_top_up_requests_vnpay_txn_ref; partial uq_payments_idempotency_key/uq_payments_vnpay_txn_ref) ; OutboxEvent mapped (reuse Shared.Persistence OutboxEvent) ; migration adds all 6 tables + outbox_events under vietride_payment ; dotnet ef database update clean, Down() reverses ; NetArchTest still green |
| source citations | db-schema/payment-wallet/schema.sql lines 19-93 (enums), 95-220 (payments/top_up_requests/wallets/wallet_transactions), 248-292 (platform_wallets/platform_wallet_transactions) ; BSOT 6 line 1077 (entity list) ; 8.7 line 1939 (TopUpRequestStatus) ; 9.7 lines 2101-2115 (optimistic lock) ; 9.10 line 2172 (vnp_TxnRef = UUID v4) |

### Task 15.2a - Inbound RabbitMQ consumer abstraction in VietRide.Shared.Messaging (NEW; blocks 15.2)
| Field | Value |
|---|---|
| stack/owner | cross-cutting (shared .NET lib) |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) - mirror NestJS RabbitMqConsumer semantics + the existing OutboxBackgroundService BackgroundService shape |
| owned files (write set) | libs/dotnet/VietRide.Shared.Messaging/Abstractions/IIntegrationEventHandler.cs (new - handler interface) ; libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqConsumerBackgroundService.cs (new - BackgroundService consuming exchange vietride.events) ; libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqConsumerOptions.cs (new - queue name + binding keys) ; libs/dotnet/VietRide.Shared.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs (add AddVietRideEventConsumer registration helper) ; tests/dotnet/VietRide.Shared.Messaging.UnitTests/** (new test project) ; libs/dotnet/VietRide.Libs.sln (add the new test project) |
| forbidden scope | apps/** (no service code - this is shared-lib only; 15.2 wires Payment) ; NO new NuGet package (RabbitMQ.Client 6.8.1 already in Directory.Packages.props line 41; Hangfire is NOT this task) ; do NOT change the publish-side seams (IEventPublisher/RabbitMqEventPublisher) or Outbox behaviour ; db-schema/** ; .env ; git ops |
| depends on | - (independent of 15.0/15.1; pure shared lib; dispatch after 15.0 to keep one-thing-at-a-time serial order, but no code dependency) |
| invariant flags | CRLF/.cs ; reuse existing IRabbitMqConnectionFactory + RabbitMqOptions (do NOT add a parallel connection abstraction) ; durable named queue payment.<purpose> bound to exchange vietride.events with binding key(s) ; manual ack (ack on handler success, nack/dead-letter on failure - NO infinite requeue loop) ; at-least-once delivery -> handlers MUST be idempotent (documented in XML doc, mirroring OutboxBackgroundService remarks) ; topic exchange vietride.events, routing-key shape <svc>.<aggregate>.<verb_past> |
| acceptance | IIntegrationEventHandler<T> constrained `where T : IIntegrationEvent` using the EXISTING marker libs/dotnet/VietRide.Shared.Messaging/Abstractions/IIntegrationEvent.cs (do NOT define a parallel marker interface) + a BackgroundService that opens a channel on IRabbitMqConnectionFactory, declares a durable queue + binds the configured key(s) to vietride.events, dispatches each message to the registered handler, manual-acks on success and dead-letters on handler exception ; AddVietRideEventConsumer DI helper registers the hosted service + options ; unit test covers ack-on-success and nack/dead-letter-on-failure dispatch ; dotnet build libs/dotnet/VietRide.Libs.sln -c Release clean ; dotnet format --verify-no-changes clean ; existing publish-side + Outbox tests still green |
| source citations | NestJS consumer semantics apps/notification/src/identity-events/identity-events.consumer.ts (durable queue per purpose, binding key, validate-then-ack/nack) ; existing seams libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqConnectionFactory.cs + RabbitMqOptions.cs ; BackgroundService shape libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs ; AGENTS.md Messaging (topic exchange vietride.events, routing key <svc>.<aggregate>.<verb_past>) ; RabbitMQ.Client pin Directory.Packages.props line 41 ; shared-lib test project convention tests/dotnet/VietRide.Shared.*.UnitTests (VietRide.Libs.sln) |

### Task 15.2 - Wallet bootstrap consumer (identity.user.created -> UPSERT wallet)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | apps/payment/src/VietRide.Payment.Application/Events/UserCreatedIntegrationEvent.cs (consumer-side DTO; mirror Identity producer payload) ; apps/payment/src/VietRide.Payment.Application/Features/Wallets/BootstrapWallet/*.cs (command + handler: idempotent UPSERT, implements the 15.2a IIntegrationEventHandler<UserCreatedIntegrationEvent>) ; apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (call AddVietRideEventConsumer from 15.2a: queue payment.wallet-bootstrap bound to identity.user.created) ; apps/payment/src/VietRide.Payment.Api/Program.cs (wire inbound messaging hosted service via the 15.2a helper) ; apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/IWalletRepository.cs + apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/WalletRepository.cs (UPSERT method) |
| forbidden scope | top-up/IPN/jobs (15.3-15.6) ; libs/dotnet/VietRide.Shared.Messaging/** (OQ-3b RESOLVED: the inbound abstraction is Task 15.2a, which lands FIRST; 15.2 only CONSUMES it from Payment - do NOT edit the shared lib here, and if the 15.2a abstraction needs a change, STOP and report) ; Identity producer code ; .env ; git ops |
| depends on | 15.0, 15.1, 15.2a |
| invariant flags | CRLF/.cs ; idempotent via UPSERT (naturally idempotent - re-deliver same userId = no-op, wallets natural PK user_id; no extra dedupe store needed) ; consume routing key identity.user.created exactly (durable queue payment.wallet-bootstrap) ; no cross-DB FK |
| acceptance | consuming identity.user.created {userId,role,email,createdAt} via the 15.2a consumer inserts a wallets row (balance 0, currency VND) or no-ops if present (UPSERT) ; re-delivery idempotent (unit/integration test) ; consumer hosted service registered through the 15.2a AddVietRideEventConsumer helper in Program.cs ; build + NetArchTest green |
| source citations | BSOT 7.3 line 1733 (identity.user.created -> Payment init Wallet UPSERT idempotent; payload {userId,role,email,createdAt}) ; Identity producer apps/identity/src/VietRide.Identity.Application/Events/UserCreatedIntegrationEvent.cs — the consumer-side DTO MUST mirror this file's field names + JSON property names character-for-character (no shared contract lib exists; a rename on the producer silently breaks the consumer) ; schema wallets lines 168-186 ; consumer abstraction = Task 15.2a (libs/dotnet/VietRide.Shared.Messaging) ; NestJS consumer reference pattern apps/notification/src/identity-events/identity-events.consumer.ts |

### Task 15.3 - VNPay client + signing + POST /v1/wallet/top-up (create TopUpRequest + redirect URL)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/payment/src/VietRide.Payment.Application/Abstractions/ExternalClients/IVnPayClient.cs ; apps/payment/src/VietRide.Payment.Infrastructure/VnPay/VnPayClient.cs + VnPayOptions.cs + HMAC-SHA512 signer ; apps/payment/src/VietRide.Payment.Application/Features/TopUps/CreateTopUp/*.cs (Command/Handler/Validator/Result) ; apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/ITopUpRequestRepository.cs + apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/TopUpRequestRepository.cs ; apps/payment/src/VietRide.Payment.Api/Controllers/WalletController.cs ; apps/payment/src/VietRide.Payment.Api/Controllers/Requests/CreateTopUpRequest.cs ; apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (register IVnPayClient + repo) ; apps/payment/src/VietRide.Payment.Api/appsettings.json (VNPay config keys, NO secrets) |
| forbidden scope | IPN handler (15.4) ; timeout job (15.5) ; NO Return URL endpoint anywhere (OQ-1 RESOLVED - FE-only) ; read endpoints (15.6) ; Gateway routes (already present) ; .env / real VNPay secrets (config via env at runtime only) ; other services ; git ops |
| depends on | 15.0, 15.1 |
| invariant flags | CRLF/.cs ; idempotency required (Idempotency-Key middleware, payment:idem:{key}) ; Money BIGINT VND, min 10,000 (WALLET_TOP_UP_AMOUNT_TOO_LOW 422 if below) ; vnp_TxnRef = UUID v4 server-side ; ApiResponse envelope ; MediatR v11 (controller -> Send, never service direct) |
| acceptance | POST /v1/wallet/top-up {amount,method:VNPAY} -> 201 {topUpRequestId,status:PENDING,paymentRedirectUrl} matching contract ; persists top_up_requests PENDING with unique vnpay_txn_ref + signed redirect URL ; amount<10000 -> 422 WALLET_TOP_UP_AMOUNT_TOO_LOW ; HMAC-SHA512 signature in redirect URL ; OpenAPI annotated ; happy-path + below-min tests ; build + format clean |
| source citations | API Contract Payment & Wallet line 1511 ; BSOT 5.9 line 1359 (WALLET_TOP_UP_AMOUNT_TOO_LOW) ; 9.10 line 2172 (vnp_TxnRef UUID v4) ; 10 env lines ~2389-2396 (VNPAY_TMN_CODE/HASH_SECRET/BASE_URL/RETURN_URL, WALLET_TOP_UP_MIN_VND) ; schema top_up_requests lines 135-163 ; 8.7 line 1939 |

### Task 15.4 - VNPay top-up IPN webhook (HMAC verify -> idempotent credit Wallet + emit event)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event (for the produced payment.wallet.credited) |
| owned files (write set) | apps/payment/src/VietRide.Payment.Api/Controllers/VnPayIpnController.cs (route POST /v1/payments/vnpay-topup-ipn) ; apps/payment/src/VietRide.Payment.Application/Features/TopUps/ConfirmTopUp/*.cs (Command/Handler: verify->credit->emit) ; apps/payment/src/VietRide.Payment.Application/Events/WalletCreditedIntegrationEvent.cs ; apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/WalletRepository.cs (atomic credit + ledger insert, optimistic lock) ; apps/payment/src/VietRide.Payment.Infrastructure/VnPay/VnPayClient.cs (verify-signature method) ; apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (Redis dedupe dep if needed) |
| forbidden scope | the booking IPN /v1/payments/vnpay-ipn (Day 16) ; timeout job (15.5) ; read endpoints (15.6) ; libs/dotnet/VietRide.Shared.Messaging/** (PRODUCE via the existing Outbox seam as Identity does - do NOT touch the shared lib; the 15.2a inbound abstraction is unrelated to this producer path) ; .env ; git ops |
| depends on | 15.1, 15.3 |
| invariant flags | CRLF/.cs ; PUBLIC endpoint (no Internal JWT, no user auth - Gateway forwards verbatim) ; HMAC-SHA512 verify, fail -> 401 PAYMENT_SIGNATURE_INVALID ; idempotent: dedupe payment:vnpay_ipn:{vnpTxnRef} (SETNX) AND guard on top_up_requests.status != PENDING -> replay returns success without double-credit (PAYMENT_ALREADY_PROCESSED semantics) ; wallet credit atomic with wallet_transactions insert + row_version optimistic lock in ONE tx ; Outbox emit payment.wallet.credited in SAME tx ; Money BIGINT VND ; Outbox routing key payment.wallet.credited ; IPN HTTP response body MUST be VNPay's machine-to-machine format (e.g. {"RspCode":"00","Message":"Confirm Success"}) and NOT the ADR 0004 ApiResponse<T> envelope - ADR 0004 governs FE-facing /v1/* responses only, not VNPay server-to-server callbacks; the controller must bypass the standard envelope for this endpoint |
| acceptance | valid IPN success (vnp_ResponseCode 00) -> TopUpRequest PENDING->SUCCEEDED, Wallet balance += amount, wallet_transactions row with correct balance_before/after (immutable), payment.wallet.credited {userId,amount,referenceType:TOP_UP,referenceId:topUpRequestId} enqueued in outbox ; replay same IPN twice = idempotent (no double credit) ; bad signature -> 401 PAYMENT_SIGNATURE_INVALID ; VNPay non-00 -> TopUpRequest FAILED (no credit) ; integration test for replay + credit ; build + format clean |
| source citations | BSOT line 891/1147 (public IPN, HMAC-SHA512, Gateway forwards) ; 7.3 line 1757 (payment.wallet.credited payload — routing key string must be EXACTLY `payment.wallet.credited` as a literal; no RoutingKeys.cs constants file exists in Shared.Messaging and this task must NOT create one) ; 9.7 lines 2107-2115 (wallet balance update pattern) ; 9.9 line 2154 (payment:vnpay_ipn:{vnpTxnRef} dedupe 24h) ; 5.9 lines 1352-1359 (PAYMENT_* / WALLET_TOP_UP_FAILED) ; 8.7 line 1939 ; BE_TIMELINE_VU Day 15 (IPN = business source of truth) |

### Task 15.5 - TopUp 15-min timeout job (Hangfire) + Hangfire CPM + server wiring
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | Directory.Packages.props (ADD <PackageVersion> for Hangfire.AspNetCore + Hangfire.PostgreSql, free MIT 1.8.x; Hangfire.Core only if a transitive pin is required - OQ-3a. **CROSS-BRANCH RULE with Day-11 Task 11.0:** before this task runs, check whether Day-11 has already MERGED to main — if yes, rebase feat/day-15-payment-wallet and SKIP the CPM edit (entries already present); if Day-11 has not merged, add the entries here and the SECOND branch to merge resolves a trivial duplicate-line conflict, keeping ONE copy of each entry with the SAME 1.8.x pins on both branches) ; apps/payment/src/VietRide.Payment.Infrastructure/Jobs/TopUpExpiredJob.cs (new) ; apps/payment/src/VietRide.Payment.Infrastructure/VietRide.Payment.Infrastructure.csproj (PackageReference, NO Version=) ; apps/payment/src/VietRide.Payment.Api/VietRide.Payment.Api.csproj (PackageReference if the Hangfire server lives in Api, NO Version=) ; apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs (AddHangfire + PostgreSql storage) ; apps/payment/src/VietRide.Payment.Api/Program.cs (Hangfire server + recurring job registration, schema vietride_payment.hangfire) ; apps/payment/src/VietRide.Payment.Application/Features/TopUps/ExpireTopUp/*.cs (Command/Handler) |
| forbidden scope | NO VNPay Return URL endpoint or controller (OQ-1 RESOLVED - FE owns the return page; do NOT create VnPayReturnController or any new endpoint not in the API contract) ; IPN handler (15.4) ; read endpoints (15.6) ; Hangfire Pro/Ace or any commercial Hangfire package (FORBIDDEN - free MIT 1.8.x only) ; pinning Hangfire with Version= on the csproj PackageReference (CPM - version lives ONLY in Directory.Packages.props) ; .env ; other services ; git ops |
| depends on | 15.1, 15.3, 15.4 |
| invariant flags | CRLF/.cs ; CPM - Hangfire version ONLY as <PackageVersion> in Directory.Packages.props, no Version= on PackageReference ; free MIT Hangfire 1.8.x (no Pro/Ace) ; Hangfire storage schema isolated to vietride_payment.hangfire (BSOT line 1131) ; timeout job idempotent (only status=PENDING + age>15min -> EXPIRED; never touches SUCCEEDED/FAILED) ; no banned deps |
| acceptance | Hangfire.AspNetCore + Hangfire.PostgreSql added as <PackageVersion> (1.8.x) in Directory.Packages.props; csproj references carry NO Version= ; Hangfire server boots against schema vietride_payment.hangfire (auto-created) ; TopUpExpiredJob (recurring) transitions top_up_requests with status=PENDING older than 15 min -> EXPIRED, leaves SUCCEEDED/FAILED untouched ; job idempotent on re-run ; pre-guard/pre-commit hooks pass (CPM + no banned deps) ; build + format clean |
| source citations | BE_TIMELINE_VU Day 15 (TopUp timeout 15 min auto-fail via Hangfire) ; BSOT Jobs line 2248 (TopUpExpiredJob — BSOT labels it "Scheduled (per TopUpRequest)"; this plan implements a RECURRING scan (status=PENDING + age>15min) instead, an intentional deviation to avoid per-item Hangfire job proliferation — /audit-day must record it in the Day-15 checklist) ; 8.7 line 1939 ; BSOT line 1131 (vietride_payment.hangfire) ; schema top_up_request_status enum line 30 (PENDING,SUCCEEDED,FAILED,EXPIRED) ; OQ-3a RESOLVED (Hangfire free MIT 1.8.x approved) ; OQ-1 RESOLVED (no backend Return URL) |

### Task 15.6 - GET /v1/wallet + GET /v1/wallet/transactions (read endpoints)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/payment/src/VietRide.Payment.Application/Features/Wallets/GetWallet/*.cs (Query/Handler/Result) ; apps/payment/src/VietRide.Payment.Application/Features/Wallets/GetWalletTransactions/*.cs (Query/Handler/Result + paging) ; apps/payment/src/VietRide.Payment.Api/Controllers/WalletController.cs (add GET actions - shared file with 15.3, see dispatch note) ; apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/WalletRepository.cs (read methods) |
| forbidden scope | mutation flows (15.2-15.5) ; cross-user reads (must scope to JWT userId) ; other services ; .env ; git ops |
| depends on | 15.1, 15.3 (shares WalletController) |
| invariant flags | CRLF/.cs ; user-scoped: balance/transactions filtered by userId from user JWT (no cross-user leak) ; ApiResponse envelope ; paged result = PagedResult<T> per BSOT 5.7 — {items,page,pageSize,totalItems,totalPages,hasNextPage,hasPreviousPage} (NOT `total`) ; MediatR v11 |
| acceptance | GET /v1/wallet -> 200 {userId,balance,currency} for the JWT user (Contract line 1537) ; GET /v1/wallet/transactions?from&to&type&page&pageSize -> 200 paged, newest first, scoped to JWT user (Contract line 1557) ; 401 without JWT ; OpenAPI annotated ; build + format clean |
| source citations | API Contract Payment & Wallet lines 1537-1563 ; schema wallets lines 168-186, wallet_transactions lines 195-220 (idx_wallet_transactions_user_id_created_at) ; BSOT envelope ADR 0004 |

## Dispatch order
1. 15.0 (baseline - blocks all) - parallel-safe: no
2. 15.2a (shared inbound-consumer abstraction; blocks 15.2) - parallel-safe: no (touches shared lib + sln)
3. 15.1 (entities + migration) after 15.0 - parallel-safe: no
4. 15.2 (bootstrap consumer) after 15.1 + 15.2a - parallel-safe: with 15.3 only (disjoint write set)
5. 15.3 (top-up create + VNPay client) after 15.1 - parallel-safe: with 15.2 only
6. 15.4 (IPN credit + event) after 15.3 - parallel-safe: no
7. 15.5 (Hangfire CPM + timeout job; NO Return URL) after 15.4 - parallel-safe: no (edits Directory.Packages.props - serialize; CROSS-BRANCH rule with Day-11 Task 11.0 applies — see Task 15.5 owned files)
8. 15.6 (read endpoints) after 15.3 - parallel-safe: no (shares WalletController.cs with 15.3; serialize after 15.3, ideally after 15.5 to avoid controller merge churn)

> Per OQ-4 RESOLVED: SERIAL execution in one tree (no-worktree policy). 15.2/15.3 are the only genuinely disjoint pair but stay serial. 15.2a lands before 15.2 because 15.2 consumes its abstraction. 15.5 touches the shared Directory.Packages.props (Hangfire CPM) so it must not run concurrently with any other CPM edit. Dependency graph: 15.0 -> {15.1, 15.2a}; 15.1 -> {15.2, 15.3}; 15.2a -> 15.2; 15.3 -> {15.4, 15.6}; 15.4 -> 15.5.

## Progress tracker
> Orchestrator bookkeeping. Informational only - NOT audit evidence. /audit-day re-verifies independently.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 15.0 | done | APPROVE | 2026-06-12 | 1 patch round; InternalJwtTokenFactory split; human verify pending |
| 15.2a | done | APPROVE | 2026-06-12 | shared-lib inbound consumer; infra started for full libs tests; human verify pending |
| 15.1 | todo | - | - | - |
| 15.2 | todo | - | - | - |
| 15.3 | todo | - | - | - |
| 15.4 | todo | - | - | - |
| 15.5 | todo | - | - | - |
| 15.6 | todo | - | - | - |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions

> ALL RESOLVED by human (Vu) 2026-06-11. Questions retained with their resolutions for the audit trail; the plan body above has been patched to match.

- OQ-0 (carry-over) - RESOLVED: No day-13/14-checklist.md exist; HEAD is the Day-12 booking seat-lock commit. Resolution: Day 15 proceeds INDEPENDENTLY on a NEW branch feat/day-15-payment-wallet off current HEAD. No merge of Day 13/14 required first. Booking<->Payment integration deferred to Day 16. (Reflected in header + Objective.)
- OQ-1 (Return URL) - RESOLVED: NO backend Return URL endpoint. VNPAY_RETURN_URL points to the FE app (https://app.vietride.app/payments/return, BSOT line 2389); FE reads the VNPay query params and polls GET /v1/wallet. IPN is the SOLE business source of truth. Do NOT add any endpoint not in the API contract. (Task 15.5 shrunk to the Hangfire timeout job only; VnPayReturnController removed from its write set + acceptance.)
- OQ-2 (event key) - RESOLVED: Use canonical routing key payment.wallet.credited (BSOT 7.3 line 1754) with referenceType=TOP_UP. The timeline's topup.succeeded is informal/wrong and must NOT be added. The Day-15 checklist (/audit-day) must record this timeline erratum. (Reflected in Contract changes.)
- OQ-3a (Hangfire) - RESOLVED/APPROVED: Add Hangfire.AspNetCore + Hangfire.PostgreSql (+ Hangfire.Core if a transitive pin is needed) as <PackageVersion> in Directory.Packages.props (CPM, no Version= on PackageReference). Pin free MIT 1.8.x; Hangfire Pro/Ace forbidden (commercial). Storage schema vietride_payment.hangfire (BSOT line 1131). CPM addition assigned to Task 15.5. (Reflected in Contract changes + Task 15.5.)
- OQ-3b (inbound consumer) - RESOLVED: Add the inbound-consumer abstraction to libs/dotnet/VietRide.Shared.Messaging as NEW Task 15.2a (blocks 15.2): event-handler interface + a BackgroundService consuming exchange vietride.events with binding key(s), durable named queue payment.<purpose>, manual ack, dead-letter on failure, reusing the existing IRabbitMqConnectionFactory/RabbitMqOptions. NO new NuGet (RabbitMQ.Client 6.8.1 already in CPM). Idempotency in 15.2 via UPSERT (naturally idempotent). Mirror apps/notification/src/identity-events/identity-events.consumer.ts. Task 15.2a owner = dotnet-worker / dotnet-reviewer, owned files under libs/dotnet/VietRide.Shared.Messaging/** + its test project. (Reflected in Tasks 15.0/15.2a/15.2/15.4, dependency graph, dispatch order.)
- OQ-4 (shared controller) - RESOLVED: Keep SERIAL execution; WalletController.cs is shared by 15.3 + 15.6 and serialized (no split). (Dispatch order unchanged - serial.)
