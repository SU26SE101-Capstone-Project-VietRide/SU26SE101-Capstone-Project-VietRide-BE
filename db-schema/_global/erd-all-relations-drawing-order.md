# VietRide - All ERD Relations Drawing Order

> File nay tong hop cac relation can ve cho DB schema VietRide.
> Dung file nay khi ban da co table boxes trong draw.io va can biet nen noi line nao truoc.

## Cach doc relation

- Cot `FK -> PK` viet theo huong database: bang con / cot FK tro den bang cha / cot PK.
- Khi ve ERD, doc nguoc lai theo nghiep vu: bang cha `1` -> bang con `N`.
- Vi du `OAuthIdentity.userId -> User.id` nghia la ve `User 1 -> N OAuthIdentity`.
- Intra-service FK: ve trong `db-schema/<service>/schema.drawio` bang solid line.
- Cross-service logical FK: chi ve trong diagram tong quan lien service, nen dung dashed/dotted line. Khong ve cac line nay trong file `schema.drawio` rieng cua tung service.

## Thu tu ve tong the

| Thu tu | Service / diagram | File can mo | Muc tieu |
|---|---|---|---|
| 1 | Identity & User | `db-schema/identity-user/schema.drawio` | Ve hub nen tang `User`, `Operator`, `SubscriptionPlan` |
| 2 | Trip-Route-Vehicle | `db-schema/trip-route-vehicle/schema.drawio` | Ve operational hub: `Trip`, `Route`, `Stop`, `Station`, `Vehicle` |
| 3 | Booking | `db-schema/booking/schema.drawio` | Ve booking hub va voucher cluster |
| 4 | Payment & Wallet | `db-schema/payment-wallet/schema.drawio` | Ve wallet ledger, invoice, settlement |
| 5 | Parcel | `db-schema/parcel/schema.drawio` | Khong co intra-service FK; chi can sap xep table |
| 6 | Tracking | `db-schema/tracking/schema.drawio` | Khong co intra-service FK; chi can sap xep table |
| 7 | Notification | `db-schema/notification/schema.drawio` | Ve `Notification -> NotificationDelivery` |
| 8 | RAG AI | `db-schema/rag-ai/schema.drawio` | Ve 2 cluster: document/chunk va conversation/message |
| 9 | Cross-service overview | `_global/cross-service-overview.drawio` neu can | Ve logical FK giua cac service |

## Connector quick rule

| Relation | Draw.io cardinality |
|---|---|
| N:1 | Dat `1` o phia bang cha / PK, dat `many` o phia bang con / FK |
| 1:1 | Dat `1` o ca 2 dau |
| Nullable FK | Van noi line, nhung label co the ghi `0..1` o phia optional |
| Self-FK | Ve loop quay ve cung table |
| Cross-service logical FK | Dashed/dotted line, label them source column neu can |

---

# Part 1 - Intra-Service Relations

## 1. Identity & User Service

