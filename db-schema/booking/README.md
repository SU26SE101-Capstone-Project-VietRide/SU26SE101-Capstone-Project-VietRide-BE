# Booking Service — DB Schema

## Overview

Quản lý **booking lifecycle, multi-passenger per booking, seat reference, voucher application, cancellation/refund triggers, round-trip pairing, vehicle substitution transfer tracking**. Tham chiếu logical FK đến Identity (User/Operator) và Trip-Route-Vehicle (Trip/Station/Stop).

- **Database:** `vietride_booking`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`
- **Hangfire schema:** `hangfire.*` trong cùng DB. Jobs: seat release khi VNPay timeout, unchanged Day-22 pending-action T+2h re-alert, và separate Day-23 schedule-change initial/terminal acceptance phases.

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Booking` | Order/giao dich mua ve. Existing legacy row below used to describe Booking as the ticket; new design separates Ticket as proof of travel. | `bookingCode` UNIQUE, booking-level amount/status, immutable trip snapshot, mutable current-departure projection, round-trip group |
| `Ticket` | Per-seat proof of travel / QR identity. | `ticketCode` UNIQUE, `passengerId` UNIQUE, `seatNumber`, `status`, fare/discount/paid snapshot, lifecycle timestamps |
| `Booking` | Vé của 1 buyer cho 1 trip. | `bookingCode` UNIQUE, 4 pickup/dropoff FK (exclusive), `totalAmount` immutable snapshot, 4 trip snapshot fields, `bookingGroupId`/`tripDirection` round-trip, `cancellationReason` enum, `refundOverride` |
| `Passenger` | Sub-entity của Booking (1–5/booking). Operational-only. | `seatNumber`, `boardingStatus`, `boardedAt`, `boardedAtStopId` |
| `BookingStatusHistory` | Authoritative append-only Booking lifecycle timeline for operator monitoring. | `bookingId`, `status`, `occurredAt`, nullable `reasonCode`/`actorUserId`, required `source` |
| `BookingPendingAction` | Pending action passenger cần phản hồi. | `reason` enum, `severity` enum (MEDIUM/MAJOR), `deadline`, `metadata` JSONB; **partial unique 1 active per booking** |
| `BookingTransfer` | Track Vehicle Substitution per Passenger. | `originalTripId`, `newTripId`, `originalSeatNumber`, `newSeatNumber` nullable, `note` |
| `BookingStats` | Counter table (event-driven UPSERT). | `(operatorId, statDate, tripId)` unique; tổng booking/cancel/no_show/revenue/refunded |
| `Voucher` | Platform-wide voucher (SYSTEM_ADMIN tạo, hoặc OPERATOR_ADMIN tự tạo operator-owned OPERATOR_FUNDED voucher). | `code` partial UNIQUE WHERE deleted_at IS NULL, `name`, `type`/`value`/`fundingType` enums, `owner_operator_id` (NULL=platform, NOT NULL=operator-owned), `applicableOperatorIds`/`applicableRouteIds` UUID[], `deleted_at` soft-delete, validity window |
| `VoucherUsage` | 1 record per apply. | `funded_by` snapshot, `bookingGroupId` nullable cho round-trip limit |
| `OperatorVoucherConsent` | Operator opt-in cho OPERATOR_FUNDED voucher. | `status` enum, UNIQUE `(operatorId, voucherId)`, `rejectReason` |
| `OutboxEvent` | Reliability — Outbox pattern. | `eventType`, `payload`, `status` |

## Design Decisions

- **`Booking.booking_code` UNIQUE** (full unique, not partial — code globally unique). Format `VR-yyyyMMdd-XXXXXXXX`. QR encode directly.
- **`Booking` 4-column pickup/dropoff** với 2 CHECK constraints:
  - Pickup: **exactly one** of `pickup_station_id` / `pickup_stop_id` not null.
  - Dropoff: **at most one** not null (cả 2 NULL = default terminal destination, lưu implicit).
  - Pattern này thay polymorphic discriminator để giữ strict FK ở DB layer.
