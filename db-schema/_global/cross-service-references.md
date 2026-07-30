# Cross-Service Logical FK — Single Source of Truth

> Cross-DB FK constraint **BỊ CẤM** ở DB layer (v6 Section 3.4). Mọi reference đến entity ở service khác là logical FK — column UUID + comment trong schema.sql + validation app-layer qua HTTP REST internal call (kèm Internal JWT).

## Enforcement patterns

| Pattern | Khi nào dùng | Note |
|---|---|---|
| **HTTP validate khi WRITE** | Tạo entity với FK đến service khác (vd `POST /v1/bookings` validate tripId + userId) | Internal JWT TTL 120s; Polly retry với exponential backoff. Hỏng → return validation error 422. |
| **Snapshot field** | Read-heavy data cần render UI mà không cross-service call (vd Booking.tripSnapshotOriginName) | Set tại CREATE, immutable. Operator edit Trip không update snapshot. |
| **Event consume** | Cascading state change (vd UserDeleted → cleanup bookings) | RabbitMQ at-least-once; Outbox đảm bảo publish. |
| **Tenant filter via Internal JWT** | `operator_id` claim từ JWT → mọi query bắt buộc WHERE operator_id = :claim | Enforced ở handler middleware. |

## Full logical FK list

### → Identity Service (User, Operator)