Ve trong `db-schema/identity-user/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `OAuthIdentity.userId -> User.id` | `User -> OAuthIdentity` | 1:N | ON DELETE CASCADE |
| 2 | `RefreshToken.userId -> User.id` | `User -> RefreshToken` | 1:N | ON DELETE CASCADE |
| 3 | `EmailVerificationToken.userId -> User.id` | `User -> EmailVerificationToken` | 1:N | ON DELETE CASCADE |
| 4 | `UserDevice.userId -> User.id` | `User -> UserDevice` | 1:N | ON DELETE CASCADE |
| 5 | `ActivityLog.userId -> User.id` | `User -> ActivityLog` | 1:N | ON DELETE RESTRICT |
| 6 | `User.operatorId -> Operator.id` | `Operator -> User` | 1:N | Nullable cho `PASSENGER`/`SYSTEM_ADMIN` |
| 7 | `OperatorSubscription.operatorId -> Operator.id` | `Operator -> OperatorSubscription` | 1:1 | UNIQUE |
| 8 | `OperatorSubscription.planId -> SubscriptionPlan.id` | `SubscriptionPlan -> OperatorSubscription` | 1:N | Current plan |
| 9 | `OperatorSubscription.previousActivePlanId -> SubscriptionPlan.id` | `SubscriptionPlan -> OperatorSubscription` | 1:N | Nullable, revert flow |
| 10 | `RefreshToken.parentTokenId -> RefreshToken.id` | `RefreshToken -> RefreshToken` | 1:N self | Self-loop |

Luu y: guide cu trong folder Identity ghi statistics la 9 FK, nhung `schema.sql` va drawing order co 10 relation. Khi ve, dung 10 line o tren.

## 2. Trip-Route-Vehicle Service

Ve trong `db-schema/trip-route-vehicle/schema.drawio`.

### 2.1 Trip hub

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `TripSeat.tripId -> Trip.id` | `Trip -> TripSeat` | 1:N | CASCADE delete |
| 2 | `TripStop.tripId -> Trip.id` | `Trip -> TripStop` | 1:N | Composite PK |
| 3 | `TripStopFare.tripId -> Trip.id` | `Trip -> TripStopFare` | 1:N | Composite PK; exception only |
| 4 | `ShuttleTrip.mainTripId -> Trip.id` | `Trip -> ShuttleTrip` | 1:N | RESTRICT |
| 5 | `ShuttlePassenger.mainTripId -> Trip.id` | `Trip -> ShuttlePassenger` | 1:N | RESTRICT |
| 6 | `Incident.tripId -> Trip.id` | `Trip -> Incident` | 1:N | RESTRICT |

### 2.2 Stop hub

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 7 | `RouteStop.stopId -> Stop.id` | `Stop -> RouteStop` | 1:N | Composite PK |
| 8 | `RouteStopFareTemplate.stopId -> Stop.id` | `Stop -> RouteStopFareTemplate` | 1:N | RESTRICT |
| 9 | `AlternativeRouteStop.stopId -> Stop.id` | `Stop -> AlternativeRouteStop` | 1:N | Composite PK |
| 10 | `TripStop.stopId -> Stop.id` | `Stop -> TripStop` | 1:N | RESTRICT |
| 11 | `TripStopFare.stopId -> Stop.id` | `Stop -> TripStopFare` | 1:N | RESTRICT |
| 12 | `Stop.replacedByStopId -> Stop.id` | `Stop -> Stop` | 1:N self | Nullable self-loop |

### 2.3 Route hub

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 13 | `RouteStop.routeId -> Route.id` | `Route -> RouteStop` | 1:N | Composite PK; CASCADE |
| 14 | `RouteStopFareTemplate.routeId -> Route.id` | `Route -> RouteStopFareTemplate` | 1:N | CASCADE |
| 15 | `AlternativeRoute.routeId -> Route.id` | `Route -> AlternativeRoute` | 1:N | CASCADE |
| 16 | `DriverSchedule.routeId -> Route.id` | `Route -> DriverSchedule` | 1:N | RESTRICT |
| 17 | `Trip.routeId -> Route.id` | `Route -> Trip` | 1:N | RESTRICT |
| 18 | `Route.returnRouteId -> Route.id` | `Route -> Route` | 1:N self | Nullable self-loop |

### 2.4 Station and Vehicle hubs

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 19 | `Route.originStationId -> Station.id` | `Station -> Route` | 1:N | RESTRICT |
| 20 | `Route.destinationStationId -> Station.id` | `Station -> Route` | 1:N | RESTRICT |
| 21 | `AlternativeRoute.destinationStationId -> Station.id` | `Station -> AlternativeRoute` | 1:N | RESTRICT |
| 22 | `OperatorStation.stationId -> Station.id` | `Station -> OperatorStation` | 1:N | RESTRICT |
| 23 | `ShuttleTrip.stationId -> Station.id` | `Station -> ShuttleTrip` | 1:N | RESTRICT |
| 24 | `Vehicle.vehicleTypeId -> VehicleType.id` | `VehicleType -> Vehicle` | 1:N | RESTRICT |
| 25 | `Trip.vehicleId -> Vehicle.id` | `Vehicle -> Trip` | 1:N | RESTRICT |
| 26 | `DriverSchedule.vehicleId -> Vehicle.id` | `Vehicle -> DriverSchedule` | 1:N | Nullable, SET NULL |
| 27 | `ShuttleTrip.vehicleId -> Vehicle.id` | `Vehicle -> ShuttleTrip` | 1:N | RESTRICT |

### 2.5 Secondary relations

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 28 | `AlternativeRouteStop.alternativeRouteId -> AlternativeRoute.id` | `AlternativeRoute -> AlternativeRouteStop` | 1:N | CASCADE |
| 29 | `Trip.driverScheduleId -> DriverSchedule.id` | `DriverSchedule -> Trip` | 1:N | Nullable, SET NULL |
| 30 | `TripGenerationSkipLog.driverScheduleId -> DriverSchedule.id` | `DriverSchedule -> TripGenerationSkipLog` | 1:N | CASCADE |
| 31 | `ShuttlePassenger.shuttleTripId -> ShuttleTrip.id` | `ShuttleTrip -> ShuttlePassenger` | 1:N | Nullable, SET NULL |

## 3. Booking Service

Ve trong `db-schema/booking/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `Passenger.bookingId -> Booking.id` | `Booking -> Passenger` | 1:N | Max 5 passengers enforced app-layer |
| 2 | `BookingPendingAction.bookingId -> Booking.id` | `Booking -> BookingPendingAction` | 1:N | Partial unique active: 1 active per booking |
| 3 | `BookingTransfer.bookingId -> Booking.id` | `Booking -> BookingTransfer` | 1:N | RESTRICT |
| 4 | `BookingTransfer.passengerId -> Passenger.id` | `Passenger -> BookingTransfer` | 1:N | RESTRICT |
| 5 | `VoucherUsage.bookingId -> Booking.id` | `Booking -> VoucherUsage` | 1:N | CASCADE |
| 6 | `VoucherUsage.voucherId -> Voucher.id` | `Voucher -> VoucherUsage` | 1:N | RESTRICT |
| 7 | `OperatorVoucherConsent.voucherId -> Voucher.id` | `Voucher -> OperatorVoucherConsent` | 1:N | CASCADE; unique `(operatorId, voucherId)` |

