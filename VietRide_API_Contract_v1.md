# VietRide API Contract v1

> Source of truth cho controller/DTO scaffolding. Business rules, status machines, entity rationale nằm trong `SU26SE101_VIETRIDE_technical_context_v7.md`.

## Global Conventions

- Public API prefix: `/v1`.
- Internal service-to-service API prefix: `/internal/v1`, require valid Internal JWT, never exposed publicly through Gateway.
- Auth header for public protected endpoints: `Authorization: Bearer <userAccessToken>`.
- Idempotent write endpoints require `Idempotency-Key: <uuid>` where noted.
- Error response: `ApiResponse` envelope `{ success: false, statusCode, error: { code, message, fields? }, meta: { traceId, timestamp } }` — ADR 0004; `error.code` từ BSOT §5.9 registry (UPPER_SNAKE_CASE). `application/problem+json` (RFC 7807) đã DROP.
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
  "success": true,
  "statusCode": 201,
  "data": {
    "userId": "uuid",
    "email": "user@example.com",
    "status": "PENDING_EMAIL_VERIFICATION",
    "otpTtlMinutes": 5
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `409` — duplicate email:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_REGISTERED", "message": "Email đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "status": "ACTIVE"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` — wrong OTP code:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Mã xác thực không đúng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` — expired OTP:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_EXPIRED", "message": "Mã xác thực đã hết hạn." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
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
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `401` — invalid credentials:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_INVALID_CREDENTIALS", "message": "Email hoặc mật khẩu không đúng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — unverified email:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "AUTH_EMAIL_NOT_VERIFIED", "message": "Email chưa được xác minh." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — account locked:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "AUTH_ACCOUNT_LOCKED", "message": "Tài khoản đã bị khóa." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — OPERATOR_ADMIN/OPERATOR_STAFF belongs to an operator that is not currently `APPROVED`:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Nhà xe chưa được phép truy cập hệ thống." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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

Response `200`: same envelope shape as login (`data` = same token bundle).

Error `401` — invalid or reused refresh token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Refresh token không hợp lệ hoặc đã bị thu hồi." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

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

### POST `/v1/auth/google`

Auth: public. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "idToken": "google-id-token"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `401` — invalid Google ID token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_GOOGLE_TOKEN_INVALID", "message": "Google ID token signature/expiry/audience invalid." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/users/me/complete-profile`

Auth: User Access Token (RS256). Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "phone": "+84901234567"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "phone": "+84901234567",
    "message": "Hồ sơ hoàn tất."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` — invalid phone format:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_PHONE_INVALID_FORMAT", "message": "Số điện thoại không đúng định dạng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `409` — duplicate phone:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_PHONE_ALREADY_REGISTERED", "message": "Số điện thoại đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `422` — phone already set:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Phone already exists and cannot be overwritten from this endpoint." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/users/me`

Auth: User Access Token (RS256). Idempotency-Key: not required (read endpoint).

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "id": "uuid",
    "email": "user@example.com",
    "displayName": "Nguyen Van A",
    "phone": "+84901234567",
    "role": "PASSENGER",
    "operatorId": null,
    "status": "ACTIVE",
    "avatarUrl": "https://example.com/avatar.png"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/auth/set-initial-password`

Auth: public. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "token": "uuid-v4-token",
  "password": "ChangeMe123!"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "status": "ACTIVE"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` — invalid initial-password token:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_INITIAL_PASSWORD_TOKEN_INVALID", "message": "SET_INITIAL_PASSWORD token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` — expired initial-password token:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED", "message": "SET_INITIAL_PASSWORD token đã hết hạn." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `422` — user is not pending initial password:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "USER_INVALID_STATUS_TRANSITION", "message": "User status không cho phép đặt mật khẩu lần đầu." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/auth/device-token`

Auth: User Access Token (RS256). Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "fcmToken": "fcm-token",
  "platform": "ANDROID"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userDeviceId": "uuid",
    "fcmToken": "fcm-token",
    "platform": "ANDROID",
    "isActive": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `422` — invalid device token payload:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Dữ liệu device token không hợp lệ.", "fields": [{ "field": "platform", "message": "platform must be IOS, ANDROID, or WEB." }] },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### DELETE `/v1/auth/device-token`

Auth: User Access Token (RS256). Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "fcmToken": "fcm-token"
}
```

Response `204`: No Content, empty body (no `ApiResponse` envelope per ADR 0004 Rule 2).

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `422` — invalid device token payload:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Dữ liệu device token không hợp lệ.", "fields": [{ "field": "fcmToken", "message": "fcmToken is required." }] },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/operator/users/{userId}/resend-initial-password`

Auth: `OPERATOR_ADMIN`. Tenant isolation: caller `operatorId` must match the target user's `operatorId`. Caller Operator must currently be `APPROVED`; tokens issued before a later suspend/reject return `403 FORBIDDEN` without token/email/ActivityLog side effects. Idempotency-Key: not required by BSOT §5.6.

Request: empty JSON object `{}`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "status": "PENDING_INITIAL_PASSWORD",
    "expiresAt": "2026-06-08T10:00:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — caller is not an operator admin, cross-operator target, or caller Operator is not currently `APPROVED`:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Bạn không có quyền thực hiện thao tác này." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `404` — target user not found:
