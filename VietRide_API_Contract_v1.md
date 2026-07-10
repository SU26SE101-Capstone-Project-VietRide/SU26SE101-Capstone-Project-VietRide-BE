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

### POST `/v1/auth/resend-verification-email`

Auth: public. Resends a registration OTP for an account in `PENDING_EMAIL_VERIFICATION`.

Request:
```json
{
  "email": "user@example.com",
  "purpose": "REGISTRATION"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "email": "user@example.com",
    "status": "PENDING_EMAIL_VERIFICATION",
    "otpTtlMinutes": 5
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` - unknown email or invalid purpose:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Ma xac thuc khong dung." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `409` - email already verified:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_VERIFIED", "message": "Email da duoc xac minh." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `429` - OTP rate limit exceeded:
```json
{
  "success": false,
  "statusCode": 429,
  "error": { "code": "AUTH_OTP_RATE_LIMIT_EXCEEDED", "message": "Too many OTP requests. Please try again later." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/auth/forgot-password`

Auth: public. Requests a password-reset OTP for an `ACTIVE` user. To prevent account enumeration, unknown emails and non-eligible accounts return the same `200` shape without sending an OTP.

Request:
```json
{
  "email": "user@example.com"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "email": "user@example.com",
    "otpTtlMinutes": 5
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `429` - password reset OTP rate limit exceeded:
```json
{
  "success": false,
  "statusCode": 429,
  "error": { "code": "AUTH_OTP_RATE_LIMIT_EXCEEDED", "message": "Too many OTP requests. Please try again later." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/auth/reset-password`

Auth: public. Resets the password for an `ACTIVE` user using a `PASSWORD_RESET` OTP. On success, all active refresh tokens for that user are revoked with reason `PASSWORD_RESET`.

Request:
```json
{
  "email": "user@example.com",
  "code": "123456",
  "newPassword": "Password123!"
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

Error `400` - wrong OTP code or non-eligible account:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Ma xac thuc khong dung." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `400` - expired OTP:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_EXPIRED", "message": "Ma xac thuc da het han." },
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
      "phone": "+84901234567",
      "displayName": "Nguyen Van A",
      "role": "PASSENGER",
      "operatorId": null,
      "status": "ACTIVE"
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Passenger accounts may receive the same `200` response while `user.status = "PENDING_EMAIL_VERIFICATION"`.
The mobile FE treats that as a restricted session and prompts email OTP verification from Profile.

Error `401` — invalid credentials:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_INVALID_CREDENTIALS", "message": "Email hoặc mật khẩu không đúng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Error `403` — unverified email for non-passenger accounts:
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
      "phone": null,
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

### GET `/v1/passenger/me`

Auth: User Access Token (RS256). Idempotency-Key: not required (read endpoint).

> Note: stub -- item schema finalized in Sprint 3 (SCV-76 / Booking).
> Passenger profile reuses the `/v1/users/me` projection verbatim; no passenger-specific fields are defined this day.

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

### GET `/v1/passenger/bookings`

Auth: User Access Token (RS256). Idempotency-Key: not required (read endpoint).

> Note: stub -- item schema finalized in Sprint 3 (SCV-76 / Booking).
> Returns the canonical empty paginated envelope; the booking item schema is NOT defined this day.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [],
    "total": 0,
    "page": 1,
    "pageSize": 20,
    "totalPages": 0,
    "hasNext": false,
    "hasPrev": false
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

### GET `/v1/admin/operator-users`

Auth: `SYSTEM_ADMIN`. Lists Driver, Assistant, and Operator Staff users across all operators. Optional `operatorId` filters to a specific operator. Idempotency-Key: not required (read endpoint).

Query parameters:
- `page` — optional, default `1`, minimum `1`.
- `pageSize` — optional, default `20`, range `1..100`.
- `search` — optional, searches `email`, `displayName`, and normalized exact `phone` when the value is a valid phone number.
- `sortBy` — optional, default `createdAt`; allowed: `createdAt`, `email`, `displayName`, `role`, `status`.
- `sortDir` — optional, default `desc`; allowed: `asc`, `desc`.
- `role` — optional; allowed: `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`.
- `status` — optional; any valid `UserStatus` value.
- `operatorId` — optional; filters by operator.

Response `200`: same shape as `GET /v1/operator/users`.

Errors:
- `403 FORBIDDEN` — caller is not `SYSTEM_ADMIN`.
- `400 INVALID_SORT_FIELD` — `sortBy` is not in the allow-list.
- `422 VALIDATION_ERROR` — invalid paging/filter value.

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
    "paymentRedirectUrl": null,
    "tickets": [
      {
        "ticketId": "uuid",
        "ticketCode": "VT-20260518-ABCDEFGH",
        "seatNumber": "A01",
        "status": "ISSUED",
        "fareAmount": 400000,
        "discountAmount": 50000,
        "paidAmount": 350000
      }
    ]
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
    "outbound": { "bookingId": "uuid", "bookingCode": "VR-20260518-ABCD1234", "totalAmount": 350000, "discountAmount": 50000, "tickets": [{ "ticketId": "uuid", "ticketCode": "VT-20260518-ABCDEFGH", "seatNumber": "A01", "status": "PENDING_PAYMENT", "fareAmount": 400000, "discountAmount": 50000, "paidAmount": 350000 }] },
    "return": { "bookingId": "uuid", "bookingCode": "VR-20260519-EFGH5678", "totalAmount": 350000, "discountAmount": 50000, "tickets": [{ "ticketId": "uuid", "ticketCode": "VT-20260519-HGFEDCBA", "seatNumber": "A01", "status": "PENDING_PAYMENT", "fareAmount": 400000, "discountAmount": 50000, "paidAmount": 350000 }] },
    "grandTotal": 700000,
    "paymentRedirectUrl": "https://vnpay.vn/..."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Rules:
- `paymentMethod=WALLET` is an all-or-nothing checkout across both legs. On success, each leg still has its own Payment record with `referenceType=BOOKING`; the client must never observe a retained first-leg debit if the second leg fails.
- `paymentMethod=VNPAY` may use a combined checkout with `referenceType=BOOKING_GROUP` and one redirect for `grandTotal`.
- `BOOKING_GROUP` is VNPay-only for this endpoint; WALLET success remains two per-booking payments.
- `paymentRedirectUrl` is `null` for WALLET and populated only when VNPay returns a redirect.

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

### GET `/internal/v1/bookings/{bookingId}`

Auth: Internal JWT. Used by Parcel to validate that a parcel can attach to a booking.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "userId": "uuid",
    "tripId": "uuid",
    "status": "CONFIRMED",
    "activeTicketCount": 1,
    "tickets": [
      {
        "ticketId": "uuid",
        "ticketCode": "VT-20260518-ABCDEFGH",
        "seatNumber": "A01",
        "status": "ISSUED"
      }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

`activeTicketCount` counts tickets in `ISSUED` or `USED`. Parcel attach must reject bookings with
`activeTicketCount = 0`.

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
    "fareDelta": 0,
    "refundAmount": 0,
    "paymentRedirectUrl": null
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Rules (price-neutral-only, BSOT v1.11.0 — human decision 2026-06-12, supersedes technical_context_v7 lines 1639-1656 downgrade-and-refund): compute the new fare from `Trip.baseFare` for terminal pickup or `TripStopFare` for along-route pickup; the edit is allowed ONLY when the new fare equals the current fare. Any fare difference — increase OR decrease — is **rejected** with `409 BOOKING_EDIT_PICKUP_PRICE_CHANGED`; to change to a different-priced pickup the passenger must cancel the booking and rebook. `fareDelta` and `refundAmount` are therefore always `0` on success; no wallet refund/charge path exists on this endpoint.

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

### GET `/v1/admin/vouchers`

> Platform voucher list only: returns vouchers where `ownerOperatorId = null`. Read-only — no Idempotency-Key.

Auth: `SYSTEM_ADMIN`.

Query: `fundingType?` (`VIETRIDE_FUNDED` | `OPERATOR_FUNDED`), `isActive?` (bool), plus standard `QueryOptions` paging/sort (`page`/`pageSize` clamped 1..100, `sortBy` whitelisted — default `createdAt` `desc`; non-whitelisted → `422 INVALID_SORT_FIELD`). `ownerOperatorId` is not supported on this endpoint and must not expose operator-owned vouchers. `applicableServices` in each item contains `BOOKING`, `PARCEL`, or both. v1 returns only active (non-soft-deleted) vouchers (respects EF `HasQueryFilter(deleted_at == null)`); `includeDeleted` not supported in v1.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "uuid",
        "code": "SUMMER26",
        "name": "Summer Sale 20%",
        "type": "PERCENT_OFF",
        "value": 20,
        "minOrderAmount": 100000,
        "maxDiscountAmount": 50000,
        "totalUsageLimit": 1000,
        "perUserLimit": 1,
        "newUserOnly": false,
        "applicableServices": ["BOOKING", "PARCEL"],
        "applicablePaymentMethods": ["VNPAY", "WALLET"],
        "applicableOperatorIds": ["operator-uuid"],
        "applicableRouteIds": ["route-uuid"],
        "fundingType": "VIETRIDE_FUNDED",
        "ownerOperatorId": null,
        "isActive": true,
        "validFrom": "2026-06-01T00:00:00+07:00",
        "validUntil": "2026-08-31T23:59:59+07:00",
        "createdAt": "2026-06-20T10:00:00+07:00"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-20T10:00:00+07:00" }
}
```

Error `403` (non-SYSTEM_ADMIN) → `FORBIDDEN`.

### POST `/v1/admin/vouchers`

Auth: `SYSTEM_ADMIN`. Idempotency: required.

Creates a platform voucher (`owner_operator_id = null`). `OPERATOR_FUNDED` requires non-null `applicableOperatorIds` (null → `422 VALIDATION_ERROR`, Q3); INSERTs one `PENDING` `OperatorVoucherConsent` per listed operator. `VIETRIDE_FUNDED` creates no consent rows. `code = null` → auto-generate 8-char uppercase base32 unique among non-deleted. Duplicate `code` (among non-soft-deleted) → `409 VOUCHER_CODE_CONFLICT`.

Request:
```json
{
  "code": "SUMMER26",
  "name": "Summer Sale 20%",
  "type": "PERCENT_OFF",
  "value": 20,
  "minOrderAmount": 100000,
  "maxDiscountAmount": 50000,
  "totalUsageLimit": 1000,
  "perUserLimit": 1,
  "validFrom": "2026-06-01T00:00:00+07:00",
  "validUntil": "2026-08-31T23:59:59+07:00",
  "applicableOperatorIds": null,
  "applicableRouteIds": null,
  "fundingType": "VIETRIDE_FUNDED"
}
```

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "id": "uuid",
    "code": "SUMMER26",
    "name": "Summer Sale 20%",
    "type": "PERCENT_OFF",
    "value": 20,
    "fundingType": "VIETRIDE_FUNDED",
    "ownerOperatorId": null,
    "isActive": true,
    "validFrom": "2026-06-01T00:00:00+07:00",
    "validUntil": "2026-08-31T23:59:59+07:00",
    "createdAt": "2026-06-20T10:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-20T10:00:00+07:00" }
}
```

### GET `/v1/admin/vouchers/{voucherId}/consents`

Auth: `SYSTEM_ADMIN`. Source: v7:685-688. Returns operator-voucher consent records for a specific voucher (admin view of consent status across operators).

Response `200`: `ApiResponse` of list `{ id, voucherId, operatorId, status, requestedAt, respondedAt, respondedByUserId, rejectReason }`.

### GET `/v1/operator/vouchers`

Auth: `OPERATOR_ADMIN`.

Lists vouchers owned by the caller operator. The service takes `operatorId` from the authenticated JWT claim and always filters `ownerOperatorId = caller.operatorId`; client-supplied `ownerOperatorId` is not accepted. Read-only — no Idempotency-Key.

Query: `isActive?` (bool), plus standard `QueryOptions` paging/sort (`page`/`pageSize` clamped 1..100, `sortBy` whitelisted — default `createdAt` `desc`; non-whitelisted → `422 INVALID_SORT_FIELD`). No `fundingType` query in v1. v1 returns only non-soft-deleted vouchers.

Response `200`: same paged `VoucherListItem` shape as `GET /v1/admin/vouchers`, with `ownerOperatorId` populated. `applicableServices` identifies whether the voucher applies to `BOOKING`, `PARCEL`, or both.

Errors: missing/invalid `operatorId` claim → `401 UNAUTHORIZED`; non-`OPERATOR_ADMIN` → `403 FORBIDDEN`; invalid sort/filter → `422`.

### POST `/v1/operator/vouchers`

Auth: `OPERATOR_ADMIN`. Idempotency: required.

Operator self-create operator-owned voucher. `fundingType` FORCED `OPERATOR_FUNDED` (body truyền `VIETRIDE_FUNDED` → `422 VOUCHER_FORBIDDEN_FUNDING`); `applicableOperatorIds` FORCED to caller operator; `ownerOperatorId` set server-side = caller operatorId. NO `OperatorVoucherConsent` rows (self-consented), NO integration event. Duplicate global code (among non-soft-deleted) → `409 VOUCHER_CODE_CONFLICT`. `code = null` → auto 8-char uppercase base32.

Request (admin-only fields `fundingType`/`applicableOperatorIds`/`ownerOperatorId` omitted — server-forced):
```json
{
  "code": "OPABCDEF",
  "name": "Tết Discount 50k",
  "type": "FIXED_AMOUNT",
  "value": 50000,
  "minOrderAmount": 100000,
  "maxDiscountAmount": null,
  "totalUsageLimit": 100,
  "perUserLimit": 1,
  "validFrom": "2026-06-01T00:00:00+07:00",
  "validUntil": "2026-08-31T23:59:59+07:00",
  "applicableRouteIds": null
}
```

Response `201`: same shape as admin create with `ownerOperatorId` populated and `fundingType: "OPERATOR_FUNDED"`.

### PATCH `/v1/operator/vouchers/{id}`

Auth: `OPERATOR_ADMIN`. Idempotency: none (behavior-idempotent). Scoped to `owner_operator_id == caller` (cross-operator → `404 VOUCHER_NOT_FOUND`).

Partial update of mutable fields: `name`, `value`, `minOrderAmount`, `maxDiscountAmount`, `totalUsageLimit`, `perUserLimit`, `validFrom`, `validUntil`, `applicableRouteIds`. `code`/`type`/`fundingType`/`ownerOperatorId` ALWAYS immutable (attempt rejected). Freeze-on-first-use (Q6): while `voucher_usages` count == 0 all listed fields editable; once >=1 usage exists the economic fields `value`/`minOrderAmount`/`maxDiscountAmount` FREEZE (edit → `409 VOUCHER_LOCKED`) — only `name`, EXTENDING `validUntil` (not shortening below current), LOOSENING limits, `applicableRouteIds`, and deactivate remain editable.

Request: partial body of the mutable fields above. Response `200`: `ApiResponse` of the updated voucher (same shape as create response).

### DELETE `/v1/operator/vouchers/{id}`

Auth: `OPERATOR_ADMIN`. Idempotency: none. Soft-delete (sets `deleted_at`); code becomes reusable (partial unique `WHERE deleted_at IS NULL`). Scoped to owner (cross-operator → `404 VOUCHER_NOT_FOUND`). Response `200`: `ApiResponse` of `{ id, deletedAt }`.

### POST `/v1/operator/vouchers/{id}/activate` + `/deactivate`

Auth: `OPERATOR_ADMIN`. Idempotency: none (behavior-idempotent). No body. Flips `is_active` (IActivatable). Scoped to owner (cross-operator → `404 VOUCHER_NOT_FOUND`). Response `200`: `ApiResponse` of `{ id, isActive }`.

### GET `/v1/operator/voucher-consents`

Auth: `OPERATOR_STAFF`/`OPERATOR_ADMIN`. Source: v7:659-663. Query: `status?` (`PENDING`|`ACCEPTED`|`REJECTED`). Returns operator-scoped consents (tenant isolation — operator may only see consents for own `operatorId` from JWT). Response `200`: `ApiResponse` of list `{ id, voucherId, voucherCode, voucherType, voucherValue, validFrom, validUntil, minOrderAmount, maxDiscountAmount, applicableRouteIds, status, requestedAt, respondedAt, respondedByUserId }`.

### POST `/v1/operator/voucher-consents/{id}/accept`

Auth: `OPERATOR_ADMIN` only (OPERATOR_STAFF → `403 FORBIDDEN`, fine-grained in .NET controller). Idempotency: required. Source: v7:665-672. Precondition: `status = PENDING`. Flips `PENDING → ACCEPTED`, sets `respondedAt`/`respondedByUserId`, publishes `booking.voucher.consent_accepted { voucherId, operatorId }` via Outbox. Response `200`: `ApiResponse` of `{ id, status }`.

### POST `/v1/operator/voucher-consents/{id}/reject`

Auth: `OPERATOR_ADMIN` only. Idempotency: required. Source: v7:674-683. Precondition: `status IN (PENDING, ACCEPTED)`. Optional body `{ "reason": "text" }`. Flips → `REJECTED`, sets `respondedAt`/`respondedByUserId`/`rejectReason`, publishes `booking.voucher.consent_rejected { voucherId, operatorId, reason? }`. Revoke after accept (`ACCEPTED → REJECTED`) does NOT roll back discount on already-CONFIRMED bookings. Response `200`: `ApiResponse` of `{ id, status }`.

## Trip, Route & Vehicle Service

> **Trip ↔ Booking seam (ownership & seat lifecycle).** `TripSeat` (per-trip seat
> inventory) is owned by **Trip-Route-Vehicle**, not Booking — it lives in the
> `trip-route-vehicle` schema (`trip_seats`) and is generated by the Trip Hangfire job
> from `Vehicle.seatLayoutJson` when a Trip is created. Booking owns only `bookings` /
> `passengers`. Cross-DB FK is forbidden, so Booking never touches `trip_seats` directly —
> it drives the seat lifecycle through the **synchronous internal HTTP** endpoints below
> (per technical_context §6.10 hybrid saga: sync HTTP for the core seat/payment path, no
> event on the seat path). Lifecycle:
>
> 1. `POST /internal/v1/trips/{tripId}/lock-seats` — checkout start. Trip checks each seat
>    is `AVAILABLE`, flips `AVAILABLE → HELD`, writes Redis key `seat_lock:{tripId}:{seatNumber}`
>    (TTL `SEAT_LOCK_TTL_MINUTES`, 10 min), returns a `seatLockToken`. **All-or-nothing.**
> 2. `POST /internal/v1/trips/{tripId}/book-seats` — payment success. Trip flips the token's
>    seats `HELD → BOOKED`.
> 3. `POST /internal/v1/trips/{tripId}/release-seats` — payment fail / timeout / cancel.
>    Trip flips the token's seats `HELD → AVAILABLE` (compensation, idempotent).
>
> Booking reads trip pricing/stop data for checkout via `GET /internal/v1/trips/{tripId}`
> (raw DTO). The FE-facing `GET /v1/trips/{tripId}` / `/seat-map` project from the same data.
>
> **Parallel-work contract:** this seam is the frozen interface between the Trip track
> (implements these endpoints + the Redis lock + `trip_seats` generation) and the Booking
> track (calls them through an internal Trip HTTP client, mockable until Trip lands them).
> Neither side may change the request/response shapes below without updating this section.

## Location Catalog

### GET `/v1/locations`

Auth: public.

Purpose: FE loads this once at app start and caches it for the origin/destination search UI. FE must not hardcode provinces/cities.

Response `200`: active locations sorted by `sortOrder`, then `name`.
```json
{
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "id": "uuid",
      "code": "HCM",
      "name": "Ho Chi Minh City",
      "type": "MUNICIPALITY",
      "isActive": true,
      "sortOrder": 5
    }
  ],
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-08T00:00:00Z" }
}
```

### Admin Location APIs

Auth: `SYSTEM_ADMIN`.

Endpoints:
- `GET /v1/admin/locations?page=&pageSize=&search=&isActive=`
- `POST /v1/admin/locations`
- `PATCH /v1/admin/locations/{id}`
- `DELETE /v1/admin/locations/{id}` soft-deactivates the location.

Create/update request:
```json
{
  "code": "HCM",
  "name": "Ho Chi Minh City",
  "type": "MUNICIPALITY",
  "sortOrder": 5,
  "isActive": true
}
```

Rules:
- `code` is unique and normalized to uppercase.
- `type` is `PROVINCE` or `MUNICIPALITY`.
- Duplicate code returns `409 LOCATION_CODE_CONFLICT`.
- Missing/inactive location references in station/stop/trip search validation return `422 VALIDATION_ERROR`.

### GET `/v1/trips/search`

Auth: optional/passenger.

Query:
- Specific station mode: `originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`.
- FE city/province mode: `originLocationCode`, `destinationLocationCode`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`.

If both station IDs and location codes are sent, station IDs win because they are the more specific filter. Location-code mode finds active Stations under each Location, then searches Routes/Trips using those exact Stations. Response still returns concrete origin/destination Stations for display.

Errors:
- `422 VALIDATION_ERROR` if neither a station pair nor a location-code pair is provided.
- `422 VALIDATION_ERROR` if a location code does not exist or is inactive.
- No matching station/route/trip returns an empty `200` list.

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

### GET `/internal/v1/trips/{tripId}`

Auth: Internal JWT. Callers: Booking, Parcel, Tracking, Payment (BSOT §7.2). Trip snapshot
that Booking reads for checkout fare calc + pickup/dropoff validation. Returns a **raw DTO**
(no `ApiResponse` envelope — §1.6.2 internal-endpoint convention); errors still use the
envelope.

Response `200` (raw):
```json
{
  "tripId": "uuid",
  "operatorId": "uuid",
  "routeId": "uuid",
  "vehicleId": "uuid",
  "status": "SCHEDULED",
  "departureDateTime": "2026-05-18T08:00:00+07:00",
  "estimatedArrivalTime": "2026-05-18T20:00:00+07:00",
  "baseFare": 400000,
  "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
  "destinationStation": { "id": "uuid", "name": "Bến xe Mỹ Đình" },
  "stops": [
    {
      "stopId": "uuid",
      "orderIndex": 1,
      "allowPickup": true,
      "allowDropoff": false,
      "estimatedArrivalTime": "2026-05-18T09:30:00+07:00",
      "distanceFromOriginKm": 42.5,
      "fareFromThisStop": 350000
    }
  ],
  "seatSummary": { "totalSeats": 40, "availableSeats": 18 },
  "returnRouteId": "uuid | null",
  "driverUserId": "uuid | null",
  "assistantUserId": "uuid | null"
}
```

Notes:
- `fareFromThisStop` is the per-stop override from `trip_stop_fares` when present; otherwise
  the caller falls back to `baseFare` (technical_context §6.10 step 2c). `null` ⇒ use `baseFare`.
- `stops` are the along-route intermediate stops (snapshot of RouteStop into `trip_stops`),
  ordered by `orderIndex`; `allowPickup` / `allowDropoff` drive Day-13 pickup/dropoff validation.
- `returnRouteId`: nullable UUID — the return-direction route linked via `Route.returnRouteId`
  self-FK. Booking uses this to validate `ROUTE_RETURN_NOT_CONFIGURED` (422) when the passenger
  requests a round-trip but the outbound route has no return route configured
  (technical_context_v7 line 1750). Trip will expose this field in Task 11.4.
- `driverUserId` / `assistantUserId`: nullable UUID logical user keys used by downstream services
  for trip-assignment authorization. They do not create cross-database foreign keys.
- Errors: `404 TRIP_NOT_FOUND`.

### POST `/internal/v1/trips/{tripId}/lock-seats`

Auth: Internal JWT. Idempotency: required (replay with same `Idempotency-Key` returns the
same `seatLockToken`). **All-or-nothing** — if any requested seat is not `AVAILABLE`, no seat
is locked.

Request:
```json
{
  "seatNumbers": ["A01", "A02"],
  "holdOwnerId": "uuid",
  "ttlSeconds": 600
}
```
- `holdOwnerId` = passenger user id (lock owner). `ttlSeconds` optional; defaults to
  `SEAT_LOCK_TTL_MINUTES` × 60 (= 600).

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "seatLockToken": "uuid",
    "lockedSeats": ["A01", "A02"],
    "expiresAt": "2026-05-18T08:10:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `404 TRIP_NOT_FOUND`.
- `409 BOOKING_TRIP_NOT_BOOKABLE` — trip status ≠ `SCHEDULED` (closed / departed / cancelled).
- `409 BOOKING_SEAT_UNAVAILABLE` — ≥1 seat is `HELD` / `BOOKED` / `UNAVAILABLE`;
  `error.fields` lists the offending `seatNumbers`. No seat is held (all-or-nothing).
- `409 IDEMPOTENCY_REQUEST_PENDING` — same `Idempotency-Key` is still being processed.

### POST `/internal/v1/trips/round-trip/lock-seats`

Auth: Internal JWT. Idempotency: required (replay with the same `Idempotency-Key` returns the
same outbound/return lock tokens). **Round-trip atomic** — Trip locks both outbound and return
seat sets in one Redis Lua script. If either leg cannot be locked, no seat is held on either leg
(technical_context_v7 lines 1755-1757).

Request:
```json
{
  "outbound": {
    "tripId": "uuid",
    "seatNumbers": ["A01", "A02"]
  },
  "return": {
    "tripId": "uuid",
    "seatNumbers": ["A01", "A02"]
  },
  "holdOwnerId": "uuid",
  "ttlSeconds": 600
}
```
- `holdOwnerId` = passenger user id (lock owner). `ttlSeconds` optional; defaults to
  `SEAT_LOCK_TTL_MINUTES` × 60 (= 600).
- `outbound.tripId` and `return.tripId` must be different trip ids.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "outbound": {
      "tripId": "uuid",
      "seatLockToken": "uuid",
      "lockedSeats": ["A01", "A02"],
      "expiresAt": "2026-05-18T08:10:00+07:00"
    },
    "return": {
      "tripId": "uuid",
      "seatLockToken": "uuid",
      "lockedSeats": ["A01", "A02"],
      "expiresAt": "2026-05-18T08:10:00+07:00"
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Errors:
- `404 TRIP_NOT_FOUND` — outbound or return trip does not exist.
- `409 BOOKING_TRIP_NOT_BOOKABLE` — outbound or return trip status ≠ `SCHEDULED`.
- `409 BOOKING_SEAT_UNAVAILABLE` — ≥1 requested outbound/return seat is `HELD` / `BOOKED` /
  `UNAVAILABLE`; `error.fields` lists the offending `seatNumbers`. No seat is held on either leg.

### POST `/internal/v1/trips/{tripId}/release-seats`

Auth: Internal JWT. Compensation for payment fail / timeout / cancel. **Idempotent** —
releasing an already-released or expired lock is a no-op `204`. Flips the token's seats
`HELD → AVAILABLE` and clears their Redis locks.

Request:
```json
{
  "seatLockToken": "uuid",
  "seatNumbers": ["A01", "A02"]
}
```

Response `204`.

### POST `/internal/v1/trips/{tripId}/book-seats`

Auth: Internal JWT. Called after payment success. Validates `seatLockToken` is still valid
(not expired) and owns the seats, then flips them `HELD → BOOKED`.

Request:
```json
{
  "seatLockToken": "uuid",
  "bookingId": "uuid",
  "passengerSeatAssignments": [{ "passengerId": "uuid", "seatNumber": "A01" }]
}
```

Response `204`.

Errors:
- `404 TRIP_NOT_FOUND`.
- `409 BOOKING_SEAT_UNAVAILABLE` — lock token expired or no longer owns the seats (seat was
  released on TTL); Booking must compensate (release + cancel). `error.fields` lists the seats.

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

Parcel cargo policy:
- Dimension unit: centimeters; weight unit: kilograms.
- Volume precision: `decimal(10,4)` m3.
- Weight/DIM/chargeable precision: `decimal(8,2)` kg.
- Money is VND `BIGINT`, floored to 1000 before persistence.
- `PENDING_OPERATOR_ACTION` is disambiguated by `pendingActionType`: `CAPACITY_EXCEEDED`, `RESERVE_FAILED`, `REFUND_CONFIRMATION`.

### GET `/v1/parcels/available-trips`

Auth: `PASSENGER`.

Query: `originStationId`, `destinationStationId`, `departureDate`, `lengthCm`, `widthCm`, `heightCm`, `estimatedWeightKg`, `sizeCategory`.

Backend calculates `volumeM3`, `dimWeightKg`, and `chargeableWeightKg = max(estimatedWeightKg, dimWeightKg)`.
Customer response must not expose raw remaining cargo capacity. Trips that cannot accept both estimated volume and estimated weight are filtered out.

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
        "estimatedPriceVnd": 150000,
        "estimatedDepositVnd": 30000
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
  "dropoffStopId": "uuid",
  "bookingId": "uuid",
  "itemName": "Thùng quà",
  "description": "Hàng dễ vỡ",
  "sizeCategory": "MEDIUM",
  "lengthCm": 60,
  "widthCm": 40,
  "heightCm": 35,
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
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "PENDING_PAYMENT",
    "totalAmount": 30000,
    "originalTotalAmount": 30000,
    "discountAmount": 0,
    "voucherCode": null,
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
        "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
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
    "rejectedAt": "2026-05-18T20:15:00+07:00",
    "canUndoUntil": "2026-05-18T20:30:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```


### POST `/v1/parcels/delivery/undo-reject`

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
    "status": "DELIVERED_PENDING_CONFIRM",
    "undoneAt": "2026-05-18T20:20:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```
Decision note: invalid, expired, and revoked delivery tokens return 400 with
`PARCEL_DELIVERY_TOKEN_INVALID`, `PARCEL_DELIVERY_TOKEN_EXPIRED`, or
`PARCEL_DELIVERY_TOKEN_REVOKED`. BSOT `401` and timeline `410` are known drift
items to reconcile.

### POST `/v1/assistant/parcels/{parcelId}/reweigh`

Auth: `ASSISTANT`. Idempotency: required.

Request:
```json
{
  "actualLengthCm": 62,
  "actualWidthCm": 42,
  "actualHeightCm": 36,
  "actualWeightKg": 13.2,
  "actualSizeCategory": "MEDIUM",
  "paymentMethod": "VNPAY"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "PENDING_ADDITIONAL_PAYMENT",
    "actualChargeableWeightKg": 15.62,
    "totalPriceVnd": 180000,
    "additionalAmount": 30000,
    "refundAmount": 0,
    "paymentRedirectUrl": "https://vnpay.vn/..."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Decision notes:
- Capacity is resolved before pricing.
- If actual cargo exceeds trip capacity beyond auto-overflow, status becomes `PENDING_OPERATOR_ACTION` with pending action type `CAPACITY_EXCEEDED`.
- If actual price is lower outside tolerance, status becomes `PENDING_OPERATOR_ACTION` with pending action type `REFUND_CONFIRMATION`.
- If actual price is higher outside tolerance, status becomes `PENDING_ADDITIONAL_PAYMENT`.

### POST `/internal/v1/parcels/{parcelId}/mark-loaded`

Auth: Internal JWT or Driver/Assistant through Driver App facade.

Request:
```json
{
  "tripId": "uuid",
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
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
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
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

### POST `/v1/operator/parcels/{parcelId}/confirm-refund`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF` for parcel's operator. Idempotency: required.

Valid only when parcel status is `PENDING_OPERATOR_ACTION` and pending action type is `REFUND_CONFIRMATION`.

Request:
```json
{
  "reason": "Confirmed actual cargo is smaller than estimated"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "PENDING",
    "tripId": "uuid"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/operator/parcels/{parcelId}/override-capacity`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF` with `CAN_OVERRIDE_CAPACITY`. Idempotency: required.

Valid only when parcel status is `PENDING_OPERATOR_ACTION` and pending action type is `CAPACITY_EXCEEDED` or `RESERVE_FAILED`.

Request:
```json
{
  "reason": "Driver approved loading within manual buffer"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "PENDING",
    "tripId": "uuid"
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

### POST `/internal/v1/payments/batch-charge`

Auth: Internal JWT. Idempotency: required. Caller: Booking round-trip WALLET checkout.

Request:
```json
{
  "userId": "uuid",
  "method": "WALLET",
  "items": [
    { "referenceType": "BOOKING", "referenceId": "uuid", "amount": 350000 },
    { "referenceType": "BOOKING", "referenceId": "uuid", "amount": 350000 }
  ]
}
```

Response `200` (raw internal DTO):
```json
{
  "payments": [
    { "paymentId": "uuid", "referenceType": "BOOKING", "referenceId": "uuid", "status": "SUCCEEDED", "paymentRedirectUrl": null },
    { "paymentId": "uuid", "referenceType": "BOOKING", "referenceId": "uuid", "status": "SUCCEEDED", "paymentRedirectUrl": null }
  ]
}
```

Rules:
- Day-13 batch charge supports `method=WALLET` only and every item must use `referenceType=BOOKING`.
- The operation is atomic in Payment service: if balance is insufficient, an item is invalid, or any payment insert/debit fails, no partial Payment rows and no retained wallet debit are committed.
- On success, Payment service creates one `SUCCEEDED` Payment row per item (`payments.reference_type=BOOKING`, `payments.reference_id=<bookingId>`) and one WALLET debit ledger entry per item (`wallet_transactions.reference_type=BOOKING_PAYMENT`, `wallet_transactions.reference_id=<bookingId>`), all committed in one Payment DB transaction; total wallet balance decrease equals the sum of item amounts.
- Batch idempotency is endpoint-level via `payment:idem:{key}` replay plus duplicate `(referenceType, referenceId)` guard; do not write the same header idempotency key into every `payments.idempotency_key` row because the unique index is per row.
- `BOOKING_GROUP` is not accepted on this WALLET batch endpoint; it remains VNPay-only for round-trip combined redirects.

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

### GET `/v1/rag/documents`

Auth: `SYSTEM_ADMIN`.

Query:
- `page` (optional, default `1`)
- `pageSize` (optional, default `20`, max `100`)
- `sortBy` (optional): `createdAt`, `updatedAt`, `title`, `status`, `ingestStatus`
- `sortDir` (optional): `asc`, `desc`
- `status` (optional): `PENDING_REVIEW`, `APPROVED`, `REJECTED`, `ARCHIVED`
- `ingestStatus` (optional): `PENDING`, `PROCESSING`, `COMPLETED`, `FAILED`
- `accessLevel` (optional): `PUBLIC`, `OPERATOR`, `ADMIN`
- `category` (optional): `CUSTOMER_SUPPORT`, `OPERATOR_POLICY`, `PLATFORM_ADMIN`
- `documentType` (optional): `FAQ`, `POLICY`, `SOP`, `GUIDE`, `TERMS`
- `operatorId` (optional, UUID)
- `q` (optional): search title, file name, or description.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "uuid",
        "title": "Chính sách hủy vé",
        "description": null,
        "storagePath": "documents/file.md",
        "fileName": "policy.md",
        "mimeType": "text/markdown",
        "fileSize": "1024",
        "fileType": "MARKDOWN",
        "accessLevel": "PUBLIC",
        "operatorId": null,
        "category": "CUSTOMER_SUPPORT",
        "documentType": "POLICY",
        "audienceRoles": [],
        "language": "vi",
        "status": "APPROVED",
        "ingestStatus": "COMPLETED",
        "createdAt": "2026-06-01T10:00:00Z",
        "updatedAt": "2026-06-01T10:00:00Z",
        "approvedAt": "2026-06-01T10:00:00Z"
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

Auth: any authenticated role. Retrieval access is filtered by role
and optional tenant scope:

| Role | Access levels | Tenant scope | `audienceRoles` filter |
|------|---------------|-------------|----------------------|
| `PASSENGER` | `PUBLIC` | Global (`operator_id IS NULL`) | Bắt buộc |
| `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`, `OPERATOR_ADMIN` | `PUBLIC`, `OPERATOR` | Own operator (`operatorId` từ JWT) | Bắt buộc |
| `SYSTEM_ADMIN` (global) | `PUBLIC`, `OPERATOR`, `ADMIN` | Global | Bỏ qua |
| `SYSTEM_ADMIN` (operator scope) | `PUBLIC`, `OPERATOR`, `ADMIN` | Selected operator | Bỏ qua |

Request:
```json
{
  "conversationId": "uuid",
  "message": "Quy trình xử lý hàng bị từ chối là gì?",
  "operatorId": "uuid"
}
```

Fields:
- `conversationId` (optional, UUID): reuse existing conversation.
- `message` (required, string, 1-4000 chars): câu hỏi.
- `operatorId` (optional, UUID, **SYSTEM_ADMIN only**): scope retrieval to a specific operator's documents.
  Non-admin gửi `operatorId` trả 403. Khi reuse conversation đã có scope, không cần gửi lại;
  gửi `operatorId` khác scope cũ trả 403.

Response: Server-Sent Events stream with assistant tokens and final cited chunk IDs.

Error codes:
- `RAG_OPERATOR_SCOPE_FORBIDDEN` (403): non-admin gửi `operatorId`.
- `RAG_CONVERSATION_SCOPE_MISMATCH` (403): đổi operator scope giữa các turn.

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

### GET `/v1/operator/users`

Auth: `OPERATOR_ADMIN`. Tenant isolation: caller `operatorId` is used as the only operator scope. `OPERATOR_STAFF` is not allowed to list employees. Idempotency-Key: not required (read endpoint).

Query parameters:
- `page` — optional, default `1`, minimum `1`.
- `pageSize` — optional, default `20`, range `1..100`.
- `search` — optional, searches `email`, `displayName`, and normalized exact `phone` when the value is a valid phone number.
- `sortBy` — optional, default `createdAt`; allowed: `createdAt`, `email`, `displayName`, `role`, `status`.
- `sortDir` — optional, default `desc`; allowed: `asc`, `desc`.
- `role` — optional; allowed: `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`.
- `status` — optional; any valid `UserStatus` value.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "userId": "uuid",
        "email": "driver@example.com",
        "phone": "+84901112222",
        "displayName": "Driver One",
        "role": "DRIVER",
        "status": "PENDING_INITIAL_PASSWORD",
        "operatorId": "uuid",
        "createdAt": "2026-06-01T10:00:00Z",
        "avatarUrl": null
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

Errors:
- `403 FORBIDDEN` — caller is not `OPERATOR_ADMIN` or has no `operatorId`.
- `400 INVALID_SORT_FIELD` — `sortBy` is not in the allow-list.
- `422 VALIDATION_ERROR` — invalid paging/filter value.

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

### GET `/internal/v1/users/{userId}`

Auth: Internal JWT via `X-Internal-Auth`. Not exposed through Gateway. Success response is raw DTO (no `ApiResponse` wrapper); errors use the standard ADR 0004 error envelope.

Purpose: service-to-service logical-FK and role/operator validation, including Trip DriverSchedule create/activation validation.

Response `200`:
```json
{
  "id": "uuid",
  "role": "DRIVER",
  "operatorId": "uuid",
  "status": "ACTIVE"
}
```

`operatorId` is nullable for non-operator-scoped users; Trip DriverSchedule validation requires it to match the caller operator for `DRIVER` and `ASSISTANT` users.

Error `404` — `RESOURCE_NOT_FOUND`.

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
  "taxCode": "0312345678",
  "cancellationPolicy": [
    { "hoursBeforeDeparture": 24, "feePercent": 10 }
  ]
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

Auth: public.

Purpose: passenger/FE station autocomplete. Mutation endpoints remain operator/admin-only.

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
  "locationId": "uuid",
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
  "locationId": "uuid",
  "locationCode": "HCM",
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
  "locationId": "uuid",
  "locationCode": "HCM",
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

Route create/update money fields are VND BIGINT-compatible JSON numbers. Persisted values follow the shared Money rule (BSOT v1.11.0): kept to the đồng — no rounding to thousands; fractional computation results round to the nearest đồng.

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
  "pathPolyline": "encoded-google-polyline-precision-5",
  "isActive": true,
  "createdAt": "2026-06-10T10:00:00Z",
  "updatedAt": "2026-06-10T10:00:00Z"
}
```

`returnRouteId` is nullable and one-way: setting Route A `returnRouteId = B` does not mutate Route B.

`pathPolyline` is nullable and appears on Route detail/mutation responses only. `GET /v1/operator/routes` returns `PagedResult<RouteListItemDto>` with the same fields except `pathPolyline`, preventing a large geometry string per list item.

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

### PUT `/v1/operator/routes/{id}/geometry`

Auth: `OPERATOR_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request: `{ "pathPolyline": "<Google encoded polyline precision-5>" }`; send `{ "pathPolyline": null }` to clear it.

Validation order: UTF-8 size at most 100 KiB; valid Google precision-5 decode; 2–10,000 decoded points; latitude/longitude ranges; every RouteStop and every origin/destination Station that has coordinates must be within 500 m of the polyline. Mismatch returns `422 ROUTE_GEOMETRY_STOP_MISMATCH`; `error.fields.stopIds` and/or `error.fields.stationIds` contain comma-separated UUIDs. Invalid encoding/range/count returns `422 ROUTE_GEOMETRY_INVALID`; oversize returns `422 ROUTE_GEOMETRY_TOO_LARGE`. Missing/cross-operator Route returns `404 ROUTE_NOT_FOUND`.

Response `200`: updated `RouteDto` including `pathPolyline`.

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

`fareFromThisStop` is an exception override for Route base fare. It is VND BIGINT-compatible and is persisted to the đồng (no flooring to 1000 — BSOT v1.11.0). Stops without a fare-template entry use `Route.baseFare`.

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
  "pathPolyline": "encoded-google-polyline-precision-5",
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

`pathPolyline` is nullable and appears on create/update/geometry responses only. The paged alternative-route list uses `AlternativeRouteListItemDto` without `pathPolyline`.

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

### PUT `/v1/operator/alternative-routes/{altId}/geometry`

Auth: `OPERATOR_ADMIN`. Idempotency-Key: not required by BSOT §5.6.

Request and base validation match Route geometry. Waypoint matching checks AlternativeRoute stops, the parent Route origin Station, and the AlternativeRoute destination Station. Mismatch fields use comma-separated `stopIds` and/or `stationIds`. Missing/cross-operator AlternativeRoute returns `404 ROUTE_NOT_FOUND`.

Response `200`: updated `AlternativeRouteDto` including `pathPolyline`.

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

## Trip Vehicle and Driver Schedule Management (Day 9)

### Role matrix and shared rules

| Method | Role(s) |
|---|---|
| `POST`, `PATCH` | `OPERATOR_ADMIN` only |
| `GET` list/by-id | `OPERATOR_ADMIN`, `OPERATOR_STAFF` |

All public responses use the ADR 0004 `ApiResponse<T>` envelope. Success responses include `{ success, statusCode, data, meta }`; errors include `{ success: false, statusCode, error: { code, message, fields? }, meta }`.

Write endpoints in this Day-9 section do not require `Idempotency-Key` per BSOT §5.6.

Vehicle and DriverSchedule writes require the caller operator to be `APPROVED` and active. A non-APPROVED or inactive operator receives `403 FORBIDDEN`.

Vehicle tenant isolation: a missing Vehicle, a soft-deleted Vehicle, or a Vehicle not owned by the caller's operator returns `404 VEHICLE_NOT_FOUND`.

### VehicleType DTO

`VehicleTypeDto` shape:
```json
{
  "id": "uuid",
  "code": "LIMOUSINE",
  "displayName": "Limousine",
  "estimatedPassengerLuggageKgPerSeat": 15,
  "defaultSeatCount": 9,
  "isSystemDefined": true,
  "isActive": true,
  "createdAt": "2026-06-11T10:00:00Z",
  "updatedAt": "2026-06-11T10:00:00Z"
}
```

The catalog contains the three platform-seeded system types:

| `code` | `defaultSeatCount` |
|---|---:|
| `STANDARD_BUS` | 45 |
| `LIMOUSINE` | 9 |
| `SLEEPER_BUS` | 40 |

`isSystemDefined=true` blocks deletion in the application layer. Day 9 exposes the catalog as read-only; it does not expose a VehicleType delete endpoint.

### GET `/v1/vehicle-types`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`, `search?`, `searchIn?`, `sortBy?`, `sortDir?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Search and sort follow BSOT §5.8; allowed search fields are `code` and `displayName`.

Response `200`: `PagedResult<VehicleTypeDto>` in the ADR 0004 success envelope.

### SeatLayoutJson contract

`Vehicle.seatLayoutJson` has this exact shared BE/FE structure:
```json
{
  "version": 1,
  "vehicleTypeCode": "LIMOUSINE",
  "totalSeats": 2,
  "rows": 1,
  "cols": 2,
  "decks": 1,
  "aisles": [
    { "afterCol": 1 }
  ],
  "seats": [
    {
      "seatNumber": "A01",
      "row": 1,
      "col": 1,
      "deck": 1,
      "type": "VIP",
      "isWindow": true,
      "isAisle": false,
      "disabled": false
    },
    {
      "seatNumber": "A02",
      "row": 1,
      "col": 2,
      "deck": 1,
      "type": "DRIVER_AREA",
      "isWindow": false,
      "isAisle": true,
      "disabled": true
    }
  ]
}
```

Field rules:
- `version` is an integer.
- `vehicleTypeCode` is a string.
- `totalSeats`, `rows`, `cols`, and `decks` are integers. `decks=1` is a normal vehicle and `decks=2` is a sleeper vehicle.
- `aisles` is an array whose entries contain integer `afterCol`.
- `seats` is an array whose entries contain string `seatNumber`, 1-indexed integer `row` and `col`, integer `deck`, enum `type`, booleans `isWindow`, `isAisle`, and `disabled`.
- `type` is exactly one of `STANDARD`, `SLEEPER_LOWER`, `SLEEPER_UPPER`, `VIP`, `DRIVER_AREA`.
- `seatNumber` is a string and identifies the seat used by TripSeat.

The complete v1 semantic seat-layout validation scope is limited to:
1. `Vehicle.totalSeats == seatLayoutJson.totalSeats == seatLayoutJson.seats.length`.
2. Every `seatLayoutJson.seats[].seatNumber` is unique within the Vehicle.

Either failure returns `422 VALIDATION_ERROR` with `error.fields` identifying `totalSeats` or `seatLayoutJson.seats[].seatNumber`. No additional row, column, deck, aisle, or seat-geometry rule is enforced in v1.

### Vehicle DTO

`VehicleDto` shape:
```json
{
  "id": "uuid",
  "operatorId": "uuid",
  "vehicleTypeId": "uuid",
  "licensePlate": "51B-12345",
  "seatLayoutJson": {
    "version": 1,
    "vehicleTypeCode": "LIMOUSINE",
    "totalSeats": 2,
    "rows": 1,
    "cols": 2,
    "decks": 1,
    "aisles": [{ "afterCol": 1 }],
    "seats": [
      {
        "seatNumber": "A01",
        "row": 1,
        "col": 1,
        "deck": 1,
        "type": "VIP",
        "isWindow": true,
        "isAisle": false,
        "disabled": false
      },
      {
        "seatNumber": "A02",
        "row": 1,
        "col": 2,
        "deck": 1,
        "type": "DRIVER_AREA",
        "isWindow": false,
        "isAisle": true,
        "disabled": true
      }
    ]
  },
  "totalSeats": 2,
  "maxCargoWeightKg": 500.00,
  "maxCargoVolumeM3": 8.50,
  "status": "ACTIVE",
  "isActive": true,
  "createdAt": "2026-06-11T10:00:00Z",
  "updatedAt": "2026-06-11T10:00:00Z"
}
```

### POST `/v1/operator/vehicles`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "vehicleTypeId": "uuid",
  "licensePlate": "51B-12345",
  "seatLayoutJson": {
    "version": 1,
    "vehicleTypeCode": "LIMOUSINE",
    "totalSeats": 2,
    "rows": 1,
    "cols": 2,
    "decks": 1,
    "aisles": [{ "afterCol": 1 }],
    "seats": [
      {
        "seatNumber": "A01",
        "row": 1,
        "col": 1,
        "deck": 1,
        "type": "VIP",
        "isWindow": true,
        "isAisle": false,
        "disabled": false
      },
      {
        "seatNumber": "A02",
        "row": 1,
        "col": 2,
        "deck": 1,
        "type": "DRIVER_AREA",
        "isWindow": false,
        "isAisle": true,
        "disabled": true
      }
    ]
  },
  "totalSeats": 2,
  "maxCargoWeightKg": 500.00,
  "maxCargoVolumeM3": 8.50
}
```

Validation:
- Missing or inactive `vehicleTypeId` returns `404 VEHICLE_TYPE_NOT_FOUND`.
- Seat-layout validation is exactly the two rules in the SeatLayoutJson contract above. A failure returns `422 VALIDATION_ERROR` with `error.fields`.
- `licensePlate` must be unique across Vehicles whose `deletedAt` is null. A conflict returns `422 VALIDATION_ERROR` with `error.fields.licensePlate`; a plate from a soft-deleted Vehicle does not conflict.
- Negative `maxCargoWeightKg` returns `422 VALIDATION_ERROR` with `error.fields.maxCargoWeightKg`.

Response `201`: `VehicleDto` in the ADR 0004 success envelope.

### GET `/v1/operator/vehicles`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`, `search?`, `searchIn?`, `sortBy?`, `sortDir?`.

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Search and sort follow BSOT §5.8; the allowed search field is `licensePlate`. Only non-soft-deleted Vehicles owned by the caller's operator are returned.

Response `200`: `PagedResult<VehicleDto>` in the ADR 0004 success envelope.

### GET `/v1/operator/vehicles/{id}`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Tenant isolation: missing, soft-deleted, or cross-operator Vehicle returns `404 VEHICLE_NOT_FOUND`.

Response `200`: `VehicleDto` in the ADR 0004 success envelope.

### PATCH `/v1/operator/vehicles/{id}`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request: partial Vehicle update using the mutable fields from the create request, plus `status` and `isActive`.
```json
{
  "vehicleTypeId": "uuid",
  "licensePlate": "51B-54321",
  "totalSeats": 2,
  "seatLayoutJson": {
    "version": 1,
    "vehicleTypeCode": "LIMOUSINE",
    "totalSeats": 2,
    "rows": 1,
    "cols": 2,
    "decks": 1,
    "aisles": [{ "afterCol": 1 }],
    "seats": [
      {
        "seatNumber": "A01",
        "row": 1,
        "col": 1,
        "deck": 1,
        "type": "VIP",
        "isWindow": true,
        "isAisle": false,
        "disabled": false
      },
      {
        "seatNumber": "A02",
        "row": 1,
        "col": 2,
        "deck": 1,
        "type": "DRIVER_AREA",
        "isWindow": false,
        "isAisle": true,
        "disabled": true
      }
    ]
  },
  "maxCargoWeightKg": 500.00,
  "maxCargoVolumeM3": 8.50,
  "status": "ACTIVE",
  "isActive": true
}
```

Validation mirrors Vehicle create for supplied fields. Missing or inactive `vehicleTypeId` returns `404 VEHICLE_TYPE_NOT_FOUND`; missing, soft-deleted, or cross-operator Vehicle returns `404 VEHICLE_NOT_FOUND`.

For every partial update, validation runs against the effective merged state: the persisted Vehicle values combined with all supplied PATCH fields. The merged state must still satisfy `Vehicle.totalSeats == seatLayoutJson.totalSeats == seatLayoutJson.seats.length` and unique `seatLayoutJson.seats[].seatNumber`, so changing only `totalSeats` or only `seatLayoutJson` cannot leave an invalid Vehicle.

Response `200`: updated `VehicleDto` in the ADR 0004 success envelope.

### DriverSchedule DTO

`DriverScheduleDto` shape:
```json
{
  "id": "uuid",
  "operatorId": "uuid",
  "routeId": "uuid",
  "vehicleId": "uuid",
  "driverUserId": "uuid",
  "assistantUserId": "uuid",
  "dayOfWeek": [1, 3, 5],
  "departureTime": "08:00:00",
  "validFrom": "2026-07-01",
  "validUntil": "2026-12-31",
  "isActive": true,
  "createdAt": "2026-06-11T10:00:00Z",
  "updatedAt": "2026-06-11T10:00:00Z"
}
```

`vehicleId`, `assistantUserId`, and `validUntil` are nullable. `dayOfWeek` is a JSON array using `1=Monday`, `2=Tuesday`, ..., `7=Sunday`. `departureTime` is a timezone-free `TIME` value with local ICT semantics.

### POST `/v1/operator/driver-schedules`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Request:
```json
{
  "routeId": "uuid",
  "vehicleId": "uuid",
  "driverUserId": "uuid",
  "assistantUserId": "uuid",
  "dayOfWeek": [1, 3, 5],
  "departureTime": "08:00:00",
  "validFrom": "2026-07-01",
  "validUntil": "2026-12-31",
  "isActive": true
}
```

Validation:
- `dayOfWeek` must be a non-empty JSON array containing only integers from `1` through `7`. An empty array, a non-integer entry, or an entry outside `1..7` returns `422 VALIDATION_ERROR` with `error.fields.dayOfWeek`.
- `validUntil`, when present, must be on or after `validFrom`; otherwise return `422 VALIDATION_ERROR` with `error.fields.validUntil`.
- `routeId` must resolve to an active Route owned by the caller's operator. A missing, inactive, or cross-operator Route returns `404 ROUTE_NOT_FOUND`.
- `vehicleId`, when present, must resolve to a non-soft-deleted Vehicle owned by the caller's operator; otherwise return `404 VEHICLE_NOT_FOUND`.
- `driverUserId` must resolve through Identity `GET /internal/v1/users/{userId}` to a user with `role=DRIVER` under the caller operator. Missing Identity user, wrong role, wrong operator, or upstream logical-FK validation failure returns `422 VALIDATION_ERROR` with `error.fields.driverUserId`.
- `assistantUserId`, when present, must resolve through Identity `GET /internal/v1/users/{userId}` to a user with `role=ASSISTANT` under the caller operator. Missing Identity user, wrong role, wrong operator, or upstream logical-FK validation failure returns `422 VALIDATION_ERROR` with `error.fields.assistantUserId`.
- An active schedule conflicts when the same `driverUserId` has any intersecting `dayOfWeek`, the same local-ICT `departureTime`, and an overlapping `[validFrom, validUntil]` window. Return `409 TRIP_DRIVER_CONFLICT`.

Response `201`: `DriverScheduleDto` in the ADR 0004 success envelope.

Creating a DriverSchedule persists the recurring assignment and, when active, is the Day-11 trigger for Trip generation enqueue after the schedule commit succeeds. Day 9 shipped persistence only; the Day-11 contract closes the deferred driver/assistant role+operator validation carryover.

### PATCH `/v1/operator/driver-schedules/{id}/activate`

Auth: `OPERATOR_ADMIN`.

Request body: none.

Idempotency-Key: not required by BSOT §5.6. The endpoint is behavior-idempotent: if the schedule is already active, return the current `DriverScheduleDto` without a duplicate Trip-generation enqueue.

Gateway impact: no new Gateway route is required; the existing `/v1/operator/driver-schedules` prefix covers this action.

Scope: activation only. Full DriverSchedule edit/cascade (`departureTime`, `dayOfWeek`, `driverUserId`, `assistantUserId`, `vehicleId`, `validUntil`, FUTURE_ONLY/ALL_PENDING) remains out of scope for Day 11.

Validation:
- The target DriverSchedule must exist and belong to the caller operator. Missing or cross-operator schedules return `404 RESOURCE_NOT_FOUND`.
- Caller operator write eligibility still applies. A non-`APPROVED` or inactive operator receives `403 FORBIDDEN`.
- Before activation, validate the same driver/assistant role+operator rules as create: `driverUserId` must be `role=DRIVER` under the caller operator, and nullable `assistantUserId`, when present, must be `role=ASSISTANT` under the caller operator. Mismatch or Identity logical-FK validation failure returns `422 VALIDATION_ERROR` with `error.fields`.
- Activation reuses active-schedule conflict checks. Enabling a conflicting schedule returns `409 TRIP_DRIVER_CONFLICT` and does not enqueue Trip generation.

Response `200`: `DriverScheduleDto` in the ADR 0004 success envelope.

On success, activation may only transition `isActive=false` to `isActive=true`; Trip generation is enqueued only after the activation commit succeeds.

### GET `/v1/operator/driver-schedules`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `routeId?`, `driverUserId?`, `isActive?`. Response is a paged schedule list. Each item retains the existing schedule IDs and fields, and adds `route` (including `originStation`/`destinationStation`), nullable `vehicle` (including `imageUrls`), and nullable `driver`/`assistant` summaries `{ id, displayName, avatarUrl, role, operatorId, status }`.

### Read-model additions

- `GET /v1/stations/{id}` is public and returns the full active `StationDto`; missing/inactive returns `404 STATION_NOT_FOUND`.
- `GET /v1/operator/stations` is paged/searchable for operator staff/admin and returns mapping fields plus the full canonical `station` object.
- Route list/detail responses include full `originStation` and `destinationStation` while preserving the original station ID fields.
- `VehicleDto` has nullable `imageUrls`. Create/PATCH accept at most five unique absolute HTTPS URLs; PATCH `[]` clears the list.
- Internal `GET /internal/v1/users?ids=<guid>&ids=<guid>` accepts 1–100 IDs and returns user summaries with `displayName` and `avatarUrl` for service-to-service schedule enrichment.

### Day-9/Day-11 error examples

Seat-layout count failure:
```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Seat layout validation failed.",
    "fields": [
      { "field": "totalSeats", "message": "totalSeats must equal seatLayoutJson.totalSeats and seats.length." }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-11T10:00:00Z" }
}
```

Driver schedule conflict:
```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "TRIP_DRIVER_CONFLICT",
    "message": "The driver already has an active schedule at this weekly time slot."
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-11T10:00:00Z" }
}
```

## Day 18 — Driver operational schedule

### GET `/v1/driver/me/schedule?from={yyyy-MM-dd}&to={yyyy-MM-dd}`

Auth: `DRIVER` or `ASSISTANT`.

Returns only Trips assigned to the authenticated JWT `sub`, where the caller is either the
Trip's `driverUserId` or `assistantUserId`. A caller cannot supply or override a user identifier.

`from` and `to` are ICT (`UTC+7`) calendar dates and are inclusive at both ends. Both parameters
must be supplied together or omitted together. When both are omitted, the range defaults to the
current ICT date through current ICT date plus 14 days. Supplying exactly one parameter, or a
`to` date before `from`, returns `422 VALIDATION_ERROR`.

Response `200`: `GetMyDriverScheduleResult` in the ADR 0004 success envelope.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "from": "2026-06-30",
    "to": "2026-07-14",
    "trips": [
      {
        "tripId": "uuid",
        "operatorId": "uuid",
        "routeId": "uuid",
        "vehicleId": "uuid",
        "departureDateTime": "2026-06-30T01:00:00Z",
        "estimatedArrivalTime": "2026-06-30T04:00:00Z",
        "status": "SCHEDULED",
        "assignmentRole": "DRIVER"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T03:00:00Z"
  }
}
```

Trips are ordered by `departureDateTime`, then by `tripId`. Date filtering converts the inclusive
ICT date range to UTC boundaries before querying. No Trip state is mutated.

### GET `/v1/bookings/trips/{tripId}/manifest`

Auth: `DRIVER` or `ASSISTANT`. The authenticated JWT `sub` must equal the Trip snapshot's
`driverUserId` or `assistantUserId`; otherwise the endpoint returns `403 FORBIDDEN`.

Returns only confirmed Booking passenger records and exposes no passenger or buyer PII. Items are
ordered by the Trip snapshot stop `orderIndex`. A terminal pickup (`pickupStationId` set and
`pickupStopId` null) is treated as the origin with `orderIndex = 0` and sorts first.

Response `200` in the ADR 0004 success envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "passengerRecordId": "uuid",
        "ticketId": "uuid",
        "ticketCode": "VT-20260630-ABCDEFGH",
        "seatNumber": "A01",
        "bookingCode": "VR-20260630-ABCD1234",
        "pickupStop": "uuid-or-null",
        "boardingStatus": "PENDING"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T03:00:00Z"
  }
}
```

Each manifest item contains `passengerRecordId`, `ticketId`, `ticketCode`, `seatNumber`,
`bookingCode`, `pickupStop`, and `boardingStatus`, and only includes tickets in `ISSUED` or
`USED` status. A trip with no confirmed active/used tickets returns `200` with `items: []`, not `404`.
Unknown trip returns `404 TRIP_NOT_FOUND`; validation failures return `422 VALIDATION_ERROR`.

### POST `/v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}`

Auth: `DRIVER` or `ASSISTANT`. The authenticated JWT `sub` must equal the Trip snapshot's
`driverUserId` or `assistantUserId`; otherwise the endpoint returns `403 FORBIDDEN`.

Marks the selected passenger record `BOARDED`. The mutation is performed through the Booking
aggregate, sets `boardedAt` to the current UTC instant, and leaves `boardedAtStopId` null when no
physical boarding stop is supplied. The request has no body and requires `Idempotency-Key`.

Response `200` in the ADR 0004 success envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "passengerRecordId": "uuid",
    "boardingStatus": "BOARDED",
    "boardedAt": "2026-06-30T03:00:00Z",
    "boardedAtStopId": null,
    "ticketId": "uuid",
    "ticketCode": "VT-20260630-ABCDEFGH",
    "ticketStatus": "USED"
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T03:00:00Z"
  }
}
```

Error responses use the ADR 0004 envelope:

- `403 FORBIDDEN`: caller is not the trip's assigned driver or assistant.
- `404 BOOKING_NOT_FOUND`: `passengerRecordId` does not exist.
- `404 TICKET_NOT_FOUND`: the passenger record has no linked ticket.
- `409 BOOKING_PASSENGER_ALREADY_BOARDED`: passenger is already `BOARDED`.
- `409 TICKET_NOT_BOARDABLE`: linked ticket is not `ISSUED`.
- `422 BOOKING_NOT_FOR_THIS_TRIP`: passenger exists but belongs to another trip.
- `422 VALIDATION_ERROR`: route parameters are invalid.

### POST `/v1/bookings/trips/{tripId}/boarding/qr-scan`

Auth: `DRIVER` or `ASSISTANT`. The authenticated JWT `sub` must equal the Trip snapshot's
`driverUserId` or `assistantUserId`; otherwise the endpoint returns `403 FORBIDDEN`.

The request body contains the plain ticket code decoded by the Driver App. The service does not
decode or persist QR image data and does not mutate Booking, Ticket, or Passenger state.

```json
{
  "ticketCode": "VT-20260630-ABCDEFGH"
}
```

`ticketCode` must match `^VT-\d{8}-[0-9A-HJ-NP-TV-Z]{8}$`. Legacy clients may send exactly one
`bookingCode` (`^VR-\d{8}-[A-Z2-7]{8}$`) instead; new clients must use `ticketCode`.

Response `200` in the ADR 0004 success envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "passengerRecordId": "uuid",
        "ticketId": "uuid",
        "ticketCode": "VT-20260630-ABCDEFGH",
        "seatNumber": "A01",
        "boardingStatus": "PENDING"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T03:00:00Z"
  }
}
```