## 4. Payment & Wallet Service

Ve trong `db-schema/payment-wallet/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `WalletTransaction.userId -> Wallet.userId` (logical, no hard FK) | `Wallet -> WalletTransaction` | 1:N | Immutable ledger; mirror OperatorWallet pattern — match qua user_id, app-layer atomic INSERT+UPDATE enforce |
| 2 | `Invoice.paymentId -> Payment.id` | `Payment -> Invoice` | 1:1 | 1 invoice per subscription payment |
| 3 | `OperatorTripSettlement.walletTransactionId -> OperatorWalletTransaction.id` | `OperatorWalletTransaction -> OperatorTripSettlement` | 1:N | Nullable, set after settled |

## 5. Parcel Service

Ve trong `db-schema/parcel/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| - | Khong co intra-service FK | - | - | Tat ca relation quan trong cua Parcel la logical FK sang service khac |

## 6. Tracking Service

Ve trong `db-schema/tracking/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| - | Khong co intra-service FK | - | - | `GpsTrail.tripId` la cross-service logical FK sang Trip |

## 7. Notification Service

Ve trong `db-schema/notification/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `NotificationDelivery.notificationId -> Notification.id` | `Notification -> NotificationDelivery` | 1:N | CASCADE |

## 8. RAG AI Service

Ve trong `db-schema/rag-ai/schema.drawio`.

| # | FK -> PK | ERD doc la | Cardinality | Note |
|---|---|---|---|---|
| 1 | `KnowledgeChunk.documentId -> KnowledgeDocument.id` | `KnowledgeDocument -> KnowledgeChunk` | 1:N | CASCADE; embedding index |
| 2 | `RagMessage.conversationId -> RagConversation.id` | `RagConversation -> RagMessage` | 1:N | CASCADE |

---

# Part 2 - Cross-Service Logical Relations

Dung phan nay sau khi da ve xong 8 service rieng. Nen tao diagram rieng: `db-schema/_global/cross-service-overview.drawio`.

Coverage note:

- Phan intra-service ben tren bao gom 54 FK that su co `REFERENCES` trong cac `schema.sql`.
- Phan cross-service ben duoi lay tu `_global/cross-service-references.md` va da gom cac dong trung nhau.
- `_global/cross-service-references.md` co lap `ShuttlePassenger.booking_id` va `Parcel.additional_payment_id` o nhieu section; file nay chi giu moi relation do 1 lan de ve khong bi duplicate.
- `Invoice.payment_id -> Payment.id` xuat hien trong `_global/cross-service-references.md`, nhung day la intra-service FK cua Payment & Wallet, nen da duoc dat o Part 1.

