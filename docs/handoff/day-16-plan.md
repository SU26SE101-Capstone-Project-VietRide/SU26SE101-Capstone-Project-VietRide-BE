# Day 16 — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 16 — Payment & Wallet: Booking payment + Refund (Jira: SCV-88 cont.)
- **Prior checklist**: docs/handoff/day-15-checklist.md (found — Day-15 green; no blocking carry-over)
- **Plan status**: REVISION-3 (anh Vu UPGRADED OQ1 from Option A to **Option A+ / Hybrid**: Task 16.1 WALLET path ALSO publishes `payment.payment.succeeded` via Outbox in the charge tx as the canonical/recovery channel; Task 16.5 consumer broadened to the recovery path for BOTH WALLET and VNPay — confirms any PENDING_PAYMENT BOOKING, idempotent no-op + no double-emit; OQ1 + divergence note 1 rewritten to "substantially aligns with §8.1"; new divergence note 4 defers the Payment↔Booking reconciliation job to Day-17+. REVISION-2 items unchanged: 16.1 CREATES IPaymentRepository/PaymentRepository, 16.4 APPENDS the scan method, PaymentExpiredJob + SeatReleaseTimeoutJob notes) → **APPROVED** by `reviewer` (PLAN-REVIEW on REVISION-3: no blockers, no should-fix, 2 nits non-blocking) — ready to dispatch

## Objective
Day 16 makes the single-leg booking-payment seam real on the Payment side and adds the refund
machinery. It (1) adds `POST /internal/v1/payments/charge` so the existing Day-13 booking saga
stops relying on the in-process WALLET stub — wallet debit + PlatformWallet holding credit happen
atomically in one Payment DB tx; (2) adds the VNPay booking-payment redirect to IPN path that emits
canonical `payment.payment.succeeded`/`.failed`/`.expired`; (3) adds `POST /internal/v1/wallet/refund`
plus a `RefundFailureLog` table + `RefundFailureRetryJob` (max 5). This unblocks Day-17 booking
cancellation (which calls the refund seam) and finishes the Sprint-3 booking-pay-cancel money path.

## Success criteria (DoD — binary, verifiable)
- [ ] Passenger pays a booking via Wallet then booking CONFIRMED in 1 transaction. (Payment side: `charge` debits Wallet + credits PlatformWallet atomically and returns SUCCEEDED; Booking saga then confirms — verified by E2E through Gateway.)
- [ ] VNPay booking payment then booking CONFIRMED on IPN. (Redirect issued at checkout; IPN debits PlatformWallet holding, marks Payment SUCCEEDED, emits `payment.payment.succeeded`; Booking consumer flips PENDING_PAYMENT to CONFIRMED and seats HELD to BOOKED.) **Option A+ / Hybrid (OQ1 DECIDED by anh Vu):** WALLET bookings are confirmed synchronously in-request by the Day-13 saga (charge returns SUCCEEDED, saga confirms in the same HTTP call so FE gets CONFIRMED in the 201) AND the WALLET charge ALSO publishes `payment.payment.succeeded` (Task 16.1) so the event is the canonical record; the Task 16.5 consumer is the RECOVERY path for BOTH WALLET (the charged-but-not-confirmed crash window) and VNPay (normal async confirm), idempotently confirming any BOOKING still PENDING_PAYMENT. This now substantially ALIGNS with BSOT §8.1 line 1874 (event is canonical for all methods) — the only residual divergence is the synchronous happy-path confirm kept for UX. See Open Question 1.
- [ ] Cancel then refund credited to Wallet. (`POST /internal/v1/wallet/refund` credits passenger Wallet, debits PlatformWallet, emits `payment.wallet.credited` with `referenceType=BOOKING_REFUND`; idempotent on retry.)
- [ ] Refund retry on Wallet-credit failure works: a failed refund persists a `RefundFailureLog` row; `RefundFailureRetryJob` retries every 10 min, max 5, then surfaces `REFUND_RETRY_EXHAUSTED`.
- [ ] Payment timeout 15 min then booking auto-released (Review item): VNPay booking Payment in PENDING_REDIRECT for 15 min then `PaymentExpiredJob` (Payment-side recurring Hangfire scan, owned by **Task 16.4**) marks EXPIRED + emits `payment.payment.expired`; Booking consumer (Task 16.5) releases seats. (PaymentExpiredJob is implemented as a recurring Hangfire scan mirroring the Day-15 `TopUpExpiredJob` pattern — a deliberate deviation from the BSOT §10.1 line 2262 `Scheduled (per Payment)` label; see "Documented divergences" note 2 — so it is NOT scheduled at PENDING_REDIRECT creation in Task 16.1.)
- [ ] `dotnet build` + `dotnet format --verify-no-changes` + `dotnet test` green for Payment, Booking, and Libs solutions; EF migration up/down reversible on a fresh DB.