- **`Booking.total_amount` IMMUTABLE** sau INSERT — comment làm rõ. EF Core: configure as snapshot, không update trong handler.
- **`trip_snapshot_departure` is immutable; `trip_current_departure` is the separate mutable projection.** Rollout backfills `trip_current_departure = trip_snapshot_departure`; `trip.trip.schedule_changed` advances only the current column by causal CAS (`current==old` apply, `current==new` duplicate, otherwise retry/quarantine). Consumer updates `PENDING_PAYMENT|CONFIRMED`, while only `CONFIRMED` emits schedule facts or creates one active `SCHEDULE_CHANGE`. Existing operator `date` and `sortBy=departureAt` queries use the current projection; `STOP_DISABLED` deadline calculation also uses it.
- **`Booking.total_amount <= base_fare` CHECK** — discount không thể âm.
- **`Booking.passenger_user_id`, `trip_id`, `operator_id`, `pickup_station_id`, `pickup_stop_id`, etc. là LOGICAL FK** — không có `REFERENCES`. Validate ở Booking Service handler khi tạo Booking (HTTP call sang Identity/Trip).
- **`Passenger` UNIQUE `(booking_id, seat_number)`** — chống duplicate seat trong cùng booking.
- **`booking_pending_actions` partial unique `(booking_id) WHERE resolved_at IS NULL`** — enforce v6 rule "chỉ 1 active per booking". Action mới phát sinh → app-layer phải close action cũ với `SUPERSEDED` trước khi INSERT mới.
- **`booking_pending_actions.severity` nullable** — chỉ set cho SCHEDULE_CHANGE (MEDIUM/MAJOR). MINOR không persist record.
- **Day-23 `SCHEDULE_CHANGE` metadata freeze:** exact `sourceEventId`, `oldDeparture`, `newDeparture`, `severity`, `initialDeadline`, nullable `terminalDeadline`, `refundBasisAmount`, `refundPercent`, `refundAmount`. Basis là immutable `Booking.total_amount`; MEDIUM = 50%, MAJOR = 100%, làm tròn đến VND bằng `MidpointRounding.AwayFromZero`. Passenger reject atomically resolves action, cancels Booking, appends history, and emits one authoritative cancellation fact. Scheduled acceptance only resolves `ACCEPTED`; it does not cancel/refund.
- **`booking_transfers` 1 record per Passenger** (không phải 1 per Booking) — multi-passenger booking sẽ có N record cùng `booking_id` khác `passenger_id`. UNIQUE constraint NOT enforced ở DB (1 passenger có thể transfer nhiều lần nếu Trip_new lại DISRUPTED).
- **`booking_stats`** dùng surrogate UUID PK + UNIQUE composite `(operator_id, stat_date, COALESCE(trip_id, ...))` — `trip_id` nullable cho per-operator-per-day aggregate row (trip_id=NULL coalesced to zero-UUID để UNIQUE bao trùm cả 2 case).
- **`vouchers.applicable_operator_ids`/`applicable_route_ids`** dùng `UUID[]` array thay vì junction table — query với `= ANY(array)` đủ cho scale (mỗi voucher target ≤ 100 operator/route trong realistic case). Junction table phức tạp hơn không justify.
- **`vouchers.owner_operator_id` nullable (logical FK identity.operators):** NULL = admin platform voucher; NOT NULL = operator self-created voucher (OPERATOR_FUNDED, self-consented, tenant-scoped). Enforced by `chk_vouchers_operator_owned_funding`: `owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'`. Operator-owned vouchers bypass the consent flow (self-created = self-consented), emit NO integration event, and are scoped to `owner_operator_id == caller` in all CRUD operations.
- **`vouchers.deleted_at` soft-delete (ADR 0003):** Admin hard-delete cũ chuyển thành soft-delete. `uq_vouchers_code` trở thành partial unique index `WHERE deleted_at IS NULL` để cho phép tái sử dụng code sau khi soft-delete. Voucher có cả `is_active` (activation toggle, `IActivatable`) và `deleted_at` (soft-delete, `ISoftDeletable`) — hai concern riêng biệt theo ADR 0003.
- **`vouchers.name` NOT NULL:** human-readable label cho cả admin và operator voucher (vd "Summer Sale 20%").
- **`voucher_usages` CASCADE DELETE từ Booking** — spec yêu cầu DELETE voucher_usage khi booking CANCELLED/REFUNDED (xem v6 Section 8 Voucher convention). Tự động qua CASCADE thay vì manual cleanup.
- **`operator_voucher_consents` UNIQUE `(operator_id, voucher_id)`** — 1 operator có 1 consent record per voucher (status có thể chuyển PENDING→ACCEPTED→REJECTED, không tạo record mới).
- **`outbox_events.status` partial index** — chỉ index PENDING/PUBLISHING/FAILED (không index PUBLISHED — > 99% rows sau lifetime).

