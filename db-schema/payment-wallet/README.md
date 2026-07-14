# Payment & Wallet Service — DB Schema

## Overview

Service xử lý **mọi giao dịch tiền**: payment VNPay/Wallet cho Booking/Parcel/Subscription, passenger wallet top-up, refund flow, **PlatformWallet holding pool + operator internal wallet + 7-day-hold trip settlement model**, subscription invoice PDF. Atomic operations + optimistic lock cho Wallet (passenger), PlatformWallet và OperatorWallet — không cho phép balance âm.

- **Database:** `vietride_payment`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`
- **Hangfire schema:** `hangfire.*` trong cùng DB. Jobs:
  - VNPay PENDING_REDIRECT EXPIRED (15 phút)
  - TopUpRequest EXPIRED (15 phút)
  - **Trip settlement eligibility flag** (daily 02:00) — `PENDING_HOLD → ELIGIBLE` khi `eligible_at <= now`
  - **Trip settlement weekly auto-settle** (Monday 09:00 weekly) — batch settle mọi `ELIGIBLE` bằng PlatformWallet DEBIT + OperatorWallet CREDIT
  - Subscription trial expire (daily 00:30)
  - Subscription PENDING_PAYMENT warn 24h + revert 7d
  - Invoice PDF reconciliation (`*/5 * * * *` UTC), five total attempts, stale PROCESSING recovery after 15 minutes
  - RefundFailureLog retry (5m, max 5 retries)

> **v1 model:** Booking/parcel revenue vào **PlatformWallet holding pool**, sau Trip terminal + 7-day hold mới settle sang **ví nội bộ operator** (`OperatorWallet`). KHÔNG có bank withdrawal trong v1 — defer sang v2.

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Payment` | Mọi giao dịch thanh toán (Booking/Parcel/TopUp/Subscription). | `referenceType`+`referenceId` polymorphic, immutable trusted `context JSONB`, `contextReconciliationRequired`, `vnpayTxnRef` UNIQUE partial, `idempotencyKey` UNIQUE partial |
| `TopUpRequest` | Passenger wallet top-up qua VNPay. | `amount` ≥ 10000, `vnpayTxnRef` UNIQUE |
| `Wallet` | Ví hành khách (1-1 với User). | **`user_id` PK** (natural, logical FK), `balance` non-negative CHECK, `row_version` optimistic lock |
| `WalletTransaction` | Ledger immutable (passenger wallet). | `user_id` (logical FK, no hard FK), `type` (CREDIT/DEBIT), `amount` positive, `balanceBefore`/`balanceAfter` snapshot |
| `Invoice` | Subscription invoice VietRide→Operator. | `paymentId` UNIQUE, `invoiceNumber` UNIQUE `VR-INV-yyyyMM-XXXXXX`, stable protected `pdfUrl`, object path, PDF status/attempt/retry timestamps |
| `PlatformWallet` | Singleton clearing/holding pool của VietRide. | `balance` non-negative, `row_version`, singleton unique index |
| `PlatformWalletTransaction` | Ledger immutable của PlatformWallet. | `referenceType` BOOKING_PAYMENT_HOLD / PARCEL_PAYMENT_HOLD / REFUND / TRIP_SETTLEMENT / SUBSCRIPTION_PAYMENT |
| `OperatorWallet` | **Ví nội bộ operator** (1-1 với Operator). Replaces former `operator_balances`. | `operator_id` PK, `balance` non-negative, `row_version` |
| `OperatorWalletTransaction` | Ledger immutable của OperatorWallet. | `type` CREDIT/DEBIT, `referenceType` TRIP_SETTLEMENT/ADJUSTMENT/SUBSCRIPTION_PAYMENT, balance snapshot |
| `OperatorTripSettlement` | Per-Trip settlement marker và settlement cùng một row. | UNIQUE `(operator_id, trip_id)`, `status` enum 4-state, `eligibleAt`, failure metadata, `row_version` |
| `OperatorLedgerEntry` | **Audit log** per booking/parcel revenue/refund. | `trip_id` nullable, `entryType` enum, `amount` signed. **KHÔNG có balance_before/after** (drop từ v1 wallet model). |
| `RefundFailureLog` | Retry tracking khi refund event consume fail. | `retryCount` ≤ 5 → admin manual |
| `OutboxEvent` | Outbox pattern. | |

