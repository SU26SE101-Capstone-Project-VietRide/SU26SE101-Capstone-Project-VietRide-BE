# Notification Service — DB Schema

## Overview

NestJS service xử lý **in-app notification history + FCM push outbound**. **Chỉ consume** RabbitMQ events (BookingConfirmed, TripDelayed, ParcelLoaded, etc.) → INSERT in-app history + enqueue BullMQ `fcm-push` queue. BullMQ retry với exponential backoff: 5s → 30s → 5m → DLQ.

- **Database:** `vietride_notification`
- **Framework:** NestJS + Prisma
- **Extensions:** `pgcrypto`
- **Background jobs:** **BullMQ** (NestJS service, KHÔNG dùng Hangfire). `fcm-push` queue Redis-backed.
- **Hangfire schema:** KHÔNG có.
- **OutboxEvent table:** **KHÔNG có** — service chỉ consume, không publish event (per v6 Section 8).

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Notification` | In-app history per user. | `type` enum (53 values), `title`, `body`, `data` JSONB, `readAt` |
| `NotificationDelivery` | FCM push attempt audit. | `fcmToken` snapshot, `status` enum, `retryCount`, `lastError` |

## Design Decisions

- **NO `OutboxEvent` table** — v6 Section 8 spec rõ: "Notification Service không có OutboxEvent (chỉ consume)". Trade-off: nếu Notification Service down giữa lúc consume từ RabbitMQ, RabbitMQ at-least-once redelivery + BullMQ enqueue đủ đảm bảo (xem v6 Section 6.7 "Durability strategy").
- **`Notification.type` enum với 53 giá trị** — toàn bộ loại thông báo theo thứ tự canonical:

  ```text
  BOOKING_CONFIRMED
  BOOKING_CANCELLED
  BOOKING_DISRUPTED
  BOOKING_REFUNDED
  PASSENGER_NO_SHOW
  TRIP_BOARDING_REMINDER
  TRIP_VEHICLE_APPROACHING
  TRIP_ROUTE_CHANGED
  TRIP_SCHEDULE_CHANGED
  TRIP_CANCELLED
  TRIP_DELAYED
  TRIP_DISRUPTED
  STOP_DISABLED
  VEHICLE_SUBSTITUTED
  VEHICLE_SWAPPED
  PARCEL_LOADED
  PARCEL_IN_TRANSIT
  PARCEL_DELIVERED_PENDING_CONFIRM
  PARCEL_REJECTED
  PARCEL_RETURNED
  WALLET_CREDITED
  WALLET_DEBITED
  INCIDENT_REPORTED
  OFF_ROUTE_ALERT
  TRIP_DELAYED_ALERT
  CARGO_NEAR_FULL_ALERT
  PARCEL_REVIEW_REQUESTED
  PARCEL_REVIEW_APPROVED
  PARCEL_FINAL_PAYMENT_REQUIRED
  PARCEL_SETTLEMENT_RECOVERED
  VOUCHER_CONSENT_REQUESTED
  VOUCHER_CONSENT_ACCEPTED
  VOUCHER_CONSENT_REJECTED
  SUBSCRIPTION_LIMIT_EXCEEDED
  SUBSCRIPTION_TRIAL_EXPIRING
  SUBSCRIPTION_EXPIRED
  SUBSCRIPTION_APPROVED
  SUBSCRIPTION_PAYMENT_PENDING_WARN
  SUBSCRIPTION_PAYMENT_AUTO_REVERTED
  INVOICE_ISSUED
  DRIVER_SCHEDULE_EDITED
  PAYOUT_PROCESSED
  PAYOUT_FAILED
  OPERATOR_APPROVED
  OPERATOR_SUSPENDED
  OPERATOR_REGISTRATION_SUBMITTED
  TRIP_ASSIGNED
  TRIP_ASSIGNMENT_REMOVED
  OPERATOR_ANNOUNCEMENT
  SHUTTLE_ASSIGNED
  SHUTTLE_UNFULFILLED
  SHUTTLE_WARNING
  DRIVER_STOP_DEPARTED_WITH_PENDING
  ```

  Mở rộng qua `ALTER TYPE ADD VALUE`.
- **`Notification.data` JSONB** — payload context routing (bookingId, tripId, parcelId). Schema linh hoạt theo type; validate ở handler.
- **`Notification.read_at` nullable** + index partial `WHERE read_at IS NULL` — query "unread count" nhanh.
- **`NotificationDelivery.fcm_token` snapshot** — copy token tại lúc enqueue (không FK đến `identity.user_devices` vì token có thể đã bị cleanup ở Identity Service sau đó).
- **`NotificationDelivery.platform` enum** — denormalized cho audit + reporting (% iOS vs Android failure).
- **NO unique constraint** trên `(notification_id, fcm_token)` — multi-device user có thể nhận push trên nhiều device → nhiều delivery record cho cùng notification.
- **`Notification`/`NotificationDelivery` retention:** truncate-by-date (e.g. 90 days) qua BullMQ daily job — KHÔNG soft delete.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `idx_notifications_user_id_created_at` | `(user_id, created_at DESC)` | B-tree | In-app history list |
| `idx_notifications_user_id_unread` | `(user_id, created_at DESC)` partial | B-tree | Unread count badge |
| `idx_notifications_type_created_at` | `(type, created_at DESC)` | B-tree | Type analytics |
| `idx_notification_deliveries_notification_id` | `notification_id` | B-tree | "Devices that received this notification" |
| `idx_notification_deliveries_status_created_at` | `(status, created_at)` partial | B-tree | BullMQ retry / DLQ scan |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `Notification.userId` | `identity.User.id` | implicit — event payload từ RabbitMQ trusted |
| `Notification.data` JSONB | various (bookingId, tripId, parcelId, etc.) | not constrained |

## Migration Strategy

- **Tool:** Prisma migrations.
- **Bootstrap order:** Sau Identity (logical FK target). Không depend service khác về schema.
- **Data retention:** BullMQ daily job `DELETE FROM notifications WHERE created_at < now() - INTERVAL '90 days'` (env var configurable). NotificationDelivery cascade.

## Open Questions

Không có. Section 6.7 + Section 8 đã spec đầy đủ.