## Layout cross-service nen dat truoc khi noi line

| Thu tu dat box | Service box | Vi tri goi y |
|---|---|---|
| 1 | Identity & User | Trung tam; hub lon nhat |
| 2 | Trip-Route-Vehicle | Ben phai Identity; operational hub |
| 3 | Booking | Giua Identity va Trip, lech len tren |
| 4 | Payment & Wallet | Ben duoi Identity |
| 5 | Parcel | Ben phai Trip |
| 6 | Tracking | Duoi Trip |
| 7 | Notification | Duoi Identity, lech trai |
| 8 | RAG AI | Goc duoi phai, gan nhu isolated |

## Thu tu noi cross-service

1. Noi tat ca line ve `Identity.User` / `Identity.Operator` truoc, vi day la platform hub.
2. Noi cac line ve `Trip`, `Route`, `Station`, `Stop` tiep theo.
3. Noi cac line ve `Booking`.
4. Noi cac line ve `Parcel`.
5. Noi cac line ve `Payment`.
6. Cuoi cung moi them polymorphic/reference lines va event-flow labels neu can.

## 9.1 Cross-service lines to Identity & User

| # | Source service | FK / logical column -> Target | ERD doc la | Cardinality | Note |
|---|---|---|---|---|---|
| 1 | Trip-Route-Vehicle | `OperatorStation.operator_id -> identity.operators.id` | `Operator -> OperatorStation` | 1:N | HTTP validate |
| 2 | Trip-Route-Vehicle | `Stop.operator_id -> identity.operators.id` | `Operator -> Stop` | 1:N | HTTP validate |
| 3 | Trip-Route-Vehicle | `Route.operator_id -> identity.operators.id` | `Operator -> Route` | 1:N | Tenant filter |
| 4 | Trip-Route-Vehicle | `Vehicle.operator_id -> identity.operators.id` | `Operator -> Vehicle` | 1:N | Tenant filter |
| 5 | Trip-Route-Vehicle | `DriverSchedule.operator_id -> identity.operators.id` | `Operator -> DriverSchedule` | 1:N | Tenant filter |
| 6 | Trip-Route-Vehicle | `DriverSchedule.driver_user_id -> identity.users.id` | `User -> DriverSchedule` | 1:N | Role DRIVER |
| 7 | Trip-Route-Vehicle | `DriverSchedule.assistant_user_id -> identity.users.id` | `User -> DriverSchedule` | 1:N | Role ASSISTANT, nullable |
| 8 | Trip-Route-Vehicle | `Trip.operator_id -> identity.operators.id` | `Operator -> Trip` | 1:N | Tenant filter |
| 9 | Trip-Route-Vehicle | `Trip.driver_user_id -> identity.users.id` | `User -> Trip` | 1:N | HTTP validate |
| 10 | Trip-Route-Vehicle | `Trip.assistant_user_id -> identity.users.id` | `User -> Trip` | 1:N | Nullable |
| 11 | Trip-Route-Vehicle | `Trip.cancelled_by_user_id -> identity.users.id` | `User -> Trip` | 1:N | Implicit from caller JWT |
| 12 | Trip-Route-Vehicle | `Trip.completed_by_user_id -> identity.users.id` | `User -> Trip` | 1:N | Implicit |
| 13 | Trip-Route-Vehicle | `ShuttleTrip.operator_id -> identity.operators.id` | `Operator -> ShuttleTrip` | 1:N | Tenant filter |
| 14 | Trip-Route-Vehicle | `ShuttleTrip.driver_user_id -> identity.users.id` | `User -> ShuttleTrip` | 1:N | HTTP validate |
| 15 | Trip-Route-Vehicle | `Incident.reported_by_user_id -> identity.users.id` | `User -> Incident` | 1:N | Implicit |
| 16 | Trip-Route-Vehicle | `Incident.resolved_by_user_id -> identity.users.id` | `User -> Incident` | 1:N | Implicit |
| 17 | Trip-Route-Vehicle | `TripGenerationSkipLog.operator_id -> identity.operators.id` | `Operator -> TripGenerationSkipLog` | 1:N | Implicit |
| 18 | Booking | `Booking.passenger_user_id -> identity.users.id` | `User -> Booking` | 1:N | Role PASSENGER |
| 19 | Booking | `Booking.operator_id -> identity.operators.id` | `Operator -> Booking` | 1:N | Denormalized from Trip |
| 20 | Booking | `BookingTransfer.transferred_by_user_id -> identity.users.id` | `User -> BookingTransfer` | 1:N | Implicit |
| 21 | Booking | `Voucher.created_by_user_id -> identity.users.id` | `User -> Voucher` | 1:N | Role SYSTEM_ADMIN |
| 22 | Booking | `VoucherUsage.user_id -> identity.users.id` | `User -> VoucherUsage` | 1:N | Implicit from booking |
| 23 | Booking | `OperatorVoucherConsent.operator_id -> identity.operators.id` | `Operator -> OperatorVoucherConsent` | 1:N | Tenant filter |
| 24 | Booking | `OperatorVoucherConsent.responded_by_user_id -> identity.users.id` | `User -> OperatorVoucherConsent` | 1:N | Role OPERATOR_ADMIN |
| 25 | Booking | `BookingStats.operator_id -> identity.operators.id` | `Operator -> BookingStats` | 1:N | Counter |
| 26 | Booking | `Voucher.applicable_operator_ids[] -> identity.operators.id[]` | `Operator -> Voucher` | N:N | Array logical reference |
| 27 | Payment & Wallet | `Payment.user_id -> identity.users.id` | `User -> Payment` | 1:N | Nullable |
| 28 | Payment & Wallet | `Payment.operator_id -> identity.operators.id` | `Operator -> Payment` | 1:N | Nullable |
| 29 | Payment & Wallet | `TopUpRequest.user_id -> identity.users.id` | `User -> TopUpRequest` | 1:N | HTTP validate |
| 30 | Payment & Wallet | `Wallet.user_id -> identity.users.id` | `User -> Wallet` | 1:1 | UNIQUE |
| 31 | Payment & Wallet | `Invoice.operator_id -> identity.operators.id` | `Operator -> Invoice` | 1:N | From subscription |
| 32 | Payment & Wallet | `Invoice.operator_subscription_id -> identity.operator_subscriptions.id` | `OperatorSubscription -> Invoice` | 1:N | HTTP validate |
| 33 | Payment & Wallet | `OperatorWallet.operator_id -> identity.operators.id` | `Operator -> OperatorWallet` | 1:1 | PK 1:1 |
| 34 | Payment & Wallet | `OperatorWalletTransaction.operator_id -> identity.operators.id` | `Operator -> OperatorWalletTransaction` | 1:N | Denormalized |
| 35 | Payment & Wallet | `OperatorLedgerEntry.operator_id -> identity.operators.id` | `Operator -> OperatorLedgerEntry` | 1:N | Audit-only |
| 36 | Payment & Wallet | `OperatorTripSettlement.operator_id -> identity.operators.id` | `Operator -> OperatorTripSettlement` | 1:N | Event-driven |
| 37 | Payment & Wallet | `OperatorTripSettlement.settled_by_user_id -> identity.users.id` | `User -> OperatorTripSettlement` | 1:N | SYSTEM_ADMIN |
| 38 | Payment & Wallet | `RefundFailureLog.resolved_by_user_id -> identity.users.id` | `User -> RefundFailureLog` | 1:N | SYSTEM_ADMIN |
| 39 | Parcel | `Parcel.sender_user_id -> identity.users.id` | `User -> Parcel` | 1:N | Required |
| 40 | Parcel | `Parcel.recipient_user_id -> identity.users.id` | `User -> Parcel` | 1:N | Nullable |
| 41 | Parcel | `Parcel.operator_id -> identity.operators.id` | `Operator -> Parcel` | 1:N | Denormalized from Trip |
| 42 | Parcel | `Parcel.reviewed_by_user_id -> identity.users.id` | `User -> Parcel` | 1:N | Operator role |
| 43 | Parcel | `Parcel.confirmed_by_user_id -> identity.users.id` | `User -> Parcel` | 1:N | Implicit |
| 44 | Parcel | `Parcel.transfer_confirmed_by_user_id -> identity.users.id` | `User -> Parcel` | 1:N | DRIVER/ASSISTANT |
| 45 | Parcel | `Parcel.returned_by_user_id -> identity.users.id` | `User -> Parcel` | 1:N | Implicit |
| 46 | Parcel | `ParcelRouteFare.operator_id -> identity.operators.id` | `Operator -> ParcelRouteFare` | 1:N | Denormalized |
| 47 | Parcel | `ParcelStats.operator_id -> identity.operators.id` | `Operator -> ParcelStats` | 1:N | Counter |
| 48 | Notification | `Notification.user_id -> identity.users.id` | `User -> Notification` | 1:N | From event payload |
| 49 | RAG AI | `KnowledgeDocument.uploaded_by_user_id -> identity.users.id` | `User -> KnowledgeDocument` | 1:N | Implicit |
| 50 | RAG AI | `KnowledgeDocument.approved_by_user_id -> identity.users.id` | `User -> KnowledgeDocument` | 1:N | SYSTEM_ADMIN |
| 51 | RAG AI | `RagConversation.user_id -> identity.users.id` | `User -> RagConversation` | 1:N | Implicit |

