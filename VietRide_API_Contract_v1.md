# VietRide API Contract v1

> Source of truth cho controller/DTO scaffolding. Business rules, status machines, entity rationale nằm trong `SU26SE101_VIETRIDE_technical_context_v6.md`.

## Global Conventions

- Public API prefix: `/v1`.
- Internal service-to-service API prefix: `/internal/v1`, require valid Internal JWT, never exposed publicly through Gateway.
- Auth header for public protected endpoints: `Authorization: Bearer <userAccessToken>`.
- Idempotent write endpoints require `Idempotency-Key: <uuid>` where noted.
- Error response: `application/problem+json` with `{ type, title, status, detail, instance, errorCode, errors? }`.
- Money fields are VND `number` in JSON, stored as BIGINT in DB.
- Datetime fields are ISO 8601 strings with offset.
- IDs are UUID strings unless explicitly named code fields.

## Identity & User Service

### POST `/v1/auth/register`

Auth: public. Idempotency: optional by email.

Request:
```json
{
  "email": "user@example.com",
  "password": "pass1234",
  "displayName": "Nguyen Van A",
  "phone": "0900000000"
}
```

Response `201`:
```json
{
  "userId": "uuid",
  "email": "user@example.com",
  "status": "PENDING_EMAIL_VERIFICATION",
  "otpTtlMinutes": 5
}
```

### POST `/v1/auth/verify-email`

Auth: public.

Request:
```json
{
  "email": "user@example.com",
  "code": "123456",
  "purpose": "REGISTRATION"
}
```

Response `200`:
```json
{
  "userId": "uuid",
  "status": "ACTIVE"
}
```

### POST `/v1/auth/login`

Auth: public.

Request:
```json
{
  "email": "user@example.com",
  "password": "pass1234"
}
```

Response `200`:
```json
{
  "accessToken": "jwt",
  "refreshToken": "opaque",
  "expiresInSeconds": 900,
  "user": {
    "id": "uuid",
    "email": "user@example.com",
    "displayName": "Nguyen Van A",
    "role": "PASSENGER",
    "operatorId": null,
    "status": "ACTIVE"
  }
}
```

### POST `/v1/auth/refresh`

Auth: public.

Request:
```json
{
  "refreshToken": "opaque"
}
```

Response `200`: same token shape as login.

### POST `/v1/auth/logout`

Auth: required.

Request:
```json
{
  "refreshToken": "opaque"
}
```

Response `204`.

### GET `/v1/.well-known/jwks.json`

Auth: public.

Response `200`:
```json
{
  "keys": [
    {
      "kty": "RSA",
      "alg": "RS256",
      "use": "sig",
      "kid": "key-id",
      "n": "modulus",
      "e": "AQAB"
    }
  ]
}
```

## Booking Service

### POST `/v1/bookings`

Auth: `PASSENGER`. Idempotency: required.

Request:
```json
{
  "tripId": "uuid",
  "pickup": { "stationId": "uuid" },
  "dropoff": { "stationId": "uuid" },
  "seats": [
    {
      "seatNumber": "A01",
      "passenger": {
        "fullName": "Nguyen Van A",
        "phoneNumber": "0900000000",
        "idNumber": "012345678901"
      }
    }
  ],
  "voucherCode": "SUMMER26",
  "paymentMethod": "WALLET"
}
```

Response `201`:
```json
{
  "bookingId": "uuid",
  "bookingCode": "VR-20260518-ABCD1234",
  "status": "CONFIRMED",
  "totalAmount": 350000,
  "discountAmount": 50000,
  "paymentRedirectUrl": null
}
```

### POST `/v1/bookings/round-trip`

Auth: `PASSENGER`. Idempotency: required.

Request:
```json
{
  "outbound": {
    "tripId": "uuid",
    "pickup": { "stationId": "uuid" },
    "dropoff": { "stationId": "uuid" },
    "seats": [{ "seatNumber": "A01", "passenger": { "fullName": "Nguyen Van A", "phoneNumber": "0900000000" } }]
  },
  "return": {
    "tripId": "uuid",
    "pickup": { "stationId": "uuid" },
    "dropoff": { "stationId": "uuid" },
    "seats": [{ "seatNumber": "A01", "passenger": { "fullName": "Nguyen Van A", "phoneNumber": "0900000000" } }]
  },
  "voucherCode": "SUMMER26",
  "paymentMethod": "VNPAY"
}
```