With `ticketCode`, the response contains exactly one passenger item. Legacy `bookingCode` may
return multiple issued/used ticket items for the booking. The scan is read-only; ticking a
passenger uses the separate boarding-passenger endpoint.

Error responses use the ADR 0004 envelope:

- `403 FORBIDDEN`: caller is not the trip's assigned driver or assistant.
- `404 BOOKING_NOT_FOUND`: the code is unknown, the booking is not `CONFIRMED`, or the ticket is not `ISSUED`/`USED`.
- `422 BOOKING_NOT_FOR_THIS_TRIP`: the code belongs to a different trip.
- `422 VALIDATION_ERROR`: the route parameter or booking-code format is invalid.

## Integration Event Contracts

### `trip.stop.departed_with_pending`

Producer: Trip. Consumer: Notification (Driver App boarding warning). Exchange:
`vietride.events`.

Payload:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-06-30T03:00:00Z",
  "eventType": "trip.stop.departed_with_pending",
  "tripId": "uuid",
  "stopId": "uuid",
  "stopName": "Ben xe Mien Dong Moi",
  "pendingPassengerCount": 2,
  "driverUserId": "uuid",
  "assistantUserId": null,
  "departedAt": "2026-06-30T03:00:00Z"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `eventId` | `Guid` | yes | Stable event identity for consumer deduplication. |
| `occurredAt` | `DateTime` | yes | UTC integration-event occurrence timestamp, matching `IntegrationEventBase` serialization. |
| `eventType` | `string` | yes | Constant `trip.stop.departed_with_pending`; also used as the AMQP routing key. |
| `tripId` | `Guid` | yes | Trip whose stop was departed. |
| `stopId` | `Guid` | yes | Departed stop. |
| `stopName` | `string` | yes | Snapshot used in the Driver App warning. |
| `pendingPassengerCount` | `int` | yes | Positive integer (`> 0`): passengers still `PENDING` at the stop. |
| `driverUserId` | `Guid` | yes | Assigned driver notification target. |
| `assistantUserId` | `Guid?` | yes, nullable | Assigned assistant notification target when present. |
| `departedAt` | `DateTimeOffset` | yes | Stop-departure timestamp serialized as UTC ISO-8601. |

The payload contains exactly the fields above. Day 18 freezes the contract and registry entry
only. The Trip Outbox emitter, handler wiring, emit-condition tests, and Day-24 `NO_SHOW`
detection remain deferred to Day 24.
