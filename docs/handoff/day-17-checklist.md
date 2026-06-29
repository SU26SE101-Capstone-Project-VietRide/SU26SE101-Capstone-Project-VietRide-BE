# Day 17 — Final checklist

> Produced by `/audit-day 17` AFTER all tasks are done and verification ran.
> Honest record: if verification failed but the day was closed, say so. Don't claim green.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 17 — Booking cancellation + BookingStats (Jira: SCV-90)
- **Plan**: docs/handoff/day-17-plan.md
- **Status**: ✅ READY (Day-17 scope) — _3 audit blockers + 1 gap found, FIXED, and verified end-to-end on the running stack._

> **Audit history:** the first audit pass found the day **❌ BLOCKED** — the central deliverable
> `POST /v1/bookings/{id}/cancel` returned **HTTP 500 for every request in Docker** (the new
> `OperatorServiceClient` had no Identity base URL), plus a BOARDING over-block and a zero-amount
> refund dead-letter, all masked by green `dotnet test` (tests mock `IOperatorServiceClient`). All
> three were fixed in this session and the full cancel → async refund → REFUNDED → BookingStats
> chain was driven live through the Gateway. See **Fixes applied** + **Verification run**.
> The pre-existing RAG TS/Docker failures are **unrelated to Day 17** and remain a separate
> carry-over (they block the full polyglot CI/app-stack, not the Day-17 flow).

## DoD result
- [x] ✅ **`POST /v1/bookings/{bookingId}/cancel` returns correct refundAmount/refundMethod per policy; blocks the right trip statuses.** Live E2E: `200 CANCELLED { refundAmount: 200000, refundMethod: WALLET }`; refund computed from operator `cancellationPolicy` (null policy → 100% refund). Trip-status guard now allows SCHEDULED **and BOARDING**, blocking only IN_PROGRESS/COMPLETED per v7 lines 2050/2166. Evidence: `CancelBookingCommandHandler.cs:61-156`; live cancel.
- [x] ✅ Refund calculator follows tiers, rounds to nearest dong AwayFromZero, no floor-1000; override/empty-policy/paidAmount=0 handled. Evidence: `CancellationRefundCalculator.cs:19-39`; unit tests.
- [x] ✅ **Booking → CANCELLED in-request, refund event-driven, then CANCELLED → REFUNDED.** Live E2E: booking transitioned to **REFUNDED** within ~2s of cancel; Payment consumed `booking.booking.cancelled`, credited the wallet, emitted `payment.wallet.credited`, Booking marked REFUNDED. A `PENDING_PAYMENT` cancel (refund=0) no longer dead-letters (skipped). Evidence: live poll; `BookingCancelledIntegrationEventHandler.cs` (Payment) + Booking `WalletCreditedIntegrationEventHandler`.
- [x] ✅ Payment refund consumer idempotent via the existing `RefundToWalletCommandHandler` reference guard (`BOOKING_REFUND` + bookingId) — no new de-dupe table; replayed cancel produced exactly **one** `CREDIT 200000 BOOKING_REFUND` wallet_transaction (no double-credit). Evidence: live wallet_transactions.
- [x] ✅ **BookingStats counters update within 5s, idempotent UPSERT.** Live: confirmed → `total_bookings/total_confirmed/total_revenue`++; cancelled → `total_cancelled`++; refunded → `total_refunded`+=amount; all within ~2s; durable `booking_stats_processed_events` de-dupe. Evidence: live `booking_stats` (`total_bookings=2, total_confirmed=2, total_cancelled=2, total_refunded=200000, total_revenue=400000`).
- [x] ✅ Operator/admin BookingStats read endpoints contract-shaped, tenant-scoped (operatorId from JWT), admin cross-operator SYSTEM_ADMIN, `operatorName` from snapshot column, `totalPartialNoShows` hard-coded `0`. Read queries now implement `IQuery<T>` (skip the redundant transaction). Evidence: `GetOperatorBookingStatsQuery.cs`, `GetAdminBookingStatsAggregateQuery.cs`, controllers.
- [x] ✅ Gateway proxies `/v1/operator/booking-stats` (OPERATOR_ADMIN/STAFF); `/v1/bookings` covers cancel; cancel requires `Idempotency-Key` (replay returned the same body). Evidence: `routes.ts`; gateway test 99/99; live idempotent replay.
- [~] ⚠️ **Full polyglot verification matrix:** all Day-17 tiers PASS; the only FAILs are **pre-existing RAG** (full TS CI build/test + full Docker app-stack) — unrelated to Day 17 (RAG was not touched). The Day-17 service subset was brought up and the real E2E ran green.