### Booking status history (Day 19 authoritative timeline)

`booking_status_history` is the only authoritative source for the operator booking-detail timeline. It replaces the earlier proposal to derive the timeline from “Outbox audit” and forbids reconstruction from Booking lifecycle timestamp columns.

| Column | Type | Null | Rule |
|---|---|---|---|
| `id` | `UUID` | no | PK. |
| `booking_id` | `UUID` | no | Local FK `REFERENCES bookings(id) ON DELETE RESTRICT`. |
| `status` | `booking_status` | no | Status reached by the successful creation/transition. |
| `occurred_at` | `TIMESTAMPTZ` | no | One application-captured `IClock.UtcNow`; never DB-read or Outbox-publish time. |
| `reason_code` | `VARCHAR(100)` | yes | Existing canonical machine-readable domain reason only; never free text. |
| `actor_user_id` | `UUID` | yes | Logical FK to `identity.users.id`; no cross-database DB FK. |
| `source` | `VARCHAR(100)` | no | Required application-controlled constant. |

The six current `source` values and their population matrix are exact:

| Source | Status | Actor | Reason |
|---|---|---|---|
| `CREATE_BOOKING` | `PENDING_PAYMENT` | authenticated passenger user id | NULL |
| `CREATE_ROUND_TRIP_BOOKING` | `PENDING_PAYMENT` per leg | authenticated passenger user id | NULL |
| `CONFIRM_ON_PAYMENT` | `CONFIRMED` | NULL | NULL |
| `EXPIRE_ON_PAYMENT` | `EXPIRED` | NULL | NULL |
| `CANCEL_BOOKING` | `CANCELLED` | authenticated passenger user id | exact existing `BookingCancellationReason` enum name |
| `MARK_REFUNDED` | `REFUNDED` | NULL | NULL |

Each selected writer captures `IClock.UtcNow` once and reuses it for the creation/transition work and history row. Creation appends `PENDING_PAYMENT`; each guarded successful transition appends exactly one history row in the same local transaction. A guarded no-op or replay appends nothing. Transaction rollback removes both state change and history insert.