Response `201`:
```json
{
  "bookingGroupId": "uuid",
  "outbound": { "bookingId": "uuid", "bookingCode": "VR-20260518-ABCD1234", "totalAmount": 350000, "discountAmount": 50000 },
  "return": { "bookingId": "uuid", "bookingCode": "VR-20260519-EFGH5678", "totalAmount": 350000, "discountAmount": 50000 },
  "grandTotal": 700000,
  "paymentRedirectUrl": "https://vnpay.vn/..."
}
```

### GET `/v1/bookings/history`

Auth: `PASSENGER`.

Query: `status?`, `from?`, `to?`, `page?`, `pageSize?`.

Response `200`:
```json
{
  "items": [
    {
      "bookingId": "uuid",
      "bookingCode": "VR-20260518-ABCD1234",
      "tripId": "uuid",
      "status": "CONFIRMED",
      "departureDateTime": "2026-05-18T08:00:00+07:00",
      "originStationName": "Bến xe Miền Đông",
      "destinationStationName": "Bến xe Mỹ Đình",
      "totalAmount": 350000
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1
}
```

### GET `/v1/bookings/{bookingId}`

Auth: booking owner or authorized operator.

Response `200`: booking detail with passengers, pickup/dropoff, payment summary, pendingActions.

### POST `/v1/bookings/{bookingId}/cancel`

Auth: booking owner. Idempotency: required.

Request:
```json
{
  "reason": "USER_INITIATED"
}
```

Response `200`:
```json
{
  "bookingId": "uuid",
  "status": "CANCELLED",
  "refundAmount": 175000,
  "refundMethod": "WALLET"
}
```

### POST `/v1/bookings/{bookingId}/edit-pickup`

Auth: booking owner. Idempotency: required. Cutoff: before `departureDateTime - 2h`.

Request:
```json
{
  "pickup": { "stationId": "uuid", "stopId": null },
  "paymentMethod": "WALLET"
}
```

Response `200`:
```json
{
  "bookingId": "uuid",
  "pickup": { "stationId": "uuid", "stopId": null },
  "fareDelta": 50000,
  "refundAmount": 0,
  "paymentRedirectUrl": null
}
```

Rules: reprice using `Trip.baseFare` for terminal pickup or `TripStopFare` for along-route pickup. If fare increases, apply the change only after the delta charge succeeds. If fare decreases, refund the delta to Wallet.

### POST `/v1/bookings/{bookingId}/edit-dropoff`

Auth: booking owner. Idempotency: required. Cutoff: before `departureDateTime - 2h`.

Request:
```json
{
  "dropoff": { "stationId": null, "stopId": "uuid" }
}
```

Response `200`:
```json
{
  "bookingId": "uuid",
  "dropoff": { "stationId": null, "stopId": "uuid" },
  "fareDelta": 0
}
```

Rules: dropoff edit is in scope, but v1 fare stays full-price by pickup point, so this endpoint does not create payment/refund side effects.

### POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`

Auth: booking owner for passenger decisions, operator for seat assignment resolution.

Request:
```json
{
  "action": "ACCEPTED",
  "selectedStopId": "uuid",
  "note": "optional"
}
```

Response `200`:
```json
{
  "bookingId": "uuid",
  "actionId": "uuid",
  "resolvedAction": "ACCEPTED",
  "resolvedAt": "2026-05-18T09:00:00+07:00"
}
```

### GET `/v1/operator/booking-stats`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`.

Query: `from`, `to`, `groupBy=date`.

Response `200`:
```json
{
  "items": [
    {
      "operatorId": "uuid",
      "date": "2026-05-18",
      "totalBookings": 120,
      "totalRevenue": 42000000,
      "totalCancellations": 4,
      "totalNoShows": 2,
      "totalPartialNoShows": 1,
      "totalCompleted": 113
    }
  ]
}
```

### GET `/v1/admin/booking-stats/aggregate`

Auth: `SYSTEM_ADMIN`.

Query: `from`, `to`, `groupBy=operator|date`.

Response `200`:
```json
{
  "items": [
    {
      "operatorId": "uuid",
      "operatorName": "VietRide Express",
      "totalBookings": 120,
      "totalRevenue": 42000000,
      "totalCancellations": 4,
      "totalNoShows": 2,
      "totalPartialNoShows": 1,
      "totalCompleted": 113
    }
  ]
}
```