## Design Decisions

### Wallet PK convention — natural key, no hard cross-service FK

3 ví trong service đều tuân pattern **natural key cho 1-1 relationship**, không dùng synthetic id:

| Wallet | PK | Lý do |
|---|---|---|
| `wallets` (passenger) | `user_id` | 1-1 với `identity.users`. Logical FK cross-service — không có hard FK vì cross-DB FK bị cấm. |
| `operator_wallets` | `operator_id` | 1-1 với `identity.operators`. Cùng pattern. |
| `platform_wallets` | synthetic `id` + `UNIQUE((TRUE))` | Singleton, không có entity domain để natural-key. |

**Hệ quả:**
- `wallet_transactions` không có hard FK tới `wallets` — chỉ match qua `user_id` (giống `operator_wallet_transactions.operator_id` không FK tới `operator_wallets`). App-layer enforce consistency: mọi INSERT `wallet_transactions` phải atomic cùng DB transaction với UPDATE `wallets.balance` + optimistic lock check.
- Bootstrap idempotent qua event: `identity.user.created` → `INSERT INTO wallets (user_id, ...) VALUES (...) ON CONFLICT (user_id) DO NOTHING`. RabbitMQ at-least-once delivery → UPSERT trên natural PK = idempotent perfect.
- Tiết kiệm 16 bytes/row + 1 index (so với synthetic id + UNIQUE(user_id)).
- EF Core config: `entity.HasKey(w => w.UserId)`; KHÔNG dùng `[Key]` attribute mặc định.

### Wallet model v1 (KHÔNG có bank transfer)

- **`platform_wallets`** là singleton holding pool nội bộ của VietRide. Nó không thay thế tài khoản ngân hàng thật; production reconciliation so sánh PlatformWallet + PassengerWallet + OperatorWallet movement với sao kê VNPay/bank.
- Booking/parcel payment:
  - VNPay payment → CREDIT PlatformWallet (`*_PAYMENT_HOLD`).
  - PassengerWallet payment → DEBIT PassengerWallet + CREDIT PlatformWallet cùng amount (chuyển liability sang holding pool, không có dòng tiền vật lý mới).
- Refund trước settlement → CREDIT PassengerWallet + DEBIT PlatformWallet; KHÔNG debit OperatorWallet.
- Settlement → DEBIT PlatformWallet + CREDIT OperatorWallet trong cùng transaction.

- **`operator_wallets`** thay thế hoàn toàn `operator_balances` cũ. Schema gần giống (1-1 với operator, balance non-negative, optimistic lock theo row_version) nhưng semantic khác:
  - **Cũ:** `operator_balances` ghi nhận "số VietRide đang nợ operator" — credit ngay khi booking SUCCEEDED, debit khi bank-transfer payout.
  - **Mới (v1):** `operator_wallets` chỉ credit khi `OperatorTripSettlement` SETTLED (sau 7-day hold + Monday auto-job hoặc admin manual trigger). v1 không có bank withdrawal → balance chỉ dùng cho dashboard view + admin adjustment.
- **Bootstrap row:** INSERT `operator_wallets { operator_id, balance=0 }` qua `identity.operator.approved` event consume (UPSERT idempotent).
- **`operator_wallet_transactions`** mirror pattern `wallet_transactions` của passenger: immutable, INSERT atomic với UPDATE `operator_wallets.balance` qua optimistic lock.
- OperatorWallet có thể DEBIT cho subscription. Transaction dùng `reference_type=SUBSCRIPTION_PAYMENT`, `reference_id=payment_id`; cùng transaction local phải CREDIT PlatformWallet `SUBSCRIPTION_PAYMENT`, insert Payment SUCCEEDED và Outbox. Partial unique indexes theo reference chặn replay double movement.
- **NO `OperatorPayoutBatch` table** — entity này đã bị drop khỏi v1 (cũ dùng cho bank-transfer flow). v2 sẽ thêm `OperatorWithdrawalRequest` cho bank withdrawal.

### Trip settlement state machine

