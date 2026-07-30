# Parcel Service — DB Schema

## Overview

Quản lý **parcel lifecycle full**: tạo request, deposit + re-weigh + additional charge, EXTRA_LARGE operator review, load/transit/unload, email link delivery confirmation (token TTL 48h), Vehicle Substitution transfer flow, return + return-to-sender. Tham chiếu logical FK đến Identity (User/Operator), Trip-Route-Vehicle (Trip/Route/Stop), Payment (Payment cho additional charge).

- **Database:** `vietride_parcel`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`
- **Hangfire schema:** `hangfire.*` trong cùng DB. Jobs: undo-reject 15m, EXTRA_LARGE auto-reject 24h, PENDING auto-reject 30m sau IN_PROGRESS, PENDING_ADDITIONAL_PAYMENT timeout (5m), PENDING_TRANSFER_CONFIRM 30m escalation, Day-32 cargo-recovery replay (5m), PENDING_OPERATOR_ACTION 2h re-alert, DELIVERED_PENDING_CONFIRM 7-day re-alert (daily 9am).

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Parcel` | Hàng ký gửi (40+ field). | `parcelCode` UNIQUE, `senderUserId` NOT NULL, `recipientUserId` nullable, `dropoffStopId` nullable, `sizeCategory` enum, deposit/additional pricing, transfer/return/review fields, full status machine |
| `ParcelDeliveryToken` | Lịch sử token xác nhận giao hàng chỉ lưu hash. | `tokenHash` UNIQUE, tối đa một token chưa revoke mỗi Parcel, expiry/revocation/issuer/reason |
| `ParcelCargoRecoveryOperation` | Durable Day-32 transfer/return orchestration history. | Stable UUID-v4 Trip key, frozen source/target/refund facts, `PENDING|COMPLETED|FAILED`, one pending operation per Parcel |
| `ParcelRouteFare` | Operator config giá per route per size. | composite PK `(routeId, sizeCategory)`, future-dated effective window |
| `ParcelStats` | Counter table per operator per day. | UNIQUE `(operatorId, statDate)` |
| `PlatformParcelStats` | Projection Day 42 theo từng Parcel `DELIVERY_CONFIRMED`. | `parcelId`, `operatorId`, `confirmedAt`, signed `parcelRevenueVnd` |
| `SystemConfig` | Versioned global Parcel logistics configuration. | `key`, `decimalValue`, `version`, effective window |
| `OperatorDepositPolicy` | Operator/route-scoped deposit policy. | `operatorId`, optional `routeId`, `depositPercent`, effective window |
| `OutboxEvent` | Outbox. | |
| `OutboxDlq` | Terminal Outbox failures for admin review. | unique `eventId`, payload, retry metadata, `terminalAt` |
| `IntegrationInbox` | Durable consumer idempotency. | UNIQUE `(consumerName, messageId)`, payload hash |

## Design Decisions

- **`parcels` table có 40+ field** — không tách thành multiple entity (ParcelReview, ParcelTransfer, ParcelDelivery, ParcelReturn) vì:
  - 1-1 relationship strict (mỗi parcel có ≤ 1 review, ≤ 1 transfer attempt active, ≤ 1 delivery confirmation, ≤ 1 return).
  - Lifecycle nested trong status machine; tách entity tạo phức tạp app-layer.
  - v6 entity requirements (Section 8 + 6.6) liệt kê tất cả field trên cùng Parcel entity.
- **`parcels.parcel_code` UNIQUE** (full unique) — QR scan lookup; format `VRP-yyyyMMdd-XXXXXXXX`.
- **`parcel_delivery_tokens` chỉ lưu SHA-256 hash** — raw UUID v4 chỉ tồn tại trong request runtime gửi Notification; `token_hash` unique và partial unique `parcel_id WHERE revoked_at IS NULL` đảm bảo tối đa một token active mỗi Parcel.
- **`parcels.sender_user_id NOT NULL`** — spec yêu cầu sender phải có account (no walk-in).
- **`parcels.recipient_email` nullable** — hỗ trợ hybrid delivery confirmation (email link nếu có email; manual confirm bởi staff nếu không).
- **`parcels.dropoff_stop_id` nullable** — null = terminal, not null = along-route Stop (validate `allowDropoff=true` app-layer).
- **`parcels.status` enum** với 22 value, gồm cả compatibility states `PENDING` và `PENDING_ADDITIONAL_PAYMENT`. Mọi transition validate ở handler.
- **`parcels` 1 mega-table thay vì split** — query "parcel detail page" lấy 1 row đủ; tránh N+1.
- **2 CHECK constraints** cho weight: `estimated_weight_kg > 0` (bắt buộc), `actual_weight_kg > 0 OR NULL`.
- **`parcels` indexes nặng vào status + updated_at partial** — Hangfire scan các state cần processing (PENDING_*, DELIVERED_PENDING_CONFIRM, TRANSFER_*, DELIVERY_REJECTED) hiệu quả qua composite index.
- **`parcels.additional_payment_deadline` index riêng** với partial `status = 'PENDING_ADDITIONAL_PAYMENT'` — Hangfire timeout job 5m interval scan rất hẹp.
- **`parcel_route_fares.operator_id` denormalized** — operator filter cho dashboard "fares của tôi" không cần cross-service JOIN. Maintain consistency app-layer khi Route đổi operator (rất hiếm).
- **`parcel_route_fares` composite PK `(route_id, size_category)`** — natural key; 1 route có ≤ 4 fare entry (4 size category).
- **NO junction table cho parcel review** — `review_decision`/`reviewed_at`/`reviewed_by_user_id` nullable trên Parcel. Chỉ EXTRA_LARGE dùng (3 field còn lại NULL cho SMALL/MEDIUM/LARGE).
- **NO junction cho parcel transfer history** — `transfer_target_trip_id`/`transfer_requested_at`/`transfer_confirmed_at`/`transfer_confirmed_by_user_id` snapshot 1 lần transfer cuối; nếu cần audit nhiều transfer (parcel chuyển 3 lần) thì query OutboxEvent.
- **Transfer confirmation durable claim** — `transfer_confirmation_claim_id` là stable UUID-v4 Idempotency-Key khi gọi Trip; `claimed_at/by_user_id` cho stale-claim recovery. Claim được giữ nguyên khi outcome không xác định và không chứa token/secret.
- **Day-32 cargo recovery uses a dedicated history table** — transfer and return persist their
  stable Trip idempotency identity and frozen facts before external I/O. A partial unique index on
  `parcel_id WHERE status='PENDING'` makes transfer-versus-return mutually exclusive; unknown
  outcomes are replayed without minting a new key.