## Fixes applied (this session)
- **BLOCKER-1 (cancel 500 in Docker) — FIXED.** Wired the booking `OperatorServiceClient` Identity config to mirror the Trip/Payment clients: `appsettings.json` `Identity:{BaseUrl,UseDevStub:false}`; `appsettings.Development.json` `Identity:UseDevStub:true`; compose booking block `IDENTITY_SERVICE_BASE_URL`/`Identity__BaseUrl=http://identity:5001` + `Identity__UseDevStub=${BOOKING_IDENTITY_USE_DEV_STUB:-false}`. Live: cancel now constructs the real client and succeeds (previously `InvalidOperationException: Identity base URL must be configured`).
- **BLOCKER-2 (BOARDING over-block) — FIXED.** `CancelBookingCommandHandler.IsTripCancellable` now allows `SCHEDULED` **or `BOARDING`** (v7 2050/2166). Added unit test `Handle_TripBoarding_CancelsSuccessfully`.
- **BLOCKER-3 (zero-amount refund dead-letter) — FIXED.** Payment `BookingCancelledIntegrationEventHandler` returns early when `RefundAmount <= 0` (a 0-VND refund is a no-op; booking stays CANCELLED). Added unit test `HandleAsync_WhenRefundAmountIsZero_DoesNotSendRefundCommand`.
- **GAP (read queries open a transaction) — FIXED.** `GetOperatorBookingStatsQuery` + `GetAdminBookingStatsAggregateQuery` now implement `IQuery<T>` so `TransactionBehavior` skips the transaction.

## Tasks completed
- Task 17.0 — Refund/policy cross-service baseline — ✅ (config wiring completed this session).
- Task 17.1 — Cancel refund-amount calculator — ✅
- Task 17.2 — POST `/v1/bookings/{id}/cancel` endpoint — ✅ (BOARDING guard fixed).
- Task 17.3 — BookingStats entity + EF configuration + migration — ✅
- Task 17.4 — BookingStats lifecycle consumers + BSOT/API-contract doc edits — ✅
- Task 17.5 — BookingStats read endpoints — ✅ (IQuery applied).
- Task 17.6 — Gateway route `/v1/operator/booking-stats` — ✅
- Task 17.7 — Payment consumes `booking.booking.cancelled` → wallet credit — ✅ (zero-amount guard added).

## Changed files
- `apps/booking/src/VietRide.Booking.Api/appsettings.json` + `appsettings.Development.json` — Identity client config (FIX-1).
- `infra/docker/docker-compose.yml` — booking Identity base URL + UseDevStub env (FIX-1).
- `apps/booking/.../CancelBooking/CancelBookingCommandHandler.cs` — BOARDING guard (FIX-2).
- `apps/payment/.../Messaging/BookingCancelledIntegrationEventHandler.cs` — zero-amount skip (FIX-3).
- `apps/booking/.../BookingStats/{GetOperatorBookingStats,GetAdminBookingStatsAggregate}/*Query.cs` — `IQuery<T>` (GAP).
- `apps/booking/tests/.../CancelBookingCommandHandlerTests.cs` + `apps/payment/tests/.../BookingCancelledIntegrationEventHandlerTests.cs` — regression tests.
- (Day-17 feature files from the original implementation: `BACKEND_SOURCE_OF_TRUTH.md`, `VietRide_API_Contract_v1.md`, identity/booking/payment/gateway sources, migrations, `db-schema/booking/schema.sql`, `docs/handoff/day-17-plan.md` — unchanged by this fix pass except as listed above.)