- **`operator_trip_settlements`** 1 record per Trip per Operator (UNIQUE constraint).
- **Lifecycle:**
  ```
  Trip terminal (COMPLETED / DISRUPTED) + SUM(ledger entries for trip) > 0
    → INSERT settlement { status: PENDING_HOLD, eligible_at = terminal + 7 days }

  Hangfire daily 02:00: PENDING_HOLD + eligible_at <= now → ELIGIBLE
  Hangfire Monday 09:00: ELIGIBLE → SETTLED (atomic with PlatformWallet DEBIT + OperatorWallet CREDIT + INSERT both transaction records)
  Admin manual `POST /v1/admin/trip-settlements/{id}/settle`: PENDING_HOLD or ELIGIBLE → SETTLED (override 7d hold)
  At settle time: if recomputed netAmount <= 0 (all refunded) → CANCELLED instead of SETTLED
  ```
- **CHECK constraint** `chk_operator_trip_settlements_settled_consistency`: enforce `(status PENDING_HOLD/ELIGIBLE ↔ settled_at NULL)` và `(status SETTLED/CANCELLED ↔ settled_at + settlement_method NOT NULL)`.
- **`row_version` optimistic lock** trên status transition — chống race: Monday auto-job + admin manual cùng lúc settle 1 record. Pattern: `UPDATE ... WHERE id=:id AND status=:expected AND row_version=:original`.
- **`net_amount`** recompute tại settle time (SUM ledger entries for trip) thay vì freeze tại create — pickup late refunds trong 7-day hold window.
- Cron lưu UTC: eligibility `0 19 * * *`, weekly `0 2 * * 1`. Mỗi row settle trong transaction riêng để failure isolation.
- PlatformWallet thiếu balance: rollback, giữ ELIGIBLE, tăng failure count/timestamp/active code và retry tuần sau vô hạn. Alert HIGH khi count ≥3 **hoặc** stuck >21 ngày, Redis throttle 24h. Recovery giữ history, clear active code và set `failure_resolved_at`.
- Settlement thành công/cancelled không còn trong stuck filter. `hasSubstitution` chỉ là audit metadata đối với công thức Payment settlement.

### Invoice PDF và number allocation

- `invoice_number_counters(period_key CHAR(6) PRIMARY KEY,last_value)` dùng `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` trong cùng transaction tạo Invoice; `000001..999999`, không reuse số đã commit.
- Invoice lifecycle: `DRAFT/PENDING/attempts=0`; claim PROCESSING mới tăng attempts. Failure 1..4 dùng backoff 1/5/15/30 phút; attempt 5 terminal. Reconciliation 5 phút recover stale PROCESSING mà không cấp attempt miễn phí.
- Object path `invoices/{operatorId}/{invoiceId}.pdf`. DB chỉ lưu protected VietRide download endpoint. Signed URL sinh sau auth, TTL 60 phút, không persist/log/event.
- PDF bundle Noto Sans Regular/Bold + OFL-1.1 và custom resolver để render tiếng Việt ổn định trong Linux container.

### OperatorLedgerEntry — audit log thuần

- **Drop `balance_before` / `balance_after`** từ v1 wallet model: ledger không còn track running balance (concept "VietRide nợ operator" thay bằng "wallet balance"). Ledger chỉ là audit per-event.
- **Add `trip_id` nullable** — cần để aggregate per trip cho TripSettlement.netAmount computation. NULL cho ADJUSTMENT/MANUAL entries không gắn trip.
- **Drop enum value `PAYOUT`** trong `operator_ledger_entry_type` (entity gây ra entry này — `operator_payout_batches` — đã drop).
- **Drop enum value `PAYOUT_BATCH`** trong `operator_ledger_reference_type` (cùng lý do).
- Index `(operator_id, trip_id)` partial trên `trip_id NOT NULL` cho TripSettlement.netAmount SUM query.

### Operator hủy chuyến — KHÔNG check balance

Trước đây flow check `OperatorBalance >= refundTotal` + `OPERATOR_INSUFFICIENT_BALANCE_FOR_CANCEL` error + `POST /v1/operator/balance/top-up` endpoint. v1 wallet model **bỏ hoàn toàn** các check này vì:
- Trip CANCELLED xảy ra TRƯỚC khi terminal (COMPLETED/DISRUPTED) → KHÔNG có TripSettlement → wallet operator chưa từng được credit cho trip này.
- Refund cho passenger chảy từ PlatformWallet holding pool, KHÔNG debit OperatorWallet.
- Ledger entries BOOKING_REFUND/PARCEL_REFUND vẫn được INSERT cho audit.

### Bank account fields — schema-only, v2 enable UI

