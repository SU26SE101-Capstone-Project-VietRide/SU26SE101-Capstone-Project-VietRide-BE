# Trip-Route-Vehicle — ERD Drawing Guide

> Hướng dẫn vẽ relation lines manually trong draw.io sau khi mở `schema.drawio`.

## Statistics
- **Total tables:** 20
- **Total intra-service FK:** ~30
- **Hub tables (≥3 inbound FK):**
  - `Stop` (5 inbound): RouteStop, AlternativeRouteStop, RouteStopFareTemplate, TripStop, TripStopFare, Stop.replacedByStopId (self)
  - `Trip` (6 inbound): TripSeat, TripStop, TripStopFare, ShuttleTrip, ShuttlePassenger, Incident
  - `Route` (4 inbound): RouteStop, RouteStopFareTemplate, AlternativeRoute, DriverSchedule, Trip + Route.returnRouteId (self)
  - `Station` (4 inbound): Route.origin, Route.destination, AlternativeRoute.destination, OperatorStation, ShuttleTrip
  - `Vehicle` (3 inbound): Trip, DriverSchedule, ShuttleTrip
- **Leaf tables (no inbound FK):** `OperatorStation`, `Incident`, `TripGenerationSkipLog`, `OutboxEvent`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Master data (top-left) | `Station`, `OperatorStation`, `Stop` | trên-trái |
| Route catalog (left-center) | `Route`, `RouteStop`, `RouteStopFareTemplate`, `AlternativeRoute`, `AlternativeRouteStop` | giữa-trái |
| Vehicle (left-bottom) | `VehicleType`, `Vehicle` | dưới-trái |
| Operational hub (center) | `Trip`, `DriverSchedule` | trung tâm |
| Trip detail (right) | `TripSeat`, `TripStop`, `TripStopFare` | phải-Trip |
| Shuttle (bottom-right) | `ShuttleTrip`, `ShuttlePassenger` | dưới-phải |
| Misc (bottom) | `Incident`, `TripGenerationSkipLog`, `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 — Trip hub relations (6 inbound)

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `TripSeat.tripId` | `Trip.id` | N:1 | CASCADE delete |
| 2 | `TripStop.tripId` | `Trip.id` | N:1 | CASCADE delete; composite PK |
| 3 | `TripStopFare.tripId` | `Trip.id` | N:1 | CASCADE delete; composite PK; exception only |
| 4 | `ShuttleTrip.mainTripId` | `Trip.id` | N:1 | RESTRICT |
| 5 | `ShuttlePassenger.mainTripId` | `Trip.id` | N:1 | RESTRICT |
| 6 | `Incident.tripId` | `Trip.id` | N:1 | RESTRICT |

### Phase 2 — Stop hub relations (5 inbound + self)

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 7 | `RouteStop.stopId` | `Stop.id` | N:1 | composite PK |
| 8 | `RouteStopFareTemplate.stopId` | `Stop.id` | N:1 | RESTRICT |
| 9 | `AlternativeRouteStop.stopId` | `Stop.id` | N:1 | composite PK |
| 10 | `TripStop.stopId` | `Stop.id` | N:1 | RESTRICT |
| 11 | `TripStopFare.stopId` | `Stop.id` | N:1 | RESTRICT |
| 12 | `Stop.replacedByStopId` | `Stop.id` | N:1 | **Self-FK**, nullable, SET NULL |

### Phase 3 — Route hub relations (4 inbound + self)

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 13 | `RouteStop.routeId` | `Route.id` | N:1 | composite PK; CASCADE |
| 14 | `RouteStopFareTemplate.routeId` | `Route.id` | N:1 | CASCADE |
| 15 | `AlternativeRoute.routeId` | `Route.id` | N:1 | CASCADE |
| 16 | `DriverSchedule.routeId` | `Route.id` | N:1 | RESTRICT |
| 17 | `Trip.routeId` | `Route.id` | N:1 | RESTRICT |
| 18 | `Route.returnRouteId` | `Route.id` | N:1 | **Self-FK**, nullable, SET NULL |

### Phase 4 — Station + Vehicle hub relations

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 19 | `Route.originStationId` | `Station.id` | N:1 | RESTRICT |
| 20 | `Route.destinationStationId` | `Station.id` | N:1 | RESTRICT |
| 21 | `AlternativeRoute.destinationStationId` | `Station.id` | N:1 | RESTRICT |
| 22 | `OperatorStation.stationId` | `Station.id` | N:1 | RESTRICT |
| 23 | `ShuttleTrip.stationId` | `Station.id` | N:1 | RESTRICT |
| 24 | `Vehicle.vehicleTypeId` | `VehicleType.id` | N:1 | RESTRICT |
| 25 | `Trip.vehicleId` | `Vehicle.id` | N:1 | RESTRICT |
| 26 | `DriverSchedule.vehicleId` | `Vehicle.id` | N:1 | SET NULL |
| 27 | `ShuttleTrip.vehicleId` | `Vehicle.id` | N:1 | RESTRICT |

### Phase 5 — Secondary + DriverSchedule + Shuttle

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 28 | `AlternativeRouteStop.alternativeRouteId` | `AlternativeRoute.id` | N:1 | CASCADE |
| 29 | `Trip.driverScheduleId` | `DriverSchedule.id` | N:1 | SET NULL nullable |
| 30 | `TripGenerationSkipLog.driverScheduleId` | `DriverSchedule.id` | N:1 | CASCADE |
| 31 | `ShuttlePassenger.shuttleTripId` | `ShuttleTrip.id` | N:1 | SET NULL nullable |

### Phase 6 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

`Trip.operatorId/driverUserId/assistantUserId/cancelledByUserId/completedByUserId`, `Route.operatorId`, `Stop.operatorId`, `Vehicle.operatorId`, `DriverSchedule.operatorId/driverUserId/assistantUserId`, `OperatorStation.operatorId`, `ShuttleTrip.operatorId/driverUserId`, `ShuttlePassenger.bookingId`, `Incident.reportedByUserId/resolvedByUserId`, `TripGenerationSkipLog.operatorId`.

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Trip + Stop là 2 hub mạnh nhất** — đặt Trip ở trung tâm, Stop phía trên Trip (giữa Route catalog và Trip).
2. **Color-code per hub:** line đến Trip dùng màu đỏ (matches service color); line đến Stop dùng màu cam; line đến Route dùng màu xanh dương.
3. **Self-FK loops:** `Stop.replacedByStopId` và `Route.returnRouteId` — vẽ vòng cung ngắn từ phải qua đỉnh table về trái.
4. **Composite PK tables (RouteStop, AlternativeRouteStop, TripStop, TripStopFare):** 2 line đầu vào, đảm bảo cả 2 chân FK có marker.
5. **TripSeat fanout:** Trip → TripSeat có thể có 40-45 record/Trip — không cần đánh dấu cardinality cụ thể, chỉ N:1.
6. **Tránh route line từ DriverSchedule** qua giữa các Trip box; route nó xuống cạnh dưới canvas.

## Validation Checklist

- [ ] Mọi FK column trong `schema.sql` có line tương ứng trong drawio (31 intra-service FK)
- [ ] 2 self-FK loop hiển thị rõ
- [ ] Composite PK (RouteStop, TripStop, TripStopFare, AlternativeRouteStop) có cả 2 FK marked PK
- [ ] Trip hub không bị che bởi 6 line đến
- [ ] Cardinality nhất quán (mọi line N:1 dùng cùng style endArrow)