## Trip, Route & Vehicle Service

### GET `/v1/trips/search`

Auth: optional/passenger.

Query: `originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`.

Response `200`:
```json
{
  "items": [
    {
      "tripId": "uuid",
      "operatorId": "uuid",
      "operatorName": "VietRide Express",
      "routeId": "uuid",
      "departureDateTime": "2026-05-18T08:00:00+07:00",
      "estimatedArrivalTime": "2026-05-18T20:00:00+07:00",
      "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
      "destinationStation": { "id": "uuid", "name": "Bến xe Mỹ Đình" },
      "availableSeats": 18,
      "baseFare": 400000,
      "allowAlongRoutePickup": true,
      "allowAlongRouteDropoff": true
    }
  ]
}
```

### GET `/v1/trips/{tripId}`

Auth: protected.

Response `200`: trip detail with route, stations, stops, seat summary, fare summary.

### GET `/v1/trips/{tripId}/seat-map`

Auth: protected.

Response `200`:
```json
{
  "tripId": "uuid",
  "vehicleType": "SLEEPER_BUS",
  "seats": [
    { "seatNumber": "A01", "status": "AVAILABLE", "type": "SLEEPER_LOWER", "row": 1, "col": 1, "deck": 1 }
  ]
}
```

### POST `/internal/v1/trips/{tripId}/lock-seats`

Auth: Internal JWT. Idempotency: required.

Request:
```json
{
  "seatNumbers": ["A01"],
  "holdOwnerId": "uuid",
  "ttlSeconds": 600
}
```

Response `200`:
```json
{
  "seatLockToken": "uuid",
  "expiresAt": "2026-05-18T08:10:00+07:00"
}
```

### POST `/internal/v1/trips/{tripId}/release-seats`

Auth: Internal JWT.

Request:
```json
{
  "seatLockToken": "uuid",
  "seatNumbers": ["A01"]
}
```

Response `204`.

### POST `/internal/v1/trips/{tripId}/book-seats`

Auth: Internal JWT.

Request:
```json
{
  "seatLockToken": "uuid",
  "bookingId": "uuid",
  "passengerSeatAssignments": [{ "passengerId": "uuid", "seatNumber": "A01" }]
}
```

Response `204`.

### POST `/v1/operator/trips/{tripId}/cancel`

Auth: operator staff/admin for trip's operator. Idempotency: required.

Request:
```json
{
  "reason": "Vehicle issue",
  "note": "Bus cannot depart safely"
}
```

Response `200`:
```json
{
  "tripId": "uuid",
  "status": "CANCELLED",
  "affectedBookings": 42,
  "affectedParcels": 3
}
```

Rules: if Trip is `SCHEDULED` or `BOARDING`, transition to `CANCELLED` and trigger full refund/cancel flows. If Trip is already `IN_PROGRESS`, transition to `DISRUPTED` and use proportional refund / parcel operator-action flows.

### POST `/v1/operator/trips/{tripId}/substitute-vehicle`

Auth: operator staff/admin for trip's operator. Idempotency: required.

Request:
```json
{
  "replacementVehicleId": "uuid",
  "driverId": "uuid",
  "assistantId": "uuid",
  "reason": "Vehicle breakdown"
}
```

Response `200`:
```json
{
  "oldTripId": "uuid",
  "oldTripStatus": "DISRUPTED",
  "newTripId": "uuid",
  "transferStatus": "PENDING_PASSENGER_CONFIRMATION"
}
```

## Parcel Service

### GET `/v1/parcels/available-trips`

Auth: `PASSENGER`.

Query: `originStationId`, `destinationStationId`, `departureDate`, `estimatedWeightKg`, `sizeCategory`.

Response `200`:
```json
{
  "items": [
    {
      "tripId": "uuid",
      "routeId": "uuid",
      "operatorName": "VietRide Express",
      "departureDateTime": "2026-05-18T08:00:00+07:00",
      "availableCargoWeightKg": 120,
      "priceVnd": 150000
    }
  ]
}
```

### POST `/v1/parcels`

Auth: `PASSENGER`. Idempotency: required.

