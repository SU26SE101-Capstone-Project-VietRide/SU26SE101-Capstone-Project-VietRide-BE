# Tracking Service — DB Schema

## Overview

Tracking Service là **NestJS service** xử lý real-time GPS broadcast (Socket.IO) + ETA calculation + off-route detection. DB rất nhẹ — hầu hết state ở Redis (5 phút TTL), chỉ persist `GpsTrail` history (batch insert mỗi 5–10 phút từ Redis buffer) và `OutboxEvent` cho các broadcast event (TripDelayed, OffRouteAlert, ApproachingAlert).

- **Database:** `vietride_tracking`
- **Framework:** NestJS + TypeORM
- **Extensions:** `pgcrypto`
- **Background jobs:** **BullMQ scheduled jobs** (NestJS service KHÔNG dùng Hangfire). Jobs:
  - `gps-batch` queue (interval 5 phút): flush Redis GPS buffer → batch INSERT `gps_trails`
  - Outbox poll (interval 5s): publish PENDING events → RabbitMQ
- **Hangfire schema:** KHÔNG có (NestJS service).

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `GpsTrail` | GPS history per trip. Persisted from Redis buffer. | `tripId`, `lat`/`lng` decimal(10,7), `speedKmh` nullable, `recordedAt` |
| `OutboxEvent` | Outbox pattern. | `eventType` (TripDelayed/OffRouteAlert/etc.), `payload` JSONB |

## Design Decisions

- **Minimal DB by design** — v6 spec: "Tracking Service có PostgreSQL DB riêng — chỉ chứa GpsTrail (và OutboxEvent nếu publish event). Redis handle realtime state."
- **`gps_trails.lat/lng` decimal(10,7)** — đủ độ chính xác ~1cm theo v6 spec.
- **`gps_trails.recorded_at` vs `created_at`** — phân biệt thời điểm GPS sample (driver app) vs insert time (batch flush). Index trên `recorded_at` cho time-range query trail playback.
- **NO foreign key** (chỉ logical FK `trip_id`) — Tracking Service không cần DB-level reference; trip lifecycle do Trip-Route-Vehicle Service quản lý.
- **NO authorization data** trong DB — Socket.IO room authorization (joinTripTracking) verify ở handler thời điểm runtime qua HTTP internal call (xem v6 Section 5.5).
- **Redis state list (reference, không trong DB):**
  - `tracking:latest:{tripId}` — last known position (TTL 5 min)
  - `tracking:gps_buffer:{tripId}` — buffer list (đến khi flush)
  - `tracking:eta:{tripId}:{stopId}` — dynamic ETA cache (TTL 60s)
  - `tracking:off_route_since:{tripId}` — off-route timer start
  - `tracking:active_trips` — set membership
  - `tracking:approaching_notified:{tripId}:{bookingId}:w{1|2}` — dedupe approaching alert (TTL đến hết chuyến)

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `idx_gps_trails_trip_id_recorded_at` | `(trip_id, recorded_at)` | B-tree | Trail playback per trip |
| `idx_gps_trails_recorded_at` | `recorded_at` | B-tree | Time-range cleanup (90-day retention) |
| `idx_outbox_events_status_created` | partial | B-tree | Outbox poll |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `GpsTrail.tripId` | `trip.Trip.id` | implicit (Tracking Service trusts driver app to send valid tripId; verify via Socket.IO joinTripTracking authorization) |

## Migration Strategy

- **Tool:** TypeORM migrations.
- **Bootstrap order:** Sau Trip-Route-Vehicle (logical FK target).
- **Data retention:** GPS trail có thể grow rất lớn (3-5s/GPS × N trip). Cleanup job (BullMQ daily): `DELETE FROM gps_trails WHERE recorded_at < now() - INTERVAL '90 days'` (cấu hình env var).
- **Partitioning consideration (v2):** Khi `gps_trails` > 100M rows, range-partition theo `recorded_at` monthly.

## Open Questions

Không có. Section 6.3 + Section 8 đã spec đầy đủ.