## 9.2 Cross-service lines to Trip-Route-Vehicle

| # | Source service | FK / logical column -> Target | ERD doc la | Cardinality | Note |
|---|---|---|---|---|---|
| 1 | Payment & Wallet | `OperatorLedgerEntry.trip_id -> trip.trips.id` | `Trip -> OperatorLedgerEntry` | 1:N | Nullable |
| 2 | Payment & Wallet | `OperatorTripSettlement.trip_id -> trip.trips.id` | `Trip -> OperatorTripSettlement` | 1:N | Event-driven |
| 3 | Booking | `Booking.trip_id -> trip.trips.id` | `Trip -> Booking` | 1:N | HTTP validate + snapshots |
| 4 | Booking | `Booking.pickup_station_id -> trip.stations.id` | `Station -> Booking` | 1:N | Exclusive with pickup stop |
| 5 | Booking | `Booking.pickup_stop_id -> trip.stops.id` | `Stop -> Booking` | 1:N | Validate pickup allowed |
| 6 | Booking | `Booking.dropoff_station_id -> trip.stations.id` | `Station -> Booking` | 1:N | Exclusive with dropoff stop |
| 7 | Booking | `Booking.dropoff_stop_id -> trip.stops.id` | `Stop -> Booking` | 1:N | Validate dropoff allowed |
| 8 | Booking | `Passenger.boarded_at_stop_id -> trip.stops.id` | `Stop -> Passenger` | 1:N | Implicit |
| 9 | Booking | `BookingTransfer.original_trip_id -> trip.trips.id` | `Trip -> BookingTransfer` | 1:N | Implicit |
| 10 | Booking | `BookingTransfer.new_trip_id -> trip.trips.id` | `Trip -> BookingTransfer` | 1:N | Implicit |
| 11 | Booking | `BookingStats.trip_id -> trip.trips.id` | `Trip -> BookingStats` | 1:N | Nullable counter |
| 12 | Booking | `Voucher.applicable_route_ids[] -> trip.routes.id[]` | `Route -> Voucher` | N:N | Array logical reference |
| 13 | Parcel | `Parcel.trip_id -> trip.trips.id` | `Trip -> Parcel` | 1:N | Validate status at create |
| 14 | Parcel | `Parcel.transfer_target_trip_id -> trip.trips.id` | `Trip -> Parcel` | 1:N | Nullable |
| 15 | Parcel | `Parcel.dropoff_stop_id -> trip.stops.id` | `Stop -> Parcel` | 1:N | Nullable |
| 16 | Parcel | `ParcelRouteFare.route_id -> trip.routes.id` | `Route -> ParcelRouteFare` | 1:N | Composite PK |
| 17 | Tracking | `GpsTrail.trip_id -> trip.trips.id` | `Trip -> GpsTrail` | 1:N | Socket auth |