## Verification run
| Command | Result | Notes |
|---|---:|---|
| `dotnet build` booking/payment `-c Release` | PASS | `0 Warning(s) 0 Error(s)` after fixes (also rebuilt as Docker images). |
| `dotnet format` booking/payment `--verify-no-changes --no-restore` | PASS | No changes. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | unit **201/201** (+BOARDING test), integration **37/37**. |
| `dotnet test apps/payment/VietRide.Payment.sln -c Release` | PASS | unit **65/65** (+zero-amount test), integration **19/19**. |
| `dotnet test` identity + libs (prior pass) | PASS | identity unit 213/213 + int 132/132; libs 80/80. |
| `dotnet ef database update` (apply/down/re-apply `AddBookingStatsProcessedEvents`) | PASS | reverses cleanly. |
| `npx nx run gateway:build/lint/test` | PASS | gateway test 99/99. |
| Day-17 service subset `up -d --build identity trip booking payment parcel gateway` | PASS | all 6 + infra healthy (real seam, UseDevStub=false). |
| `/health` matrix ports 3000/5001/5002/5003/5004/5005 | PASS | all 200. |
| **Cancel construct fix (BLOCKER-1)** — `POST /v1/bookings/{id}/cancel` live | PASS | After fix: no more "Identity base URL" 500; a non-existent trip now correctly returns `404 TRIP_NOT_FOUND` (handler constructs the real `OperatorServiceClient`). |
| **FULL real-seam cancel E2E** (real Trip seat-lock + real Payment wallet **charge** + real operator-policy lookup; operator `…009` seeded in Identity w/ tiered policy; passenger wallet funded; trip `6e8e446a`) | PASS | book `201 CONFIRMED 125000` (wallet **debited** real) → cancel `200 CANCELLED refund **112500**` (tiered **10%** fee, not trivial 100%) → **REFUNDED** ~2s → wallet `375000→487500` → txns `DEBIT 125000` + `CREDIT 112500` → stats incremented; **`operator_name="Day 17 E2E Operator"` snapshot via real Identity**. **Refund preview == credited amount.** |
| Idempotent cancel replay (same `Idempotency-Key`) | PASS | identical body; exactly one wallet credit (no double refund). |
| **Trip-status guard (all 4 states)** via real Trip snapshot | PASS | SCHEDULED → cancel 200 ✅; **BOARDING → cancel 200 ✅ (BLOCKER-2 fix)**; IN_PROGRESS → **409 BOOKING_NOT_CANCELLABLE** ✅; COMPLETED → **409** ✅ (booking stays CONFIRMED). |
| **Booking-status guard** + **owner isolation** via Gateway | PASS | cancel a REFUNDED booking → `409 BOOKING_NOT_CANCELLABLE`; cancel by non-owner → `403 FORBIDDEN`. |
| **PENDING_PAYMENT zero-refund** (BLOCKER-3 fix) via real VNPAY booking | PASS | VNPAY book → `PENDING_PAYMENT`; cancel → `200 CANCELLED refund 0`; Payment log `Skipping wallet refund … is 0`, **no dead-letter / no nack**, booking stays CANCELLED, 0 wallet credit. |
| **GET /v1/operator/booking-stats** (OPERATOR_ADMIN + OPERATOR_STAFF) via Gateway | PASS | `200` with items (`totalBookings/totalCancellations/...`, `totalPartialNoShows=0`); PASSENGER → `403` (gateway role gate). |
| **GET /v1/admin/booking-stats/aggregate** (SYSTEM_ADMIN) via Gateway | PASS | `200` cross-operator with `operatorName` from snapshot column; OPERATOR_ADMIN → `403`. |
| **Round-trip per-leg independence** (real round-trip booking) | PASS | After fixing the round-trip lock-store bug (below), drove a REAL round-trip booking: `POST /v1/bookings/round-trip` → `201` both legs CONFIRMED (grandTotal 250000, shared `bookingGroupId`). Cancelled **only the outbound leg** → outbound `CANCELLED`→`REFUNDED` (refund 112500), **return leg stays CONFIRMED**. No cascade. |
| Hard invariants (CPM/banned deps/MediatR v12/Co-Authored-By/EOL) | PASS | unchanged; new edits add no deps. |
| `npx nx run-many -t build/test --all --exclude="VietRide.*"` (full TS) | FAIL | **RAG** only (`@sentry/nestjs/setup` unresolved; rag tests). **Pre-existing, Day-17-unrelated.** |
| `docker compose --profile app up -d --build` (full stack) | FAIL | **RAG** image build (`prisma-generate-win.mjs` not in build context). **Pre-existing, Day-17-unrelated.** |

> **E2E note (honest):** every Day-17 flow above was driven against the **real seam** — the FULL cancel E2E used a **real paid booking** (real Trip seat-lock + real Payment wallet charge, no payment stub), so the refund leg released the real platform-wallet hold (no seeding). The only flow not driven via its endpoint is the **round-trip create** (to then cancel one leg): the Trip real-seam seat-lock intermittently returned `BOOKING_SEAT_UNAVAILABLE` ("Seat lock expired before booking could be confirmed"), aggravated by repeated direct seat-state manipulation during fixture setup — a Trip (Day-12/13) seam concern, not Day-17. Round-trip per-leg independence is otherwise covered (code + unit test + empirical independence observed across the E2E run).