- **`platform_parcel_stats`** được trigger đồng bộ cùng transaction và job `parcel.platform-stats-backfill` rebuild idempotent từ earned live; platform report chỉ cache sau khi projection khớp live theo operator/range.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `uq_parcels_parcel_code` | `parcel_code` | unique | QR scan |
| `uq_parcel_delivery_tokens_token_hash` | `token_hash` | unique | Email link lookup bằng SHA-256 hash |
| `uq_parcel_delivery_tokens_active_parcel` | `parcel_id` partial | unique | Tối đa một token chưa revoke mỗi Parcel |
| `idx_parcel_delivery_tokens_expires_at_active` | `expires_at` partial | B-tree | Quét re-alert token active đã hết hạn |
| `uq_parcel_cargo_recovery_operations_active_parcel` | `parcel_id` partial | unique | At most one pending Day-32 recovery operation per Parcel |
| `idx_parcel_cargo_recovery_operations_stale` | `(claimed_at, id)` partial | B-tree | Five-minute replay scan with stable ordering |
| `idx_parcels_sender_user_id_created_at` | `(sender_user_id, created_at DESC)` | B-tree | "My sent parcels" |
| `idx_parcels_recipient_user_id_created_at` | `(recipient_user_id, created_at DESC)` partial | B-tree | "My received parcels" |
| `idx_parcels_trip_id_status` | `(trip_id, status)` | B-tree | Trip detail page (parcels of trip) |
| `idx_parcels_operator_id_status` | `(operator_id, status)` | B-tree | Operator dashboard list |
| `idx_parcels_status_updated_at` | `(status, updated_at)` partial | B-tree | Hangfire scan all transient states |
| `idx_parcels_additional_payment_deadline` | `additional_payment_deadline` partial | B-tree | 5m timeout job |
| `idx_parcels_transfer_target_trip_id` | partial | B-tree | "Parcels awaiting confirm on this trip" |
| `idx_parcels_transfer_confirmation_claimed_at` | `transfer_confirmation_claimed_at` partial | B-tree | Recover stale durable transfer claims |
| `idx_parcel_route_fares_operator_id` | `operator_id` | B-tree | Dashboard fare list |
| `uq_parcel_stats_operator_date` | `(operator_id, stat_date)` | unique | Counter upsert |
| `idx_platform_parcel_stats_confirmed_operator` | `(confirmed_at, operator_id)` | B-tree | Exact UTC range reconciliation |
| `idx_outbox_events_status_created` | partial | B-tree | Outbox poll |
| `uq_outbox_dlq_event_id` | `event_id` | unique | One terminal row per event |
| `idx_outbox_dlq_terminal_event_id` | `(terminal_at, event_id)` | B-tree | Composite cursor review theo contract |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `Parcel.senderUserId/recipientUserId/reviewedByUserId/confirmedByUserId/transferConfirmedByUserId/transferConfirmationClaimedByUserId/returnedByUserId`, `ParcelDeliveryToken.issuedByUserId`, `ParcelCargoRecoveryOperation.actorUserId` | `identity.User.id` | app-layer |
| `Parcel.operatorId`, `ParcelRouteFare.operatorId`, `ParcelStats.operatorId`, `ParcelCargoRecoveryOperation.operatorId` | `identity.Operator.id` | app-layer + tenant filter |
| `Parcel.tripId`, `Parcel.transferTargetTripId`, `ParcelCargoRecoveryOperation.sourceTripId/targetTripId` | `trip.Trip.id` | app-layer |
| `Parcel.dropoffStopId` | `trip.Stop.id` | app-layer validate `allowDropoff=true` |
| `ParcelRouteFare.routeId` | `trip.Route.id` | app-layer |
| `Parcel.additionalPaymentId` | `payment.Payment.id` | app-layer |

## Migration Strategy

- **Tool:** EF Core Migrations.
- **Bootstrap order:** Sau Identity, Trip-Route-Vehicle, Payment & Wallet (logical FK targets).
- **Status enum migration:** Add new value via `ALTER TYPE parcel_status ADD VALUE 'X'` (PG ≥ 9.1 supports inline).

## Open Questions

Không có. Section 6.6 + Section 8 đã spec đầy đủ.