## 9.3 Cross-service lines to Booking

| # | Source service | FK / logical column -> Target | ERD doc la | Cardinality | Note |
|---|---|---|---|---|---|
| 1 | Payment & Wallet | `Payment.reference_id -> booking.bookings.id` when `reference_type=BOOKING` | `Booking -> Payment` | 1:N | Polymorphic dotted line |
| 2 | Payment & Wallet | `Payment.reference_id -> booking.bookings.booking_group_id` when `reference_type=BOOKING_GROUP` | `BookingGroup -> Payment` | 1:N | Polymorphic dotted line |
| 3 | Payment & Wallet | `OperatorLedgerEntry.reference_id -> booking.bookings.id` when `reference_type=BOOKING` | `Booking -> OperatorLedgerEntry` | 1:N | Polymorphic dotted line |
| 4 | Payment & Wallet | `OperatorLedgerEntry.reference_id -> booking.voucher_usages.id` when `reference_type=VOUCHER_USAGE` | `VoucherUsage -> OperatorLedgerEntry` | 1:N | Polymorphic dotted line |
| 5 | Payment & Wallet | `RefundFailureLog.booking_id -> booking.bookings.id` | `Booking -> RefundFailureLog` | 1:N | Nullable |
| 6 | Trip-Route-Vehicle | `ShuttlePassenger.booking_id -> booking.bookings.id` | `Booking -> ShuttlePassenger` | 1:N | Nullable optional link |