| Source service | Source column | Target | Cardinality | Enforcement |
|---|---|---|---|---|
| Trip-Route-Vehicle | `OperatorStation.operator_id` | `identity.operators.id` | N:1 | HTTP validate at OperatorStation create |
| Trip-Route-Vehicle | `Stop.operator_id` | `identity.operators.id` | N:1 | HTTP validate at Stop create |
| Trip-Route-Vehicle | `Route.operator_id` | `identity.operators.id` | N:1 | HTTP validate; tenant filter |
| Trip-Route-Vehicle | `Vehicle.operator_id` | `identity.operators.id` | N:1 | HTTP validate; tenant filter |
| Trip-Route-Vehicle | `DriverSchedule.operator_id` | `identity.operators.id` | N:1 | HTTP validate; tenant filter |
| Trip-Route-Vehicle | `DriverSchedule.driver_user_id` | `identity.users.id` (role=DRIVER) | N:1 | HTTP validate role+operator |
| Trip-Route-Vehicle | `DriverSchedule.assistant_user_id` | `identity.users.id` (role=ASSISTANT, nullable) | N:1 | HTTP validate |
| Trip-Route-Vehicle | `Trip.operator_id` | `identity.operators.id` | N:1 | tenant filter |
| Trip-Route-Vehicle | `Trip.driver_user_id` | `identity.users.id` | N:1 | HTTP validate |
| Trip-Route-Vehicle | `Trip.assistant_user_id` | `identity.users.id` | N:1 | HTTP validate |
| Trip-Route-Vehicle | `Trip.cancelled_by_user_id` | `identity.users.id` | N:1 | implicit from caller JWT |
| Trip-Route-Vehicle | `Trip.completed_by_user_id` | `identity.users.id` | N:1 | implicit |
| Trip-Route-Vehicle | `ShuttleTrip.operator_id` | `identity.operators.id` | N:1 | tenant filter |
| Trip-Route-Vehicle | `ShuttleTrip.driver_user_id` | `identity.users.id` | N:1 | HTTP validate |
| Trip-Route-Vehicle | `Incident.reported_by_user_id` | `identity.users.id` | N:1 | implicit |
| Trip-Route-Vehicle | `Incident.resolved_by_user_id` | `identity.users.id` | N:1 | implicit |
| Trip-Route-Vehicle | `TripGenerationSkipLog.operator_id` | `identity.operators.id` | N:1 | implicit |
| Booking | `Booking.passenger_user_id` | `identity.users.id` (role=PASSENGER) | N:1 | HTTP validate at Booking create |
| Booking | `Booking.operator_id` | `identity.operators.id` | N:1 | tenant filter (denormalized from Trip.operator_id) |
| Booking | `BookingTransfer.transferred_by_user_id` | `identity.users.id` | N:1 | implicit |
| Booking | `Voucher.created_by_user_id` | `identity.users.id` (role=SYSTEM_ADMIN) | N:1 | HTTP validate role |
| Booking | `VoucherUsage.user_id` | `identity.users.id` | N:1 | implicit from booking |
| Booking | `OperatorVoucherConsent.operator_id` | `identity.operators.id` | N:1 | tenant filter |
| Booking | `OperatorVoucherConsent.responded_by_user_id` | `identity.users.id` (role=OPERATOR_ADMIN) | N:1 | implicit |
| Booking | `BookingStats.operator_id` | `identity.operators.id` | N:1 | denormalized counter |
| Booking | `Voucher.applicable_operator_ids[]` | `identity.operators.id[]` | N:N | array; app-layer validate |
| Payment & Wallet | `Payment.user_id` | `identity.users.id` (nullable) | N:1 | HTTP validate |
| Payment & Wallet | `Payment.operator_id` | `identity.operators.id` (nullable) | N:1 | HTTP validate |
| Payment & Wallet | `TopUpRequest.user_id` | `identity.users.id` | N:1 | HTTP validate |
| Payment & Wallet | `Wallet.user_id` | `identity.users.id` (PK 1-1) | 1:1 | atomic create on `identity.user.created` event (UPSERT idempotent) |
| Payment & Wallet | `WalletTransaction.user_id` | `identity.users.id` | N:1 | denormalized for query; no hard FK to wallets table |
| Payment & Wallet | `Invoice.operator_id` | `identity.operators.id` | N:1 | implicit from subscription |
| Payment & Wallet | `Invoice.operator_subscription_id` | `identity.operator_subscriptions.id` | N:1 | HTTP validate |
| Payment & Wallet | `OperatorWallet.operator_id` | `identity.operators.id` (PK 1-1) | 1:1 | atomic create on Operator APPROVED event |
| Payment & Wallet | `OperatorWalletTransaction.operator_id` | `identity.operators.id` | N:1 | denormalized for query |
| Payment & Wallet | `OperatorLedgerEntry.operator_id` | `identity.operators.id` | N:1 | event-driven INSERT (audit-only) |
| Payment & Wallet | `OperatorTripSettlement.operator_id` | `identity.operators.id` | N:1 | event-driven INSERT on Trip terminal |
| Payment & Wallet | `OperatorTripSettlement.settled_by_user_id` | `identity.users.id` (role=SYSTEM_ADMIN) | N:1 | implicit (ADMIN_MANUAL only) |
| Payment & Wallet | `RefundFailureLog.resolved_by_user_id` | `identity.users.id` (role=SYSTEM_ADMIN) | N:1 | implicit |
| Parcel | `Parcel.sender_user_id` | `identity.users.id` (NOT NULL) | N:1 | HTTP validate at Parcel create |
| Parcel | `Parcel.recipient_user_id` | `identity.users.id` (nullable) | N:1 | HTTP lookup by email at create |
| Parcel | `Parcel.operator_id` | `identity.operators.id` | N:1 | denormalized from Trip |
| Parcel | `Parcel.reviewed_by_user_id` | `identity.users.id` (role=OPERATOR_*) | N:1 | implicit |
| Parcel | `Parcel.confirmed_by_user_id` | `identity.users.id` | N:1 | implicit |
| Parcel | `Parcel.transfer_confirmed_by_user_id` | `identity.users.id` (role=DRIVER/ASSISTANT) | N:1 | implicit |
| Parcel | `Parcel.transfer_confirmation_claimed_by_user_id` | `identity.users.id` (role=DRIVER/ASSISTANT) | N:1 | durable claim actor; no cross-DB FK |
| Parcel | `Parcel.returned_by_user_id` | `identity.users.id` | N:1 | implicit |
| Parcel | `ParcelDeliveryToken.issued_by_user_id` | `identity.users.id` (nullable) | N:1 | implicit authenticated issuer; null for migration backfill; logical reference only, no DB FK |
| Parcel | `ParcelCargoRecoveryOperation.actor_user_id` | `identity.users.id` | N:1 | durable Day-32 recovery actor; logical reference only |
| Parcel | `ParcelCargoRecoveryOperation.operator_id` | `identity.operators.id` | N:1 | frozen tenant scope; logical reference only |
| Parcel | `ParcelCargoRecoveryOperation.source_trip_id` | `trip.trips.id` | N:1 | frozen source cargo owner; logical reference only |
| Parcel | `ParcelCargoRecoveryOperation.target_trip_id` | `trip.trips.id` (nullable) | N:1 | TRANSFER target; null for RETURN; logical reference only |
| Parcel | `ParcelRouteFare.operator_id` | `identity.operators.id` | N:1 | denormalized |
| Parcel | `ParcelStats.operator_id` | `identity.operators.id` | N:1 | counter |
| Notification | `Notification.user_id` | `identity.users.id` | N:1 | implicit from event payload |
| RAG AI | `KnowledgeDocument.uploaded_by_user_id` | `identity.users.id` | N:1 | implicit |
| RAG AI | `KnowledgeDocument.approved_by_user_id` | `identity.users.id` (role=SYSTEM_ADMIN) | N:1 | implicit |
| RAG AI | `RagConversation.user_id` | `identity.users.id` | N:1 | implicit |

