# VietRide — Master ERD Drawing Guide

> Hướng dẫn vẽ Physical ERD cho 8 services trong draw.io.
> File này thay thế việc đọc 8 per-service guides riêng lẻ — đây là master playbook duy nhất.
> Per-service guides (`<service>/erd-drawing-guide.md`) vẫn giữ làm reference chi tiết.

---

## How to Use This Guide

1. Mở draw.io desktop hoặc https://app.diagrams.net
2. Chia màn hình: bên trái = draw.io, bên phải = file này
3. Follow **Phase 0 (setup)** → **Phase 1–8 (per-service)** → **Phase 9 (optional cross-service overview)**
4. Estimated total time: **3–5 giờ** cho all 8 services (chia 2–3 session nếu cần)

---

## Global Statistics

| Metric | Count |
|---|---|
| Total services | 8 |
| Total tables (across services) | **64** |
| Total enum types | **62** |
| Total indexes | **178** |
| Total intra-service FK (with REFERENCES constraint) | **54** |
| Total cross-service logical FK (no REFERENCES, app-layer enforce) | **~70** (xem `cross-service-references.md`) |
| Hub tables (≥3 inbound FK same-service) | **7** (User, Operator, Trip, Stop, Route, Station, Booking) |
| Junction tables | **8** (route_stops, alternative_route_stops, trip_stops, trip_stop_fares, operator_stations, operator_voucher_consents, parcel_route_fares, route_stop_fare_templates) |
| Self-FKs | **3** (refresh_tokens.parent_token_id, stops.replaced_by_stop_id, routes.return_route_id) |
| Files to draw | **8** (`<service>/schema.drawio` mỗi service) |

---

## Pre-flight Checklist