The persistence surface is insert/read only; no update/delete API or repository operation is allowed. Booking remains non-deletable, with `ON DELETE RESTRICT` as defense in depth. Existing bookings receive no fabricated backfill. History emits no integration event. Future writers require reviewed SOT approval for a new source constant; authenticated-human writers store caller id, automated/system/event-driven writers store NULL, and reason codes must come from canonical domain enums/codes.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `uq_bookings_booking_code` | `booking_code` | unique | QR scan lookup |
| `idx_bookings_passenger_user_id_created_at` | `(passenger_user_id, created_at DESC)` | B-tree | History query |
| `idx_bookings_trip_id_status` | `(trip_id, status)` | B-tree | "bookings on this trip" + filter |
| `idx_bookings_operator_id_status` | `(operator_id, status)` | B-tree | Operator dashboard |
| `idx_bookings_booking_group_id` | `booking_group_id` partial | B-tree | Round-trip group lookup |
| `idx_bookings_status_created_at` | `(status, created_at)` partial | B-tree | Hangfire VNPay timeout scan |
| `idx_bookings_trip_current_departure` | `(trip_current_departure DESC)` | B-tree | Current schedule date filter and existing `departureAt` sort |
| `idx_booking_status_history_booking_occurred_id` | `(booking_id, occurred_at, id)` | B-tree | Stable timeline read ordered by `occurred_at ASC, id ASC` |
| `uq_passengers_booking_seat` | `(booking_id, seat_number)` | unique | Avoid duplicate seat |
| `idx_passengers_boarding_status` | `(booking_id, boarding_status)` | B-tree | NO_SHOW detection job |
| `uq_booking_pending_actions_active_per_booking` | `(booking_id)` partial | unique | "1 active per booking" rule |
| `idx_booking_pending_actions_deadline_unresolved` | `deadline` partial | B-tree | Hangfire timeout scan |
| `idx_booking_transfers_booking_id` | `booking_id` | B-tree | Transfer history per booking |
| `idx_booking_transfers_original_trip_id` | `original_trip_id` | B-tree | Audit Vehicle Substitution |
| `uq_booking_stats_operator_date_trip` | `(operator_id, stat_date, COALESCE(trip_id, ...))` | unique | UPSERT upsert lookup |
| `uq_vouchers_code` | `code` | unique | Code redeem lookup |
| `idx_voucher_usages_voucher_user` | `(voucher_id, user_id)` | B-tree | Per-user usage limit |
| `idx_voucher_usages_voucher_group` | `(voucher_id, booking_group_id)` partial | B-tree | Round-trip COUNT DISTINCT |
| `uq_operator_voucher_consents_operator_voucher` | `(operator_id, voucher_id)` | unique | 1 consent per pair |
| `idx_operator_voucher_consents_operator_status` | `(operator_id, status)` | B-tree | Operator Web "Voucher đề xuất" tab |
| `idx_outbox_events_status_created` | `(status, created_at)` partial | B-tree | Outbox worker poll |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `Booking.passengerUserId`, `Voucher.createdByUserId`, `OperatorVoucherConsent.respondedByUserId`, `VoucherUsage.userId`, `BookingTransfer.transferredByUserId` | `identity.User.id` | app-layer validate (Internal JWT carry userId) |
| `BookingStatusHistory.actorUserId` | `identity.User.id` | nullable logical FK only; authenticated-human actor or NULL for automated transitions |
| `Booking.operatorId`, `OperatorVoucherConsent.operatorId`, `BookingStats.operatorId` | `identity.Operator.id` | app-layer + tenant filter |
| `Booking.tripId`, `BookingTransfer.originalTripId/newTripId`, `BookingStats.tripId` | `trip.Trip.id` | app-layer validate via HTTP `GET /internal/v1/trips/{id}` |
| `Booking.pickupStationId/dropoffStationId` | `trip.Station.id` | app-layer |
| `Booking.pickupStopId/dropoffStopId`, `Passenger.boardedAtStopId` | `trip.Stop.id` | app-layer |
| `vouchers.applicable_operator_ids[]` | `identity.Operator.id` (array) | app-layer |
| `vouchers.applicable_route_ids[]` | `trip.Route.id` (array) | app-layer |

## Migration Strategy

- **Tool:** EF Core Migrations.
- **Bootstrap order:** Sau Identity Service (logical FK validate target).
- **Snapshot/current departure rollout:** `trip_snapshot_*` được set tại CREATE Booking và KHÔNG cập nhật khi Trip edit. Add nullable `trip_current_departure`, backfill it from `trip_snapshot_departure`, then create `idx_bookings_trip_current_departure`; new Booking writes both departure fields initially, and later schedule events mutate only the current projection.
- **Booking status history:** add through an EF Core migration in Task 19.0c; do not backfill pre-existing bookings. The migration must create the local `ON DELETE RESTRICT` FK and `(booking_id, occurred_at, id)` index. No DDL is added by this architecture-baseline task.
- **`booking_pending_actions.metadata` JSONB schema** linh hoạt theo reason — không enforce schema ở DB, validate ở handler.

## Open Questions

Không có. Section 6.1, 6.2, 6.4, 6.4.1, 6.12, 6.13 + Section 8 đã spec đầy đủ.