```json
{
  "success": false,
  "statusCode": 404,
  "error": { "code": "RESOURCE_NOT_FOUND", "message": "User không tồn tại." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `422` — target user is not pending initial password:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "USER_INVALID_STATUS_TRANSITION", "message": "Chỉ user ở trạng thái PENDING_INITIAL_PASSWORD mới được gửi lại link đặt mật khẩu." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/admin/users`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "email": "admin2@example.com",
  "displayName": "Admin Two",
  "role": "SYSTEM_ADMIN"
}
```

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "userId": "uuid",
    "email": "admin2@example.com",
    "displayName": "Admin Two",
    "role": "SYSTEM_ADMIN",
    "status": "PENDING_INITIAL_PASSWORD"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — caller is not a system admin:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Bạn không có quyền thực hiện thao tác này." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `409` — duplicate email:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_REGISTERED", "message": "Email đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 201,
  "data": {
    "bookingId": "uuid",
    "bookingCode": "VR-20260518-ABCD1234",
    "status": "CONFIRMED",
    "totalAmount": 350000,
    "discountAmount": 50000,
    "paymentRedirectUrl": null
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 201,
  "data": {
    "bookingGroupId": "uuid",
    "outbound": { "bookingId": "uuid", "bookingCode": "VR-20260518-ABCD1234", "totalAmount": 350000, "discountAmount": 50000 },
    "return": { "bookingId": "uuid", "bookingCode": "VR-20260519-EFGH5678", "totalAmount": 350000, "discountAmount": 50000 },
    "grandTotal": 700000,
    "paymentRedirectUrl": "https://vnpay.vn/..."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/bookings/history`

Auth: `PASSENGER`.

Query: `status?`, `from?`, `to?`, `page?`, `pageSize?`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "status": "CANCELLED",
    "refundAmount": 175000,
    "refundMethod": "WALLET"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "pickup": { "stationId": "uuid", "stopId": null },
    "fareDelta": 50000,
    "refundAmount": 0,
    "paymentRedirectUrl": null
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "dropoff": { "stationId": null, "stopId": "uuid" },
    "fareDelta": 0
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "actionId": "uuid",
    "resolvedAction": "ACCEPTED",
    "resolvedAt": "2026-05-18T09:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/operator/booking-stats`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`.

