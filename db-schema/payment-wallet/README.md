# Payment & Wallet Service — DB Schema

## Overview

Service xử lý **mọi giao dịch tiền**: payment VNPay/Wallet cho Booking/Parcel/Subscription, passenger wallet top-up, refund flow, **PlatformWallet holding pool + operator internal wallet + 7-day-hold trip settlement model**, subscription invoice PDF. Atomic operations + optimistic lock cho Wallet (passenger), PlatformWallet và OperatorWallet — không cho phép balance âm.

- **Database:** `vietride_payment`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`
- **Hangfire schema:** `hangfire.*` trong cùng DB. Jobs:
  - VNPay PENDING_REDIRECT EXPIRED tại `due_at ?? created_at + 15 phút`
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
| `Payment` | Mọi giao dịch thanh toán (Booking/Parcel/TopUp/Subscription). | `referenceType`+`referenceId` polymorphic, authoritative nullable `dueAt`, immutable trusted `context JSONB`, `contextReconciliationRequired`, `vnpayTxnRef` UNIQUE partial, `idempotencyKey` UNIQUE partial |
| `TopUpRequest` | Passenger wallet top-up qua VNPay. | `amount` ≥ 10000, `vnpayTxnRef` UNIQUE |
| `Wallet` | Ví hành khách (1-1 với User). | **`user_id` PK** (natural, logical FK), `balance` non-negative CHECK, `row_version` optimistic lock |
| `WalletTransaction` | Ledger immutable (passenger wallet). | `user_id` (logical FK, no hard FK), `type` (CREDIT/DEBIT), `amount` positive, `balanceBefore`/`balanceAfter` snapshot |
| `Invoice` | Subscription invoice VietRide→Operator. | `paymentId` UNIQUE, `invoiceNumber` UNIQUE `VR-INV-yyyyMM-XXXXXX`, stable protected `pdfUrl`, object path, PDF status/attempt/retry timestamps |
| `PlatformWallet` | Singleton clearing/holding pool của VietRide. | `balance` non-negative, `row_version`, singleton unique index |
| `PlatformWalletTransaction` | Ledger immutable của PlatformWallet. | `referenceType` BOOKING_PAYMENT_HOLD / PARCEL_PAYMENT_HOLD / REFUND / TRIP_SETTLEMENT / SUBSCRIPTION_PAYMENT |
| `OperatorWallet` | **Ví nội bộ operator** (1-1 với Operator). Replaces former `operator_balances`. | `operator_id` PK, `balance` non-negative, `row_version` |
| `OperatorWalletTransaction` | Ledger immutable của OperatorWallet. | `type` CREDIT/DEBIT, `referenceType` TRIP_SETTLEMENT/ADJUSTMENT/SUBSCRIPTION_PAYMENT, balance snapshot |
| `OperatorTripSettlement` | Per-Trip settlement marker và settlement cùng một row. | UNIQUE `(operator_id, trip_id)`, `status` enum 4-state, `eligibleAt`, failure metadata, `row_version` |
| `OperatorLedgerEntry` | **Audit log** per booking/parcel revenue/refund. | `trip_id` nullable, `entryType` enum, `amount` signed, `adjustmentReason` có kiểu rõ ràng cho mọi `ADJUSTMENT`. **KHÔNG có balance_before/after** (drop từ v1 wallet model). |
| `RefundFailureLog` | Retry tracking khi refund event consume fail. | `retryCount` ≤ 5 → admin manual |
| `OutboxEvent` | Outbox pattern. | |
| `OutboxDlq` | Terminal Outbox failures for admin review. | unique `eventId`, payload, retry metadata, `terminalAt` |

## Design Decisions

### Authoritative Payment deadline

- `payments.due_at` áp dụng cho mọi payment session, không chỉ Subscription. Booking VNPay lưu exact
  Trip seat-lock expiry; round-trip lưu deadline sớm hơn của hai leg. Parcel lưu deadline nghiệp vụ
  của deposit/final flow.
- Expiry dùng inclusive boundary `effective_due_at <= now`, với
  `effective_due_at = due_at ?? created_at + interval '15 minutes'`. Fallback chỉ phục vụ legacy
  rows thiếu deadline; nó không kéo dài seat lock Booking.
- Existing indexes giữ nguyên trong repair này. `idx_payments_status_created_at` tiếp tục hỗ trợ
  legacy fallback scan; `idx_payments_subscription_due_at` vẫn là index riêng cho Subscription.
  Không thêm migration/index.
- Valid VNPay capture sau local expiry vẫn phải được ghi nhận đúng một lần. Booking đã release seat
  không được hồi sinh; captured allocation không confirm được phải refund idempotent về ví.

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
- Admin financial reads expose nullable operator/settled-by snapshots. Legacy rows are filled by
  the bounded `payment.financial-projection-backfill` job and keep a bounded Identity read fallback
  until their resolution markers are complete. Manual settlement stores the verified SYSTEM_ADMIN
  actor snapshot in the same local transaction; automated settlement has no user actor.
- `platform_wallet_transactions.actor_type` is `USER` for authenticated manual writes and `SYSTEM`
  for jobs/events. Actor display fields are nullable historical snapshots with no cross-database FK.
- Payment consumes `identity.user.deleted`, stores a durable `deleted_financial_actor_markers`
  tombstone, and atomically redacts settlement/platform actor PII. The consumer and manual writes
  share a PostgreSQL advisory lock; backfill uses `snapshot_resolved = FALSE` compare-and-set so
  deletion wins every race and replay remains idempotent.
- **Lifecycle:**
  ```
  Trip terminal (COMPLETED / DISRUPTED)
    → INSERT/UPSERT exactly one settlement { status: PENDING_HOLD, eligible_at = terminal + 7 days }

  Hangfire daily 02:00: PENDING_HOLD + eligible_at <= now → ELIGIBLE
  Hangfire Monday 09:00: ELIGIBLE → SETTLED (atomic with PlatformWallet DEBIT + OperatorWallet CREDIT + INSERT both transaction records)
  Admin manual `POST /v1/admin/trip-settlements/{id}/settle`: PENDING_HOLD or ELIGIBLE → SETTLED (override 7d hold)
  At settle time: if recomputed netAmount <= 0 (all refunded) → keep the marker and set CANCELLED;
  create no PlatformWallet/OperatorWallet movement and publish no settlement event
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

