# Parcel Service — DB Schema

## Overview

Quản lý **parcel lifecycle full**: tạo request, deposit + re-weigh + additional charge, EXTRA_LARGE operator review, load/transit/unload, email link delivery confirmation (token TTL 48h), Vehicle Substitution transfer flow, return + return-to-sender. Tham chiếu logical FK đến Identity (User/Operator), Trip-Route-Vehicle (Trip/Route/Stop), Payment (Payment cho additional charge).

- **Database:** `vietride_parcel`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`
- **Hangfire schema:** `hangfire.*` trong cùng DB. Jobs: undo-reject 15m, EXTRA_LARGE auto-reject 24h, PENDING auto-reject 30m sau IN_PROGRESS, PENDING_ADDITIONAL_PAYMENT timeout (5m), PENDING_TRANSFER_CONFIRM 30m escalation, Day-32 cargo-recovery replay (5m), PENDING_OPERATOR_ACTION 2h re-alert, DELIVERED_PENDING_CONFIRM 7-day re-alert (daily 9am), Parcel incident search expiry (15m).

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Parcel` | Hàng ký gửi (40+ field). | `parcelCode` UNIQUE, `senderUserId` NOT NULL, `recipientUserId` nullable, `dropoffStopId` nullable, `sizeCategory` enum, deposit/additional pricing, delivery-confirmation, transfer/return/review fields, full status machine |
| `ParcelDeliveryToken` | Lịch sử token xác nhận giao hàng chỉ lưu hash. | `tokenHash` UNIQUE, tối đa một token chưa revoke mỗi Parcel, expiry/revocation/issuer/reason |
| `ParcelCargoRecoveryOperation` | Durable Day-32 transfer/return orchestration history. | Stable UUID-v4 Trip key, frozen source/target/refund facts, `PENDING|COMPLETED|FAILED`, one pending operation per Parcel |
| `ParcelTransitLeg` | Một chặng vật lý của Parcel trên một Trip. | Sequence bất biến, expected/actual endpoints, vehicle snapshot, forwarding/multi-leg status |
| `ParcelCustodyEvent` | Chain of custody append-only. | Expected/actual location, Trip/vehicle, actor role, evidence reference, source, idempotency key, sequence |
| `ParcelStopDepartureApprovalRequest` | Phê duyệt cho xe rời stop khi reconciliation còn kiện unresolved. | Snapshot Parcel IDs, reason, Assistant requester và Driver/Operator reviewer lấy từ JWT |
| `ParcelCurrentCustody` | Projection vị trí đã xác nhận gần nhất. | Last location/time, current Trip/vehicle, `CONFIRMED_SCAN|MANUAL_EXCEPTION|INFERRED_FROM_MANIFEST|UNKNOWN` |
| `ParcelIncident` | Vụ việc missing/wrong-stop/unscanned/not-received/damage. | Search deadline, last known location, operational breach, recovery/loss state |
| `ParcelSearchTask` | Checklist điều tra giao cho crew/station/operator. | Type/location/assignee/deadline/result/evidence/completedAt |
| `ParcelClaim` | Claim do sender sở hữu, snapshot policy và award. | Rate/cap/fallback/version, nullable legacy proof status, direct loss, awards, decision actor/time và payout reference bất biến |
| `ParcelClaimAppeal` | Hồ sơ appeal riêng, không thay đổi trạng thái tài chính của claim gốc. | Nullable legacy proof status, original award, revised award, supplementary delta, reviewer và payout bổ sung |
| `ParcelClaimEvidence` | Chứng từ claim. | Invoice/receipt/payment proof/photo/serial/biên bản reference và uploader |
| `ParcelClaimDecisionEvidence` | Liên kết evidence được chấp nhận cho quyết định claim. | Composite FK đúng claim, reviewer/time snapshot; trigger chặn update/delete |
| `ParcelClaimAppealDecisionEvidence` | Liên kết evidence claim được chấp nhận cho quyết định appeal. | Composite FK đúng appeal/claim/evidence, reviewer/time snapshot; trigger chặn update/delete |
| `ParcelCompensationPolicy` | Active versioned policy per operator. | Default 50%/30m, legacy multiplier metadata (không dùng cho award mới), claim/search/decision/payout SLA |
| `UnidentifiedParcelPackage` | Kiện không đọc được QR ở station. | Temporary tag, location, description/weight/evidence, matched Parcel audit |
| `ParcelStatusHistory` | Dòng thời gian trạng thái bất biến do trigger sở hữu. | `status`, `occurredAt`, `actorType`, `actorId`, `source`, `reason` |
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
- **Sáu `trip_snapshot_*` nullable** — lưu cố định route, tên bến và xe tại lúc tạo Parcel để UI hiển thị ổn định khi dữ liệu Trip/Route/Vehicle về sau đổi hoặc bị soft-delete. Migration không gọi Trip; job `parcel.trip-display-snapshot-backfill` xử lý tối đa 100 Parcel mỗi lần bằng một batch API, ghi nguyên tuple với CAS và không bịa dữ liệu khi Trip thiếu.
- **`parcels.status` enum** với 22 value, gồm cả compatibility states `PENDING` và `PENDING_ADDITIONAL_PAYMENT`. Mọi transition validate ở handler.
- **`parcel_status_history` bất biến và do trigger ghi** — mọi câu `UPDATE` đổi `parcels.status`, kể cả EF bulk update và raw SQL, tạo đúng một dòng. Migration chỉ ghi một `MIGRATION_BASELINE` theo trạng thái hiện tại của Parcel cũ tại thời điểm migration; không dựng lại transition lịch sử. Parcel mới không có dòng lúc `INSERT`, chỉ có history từ transition thật đầu tiên. `actor_type` là `USER`/`RECIPIENT` khi có bằng chứng persisted, `UNKNOWN` khi không thể suy ra chính xác; `SYSTEM` chỉ dùng cho baseline.
- **`parcel_custody_events` append-only** — trigger chặn cả UPDATE và DELETE. `ParcelStatusHistory` mô tả state machine; custody event mô tả bàn giao vật lý và không được dùng GPS để bịa vị trí.
- **Transit leg/search terminalization** — `LOADED` chuyển leg sang `ACTIVE`, unload hợp lệ tại đích chuyển `COMPLETED`, forwarding giữ leg cũ `FORWARDED` và leg mới `ACTIVE`, còn `LOST_CONFIRMED` chuyển leg chưa kết thúc sang `LOST`. Khi tìm thấy hàng, task chưa xong chuyển `CANCELLED`; khi xác nhận mất, task chưa xong chuyển `FAILED`; task đã hoàn tất giữ nguyên result/evidence.
- **`LOST` không thuộc ParcelStatus** — nhánh mất do `parcel_incidents.status=LOST_CONFIRMED` sở hữu. Custody exception tạm dùng `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION` và giữ resume status.
- **Stop departure fail-closed theo Parcel clearance** — Assistant không truyền reviewer UUID. Unresolved manifest tạo approval request; chỉ Driver được phân công hoặc same-tenant Operator duyệt bằng JWT. Trip phải đọc Internal-JWT clearance `CLEAR|APPROVED_OVERRIDE|BLOCKED_PENDING_APPROVAL` trước khi ghi actual departure.
- **Appeal là aggregate riêng** — `ParcelClaim` giữ nguyên `PAID|REJECTED` và audit quyết định/payout gốc. Appeal được uphold hoặc duyệt mức mới; Payment chỉ chi `max(revisedTotal-originalPaid,0)` với unique reference theo `appealId`.
- **Decision proof audit** — quyết định mới luôn ghi `VERIFIED|UNVERIFIED|NO_PROOF`; `VERIFIED` liên kết ít nhất một evidence thuộc claim bằng composite FK. Historical rows giữ `proof_status=NULL` và không backfill accepted evidence.
- **Incident evidence inheritance** — khi sender submit claim, URL evidence hợp lệ của incident `LOST_CONFIRMED` được sao chép atomically thành claim evidence loại `INCIDENT_PHOTO`; chỉ để hiển thị/chọn, không tự VERIFIED hoặc accepted.
- **Policy frozen per Parcel/claim** — default 50% thiệt hại trực tiếp, cap cargo 30.000.000 VND; operator policy thay đổi không hồi tố. Mọi quyết định mới bắt buộc VERIFIED proof để có cargo award, kể cả snapshot legacy; UNVERIFIED/NO_PROOF chỉ hoàn cước còn lại, không dùng giá tự khai làm proof. Các cột multiplier giữ nguyên để đọc lịch sử, không tham gia tính award mới; không cần migration và không rewrite quyết định/payout đã chốt. Hoàn cước nằm ngoài cargo cap. Sender là beneficiary duy nhất.
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
| `uq_parcel_cargo_recovery_operations_active_parcel` | `parcel_id` partial | unique | At most one pending `TRANSFER|RETURN|RELEASE` recovery operation per Parcel |
| `idx_parcel_cargo_recovery_operations_stale` | `(claimed_at, id)` partial | B-tree | Five-minute replay scan with stable ordering |
| `idx_parcels_sender_user_id_created_at` | `(sender_user_id, created_at DESC)` | B-tree | "My sent parcels" |
| `idx_parcels_recipient_user_id_created_at` | `(recipient_user_id, created_at DESC)` partial | B-tree | "My received parcels" |
| `idx_parcels_trip_id_status` | `(trip_id, status)` | B-tree | Trip detail page (parcels of trip) |
| `idx_parcels_operator_id_status` | `(operator_id, status)` | B-tree | Operator dashboard list |
| `idx_parcel_status_history_parcel_occurred_id` | `(parcel_id, occurred_at, id)` | B-tree | Đọc timeline theo thứ tự xác định |
| `uq_parcel_custody_events_idempotency` | `(parcel_id, idempotency_key)` partial | unique | Một custody fact cho mỗi Parcel/idempotency identity |
| `idx_parcel_custody_events_timeline` | `(parcel_id, occurred_at, id)` | B-tree | Đọc physical custody timeline |
| `uq_parcel_transit_legs_parcel_sequence` | `(parcel_id, sequence)` | unique | Thứ tự leg bất biến, forwarding không sửa leg cũ |
| `uq_parcel_incidents_active_type` | `(parcel_id, type)` partial | unique | Không mở trùng active incident cùng type |
| `idx_parcel_incidents_search_deadline` | `(search_deadline, status)` | B-tree | Search expiry scan 15 phút |
| `uq_parcel_custody_exception_requests_incident` | `(incident_id)` | unique | Một incident custody exception chỉ có một approval request |
| `uq_parcel_custody_exception_requests_idempotency` | `(idempotency_key)` | unique | Retry assistant report không tạo request mới |
| `uq_parcel_custody_exception_requests_pending_parcel_type` | `(parcel_id, incident_type)` partial | unique | Một pending approval cho cùng Parcel + loại sự cố |
| `idx_parcel_custody_exception_requests_operator_status` | `(operator_id, status, created_at)` | B-tree | Operator approval queue |
| `idx_parcel_custody_exception_requests_trip_status` | `(trip_id, status, created_at)` | B-tree | Assigned Driver approval queue |
| `idx_parcel_custody_exception_requests_approved_event` | `(approved_custody_event_id)` | B-tree | Audit link đến custody fact đã duyệt |
| `uq_parcel_stop_departure_approval_pending` | `(trip_id,stop_id,status)` partial | unique | Một yêu cầu chờ duyệt cho mỗi Trip stop |
| `uq_parcel_stop_departure_approval_idempotency` | `idempotency_key` | unique | Retry reconciliation không tạo approval trùng |
| `uq_parcel_claims_incident` | `incident_id` | unique | Một claim cho mỗi lost incident |
| `uq_parcel_claim_appeals_claim` | `claim_id` | unique | Một appeal case cho mỗi claim |
| `uq_parcel_claim_appeals_idempotency` | `idempotency_key` | unique | Retry appeal không tạo case/payout trùng |
| `uq_parcel_claim_decision_evidence` | `(claim_id,evidence_id)` | unique | Một accepted evidence link cho mỗi claim decision |
| `uq_parcel_claim_appeal_decision_evidence` | `(appeal_id,evidence_id)` | unique | Một accepted evidence link cho mỗi appeal decision |
| `uq_parcel_compensation_policies_operator` | `operator_id` | unique | Một active policy/version per operator |
| `uq_parcel_status_history_migration_baseline` | `parcel_id` partial khi `source = 'MIGRATION_BASELINE'` | unique | Tối đa một baseline cho mỗi Parcel |
| `idx_parcels_trip_snapshot_backfill` | `(created_at, id)` partial khi bất kỳ snapshot còn null | B-tree | Bounded application backfill không full scan |
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
| `Parcel.senderUserId/recipientUserId/reviewedByUserId/confirmedByUserId/transferConfirmedByUserId/transferConfirmationClaimedByUserId/returnedByUserId`, `ParcelDeliveryToken.issuedByUserId`, nullable `ParcelCargoRecoveryOperation.actorUserId` | `identity.User.id` | app-layer; system `RELEASE` operations have no actor |
| `Parcel.operatorId`, `ParcelRouteFare.operatorId`, `ParcelStats.operatorId`, `ParcelCargoRecoveryOperation.operatorId`, Reliability entities `operatorId` | `identity.Operator.id` | app-layer + tenant filter |
| `Parcel.tripId`, `Parcel.transferTargetTripId`, `ParcelCargoRecoveryOperation.sourceTripId/targetTripId` | `trip.Trip.id` | app-layer |
| `Parcel.dropoffStopId` | `trip.Stop.id` | app-layer validate `allowDropoff=true` |
| `ParcelRouteFare.routeId` | `trip.Route.id` | app-layer |
| `Parcel.additionalPaymentId` | `payment.Payment.id` | app-layer |

## Migration Strategy

- **Tool:** EF Core Migrations.
- **Bootstrap order:** Sau Identity, Trip-Route-Vehicle, Payment & Wallet (logical FK targets).
- **Status enum migration:** Add new value via `ALTER TYPE parcel_status ADD VALUE 'X'` (PG ≥ 9.1 supports inline).
- **Status history rollout:** Migration khóa `parcels` bằng `SHARE ROW EXCLUSIVE` trong transaction, seed đúng một baseline cho từng Parcel hiện hữu, sau đó bật trigger transition trước khi nhả khóa để không có khoảng trống mất lịch sử. Trigger riêng chặn `UPDATE`/`DELETE` history; `Down()` gỡ trigger/function trước khi gỡ bảng.

## Open Questions

Không có. Section 6.6 + Section 8 đã spec đầy đủ.