## Contract changes
- **REST (internal)** — VietRide_API_Contract_v1.md Payment and Wallet Service section:
  - `POST /internal/v1/payments/charge` (lines 1801-1828) — NEW controller action (handler/command new). Internal JWT, Idempotency required. Returns `ApiResponse<{paymentId,status,paymentRedirectUrl}>`. WALLET to SUCCEEDED; VNPAY to PENDING_REDIRECT + redirect URL.
  - `POST /internal/v1/wallet/refund` (lines 1863-1888) — NEW. Internal JWT, Idempotency required. Returns `ApiResponse<{walletTransactionId,balanceAfter}>`.
- **REST (public, FE-facing)** — booking checkout `POST /v1/bookings` already exists (Day-13). No new FE endpoint. The timeline label `POST /payments/booking` is satisfied by the Booking checkout calling the Payment `charge` seam — see Open Question 1.
- **Events** — BSOT section 7 event registry (lines 1769-1772), all already registered:
  - `payment.payment.succeeded` `{ paymentId, referenceType, referenceId, amount }` — Payment publishes, Booking consumes (CONFIRMED).
  - `payment.payment.failed` `{ paymentId, referenceType, referenceId, reason }` — Payment publishes.
  - `payment.payment.expired` `{ paymentId, referenceType, referenceId }` — Payment publishes, Booking consumes (release seats).
  - `payment.wallet.credited` `{ userId, amount, referenceType, referenceId }` — already exists (Day-15); reused for refund with `referenceType=BOOKING_REFUND`. Booking consumes (CANCELLED to REFUNDED).
  - Routing-key shape `<svc>.<aggregate>.<verb_past>` (AGENTS.md Messaging).
- **DB migration** — Payment service: add `refund_failure_logs` table (db-schema/payment-wallet/schema.sql:436-458). No other new tables; Day-15 tables already shipped.
- **Gateway routes** — already present: public `POST /v1/payments/vnpay-ipn` (apps/gateway/src/config/routes.ts:227). No route change expected; `charge`/`wallet/refund` are internal-only. Confirm no new public route needed.
- **Error codes** — all already in BSOT section 5.9: `PAYMENT_INSUFFICIENT_WALLET` (402), `PAYMENT_ALREADY_PROCESSED` (409), `PLATFORM_WALLET_INSUFFICIENT_BALANCE` (500), `REFUND_FAILURE_PERSISTED` (500), `REFUND_RETRY_EXHAUSTED` (500). No new code. If a worker finds a needed code missing, STOP — do not invent.

## Tasks