Query: `from`, `to`, `groupBy=date`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/admin/booking-stats/aggregate`

Auth: `SYSTEM_ADMIN`.

Query: `from`, `to`, `groupBy=operator|date`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

## Trip, Route & Vehicle Service

### GET `/v1/trips/search`

Auth: optional/passenger.

Query: `originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "vehicleType": "SLEEPER_BUS",
    "seats": [
      { "seatNumber": "A01", "status": "AVAILABLE", "type": "SLEEPER_LOWER", "row": 1, "col": 1, "deck": 1 }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "seatLockToken": "uuid",
    "expiresAt": "2026-05-18T08:10:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "status": "CANCELLED",
    "affectedBookings": 42,
    "affectedParcels": 3
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "oldTripId": "uuid",
    "oldTripStatus": "DISRUPTED",
    "newTripId": "uuid",
    "transferStatus": "PENDING_PASSENGER_CONFIRMATION"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

## Parcel Service

### GET `/v1/parcels/available-trips`

Auth: `PASSENGER`.

Query: `originStationId`, `destinationStationId`, `departureDate`, `estimatedWeightKg`, `sizeCategory`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "tripId": "uuid",
        "routeId": "uuid",
        "operatorName": "VietRide Express",
        "departureDateTime": "2026-05-18T08:00:00+07:00",
        "availableCargoWeightKg": 120,
        "priceVnd": 150000
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 201,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VRP-20260518-P7K3D9Q2",
    "status": "PENDING_PAYMENT",
    "totalAmount": 150000,
    "paymentRedirectUrl": "https://vnpay.vn/..."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/parcels/received`

Auth: `PASSENGER`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
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
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "DELIVERY_CONFIRMED",
    "confirmedAt": "2026-05-18T20:15:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "DELIVERY_REJECTED",
    "rejectedAt": "2026-05-18T20:15:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "LOADED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "tripId": "uuid",
    "status": "LOADED",
    "transferConfirmedAt": "2026-05-18T10:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "PENDING_TRANSFER_CONFIRM",
    "transferTargetTripId": "uuid"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "RETURNED",
    "returnReason": "Sender requested return after trip disruption",
    "returnedAt": "2026-05-18T11:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 201,
  "data": {
    "topUpRequestId": "uuid",
    "status": "PENDING",
    "paymentRedirectUrl": "https://vnpay.vn/..."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### GET `/v1/wallet`

Auth: required.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "balance": 1000000,
    "currency": "VND"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "paymentId": "uuid",
    "status": "SUCCEEDED",
    "paymentRedirectUrl": null
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 200,
  "data": {
    "walletTransactionId": "uuid",
    "balanceAfter": 1175000
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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
  "success": true,
  "statusCode": 201,
  "data": {
    "documentId": "uuid",
    "status": "PENDING_REVIEW"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### PUT `/v1/rag/documents/{documentId}/approve`

Auth: `SYSTEM_ADMIN`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "documentId": "uuid",
    "status": "APPROVED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
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

### POST `/v1/operators/register`

Auth: public. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "name": "VietRide Limousine",
  "contactEmail": "ops@example.com",
  "contactPhone": "+84901234567",
  "businessRegistrationNumber": "0312345678",
  "taxCode": "0312345678",
  "addressStreet": "123 Le Loi",
  "addressWard": "Ben Nghe",
  "addressDistrict": "District 1",
  "addressProvince": "Ho Chi Minh City",
  "representativeName": "Nguyen Van Operator",
  "representativePhone": "+84907654321",
  "password": "pass1234"
}
```

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "operatorId": "uuid",
    "message": "Đơn đăng ký đã nhận, vui lòng xác thực email"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `409 OPERATOR_DUPLICATE_REGISTRATION` — `businessRegistrationNumber` already exists among non-deleted operators.
- `409 OPERATOR_DUPLICATE_TAX_CODE` — `taxCode` already exists among non-deleted operators.
- `409 AUTH_EMAIL_ALREADY_REGISTERED` — OPERATOR_ADMIN email already exists.
- `409 AUTH_PHONE_ALREADY_REGISTERED` — OPERATOR_ADMIN phone already exists.
- `422 VALIDATION_ERROR` — invalid payload/password/phone format.

Notes: creates `Operator.registrationStatus=PENDING`, OPERATOR_ADMIN user `PENDING_EMAIL_VERIFICATION`, and Starter Free-Trial `OperatorSubscription.status=PENDING_APPROVAL` in one transaction. The OPERATOR_ADMIN cannot login until email is verified and the operator is approved.

### GET `/v1/admin/operators`

Auth: `SYSTEM_ADMIN`.

Query: `page?`, `pageSize?`, `search?`, `sortBy?`, `sortDir?`, `status?` (`PENDING|APPROVED|REJECTED|SUSPENDED`).

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "operatorId": "uuid",
        "name": "VietRide Limousine",
        "contactEmail": "ops@example.com",
        "contactPhone": "+84901234567",
        "businessRegistrationNumber": "0312345678",
        "taxCode": "0312345678",
        "registrationStatus": "PENDING",
        "isActive": true,
        "createdAt": "2026-06-01T10:00:00Z",
        "approvedAt": null,
        "suspendedAt": null
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/admin/operators`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "name": "VietRide Limousine",
  "contactEmail": "ops@example.com",
  "contactPhone": "+84901234567",
  "businessRegistrationNumber": "0312345678",
  "taxCode": "0312345678",
  "addressStreet": "123 Le Loi",
  "addressWard": "Ben Nghe",
  "addressDistrict": "District 1",
  "addressProvince": "Ho Chi Minh City",
  "representativeName": "Nguyen Van Operator",
  "representativePhone": "+84907654321"
}
```

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "operator": {
      "operatorId": "uuid",
      "name": "VietRide Limousine",
      "registrationStatus": "APPROVED",
      "contactEmail": "ops@example.com",
      "contactPhone": "+84901234567",
      "businessRegistrationNumber": "0312345678",
      "taxCode": "0312345678"
    },
    "adminUser": {
      "userId": "uuid",
      "email": "ops@example.com",
      "phone": "+84907654321",
      "displayName": "Nguyen Van Operator",
      "role": "OPERATOR_ADMIN",
      "status": "PENDING_INITIAL_PASSWORD"
    },
    "subscription": {
      "subscriptionId": "uuid",
      "planId": "00000000-0000-0000-0000-000000000001",
      "planName": "Starter (Free Trial)",
      "status": "ACTIVE",
      "startedAt": "2026-06-01T10:00:00Z",
      "expiresAt": "2026-07-01T10:00:00Z",
      "currentOperatorUsers": 1
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `409 OPERATOR_DUPLICATE_REGISTRATION` — `businessRegistrationNumber` already exists among non-deleted operators.
- `409 OPERATOR_DUPLICATE_TAX_CODE` — `taxCode` already exists among non-deleted operators.
- `409 AUTH_EMAIL_ALREADY_REGISTERED` — OPERATOR_ADMIN email already exists.
- `409 AUTH_PHONE_ALREADY_REGISTERED` — OPERATOR_ADMIN phone already exists.
- `422 VALIDATION_ERROR` — invalid payload, including any paid-plan/`planId` field. Day 6 supports only the default Starter Free-Trial path and never creates `PENDING_PAYMENT` here.

Notes: creates an approved Operator, a passwordless OPERATOR_ADMIN `PENDING_INITIAL_PASSWORD`, a 48h `SET_INITIAL_PASSWORD` email link, and an ACTIVE Starter Free-Trial subscription in one transaction.

### POST `/v1/admin/operators/{operatorId}/approve`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request: empty JSON object `{}`.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "uuid",
    "registrationStatus": "APPROVED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — invalid lifecycle transition, for example approving a non-`PENDING` operator. Day 6 does not implement SUSPENDED -> APPROVED reactivation.

Notes: atomically sets `Operator.registrationStatus=APPROVED` and activates the PENDING_APPROVAL Starter Free-Trial subscription for 30 days. Outbox emission is deferred to Day 10.

### POST `/v1/admin/operators/{operatorId}/reject`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "reason": "Business registration documents are invalid."
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "uuid",
    "registrationStatus": "REJECTED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — missing reason or invalid lifecycle transition.

Notes: atomically sets `Operator.registrationStatus=REJECTED`, stores reject metadata, and sets the PENDING_APPROVAL subscription to `CANCELLED`. `operator_subscriptions` is not soft-deletable in the canonical DDL, so no `deletedAt` is set.

### POST `/v1/admin/operators/{operatorId}/suspend`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "reason": "Policy violation"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "uuid",
    "registrationStatus": "SUSPENDED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — missing reason or invalid lifecycle transition.

Notes: suspend writes no ActivityLog in Day 6 because canonical `activity_log_action` has no `SUSPEND_OPERATOR`. Outbox emission is deferred to Day 10.

### POST `/v1/operator/users`

Auth: `OPERATOR_ADMIN`. Tenant isolation: caller `operatorId` is the created user's `operatorId`. Caller Operator must currently be `APPROVED`; tokens issued before a later suspend/reject return `403 FORBIDDEN` without side effects. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "email": "driver@example.com",
  "phone": "+84901112222",
  "displayName": "Driver One",
  "role": "DRIVER"
}
```

Allowed `role`: `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`.

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "userId": "uuid",
    "email": "driver@example.com",
    "phone": "+84901112222",
    "displayName": "Driver One",
    "role": "DRIVER",
    "status": "PENDING_INITIAL_PASSWORD",
    "operatorId": "uuid",
    "initialPasswordExpiresAt": "2026-06-03T10:00:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `403 FORBIDDEN` — caller is not `OPERATOR_ADMIN`, has no `operatorId`, or caller Operator is not currently `APPROVED`.
- `409 AUTH_EMAIL_ALREADY_REGISTERED` — target email already exists.
- `409 AUTH_PHONE_ALREADY_REGISTERED` — target phone already exists.
- `422 SUBSCRIPTION_LIMIT_EXCEEDED` — creating the target role would exceed the current subscription limit.
- `422 VALIDATION_ERROR` — invalid payload or role outside the allowed set.

### GET `/v1/operator/profile`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`. Tenant isolation: operator is resolved from caller `operatorId`. Read is allowed even when the current Operator is non-`APPROVED` so the UI can display current status/policies.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "uuid",
    "name": "VietRide Limousine",
    "businessRegistrationNumber": "0312345678",
    "taxCode": "0312345678",
    "contactEmail": "ops@example.com",
    "contactPhone": "+84901234567",
    "logoUrl": null,
    "address": {
      "street": "123 Le Loi",
      "ward": "Ben Nghe",
      "district": "District 1",
      "province": "Ho Chi Minh City"
    },
    "representativeName": "Nguyen Van Operator",
    "representativePhone": "+84907654321",
    "registrationStatus": "APPROVED",
    "isActive": true,
    "cancellationPolicy": [
      { "hoursBeforeDeparture": 24, "feePercent": 10 }
    ],
    "parcelNoShowPolicy": { "noShowFeePercent": 0, "additionalPaymentTimeoutMinutes": 30 },
    "luggagePolicy": { "defaultLuggageKgPerSeat": 10 }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `403 FORBIDDEN` — caller is not an operator role or has no `operatorId`.
- `404 RESOURCE_NOT_FOUND` — operator does not exist.

### PATCH `/v1/operator/profile`

Auth: `OPERATOR_ADMIN`. Tenant isolation: operator is resolved from caller `operatorId`. Caller Operator must currently be `APPROVED`; tokens issued before a later suspend/reject return `403 FORBIDDEN` without side effects. Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "name": "VietRide Limousine",
  "contactPhone": "+84901234567",
  "logoUrl": "https://cdn.vietride.app/operators/logo.png",
  "addressStreet": "123 Le Loi",
  "addressWard": "Ben Nghe",
  "addressDistrict": "District 1",
  "addressProvince": "Ho Chi Minh City",
  "representativeName": "Nguyen Van Operator",
  "representativePhone": "+84907654321",
  "cancellationPolicy": [
    { "hoursBeforeDeparture": 24, "feePercent": 10 },
    { "hoursBeforeDeparture": 2, "feePercent": 50 }
  ],
  "parcelNoShowPolicy": { "noShowFeePercent": 0, "additionalPaymentTimeoutMinutes": 30 },
  "luggagePolicy": { "defaultLuggageKgPerSeat": 10 }
}
```

Response `200`: same `data` shape as `GET /v1/operator/profile`.

Errors:
- `403 FORBIDDEN` — caller is not `OPERATOR_ADMIN`, has no `operatorId`, or caller Operator is not currently `APPROVED`.
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — invalid policy JSON shape or invalid profile payload.

### GET `/internal/v1/operators/{operatorId}`

Auth: Internal JWT via `X-Internal-Auth`. Not exposed through Gateway. Success response is raw DTO (no `ApiResponse` wrapper); errors use the standard ADR 0004 error envelope.

Response `200`:
```json
{
  "operatorId": "uuid",
  "name": "VietRide Limousine",
  "registrationStatus": "APPROVED",
  "isActive": true,
  "contactEmail": "ops@example.com",
  "contactPhone": "+84901234567",
  "businessRegistrationNumber": "0312345678",
  "taxCode": "0312345678"
}
```

Error `404` — `RESOURCE_NOT_FOUND`.

### GET `/internal/v1/operators/{operatorId}/subscription`

Auth: Internal JWT via `X-Internal-Auth`. Not exposed through Gateway. Success response is raw DTO; errors use the standard ADR 0004 error envelope.

Response `200`:
```json
{
  "operatorId": "uuid",
  "subscriptionId": "uuid",
  "status": "ACTIVE",
  "startedAt": "2026-06-01T10:00:00Z",
  "expiresAt": "2026-07-01T10:00:00Z",
  "plan": {
    "planId": "00000000-0000-0000-0000-000000000001",
    "name": "Starter (Free Trial)",
    "limits": {
      "maxVehicles": 3,
      "maxDrivers": 5,
      "maxAssistants": 5,
      "maxOperatorUsers": 3,
      "maxRoutes": 5,
      "maxTripsPerMonth": 100
    },
    "modules": {
      "enableParcel": false,
      "enableShuttle": false,
      "enableRag": true
    }
  },
  "usage": {
    "currentVehicles": 0,
    "currentDrivers": 0,
    "currentAssistants": 0,
    "currentOperatorUsers": 1,
    "currentRoutes": 0,
    "currentTripsThisMonth": 0
  },
  "lastResetAt": "2026-06-01T10:00:00Z"
}
```

Error `404` — `RESOURCE_NOT_FOUND`.

### POST `/internal/v1/operators/{operatorId}/usage/increment`

Auth: Internal JWT via `X-Internal-Auth`. Not exposed through Gateway. Success response is raw DTO; errors use the standard ADR 0004 error envelope.

Request:
```json
{
  "resource": "DRIVERS",
  "delta": 1
}
```

Allowed `resource`: `VEHICLES`, `DRIVERS`, `ASSISTANTS`, `OPERATOR_USERS`, `ROUTES`, `TRIPS_THIS_MONTH`. `delta` must be a positive integer.

Response `200`: same raw DTO shape as `GET /internal/v1/operators/{operatorId}/subscription`, with the updated `usage` counters.

Errors:
- `404 RESOURCE_NOT_FOUND` — operator or subscription does not exist.
- `402 SUBSCRIPTION_EXPIRED` — operator subscription has expired.
- `422 SUBSCRIPTION_LIMIT_EXCEEDED` — `current + delta` would exceed the matching plan limit.
- `422 VALIDATION_ERROR` — invalid resource or delta.

### GET `/v1/stations/search`

Auth: `OPERATOR_STAFF`, `OPERATOR_ADMIN`.

Query: `q`, `city?`, `province?`.

`q` is required. Blank or empty `q` is invalid and returns `422 VALIDATION_ERROR`.

Matching: accent-insensitive contains via `unaccent(name) ILIKE unaccent('%' || q || '%')`.

Day-7 exception: this endpoint intentionally uses `q` (not BSOT §5.8 `search`) because `technical_context_v7` line 523 is higher-priority for the OperatorStation Management flow.

`pg_trgm` is enabled only for canonical schema compatibility with the deferred `idx_stations_name_trgm ... gin_trgm_ops WHERE FALSE` placeholder. Trigram similarity search and distance-from-operator-coordinates ranking are deferred.

Response `200`: `StationSearchResult[]` in the ADR 0004 success envelope.

`StationSearchResult` shape:
```json
{
  "id": "uuid",
  "name": "Bến xe Miền Tây",
  "city": "Ho Chi Minh City",
  "province": "Ho Chi Minh",
  "latitude": 10.7212345,
  "longitude": 106.6267890,
  "addressStreet": "Kinh Dương Vương",
  "supportsShuttle": true
}
```

### POST `/v1/operator/stations`

Auth: `OPERATOR_STAFF`, `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active.
Identity validation failures (404, 5xx, transport, circuit-breaker) map to `422 VALIDATION_ERROR` per current BSOT logical-FK rule; non-APPROVED or inactive operators get `403 FORBIDDEN`.

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

