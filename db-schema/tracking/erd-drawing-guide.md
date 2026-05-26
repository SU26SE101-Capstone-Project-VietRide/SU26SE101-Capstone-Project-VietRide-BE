# Tracking — ERD Drawing Guide

## Statistics
- **Total tables:** 2
- **Total intra-service FK:** 0
- **Hub tables:** N/A
- **Leaf tables:** tất cả

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| GPS | `GpsTrail` | trái |
| Reliability | `OutboxEvent` | phải |

## Drawing Order

### Phase 1 — Intra-service
KHÔNG có.

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ)
- `GpsTrail.tripId` → `trip.Trip.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. Service minimal — chỉ 2 table box, không có connection lines.
2. Hầu hết state ở Redis (`tracking:latest:{tripId}`, `tracking:gps_buffer:{tripId}`, `tracking:eta:{tripId}:{stopId}`, `tracking:off_route_since:{tripId}`, `tracking:active_trips`, `tracking:approaching_notified:{tripId}:{bookingId}:wN`). Note tham khảo vào drawio.

## Validation Checklist

- [ ] 2 table box hiển thị đầy đủ column
- [ ] Không có connection lines
- [ ] GpsTrail có CHECK constraint lat/lng/speed