- `Operator.bankAccountName/bankAccountNumber/bankName` vẫn tồn tại nullable trên `identity-user/schema.sql` (chuẩn bị cho v2 bank withdrawal).
- v1 KHÔNG enforce nhập, KHÔNG validate format, KHÔNG có UI banner, KHÔNG có endpoint `PATCH /v1/operator/profile/bank-account`.
- Activity log action `BANK_ACCOUNT_UPDATED` đã bị drop khỏi `identity-user/schema.sql` enum (defer v2).

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `uq_payments_vnpay_txn_ref` | `vnpay_txn_ref` partial | unique | VNPay IPN dedupe |
| `uq_payments_idempotency_key` | `idempotency_key` partial | unique | Double-charge protection |
| `idx_payments_reference` | `(reference_type, reference_id)` | B-tree | "Payments for booking X" lookup |
| `idx_payments_status_created_at` | `(status, created_at)` partial | B-tree | Hangfire PENDING_REDIRECT timeout scan |
| `uq_top_up_requests_vnpay_txn_ref` | `vnpay_txn_ref` | unique | Top-up dedupe |
| `idx_top_up_requests_status_created_at` | partial PENDING | B-tree | Hangfire EXPIRED scan |
| (none — `user_id` is PK, covers wallet lookup) | — | — | — |
| `idx_wallet_transactions_user_id_created_at` | `(user_id, created_at DESC)` | B-tree | Transaction history |
| `idx_wallet_transactions_reference` | `(reference_type, reference_id)` partial | B-tree | Audit per booking/parcel |
| `uq_invoices_invoice_number` | `invoice_number` | unique | Lookup by number |
| `uq_invoices_payment_id` | `payment_id` | unique | One Invoice per successful subscription Payment |
| `pk_invoice_number_counters` | `period_key` | primary | Atomic monthly invoice sequence |
| `idx_invoices_operator_id_created_at` | `(operator_id, created_at DESC)` | B-tree | Operator invoice list |
| `uq_platform_wallets_singleton` | expression `(TRUE)` | unique | Enforce exactly one PlatformWallet row |
| `idx_platform_wallet_transactions_created_at` | `(created_at DESC)` | B-tree | Admin reconciliation list |
| `idx_platform_wallet_transactions_reference` | `(reference_type, reference_id)` partial | B-tree | Trace hold/refund/settlement movement |
| `uq_platform_wallet_transactions_subscription` | `(type, reference_type, reference_id)` partial where SUBSCRIPTION_PAYMENT | unique | Idempotent subscription credit |
| `idx_operator_wallet_transactions_operator_id_created_at` | `(operator_id, created_at DESC)` | B-tree | Operator wallet history |
| `idx_operator_wallet_transactions_reference` | `(reference_type, reference_id)` partial | B-tree | Trace credit/adjustment |
| `uq_operator_wallet_transactions_subscription` | `(operator_id, type, reference_type, reference_id)` partial where SUBSCRIPTION_PAYMENT | unique | Idempotent subscription debit |
| `uq_operator_trip_settlements_operator_trip` | `(operator_id, trip_id)` | unique | 1 settlement per trip per operator |
| `idx_operator_trip_settlements_status_eligible` | `(status, eligible_at)` partial | B-tree | **Hangfire daily 02:00 + Monday 09:00 jobs** |
| `idx_operator_trip_settlements_operator_status` | `(operator_id, status)` | B-tree | Operator dashboard "pending revenue" tab |
| `idx_operator_ledger_entries_operator_id_created_at` | `(operator_id, created_at DESC)` | B-tree | Ledger query per operator |
| `idx_operator_ledger_entries_operator_trip` | `(operator_id, trip_id)` partial | B-tree | **TripSettlement.netAmount SUM** query |
| `idx_operator_ledger_entries_reference` | `(reference_type, reference_id)` | B-tree | Audit per booking/parcel |
| `uq_operator_ledger_entries_source` | `(source_event_id, entry_type, reference_id)` | unique | Allocation/event replay dedupe |
| `idx_refund_failure_logs_unresolved` | `last_attempt_at` partial | B-tree | Hangfire retry job |
| `idx_outbox_events_status_created` | partial | B-tree | Outbox worker poll |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `Payment.userId`, `TopUpRequest.userId`, `Wallet.userId` (PK), `WalletTransaction.userId`, `RefundFailureLog.resolvedByUserId`, `OperatorTripSettlement.settledByUserId` | `identity.User.id` | app-layer |
| `Payment.operatorId`, `Invoice.operatorId`, `OperatorWallet.operatorId`, `OperatorWalletTransaction.operatorId`, `OperatorLedgerEntry.operatorId`, `OperatorTripSettlement.operatorId` | `identity.Operator.id` | app-layer |
| `Invoice.operatorSubscriptionId` | `identity.OperatorSubscription.id` | app-layer |
| `OperatorLedgerEntry.tripId`, `OperatorTripSettlement.tripId` | `trip.Trip.id` | app-layer |
| `Payment.referenceId` (polymorphic) | `booking.Booking.id` / `booking.bookingGroupId` / `parcel.Parcel.id` / `top_up_requests.id` / `identity.operator_subscriptions.id` | by `referenceType` |
| `OperatorLedgerEntry.referenceId` (polymorphic) | `booking.Booking.id` / `parcel.Parcel.id` / `booking.VoucherUsage.id` | by `referenceType` |
| `OperatorWalletTransaction.referenceId` (polymorphic) | `OperatorTripSettlement.id` (TRIP_SETTLEMENT), `Payment.id` (SUBSCRIPTION_PAYMENT), hoặc adjustment uuid (ADJUSTMENT) | by `referenceType` |
| `RefundFailureLog.bookingId/parcelId` | `booking.Booking.id` / `parcel.Parcel.id` | app-layer |