### Task 16.0 — Payment booking-payment + refund baseline (events, errors, repos, PlatformWallet wiring)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event (for the 3 payment events) |
| owned files (write set) | apps/payment/src/VietRide.Payment.Application/Events/PaymentSucceededIntegrationEvent.cs, .../Events/PaymentFailedIntegrationEvent.cs, .../Events/PaymentExpiredIntegrationEvent.cs, .../Abstractions/Repositories/IPlatformWalletRepository.cs, apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/PlatformWalletRepository.cs, apps/payment/src/VietRide.Payment.Domain/Entities/Payment.cs (add VNPay-booking factory only), apps/payment/src/VietRide.Payment.Domain/Entities/PlatformWalletTransaction.cs (add booking-hold/refund factories only) |
| forbidden scope | .env, secrets; any apps/booking, apps/trip, apps/identity, apps/parcel; apps/payment/.../Migrations/** (Task 16.4 owns the migration); existing Day-15 ConfirmTopUp/Wallet handlers; git ops |
| depends on | — |
| invariant flags | CRLF/.cs; CPM no Version=; MediatR v11; Money to-the-dong (no floor-1000, BSOT v1.11.0); Outbox routing-key svc.aggregate.verb_past; no cross-DB FK (logical only) |
| acceptance | dotnet build apps/payment/VietRide.Payment.sln -c Release + dotnet format --verify-no-changes green; 3 event classes carry exact routing keys payment.payment.succeeded/failed/expired and the BSOT section 7 payloads; IPlatformWalletRepository/impl loads the singleton (uq_platform_wallets_singleton) with optimistic lock and credit/debit; new Payment factory yields a PENDING_REDIRECT VNPAY BOOKING payment; PlatformWalletTransaction factories produce BOOKING_PAYMENT_HOLD (CREDIT) and BOOKING_REFUND (DEBIT) ledger rows; no behavior change to Day-15 paths. |
| source citations | API Contract Payment 1769-1772 (events), 1801-1828; BSOT section 7 lines 1769-1772; db-schema/payment-wallet/schema.sql:43-58 (platform_wallet enums), 246-292 (platform_wallets + ledger), 95-133 (payments); BSOT section 5.9 lines 1407-1409 |

### Task 16.1 — POST /internal/v1/payments/charge (single-leg WALLET + VNPay booking payment)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/payment/src/VietRide.Payment.Application/Features/Internal/Payments/ChargePayment/ChargePaymentCommand.cs, ChargePaymentCommandHandler.cs, ChargePaymentCommandValidator.cs, ChargePaymentResult.cs (new folder); apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/IPaymentRepository.cs (**CREATE** — absent from repo; declare at minimum AddAsync + an idempotency/duplicate-guard lookup: FindByIdempotencyKeyAsync(string) and FindByReferenceAsync(PaymentReferenceType, Guid) so the (reference_type, reference_id) duplicate guard + idempotency_key replay work; extends IRepository<Payment, Guid> — mirror ITopUpRequestRepository.cs shape); apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/PaymentRepository.cs (**CREATE** — absent from repo; implement those methods against PaymentDbContext.Payments — mirror Persistence/Repositories/TopUpRequestRepository.cs; register in the Infrastructure service-collection extension); apps/payment/src/VietRide.Payment.Api/Controllers/InternalPaymentsController.cs (add charge action only); apps/payment/src/VietRide.Payment.Api/Controllers/Requests/ChargePaymentRequest.cs; tests apps/payment/tests/VietRide.Payment.UnitTests/Features/Internal/Payments/ChargePayment/**, apps/payment/tests/VietRide.Payment.IntegrationTests/InternalPaymentsChargeEndpointTests.cs |
| forbidden scope | .env, secrets; other services; BatchChargePayment/** handler (reuse its DbContext seam pattern, do not edit it); Migrations (Task 16.4 owns the migration); the PaymentExpiredJob scan method (ExpirePendingRedirectOlderThanAsync) on IPaymentRepository/PaymentRepository — that member is APPENDED by Task 16.4, so 16.1 creates the two files with only the charge-path members above and must NOT pre-declare the scan method; git ops |
| depends on | 16.0 |
| invariant flags | CRLF/.cs; CPM; MediatR v11; Money to-the-dong; per-row payments.idempotency_key unique + payment:idem:{key} replay; ApiResponse envelope (ADR 0004); single Payment DB tx; **WALLET path ALSO enqueues payment.payment.succeeded via Outbox in the SAME tx (Option A+ / Hybrid — event is the canonical/recovery channel; routing-key svc.aggregate.verb_past = payment.payment.succeeded; same payload + Outbox shape as the Task 16.2 IPN path)** |
| acceptance | WALLET path in ONE tx: debit Wallet (PAYMENT_INSUFFICIENT_WALLET 402 if short) + credit PlatformWallet BOOKING_PAYMENT_HOLD + insert SUCCEEDED payments row + wallet_transactions BOOKING_PAYMENT debit **+ enqueue payment.payment.succeeded into the Outbox (payload `{ paymentId, referenceType, referenceId, amount }`, identical to the Task 16.2 VNPay-IPN emission) — all in the same single Payment DB transaction**; total wallet decrease equals amount; PlatformWallet balance increase equals amount; the endpoint STILL returns `status=SUCCEEDED` synchronously (the Day-13 saga continues to confirm the booking inline in the same HTTP call — this task does NOT change that). VNPAY path: insert PENDING_REDIRECT payment + return signed redirect URL, NO wallet/platform movement and NO event yet (the event is emitted on IPN by Task 16.2). Duplicate (reference_type, reference_id) returns 409 PAYMENT_ALREADY_PROCESSED (via the new FindByReferenceAsync guard) — and on the duplicate-guard short-circuit it must NOT enqueue a second payment.payment.succeeded. Idempotency-Key required (422 VALIDATION_ERROR if absent); replay safe (via FindByIdempotencyKeyAsync + payment:idem:{key}) — a replayed WALLET charge must not double-debit nor double-enqueue the event. IPaymentRepository + PaymentRepository are CREATED in this task (both absent from repo) and registered in DI; they expose AddAsync + the two lookup methods and extend IRepository<Payment, Guid>. Build + format + unit + integration tests green. |
| source citations | API Contract 1801-1828; BSOT section 8.3 line 1926 (WALLET INSERT SUCCEEDED same tx as deduct), 8.3 line 1930 (VNPay PENDING_REDIRECT to SUCCEEDED); BSOT section 7 line 1769 (payment.payment.succeeded payload `{ paymentId, referenceType, referenceId, amount }`); BSOT section 11 Outbox (enqueue event INSERT in the same tx as the state change, lines 1817-1820); db-schema/payment-wallet/schema.sql:97-133, 246-292; BSOT section 5.9 line 1407; existing BatchChargePaymentCommandHandler.cs (seam pattern to mirror) |

### Task 16.2 — VNPay booking-payment IPN to payment.payment.succeeded / payment.payment.failed
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/payment/src/VietRide.Payment.Api/Controllers/VnPayBookingIpnController.cs (route payments/vnpay-ipn); apps/payment/src/VietRide.Payment.Application/Features/Payments/ConfirmBookingPayment/ConfirmBookingPaymentCommand.cs, ConfirmBookingPaymentCommandHandler.cs, ConfirmBookingPaymentResult.cs (new folder); tests apps/payment/tests/VietRide.Payment.UnitTests/Features/Payments/ConfirmBookingPayment/**, apps/payment/tests/VietRide.Payment.IntegrationTests/Features/Payments/ConfirmBookingPayment/ConfirmBookingPaymentIpnIntegrationTests.cs |
| forbidden scope | .env, secrets; ConfirmTopUp/** (Day-15 top-up IPN — mirror its Redis-reservation + PENDING-lock idempotency pattern, do not edit it); the top-up VnPayIpnController; other services; Migrations; git ops |
| depends on | 16.0, 16.1 |
| invariant flags | CRLF/.cs; CPM; MediatR v11; Money to-the-dong; public IPN (no auth) but HMAC-verified; idempotent (Redis SETNX + DB PENDING_REDIRECT status guard); Outbox routing-key; VNPay machine-JSON response (not ApiResponse envelope) |
| acceptance | Public endpoint validates VNPay HMAC (invalid then reject like top-up IPN); on success code 00: lock the PENDING_REDIRECT BOOKING payment, mark SUCCEEDED, credit PlatformWallet BOOKING_PAYMENT_HOLD, emit payment.payment.succeeded with paymentId/referenceType/referenceId/amount via Outbox in one tx; on failure code: mark FAILED + emit payment.payment.failed with reason. Replaying the same IPN is idempotent (no double PlatformWallet credit, no duplicate event). Returns VNPay machine JSON. Build + format + tests green. |
| source citations | API Contract events 1769-1772; BSOT section 8.3 line 1930, 8.1 line 1874 (CONFIRMED trigger = payment.payment.succeeded); db-schema/payment-wallet/schema.sql:97-133; Day-15 ConfirmTopUpCommandHandler.cs (idempotency pattern); gateway routes.ts:227 (public route exists) |

### Task 16.3 — POST /internal/v1/wallet/refund (Wallet credit + PlatformWallet debit + payment.wallet.credited)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | apps/payment/src/VietRide.Payment.Api/Controllers/InternalWalletController.cs (route internal/v1/wallet, refund action); apps/payment/src/VietRide.Payment.Api/Controllers/Requests/RefundToWalletRequest.cs; apps/payment/src/VietRide.Payment.Application/Features/Internal/Wallet/RefundToWallet/RefundToWalletCommand.cs, RefundToWalletCommandHandler.cs, RefundToWalletCommandValidator.cs, RefundToWalletResult.cs (new folder); tests apps/payment/tests/VietRide.Payment.UnitTests/Features/Internal/Wallet/RefundToWallet/**, apps/payment/tests/VietRide.Payment.IntegrationTests/InternalWalletRefundEndpointTests.cs |
| forbidden scope | .env, secrets; other services; Day-15 wallet read/top-up handlers; Migrations; RefundFailureLog persistence (Task 16.4 owns the table + retry job; this task may reference the entity once 16.4 lands but must not define it); git ops |
| depends on | 16.0, 16.4 |
| invariant flags | CRLF/.cs; CPM; MediatR v11; Money to-the-dong; Internal JWT; Idempotency required; single tx; Outbox routing-key; PlatformWallet debit guarded (PLATFORM_WALLET_INSUFFICIENT_BALANCE 500) |
| acceptance | One tx: credit passenger Wallet by amount + insert wallet_transactions BOOKING_REFUND credit + debit PlatformWallet BOOKING_REFUND + emit payment.wallet.credited with userId/amount/referenceType=BOOKING_REFUND/referenceId. Returns ApiResponse with walletTransactionId + balanceAfter. Idempotent on (referenceType,referenceId) + Idempotency-Key replay (no double credit). PlatformWallet underflow returns 500 PLATFORM_WALLET_INSUFFICIENT_BALANCE. Build + format + tests green. |
| source citations | API Contract 1863-1888; BSOT section 7 line 1772 (payment.wallet.credited payload), 8.1 line 1879 (CANCELLED to REFUNDED via this event), 5.9 line 1407; db-schema/payment-wallet/schema.sql:36-58, 246-292 |

### Task 16.4 — RefundFailureLog entity + EF migration + RefundFailureRetryJob (max 5) + PaymentExpiredJob
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files (write set) | apps/payment/src/VietRide.Payment.Domain/Entities/RefundFailureLog.cs; apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Configurations/RefundFailureLogConfiguration.cs; apps/payment/src/VietRide.Payment.Infrastructure/PaymentDbContext.cs (add DbSet only); apps/payment/src/VietRide.Payment.Infrastructure/Migrations/** (new migration + snapshot); apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/IRefundFailureLogRepository.cs; apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/RefundFailureLogRepository.cs; apps/payment/src/VietRide.Payment.Infrastructure/Jobs/RefundFailureRetryJob.cs; apps/payment/src/VietRide.Payment.Infrastructure/Jobs/PaymentExpiredJob.cs (NEW — mirror Jobs/TopUpExpiredJob.cs); apps/payment/src/VietRide.Payment.Application/Features/Payments/ExpirePayment/ExpirePaymentCommand.cs, ExpirePaymentCommandHandler.cs, ExpirePaymentResult.cs (NEW folder — mirror Features/TopUps/ExpireTopUp/**; scans PENDING_REDIRECT VNPAY BOOKING payments older than 15 min, marks EXPIRED, emits payment.payment.expired via Outbox same tx); apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/IPaymentRepository.cs (APPEND the ExpirePendingRedirectOlderThanAsync scan method ONLY to the file CREATED by Task 16.1 — do NOT create the file, do NOT redefine/touch the charge-path members 16.1 added); apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/PaymentRepository.cs (APPEND the scan-method implementation ONLY to the file CREATED by Task 16.1 — do NOT redefine existing members; mirror TopUpRequestRepository.ExpirePendingOlderThanAsync ExecuteUpdateAsync shape); apps/payment/src/VietRide.Payment.Api/Program.cs (recurring-job registration only — both RefundFailureRetryJob and PaymentExpiredJob); tests apps/payment/tests/VietRide.Payment.UnitTests/.../RefundFailureRetryJobTests.cs, apps/payment/tests/VietRide.Payment.UnitTests/Features/Payments/ExpirePayment/ExpirePaymentCommandHandlerTests.cs |
| forbidden scope | .env, secrets; other services; rewriting Day-15 InitPaymentSchema migration (this must be a NEW migration); changing canonical Day-15 tables/triggers; the Day-15 TopUpExpiredJob / ExpireTopUp handler (mirror them, do NOT edit); IPaymentRepository.cs / Persistence/Repositories/PaymentRepository.cs are CREATED by Task 16.1 — this task only APPENDS ExpirePendingRedirectOlderThanAsync; do NOT create those files, do NOT redefine the charge-path members 16.1 added (depends on 16.1 landing first); Task 16.0's PaymentExpiredIntegrationEvent.cs (consume it, do NOT redefine); git ops |
| depends on | 16.0 (uses PaymentExpiredIntegrationEvent from 16.0); **16.1** (the scan method is APPENDED to the IPaymentRepository/PaymentRepository files that Task 16.1 CREATES — serial order 16.0 -> 16.1 -> 16.4) |
| invariant flags | CRLF/.cs; CPM (Hangfire PackageVersion already present from Day-15); EF migration via IDesignTimeDbContextFactory (no host boot); no cross-DB FK (booking_id/parcel_id logical FK); Money to-the-dong; Outbox routing-key (payment.payment.expired); recurring Hangfire scan (NOT per-creation delayed job) |
| acceptance | New migration creates refund_failure_logs matching schema.sql:436-458 (nullable booking_id/parcel_id, chk_refund_failure_logs_target_exists, 4 partial indexes) and is reversible (Down drops it); applies fresh-from-empty and rolls back to previous migration cleanly. RefundFailureRetryJob recurring every 10 min retries unresolved rows (resolved_at IS NULL), increments retry_count, re-invokes the refund path, and at retry_count GE 5 stops retrying + records REFUND_RETRY_EXHAUSTED (alert Admin). **PaymentExpiredJob** registered as a recurring Hangfire job (mirrors TopUpExpiredJob.RecurringJobId registration in Program.cs) that on each run sends ExpirePaymentCommand: scans PENDING_REDIRECT VNPAY BOOKING payments older than 15 min, marks them EXPIRED, and emits payment.payment.expired (paymentId/referenceType/referenceId) via Outbox in the same tx; no behavior change to Day-15 top-up expiry. Build + format + unit tests green. |
| source citations | db-schema/payment-wallet/schema.sql:434-458; BSOT section 10.1 Payment line 2271 (RefundFailureRetryJob recurring/10min/max5), line 2262 (PaymentExpiredJob = Scheduled, PENDING_REDIRECT + 15 min then EXPIRED), 5.9 lines 1408-1409; BSOT section 7 line 1771 (payment.payment.expired payload); BSOT section 11 lines 1841-1842 (refund/wallet-credit fail then RefundFailureLog); existing apps/payment/src/VietRide.Payment.Infrastructure/Jobs/TopUpExpiredJob.cs + Features/TopUps/ExpireTopUp/ExpireTopUpCommandHandler.cs + Api/Program.cs:77-80 (recurring-job registration pattern to mirror) |

### Task 16.5 — Booking inbound consumer: payment.payment.succeeded / payment.payment.expired + payment.wallet.credited
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none; wire shared inbound consumer abstraction from Day-15) |
| owned files (write set) | apps/booking/src/VietRide.Booking.Application/Features/Bookings/ConfirmBookingOnPayment/**; apps/booking/src/VietRide.Booking.Application/Features/Bookings/MarkBookingRefunded/**; apps/booking/src/VietRide.Booking.Application/Features/Bookings/ExpireBookingOnPayment/**; apps/booking/src/VietRide.Booking.Infrastructure/Messaging/** (inbound consumer registrations); the existing Booking infra service-collection extension (consumer binding only); tests apps/booking/tests/VietRide.Booking.UnitTests/.../**, apps/booking/tests/VietRide.Booking.IntegrationTests/.../PaymentEventConsumerIntegrationTests.cs |
| forbidden scope | .env, secrets; apps/payment/** (consumes events only); CreateBookingCommandHandler.cs WALLET synchronous path (do NOT rip out the Day-13 sync confirm — under Option A+/Hybrid the happy path stays synchronous for UX; this consumer is the RECOVERY path, not a replacement; see Open Question 1); Trip/Identity/Parcel; git ops |
| depends on | 16.2, 16.3 |
| invariant flags | CRLF/.cs; CPM; MediatR v11; idempotent consumer (re-delivery safe, guard on current Booking status); **payment.payment.succeeded consumer transitions ONLY when the booking is still PENDING_PAYMENT — guard the UPDATE on `WHERE status IN (PENDING_PAYMENT)` and treat an already-CONFIRMED booking as an idempotent no-op (the Option A+ recovery path covers BOTH WALLET and VNPay)**; **emit booking.booking.confirmed INLINE only when the consumer actually performs the transition — no double-emit vs the Day-13 synchronous saga**; tenant: booking lookup by id only; uses shared VietRide.Shared.Messaging inbound consumer (Day-15 abstraction); no cross-DB FK; booking.booking.confirmed AND booking.booking.refunded are emitted INLINE via IIntegrationEventOutbox.EnqueueAsync(EventType, json) following the existing Booking convention (see CreateBookingCommandHandler.cs:40,322-336) — do NOT introduce a per-event class file (Booking has no BookingRefundedIntegrationEvent.cs and inventing one would break the established inline-anonymous-payload pattern) |
| acceptance | Binds Booking queues to payment.payment.succeeded, payment.payment.expired, payment.wallet.credited on exchange vietride.events. On payment.payment.succeeded for ANY referenceType=BOOKING booking still in PENDING_PAYMENT (guard the transition on `WHERE status IN (PENDING_PAYMENT)`): book-seats (HELD to BOOKED) + confirm booking + emit booking.booking.confirmed INLINE — and do so only when the booking was actually transitioned. If the booking is already CONFIRMED, no-op (no seat re-book, no second booking.booking.confirmed). This is now the RECOVERY path for BOTH methods (Option A+/Hybrid): for WALLET it reconciles the charged-but-not-confirmed crash window (Payment debits + emits the event inside the charge tx — CreateBookingCommandHandler.cs charge at ~line 243 — while the Booking-side Confirm+outbox commits in a SEPARATE later tx at ~lines 320-336; a crash between those two commits leaves the booking PENDING_PAYMENT yet charged, and this consumer finishes the confirm), and for VNPay it is the normal async confirm. Because the Day-13 saga already CONFIRMs the WALLET happy path synchronously, the consumer is a safe no-op on the redelivered/already-confirmed common case and only fires on the crash window. On payment.payment.expired then release seats + EXPIRED. On payment.wallet.credited with referenceType=BOOKING_REFUND then CANCELLED to REFUNDED + emit booking.booking.refunded (emitted INLINE via IIntegrationEventOutbox.EnqueueAsync, NOT a new event class — see invariant flags). Re-delivery idempotent (status-guarded so no duplicate confirm/refund/release and no double-emit). Build + format + unit + integration tests green. |
| source citations | BSOT section 8.1 line 1874 (CONFIRMED = Payment publish payment.payment.succeeded -> Booking consume — Option A+ aligns: event is canonical), lines 1875-1879 (EXPIRED/REFUNDED triggers; line 1879 = CANCELLED to REFUNDED via payment.wallet.credited); BSOT section 7 line 1769 (payment.payment.succeeded payload `{ paymentId, referenceType, referenceId, amount }`); API Contract events 1769-1772; BSOT section 11 Outbox (lines 1817-1820 = enqueue INLINE in same tx); Day-15 inbound consumer abstraction in libs/dotnet/VietRide.Shared.Messaging; AGENTS.md Messaging (exchange vietride.events); existing apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs:40 (EventType), :243 (WALLET charge — Payment debit + event in Payment tx), :320-336 (Booking-side Confirm + inline EnqueueAsync in the SEPARATE Booking tx — the crash window the recovery consumer covers; inline EnqueueAsync event-emission convention, no per-event class) |

## Dispatch order
1. Task 16.0 (baseline: events, PlatformWallet repo/wiring, factories) — no deps. parallel-safe: no.
2. Task 16.1 (charge endpoint — **CREATES** IPaymentRepository + Persistence/Repositories/PaymentRepository) — depends 16.0. parallel-safe: no.
3. Task 16.4 (RefundFailureLog table + migration + retry job + PaymentExpiredJob — **APPENDS** the ExpirePendingRedirectOlderThanAsync scan method to the IPaymentRepository/PaymentRepository files created by 16.1) — depends 16.0, 16.1. parallel-safe: no.
4. Task 16.2 (VNPay booking IPN + events) — depends 16.0, 16.1. parallel-safe: no.
5. Task 16.3 (wallet/refund) — depends 16.0, 16.4. parallel-safe: no.
6. Task 16.5 (Booking inbound consumer) — depends 16.2, 16.3. Cross-service; run last. parallel-safe: no.

> **Mandated serial order in one tree: 16.0 → 16.1 → 16.4** (then 16.2, 16.3, 16.5). Task 16.1 CREATES `IPaymentRepository.cs` + `Persistence/Repositories/PaymentRepository.cs` (both absent from repo today) with only the charge-path members (AddAsync, FindByIdempotencyKeyAsync, FindByReferenceAsync). Task 16.4 then APPENDS the `ExpirePendingRedirectOlderThanAsync` scan method to those same two files for PaymentExpiredJob — so 16.1 and 16.4 share a write set and MUST run serially (16.1 before 16.4), never in parallel. 16.0 must land first because all feature tasks depend on it.

## Progress tracker
> Orchestrator bookkeeping; informational only, NOT audit evidence.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 16.0 | done | APPROVE | 2026-06-23 | Approved after 2 patch rounds; DI registration scope human-approved; pending human /verify. |
| 16.1 | done | APPROVE | 2026-06-23 | Approved after 1 patch round; advisory duplicate lock + ApiResponse envelope verified; pending human /verify. |
| 16.2 | done | APPROVE | 2026-06-24 | Approved without patch round; pending human /verify. |
| 16.3 | done | APPROVE | 2026-06-24 | Approved after schema override + 1 patch round; retry payload/real executor connected; pending human /verify. |
| 16.4 | done | APPROVE | 2026-06-24 | Approved after 2 patch rounds; temporary refund retry seam human-approved, Task 16.3 connects real executor; pending human /verify. |
| 16.5 | done | APPROVE | 2026-06-24 | Approved after scope expansion + 2 patch rounds; Booking integration full suite needs local Postgres for /verify. |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions
> OQ1 and OQ3 are CLOSED by human decision (anh Vu) this revision; OQ2 and OQ4 remain as recorded design assumptions, both already reflected in the tasks.

1. **[CLOSED — DECIDED Option A+ / Hybrid by anh Vu]** WALLET booking confirm is **synchronous in-request
   for UX AND event-backed for recovery**. (a) The Day-13 saga still confirms a WALLET booking inline in
   the HTTP request after `charge` returns SUCCEEDED, so FE receives `status=CONFIRMED` in the 201 — kept
   unchanged. (b) Task 16.1 ADDITIONALLY enqueues `payment.payment.succeeded` into the Outbox inside the
   same Payment charge transaction, making the event the **canonical/recovery channel** (identical payload
   + Outbox shape to the VNPay IPN path in Task 16.2). (c) Task 16.5's `payment.payment.succeeded` consumer
   is the **recovery path for BOTH methods**: it idempotently confirms ANY BOOKING still in PENDING_PAYMENT,
   covering the WALLET charged-but-not-confirmed crash window (Payment debits + emits the event in the
   charge tx — CreateBookingCommandHandler.cs ~line 243 — while the Booking-side Confirm+outbox commits in a
   SEPARATE later tx ~lines 320-336; a crash between those two commits otherwise leaves the booking
   PENDING_PAYMENT yet charged) as well as the normal VNPay async confirm. On the WALLET happy path the saga
   has already CONFIRMED, so the consumer is a safe no-op (status-guarded on `WHERE status IN
   (PENDING_PAYMENT)`, no double-emit of booking.booking.confirmed). *Rationale:* Day-13 already shipped and
   matches the API Contract returning `status=CONFIRMED` in the 201; layering the canonical event + recovery
   consumer on top gives crash-safety without a full event-driven rewrite or FE change. *Alignment:* this now
   substantially **aligns** with BSOT §8.1 line 1874 (CONFIRMED driven by `payment.payment.succeeded` for all
   methods — the event IS now published and consumed for WALLET too); the only residual divergence is that the
   happy-path confirm is synchronous for UX rather than waiting on the event. Recorded for a §13-changelog
   note, not a defect.
2. **[OPEN — design assumption, reflected in Task 16.1]** charge VNPay redirect for bookings. The API
   Contract `charge` response includes `paymentRedirectUrl`, and Day-13 CreateBookingCommandHandler
   already handles a VNPAY branch expecting PENDING + redirect. The plan assumes the charge endpoint
   (Task 16.1) owns booking VNPay redirect-URL generation (same as the top-up flow), not a separate
   Booking-side VNPay client. Confirm if not already implied by OQ1's Option A.
3. **[CLOSED — DECIDED out of scope for Day-16 by anh Vu]** Refund amount source. `wallet/refund`
   refunds **exactly the amount the caller (Booking) passes in** — Day-16 executes the refund only;
   it does NOT compute the cancellation-policy refund percent. The policy-percent calculation is
   **Day-17** (BE_TIMELINE Day-17 line 187). Task 16.3 takes `amount` from the request body verbatim
   and performs no percentage math.
4. **[OPEN — design assumption, reflected in Task 16.5]** Booking inbound consumer infra. Discovery
   found NO inbound RabbitMQ consumer in apps/booking/src/.../Infrastructure. Task 16.5 stands up the
   Booking-side consumer using the shared VietRide.Shared.Messaging abstraction (Day-15). Working tree
   is clean (re-verified this revision), so no half-built consumer to clobber; confirm none is planned
   out-of-band.

## Documented divergences & scope notes (for the /audit-day §13-changelog audit)
> Deliberate, decided deviations — not open questions. Listed here so the §13 changelog audit at `/audit-day` is complete.

1. **Option A+ / Hybrid WALLET confirm vs BSOT §8.1 line 1874** (cross-ref OQ1, DECIDED Option A+ by anh Vu): WALLET booking is CONFIRMED synchronously in-request for UX (FE gets CONFIRMED in the 201 via the Day-13 saga) AND `payment.payment.succeeded` is published on the WALLET charge path (Task 16.1) as the canonical event + recovery channel; the Task 16.5 consumer reconciles the charged-but-not-confirmed crash window for BOTH WALLET and VNPay. This now substantially ALIGNS with BSOT §8.1 line 1874 (the event is canonical and IS published/consumed for all methods) — the ONLY remaining divergence is that the happy-path confirm is synchronous for UX rather than waiting on the event. Flag for a §13-changelog note, not a defect.
2. **PaymentExpiredJob: recurring scan vs BSOT §10.1 line 2262 "Scheduled (per Payment)" label** — BSOT §10.1 line 2262 labels `PaymentExpiredJob` as `Scheduled (per Payment)` (a per-creation delayed job at PENDING_REDIRECT + 15 min). Task 16.4 instead implements it as a **recurring Hangfire scan** (ExpirePaymentCommand over PENDING_REDIRECT VNPAY BOOKING payments older than 15 min), deliberately mirroring the Day-15 `TopUpExpiredJob` recurring-scan pattern (§10.1 line 2263 also labels TopUpExpiredJob `Scheduled (per TopUpRequest)` yet Day-15 shipped it recurring). This is an intentional implementation-pattern deviation for consistency with the shipped Payment-service expiry mechanism — flag for a §13-changelog note, not a defect; it is NOT an open question.

3. **Booking SeatReleaseTimeoutJob (BSOT §10.1 line 2242) is deferred to Day-17+ and is NOT part of Task 16.5** — Day-16 handles booking expiry purely event-driven via the Task 16.5 `payment.payment.expired` consumer (release seats + Booking → EXPIRED, idempotent / no-op on an already-EXPIRED booking); the Booking-side scheduled `SeatReleaseTimeoutJob` (`Scheduled (per Booking)`, §10.1 line 2242) is out of Day-16 scope and to be planned in Day-17 or later.

4. **Payment↔Booking reconciliation job is DEFERRED to Day-17+ (belt-and-suspenders for a permanently-unprocessable event)** — Under Option A+/Hybrid (OQ1), the normal WALLET crash window (Payment charged but Booking-side Confirm tx not yet committed) is covered for Day-16 by the Outbox **at-least-once** delivery of `payment.payment.succeeded` plus the **idempotent, status-guarded** Task 16.5 recovery consumer. A standing Payment↔Booking reconciliation/sweep job — the safety net for an event that is published but never successfully consumed (e.g. permanently poison-messaged, or a Booking row that can never transition) — is intentionally OUT of Day-16 scope and deferred to Day-17+ (alongside the deferred Booking `SeatReleaseTimeoutJob`, note 3). Not an open question; recorded as a deliberate scope boundary.
