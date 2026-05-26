# Payment & Wallet — ERD Drawing Guide

> Hướng dẫn vẽ relation lines manually trong draw.io sau khi mở `schema.drawio`.
>
> **v1 model:** Booking/parcel revenue vào PlatformWallet holding pool, sau đó settle sang ví nội bộ operator (OperatorWallet). KHÔNG có bank withdrawal (defer v2).

## Statistics
- **Total tables:** 13
- **Total intra-service FK (hard DB FK):** 2 — Invoice→Payment, OperatorTripSettlement→OperatorWalletTransaction
- **Logical 1:N via shared column (no hard FK):** Wallet→WalletTransaction (qua `user_id`), OperatorWallet→OperatorWalletTransaction (qua `operator_id`) — mirror pattern.
- **Hub tables (≥3 inbound FK):** không có (service này chủ yếu reference cross-service)
- **Leaf tables (no inbound FK):** `TopUpRequest`, `PlatformWallet`, `PlatformWalletTransaction`, `OperatorWallet`, `OperatorLedgerEntry`, `RefundFailureLog`, `OutboxEvent`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Passenger payment (top-left) | `Payment`, `TopUpRequest`, `Wallet`, `WalletTransaction` | trên-trái |
| Platform holding pool (center-left) | `PlatformWallet`, `PlatformWalletTransaction` | giữa-trái |
| Operator wallet & settlement (center) | `OperatorWallet`, `OperatorWalletTransaction`, `OperatorTripSettlement`, `OperatorLedgerEntry` | trung tâm |
| Subscription billing (top-right) | `Invoice` | trên-phải |
| Reliability (bottom) | `RefundFailureLog`, `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 — Wallet ↔ WalletTransaction (logical 1:N, NO hard FK)

| # | From | To | Cardinality | Note |
|---|---|---|---|---|
| 1 | `WalletTransaction.userId` | `Wallet.userId` (PK) | N:1 | **NO hard DB FK** — match qua `user_id` (mirror `OperatorWallet`/`OperatorWalletTransaction`). App-layer enforce: INSERT transaction atomic với UPDATE wallet (optimistic lock theo `row_version`). Trong drawio dùng dashed line nếu muốn visualize. |

### Phase 2 — Invoice ↔ Payment

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 2 | `Invoice.paymentId` | `Payment.id` | 1:1 | RESTRICT; 1 invoice per subscription payment |

### Phase 3 — Operator wallet ledger ↔ settlement

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 3 | `OperatorTripSettlement.walletTransactionId` | `OperatorWalletTransaction.id` | N:1 | SET NULL; set sau khi settled — link settlement event đến wallet credit transaction |

> Note: KHÔNG có FK trực tiếp từ `OperatorWalletTransaction.operator_id` đến `OperatorWallet.operator_id` ở DB level (mặc dù logical 1:1). Pattern thay thế: app-layer enforce — INSERT transaction luôn atomic với UPDATE wallet (cùng DB transaction, optimistic lock theo `row_version`).

### Phase 4 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

- `Payment.userId/operatorId`, `TopUpRequest.userId`, `Wallet.userId`, `Invoice.operatorId/operatorSubscriptionId`, `OperatorWallet.operatorId`, `OperatorWalletTransaction.operatorId`, `OperatorLedgerEntry.operatorId/tripId`, `OperatorTripSettlement.operatorId/tripId/settledByUserId`, `RefundFailureLog.resolvedByUserId` → Identity / Trip-Route-Vehicle Service
- `Payment.referenceId` (polymorphic): bookingId | bookingGroupId | parcelId | topUpRequestId | operatorSubscriptionId → tùy `referenceType`
- `OperatorLedgerEntry.referenceId` (polymorphic): bookingId | parcelId | voucherUsageId
- `OperatorWalletTransaction.referenceId` (polymorphic): tripSettlementId (TRIP_SETTLEMENT) hoặc adjustment uuid (ADJUSTMENT)
- `RefundFailureLog.bookingId/parcelId` → Booking / Parcel Service

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Service này coupling thấp ở DB layer** — chỉ 3 intra-service FK. Phần lớn relationship qua logical FK (cross-service polymorphic reference). PlatformWalletTransaction không có FK tới PlatformWallet vì PlatformWallet là singleton enforced bằng unique expression index.
2. **Polymorphic reference** — `Payment.reference_id`, `OperatorLedgerEntry.reference_id`, `OperatorWalletTransaction.reference_id`: KHÔNG vẽ line đến entity cụ thể; chỉ note trong drawing rằng `(reference_type, reference_id)` quyết định target.
3. **`OperatorWallet.operator_id` PK** (1-1 với Operator) — note "PK" + "logical FK to Operator" trong drawing.
4. **WalletTransaction → Wallet** là line ngắn nhất, vẽ trước (trên-trái).
5. **Invoice → Payment** là 1:1 — show cardinality 1..1 ở cả 2 đầu.
6. **OperatorTripSettlement → OperatorWalletTransaction** — line cardinality N:1 nullable (SET NULL khi settled, NULL trước settled).
7. **Cluster platform wallet** — đặt PlatformWallet cạnh OperatorWallet, PlatformWalletTransaction phía trên/trái; note "singleton holding pool".
8. **Cluster operator wallet** (center) — đặt OperatorWallet ở giữa cluster, OperatorWalletTransaction phía trên, OperatorTripSettlement phía dưới (chuỗi: settlement → platform txn + wallet txn → wallet).

## Validation Checklist

- [ ] 3 intra-service FK có line
- [ ] PlatformWallet có note singleton + unique expression index
- [ ] OperatorWallet hiển thị PK = operator_id (1-1 with Operator note)
- [ ] Polymorphic reference (Payment.reference_id, OperatorLedgerEntry.reference_id, OperatorWalletTransaction.reference_id) note rõ trong drawing
- [ ] Mọi BIGINT money column có comment "VND, non-negative" trong README
- [ ] **KHÔNG còn** OperatorPayoutBatch box hoặc OperatorBalance box (đã xóa khỏi v1)