## 9.4 Cross-service lines to Parcel

| # | Source service | FK / logical column -> Target | ERD doc la | Cardinality | Note |
|---|---|---|---|---|---|
| 1 | Payment & Wallet | `Payment.reference_id -> parcel.parcels.id` when `reference_type=PARCEL` | `Parcel -> Payment` | 1:N | Polymorphic dotted line |
| 2 | Payment & Wallet | `OperatorLedgerEntry.reference_id -> parcel.parcels.id` when `reference_type=PARCEL` | `Parcel -> OperatorLedgerEntry` | 1:N | Polymorphic dotted line |
| 3 | Payment & Wallet | `RefundFailureLog.parcel_id -> parcel.parcels.id` | `Parcel -> RefundFailureLog` | 1:N | Nullable |

## 9.5 Cross-service lines to Payment & Wallet

| # | Source service | FK / logical column -> Target | ERD doc la | Cardinality | Note |
|---|---|---|---|---|---|
| 1 | Parcel | `Parcel.additional_payment_id -> payment.payments.id` | `Payment -> Parcel` | 1:N | Nullable reverse logical FK |

## 9.6 Polymorphic reference summary

Nhung dong nay khong nen ve tat ca thanh line chi tiet neu diagram bi roi. Neu can ve, dung dotted line va label `reference_type`.

| Source column | Possible targets |
|---|---|
| `payments.reference_id` | `BOOKING -> bookings.id`; `BOOKING_GROUP -> bookings.booking_group_id`; `PARCEL -> parcels.id`; `TOP_UP -> top_up_requests.id`; `SUBSCRIPTION -> operator_subscriptions.id` |
| `operator_ledger_entries.reference_id` | `BOOKING -> bookings.id`; `PARCEL -> parcels.id`; `VOUCHER_USAGE -> voucher_usages.id`; `MANUAL -> arbitrary uuid` |
| `operator_wallet_transactions.reference_id` | `TRIP_SETTLEMENT -> operator_trip_settlements.id`; `ADJUSTMENT -> arbitrary uuid`; v2: `WITHDRAWAL -> operator_withdrawal_requests.id` |
| `wallet_transactions.reference_id` | `TOP_UP -> top_up_requests.id`; `BOOKING_PAYMENT/REFUND -> bookings.id`; `PARCEL_PAYMENT/REFUND -> parcels.id`; `MANUAL_ADJUSTMENT -> NULL` |

---

# Final Checklist

- [ ] Da ve xong 10 relation trong Identity & User.
- [ ] Da ve xong 31 relation trong Trip-Route-Vehicle.
- [ ] Da ve xong 7 relation trong Booking.
- [ ] Da ve xong 3 relation trong Payment & Wallet.
- [ ] Parcel khong co intra-service relation line.
- [ ] Tracking khong co intra-service relation line.
- [ ] Da ve xong 1 relation trong Notification.
- [ ] Da ve xong 2 relation trong RAG AI.
- [ ] Neu co cross-service overview: da ve logical FK theo Part 2, dung dashed/dotted line.
- [ ] Khong ve cross-service line vao file `schema.drawio` rieng cua tung service.