### → Trip-Route-Vehicle Service (Trip, Route, Station, Stop)

| Source service | Source column | Target | Cardinality | Enforcement |
|---|---|---|---|---|
| Payment & Wallet | `OperatorLedgerEntry.trip_id` | `trip.trips.id` (nullable) | N:1 | denormalized for TripSettlement netAmount SUM |
| Payment & Wallet | `OperatorTripSettlement.trip_id` | `trip.trips.id` | N:1 | event-driven on Trip terminal |
| Booking | `Booking.trip_id` | `trip.trips.id` | N:1 | HTTP validate + snapshot fields |
| Booking | `Booking.pickup_station_id` | `trip.stations.id` | N:1 | exclusive with pickup_stop_id |
| Booking | `Booking.pickup_stop_id` | `trip.stops.id` | N:1 | exclusive with pickup_station_id; validate allow_pickup=true |
| Booking | `Booking.dropoff_station_id` | `trip.stations.id` | N:1 | exclusive |
| Booking | `Booking.dropoff_stop_id` | `trip.stops.id` | N:1 | exclusive; validate allow_dropoff=true |
| Booking | `Passenger.boarded_at_stop_id` | `trip.stops.id` | N:1 | implicit |
| Booking | `BookingTransfer.original_trip_id` | `trip.trips.id` | N:1 | implicit |
| Booking | `BookingTransfer.new_trip_id` | `trip.trips.id` | N:1 | implicit |
| Booking | `BookingStats.trip_id` | `trip.trips.id` (nullable) | N:1 | counter |
| Booking | `Voucher.applicable_route_ids[]` | `trip.routes.id[]` | N:N | array |
| Parcel | `Parcel.trip_id` | `trip.trips.id` | N:1 | HTTP validate (status=SCHEDULED/BOARDING) at create |
| Parcel | `Parcel.transfer_target_trip_id` | `trip.trips.id` (nullable) | N:1 | set in Vehicle Substitution flow |
| Parcel | `Parcel.dropoff_stop_id` | `trip.stops.id` (nullable) | N:1 | validate allow_dropoff=true |
| Parcel | `ParcelRouteFare.route_id` | `trip.routes.id` | N:1 | composite PK |
| Tracking | `GpsTrail.trip_id` | `trip.trips.id` | N:1 | implicit (Socket.IO authorization at handshake) |
| Trip-Route-Vehicle | `ShuttlePassenger.booking_id` | `booking.bookings.id` (nullable) | N:1 | optional link |

### → Booking Service

| Source service | Source column | Target | Cardinality | Enforcement |
|---|---|---|---|---|
| Payment & Wallet | `Payment.reference_id` (when reference_type=BOOKING) | `booking.bookings.id` | N:1 | polymorphic |
| Payment & Wallet | `Payment.reference_id` (when reference_type=BOOKING_GROUP) | `booking.bookings.booking_group_id` | N:1 | polymorphic |
| Payment & Wallet | `OperatorLedgerEntry.reference_id` (when reference_type=BOOKING) | `booking.bookings.id` | N:1 | polymorphic |
| Payment & Wallet | `OperatorLedgerEntry.reference_id` (when reference_type=VOUCHER_USAGE) | `booking.voucher_usages.id` | N:1 | polymorphic |
| Payment & Wallet | `RefundFailureLog.booking_id` | `booking.bookings.id` (nullable) | N:1 | |
| Trip-Route-Vehicle | `ShuttlePassenger.booking_id` | `booking.bookings.id` (nullable) | N:1 | |

### → Parcel Service

| Source service | Source column | Target | Cardinality | Enforcement |
|---|---|---|---|---|
| Payment & Wallet | `Payment.reference_id` (when reference_type=PARCEL) | `parcel.parcels.id` | N:1 | polymorphic |
| Payment & Wallet | `OperatorLedgerEntry.reference_id` (when reference_type=PARCEL) | `parcel.parcels.id` | N:1 | polymorphic |
| Payment & Wallet | `RefundFailureLog.parcel_id` | `parcel.parcels.id` (nullable) | N:1 | |
| Parcel | `Parcel.additional_payment_id` | `payment.payments.id` (nullable) | N:1 | reverse — Parcel ref Payment |

### → Payment & Wallet Service