Create-Station branch request (field names derive from `stations` columns; JSON uses the contract's existing camelCase style for multi-word names):
```json
{
  "name": "Bến xe Miền Tây",
  "city": "Ho Chi Minh City",
  "province": "Ho Chi Minh",
  "latitude": 10.7212345,
  "longitude": 106.6267890,
  "addressStreet": "Kinh Dương Vương",
  "contactPhone": "02837650601",
  "contactEmail": "info@bexe.com",
  "operatingHours": {
    "mon": "05:00-22:00"
  },
  "facilities": ["waiting_room", "parking"],
  "supportsShuttle": true
}
```

Branching:
- `stationId` present -> link existing Station only.
- station fields present -> create Station + auto-link in one transaction.
- create branch requires both `latitude` and `longitude` even though DB columns are nullable; missing either returns `422 VALIDATION_ERROR`.
- link branch validates the target Station is present and active; missing or inactive `stationId` returns `404 STATION_NOT_FOUND` in the ADR 0004 error envelope.
- link branch duplicate `(operatorId, stationId)` returns HTTP `200` success envelope with the existing `OperatorStation` mapping; no new mapping is created.
- If create branch finds an existing active Station within <100m, return `200` with `data.warning.code = "STATION_DUPLICATE_NEARBY"` and `data.nearbyStations: StationSearchResult[]`; do not create a Station.

Duplicate-nearby response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "warning": {
      "code": "STATION_DUPLICATE_NEARBY",
      "message": "A nearby active station already exists. Link an existing station instead."
    },
    "nearbyStations": [
      {
        "id": "uuid",
        "name": "Bến xe Miền Tây",
        "city": "Ho Chi Minh City",
        "province": "Ho Chi Minh",
        "latitude": 10.7212345,
        "longitude": 106.6267890,
        "addressStreet": "Kinh Dương Vương",
        "supportsShuttle": true
      }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

`data.nearbyStations` uses the same `StationSearchResult[]` item shape as `GET /v1/stations/search`; this is a data payload detail and does not change `meta` / `ApiMeta`.

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "operatorId": "uuid",
    "stationId": "uuid",
    "isActive": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/operator/stops`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active.
Identity validation failures (404, 5xx, transport, circuit-breaker) map to `422 VALIDATION_ERROR` per current BSOT logical-FK rule; non-APPROVED or inactive operators get `403 FORBIDDEN`.

`google_place_id` is an opaque persisted string only; no live Google Maps/Places call in Day 7.

Day 7 does not accept or mutate `shared_suggestion` / `sharedSuggestion`; that write path is deferred.

DELETE / disable-with-replacement is deferred to Day 24.

Coordinates validate latitude in [-90, 90] and longitude in [-180, 180].

Request:
```json
{
  "name": "Trạm dừng Phú Lâm",
  "description": "Điểm đón phía trước cổng chính",
  "latitude": 10.7321000,
  "longitude": 106.6142000,
  "address": "123 Hồng Bàng, Quận 6",
  "googlePlaceId": "ChIJ1234567890"
}
```

Response `201`: created Stop DTO in ADR 0004 envelope.

### GET `/v1/operator/stops`

Auth: `OPERATOR_STAFF`, `OPERATOR_ADMIN`.

Query: `page?`, `pageSize?`, `search?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Optional `search` is allow-listed to Stop `name` and `address` only.

Response `200`: `PagedResult<StopDto>`.

### GET `/v1/operator/stops/{id}`

Auth: `OPERATOR_STAFF`, `OPERATOR_ADMIN`.

Tenant isolation: missing Stop or Stop owned by another operator returns `404 STOP_NOT_FOUND` in the ADR 0004 error envelope.

Response `200`: canonical Stop DTO.

### PATCH `/v1/operator/stops/{id}`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active.

Identity logical-FK/status validation for this write: Identity 404, Identity 5xx, transport failures, and circuit-breaker failures map to `422 VALIDATION_ERROR`; non-APPROVED or inactive operators get `403 FORBIDDEN`.

Tenant isolation: missing Stop or Stop owned by another operator returns `404 STOP_NOT_FOUND` in the ADR 0004 error envelope.

Coordinates validate latitude in [-90, 90] and longitude in [-180, 180]; invalid `latitude` or `longitude` returns `422 VALIDATION_ERROR`.

Request: partial Stop update.

Response `200`: updated Stop DTO.

### GET `/internal/v1/stations/{id}`

Auth: internal service authentication required (`X-Internal-Auth: Bearer <jwt>`).

Response `200`: raw Station DTO (successful internal response is not wrapped).

Errors:
- `404 STATION_NOT_FOUND` — Station does not exist; returned in ADR 0004 error envelope.

### GET `/internal/v1/stops/{id}`

Auth: internal service authentication required (`X-Internal-Auth: Bearer <jwt>`).

Response `200`: raw Stop DTO (successful internal response is not wrapped).

Errors:
- `404 STOP_NOT_FOUND` — Stop does not exist; returned in ADR 0004 error envelope.

## Trip Route Management (Day 8)

### Role matrix and shared rules

Gateway route entries allow both `OPERATOR_ADMIN` and `OPERATOR_STAFF`, but Trip controllers enforce method-level roles:

| Method | Role(s) |
|---|---|
| `POST`, `PATCH`, `DELETE` | `OPERATOR_ADMIN` only |
| `GET` list/by-id | `OPERATOR_ADMIN`, `OPERATOR_STAFF` |

All public responses use the ADR 0004 `ApiResponse<T>` envelope. Success responses include `{ success, statusCode, data, meta }`; errors include `{ success: false, statusCode, error: { code, message, fields? }, meta }`.

Write endpoints in this Day-8 section do not require `Idempotency-Key` per BSOT §5.6.

Tenant isolation: a missing Route or a Route not owned by the caller's operator returns `404 ROUTE_NOT_FOUND` in the ADR 0004 error envelope. Child resources under a Route apply the same parent Route tenant check first unless noted otherwise.

Route create/update money fields are VND BIGINT-compatible JSON numbers. Persisted values follow the shared Money rule and are floored to 1000 before storage.

### Route DTOs

`RouteDto` shape:
```json
{
  "id": "uuid",
  "operatorId": "uuid",
  "name": "Ho Chi Minh City to Da Lat",
  "originStationId": "uuid",
  "destinationStationId": "uuid",
  "returnRouteId": "uuid",
  "baseFare": 250000,
  "totalDistanceKm": 308.50,
  "estimatedDurationMinutes": 420,
  "isActive": true,
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

`returnRouteId` is nullable and one-way: setting Route A `returnRouteId = B` does not mutate Route B.

### POST `/v1/operator/routes`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active; non-APPROVED or inactive operators get `403 FORBIDDEN`.

Request:
```json
{
  "name": "Ho Chi Minh City to Da Lat",
  "originStationId": "uuid",
  "destinationStationId": "uuid",
  "returnRouteId": "uuid",
  "baseFare": 250000,
  "totalDistanceKm": 308.50,
  "estimatedDurationMinutes": 420,
  "isActive": true
}
```

Validation:
- `originStationId` and `destinationStationId` must reference existing active Stations; missing Station returns `404 STATION_NOT_FOUND`.
- Before creating a Route, the caller operator must have an active `OperatorStation` link for both origin and destination Station. Missing or inactive link returns `422 VALIDATION_ERROR` with `error.fields` on `originStationId` and/or `destinationStationId`.
- `originStationId == destinationStationId` returns `422 VALIDATION_ERROR`.
- `returnRouteId`, when present, must reference an existing, active, non-soft-deleted Route owned by the same caller operator; missing or cross-operator target returns `404 ROUTE_NOT_FOUND`.

Response `201`: `RouteDto` in the ADR 0004 success envelope.

### GET `/v1/operator/routes`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`, `search?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Optional `search` follows BSOT §5.8 and is allow-listed to Route `name`.

Response `200`: `PagedResult<RouteDto>` in the ADR 0004 success envelope.

### GET `/v1/operator/routes/{id}`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Tenant isolation: missing Route, soft-deleted Route, or Route owned by another operator returns `404 ROUTE_NOT_FOUND`.

Response `200`: `RouteDto` in the ADR 0004 success envelope.

### PATCH `/v1/operator/routes/{id}`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request: partial Route update.
```json
{
  "name": "Ho Chi Minh City to Da Lat Express",
  "returnRouteId": "uuid",
  "baseFare": 260000,
  "totalDistanceKm": 308.50,
  "estimatedDurationMinutes": 400,
  "isActive": true
}
```

Validation mirrors Route create for mutable fields. `returnRouteId`, when present, must reference an existing, active, non-soft-deleted Route owned by the same caller operator; missing or cross-operator target returns `404 ROUTE_NOT_FOUND`.

Response `200`: updated `RouteDto` in the ADR 0004 success envelope.

### RouteStop DTOs

`RouteStopDto` shape:
```json
{
  "routeId": "uuid",
  "stopId": "uuid",
  "orderIndex": 1,
  "estimatedDurationFromOriginMinutes": 90,
  "distanceFromOriginKm": 75.25,
  "allowPickup": true,
  "allowDropoff": false,
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

RouteStop entries are intermediate waypoints only; Route origin/destination Stations live on the Route entity.

### POST `/v1/operator/routes/{id}/stops`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "stopId": "uuid",
  "orderIndex": 1,
  "estimatedDurationFromOriginMinutes": 90,
  "distanceFromOriginKm": 75.25,
  "allowPickup": true,
  "allowDropoff": false
}
```

Validation:
- Parent Route must belong to caller operator; otherwise `404 ROUTE_NOT_FOUND`.
- `stopId` must belong to caller operator; otherwise `404 STOP_NOT_FOUND`.
- `allowPickup=false` and `allowDropoff=false` is rejected with `422 ROUTE_STOP_FLAGS_INVALID`; `error.fields.allowPickup` identifies the discriminator.
- Duplicate `orderIndex` within the same Route is rejected with `422 ROUTE_STOP_ORDER_CONFLICT`; `error.fields.orderIndex` identifies the discriminator.

Response `201`: `RouteStopDto` in the ADR 0004 success envelope.

### DELETE `/v1/operator/routes/{id}/stops/{stopId}`

Auth: `OPERATOR_ADMIN`.

RouteStop delete is a hard-delete of the junction row. `route_stops` has no `deleted_at`; Day 8 has no booking-impact check because Trips/Bookings do not exist yet.

Validation:
- Parent Route must belong to caller operator; otherwise `404 ROUTE_NOT_FOUND`.
- Missing RouteStop returns `404 STOP_NOT_FOUND`.

Response `200`: success envelope with `{ "deleted": true }`.

### RouteStopFareTemplate DTOs

`RouteStopFareTemplateDto` shape:
```json
{
  "id": "uuid",
  "routeId": "uuid",
  "stopId": "uuid",
  "fareFromThisStop": 200000,
  "effectiveFrom": "2026-07-01T00:00:00+07:00",
  "effectiveUntil": "2026-08-01T00:00:00+07:00",
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

`fareFromThisStop` is an exception override for Route base fare. It is VND BIGINT-compatible and is floored to 1000 before persisting. Stops without a fare-template entry use `Route.baseFare`.

### POST `/v1/operator/routes/{id}/fare-templates`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "stopId": "uuid",
  "fareFromThisStop": 200000,
  "effectiveFrom": "2026-07-01T00:00:00+07:00",
  "effectiveUntil": "2026-08-01T00:00:00+07:00"
}
```

Validation:
- Parent Route must belong to caller operator; otherwise `404 ROUTE_NOT_FOUND`.
- `stopId` must belong to caller operator; otherwise `404 STOP_NOT_FOUND`.
- `stopId` must already be a RouteStop on the same Route; otherwise `422 VALIDATION_ERROR` with `error.fields.stopId`.
- `effectiveUntil`, when present, must be greater than `effectiveFrom`; otherwise `422 VALIDATION_ERROR` with `error.fields.effectiveUntil`.
- A new `[effectiveFrom, effectiveUntil)` window must not overlap an existing template window for the same `(routeId, stopId)`. Overlap is rejected with `422 VALIDATION_ERROR` and `error.fields.effectiveFrom`/`error.fields.effectiveUntil`. `effectiveUntil = null` is treated as open-ended.

Response `201`: `RouteStopFareTemplateDto` in the ADR 0004 success envelope.

### GET `/v1/operator/routes/{id}/fare-templates`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`).

Response `200`: `PagedResult<RouteStopFareTemplateDto>` in the ADR 0004 success envelope.

### AlternativeRoute DTOs

`AlternativeRouteStopDto` shape:
```json
{
  "alternativeRouteId": "uuid",
  "stopId": "uuid",
  "orderIndex": 1,
  "estimatedDurationFromOriginMinutes": 80,
  "distanceFromOriginKm": 70.25,
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

`AlternativeRouteDto` shape:
```json
{
  "id": "uuid",
  "routeId": "uuid",
  "name": "Da Lat bypass via Bao Loc",
  "description": "Use when the main pass is disrupted.",
  "destinationStationId": "uuid",
  "totalDistanceKm": 320.00,
  "estimatedDurationMinutes": 450,
  "isActive": true,
  "stops": [
    {
      "alternativeRouteId": "uuid",
      "stopId": "uuid",
      "orderIndex": 1,
      "estimatedDurationFromOriginMinutes": 80,
      "distanceFromOriginKm": 70.25,
      "createdAt": "2026-06-10T10:00:00Z",
      "updatedAt": "2026-06-10T10:00:00Z"
    }
  ],
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

Each main Route can have at most two active AlternativeRoutes. AlternativeRoute stops are an independent stop sequence and do not reuse RouteStop rows.

### POST `/v1/operator/routes/{id}/alternative-routes`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "name": "Da Lat bypass via Bao Loc",
  "description": "Use when the main pass is disrupted.",
  "destinationStationId": "uuid",
  "totalDistanceKm": 320.00,
  "estimatedDurationMinutes": 450,
  "stops": [
    {
      "stopId": "uuid",
      "orderIndex": 1,
      "estimatedDurationFromOriginMinutes": 80,
      "distanceFromOriginKm": 70.25
    }
  ]
}
```

Validation:
- Parent Route must belong to caller operator; otherwise `404 ROUTE_NOT_FOUND`.
- `destinationStationId` must reference an existing active Station; missing Station returns `404 STATION_NOT_FOUND`.
- A third active AlternativeRoute for the same parent Route is rejected with `422 ALTERNATIVE_ROUTE_LIMIT_EXCEEDED`; `error.fields.alternativeRoutes` identifies the discriminator. Only active rows count toward the cap.
- Duplicate `orderIndex` within the same AlternativeRoute stop sequence is rejected with `422 VALIDATION_ERROR` and `error.fields.orderIndex`.
- `stopId` values in the alternative stop sequence must belong to caller operator; otherwise `404 STOP_NOT_FOUND`.

Response `201`: `AlternativeRouteDto` in the ADR 0004 success envelope.

### GET `/v1/operator/routes/{id}/alternative-routes`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`).

