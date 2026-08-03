# Trip-Route-Vehicle Service — DB Schema

## Overview

Service domain logic nặng nhất — quản lý **mạng lưới tuyến đường (Route/Stop/Station), đội xe (Vehicle/VehicleType), chuyến cụ thể (Trip + TripSeat + TripStop), assignment recurring (DriverSchedule), shuttle service, incident report**. Tham chiếu logical FK đến `Operator`/`User` ở Identity Service.

- **Database:** `vietride_trip`
- **Framework:** .NET Core 8 + EF Core 8
- **Extensions:** `pgcrypto`, `unaccent`, `pg_trgm`
  - `unaccent` backs Day-7 accent-insensitive station search (`unaccent(name) ILIKE unaccent('%' || q || '%')`).
  - `pg_trgm` is enabled only because the canonical schema keeps the deferred `idx_stations_name_trgm ... gin_trgm_ops WHERE FALSE` placeholder; Day 7 does not enable trigram similarity search.
- **Hangfire schema:** `hangfire.*` trong cùng DB này. Jobs: auto-generate Trip (CN 23:00 + on-create), auto-BOARDING 30 phút trước departure, auto-COMPLETED fallback +30 phút sau ETA, delayed detection.

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Station` | Bến canonical platform-level (KHÔNG có operatorId). | `slug` UNIQUE, `supportsShuttle`, `operatingHours` JSONB, `facilities` JSONB |
| `OperatorStation` | Mapping nhà xe ↔ bến. | UNIQUE `(operatorId, stationId)`, `counterLocation`, `instructions` |
| `Stop` | Điểm dừng dọc tuyến (operator-owned). | `googlePlaceId`, `sharedSuggestion`, `replacedByStopId` self-FK |
| `Route` | Tuyến chính: origin/destination Station + `baseFare`. | `returnRouteId` self-FK, `totalDistanceKm` |
| `RouteStop` | Junction Route↔Stop intermediate. | composite PK, `orderIndex`, `allowPickup`+`allowDropoff` (CHECK ≥1 true), `distanceFromOriginKm` |
| `RouteStopFareTemplate` | **Exception only** override `baseFare` per stop với half-open time window. | `effectiveFrom`/`effectiveUntil`; DB exclusion guard chống overlap |
| `OperatorFareSurchargeSetting` | Global holiday-fare switch per logical operator. | `operatorId` PK, `isEnabled`; missing row = disabled |
| `OperatorFareSurchargePeriod` | Named holiday surcharge window. | Inclusive ICT dates, percent `1..100`, active + soft-delete; DB exclusion guard chống active overlap |
| `AlternativeRoute` | Tuyến thay thế (max 2 per Route, enforced app). | Stop sequence riêng — KHÔNG reuse `RouteStop` |
| `AlternativeRouteStop` | Junction AlternativeRoute↔Stop. | composite PK, `orderIndex` |
| `VehicleType` | Loại xe catalog. | `code` UNIQUE, `isSystemDefined` (block delete cho 3 platform seed) |
| `Vehicle` | Xe operator. | `licensePlate` UNIQUE, `seatLayoutJson`, `maxCargoWeightKg`, `status` enum |
| `Trip` | Chuyến cụ thể. | snapshot `baseFare`/`estimatedPassengerLuggageKg`/`maxCargoWeightKg`, nullable trimmed `notes` (max 2000), 2 cargo counter, `source` enum, `disruptedAt`, `hasSubstitution` |
| `TripAuditLog` | Append-only audit do Trip service sở hữu. | local `tripId` FK; logical `actorUserId`; JSONB metadata |
| `TripSeat` | Trạng thái từng ghế per trip. | composite UNIQUE `(tripId, seatNumber)`, `status` enum |
| `TripStop` | Snapshot RouteStop khi generate. | composite PK, `estimatedArrivalTime` static, nullable `actualArrivalTime`, nullable `actual_departure_time` persisted when assigned crew departs an arrived stop |
| `TripStopFare` | Exception per trip per stop. | `source=TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`; Day 22 chỉ tạo mới `MANUAL_OVERRIDE` |
| `DriverSchedule` | Recurring assignment driver/assistant↔vehicle↔route. | `dayOfWeek` JSONB array, `departureTime` TIME, `validFrom`/`validUntil` |
| `DriverScheduleAuditLog` | Append-only audit do Trip service sở hữu. | local `driverScheduleId` FK; logical `actorUserId`; JSONB metadata |
| `TripGenerationSkipLog` | Audit khi Hangfire skip generate Trip. | `reason` enum, `driverScheduleId` NOT NULL |
| `ShuttleTrip` | Xe trung chuyển gắn với main Trip. | `direction` (INBOUND/OUTBOUND), `mainTripId`, `stationId` |
| `ShuttlePassenger` | Manifest entry shuttle. | `shuttleTripId` nullable (chờ assign), `pickupAddress`+lat/lng |
| `Incident` | Driver-reported incident. | `category` enum, `photoUrls` JSONB max 3 |
| `OutboxEvent` | Reliability — Outbox pattern. | `eventType`, `payload` JSONB, `status` enum |
| `OutboxDlq` | Terminal Outbox failures for admin review. | unique `eventId`, payload, retry metadata, `terminalAt` |
| `PlatformTripStats` | Projection Day 42 theo từng Trip `COMPLETED`. | `tripId`, `operatorId`, `completedAt`, `projectedAt` |

## Design Decisions

- **`Station` canonical, không có `operatorId`** — operator tự tạo Station (autocomplete dedupe ở UI, xem 4.3). Multiple operators có thể link cùng Station qua OperatorStation.
- **`OperatorStation.operator_id` là LOGICAL FK** (no `REFERENCES`) tới `vietride_identity.operators` — tránh cross-DB FK constraint. App-layer validate.
- **`Stop.replaced_by_stop_id` self-FK** với `ON DELETE SET NULL` + CHECK `replaced_by_stop_id <> id` (no self-reference). Cycle detection enforced app-layer.
- **`Route.return_route_id` self-FK** với `ON DELETE SET NULL` — round-trip pairing. Không enforce strict 2-way (R1→R2 + R2→R1) ở DB; operator UI tự maintain.
- **`Route` CHECK `origin_station_id <> destination_station_id`** — chống tuyến vô nghĩa.
- **`RouteStop` PRIMARY KEY `(route_id, stop_id)`** với UNIQUE phụ `(route_id, order_index)` — 1 stop xuất hiện 1 lần per route + order_index unique trên cùng route.
- **`RouteStop` CHECK `allow_pickup OR allow_dropoff`** — enforce v6 rule "ít nhất 1 phải true".
- **`RouteStopFareTemplate` KHÔNG composite key** trên `(route_id, stop_id)` mà dùng surrogate UUID + half-open `[effectiveFrom,effectiveUntil)` window — cho phép nhiều entry cùng stop với time windows khác nhau (future-dated pricing). PostgreSQL `btree_gist` + `ex_route_stop_fare_templates_no_overlap` enforce không overlap ngay cả khi concurrent; boundary liền kề được phép và `effectiveUntil = NULL` là open-ended.
- **Holiday surcharge is Trip-owned** because matching depends on `Trip.departure_date_time`. `operator_id` remains a logical Identity reference with no DB FK. Inclusive ICT date windows are represented as half-open PostgreSQL `daterange(start_date,end_date+1,'[)')`; a partial GiST exclusion constraint applies only to active, non-deleted rows, so concurrent overlap is rejected while inactive drafts may coexist.
- **`Vehicle.licensePlate` partial unique** trên `deleted_at IS NULL` — cho phép tái dùng biển số sau khi xe RETIRED + soft delete.
- **`Trip` 2 unique partial indexes** `(driver_user_id, departure_date_time)` và `(vehicle_id, departure_date_time)` với `status NOT IN ('CANCELLED')` — chống conflict assignment + idempotent generate (Hangfire chạy 2 lần không tạo duplicate, CANCELLED không block re-create).
- **`Trip.source` enum** với value `VEHICLE_SUBSTITUTION` — Hangfire counter check skip cho value này (xem v6 Section 4.5 c.0).
- **Day-34 Vehicle Substitution:** lock/reload an `IN_PROGRESS` old Trip, capture one `disruptedAt`, require the absolute UTC `estimatedRecoveryDepartureAt` strictly later than it, then atomically terminalize old Trip as `DISRUPTED` with `hasSubstitution=true` and create one dedicated replacement as `BOARDING`/`VEHICLE_SUBSTITUTION`. `recoveryDelay = estimatedRecoveryDepartureAt - disruptedAt`; replacement destination ETA and copied old `PENDING` TripStop ETA equal their old baselines plus that delay. Non-`PENDING` stops are not copied. The existing assigned-driver start flow owns `BOARDING -> IN_PROGRESS`; no new Trip status is added.
- **Trip owns replacement seats:** Booking's impact Passenger shape is exactly `{passengerId,boardingStatus,originalSeatNumber}` and does not carry `seatType`. Trip parses the replacement Vehicle layout and, when `originalSeatNumber` is non-null, looks up the old Trip's TripSeat to derive the preferred type. A null original seat or no matching old TripSeat means no preferred type; deterministic allocation falls back to the remaining passenger-seat order, then null when exhausted. Trip creates/reserves mapped TripSeats and emits one `trip.trip.vehicle_substituted` plus canonical `trip.trip.disrupted` in the same local transaction. Booking and Parcel remain logical consumers; there is no cross-service FK or foreign database write.
- **`TripAuditLog` append-only** — repository chỉ expose insert/read. `trip_id` là local FK `ON DELETE RESTRICT`; `actor_user_id` là logical Identity FK, không có DB constraint; `action` là application string whitelist, không tạo PostgreSQL enum.
- **`Trip.estimated_passenger_luggage_kg`, `reserved_parcel_weight_kg`, `total_loaded_weight_kg` decimal(8,2)** — đủ precision cho cargo accounting (10kg.50). CHECK non-negative cho 2 counter.
- **`TripStop.estimated_arrival_time` immutable sau khi generate** — DELAYED chỉ ở Redis (v6 quyết định KHÔNG thêm `Trip.isDelayed`).
- **`TripStopFare` composite PK `(trip_id, stop_id)`** — chỉ tồn tại cho stop có exception. `source` chỉ nhận `TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`; pre-Day-22 rows backfill `TEMPLATE_SNAPSHOT`, còn Day 22 không tạo snapshot mới và explicit per-Trip override dùng `MANUAL_OVERRIDE`.
- **`DriverSchedule.day_of_week` JSONB** thay vì bit mask — đọc dễ, mở rộng nếu cần thêm flag per day.
- **Day-23 schedule-change producer:** PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING` is the only contract that may cascade a generated Trip departure; only `ALL_PENDING` mutates Trips, and no dedicated Trip schedule endpoint/Gateway route exists. One captured clock requires `oldDeparture - now >= 2h` and computed `newDeparture - now >= 2h` for every affected Trip with a `CONFIRMED` Booking; equality is valid and either strict-less value rejects the whole batch. Absolute delta on ICT dates classifies same-date `<= 2h` as MINOR, same-date `> 2h && < 6h` as MEDIUM, and `>= 6h` or an ICT date change as MAJOR. Each committed change emits exact `trip.trip.schedule_changed {eventId,occurredAt,tripId,operatorId,oldDeparture,newDeparture,severity}` atomically through Outbox with the same payload/row/MessageId identity.
- **`ShuttlePassenger.shuttle_trip_id` nullable** — passenger có thể đăng ký shuttle trước khi operator tạo ShuttleTrip (`PENDING_ASSIGNMENT` status).
- **`Incident.photo_urls` JSONB** thay vì junction table — max 3 URLs, đơn giản, không có query cần JOIN.
- **`OutboxEvent`** trong cùng service DB — atomic INSERT với business write trong 1 transaction (Outbox pattern). Worker poll mỗi 5s, dùng `BackgroundService` (.NET).
- **`platform_trip_stats`** được trigger đồng bộ cùng transaction và job `trip.platform-stats-backfill` rebuild idempotent từ earned live; platform report chỉ cache sau khi projection khớp live theo operator/range.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `uq_stations_slug` | `slug` | partial unique | Canonical slug lookup |
| `idx_stations_city_province` | `(city, province)` | partial B-tree | Search station autocomplete |
| `idx_stations_supports_shuttle` | `supports_shuttle` | partial | Shuttle-enabled stations filter |
| `uq_operator_stations_operator_station` | `(operator_id, station_id)` | unique | Avoid duplicate mapping |
| `idx_stops_operator_id` | `operator_id` | partial | Operator's stops list |
| `idx_stops_shared_suggestion` | `shared_suggestion` | partial | Cross-operator suggest |
| `idx_routes_origin_destination` | `(origin_station_id, destination_station_id)` | partial | `GET /trips/search` filter |
| `uq_route_stops_route_order` | `(route_id, order_index)` | unique | Avoid duplicate order |
| `idx_route_stop_fare_templates_route_stop_effective` | `(route_id, stop_id, effective_from)` | B-tree | Pick active fare at Trip generate |
| `uq_vehicles_license_plate` | `license_plate` | partial unique | License plate uniqueness |
| `idx_vehicles_operator_status` | `(operator_id, status)` | partial | Operator vehicle list by status |
| `uq_trips_driver_departure` | `(driver_user_id, departure_date_time)` partial | unique | Driver conflict (Hangfire idempotent) |
| `uq_trips_vehicle_departure` | `(vehicle_id, departure_date_time)` partial | unique | Vehicle conflict |
| `idx_trips_route_departure` | `(route_id, departure_date_time)` | B-tree | Trip search by route + date |
| `idx_trips_alternative_route_id` | `(alternative_route_id)` | B-tree | Trip lookup by selected AlternativeRoute |
| `idx_trips_status_departure` | `(status, departure_date_time)` | B-tree | Hangfire BOARDING/COMPLETED scans |
| `idx_platform_trip_stats_completed_operator` | `(completed_at, operator_id)` | B-tree | Exact UTC range reconciliation |
| `idx_trip_audit_logs_trip_occurred` | `(trip_id, occurred_at DESC)` | B-tree | Audit timeline per trip |
| `idx_trip_audit_logs_actor_occurred` | `(actor_user_id, occurred_at DESC)` | partial B-tree | Audit timeline per actor |
| `idx_trip_audit_logs_action_occurred` | `(action, occurred_at DESC)` | B-tree | Audit lookup per action |
| `uq_trip_seats_trip_seat` | `(trip_id, seat_number)` | unique | Seat map per trip |
| `idx_trip_seats_trip_status` | `(trip_id, status)` | B-tree | Available seats query |
| `uq_trip_stops_trip_order` | `(trip_id, order_index)` | unique | Ordering integrity |
| `idx_trip_stops_estimated_arrival` | `estimated_arrival_time` | partial | Approaching alert candidates |
| `idx_driver_schedules_operator_active` | `(operator_id, is_active)` | B-tree | Generate Trip iteration |
| `idx_driver_schedules_vehicle_active` | `(vehicle_id, is_active)` | partial | Vehicle conflict check |
| `idx_driver_schedule_audit_logs_schedule_occurred` | `(driver_schedule_id, occurred_at DESC)` | B-tree | Audit timeline per driver schedule |
| `idx_driver_schedule_audit_logs_actor_occurred` | `(actor_user_id, occurred_at DESC)` | partial B-tree | Driver schedule audit timeline per actor |
| `idx_driver_schedule_audit_logs_action_occurred` | `(action, occurred_at DESC)` | B-tree | Driver schedule audit lookup per action |
| `idx_trip_gen_skip_logs_operator_date` | `(operator_id, skipped_date DESC)` | B-tree | Dashboard "skipped this month" |
| `idx_shuttle_passengers_main_trip_status` | `(main_trip_id, status)` | B-tree | "Shuttle requests pending" view |
| `idx_outbox_events_status_created` | `(status, created_at)` partial | B-tree | Outbox worker polling |
| `uq_outbox_dlq_event_id` | `event_id` | unique | One terminal row per event |
| `idx_outbox_dlq_terminal_event_id` | `(terminal_at, event_id)` | B-tree | Composite cursor review theo contract |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `OperatorStation.operatorId`, `Stop.operatorId`, `Route.operatorId`, `Vehicle.operatorId`, `DriverSchedule.operatorId`, `Trip.operatorId`, `ShuttleTrip.operatorId`, `TripGenerationSkipLog.operatorId` | `identity.Operator.id` | app-layer (Internal JWT carry operatorId; ON DELETE RESTRICT enforced via Identity Service soft delete + service-level check) |
| `DriverSchedule.driverUserId/assistantUserId`, `Trip.driverUserId/assistantUserId/cancelledByUserId/completedByUserId`, `ShuttleTrip.driverUserId`, `Incident.reportedByUserId/resolvedByUserId` | `identity.User.id` | app-layer validate via HTTP `GET /internal/v1/users/{id}` |
| `TripAuditLog.actorUserId` | `identity.User.id` | authenticated actor; logical reference only, no DB FK |
| `ShuttlePassenger.bookingId` | `booking.Booking.id` | app-layer |

## Migration Strategy

- **Tool:** EF Core Migrations. Migration history `__EFMigrationsHistory`.
- **Bootstrap order:** Sau `vietride_identity`. 3 VehicleType seed chạy ngay sau migration (qua `seed.sql` hoặc EF Core HasData với UUID cố định).
- **Breaking change policy:** Khi đổi `seat_layout_json` schema version, **forward-migrate** existing Vehicles trước khi remove old version support. Trip đã generate giữ TripSeat theo layout cũ.
- **Hangfire bootstrap order:** Hangfire.PostgreSql tạo schema `hangfire.*` ở app startup (lần đầu chạy app sau migration).

## Open Questions

Không có. Section 6 + Section 8 trong v6 đã đầy đủ.