| Source service | Source column | Target | Cardinality | Enforcement |
|---|---|---|---|---|
| Payment & Wallet | `Invoice.payment_id` | `payment.payments.id` | 1:1 | intra-service (FK constraint exists) |
| Parcel | `Parcel.additional_payment_id` | `payment.payments.id` (nullable) | N:1 | reverse logical FK |

### → Polymorphic reference targets

| Source | Column | Possible targets |
|---|---|---|
| `payments.reference_id` | by `reference_type` | BOOKING → `bookings.id`; BOOKING_GROUP → `bookings.booking_group_id`; PARCEL → `parcels.id`; TOP_UP → `top_up_requests.id`; SUBSCRIPTION → `operator_subscriptions.id`. (v1 dropped: OPERATOR_TOP_UP.) |
| `operator_ledger_entries.reference_id` | by `reference_type` | BOOKING → `bookings.id`; PARCEL → `parcels.id`; VOUCHER_USAGE → `voucher_usages.id`; MANUAL → any (Admin adjustment). (v1 dropped: PAYOUT_BATCH.) |
| `operator_wallet_transactions.reference_id` | by `reference_type` | TRIP_SETTLEMENT → `operator_trip_settlements.id`; ADJUSTMENT → arbitrary uuid (Admin endpoint). (v2 adds WITHDRAWAL → `operator_withdrawal_requests.id`.) |
| `wallet_transactions.reference_id` | by `reference_type` | TOP_UP → `top_up_requests.id`; BOOKING_PAYMENT/REFUND → `bookings.id`; PARCEL_PAYMENT/REFUND → `parcels.id`; MANUAL_ADJUSTMENT → NULL. NOTE: `wallet_transactions.user_id` is logical FK to `wallets.user_id` (= `identity.users.id`), NO hard DB FK to `wallets` — mirrors `operator_wallet_transactions` pattern. |

## Cascading behavior (event-driven)

Event-driven cleanup khi parent entity bị soft delete / SUSPENDED:

| Event | Consumers | Cascade action |
|---|---|---|
| `identity.user.deleted` | Booking, Parcel, Payment, Notification, RAG | Anonymize PII (passenger_user_id, sender_user_id, etc.) — không xóa record. |
| `identity.operator.suspended` | Trip, Booking, Parcel | Trip-Route-Vehicle: block create Trip/Route/Vehicle. Booking/Parcel: block apply voucher (consent invalid). |
| `trip.trip.cancelled` | Booking, Parcel | Booking → CANCELLED + refund; Parcel → CANCELLED/PENDING_OPERATOR_ACTION per status. |
| `trip.trip.disrupted` (hasSubstitution=false) | Booking, Parcel | Booking → DISRUPTED + proportional refund; Parcel → PENDING_OPERATOR_ACTION. |
| `trip.trip.vehicle_substituted` | Booking, Parcel | Booking → BookingTransfer per Passenger; Parcel → PENDING_TRANSFER_CONFIRM. Parcel cargo moves only through Trip's atomic source-to-target internal transfer API after target-crew confirmation. |
| `payment.payment.succeeded` (referenceType=BOOKING) | Booking, Payment (self for hold + ledger) | Booking → CONFIRMED; INSERT PlatformWalletTransaction BOOKING_PAYMENT_HOLD + OperatorLedgerEntry BOOKING_REVENUE (audit-only, no OperatorWallet update). |
| `payment.wallet.credited` (referenceType=BOOKING_REFUND) | Booking | Booking → REFUNDED. |
| `trip.trip.completed` / `trip.trip.disrupted` | Payment | IF SUM(operator_ledger_entries.amount for trip) > 0 → INSERT OperatorTripSettlement {status: PENDING_HOLD, eligible_at = terminal + 7 days}. Weekly settle debits PlatformWallet and credits OperatorWallet. |
| `payment.trip_settlement.completed` | Notification | Push OPERATOR_ADMIN "Đã tất toán [amount] VND từ chuyến [tripId] vào ví." |

## Bootstrap event for Wallet / OperatorWallet creation

- **Wallet:** Identity Service publish `identity.user.created` event → Payment & Wallet Service consume → INSERT `wallets { user_id, balance=0 } ON CONFLICT (user_id) DO NOTHING`. `user_id` là natural PK của `wallets` (giống `operator_wallets.operator_id` PK). **OR** lazy create on first wallet transaction.
- **OperatorWallet:** Identity Service publish `identity.operator.approved` event → Payment Service consume → INSERT `operator_wallets { operator_id, balance=0 }`.

Cả 2 dùng pattern UPSERT (ON CONFLICT) cho idempotent.