- `ADJUSTMENT` bắt buộc có `adjustment_reason`; entry type khác bắt buộc để null. `note` chỉ phục vụ hiển thị/audit và không được dùng làm điều kiện tính tiền.
- `reference_code` lưu `BookingCode`/`ParcelCode` để đối soát; `occurred_at` lưu thời điểm payment/refund thực tế. Hai field nullable để đọc được JSON và ledger cũ; API dùng `created_at` làm fallback cho `occurred_at` và trả `dataCompleteness=PARTIAL`.
- `operator_funded_voucher_amount` là metadata dương chỉ dành cho `VOUCHER_OPERATOR_FUNDED_AUDIT`. `amount` của entry này vẫn bằng `0`, không được parse `note` và không làm thay đổi net entitlement lần thứ hai.
- `VIETRIDE_FUNDED_VOUCHER_REVERSAL`: số âm, reference `BOOKING` hoặc `PARCEL`; đây là adjustment duy nhất được tính vào doanh thu.
- `GENERIC_BOOKING_REFUND_ENTITLEMENT`: số 0, reference `BOOKING`; marker kỹ thuật, không phải doanh thu.
- `MANUAL_WALLET_ADJUSTMENT`: số khác 0, reference `MANUAL`; không phải doanh thu.
- `LEGACY_UNCLASSIFIED`: chỉ dành cho dữ liệu cũ chưa phân loại; application không được tạo mới và mọi truy vấn doanh thu phải loại bỏ.

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
| `idx_payments_status_created_at` | `(status, created_at)` partial | B-tree | Hangfire PENDING_REDIRECT legacy fallback scan |
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
| `idx_operator_ledger_entries_operator_reference_code` | `(operator_id, reference_code)` partial | B-tree | Tìm mã Booking/Parcel trong phạm vi nhà xe |
| `idx_operator_ledger_entries_operator_occurred_at` | `(operator_id, occurred_at DESC)` partial | B-tree | Lọc ledger theo thời điểm nghiệp vụ |
| `idx_operator_ledger_entries_reference` | `(reference_type, reference_id)` | B-tree | Audit per booking/parcel |
| `uq_operator_ledger_entries_source` | `(source_event_id, entry_type, reference_id)` | unique | Allocation/event replay dedupe |
| `idx_refund_failure_logs_unresolved` | `last_attempt_at` partial | B-tree | Hangfire retry job |
| `idx_outbox_events_status_created` | partial | B-tree | Outbox worker poll |
| `uq_outbox_dlq_event_id` | `event_id` | unique | One terminal row per event |
| `idx_outbox_dlq_terminal_event_id` | `(terminal_at, event_id)` | B-tree | Composite cursor review theo contract |