Request:
```json
{
  "tripId": "uuid",
  "bookingId": "uuid",
  "itemName": "Thùng quà",
  "description": "Hàng dễ vỡ",
  "sizeCategory": "MEDIUM",
  "estimatedWeightKg": 12.5,
  "photoUrl": "https://storage.googleapis.com/...",
  "recipient": {
    "fullName": "Tran Thi B",
    "phoneNumber": "0911111111",
    "email": "recipient@example.com"
  },
  "deliveryMethod": "TERMINAL_PICKUP",
  "paymentMethod": "VNPAY"
}
```

Response `201`:
```json
{
  "parcelId": "uuid",
  "parcelCode": "VRP-20260518-P7K3D9Q2",
  "status": "PENDING_PAYMENT",
  "totalAmount": 150000,
  "paymentRedirectUrl": "https://vnpay.vn/..."
}
```

### GET `/v1/parcels/received`

Auth: `PASSENGER`.

Response `200`:
```json
{
  "items": [
    {
      "parcelId": "uuid",
      "parcelCode": "VRP-20260518-P7K3D9Q2",
      "tripId": "uuid",
      "status": "IN_TRANSIT",
      "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
      "destinationStation": { "id": "uuid", "name": "Bến xe Mỹ Đình" },
      "eta": "2026-05-18T20:00:00+07:00"
    }
  ]
}
```

### GET `/v1/parcels/{parcelId}`

Auth: sender, recipient account, or authorized operator.

Response `200`: parcel detail with sender, recipient, trip, payment, transfer, and delivery token state excluding raw token.

### POST `/v1/parcels/delivery/confirm`

Auth: public token link. Idempotency: required.

Request:
```json
{
  "token": "delivery-token"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "status": "DELIVERY_CONFIRMED",
  "confirmedAt": "2026-05-18T20:15:00+07:00"
}
```

### POST `/v1/parcels/delivery/reject`

Auth: public token link. Idempotency: required.

Request:
```json
{
  "token": "delivery-token",
  "rejectionReason": "Package damaged"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "status": "DELIVERY_REJECTED",
  "rejectedAt": "2026-05-18T20:15:00+07:00"
}
```

### POST `/internal/v1/parcels/{parcelId}/mark-loaded`

Auth: Internal JWT or Driver/Assistant through Driver App facade.

Request:
```json
{
  "tripId": "uuid",
  "parcelCode": "VRP-20260518-P7K3D9Q2",
  "confirmedByUserId": "uuid"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "status": "LOADED"
}
```

### POST `/internal/v1/parcels/{parcelId}/confirm-transfer`

Auth: Internal JWT or Driver/Assistant of target trip.

Request:
```json
{
  "targetTripId": "uuid",
  "parcelCode": "VRP-20260518-P7K3D9Q2",
  "confirmedByUserId": "uuid"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "tripId": "uuid",
  "status": "LOADED",
  "transferConfirmedAt": "2026-05-18T10:00:00+07:00"
}
```

### POST `/v1/operator/parcels/{parcelId}/request-transfer`

Auth: operator staff/admin for parcel's operator.

Request:
```json
{
  "targetTripId": "uuid",
  "reason": "Trip disrupted, move parcel to next available trip"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "status": "PENDING_TRANSFER_CONFIRM",
  "transferTargetTripId": "uuid"
}
```

### POST `/v1/operator/parcels/{parcelId}/return`

Auth: operator staff/admin for parcel's operator.

Request:
```json
{
  "returnReason": "Sender requested return after trip disruption"
}
```

Response `200`:
```json
{
  "parcelId": "uuid",
  "status": "RETURNED",
  "returnReason": "Sender requested return after trip disruption",
  "returnedAt": "2026-05-18T11:00:00+07:00"
}
```

## Payment & Wallet Service

### POST `/v1/wallet/top-up`

Auth: required. Idempotency: required.

Request:
```json
{
  "amount": 500000,
  "method": "VNPAY"
}
```

Response `201`:
```json
{
  "topUpRequestId": "uuid",
  "status": "PENDING",
  "paymentRedirectUrl": "https://vnpay.vn/..."
}
```

### GET `/v1/wallet`

Auth: required.

Response `200`:
```json
{
  "userId": "uuid",
  "balance": 1000000,
  "currency": "VND"
}
```

> Note: `wallets.user_id` là PK natural (1-1 với User), không có cột `id` riêng — xem v7 Section 6.5.