- [ ] draw.io đã install (https://www.diagrams.net/) hoặc mở app.diagrams.net trên browser hiện đại
- [ ] Đã clone repo và mở folder `db-schema/`
- [ ] Có monitor đủ rộng cho 2 cửa sổ (recommend ≥1920×1080)
- [ ] Đã đọc `_global/README.md` để hiểu service map + bootstrap order
- [ ] Đã đọc `_global/cross-service-references.md` (đặc biệt nếu vẽ Phase 9)
- [ ] (Optional) Print color code reference table phía dưới

---

## Color Code Reference (apply trước khi vẽ)

Color đã được sẵn trong drawio files (auto-generated). Nếu cần customize:

| Service / Group | Fill | Stroke | Khi nào dùng |
|---|---|---|---|
| identity-user (User group) | `#dae8fc` | `#6c8ebf` | `User`, `OAuthIdentity`, `RefreshToken`, `EmailVerificationToken`, `UserDevice`, `ActivityLog` |
| identity-user (Operator group) | `#d5e8d4` | `#82b366` | `Operator`, `SubscriptionPlan`, `OperatorSubscription` |
| booking | `#fff2cc` | `#d6b656` | Mọi entity Booking service |
| trip-route-vehicle | `#f8cecc` | `#b85450` | Mọi entity Trip-Route-Vehicle service |
| payment-wallet | `#e1d5e7` | `#9673a6` | Mọi entity Payment & Wallet service |
| parcel | `#ffe6cc` | `#d79b00` | Mọi entity Parcel service |
| tracking | `#f5f5f5` | `#666666` | `GpsTrail`, `OutboxEvent` |
| notification | `#dae8fc` | `#6c8ebf` | `Notification`, `NotificationDelivery` |
| rag-ai | `#fad9d5` | `#ae4132` | Mọi entity RAG AI service |

---

## Connector Style Quick Reference

Right-click line → **Edit Style** → paste exact string:

| Relation type | Style string (copy-paste exact) |
|---|---|
| **N:1 (mandatory FK)** | `endArrow=ERmany;startArrow=ERone;html=1;rounded=0;` |
| **N:1 (optional/nullable FK)** | `endArrow=ERmany;startArrow=ERoneToMany;html=1;rounded=0;` |
| **1:1 (UNIQUE FK)** | `endArrow=ERone;startArrow=ERone;html=1;rounded=0;` |
| **M:N (junction)** | Draw 2 separate N:1 lines từ junction table tới 2 parent tables |
| **Self-FK** | `endArrow=ERmany;startArrow=ERoneToMany;html=1;rounded=0;curved=1;` + bend points loop về same table |

> **Tip:** Có thể customize routing — right-click → Edit Geometry → drag bend points để route line qua "lane" trống giữa các table.

---

## Drawing Workflow

### Phase 0 — Setup (15 phút)

1. Open project root, navigate to `db-schema/identity-user/schema.drawio` (start with smallest hub service).
2. Verify mở được, **không có connection lines** sẵn (chỉ có table boxes).
3. **Layout zones** — di chuyển tables vào vị trí, chưa vẽ line:
   - Center: User (hub)
   - Left column: Operator, OperatorSubscription, SubscriptionPlan
   - Right column: OAuthIdentity, RefreshToken, EmailVerificationToken
   - Bottom: UserDevice, ActivityLog
4. Save sau khi layout xong (**Ctrl+S**).

> **Tip:** Drawio auto-layout từ generator script đã đặt tables theo 4-column grid + shortest-column placement. Bạn có thể giữ nguyên hoặc re-layout theo hub-spoke.

---

### Phase 1 — Identity & User Service (30–45 phút)

**Tables:** 9 (`User`, `Operator`, `OAuthIdentity`, `RefreshToken`, `EmailVerificationToken`, `UserDevice`, `ActivityLog`, `SubscriptionPlan`, `OperatorSubscription`)
**Intra-service FKs:** 10
**Hubs:** `User` (5 inbound), `Operator` (2 inbound + via `User.operatorId`), `SubscriptionPlan` (2 inbound)
**Self-FK:** 1 (`RefreshToken.parentTokenId → RefreshToken.id`)

#### Step 1.1: Vẽ User hub relations (5 lines)

| # | From → To | Cardinality | Connector Style | Note |
|---|---|---|---|---|
| 1 | `OAuthIdentity.userId → User.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | UNIQUE composite `(userId, provider)` — 1 Google per user |
| 2 | `RefreshToken.userId → User.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | Family-based rotation |
| 3 | `EmailVerificationToken.userId → User.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | 3 purposes |
| 4 | `UserDevice.userId → User.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | Multi-device FCM |
| 5 | `ActivityLog.userId → User.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | Audit trail |

#### Step 1.2: Vẽ Operator hub relations (4 lines)

| # | From → To | Cardinality | Connector Style | Note |
|---|---|---|---|---|
| 6 | `User.operatorId → Operator.id` | N:1 (nullable) | `endArrow=ERmany;startArrow=ERoneToMany;` | NULL cho PASSENGER/SYSTEM_ADMIN |
| 7 | `OperatorSubscription.operatorId → Operator.id` | 1:1 (UNIQUE) | `endArrow=ERone;startArrow=ERone;` | 1 active subscription per operator |
| 8 | `OperatorSubscription.planId → SubscriptionPlan.id` | N:1 | `endArrow=ERmany;startArrow=ERone;` | Current plan |
| 9 | `OperatorSubscription.previousActivePlanId → SubscriptionPlan.id` | N:1 (nullable) | `endArrow=ERmany;startArrow=ERoneToMany;` | Revert flow |

#### Step 1.3: Self-reference (1 line)

| # | Relation | Visual hint |
|---|---|---|
| 10 | `RefreshToken.parentTokenId → RefreshToken.id` | Self-FK loop — vẽ vòng cung ngắn từ cạnh phải qua đỉnh table về lại cạnh trái. `curved=1;` style. |

#### Step 1.4: Validation
- [ ] **10 relation lines** drawn (intra-service count)
- [ ] Mọi FK column trong `identity-user/schema.sql` có line tương ứng
- [ ] Không có line cross ≥3 tables
- [ ] Self-FK `RefreshToken.parentTokenId` hiển thị rõ
- [ ] 5 satellite (OAuthIdentity/RefreshToken/EmailVerificationToken/UserDevice/ActivityLog) tỏa từ User không cross
- [ ] Save file (Ctrl+S)
- [ ] Export PNG: **File → Export As → PNG** (scale 2x). Save vào `db-schema/identity-user/erd-identity-user.png`

---

### Phase 2 — Trip-Route-Vehicle Service (45–60 phút — lớn nhất)

**Tables:** 20
**Intra-service FKs:** 31 (lớn nhất)
**Hubs:** `Trip` (6 inbound), `Stop` (5 inbound + 1 self-FK), `Route` (4 inbound + 1 self-FK), `Station` (4 inbound), `Vehicle` (3 inbound)
**Self-FKs:** 2 (`Stop.replacedByStopId`, `Route.returnRouteId`)

#### Step 2.1: Vẽ Trip hub relations (6 lines)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 1 | `TripSeat.tripId → Trip.id` | N:1 | CASCADE delete; UNIQUE `(trip_id, seat_number)` |
| 2 | `TripStop.tripId → Trip.id` | N:1 (composite PK) | CASCADE delete |
| 3 | `TripStopFare.tripId → Trip.id` | N:1 (composite PK) | CASCADE delete; exception only |
| 4 | `ShuttleTrip.mainTripId → Trip.id` | N:1 | RESTRICT |
| 5 | `ShuttlePassenger.mainTripId → Trip.id` | N:1 | RESTRICT |
| 6 | `Incident.tripId → Trip.id` | N:1 | RESTRICT |

#### Step 2.2: Vẽ Stop hub relations (5 + 1 self)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 7 | `RouteStop.stopId → Stop.id` | N:1 (composite PK) | — |
| 8 | `RouteStopFareTemplate.stopId → Stop.id` | N:1 | RESTRICT |
| 9 | `AlternativeRouteStop.stopId → Stop.id` | N:1 (composite PK) | — |
| 10 | `TripStop.stopId → Stop.id` | N:1 | RESTRICT |
| 11 | `TripStopFare.stopId → Stop.id` | N:1 | RESTRICT |
| 12 | `Stop.replacedByStopId → Stop.id` | N:1 (self, nullable) | SET NULL on delete; CHECK ≠ self |

#### Step 2.3: Vẽ Route hub relations (4 + 1 self)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 13 | `RouteStop.routeId → Route.id` | N:1 (composite PK) | CASCADE |
| 14 | `RouteStopFareTemplate.routeId → Route.id` | N:1 | CASCADE |
| 15 | `AlternativeRoute.routeId → Route.id` | N:1 | CASCADE; max 2 per Route enforced app-layer |
| 16 | `DriverSchedule.routeId → Route.id` | N:1 | RESTRICT |
| 17 | `Trip.routeId → Route.id` | N:1 | RESTRICT |
| 18 | `Route.returnRouteId → Route.id` | N:1 (self, nullable) | SET NULL |

#### Step 2.4: Vẽ Station + Vehicle hub relations (8 lines)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 19 | `Route.originStationId → Station.id` | N:1 | RESTRICT; CHECK origin ≠ dest |
| 20 | `Route.destinationStationId → Station.id` | N:1 | RESTRICT |
| 21 | `AlternativeRoute.destinationStationId → Station.id` | N:1 | RESTRICT |
| 22 | `OperatorStation.stationId → Station.id` | N:1 | UNIQUE composite `(operator_id, station_id)` |
| 23 | `ShuttleTrip.stationId → Station.id` | N:1 | RESTRICT |
| 24 | `Vehicle.vehicleTypeId → VehicleType.id` | N:1 | RESTRICT |
| 25 | `Trip.vehicleId → Vehicle.id` | N:1 | RESTRICT |
| 26 | `DriverSchedule.vehicleId → Vehicle.id` | N:1 (nullable) | SET NULL |
| 27 | `ShuttleTrip.vehicleId → Vehicle.id` | N:1 | RESTRICT |

#### Step 2.5: Vẽ secondary relations (4 lines)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 28 | `AlternativeRouteStop.alternativeRouteId → AlternativeRoute.id` | N:1 (composite PK) | CASCADE |
| 29 | `Trip.driverScheduleId → DriverSchedule.id` | N:1 (nullable) | SET NULL |
| 30 | `TripGenerationSkipLog.driverScheduleId → DriverSchedule.id` | N:1 | CASCADE |
| 31 | `ShuttlePassenger.shuttleTripId → ShuttleTrip.id` | N:1 (nullable) | SET NULL — passenger có thể đăng ký trước khi shuttle tạo |

#### Step 2.6: Validation
- [ ] **31 relation lines** drawn
- [ ] 2 self-FK loops (Stop, Route) hiển thị rõ
- [ ] 5 composite PK tables (RouteStop, AlternativeRouteStop, TripStop, TripStopFare, OperatorStation) có cả 2 FK marked PK
- [ ] Trip hub (6 inbound) không bị che
- [ ] Tip layout: đặt Trip ở trung tâm canvas, Stop trên Trip, Route trái-trên Trip, Vehicle dưới-trái, Station góc trái-trên xa
- [ ] Color-code lines per hub (vd lines đến Trip màu đỏ, đến Stop màu cam, đến Route màu xanh dương)
- [ ] Save + Export PNG → `db-schema/trip-route-vehicle/erd-trip-route-vehicle.png`

---

### Phase 3 — Booking Service (30–45 phút)

**Tables:** 9 (Booking, Passenger, BookingPendingAction, BookingTransfer, BookingStats, Voucher, VoucherUsage, OperatorVoucherConsent, OutboxEvent)
**Intra-service FKs:** 7
**Hub:** `Booking` (4 inbound)

#### Step 3.1: Booking hub relations (5 lines — incl Passenger sub-hub)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 1 | `Passenger.bookingId → Booking.id` | N:1 (max 5 — DB trigger) | CASCADE delete |
| 2 | `BookingPendingAction.bookingId → Booking.id` | N:1 (max 1 active — partial unique) | CASCADE |
| 3 | `BookingTransfer.bookingId → Booking.id` | N:1 | RESTRICT |
| 4 | `BookingTransfer.passengerId → Passenger.id` | N:1 | RESTRICT |
| 5 | `VoucherUsage.bookingId → Booking.id` | N:1 | CASCADE — DELETE khi booking CANCELLED/REFUNDED |

#### Step 3.2: Voucher cluster (2 lines)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 6 | `VoucherUsage.voucherId → Voucher.id` | N:1 | RESTRICT |
| 7 | `OperatorVoucherConsent.voucherId → Voucher.id` | N:1 | CASCADE; UNIQUE `(operator_id, voucher_id)` |

#### Step 3.3: Validation
- [ ] **7 relation lines** drawn
- [ ] Partial unique của `BookingPendingAction` (1 active per booking) — note "PARTIAL UNIQUE WHERE resolved_at IS NULL" trên line hoặc trong note
- [ ] Voucher cluster (Voucher, VoucherUsage, OperatorVoucherConsent) tách biệt với Booking cluster, không cross lines
- [ ] BookingStats không có line vào (leaf table — counter)
- [ ] Cardinality `Booking → Passenger` label `1..5` (max 5)
- [ ] Save + Export PNG → `db-schema/booking/erd-booking.png`

---

### Phase 4 — Payment & Wallet Service (30–45 phút)

**Tables:** 13 (Payment, TopUpRequest, Wallet, WalletTransaction, Invoice, PlatformWallet, PlatformWalletTransaction, OperatorWallet, OperatorWalletTransaction, OperatorTripSettlement, OperatorLedgerEntry, RefundFailureLog, OutboxEvent)
**Intra-service hard FKs:** 2 (Invoice→Payment, OperatorTripSettlement→OperatorWalletTransaction)
**Logical 1:N (no hard FK):** Wallet→WalletTransaction qua `user_id`, OperatorWallet→OperatorWalletTransaction qua `operator_id` — mirror pattern.
**Hubs:** không có (service coupling thấp ở DB layer; nhiều polymorphic logical FK cross-service)

#### Step 4.1: Wallet ledger lines (logical, no hard FK)

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 1 | `WalletTransaction.userId → Wallet.userId` (logical) | N:1 | Immutable ledger; mirror OperatorWallet pattern — match qua user_id, app-layer atomic enforce (optimistic lock row_version) |

#### Step 4.2: Invoice ↔ Payment

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 2 | `Invoice.paymentId → Payment.id` | 1:1 | RESTRICT; 1 invoice per subscription payment |

#### Step 4.3: Operator settlement ↔ wallet tx

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 3 | `OperatorTripSettlement.walletTransactionId → OperatorWalletTransaction.id` | N:1 (nullable) | SET NULL; set sau khi settled |

#### Step 4.4: Validation
- [ ] **3 relation lines** drawn
- [ ] OperatorWallet hiển thị PK = `operatorId` (1-1 with Operator note in margin)
- [ ] PlatformWallet hiển thị singleton note; PlatformWalletTransaction nằm cạnh holding pool
- [ ] Polymorphic reference (Payment.referenceId, PlatformWalletTransaction.referenceId, OperatorLedgerEntry.referenceId, OperatorWalletTransaction.referenceId) note rõ trong drawio (use Text label "polymorphic by referenceType")
- [ ] Service này coupling thấp ở DB layer — phần lớn relationship cross-service (xem `_global/cross-service-references.md`)
- [ ] Save + Export PNG → `db-schema/payment-wallet/erd-payment-wallet.png`

---

### Phase 5 — Parcel Service (30 phút)

**Tables:** 4 (Parcel, ParcelRouteFare, ParcelStats, OutboxEvent)
**Intra-service FKs:** 0 (mọi reference cross-service)
**Hubs:** N/A

#### Step 5.1: Không có intra-service relation lines

- Tất cả là leaf table với cross-service logical FK only.

#### Step 5.2: Validation
- [ ] 4 table box hiển thị đúng tên + columns
- [ ] **0 connection lines** (đúng — service này không có intra FK)
- [ ] `ParcelRouteFare` composite PK `(routeId, sizeCategory)` hiển thị 2 column PK marker
- [ ] Annotation tham khảo cross-service-references.md (40+ logical FK)
- [ ] Note `Parcel` có 40+ field — đảm bảo column list đầy đủ
- [ ] Save + Export PNG → `db-schema/parcel/erd-parcel.png`

---

### Phase 6 — Tracking Service (10 phút — minimal)

**Tables:** 2 (GpsTrail, OutboxEvent)
**Intra-service FKs:** 0
**Hubs:** N/A

#### Step 6.1: Validation
- [ ] 2 table box hiển thị đầy đủ column
- [ ] **0 connection lines**
- [ ] GpsTrail có CHECK constraint lat/lng/speed — annotation
- [ ] Note "Hầu hết state ở Redis (`tracking:latest:{tripId}`, `tracking:gps_buffer:{tripId}`, etc.)" làm context
- [ ] Save + Export PNG → `db-schema/tracking/erd-tracking.png`

---

### Phase 7 — Notification Service (10 phút — minimal)

**Tables:** 2 (Notification, NotificationDelivery)
**Intra-service FKs:** 1
**Hubs:** N/A

#### Step 7.1: 1 line

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 1 | `NotificationDelivery.notificationId → Notification.id` | N:1 | CASCADE |

#### Step 7.2: Validation
- [ ] 1 line drawn
- [ ] Note "NO OutboxEvent — Notification chỉ consume" trong drawing margin
- [ ] Enum `notification_type` column hiển thị "enum (33 values)" — KHÔNG expand
- [ ] Save + Export PNG → `db-schema/notification/erd-notification.png`

---

### Phase 8 — RAG AI Service (15 phút)

**Tables:** 5 (KnowledgeDocument, KnowledgeChunk, RagConversation, RagMessage, OutboxEvent)
**Intra-service FKs:** 2
**Hubs:** Mỗi cluster có 1 hub (KnowledgeDocument, RagConversation)

#### Step 8.1: 2 lines

| # | From → To | Cardinality | Note |
|---|---|---|---|
| 1 | `KnowledgeChunk.documentId → KnowledgeDocument.id` | N:1 | CASCADE; ivfflat embedding index |
| 2 | `RagMessage.conversationId → RagConversation.id` | N:1 | CASCADE |

#### Step 8.2: Validation
- [ ] 2 lines drawn
- [ ] 2 cluster riêng biệt (Knowledge base + Conversation), không cross 2 cluster (chỉ logical array reference `RagMessage.citedChunkIds UUID[]`)
- [ ] KnowledgeChunk hiển thị column `embedding vector(1536)` rõ
- [ ] Note pgvector extension required + IVFFlat cosine index
- [ ] RagMessage.citedChunkIds note polymorphic array reference (KHÔNG vẽ line)
- [ ] Save + Export PNG → `db-schema/rag-ai/erd-rag-ai.png`

---

### Phase 9 (Optional) — Cross-Service Overview Diagram (45 phút)

Tạo 1 file mới `db-schema/_global/cross-service-overview.drawio` cho stakeholder presentation / capstone defense slide.

#### Step 9.1: Layout 8 service boxes

Mỗi service = 1 **service box** lớn (KHÔNG hiện table chi tiết), chỉ gắn tên service + số table inside.

```
                  ┌─────────────────────┐
                  │   Notification      │
                  │   (2 tables)        │
                  └──────────┬──────────┘
                             │ N:1
┌────────────────┐     ┌─────▼──────────┐     ┌──────────────────┐
│   Tracking     │◄────┤ Identity &     │────▶│  Trip-Route-     │
│   (2 tables)   │ N:1 │ User (HUB)     │ N:1 │  Vehicle         │
│                │     │   (9 tables)   │     │  (20 tables)     │
└────────────────┘     └─────┬──────────┘     └────┬─────────────┘
                             │ N:1                 │ N:1
                       ┌─────▼──────────┐          │
                       │  Booking       │◄─────────┤
                       │  (9 tables)    │          │
                       └─────┬──────────┘          │
                             │ N:1                 │
                       ┌─────▼──────────┐          │
                       │  Payment       │◄─────────┤
                       │  & Wallet      │          │
                       │  (11 tables)   │          │
                       └────────────────┘          │
                                                   │
                       ┌────────────────┐          │
                       │  Parcel        │◄─────────┘
                       │  (4 tables)    │
                       └────────────────┘
                       ┌────────────────┐
                       │  RAG AI        │  (isolated)
                       │  (5 tables)    │
                       └────────────────┘
```

Layout chi tiết:
- **Center:** Identity & User (platform hub)
- **Top:** Notification
- **Top-left:** Tracking
- **Right-top:** Trip-Route-Vehicle (operational hub)
- **Right-mid:** Booking
- **Right-bottom:** Parcel
- **Bottom-center:** Payment & Wallet
- **Bottom-right (isolated):** RAG AI

#### Step 9.2: Vẽ cross-service logical FK lines

Use **dashed line** style để phân biệt với intra-service hard FK (solid). Color-code per target:

| # | From Service.Column | To Service.Entity | Cardinality | Color suggestion |
|---|---|---|---|---|
| 1 | All-services.operatorId | Identity.Operator | N:1 | Green (matches Operator group) |
| 2 | All-services.userId variants | Identity.User | N:1 | Blue (matches User group) |
| 3 | Booking.tripId | TripRouteVehicle.Trip | N:1 | Red |
| 4 | Booking.pickup/dropoffStationId | TripRouteVehicle.Station | N:1 | Red |
| 5 | Booking.pickup/dropoffStopId | TripRouteVehicle.Stop | N:1 | Red |
| 6 | Parcel.tripId | TripRouteVehicle.Trip | N:1 | Red |
| 7 | Parcel.dropoffStopId | TripRouteVehicle.Stop | N:1 | Red |
| 8 | Parcel.additionalPaymentId | Payment.Payment | N:1 | Purple |
| 9 | Payment.referenceId (BOOKING) | Booking.Booking | N:1 | Yellow (polymorphic, dotted) |
| 10 | Payment.referenceId (PARCEL) | Parcel.Parcel | N:1 | Orange (polymorphic, dotted) |
| 11 | OperatorLedgerEntry.tripId | TripRouteVehicle.Trip | N:1 | Red |
| 12 | OperatorTripSettlement.tripId | TripRouteVehicle.Trip | N:1 | Red |
| 13 | Tracking.GpsTrail.tripId | TripRouteVehicle.Trip | N:1 | Red |
| 14 | Notification.userId | Identity.User | N:1 | Blue |
| 15 | RagAI.uploaderByUserId etc. | Identity.User | N:1 | Blue |
| 16 | ShuttlePassenger.bookingId | Booking.Booking | N:1 | Yellow |

**Full list available in** `_global/cross-service-references.md` (89 logical FK rows).

#### Step 9.3: Label key flows

Trên line, add text label cho key flow:
- "validate tripId @ POST /v1/bookings" (sync HTTP)
- "consume payment.payment.succeeded" (async event)
- "consume trip.trip.completed → INSERT TripSettlement" (async event)

#### Step 9.4: Export

- Save: `_global/cross-service-overview.drawio`
- Export PNG: `_global/erd-cross-service-overview.png` (scale 2x cho slide HD)

---

## Common Layout Patterns

### Hub-and-Spoke (cho service có 1 entity dominant)

- Hub (User, Trip, Booking) đặt giữa canvas
- Satellites bao quanh radial
- Khoảng cách hub → satellite: ~200px
- Line ngắn, ít cross

### Pipeline (cho flow tuần tự)

- Booking → Payment → PlatformWalletTransaction (hold) + OperatorLedgerEntry (audit) → ... → OperatorTripSettlement → PlatformWallet debit + OperatorWallet credit
- Đặt thẳng hàng từ trái qua phải
- Line đi thẳng, không bend

### Junction Centered

- RouteStop ở giữa Route và Stop
- 2 line N:1 từ junction outward
- Đặt junction ngay trên line nối tâm Route ↔ Stop

### Audit Tail

- ActivityLog, BookingTransfer, OperatorLedgerEntry, NotificationDelivery, RefundFailureLog, GpsTrail
- Đặt dưới cùng canvas
- Line đi xuống từ entity chính

### Cluster (cho RAG)

- 2 cluster riêng (Knowledge: Document → Chunk; Conversation: Conversation → Message)
- Không có line cross-cluster
- Note polymorphic array reference

---

## Troubleshooting

### Lines crossing quá nhiều
- Re-layout: di chuyển satellite table sang phía khác hub
- Dùng bend points: click giữa line → add waypoint
- Đổi exit/entry side: line ra từ cạnh dưới thay vì phải (Edit Geometry)

### Tables quá đông (Phase 2 — Trip service)
- Tăng pageWidth/pageHeight trong drawio File → Page Setup
- Group tables theo subdomain (vd container "Route catalog group" chứa Route, RouteStop, RouteStopFareTemplate, AlternativeRoute, AlternativeRouteStop)
- Dùng container/group khoanh vùng (right-click → Edit Style → `swimlane` shape)

### Cardinality không rõ
- Add text label trên line: `1..*`, `0..1`, `1..1`, `1..5`
- Hoặc dùng connector arrow style ER-standard (đã có style strings ở Quick Reference table)

### Self-FK loop xấu
- Curve line, đặt label "self-FK: parent_id"
- Style `curved=1;` + 2 bend points ở 2 góc
- Đặt label "→ self" gần loop

---

## Output Per Service (sau khi vẽ xong)

Mỗi service phải có:
- ✅ `db-schema/<service>/schema.drawio` (updated với connections)
- ✅ `db-schema/<service>/erd-<service>.png` (export PNG, scale 2x cho HD)
- ✅ Commit cả 2 vào git

Sau Phase 9 (optional):
- ✅ `db-schema/_global/cross-service-overview.drawio`
- ✅ `db-schema/_global/erd-cross-service-overview.png`

---

## Final Checklist (sau Phase 8 hoặc Phase 9)

- [ ] 8 service ERDs đã vẽ xong + export PNG
- [ ] Mọi intra-service FK trong `<service>/schema.sql` có line trong `<service>/schema.drawio` (tổng 54 FK)
- [ ] Mọi cross-service logical FK trong `_global/cross-service-references.md` có trong cross-service-overview (nếu vẽ Phase 9)
- [ ] PNGs commit vào git (8 file + optional 1 cross-service)
- [ ] (Optional) Compile all PNGs vào 1 PDF cho capstone defense slide

---

## Estimated Total Time

| Phase | Service | Tables | FK | Time | Cumulative |
|---|---|---|---|---|---|
| Phase 0 | Setup | — | — | 15 min | 15 min |
| Phase 1 | Identity & User | 9 | 10 | 30–45 min | 1 hr |
| Phase 2 | Trip-Route-Vehicle | 20 | 31 | 45–60 min | 2 hr |
| Phase 3 | Booking | 9 | 7 | 30–45 min | 2.75 hr |
| Phase 4 | Payment & Wallet | 13 | 3 | 35–50 min | 3.5 hr |
| Phase 5 | Parcel | 4 | 0 | 30 min | 4 hr |
| Phase 6 | Tracking | 2 | 0 | 10 min | 4.2 hr |
| Phase 7 | Notification | 2 | 1 | 10 min | 4.3 hr |
| Phase 8 | RAG AI | 5 | 2 | 15 min | 4.5 hr |
| Phase 9 (optional) | Cross-service overview | — | — | 45 min | 5.25 hr |

**Recommend session split (tránh fatigue):**
- **Session 1 (2 hr):** Phase 0 + Phase 1 + Phase 2 (hardest — Trip service)
- **Session 2 (1.5 hr):** Phase 3 + Phase 4 + Phase 5
- **Session 3 (45 min):** Phase 6 + Phase 7 + Phase 8 + (optional) Phase 9

---

## Per-Service Reference (chi tiết)

Mỗi service vẫn có `<service>/erd-drawing-guide.md` riêng với chi tiết hơn về:
- Statistics chi tiết per service
- Recommended layout zones
- Phase-by-phase drawing order
- Drawing tips specific
- Validation checklist riêng

Master guide (file này) consolidate workflow tổng + connector reference + estimated time. Per-service guide là detail reference khi user cần đào sâu.

---

## Sign-off

Sau khi hoàn tất 8 service ERDs:
- Schema DB sealed
- Visual ERD complete
- Sẵn sàng cho phase Auth Service implementation
- Capstone defense slide có 8 PNG (+ optional cross-service overview)
