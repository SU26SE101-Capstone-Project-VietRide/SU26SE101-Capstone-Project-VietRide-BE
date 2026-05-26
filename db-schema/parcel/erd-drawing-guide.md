# Parcel — ERD Drawing Guide

## Statistics
- **Total tables:** 4
- **Total intra-service FK:** 0 (mọi reference đều cross-service logical)
- **Hub tables:** N/A
- **Leaf tables:** tất cả 4 (`Parcel`, `ParcelRouteFare`, `ParcelStats`, `OutboxEvent`)

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Core | `Parcel` | trung tâm |
| Pricing | `ParcelRouteFare` | trái |
| Reporting | `ParcelStats` | phải |
| Reliability | `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 — Intra-service

KHÔNG có intra-service FK. Mọi reference là cross-service.

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

- `Parcel.senderUserId/recipientUserId/reviewedByUserId/confirmedByUserId/transferConfirmedByUserId/returnedByUserId` → `identity.User.id`
- `Parcel.operatorId`, `ParcelRouteFare.operatorId`, `ParcelStats.operatorId` → `identity.Operator.id`
- `Parcel.tripId`, `Parcel.transferTargetTripId` → `trip.Trip.id`
- `Parcel.dropoffStopId` → `trip.Stop.id`
- `ParcelRouteFare.routeId` → `trip.Route.id`
- `Parcel.additionalPaymentId` → `payment.Payment.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Parcel ở giữa**, các table khác (Stats, Fares, Outbox) đặt ngoài rìa.
2. KHÔNG có connection line trong file này — chỉ table boxes với annotation cho `delivery_token` UNIQUE partial và `(route_id, size_category)` composite PK.
3. Note rõ `ParcelRouteFare` có composite PK `(route_id, size_category)` trên drawio.

## Validation Checklist

- [ ] 4 table box hiển thị đúng tên + columns
- [ ] Không có connection lines (drawio file này)
- [ ] Annotation rõ Parcel có 40+ field — đảm bảo column list đầy đủ
- [ ] Composite PK (ParcelRouteFare) hiển thị 2 column với PK marker