## Contract / event / schema changes shipped
- REST: `POST /v1/bookings/{bookingId}/cancel`; `GET /v1/operator/booking-stats`; `GET /v1/admin/booking-stats/aggregate`.
- Internal REST: `GET /internal/v1/operators/{operatorId}` now includes `cancellationPolicy`.
- Gateway: `/v1/operator/booking-stats` (OPERATOR_ADMIN/STAFF).
- Events: `booking.booking.cancelled` payload `+userId`; `booking.booking.refunded` registry `{ bookingId, userId, amount }`; Payment consumes cancelled → emits `payment.wallet.credited`.
- Schema: `booking_stats` (+ nullable `operator_name`); `booking_stats_processed_events`.
- BSOT §7 (1752/1753/1754) + §13 changelog `1.17.0` — appended & verified.

## Known gaps & carry-over for Day 18
- **RAG (pre-existing, NOT Day-17):** TS build/test fails (`@sentry/nestjs/setup` unresolved) and the RAG Docker image fails (`scripts/prisma-generate-win.mjs` exists in-repo but is not copied into the Docker build context). These block the full `nx run-many` CI and the full `--profile app` stack. Fix independently — not charged to Day 17. (Not auto-fixed here as it is outside Day-17 scope; flag to the human whether to spin it off.)
- **✅ FIXED this session (Day-13 round-trip CREATE seam bug, found while verifying Day-17):** real-seam `POST /v1/bookings/round-trip` failed at the book step because `RedisRoundTripSeatLockStore` wrote each `seat_lock:{trip}:{seat}` key with a **JSON payload** value, while the shared single-trip `BookSeatsHandler.IsOwnedByAsync` (`RedisSeatLockStore`) compares that key's value to `SeatLockToken.ToString("D")` — so the comparison never matched and both legs failed to confirm (`BOOKING_SEAT_UNAVAILABLE` → "Seat lock expired before booking could be confirmed"). It never surfaced on Day 13 because that E2E used the Trip **dev stub** (`DevTripServiceClient.BookSeats` returns `true`). **Fix:** `RedisRoundTripSeatLockStore` now writes the leg's `SeatLockToken` "D" string as each seat key's value, identical to the single-trip store (`apps/trip/.../SeatLocks/RedisRoundTripSeatLockStore.cs`). Verified by the real round-trip E2E above (201 both legs → cancel one leg → independence). Trip build/format/test green (unit 201, integration 56).
  - **Regression guard (follow-up):** the in-memory Redis fake in `InternalTripsEndpointTests` does NOT execute the round-trip Lua `LockScript`, so it can't model this Redis-value bug; a faithful guard needs a **real-Redis (Testcontainers) round-trip lock→book integration test**. Recommended follow-up — a mock-based test here would give false confidence.
  - Minor (not fixed): round-trip lock rejects when the **same seat number** is used for both legs (different trips) — surfaced as `"B1, B1"` at lock time. Use distinct seat numbers, or have the lock-availability check key by `(tripId, seatNumber)`. Low priority.
- **Postman:** add the cancel + booking-stats flow to the cumulative collection (`docs/api/postman/...`) for the external reviewer.
- `totalPartialNoShows` stays a hard-coded `0` contract stub until a no-show / partial-no-show lifecycle event exists (by design).
- **Dev-DB test artifacts:** this verification left CANCELLED/REFUNDED test bookings, a seeded passenger wallet (`aaaaaaaa-…-017`), a seeded Identity operator (`…009` "Day 17 E2E Operator" + tiered policy), and `booking_stats` rows in the local dev DB; trip `6e8e446a` departure was shifted to ~now+20h. Reset via `scripts/reset-local.sh` for a clean dev DB.

## Notes for Day 18 planning
- Day-17 deliverables are complete and verified end-to-end; the day is closeable for its own scope.
- **Lesson (recurring):** green `dotnet test` masked all three bugs because the integration tests mock `IOperatorServiceClient` and never feed `amount=0`. New cross-service HTTP clients MUST have base-URL + `UseDevStub` wired in BOTH appsettings(.Development) AND the compose block, and be smoke-tested against the running stack. (Mirrors the earlier "eager consumer needs compose RabbitMq env" gap.)
- Infra + the Day-17 service subset are left running (real seam) for further verification.
</content>
