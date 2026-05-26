# Booking — ERD Drawing Guide

> Hướng dẫn vẽ relation lines manually trong draw.io sau khi mở `schema.drawio`.

## Statistics
- **Total tables:** 9
- **Total intra-service FK:** 6
- **Hub tables (≥3 inbound FK):** `Booking` (5 inbound: Passenger, BookingPendingAction, BookingTransfer, VoucherUsage, + booking_stats logical)
- **Leaf tables (no inbound FK):** `BookingStats`, `OperatorVoucherConsent`, `OutboxEvent`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Core hub (center-left) | `Booking`, `Passenger` | trung tâm |
| Booking events (right of hub) | `BookingPendingAction`, `BookingTransfer` | bên phải Booking |
| Voucher (top-right) | `Voucher`, `VoucherUsage`, `OperatorVoucherConsent` | trên-phải |
| Reporting (bottom) | `BookingStats`, `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 — Booking hub relations (intra-service inbound)

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `Passenger.bookingId` | `Booking.id` | N:1 | CASCADE delete; max 5 enforced app-layer |
| 2 | `BookingPendingAction.bookingId` | `Booking.id` | N:1 | CASCADE; partial unique active (1 active per booking) |
| 3 | `BookingTransfer.bookingId` | `Booking.id` | N:1 | RESTRICT |
| 4 | `BookingTransfer.passengerId` | `Passenger.id` | N:1 | RESTRICT |
| 5 | `VoucherUsage.bookingId` | `Booking.id` | N:1 | CASCADE (DELETE on cancel per spec) |

### Phase 2 — Voucher relations

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 6 | `VoucherUsage.voucherId` | `Voucher.id` | N:1 | RESTRICT |
| 7 | `OperatorVoucherConsent.voucherId` | `Voucher.id` | N:1 | CASCADE; unique `(operatorId, voucherId)` |

### Phase 3 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

- `Booking.passengerUserId`, `Voucher.createdByUserId`, `OperatorVoucherConsent.respondedByUserId`, `VoucherUsage.userId`, `BookingTransfer.transferredByUserId` → `identity.User.id`
- `Booking.operatorId`, `OperatorVoucherConsent.operatorId`, `BookingStats.operatorId` → `identity.Operator.id`
- `Booking.tripId`, `BookingTransfer.originalTripId`/`newTripId`, `BookingStats.tripId` → `trip.Trip.id`
- `Booking.pickupStationId`/`dropoffStationId` → `trip.Station.id`
- `Booking.pickupStopId`/`dropoffStopId`, `Passenger.boardedAtStopId` → `trip.Stop.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Booking là hub trung tâm** — đặt giữa canvas, 4 satellites tỏa ra 4 hướng.
2. **Passenger gần Booking nhất** (line ngắn nhất) vì 1:N strong.
3. **BookingTransfer có 2 line vào Booking + Passenger** — vẽ qua "lane" riêng để không cross các line khác.
4. **Voucher group cluster** ở góc — `Voucher` ở giữa, `VoucherUsage` + `OperatorVoucherConsent` 2 bên.
5. **Cardinality:** Booking → Passenger là 1:1..5; show `1..5` label.
6. **Color:** mọi line đến Booking dùng cùng màu (vàng theo service color).

## Validation Checklist

- [ ] Mọi FK column trong `schema.sql` có line tương ứng (7 intra-service FK)
- [ ] Partial unique của `booking_pending_actions` (1 active per booking) note rõ trong drawing
- [ ] Voucher cluster tách biệt với Booking cluster, không cross lines
- [ ] BookingStats không có line vào (leaf table)
