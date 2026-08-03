# Tracking — ERD Drawing Guide

## Statistics
- **Total tables:** 4
- **Total intra-service FK:** 0
- **Hub tables:** N/A
- **Leaf tables:** tất cả

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| GPS | `GpsTrail` | trái |
| Sharing | `TripShareGrant` | giữa |
| Reliability | `OutboxEvent`, `OutboxDlq` | phải |

## Drawing Order

### Phase 1 — Intra-service
KHÔNG có.

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ)
- `GpsTrail.tripId` → `trip.Trip.id`
- `TripShareGrant.tripId` → `trip.Trip.id`
- `TripShareGrant.createdByUserId` → `identity.User.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. Vẽ 4 table box và không nối cross-service logical FK bằng connection line.
2. Hầu hết state ở Redis (`tracking:latest:{tripId}`, `tracking:gps_buffer:{tripId}`, `tracking:eta:{tripId}:{stopId}`, `tracking:off_route_since:{tripId}`, `tracking:active_trips`, `tracking:approaching_notified:{tripId}:{bookingId}:wN`). Note tham khảo vào drawio.

## Validation Checklist

- [ ] 4 table box hiển thị đầy đủ column
- [ ] Không có connection lines
- [ ] GpsTrail có CHECK constraint lat/lng/speed
- [ ] TripShareGrant có partial unique active owner/trip, active expiry index và các CHECK constraint