Response `200`: `PagedResult<AlternativeRouteDto>` in the ADR 0004 success envelope.

### PATCH `/v1/operator/alternative-routes/{altId}`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request: partial AlternativeRoute update.
```json
{
  "name": "Da Lat bypass via Bao Loc updated",
  "description": "Use when main route is blocked.",
  "destinationStationId": "uuid",
  "totalDistanceKm": 321.00,
  "estimatedDurationMinutes": 455,
  "isActive": true,
  "stops": [
    {
      "stopId": "uuid",
      "orderIndex": 1,
      "estimatedDurationFromOriginMinutes": 80,
      "distanceFromOriginKm": 70.25
    }
  ]
}
```

Validation mirrors AlternativeRoute create for mutable fields. Missing AlternativeRoute or AlternativeRoute whose parent Route belongs to another operator returns `404 ROUTE_NOT_FOUND`.

Response `200`: updated `AlternativeRouteDto` in the ADR 0004 success envelope.

### DELETE `/v1/operator/alternative-routes/{altId}`

Auth: `OPERATOR_ADMIN`.

AlternativeRoute delete deactivates the row by setting `isActive=false`; it is not a hard-delete and `alternative_routes` has no `deleted_at`. Deactivating one AlternativeRoute frees one slot toward the max-two-active cap.

Validation: missing AlternativeRoute or AlternativeRoute whose parent Route belongs to another operator returns `404 ROUTE_NOT_FOUND`.

Response `200`: success envelope with `{ "isActive": false }`.

### Day-8 dedicated validation error examples

RouteStop order conflict:
```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "ROUTE_STOP_ORDER_CONFLICT",
    "message": "A route stop with the same order index already exists.",
    "fields": [
      { "field": "orderIndex", "message": "Order index must be unique within a route." }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-10T10:00:00Z" }
}
```

RouteStop flags invalid:
```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "ROUTE_STOP_FLAGS_INVALID",
    "message": "At least one of allowPickup or allowDropoff must be true.",
    "fields": [
      { "field": "allowPickup", "message": "allowPickup and allowDropoff cannot both be false." }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-10T10:00:00Z" }
}
```

AlternativeRoute active limit exceeded:
```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "ALTERNATIVE_ROUTE_LIMIT_EXCEEDED",
    "message": "A route can have at most two active alternative routes.",
    "fields": [
      { "field": "alternativeRoutes", "message": "Deactivate an existing alternative route before creating another one." }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-10T10:00:00Z" }
}
```