`refund_failure_logs.reference_type=BOOKING_REFUND_PAYMENT` phân biệt retry exact captured-payment:
`reference_id` là `payment_id` (không phải `booking_id`) và `amount=0` hợp lệ cho allocation
fully voucher-funded. Các discriminator generic `BOOKING_REFUND` / `PARCEL_REFUND` vẫn yêu cầu
amount dương. Đây là contract logic trên schema hiện có, không thêm migration/index.

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
- **Trip terminal handler:** Payment Service consume `trip.trip.completed` hoặc `trip.trip.disrupted`
  event → INSERT/UPSERT `operator_trip_settlements { status: PENDING_HOLD, eligible_at =
  trip_terminal_at + 7 days }` regardless of the initial ledger sum. Exactly one marker always
  exists per `(operator_id, trip_id)`, with UPSERT idempotency for event redelivery. Initial
  zero-net may terminalize that marker immediately during terminal-event handling/eligibility
  refresh. A marker still `PENDING_HOLD|ELIGIBLE` is recomputed and may become `CANCELLED` during
  manual or weekly settlement; every zero-net path creates no wallet movement or settlement event.
- **Optimistic lock pattern:** EF Core `[ConcurrencyCheck]` hoặc `[Timestamp]` attribute trên `row_version` cho `wallets`, `platform_wallets`, `operator_wallets`, `operator_trip_settlements`.
- **Day-37 upgrade:** Phase A additive migration + dual-write context; valid legacy VNPay callback with `{}` context still settles existing money path and marks reconciliation. Backfill context through authenticated internal HTTP, then backfill missing ledger/Invoice without repeating PlatformWallet movement. Phase B enables `PaymentContext:Required`, terminal consumers and Invoice reconciliation only after readiness has no untreated legacy rows; failures are quarantined with runbook. Rollback disables gates/consumers, never drops migrated data.

## Open Questions

Không có. Section 4.6 đã được rewrite cho wallet model + 7-day hold + Monday auto-settle.

## v2 Roadmap (out of scope v1)

- **Bank Withdrawal flow** — operator request rút balance về tài khoản ngân hàng. Components: (1) UI nhập + validate bank account 3 fields đã có schema; (2) entity `OperatorWithdrawalRequest` mới với status machine PENDING/PROCESSING/COMPLETED/FAILED; (3) `OperatorWalletTransaction.referenceType` enum thêm value `WITHDRAWAL`; (4) banking API integration. Error codes: `OPERATOR_BANK_ACCOUNT_MISSING`, `OPERATOR_WALLET_INSUFFICIENT_BALANCE_FOR_WITHDRAWAL`.
- **VNPay Refund API** — refund trực tiếp về ngân hàng thay vì wallet.
- **E-invoice provider integration** (VNPT/Misa/Viettel).
- **Advanced reconciliation UI** — admin so sánh ledger với bank statement (v1 có PlatformWallet transaction list cơ bản).