### GET `/v1/wallet/transactions`

Auth: required.

Query: `from?`, `to?`, `type?`, `page?`, `pageSize?`.

Response `200`: paged wallet transactions.

### POST `/internal/v1/payments/charge`

Auth: Internal JWT. Idempotency: required.

Request:
```json
{
  "referenceType": "BOOKING",
  "referenceId": "uuid",
  "userId": "uuid",
  "amount": 350000,
  "method": "WALLET"
}
```

Response `200`:
```json
{
  "paymentId": "uuid",
  "status": "SUCCEEDED",
  "paymentRedirectUrl": null
}
```

### POST `/internal/v1/wallet/refund`

Auth: Internal JWT. Idempotency: required.

Request:
```json
{
  "userId": "uuid",
  "amount": 175000,
  "referenceType": "BOOKING_REFUND",
  "referenceId": "uuid"
}
```

Response `200`:
```json
{
  "walletTransactionId": "uuid",
  "balanceAfter": 1175000
}
```

## Notification Service

### GET `/v1/notifications`

Auth: required.

Query: `unreadOnly?`, `page?`, `pageSize?`.

Response `200`: paged notifications.

### POST `/v1/notifications/{notificationId}/read`

Auth: owner.

Response `204`.

## Tracking Service Socket.IO

Connection:
```ts
io("wss://api.vietride.app", {
  path: "/tracking/socket.io",
  auth: { token: "<userAccessToken>" }
})
```

### `joinTripTracking`

Client emit:
```ts
socket.emit("joinTripTracking", { tripId }, ack)
```

Success ack:
```json
{
  "success": true,
  "tripId": "uuid",
  "room": "trip:uuid",
  "scope": "PARCEL_RECIPIENT"
}
```

Error ack:
```json
{
  "success": false,
  "error": "UNAUTHORIZED"
}
```

Server broadcasts to room `trip:{tripId}`:
- `gps:update`
- `eta:update`
- `trip:statusChanged`

## RAG AI Service

### POST `/v1/rag/documents`

Auth: `SYSTEM_ADMIN`.

Request: multipart file + `{ title, description?, accessLevel }`.

Response `201`:
```json
{
  "documentId": "uuid",
  "status": "PENDING_REVIEW"
}
```

### PUT `/v1/rag/documents/{documentId}/approve`

Auth: `SYSTEM_ADMIN`.

Response `200`:
```json
{
  "documentId": "uuid",
  "status": "APPROVED"
}
```

### POST `/v1/rag/chat`

Auth: any authenticated role. Retrieval access is filtered by role:
`PASSENGER` can query `PUBLIC`; `DRIVER`/`ASSISTANT`/operator roles can query
`PUBLIC` + `OPERATOR`; `SYSTEM_ADMIN` can query all access levels.

Request:
```json
{
  "conversationId": "uuid",
  "message": "Quy trình xử lý hàng bị từ chối là gì?"
}
```

Response: Server-Sent Events stream with assistant tokens and final cited chunk IDs.

## Operator/Admin Management

### GET `/v1/admin/operators`

Auth: `SYSTEM_ADMIN`.

Response `200`: paged operators.

### POST `/v1/admin/operators/{operatorId}/approve`

Auth: `SYSTEM_ADMIN`.

Response `200`:
```json
{
  "operatorId": "uuid",
  "registrationStatus": "APPROVED"
}
```

### POST `/v1/admin/operators/{operatorId}/suspend`

Auth: `SYSTEM_ADMIN`.

Request:
```json
{
  "reason": "Policy violation"
}
```

Response `200`:
```json
{
  "operatorId": "uuid",
  "registrationStatus": "SUSPENDED"
}
```

### GET `/v1/stations`

Auth: protected.

Query: `q?`, `city?`, `province?`.

Response `200`: list canonical Stations.

### POST `/v1/operator/stations`

Auth: `OPERATOR_ADMIN`.

Request:
```json
{
  "stationId": "uuid",
  "displayNameOverride": "Quầy VietRide - Bến xe Miền Đông",
  "counterLocation": "Quầy 12",
  "contactPhone": "0900000000",
  "instructions": "Có mặt trước giờ chạy 30 phút"
}
```

Response `201`:
```json
{
  "operatorId": "uuid",
  "stationId": "uuid",
  "isActive": true
}
```