## Migration Strategy

- **Tool:** EF Core Migrations.
- **Bootstrap order:** Sau Identity, Trip-Route-Vehicle. Seed fixed singleton `platform_wallets { id='00000000-0000-0000-0000-000000000001', balance=0 }`. Khi operator được APPROVED, atomic event handler INSERT 1 row `operator_wallets { operator_id, balance=0 }` (UPSERT).
- **Operator legacy backfill:** Identity persist `operator_wallet_backfill_markers(operator_id PK,event_id UNIQUE)` và approval Outbox cùng transaction. Payload carries stable `eventId`; Payment durable inbox dedupes theo eventId. Money mutations may lazy-create balance-0 wallet with insert-on-conflict; reads never create rows.
- **Trip terminal handler:** Payment Service consume `trip.trip.completed` hoặc `trip.trip.disrupted` event → IF SUM(ledger entries cho trip) > 0 → INSERT `operator_trip_settlements { status: PENDING_HOLD, eligible_at = trip_terminal_at + 7 days }`. Pattern UPSERT trên `(operator_id, trip_id)` để idempotent với event redelivery.
- **Optimistic lock pattern:** EF Core `[ConcurrencyCheck]` hoặc `[Timestamp]` attribute trên `row_version` cho `wallets`, `platform_wallets`, `operator_wallets`, `operator_trip_settlements`.
- **Day-37 upgrade:** Phase A additive migration + dual-write context; valid legacy VNPay callback with `{}` context still settles existing money path and marks reconciliation. Backfill context through authenticated internal HTTP, then backfill missing ledger/Invoice without repeating PlatformWallet movement. Phase B enables `PaymentContext:Required`, terminal consumers and Invoice reconciliation only after readiness has no untreated legacy rows; failures are quarantined with runbook. Rollback disables gates/consumers, never drops migrated data.

## Open Questions

Không có. Section 4.6 đã được rewrite cho wallet model + 7-day hold + Monday auto-settle.

## v2 Roadmap (out of scope v1)

- **Bank Withdrawal flow** — operator request rút balance về tài khoản ngân hàng. Components: (1) UI nhập + validate bank account 3 fields đã có schema; (2) entity `OperatorWithdrawalRequest` mới với status machine PENDING/PROCESSING/COMPLETED/FAILED; (3) `OperatorWalletTransaction.referenceType` enum thêm value `WITHDRAWAL`; (4) banking API integration. Error codes: `OPERATOR_BANK_ACCOUNT_MISSING`, `OPERATOR_WALLET_INSUFFICIENT_BALANCE_FOR_WITHDRAWAL`.
- **VNPay Refund API** — refund trực tiếp về ngân hàng thay vì wallet.
- **E-invoice provider integration** (VNPT/Misa/Viettel).
- **Advanced reconciliation UI** — admin so sánh ledger với bank statement (v1 có PlatformWallet transaction list cơ bản).
