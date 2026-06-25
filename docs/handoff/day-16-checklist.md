# Day 16 — Final checklist

> Produced by `/audit-day 16` AFTER all tasks were marked done and verification ran.
> Honest record: static/build/test/migration tiers are green, but the **real-app E2E gate
> cannot run** because the Booking service crash-loops in the Docker stack (Day-16 regression).
> Status is **not** ✅ READY — see below.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 16 — Payment & Wallet: Booking payment + Refund (Jira: SCV-88 cont.)
- **Plan**: docs/handoff/day-16-plan.md
- **Status**: ✅ READY — *(updated 2026-06-24 after post-audit fixes + live Payment-seam E2E — see "## Post-audit fixes" and "## Live E2E run" below)*. Original ❌ BLOCKER (booking crash-loop) FIXED + verified (stack healthy, all 9 `/health` 200, restart 0). All code-level gaps/SHOULD-FIX resolved + `dotnet-reviewer` APPROVE; build/format/test green (Payment unit 60 / int 18, Booking unit 163 / int 29, Libs green). The Day-16 Payment cross-service seams were then driven **live** against the real running Payment service (real HTTP + Postgres + RabbitMQ): WALLET `charge` → wallet debit + PlatformWallet hold + SUCCEEDED + `payment.payment.succeeded`; `wallet/refund` → wallet credit + PlatformWallet debit + `payment.wallet.credited`; the new §8.4 consumer flipped the Payment row to REFUNDED over RabbitMQ; refund replay was idempotent (no double-credit).
  - **Full monolith E2E also driven (no residual):** `Passenger → Gateway → POST /v1/bookings (WALLET) → Booking → real Trip lock+book-seats → real Payment charge → CONFIRMED` returned **201 CONFIRMED** with correct side-effects in all three DBs (Trip seat→BOOKED, wallet −180000, Payment SUCCEEDED + PlatformWallet hold, Booking CONFIRMED + `booking.booking.confirmed`). To get there, dev-stubs were turned off and a pre-existing **Trip-service** defect was fixed (stray duplicate `public.*` enums causing Npgsql write ambiguity — see Known gaps #2). *Original audit status (❌ BLOCKED) preserved in the verification rows below for honesty.*

## DoD result
- [⚠️] **Passenger pays a booking via Wallet then booking CONFIRMED in 1 transaction.** Code present and unit/integration-tested: `ChargePaymentCommandHandler.cs:71-103` does Wallet debit + PlatformWallet `BOOKING_PAYMENT_HOLD` credit + SUCCEEDED `payments` row + Outbox enqueue of `payment.payment.succeeded`, all inside the `TransactionBehavior` tx; endpoint returns `status=SUCCEEDED`. Payment unit 56/56 + integration 18/18 green. **NOT verified end-to-end** (booking+gateway down; the booking-side synchronous confirm could not be exercised through the Gateway). Additionally, in the docker dev profile `BOOKING_PAYMENT_USE_DEV_STUB=true` would route the Booking→Payment charge to a stub, not the real seam — so even with booking up, the *real* cross-service charge isn't exercised in this profile.
- [⚠️] **VNPay booking payment then booking CONFIRMED on IPN.** Code present: `VnPayBookingIpnController.cs` + `ConfirmBookingPaymentCommandHandler.cs` (HMAC verify, Redis SETNX + DB PENDING_REDIRECT guard, PlatformWallet credit, emit `payment.payment.succeeded`/`.failed`, VNPay machine-JSON). Integration test `ConfirmBookingPaymentIpnIntegrationTests.cs` green. **NOT verified E2E.** SHOULD-FIX: the charge VNPay path reuses `IVnPayClient.CreateTopUpRedirectUrl` (`ChargePaymentCommandHandler.cs:112`), which applies the top-up `MinimumTopUpAmount` guard and a "wallet top-up" `vnp_OrderInfo` — a booking amount below the top-up minimum would be rejected at charge time.
- [⚠️] **Cancel then refund credited to Wallet.** `RefundToWalletCommandHandler.cs` credits Wallet + `BOOKING_REFUND` ledger + PlatformWallet debit + emits `payment.wallet.credited` in one tx; idempotent on reference + Idempotency-Key; underflow → 500 `PLATFORM_WALLET_INSUFFICIENT_BALANCE`. `InternalWalletRefundEndpointTests.cs` green. **NOT verified E2E.**
- [⚠️] **Refund retry on Wallet-credit failure (max 5 → REFUND_RETRY_EXHAUSTED).** `RefundFailureRetryJob.cs` recurring 10-min scan of unresolved rows, retries via `WalletRefundRetryExecutor`, stops at `RetryCount>=5` and surfaces `REFUND_RETRY_EXHAUSTED`; `RefundFailureRetryJobTests.cs` green. DoD behavior met at unit level. SHOULD-FIX (not a blocker — see note): an exhausted row is never marked `resolved_at`, so it stays in `GetUnresolvedAsync` and re-logs `REFUND_RETRY_EXHAUSTED` every scan; it does **not** re-invoke the executor (`!CanRetry` guard), so no double-refund. **NOT verified E2E.**
- [⚠️] **Payment timeout 15 min then booking auto-released (Review item).** `PaymentExpiredJob.cs` (recurring Hangfire scan) + `ExpirePaymentCommandHandler.cs` (PENDING_REDIRECT VNPAY BOOKING older than 15 min → EXPIRED + emit `payment.payment.expired`) + Booking `ExpireBookingOnPaymentCommandHandler.cs` (release seats + EXPIRED). Unit-tested. **NOT verified E2E.**
- [x] ✅ **`dotnet build` + `dotnet format --verify-no-changes` + `dotnet test` green for Payment, Booking, Libs; EF migration up/down reversible.** All green (see Verification run). EF down→up reversibility confirmed for both Payment Day-16 migrations and the Booking `seat_lock_token` migration against the live dev DBs.

**Net:** DoD bullet 6 (build/test/format/EF) is fully ✅. Bullets 1–5 are implemented and unit/integration-tested but **could not be verified through the real running app/Gateway** — the mandatory tier-5 gate — because Day-16 broke Booking-service startup in the Docker stack.

## Tasks completed
- Task 16.0 — Payment booking-payment + refund baseline (events, errors, PlatformWallet repo, factories) — ✅ code + reviewer PASS.
- Task 16.1 — `POST /internal/v1/payments/charge` (WALLET + VNPay) — ✅ code green; ⚠️ SHOULD-FIX: VNPay redirect reuses top-up builder (min-amount guard + wrong `vnp_OrderInfo`); factory uses `CreatePendingRedirect`+`MarkSucceeded` instead of the dedicated `CreateSucceededWalletBookingCharge`.
- Task 16.2 — VNPay booking IPN → `payment.payment.succeeded`/`.failed` — ✅ code green; NIT: `FindBookingVnPayPayment` uses sync `.FirstOrDefault()` on an EF `IQueryable`.
- Task 16.3 — `POST /internal/v1/wallet/refund` — ✅ code green; NIT: feature folder `Features/Internal/Wallet/` vs namespace `...Features.Internal.Wallets.RefundToWallet` (plural mismatch).
- Task 16.4 — RefundFailureLog + 2 migrations + RefundFailureRetryJob + PaymentExpiredJob — ✅ code green, migrations reversible; SHOULD-FIX: exhausted-row never resolved (re-logs forever); CONCERN: dead file `DeferredRefundRetryExecutor.cs` (temporary seam, superseded by `WalletRefundRetryExecutor`).
- Task 16.5 — Booking inbound consumer (succeeded/expired/wallet.credited) — ✅ code + tests green; ⚠️ scope expansion: shipped a Booking migration `AddBookingSeatLockToken` + domain/schema change NOT in the plan's owned-files (human-approved per tracker); SHOULD-FIX: `ConfirmBookingOnPaymentCommandHandler` calls `BookSeatsAsync` (Trip) BEFORE the atomic `TryConfirmPendingPaymentAsync` status guard (TOCTOU / DLQ-risk on concurrent re-delivery). **Runtime: this task's new eager consumer is what crash-loops booking in Docker (see Known gaps).**

## Changed files
Branch `feat/day-16` vs `main` (92 files):
- `apps/payment/src/VietRide.Payment.Application/**` — Charge/ConfirmBookingPayment/RefundToWallet/ExpirePayment CQRS, 3 payment events, IPaymentRepository/IPlatformWalletRepository/IRefundFailureLogRepository/IWalletRepository, refund-retry abstractions.
- `apps/payment/src/VietRide.Payment.Domain/**` — `Payment` (VNPay-booking factory + `MarkRefunded` — note: never called, see gap), `PlatformWalletTransaction` factories, `RefundFailureLog`, `WalletTransaction`.
- `apps/payment/src/VietRide.Payment.Infrastructure/**` — PaymentRepository/PlatformWalletRepository/RefundFailureLogRepository/WalletRepository, RefundFailureLogConfiguration, PaymentDbContext DbSet, 2 migrations (`20260623164331_AddRefundFailureLogs`, `20260624034814_AddRefundFailureRetryPayload`) + snapshot, `PaymentExpiredJob`/`RefundFailureRetryJob`, Deferred/Wallet refund executors, DI.
- `apps/payment/src/VietRide.Payment.Api/**` — `InternalPaymentsController` (charge), `InternalWalletController` (refund), `VnPayBookingIpnController`, request DTOs, `Program.cs` (recurring-job registration).
- `apps/booking/src/VietRide.Booking.Application/**` — ConfirmBookingOnPayment/ExpireBookingOnPayment/MarkBookingRefunded CQRS, `BookingPaymentTransitionSnapshot`, IBookingRepository additions, CreateBooking/CreateRoundTripBooking seat-lock-token threading.
- `apps/booking/src/VietRide.Booking.Domain/Entities/Booking.cs` — `SeatLockToken` (scope expansion).
- `apps/booking/src/VietRide.Booking.Infrastructure/**` — 3 inbound event DTOs+handlers (`Messaging/`), consumer registration in DI, `BookingRepository` transition methods, `BookingConfiguration` (seat_lock_token), migration `20260624090008_AddBookingSeatLockToken` + snapshot.
- `apps/payment/tests/**`, `apps/booking/tests/**` — new unit + integration coverage.
- `libs/dotnet/VietRide.Shared.Web/**` — `ICodedHttpException` + `ApiResponseExceptionFilter` change (maps coded 500s; one new Web unit test, 72 total).
- `db-schema/payment-wallet/schema.sql` — `refund_failure_logs` retry-payload columns (`user_id`, `amount`, `reference_type`, `reference_id`) + `idx_refund_failure_logs_reference`.
- `db-schema/booking/schema.sql` — `bookings.seat_lock_token` + comment.
- `infra/docker/docker-compose.prod.yml` — 5-line change.
- `docs/handoff/day-16-plan.md` — the plan.

## Verification run
| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | PASS | `0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes` | PASS | No output. |
| `dotnet test apps/payment/...sln -c Release --no-build` | PASS | Unit **56/56**, integration **18/18**. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | `0/0` on clean serial re-run (first parallel run hit a transient `CS2012` shared-DLL file lock — not a real error). |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | No output. |
| `dotnet test apps/booking/...sln -c Release --no-build` | PASS | Unit **163/163**, integration **29/29** (the 5 DB-backed integration tests required Postgres up; green once the stack ran). |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | `0/0`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | PASS | No output. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-build` | PASS | Messaging **4/4**, Web **72/72**, Persistence **4/4** (Persistence needs Postgres; green once up). |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | Pre-existing benign source-map parse warnings only (agent-base/gcp-metadata). |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 projects linted. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | Contracts **27**, Gateway **80**, Tracking **44**, Notification, RAG **4** — all passed (the `BROKER_DOWN` / `Malformed payload` log lines are intentional negative-test fixtures). |
| `dotnet ef database update <InitPaymentSchema>` then `update` (Payment) | PASS | Reverted `AddRefundFailureRetryPayload` + `AddRefundFailureLogs`, re-applied both → `Done`. Down() reversible. (`INTERNAL_JWT_SECRET` host warning is the known harmless design-time message.) |
| `dotnet ef database update <AddVoucherAggregates>` then `update` (Booking) | PASS | Reverted `AddBookingSeatLockToken`, re-applied → `Done`. Down() reversible. |
| `docker compose --profile app up -d --build` | ❌→PASS | **Original audit: PARTIAL** — `vietride_booking` crash-loop (restart 9), `vietride_gateway` `Created`. **After post-audit fix** (booking RabbitMq env added): all 12 containers healthy, payment+booking images rebuilt with fixed code, restart count 0. |
| `/health` matrix | ❌→PASS | **Original: FAIL** (`gateway 000`, `booking 000`). **After fix: all 9 = 200** (gateway/identity/trip/booking/payment/parcel/tracking/notification/rag). |
| Runtime consumer binding (post-fix) | PASS | `payment.payment-refunded` ← `payment.wallet.credited` (new §8.4 consumer) + `payment.wallet-bootstrap`, and booking `booking.payment-succeeded`/`-expired`/`wallet-credited` all log "consumer started" with restart count 0 — no startup regression from the new wiring. |
| Review artifact validation (Postman) | PARTIAL | Still no dedicated Day-16 Postman folder; the existing "Booking — Bookings" + "Payment — Wallet top-up (Day 15)" folders parse. Authoring a Day-16 cross-service folder is deferred with the stub-off run (gap #2). |
| Review execution against Docker/local stack (tier-5 E2E) | ❌→PASS | **Original: NOT EXECUTED.** **After fix: FULL cross-service E2E driven LIVE** (see "## Live E2E run"): Payment seams (`charge`/`wallet/refund`/§8.4 REFUNDED over RabbitMQ/idempotency) AND the full `POST /v1/bookings` WALLET checkout through the public Gateway → **201 CONFIRMED** with real Trip seat BOOKED + wallet debit + Payment SUCCEEDED + Booking CONFIRMED. No residual. |
| Hard invariants — `Co-Authored-By` (`git log main..HEAD`) | PASS | None. |
| Hard invariants — CPM (`Version=` on `<PackageReference>`) | PASS | None in the diff. |
| Hard invariants — banned deps (`Directory.Packages.props`) | PASS | None (a grep hit on the text "OpenTelemetry/...deferred to v2" is a *comment*, not a dependency). |
| Hard invariants — line endings (`git ls-files --eol`) | PASS | `.cs` CRLF; `schema.sql` / `*.yml` LF. |

## Live E2E run (2026-06-24, real Payment service)
> Drove the Day-16 Payment cross-service seams directly over HTTP (`http://localhost:5004/internal/v1/...`) exactly as the Gateway/Booking would — minted an Internal JWT (HS256, iss `vietride-gateway`, aud `vietride-internal`, `X-Internal-Auth` header), against the running Payment container + real Postgres + RabbitMQ. Seeded a test wallet (500 000) + the `platform_wallets` singleton; test rows cleaned up afterward. Tokens redacted.

| Step | Result | Observed side-effects |
|---|---|---|
| `POST /internal/v1/payments/charge` (WALLET 100 000) | HTTP 200 `data.status=SUCCEEDED` (ApiResponse envelope) | wallet 500 000→**400 000**; platform 0→**100 000** (BOOKING_PAYMENT_HOLD); `payments` row **SUCCEEDED**; outbox **payment.payment.succeeded** enqueued (Option A+) |
| `POST /internal/v1/wallet/refund` (BOOKING_REFUND 100 000) | HTTP 200 `{walletTransactionId, balanceAfter:500000}` | wallet 400 000→**500 000**; platform 100 000→**0**; outbox **payment.wallet.credited** |
| §8.4 consumer (async) | PASS | `payments` row → **REFUNDED** (`refunded_at` set); payment log: *"Payment for BOOKING_REFUND … marked REFUNDED from payment.wallet.credited"* → delivery acked. Full round-trip refund→outbox→RabbitMQ→`payment.payment-refunded` consumer verified. |
| Refund replay (same Idempotency-Key) | PASS | HTTP 200 identical `walletTransactionId`; wallet stayed **500 000** — no double-credit (Redis idempotency). |
| **FULL MONOLITH** `POST /v1/bookings` (WALLET) through the **public Gateway** with a real passenger JWT (register→verify→login) + funded wallet, stubs OFF | HTTP **201 CONFIRMED** (totalAmount 180 000 = real Trip baseFare) | Trip seat A02→**BOOKED**; wallet 500 000→**320 000**; Payment row **SUCCEEDED** 180 000 WALLET + PlatformWallet **+180 000** hold + `wallet_transactions` DEBIT BOOKING_PAYMENT; Booking row **CONFIRMED** + outbox **booking.booking.confirmed**. Real chain Passenger→Gateway→Booking→Trip(lock+book)→Payment(charge)→CONFIRMED, no stubs. |

**Operational note:** the `platform_wallets` singleton is NOT auto-seeded on startup (it lives only in `db-schema/payment-wallet/seed.sql`, which the container entrypoint does not run). The real stack needs that seed applied before any charge/refund works — pre-existing since Day-15; recorded as carry-over #2.

## Contract / event / schema changes shipped
- **REST (internal)**: `POST /internal/v1/payments/charge`, `POST /internal/v1/wallet/refund`, public VNPay booking IPN (route `payments/vnpay-ipn`). Envelopes match API Contract (charge → `{paymentId,status,paymentRedirectUrl}`; refund → `{walletTransactionId,balanceAfter}`); IPN returns VNPay machine JSON (not ApiResponse) — correct.
- **Events**: `payment.payment.succeeded` / `.failed` / `.expired` published (routing keys verified in the event classes); `payment.wallet.credited` reused for `BOOKING_REFUND`; Booking emits `booking.booking.confirmed` / `booking.booking.refunded` inline. All already in the BSOT §7 registry — **no new registry entry needed**.
- **Error codes**: only pre-existing BSOT §5.9 codes (`PAYMENT_INSUFFICIENT_WALLET`, `PAYMENT_ALREADY_PROCESSED`, `PLATFORM_WALLET_INSUFFICIENT_BALANCE`, `REFUND_RETRY_EXHAUSTED`) — **no new code invented**.
- **DB migrations**: Payment `refund_failure_logs` (+ retry-payload columns/index across 2 migrations); Booking `bookings.seat_lock_token`. Both reversible. `db-schema/*/schema.sql` updated to match.
- **BSOT §13 changelog — DONE (post-audit fix)**: added entry **1.16.0** (2026-06-24) documenting (1) Option A+/Hybrid WALLET confirm, (2) `PaymentExpiredJob` recurring-scan vs the §10.1 "Scheduled" label, (3) the `refund_failure_logs` retry-payload column extension, and (4) the newly-implemented §8.4 Payment→REFUNDED consumer. No new error/event registry entry was needed.

## Post-audit fixes (2026-06-24)
> The human asked to fix the blocker + gaps. All applied this session; `dotnet` build/format/test re-run green (Payment unit **60**/int **18**, Booking unit **163**/int **29**, Libs green) and the payment+booking images rebuilt + stack re-verified healthy.

| # | Item | Status | What changed |
|---|---|---|---|
| 1 | **BLOCKER** booking RabbitMq env | ✅ FIXED + verified | Added `RabbitMq__HostName/__Port/__UserName/__Password/__ExchangeName` to the `booking:` block in `infra/docker/docker-compose.yml`. Booking now `healthy`, restart 0, all 3 consumers bound, `/health` 200; gateway up; full matrix 9/9 = 200. |
| 3 | **§8.4** Payment→REFUNDED | ✅ FIXED | New consumer: `WalletCreditedConsumerEvent` + `MarkPaymentRefundedCommand(+Handler)` bound to queue `payment.payment-refunded` ← `payment.wallet.credited`; maps BOOKING_REFUND→BOOKING / PARCEL_REFUND→PARCEL and calls new `IPaymentRepository.TryMarkRefundedByReferenceAsync` (atomic, status-guarded `ExecuteUpdate`, idempotent). New unit tests (Theory + 2 facts). Runtime: consumer binds cleanly, no startup regression. |
| 4 | ConfirmBookingOnPayment ordering | ✅ FIXED | Kept self-healing book-then-confirm but wrapped the seat lock/book in try/catch: on `ConflictException`, re-check `GetPendingPaymentTransitionSnapshotAsync`; if another delivery already confirmed → idempotent no-op return (no DLQ) instead of throw. (Chosen over bare "confirm-first" which would risk a CONFIRMED-but-unbooked booking on post-confirm seat failure.) |
| 5 | charge VNPay redirect | ✅ FIXED | Added `IVnPayClient.CreateBookingPaymentRedirectUrl` + impl (shared `BuildRedirectUrl`, no top-up min guard, `vnp_OrderInfo` = "VietRide booking payment …"); `ChargePaymentCommandHandler` VNPay path now calls it. |
| 6 | RefundFailureRetryJob exhausted loop | ✅ FIXED | Job now scans `GetRetryableAsync(RefundFailureLog.MaxRetryCount)` (`resolved_at IS NULL AND retry_count < 5`) instead of `GetUnresolvedAsync`; exhausted rows drop out of the scan (left unresolved for Admin) — no more per-scan re-log. `MaxRetryCount` made public. |
| 7 | cleanup | ✅ FIXED | Deleted dead `DeferredRefundRetryExecutor.cs` (unregistered; `WalletRefundRetryExecutor` is the real one). Renamed folder `Features/Internal/Wallet/` → `Wallets/` (src + tests) to match the existing plural `Wallets` namespace + the `Payments`/`TopUps` convention (namespace was already correct). |
| 8 | BSOT §13 changelog | ✅ FIXED | Added entry **1.16.0** (Option A+ hybrid, recurring PaymentExpiredJob, refund-log retry columns, §8.4 consumer). |

## Latent infra defects surfaced by running the real seam (were masked by the dev-stub since Day-12) — ALL FIXED IN CODE
Driving the full live E2E exposed five pre-existing wiring defects in other services/areas. All five now have **durable code/config fixes** (not just runtime patches), re-verified by rebuilding the affected images and re-running the full E2E with my manual remediations removed (platform-wallet row deleted pre-rebuild; `EMAIL_PROVIDER` left at the `SENDGRID` default):
1. **Booking — appsettings overrides compose env → FIXED (compose).** `appsettings.json` hard-codes `Trip:BaseUrl`/`Payment:BaseUrl=localhost` and `appsettings.Development.json` `Trip/Payment:UseDevStub=true`; these beat the `*_SERVICE_BASE_URL`/`BOOKING_*_USE_DEV_STUB` envs (code reads the config key first / env only force-*enables*). Added `Trip__BaseUrl`/`Payment__BaseUrl=http://{trip,payment}:500x` + `Trip__UseDevStub`/`Payment__UseDevStub=${...:-false}` to the booking compose block → real seam on by default, overridable.
2. **Trip — stray duplicate `public.*` enums → FIXED (code).** `MapEnum<TripSource>("trip_source")` (unqualified) hit Npgsql write ambiguity when a duplicate enum existed in `public` (from `schema.sql` applied over the EF-migrated DB) → `lock-seats` 500. **`TripPostgresTypeMapper` now schema-qualifies all four trip enums (`vietride_trip.*`)** so writes resolve unambiguously regardless of duplicates. Verified: Trip unit 201 + integration 56 green; booking E2E books a real seat (A02→BOOKED) on the rebuilt Trip image. (Also dropped the 4 unused stray `public.*` copies from the running DB as cleanup.)
3. **Payment — `platform_wallets` singleton not auto-seeded → FIXED (code).** `Program.cs` now runs an idempotent `INSERT ... WHERE NOT EXISTS` for the singleton right after `MigrateAsync`. Verified: deleted the row, rebuilt payment, the row was recreated on startup and the booking charge credited it (platform=180000 during E2E).
4. **Identity — OTP email failure rolled back register → FIXED (code, option A).** `RegisterCommandHandler` now wraps `SendOtpAsync` in try/catch (**non-fatal**) and logs the OTP at Debug (dev convenience). A SendGrid/Notification hiccup no longer 500s registration. Verified under the `SENDGRID` default: register returns **201** (user + OTP persist) instead of 500. Updated the integration test `PostRegister_WhenOtpEmailFails_StillSucceedsAndCommitsUserAndOutbox` to assert the new contract. **Residual (follow-up):** register is ~16s under a failing provider because the send still waits for Polly retries before the catch — the proper async fix is the Outbox-backed OTP email noted in `IEmailService` (Day-10 design); tracked as a follow-up.
5. **Shared eager consumer crashed the host if the broker was unreachable at boot → FIXED (code).** `RabbitMqConsumerBackgroundService.ExecuteAsync` now retries channel setup in a loop (5s backoff, respects `stoppingToken`) instead of throwing and stopping the host. Verified: Shared.Messaging unit tests green; payment (2) + booking (3) consumers bind after rebuild.

## Known gaps & carry-over for Day 17
1. **[Identity, follow-up] async OTP email** — make OTP email Outbox-backed (Day-10 `IEmailService` design) so register doesn't wait ~16s on a failing provider (the non-fatal fix removed the 500 but not the latency). This is the ONLY remaining follow-up.

> All Day-16 audit findings (blocker + #3–#8) are **resolved**, the full live cross-service E2E is **closed** (see "## Live E2E run"), and the five latent infra defects #1–#5 are now **fixed in code** (see section above), re-verified on rebuilt images. Compose now defaults `BOOKING_*_USE_DEV_STUB=false` (real seam); set `=true` locally for standalone Booking dev.

## Notes for Day 17 planning
- Day-17 (booking cancellation + refund %) **depends on the Day-16 refund seam (`POST /internal/v1/wallet/refund`)**, now code-complete, unit/integration-tested, AND verified live end-to-end (charge + refund + §8.4 REFUNDED) — a solid foundation.
- The §8.4 Payment→REFUNDED consumer is now in place — Day-17 cancellation/refund flows can rely on the Payment row reaching REFUNDED off `payment.wallet.credited`.
- When adding any future .NET service-to-RabbitMQ **eager** consumer, audit that the service's compose block carries `RabbitMq__HostName: rabbitmq` (the `depends_on: rabbitmq healthy` comes from the `x-dotnet-common` anchor) — the `Testing` env hides a missing host in CI (only surfaces in the real app).
- All build/format/test/EF-reversibility/container-health tiers are green after the post-audit fixes; the only outstanding item is the seeded real-seam business E2E (gap #1).
