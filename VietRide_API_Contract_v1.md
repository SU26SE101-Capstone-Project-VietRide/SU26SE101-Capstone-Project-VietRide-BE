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

### PATCH `/v1/users/me/avatar`

Auth: User Access Token (RS256). Idempotency-Key: required.

The client first uploads the image using the Firebase custom token flow, then sends the
Firebase Storage download URL here. Identity accepts only an HTTPS URL in the configured
Firebase bucket under `avatars/{callerUserId}/`; the URL is never accepted for another user
or an unrelated path.

Request:
```json
{
  "avatarUrl": "https://firebasestorage.googleapis.com/v0/b/vietride.firebasestorage.app/o/avatars%2Fuser-id%2Favatar.webp?alt=media"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": { "avatarUrl": "https://firebasestorage.googleapis.com/..." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-23T10:00:00Z" }
}
```

Errors: `401 AUTH_TOKEN_INVALID`, `404 USER_NOT_FOUND`, and `422 VALIDATION_ERROR` for a
missing, malformed, or caller-unowned Firebase URL.

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

### POST `/v1/firebase/custom-token`

Auth: User Access Token (RS256). Body is optional for backward compatibility; an empty body
defaults to `VEHICLE_IMAGE`. Idempotency-Key: not required.

Optional request:
```json
{ "purpose": "VEHICLE_IMAGE" }
```

Allowed purposes and claims:

| Purpose | Allowed role(s) | Upload prefix |
|---|---|---|
| `VEHICLE_IMAGE` | `OPERATOR_ADMIN` | `vehicles/{operatorId}/` |
| `OPERATOR_LOGO` | `OPERATOR_ADMIN` | `operators/{operatorId}/logo/` |
| `PARCEL_PHOTO` | `PASSENGER` | `parcels/{userId}/` |
| `INCIDENT_PHOTO` | `DRIVER`, `ASSISTANT` | `incidents/{operatorId}/{userId}/` |
| `USER_AVATAR` | any active user | `avatars/{userId}/` |

Identity revalidates the persisted caller before minting a token. Operator-scoped users must
still belong to an active, `APPROVED` Operator. Firebase UID is the VietRide `userId`; claims
include `role`, `uploadPurpose`, and `operatorId` when applicable.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "token": "firebase-custom-token",
    "purpose": "VEHICLE_IMAGE",
    "uploadPath": "vehicles/operator-id/"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-20T10:00:00Z" }
}
```

Errors: `401 UNAUTHORIZED` when authentication is absent, `403 FORBIDDEN` when the persisted
user/operator state is no longer eligible, and `502 UPSTREAM_UNAVAILABLE` when Firebase Auth
cannot mint the token. Token values and Firebase credentials must never be logged.

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

### GET `/v1/admin/users`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required.

Query parameters:

| Parameter | Type | Default | Validation / semantics |
|---|---|---|---|
| `search` | string? | null | Case-insensitive contains over email, display name and phone. |
| `role` | UserRole? | null | Exact role filter. |
| `status` | UserStatus? | null | Exact status filter. `DELETED` with `includeDeleted=false` returns an empty page. |
| `operatorId` | UUID? | null | Exact logical operator filter. |
| `includeDeleted` | boolean | `false` | `true` includes soft-deleted users; otherwise the global query filter remains active. |
| `page` | integer | `1` | Minimum 1. |
| `pageSize` | integer | `20` | Range 1..100. |
| `sortBy` | string | `createdAt` | `createdAt,email,displayName,role,status`. |
| `sortDir` | string | `desc` | `asc` or `desc`; `id` is the deterministic tie-breaker. |

Response `200`: `PagedResult<AdminUserListItemDto>` in the ADR 0004 envelope.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [{
      "id": "uuid",
      "email": "passenger@example.com",
      "displayName": "Nguyen Van A",
      "phone": "+84900000000",
      "avatarUrl": null,
      "role": "PASSENGER",
      "status": "ACTIVE",
      "operatorId": null,
      "createdAt": "2026-07-16T01:00:00Z",
      "updatedAt": "2026-07-16T01:00:00Z",
      "deletedAt": null
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T01:00:00Z" }
}
```

The DTO never exposes password hashes, OAuth subjects, refresh tokens, OTP state or failed-login
internals. Errors: `403 FORBIDDEN`, `400 INVALID_SORT_FIELD`, `422 VALIDATION_ERROR`.

### POST `/v1/admin/users/{userId}/lock`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key` is required. Request has no body. Shared idempotency uses
`AllowRequestBody=false`. The caller cannot lock itself (`403 FORBIDDEN`).

Manual lock permits `ACTIVE -> LOCKED` and records `lockedFromStatus=ACTIVE`. A target already
`LOCKED` returns ensure-locked success with `statusChanged=false`, preserves the origin, revokes any
remaining active refresh token with `ADMIN_REVOKE`, and audits this logical request. Other status
transitions return `422 USER_INVALID_STATUS_TRANSITION`.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": { "userId": "uuid", "status": "LOCKED", "statusChanged": true },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T01:00:00Z" }
}
```

### POST `/v1/admin/users/{userId}/unlock`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key` is required. Request has no body. The caller cannot unlock
itself (`403 FORBIDDEN`). The target must be `LOCKED` with a valid `lockedFromStatus` of `ACTIVE` or
`PENDING_EMAIL_VERIFICATION`. Unlock restores exactly that status, resets DB and Redis login-lockout
state, clears `lockedFromStatus`, and never restores revoked refresh tokens. Missing/invalid origin
is an invariant failure, not an implicit promotion to `ACTIVE`.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "status": "PENDING_EMAIL_VERIFICATION",
    "statusChanged": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T01:00:00Z" }
}
```

Both lock/unlock return only these domain errors in addition to auth/validation envelopes:
`RESOURCE_NOT_FOUND`, `FORBIDDEN`, `USER_INVALID_STATUS_TRANSITION`,
`IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`, `IDEMPOTENCY_REQUEST_PENDING`.

### GET `/v1/admin/activity-logs`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: not required.

Query: `userId?` (actor), `action?`, `from?`, `to?`, `page=1`, `pageSize=20`. Date boundaries are
RFC 3339 UTC and use `[from,to)`; when both are supplied `from < to`. Ordering is always
`createdAt DESC,id DESC`.

Response `200`: `PagedResult<AdminActivityLogItemDto>` in the ADR 0004 envelope.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [{
      "id": "uuid",
      "actor": {
        "id": "uuid",
        "email": "admin@vietride.vn",
        "displayName": "System Administrator",
        "role": "SYSTEM_ADMIN"
      },
      "action": "LOCK_USER",
      "metadata": {
        "targetUserId": "uuid",
        "previousStatus": "ACTIVE",
        "newStatus": "LOCKED",
        "statusChanged": true
      },
      "ipAddress": "203.0.113.10",
      "userAgent": "VietRide Admin Web",
      "createdAt": "2026-07-16T01:00:00Z"
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T01:00:00Z" }
}
```

ActivityLog is append-only. No update/delete API exists and the database rejects direct
`UPDATE`/`DELETE`. Metadata must not contain password, OTP or token material.

### POST `/internal/v1/operators/summaries/batch`

Auth: Internal JWT only. This is a read-only POST and does not require Idempotency-Key.

Request:

```json
{ "operatorIds": ["uuid"] }
```

At most 500 distinct non-empty UUIDs are accepted. Empty input returns an empty raw array. Requested
soft-deleted operators are included. Response items are sorted by ID ascending.

Response `200` (raw internal success payload):

```json
[{ "operatorId": "uuid", "operatorName": "Nha xe A" }]
```

## Booking Service

### GET `/v1/bookings/{bookingId}`

Auth: the booking owner (`PASSENGER`) or an authorized `OPERATOR_ADMIN`/`OPERATOR_STAFF` whose authenticated `operatorId` claim matches the booking tenant. Idempotency is not required (read-only). This is the Booking-owned poll resource for payment confirmation; it does not synchronously query Payment Service and deliberately exposes no payment fields. Operator detail remains the separate `GET /v1/operator/bookings/{id}` resource.

Response `200`: ADR 0004 success envelope whose `data` contains exactly:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingId": "uuid",
    "status": "PENDING_PAYMENT"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-13T01:00:00Z" }
}
```

`status` is the canonical Booking lifecycle value, so the client polls until the payment event transitions `PENDING_PAYMENT` to `CONFIRMED`.

Errors use the ADR 0004 envelope:

- `401 UNAUTHORIZED`: caller has no valid user JWT.
- `404 BOOKING_NOT_FOUND`: booking does not exist or does not belong to the authenticated passenger; ownership is intentionally not disclosed.
- `403 FORBIDDEN`: an operator caller is not authorized for the booking tenant or does not have a valid `operatorId` claim.

### GET `/v1/operator/bookings`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`. The tenant key is the non-null `operatorId` claim from the authenticated JWT; the endpoint never accepts an operator id from the client. Idempotency: not required (read-only).

Query parameters:

| Parameter | Type | Default | Validation and semantics |
|---|---|---|---|
| `status` | string? | null | One `booking_status` value or a comma-separated list. Empty entries or unknown values return `422 VALIDATION_ERROR`. |
| `tripId` | UUID? | null | Exact trip id; malformed UUID returns `422 VALIDATION_ERROR`. |
| `date` | `YYYY-MM-DD`? | null | Calendar day in `Asia/Ho_Chi_Minh` (ICT). Convert local midnight and the next local midnight to the UTC half-open interval `[fromUtc, toUtc)` and filter `trip_current_departure`. Invalid dates return `422 VALIDATION_ERROR`. |
| `passengerPhone` | string? | null | Trim outer whitespace, then apply `PhoneNumber.Normalize`: accept only local `0xxxxxxxxx`/`0xxxxxxxxxx` or canonical `+84xxxxxxxxx`/`+84xxxxxxxxxx`; canonicalize local input to E.164. Internal spaces, hyphens, parentheses, or other separators are invalid and are not stripped. |
| `bookingCode` | string? | null | Trimmed, non-empty, maximum 30 characters, exact case-insensitive match. |
| `page` | integer | `1` | Must be `>= 1`. |
| `pageSize` | integer | `20` | Must be `>= 1`; values above 100 are clamped to 100. |
| `sortBy` | string | `createdAt` | Allow-list: `createdAt`, `departureAt`, `bookingCode`, `status`, `totalAmount`; otherwise `400 INVALID_SORT_FIELD`. |
| `sortDir` | string | `desc` | `asc` or `desc`; otherwise `422 VALIDATION_ERROR`. |

`search`, `searchIn`, `operatorId`, and `includeDeleted` are not supported. Every SQL query path first constrains `bookings.operator_id = :claimOperatorId`, before filters and pagination. `sortBy=departureAt` sorts by `trip_current_departure`; there is no `currentDepartureAt` sort key. Sort always adds `id` as the deterministic tie-breaker in the same direction as `sortDir`.

When `passengerPhone` is present, Booking validates and normalizes it before URI-escaping the canonical E.164 value and calling `GET /internal/v1/users/by-phone`. Only Identity's `404 RESOURCE_NOT_FOUND` means no matching user and produces a normal empty page.

Response `200`: ADR 0004 success envelope whose `data` is the seven-field `PagedResult<OperatorBookingListItemDto>`.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [{
      "id": "uuid",
      "bookingCode": "VR-20260618-ABCDEFGH",
      "tripId": "uuid",
      "status": "CONFIRMED",
      "trip": {
        "routeName": "Sai Gon - Da Lat",
        "originName": "Sai Gon",
        "destinationName": "Da Lat",
        "departureAt": "2026-06-18T08:00:00+07:00",
        "currentDepartureAt": "2026-06-18T10:30:00+07:00"
      },
      "seatCount": 2,
      "totalAmount": 500000,
      "createdAt": "2026-06-17T12:00:00Z"
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-18T01:00:00Z" }
}
```

Trip snapshot strings and immutable `trip.departureAt` are nullable for legacy rows. Mutable `trip.currentDepartureAt` projects `trip_current_departure` and is also nullable only where a legacy row could not be backfilled. Neither list nor detail duplicates `currentDepartureAt` at the top level. Money is VND backed by BIGINT, to-the-dong. An unknown normalized phone or a page beyond the last page returns HTTP 200 with `items: []` in the same seven-field shape; the requested `page`, effective `pageSize`, counts, and flags are returned normally.

Errors use the ADR 0004 envelope:

- `403 FORBIDDEN`: role is not allowed or the authenticated operator claim is absent.
- `400 INVALID_SORT_FIELD`: `sortBy` is outside the allow-list.
- `422 VALIDATION_ERROR`: any other invalid filter or paging value.
- `502 UPSTREAM_UNAVAILABLE`: the Identity lookup failed in any way other than its exact `404 RESOURCE_NOT_FOUND` no-match response.

### GET `/v1/operator/bookings/{id}`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`. The tenant key comes only from the authenticated JWT `operatorId` claim. Idempotency: not required (read-only). `id` must be a UUID; malformed input returns `422 VALIDATION_ERROR`.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "id": "uuid",
    "bookingCode": "VR-20260618-ABCDEFGH",
    "buyerUserId": "uuid",
    "tripId": "uuid",
    "status": "CANCELLED",
    "trip": {
      "routeName": "Sai Gon - Da Lat",
      "originName": "Sai Gon",
      "destinationName": "Da Lat",
      "departureAt": "2026-06-18T08:00:00+07:00",
      "currentDepartureAt": "2026-06-18T10:30:00+07:00"
    },
    "seatCount": 1,
    "baseFare": 600000,
    "discountAmount": 100000,
    "totalAmount": 500000,
    "pickupStationId": "uuid",
    "pickupStopId": null,
    "dropoffStationId": "uuid",
    "dropoffStopId": null,
    "bookingGroupId": null,
    "tripDirection": null,
    "cancellationReason": "USER_INITIATED",
    "createdAt": "2026-06-17T12:00:00Z",
    "seats": [{
      "passengerRecordId": "uuid",
      "ticketId": "uuid",
      "ticketCode": "VT-20260618-ABCDEFGH",
      "seatNumber": "A01",
      "ticketStatus": "CANCELLED",
      "boardingStatus": "PENDING"
    }],
    "statusTimeline": [
      { "status": "PENDING_PAYMENT", "occurredAt": "2026-06-17T12:00:00Z", "reasonCode": null },
      { "status": "CANCELLED", "occurredAt": "2026-06-17T12:05:00Z", "reasonCode": "USER_INITIATED" }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-18T01:00:00Z" }
}
```

The detail contains the list fields plus exactly the additional buyer, amount, pickup/dropoff, round-trip, cancellation, seat, and timeline fields shown above. Nullable database fields remain nullable. It never returns buyer/passenger phone, email, display name, ID number, timeline `actorUserId`, or timeline `source`. Timeline rows are real `booking_status_history` records ordered by `occurred_at ASC, id ASC`; no lifecycle-timestamp or Outbox reconstruction is permitted.

Errors use the ADR 0004 envelope:

- `403 FORBIDDEN`: the booking id exists but belongs to another operator, or caller role/operator context is invalid.
- `404 BOOKING_NOT_FOUND`: the booking id does not exist.
- `422 VALIDATION_ERROR`: malformed booking `id` UUID in the detail route parameter.

### POST `/v1/bookings`

Auth: `PASSENGER`. Idempotency: required.

Request:
```json
{
  "tripId": "uuid",
  "pickup": { "stationId": "uuid" },
  "dropoff": { "stationId": "uuid" },
  "seats": [{ "seatNumber": "A01" }],
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
    "paymentId": "uuid | null",
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
    "seats": [{ "seatNumber": "A01" }]
  },
  "return": {
    "tripId": "uuid",
    "pickup": { "stationId": "uuid" },
    "dropoff": { "stationId": "uuid" },
    "seats": [{ "seatNumber": "A01" }]
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
    "paymentId": "uuid",
    "status": "PENDING_PAYMENT",
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

Query: `status?`, `from?`, `to?`, `page=1`, `pageSize=20` (maximum 100). `status`, when supplied,
must be a `BookingStatus`. `from` and `to` are RFC 3339 timestamps filtering `createdAt`, with
`from` inclusive, `to` exclusive, and `from < to`. Ordering is fixed as
`createdAt DESC, bookingId DESC`. Results are bookings owned by JWT `sub`; pagination is per
Booking, and each Booking carries its Ticket summaries.

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
        "createdAt": "2026-05-01T09:00:00Z",
        "departureDateTime": "2026-05-18T08:00:00+07:00",
        "originName": "Bến xe Miền Đông",
        "destinationName": "Bến xe Mỹ Đình",
        "totalAmount": 350000,
        "bookingGroupId": null,
        "tripDirection": null,
        "routeName": "TP.HCM - Hà Nội",
        "tickets": [
          {
            "ticketId": "uuid",
            "ticketCode": "VT-20260518-ABCDEFGH",
            "seatNumber": "A01",
            "status": "ISSUED",
            "paidAmount": 350000
          }
        ]
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

Validation failures return `422 VALIDATION_ERROR`.

### GET `/internal/v1/bookings/history`

Auth: Internal JWT. Caller: Parcel Service. Never exposed through Gateway.

Query: required `userId`, plus the same `status?`, `from?`, `to?`, `page=1`, and `pageSize=20`
semantics as the public Booking history endpoint. It returns the same paged data DTO, preserving
Booking ownership, per-Booking pagination, nested Ticket summaries, and deterministic ordering.

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

### GET `/internal/v1/bookings/trips/{tripId}/edit-impact?operatorId={operatorId}`

Auth: Internal JWT. Caller: Trip only. `operatorId` is required, non-empty, and is supplied from
the tenant-scoped Trip aggregate, never from a public request body. Every Booking query predicates
both `trip_id = tripId` and `operator_id = operatorId` before applying the active-status filter.

Response `200` is a raw DTO (no `ApiResponse` success envelope):

```json
{
  "tripId": "uuid",
  "activeBookingCount": 2,
  "activeBookings": [
    {
      "bookingId": "uuid",
      "status": "PENDING_PAYMENT",
      "seatNumbers": ["A01", "A02"]
    },
    {
      "bookingId": "uuid",
      "status": "CONFIRMED",
      "seatNumbers": ["B01"]
    }
  ]
}
```

Active means exactly `PENDING_PAYMENT|CONFIRMED`. An unaffected Trip returns `200` with count `0`
and an empty array. Each Booking appears once and `seatNumbers` contains no duplicates. The
response contains no user/contact PII. Missing/empty `operatorId` is
`422 VALIDATION_ERROR`; invalid internal authentication is `401`; no cross-tenant row contributes
to either the count or list.

### GET `/internal/v1/bookings/trips/{tripId}/notification-recipients`

Auth: valid Internal JWT only. Caller: Notification Service. Never exposed through Gateway.
Booking resolves the passenger ownership projection for trip-wide notifications without exposing
contact PII or requiring a cross-database query.

Response `200` is a raw DTO without an `ApiResponse` success envelope:

```json
{
  "tripId": "uuid",
  "recipients": [
    {
      "bookingId": "uuid",
      "userId": "uuid",
      "status": "CONFIRMED"
    }
  ]
}
```

Only `CONFIRMED` and `PARTIAL_NO_SHOW` bookings are returned. Rows are distinct and ordered by
`bookingId`, then `userId`, to keep retries deterministic. A trip with no eligible booking returns
raw `200` with `recipients: []`. Invalid Internal JWT returns `401`; malformed/all-zero `tripId`
returns `422 VALIDATION_ERROR`. The endpoint has no Gateway route.

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

## Day 24 — STOP_DISABLED passenger choices

The three STOP_DISABLED mutations are owner-only, require a UUID-v4 `Idempotency-Key`, and
accept a request at `deadline == now`; only a request strictly after the effective deadline is
expired. The fingerprint is the authenticated actor, method, path, canonical query, and raw body.
Booking captures one handler clock `capturedNow` and persists
`deadline = min(capturedNow + 24h, tripCurrentDeparture - 2h)`; the scheduler selects only
`deadline < now`; equality performs no synchronous fallback and is resolved by only the next
five-minute scheduler pass after the strict boundary.
Same-key/same-fingerprint replays the original HTTP status and bytes before action state lookup;
same-key/different-fingerprint returns `422 IDEMPOTENCY_KEY_MISMATCH`; a new key after terminal
resolution returns `409 BOOKING_PENDING_ACTION_ALREADY_RESOLVED` (or the documented expired/stale
conflict when that state check wins). Ownership mismatches return `404 BOOKING_NOT_FOUND`.

| Mutation | Exact request body | Successful response | STOP_DISABLED transition |
|---|---|---|---|
| `POST /v1/bookings/{bookingId}/edit-pickup` | Existing body `{ "pickup": { "stationId": "uuid", "stopId": "uuid|null" }, "paymentMethod": "WALLET" }` | Existing `200 ApiResponse<{bookingId,pickup,fareDelta:0,refundAmount:0,paymentRedirectUrl:null}>` | Atomically updates pickup, sets `resolvedAction=ACCEPTED`, and resolves the active action. |
| `POST /v1/bookings/{bookingId}/edit-dropoff` | Existing body `{ "dropoff": { "stationId": "uuid|null", "stopId": "uuid|null" } }` | Existing `200 ApiResponse<{bookingId,dropoff,fareDelta:0}>` | Atomically updates dropoff, sets `resolvedAction=ACCEPTED`, and resolves the active action. |
| `POST /v1/bookings/{bookingId}/pending-action/{actionId}/accept-fallback` | No body | `200 ApiResponse` (body shape is not specified by the ratified D24-2 record) | Atomically maps pickup to the route origin station or dropoff to the destination station, then resolves the action. |
| `POST /v1/bookings/{bookingId}/cancel` | Existing body `{ "reason": "STOP_DISABLED_REFUSED" }` | Existing `200 ApiResponse<{bookingId,status:"CANCELLED",refundAmount:100% of totalAmount,refundMethod}>` | Atomically resolves, cancels, sets `refundOverride=true`, and emits only the existing `booking.booking.cancelled`. |

The edit endpoints retain their existing price-neutral rules and do not add a STOP_DISABLED
resolver body or broaden the Day-23 `POST /pending-actions/{actionId}/resolve` body. The Day-23 `SCHEDULE_CHANGE` resolver/body is unchanged. Every choice
preserves passenger ownership and deadline checks. All three use `409 BOOKING_PENDING_ACTION_EXPIRED`
for a strictly late request and `409 BOOKING_PENDING_ACTION_ALREADY_RESOLVED` for a new key after
terminal resolution; missing/mismatched idempotency keys use `422 IDEMPOTENCY_KEY_REQUIRED` /
`422 IDEMPOTENCY_KEY_MISMATCH`.

### POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`

Auth: user JWT with role `PASSENGER`, and the caller must own the Booking. A valid JWT with any
other role returns `403 FORBIDDEN` before Booking/action lookup. This endpoint resolves a
persisted `SCHEDULE_CHANGE` or `ROUTE_CHANGE`; operator seat assignment remains outside this
contract.

`Idempotency-Key` is required and must be UUID v4. The fingerprint includes actor, method, path,
canonical query, and raw body. A same key + same payload request replays the stored status/body
byte-identical before any current Booking/action state is inspected. A new key evaluates the
current state and therefore receives the applicable terminal conflict. A same key with a different
fingerprint returns `422 IDEMPOTENCY_KEY_MISMATCH`; an in-flight same request returns
`409 IDEMPOTENCY_REQUEST_PENDING`.

Request:
```json
{
  "action": "ACCEPTED",
  "selectedStopId": "00000000-0000-4000-8000-000000000037",
  "selectedStationId": null,
  "note": "optional"
}
```

The body has `action: ACCEPTED|REJECTED`, nullable `selectedStopId`, nullable
`selectedStationId`, and optional `note`. For `SCHEDULE_CHANGE`, both selected IDs are invalid and
must be omitted or null. For `ROUTE_CHANGE`, `ACCEPTED` requires exactly one selected identity and
it must exactly match one frozen candidate in action metadata; `REJECTED` requires neither.
Booking never calls Trip to refresh candidates. Resolution at the effective cutoff is
passenger-eligible; only a request strictly after the effective cutoff returns
`BOOKING_PENDING_ACTION_EXPIRED`. For SCHEDULE_CHANGE MEDIUM the cutoff is `initialDeadline`; for
MAJOR it is `terminalDeadline` only when `initialDeadline < terminalDeadline`, otherwise
`initialDeadline`. ROUTE_CHANGE uses `occurredAt + 30m` for `IN_PROGRESS` and `occurredAt + 60m`
for `SCHEDULED|BOARDING`.

`ACCEPTED` atomically resolves the action and leaves the Booking `CONFIRMED`. `REJECTED` computes
the frozen refund from immutable `Booking.totalAmount` (SCHEDULE_CHANGE MEDIUM 50%, MAJOR 100%;
ROUTE_CHANGE 100%; rounded to the nearest VND with `MidpointRounding.AwayFromZero`), then
atomically resolves the action, sets `refundOverride=true`, cancels the Booking with
`SCHEDULE_CHANGED` or `ROUTE_CHANGED_REFUSED`, appends history, and enqueues exactly one authoritative
`booking.booking.cancelled` containing that `refundAmount`.

ROUTE_CHANGE no-response expiry is different from explicit `REJECTED`: only a scheduler pass with
`deadline < now` resolves `AUTO_FALLBACK_DESTINATION`. It leaves the Booking `CONFIRMED`, changes
no pickup field, creates no refund, and retains immutable metadata
`{originalStopId,fallbackDestinationStationId,shuttleRequired:true}` for shuttle coordination.
The same transaction emits one `booking.booking.route_change_auto_fallback_applied`.

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

Errors use the ADR 0004 envelope. Authorization/masking and lookup order are exact:

| HTTP | Error code | Trigger / masking rule |
|---|---|---|
| 401 | `AUTH_TOKEN_INVALID` | Missing, invalid, or expired user JWT. |
| 403 | `FORBIDDEN` | Valid JWT role is not `PASSENGER`; reject before Booking/action lookup. |
| 404 | `BOOKING_NOT_FOUND` | Booking is missing or not owned; also masks a discovered Booking/action ownership mismatch before action state is revealed. |
| 404 | `BOOKING_PENDING_ACTION_NOT_FOUND` | Booking was found and owner-authorized, but `actionId` does not exist under that Booking. |
| 409 | `BOOKING_PENDING_ACTION_NOT_RESOLVABLE` | Active action exists, but persisted reason/state or Booking state does not support this Day-23 resolution. |
| 409 | `BOOKING_PENDING_ACTION_SUPERSEDED` | A new key targets an action terminally resolved as `SUPERSEDED`. |
| 409 | `BOOKING_PENDING_ACTION_ALREADY_RESOLVED` | A new key targets an action resolved as `ACCEPTED` or `REJECTED`. |
| 409 | `BOOKING_PENDING_ACTION_EXPIRED` | Passenger request is strictly after the effective cutoff; timeout owns the outcome and only auto-accepts, never cancels or refunds. Equality remains eligible. |
| 409 | `IDEMPOTENCY_REQUEST_PENDING` | Same key/fingerprint is still executing. |
| 422 | `IDEMPOTENCY_KEY_REQUIRED` | Required `Idempotency-Key` header is absent. |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Same key has a different actor/method/path/query/raw-body fingerprint. |
| 422 | `VALIDATION_ERROR` | Malformed/non-v4 key; malformed route UUID; missing/invalid `action`; `selectedStopId` present; or another request-shape failure. |

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

Trip detail includes nullable `destinationArrivedAt` (ISO-8601 datetime with offset). It is
`null` until the assigned Driver/Assistant records physical arrival at the destination terminal.

Each stop includes `stopId`, `name`, `address`, `latitude`, `longitude`, `isActive`,
`orderIndex`, `allowPickup`, `allowDropoff`, `status` (`PENDING|ARRIVED|SKIPPED`),
`estimatedArrivalTime`, nullable `actualArrivalTime`,
`distanceFromOriginKm`, nullable `fareFromThisStop`, and `effectiveFare`.
`effectiveFare = fareFromThisStop ?? baseFare`; this is pickup-point pricing, not segment fare.
`actualArrivalTime` is populated only for `ARRIVED`; it remains `null` for `PENDING` and
`SKIPPED`.

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

### GET `/internal/v1/bookings/trips/{tripId}/stops/{stopId}/pending-passenger-count?operatorId={operatorId}`

Canonical route: `GET /internal/v1/bookings/trips/{tripId}/stops/{stopId}/pending-passenger-count?operatorId={operatorId}`.

Auth: valid Internal JWT only. This is a raw internal success response (no `ApiResponse` envelope)
and has no Gateway route. The sole caller is Trip after it has validated its own Trip, TripStop,
assigned crew, and tenant. Booking performs no Trip/Stop lookup, caller-service/tenant-claim authorization,
or additional cross-service validation, and absent logical references do not become
`403`/`404`.

The exact predicate is:

```text
Booking.status = CONFIRMED
AND Passenger.boardingStatus = PENDING
AND Booking.tripId = :tripId
AND Booking.pickupStopId = :stopId
AND Booking.operatorId = :operatorId
```

Response `200` (raw), including the no-match case with count `0`:

```json
{ "tripId": "uuid", "stopId": "uuid", "pendingPassengerCount": 0 }
```

`tripId`, `stopId`, and `operatorId` must be non-empty, non-zero UUIDs. Malformed/all-zero input
returns `422 VALIDATION_ERROR`; invalid Internal JWT returns `401 AUTH_TOKEN_INVALID`. No row
match is still raw `200` with zero.

### GET `/internal/v1/trips/{tripId}?pricingAt={iso8601}`

Auth: valid Internal JWT only. Callers: Booking, Parcel, Tracking, Payment, Notification (BSOT §7.2). This
endpoint adds no tenant authorization; invalid Internal JWT returns `401 AUTH_TOKEN_INVALID`. Trip snapshot
that Booking reads for checkout fare calc + pickup/dropoff validation. `pricingAt` is optional;
when present it must be an ISO-8601 datetime with offset. Returns a **raw DTO**
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
  "actualDepartureTime": null,
  "estimatedArrivalTime": "2026-05-18T20:00:00+07:00",
  "baseFare": 400000,
  "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
  "destinationStation": { "id": "uuid", "name": "Bến xe Mỹ Đình" },
  "stops": [
    {
      "stopId": "uuid",
      "isActive": true,
      "orderIndex": 1,
      "allowPickup": true,
      "allowDropoff": false,
      "status": "PENDING",
      "actualArrivalTime": null,
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
- The serialized response fields are unchanged whether `pricingAt` is present or omitted.
- With `pricingAt`, Trip resolves each pickup fare in this exact order: persisted
  `TripStopFare.source=MANUAL_OVERRIDE`; otherwise the active `RouteStopFareTemplate` whose
  half-open window satisfies `effectiveFrom <= pricingAt < effectiveUntil` (or has no upper
  bound); otherwise `Trip.baseFare`. Overlapping active template windows for the same
  `(routeId,stopId)` are forbidden.
- Without `pricingAt`, preserve the legacy operational snapshot: use a persisted
  `MANUAL_OVERRIDE` or `TEMPLATE_SNAPSHOT` when present, otherwise `Trip.baseFare`; do not query
  time-window templates. Existing Parcel, Notification, Payment, Tracking, and non-pricing
  Booking callers therefore remain source/wire compatible.
- Booking creation and both round-trip legs capture one handler-start `pricingAt` and pass that
  same timestamp on every Trip pricing call. `PaymentSucceeded` never re-queries Trip pricing.
- `fareFromThisStop` is the resolved per-stop override when present; otherwise the caller falls
  back to `baseFare`. `null` ⇒ use `baseFare`.
- `stops` are the along-route intermediate stops (snapshot of RouteStop into `trip_stops`),
  ordered by `orderIndex`; `allowPickup` / `allowDropoff` drive Day-13 pickup/dropoff validation.
- `returnRouteId`: nullable UUID — the return-direction route linked via `Route.returnRouteId`
  self-FK. Booking uses this to validate `ROUTE_RETURN_NOT_CONFIGURED` (422) when the passenger
  requests a round-trip but the outbound route has no return route configured
  (technical_context_v7 line 1750). Trip will expose this field in Task 11.4.
- `driverUserId` / `assistantUserId`: nullable UUID logical user keys used by downstream services
  for trip-assignment authorization. They do not create cross-database foreign keys.
- `actualDepartureTime`: nullable UTC datetime captured by Trip when the vehicle actually leaves
  the terminal. This additive field is authoritative for terminal no-show detection; existing
  stop snapshot `status` and nullable `actualArrivalTime` remain authoritative for along-route
  anchors. No event or projection is added to this snapshot seam.
- Errors: `404 TRIP_NOT_FOUND`.

### POST `/internal/v1/trips/{tripId}/lock-seats`

Auth: Internal JWT. Idempotency: required (replay with same `Idempotency-Key` returns the
same `seatLockToken`). **All-or-nothing** — if any requested seat is not `AVAILABLE`, no seat
is locked.

Round-trip confirmation uses `POST /internal/v1/trips/round-trip/book-seats` with outbound
and return legs (`tripId`, `seatLockToken`, `bookingId`, `passengerSeatAssignments`). Trip
validates ownership of both locks before changing either leg and persists both legs atomically.

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

### POST `/v1/operator/trips/{id}/cancel/preview`

Auth: `OPERATOR_ADMIN` for the Trip's operator. This read-only preview does not require an
Idempotency-Key and does not change Trip or Booking state.

Request:
```json
{}
```

Response `200` data:
```json
{
  "tripId": "00000000-0000-4000-8000-000000000033",
  "status": "SCHEDULED",
  "affectedBookingIds": ["00000000-0000-4000-8000-000000000034"],
  "refundTotalBooking": 1250000,
  "affectedParcelIds": ["00000000-0000-4000-8000-000000000035"],
  "refundTotalParcel": 35000,
  "grandTotal": 1285000
}
```

The preview is available only while the Trip is `SCHEDULED` or `BOARDING`. Booking and parcel
totals are calculated independently from immutable persisted amounts; `grandTotal` is their sum.
Pending-payment Bookings contribute zero.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

Errors:
- `404 TRIP_NOT_FOUND` when the Trip is missing or belongs to another operator.
- `409 TRIP_NOT_EDITABLE` when the Trip is not `SCHEDULED` or `BOARDING`.

### POST `/v1/operator/trips/{id}/cancel`

Auth: `OPERATOR_ADMIN` for the Trip's operator. Idempotency: required UUID-v4
`Idempotency-Key`.

Request:
```json
{
  "reason": "Vehicle issue"
}
```

`reason` is required and must be non-empty text.

Response `200` data:
```json
{
  "tripId": "00000000-0000-4000-8000-000000000033",
  "status": "CANCELLED"
}
```

Only a `SCHEDULED` or `BOARDING` Trip can transition to `CANCELLED`. Trip publishes
`trip.trip.cancelled`; Booking owns the affected Booking transitions and publishes the sole
refund trigger, `booking.booking.cancelled`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

Errors:
- `404 TRIP_NOT_FOUND` when the Trip is missing or belongs to another operator.
- `409 TRIP_NOT_EDITABLE` when the Trip is not `SCHEDULED` or `BOARDING`.

### POST `/v1/operator/trips/{id}/change-route`

Auth: `OPERATOR_ADMIN` for the Trip's operator. Idempotency: required UUID-v4
`Idempotency-Key`.

Request:
```json
{
  "alternativeRouteId": "00000000-0000-4000-8000-000000000036"
}
```

Response `200` data:
```json
{
  "tripId": "00000000-0000-4000-8000-000000000033",
  "status": "IN_PROGRESS",
  "alternativeRouteId": "00000000-0000-4000-8000-000000000036",
  "affectedBookings": [
    {
      "bookingId": "00000000-0000-4000-8000-000000000034",
      "candidateStops": [
        {
          "stopId": "00000000-0000-4000-8000-000000000037",
          "stationId": null,
          "stationName": "Alternative stop",
          "sequence": 1,
          "estimatedArrivalAt": "2026-07-23T01:45:00Z"
        },
        {
          "stopId": null,
          "stationId": "00000000-0000-4000-8000-000000000038",
          "stationName": "Destination station",
          "sequence": 2,
          "estimatedArrivalAt": "2026-07-23T04:50:00Z"
        }
      ]
    }
  ]
}
```

The AlternativeRoute must be active, belong to the Trip's Route, and belong to the same
operator. Route change is supported for `SCHEDULED`, `BOARDING`, and `IN_PROGRESS`; other Trip
states are not editable. `affectedBookings` is ordered by `bookingId`, and each immutable
`candidateStops` array is ordered by `sequence`. Every candidate contains exactly
`{stopId,stationId,stationName,sequence,estimatedArrivalAt}` with XOR identity: intermediate
AlternativeRoute stops set only `stopId`, while the appended destination Station sets only
`stationId`. Trip derives these snapshots locally from the selected AlternativeRoute in the
route-change transaction; no cross-database FK or synchronous consumer lookup is permitted.
The event adds `tripStatus` with exactly `SCHEDULED|BOARDING|IN_PROGRESS`. Neither response nor
event contains `affectedBookingIds`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

Errors:
- `404 TRIP_NOT_FOUND` when the Trip is missing or belongs to another operator.
- `404 ROUTE_NOT_FOUND` when the AlternativeRoute is missing, inactive, belongs to another
  parent Route, or belongs to another operator.
- `409 TRIP_NOT_EDITABLE` when the Trip lifecycle does not permit a route change.

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

## Day 22 — Trip edit, pricing snapshot, and cascade contracts

### PATCH `/v1/operator/trips/{tripId}`

Auth: `OPERATOR_ADMIN` for the Trip operator. `Idempotency-Key`: required UUID v4. Existing
Gateway prefixes already cover the path; the controller enforces the narrower write role.

Request is exactly:

```json
{
  "baseFare": 420000,
  "notes": "Đón khách sớm 15 phút",
  "vehicleId": "uuid",
  "routeId": "uuid"
}
```

All fields are optional, but at least one recognized field must be supplied. Omitted means
unchanged. `notes:null` clears; notes are trimmed and blank normalizes to null. `baseFare`,
`vehicleId`, and `routeId` do not accept null. Unknown-only/empty/malformed bodies and any
attempt to edit `departureDateTime`, crew, or other fields return `422 VALIDATION_ERROR`.
`baseFare` is VND to the đồng: raw BIGINT-compatible values are not floored to 1,000.

Field lifecycle:

| Field | Statuses in which an actual change is allowed |
|---|---|
| `baseFare` | `SCHEDULED` |
| `routeId` | `SCHEDULED` |
| `vehicleId` | `SCHEDULED\|BOARDING`, subject to the swap matrix |
| `notes` | every non-terminal status (`SCHEDULED\|BOARDING\|IN_PROGRESS`) |

The domain order is fixed: tenant-scoped Trip load (missing/cross-tenant is `404 TRIP_NOT_FOUND`)
→ normalize and compute actual changed fields → same-value no-op `200` → validate the lifecycle
matrix only for actual changes → validate new Route/Vehicle under the same tenant → make exactly
one Booking edit-impact call when route/vehicle changes → apply conflict precedence
`TRIP_ROUTE_CHANGE_BOOKINGS_EXIST`, then `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`, then
`TRIP_VEHICLE_SWAP_TOO_LATE`, then remaining local route/layout conflicts → only then open one
local transaction → lock/reload/revalidate in fixed aggregate order Trip → seats → stops (with
stable repository ordering inside each collection) → apply
the complete mutation → stage audit/Outbox → save and commit once. No database transaction spans
an HTTP call. A no-op creates no audit, event, or downstream call.

Route change is rejected with exactly `409 TRIP_ROUTE_CHANGE_BOOKINGS_EXIST` when the trusted
Booking projection contains any `PENDING_PAYMENT|CONFIRMED` Booking. Otherwise Trip atomically
rebuilds Trip stops, per-stop fares, and the static planned ETA baseline from the new Route, with
local HELD/BOOKED seat races revalidated under lock. An approved pre-departure Route edit or a
DriverSchedule `ALL_PENDING` cascade may recompute this baseline; GPS/Tracking dynamic ETA never
updates it.

Vehicle compatibility is keyed by normalized `seatNumber` and uses this comparison only:
`STANDARD < SLEEPER_UPPER < SLEEPER_LOWER < VIP`. The rank never affects pricing.
`DRIVER_AREA` is not a passenger seat. For every old HELD/BOOKED passenger seat:

| New layout at the same number | Result |
|---|---|
| number absent | `SEAT_REMOVED` |
| `disabled=true` or type `DRIVER_AREA` | `SEAT_DISABLED` |
| enabled passenger type with lower rank | `SEAT_TYPE_DOWNGRADED` |
| enabled equal/higher-ranked passenger type | compatible; preserve HELD/BOOKED |

For a `SCHEDULED` Trip, any incompatible HELD seat rejects the entire request with
`409 TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`. An incompatible BOOKED seat may proceed only when
`deadline = min(now + 4h, departureDateTime - 30m)` is strictly greater than the one captured
handler-start `now`; otherwise return `409 TRIP_VEHICLE_SWAP_TOO_LATE`. For a `BOARDING` Trip,
any incompatible HELD or BOOKED seat returns `409 TRIP_VEHICLE_SWAP_TOO_LATE`. AVAILABLE seats
may be deterministically removed/replaced; new enabled non-`DRIVER_AREA` seats become AVAILABLE.

Response `200`: `ApiResponse<TripDetailDto>`. Day 22 extends the existing DTO with nullable
`notes`; all existing fields remain:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "operatorId": "uuid",
    "routeId": "uuid",
    "vehicleId": "uuid",
    "status": "SCHEDULED",
    "departureDateTime": "2026-07-20T08:00:00+07:00",
    "estimatedArrivalTime": "2026-07-20T15:00:00+07:00",
    "baseFare": 420000,
    "notes": "Đón khách sớm 15 phút",
    "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
    "destinationStation": { "id": "uuid", "name": "Bến xe Đà Lạt" },
    "stops": [],
    "seatSummary": { "totalSeats": 40, "availableSeats": 18 },
    "returnRouteId": null,
    "fareBreakdown": { "baseFare": 420000, "stopFares": [] }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T10:00:00Z" }
}
```

### Day-22 idempotency and request precedence

For the two canonical PATCH endpoints and deprecated `/crew` alias, the feasible shared pipeline
is: authentication (`401`) → method-level role authorization (`403`) → shared pre-reservation
checks (matched route, UUID-v4 key, body-presence policy) → normalized fingerprint
reservation/replay/mismatch/pending → MVC JSON binding and endpoint query/body validation (`422`)
→ capture one handler-start `now` for a newly reserved request → domain validation/preflight →
one local transaction/commit → store the exact status/body for replay for every reserved response
below `500`. Thus a reserved `422` from missing/invalid `applyTo`, malformed JSON, unknown/empty
body, or FluentValidation is replayable. Authentication/authorization, unmatched-route, and
malformed-key failures before reservation are not cached; a `5xx` releases the reservation.

The fingerprint is exactly method + normalized route + normalized route values + normalized query
+ authenticated subject + canonical JSON body. Query keys sort ordinally; absent differs from
empty; ASP.NET-decoded values are used; repeated-value order is preserved; no query is omitted.
Semantically identical reordered query keys replay, while a different `applyTo`, subject, path
(including `/crew`), route value, or body returns `422 IDEMPOTENCY_KEY_MISMATCH`. A matching
in-flight request returns `409 IDEMPOTENCY_REQUEST_PENDING`.

### Day-22 persistence and audit freeze

- `Trip.notes` is nullable, trimmed, and at most 2,000 characters.
- `TripStopFare.source` is exactly `TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`; pre-Day-22 rows backfill
  `TEMPLATE_SNAPSHOT`. Day 22 creates no new `TEMPLATE_SNAPSHOT` rows: legacy rows remain readable
  only for callers that omit `pricingAt` and are non-authoritative when explicit `pricingAt` is
  supplied. Only an explicit operator per-Trip fare override persists `MANUAL_OVERRIDE`.
- The Trip database explicitly enables PostgreSQL `btree_gist`. A GiST exclusion constraint on
  `routeId`/`stopId` equality and
  `tstzrange(effectiveFrom,coalesce(effectiveUntil,'infinity'),'[)')` overlap makes future-dated
  templates concurrency-safe.
- Real changes alone append Trip actions `TRIP_EDITED`, `TRIP_VEHICLE_SWAPPED`,
  `TRIP_ROUTE_CHANGED`, or `DRIVER_SCHEDULE_CASCADE_APPLIED`; schedule changes append
  `DRIVER_SCHEDULE_EDITED` to the separate append-only DriverSchedule audit store. Metadata is
  exactly `{changedFields,before,after,requestId}` and never includes a raw Idempotency-Key.
- The Day-22 conflict codes are exactly `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST`,
  `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`, `TRIP_VEHICLE_SWAP_TOO_LATE`, and existing
  `DRIVER_SCHEDULE_EDIT_TOO_LATE`; validation still uses `VALIDATION_ERROR`.

## Parcel Service

Parcel cargo policy:
- Dimension unit: centimeters; weight unit: kilograms.
- Volume precision: `decimal(10,4)` m3.
- Weight/DIM/chargeable precision: `decimal(8,2)` kg.
- Money is VND `BIGINT` persisted to the đồng. `Money.FromRaw` is pass-through; a fractional
  calculation rounds to the nearest đồng with `MidpointRounding.AwayFromZero`.
- `dimWeightKg = lengthCm × widthCm × heightCm / 6000` and `chargeableWeightKg = max(weightKg, dimWeightKg)`.
- `grossPriceVnd = max(minimumPriceVnd, round(chargeableWeightKg × pricePerKgVnd))`; rounding is to the nearest đồng with `MidpointRounding.AwayFromZero`. There is no kg ceiling and no 1,000-VND floor.
- Size is derived from chargeable weight: `SMALL <= 5`, `MEDIUM <= 15`, `LARGE <= 30`, `EXTRA_LARGE > 30` kg. Client size fields are compatibility hints only.
- `estimatedTotalPriceVnd = estimatedGrossPriceVnd - min(discountAmountVnd, estimatedGrossPriceVnd)`; final total uses the same clamp against final gross.
- Settlement v2 deposit is 20% of estimated total. Only `READY_TO_LOAD` may transition to `LOADED`.
- `PENDING_OPERATOR_ACTION` is disambiguated by `pendingActionType`; `pendingActionResumeStatus` records the settlement state to resume after recovery.

### GET `/v1/parcels/available-trips`

Auth: `PASSENGER`.

Query: `originStationId`, `destinationStationId`, `departureDate`, `lengthCm`, `widthCm`, `heightCm`, `estimatedWeightKg`; legacy `sizeCategory` is optional and non-authoritative.

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
        "status": "SCHEDULED",
        "operatorId": "uuid",
        "operatorName": "VietRide Express",
        "originStation": { "id": "uuid", "name": "Bến đi" },
        "destinationStation": { "id": "uuid", "name": "Bến đến" },
        "departureDateTime": "2026-05-18T08:00:00+07:00",
        "estimatedArrivalTime": "2026-05-18T16:00:00+07:00",
        "estimatedPriceVnd": 150000,
        "depositPercent": 20,
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

`depositPercent` is `20` under settlement policy v2 and is snapshotted on creation. The public item does not serialize `availableCargoWeightKg`,
`availableCargoVolumeM3`, or the internal `priceVnd` alias.

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
  "voucherCode": null
}
```

`photoUrl` is optional and represents at most one parcel image. The client uploads the image
directly to Firebase Storage before creating the Parcel. When supplied, the URL is trimmed, must
be an absolute HTTPS URL no longer than 2,048 characters, and must address the bucket configured
by `FIREBASE_STORAGE_BUCKET` through either `firebasestorage.googleapis.com` or
`storage.googleapis.com`. Invalid values return `422 VALIDATION_FAILED` with a `photoUrl` field
error. Firebase Storage Rules enforce a 5 MB object limit and MIME type
`image/jpeg | image/png | image/webp`; Parcel Service does not receive or inspect file bytes.

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "PENDING_PAYMENT",
    "estimatedSizeCategory": "MEDIUM",
    "estimatedGrossPriceVnd": 150000,
    "discountAmountVnd": 0,
    "estimatedTotalPriceVnd": 150000,
    "depositPercent": 20,
    "depositRequiredVnd": 30000,
    "depositPaidVnd": 0,
    "voucherCode": null,
    "settlementPolicyVersion": 2
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

The server derives `estimatedSizeCategory`; old clients may still send `sizeCategory`, but it does not override calculated size or price. `EXTRA_LARGE` is returned as `PENDING_OPERATOR_REVIEW`; all other sizes return `PENDING_PAYMENT`. Create does not reserve cargo or create a payment.

### POST `/v1/parcels/{parcelId}/deposit-payment`

Auth: owning `PASSENGER`. Idempotency: required.

Request: `{ "paymentMethod": "WALLET|VNPAY" }`.

The mutation first creates an idempotent soft cargo hold using estimated weight/volume, then creates a Payment whose `dueAt = min(paymentStartedAt + 15 minutes, latestCheckInAt)`. If no positive payment window remains, it does not create a Payment or hold. A zero deposit consumes the validated voucher, keeps the reservation, and moves directly to `RESERVED` without creating a zero-value Payment.

Response `200` data contains `parcelId`, `status`, `depositPaymentId?`, `depositRequiredVnd`, `depositPaidVnd`, `paymentDueAt?`, and `paymentRedirectUrl?`. Payment success is valid only when authoritative `paidAt < paymentDueAt`; fail/expiry moves the Parcel to `EXPIRED`, releases cargo, and does not consume the voucher.

### POST `/v1/parcels/{parcelId}/final-payment`

Auth: owning `PASSENGER`. Idempotency: required. Allowed only in `PENDING_FINAL_PAYMENT` and before `finalPaymentDeadline`.

Request: `{ "paymentMethod": "WALLET|VNPAY" }`.

The charged amount is server-derived `max(0, balanceRequiredVnd - balancePaidVnd)`. Response data contains `parcelId`, `status`, `balancePaymentId?`, `balanceRequiredVnd`, `balancePaidVnd`, `finalPaymentDeadline`, and `paymentRedirectUrl?`. A payment with `paidAt >= finalPaymentDeadline` is not added to `balancePaidVnd`; Payment Service owns capture/refund tracking for that late payment.

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

### GET `/v1/parcels/sent`

Auth: `PASSENGER`.

Query: `status?`, `from?`, `to?`, `page=1`, `pageSize=20` (maximum 100). `status`, when supplied,
must be a `ParcelStatus`; timestamps use the same inclusive/exclusive RFC 3339 range semantics as
Booking history. Returns only rows where `senderUserId == JWT sub`, ordered by
`createdAt DESC, parcelId DESC`. Received-only parcels are not included.

Response data is paged and each item contains `parcelId`, `parcelCode`, `tripId`, `status`,
`createdAt`, `totalAmount`, `recipientName`, `sizeCategory`, `photoUrl`, `deliveryMethod`, and
nullable journey fields `originName`, `destinationName`, `departureDateTime`, and
`estimatedArrivalTime`. Trip enrichment failure leaves only the journey fields null and does not
turn a successful local history query into an error.

### GET `/v1/passenger/history`

Auth: `PASSENGER`. This facade is owned by Parcel Service and does not fan out both branches.

Query:
- `type` (required): `TICKET | PARCEL`; `ALL` is not supported.
- `status?`: `BookingStatus` for `TICKET`, `ParcelStatus` for `PARCEL`.
- `from?`, `to?`: RFC 3339 `createdAt` range; `from` inclusive, `to` exclusive, `from < to`.
- `page=1`, `pageSize=20`, maximum `100`.

Ordering is fixed as `createdAt DESC, id DESC`. `TICKET` pages Booking aggregates and invokes only
Booking's internal history endpoint. `PARCEL` invokes only Parcel's local sent-history query.

Response `200` item shape:
```json
{
  "type": "TICKET",
  "id": "booking-uuid",
  "code": "VR-20260518-ABCD1234",
  "tripId": "uuid",
  "status": "CONFIRMED",
  "createdAt": "2026-05-01T09:00:00Z",
  "totalAmount": 350000,
  "originName": "Bến xe Miền Đông",
  "destinationName": "Bến xe Mỹ Đình",
  "departureDateTime": "2026-05-18T08:00:00+07:00",
  "estimatedArrivalTime": null,
  "ticket": {
    "bookingGroupId": null,
    "tripDirection": null,
    "routeName": "TP.HCM - Hà Nội",
    "tickets": [
      {
        "ticketId": "uuid",
        "ticketCode": "VT-20260518-ABCDEFGH",
        "seatNumber": "A01",
        "status": "ISSUED",
        "paidAmount": 350000
      }
    ]
  },
  "parcel": null
}
```

For `PARCEL`, `ticket` is null and `parcel` is
`{ bookingId, recipientName, sizeCategory, photoUrl, deliveryMethod }`. Exactly one of `ticket` or
`parcel` is non-null. Journey fields may be null for legacy data or unavailable Trip enrichment.
Booking unavailability on `TICKET` returns `502 UPSTREAM_UNAVAILABLE`; it must not be represented
as an empty page. Validation failures return `422 VALIDATION_ERROR`.

### GET `/v1/parcels/{parcelId}`

Auth: sender, recipient account, or authorized operator.

Response `200`: parcel detail with sender, recipient, trip, transfer, optional `photoUrl`, delivery token state excluding raw token, estimated/actual cargo snapshots, and the canonical settlement fields: `estimatedGrossPriceVnd`, `finalGrossPriceVnd`, `discountAmountVnd`, `estimatedTotalPriceVnd`, `finalTotalPriceVnd`, `depositPercent`, `depositRequiredVnd`, `depositPaidVnd`, `balanceRequiredVnd`, `balancePaidVnd`, `refundDueVnd`, `refundedAmountVnd`, `forfeitedDepositVnd`, payment IDs, `finalPaymentDeadline`, check-in/reweigh timestamps, fare snapshots, and `settlementPolicyVersion`.

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

### GET `/v1/assistant/trips/{tripId}/parcels`

Auth: `ASSISTANT`. Read-only; Idempotency-Key is not required.

The caller must be the Assistant currently assigned to `tripId`. Results include all
non-deleted parcels whose current `tripId` and `operatorId` match the authorized trip
crew context. Query: `page` (default `1`) and `pageSize` (default `20`, maximum `100`).

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
        "status": "LOADED",
        "recipientName": "Nguyen Van A",
        "recipientPhone": "0900000000",
        "dropoffStopId": "uuid",
        "estimatedSizeCategory": "MEDIUM",
        "actualSizeCategory": "MEDIUM",
        "estimatedWeightKg": 12.5,
        "actualWeightKg": 13.2,
        "balanceRequiredVnd": 24000,
        "balancePaidVnd": 24000,
        "finalPaymentDeadline": "2026-05-18T07:50:00+07:00",
        "description": "Gói hàng nhỏ",
        "photoUrl": "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg"
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

Errors: `401 UNAUTHORIZED` without a valid access token; `403 FORBIDDEN` when the
caller is not the assigned Assistant, has no operator scope, or the trip is unavailable;
`422 VALIDATION_FAILED` for invalid pagination; `503 TRIP_SERVICE_UNAVAILABLE` when
assignment verification cannot reach Trip service.

### POST `/v1/assistant/parcels/{parcelId}/check-in`

Auth: assigned `ASSISTANT` under the same operator. Idempotency: required.

Request: `{ "tripId": "uuid", "parcelCode": "VRP-20260722-ABCDEFGH" }`.

Only `RESERVED` may be checked in and the request must arrive strictly before `latestCheckInAt = min(departureAt - 30 minutes, loadCutoffAt - 10 minutes)`. Response `200` data contains `parcelId`, `parcelCode`, `status: "CHECKED_IN"`, `checkedInAt`, and `latestCheckInAt`. A foreign trip/code is hidden as `404 PARCEL_NOT_FOUND`; a late request returns `409 PARCEL_CHECK_IN_CLOSED`.

### POST `/v1/assistant/parcels/{parcelId}/reweigh`

Auth: `ASSISTANT`. Idempotency: required.

Request:
```json
{
  "actualLengthCm": 62,
  "actualWidthCm": 42,
  "actualHeightCm": 36,
  "actualWeightKg": 13.2
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
    "status": "PENDING_FINAL_PAYMENT",
    "actualSizeCategory": "MEDIUM",
    "actualChargeableWeightKg": 15.62,
    "finalGrossPriceVnd": 180000,
    "discountAmountVnd": 0,
    "finalTotalPriceVnd": 180000,
    "depositPaidVnd": 30000,
    "balanceRequiredVnd": 150000,
    "refundDueVnd": 0,
    "finalPaymentDeadline": "2026-05-18T07:50:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

Decision notes:
- Reweigh is allowed only from `CHECKED_IN`; backend derives actual size and all money values.
- Task reweigh owns estimated reservation → actual reservation. If capacity cannot be updated, status becomes `PENDING_OPERATOR_ACTION` with `pendingActionType=CAPACITY_EXCEEDED` and a `pendingActionResumeStatus`.
- Tolerance never waives settlement. A positive balance produces `PENDING_FINAL_PAYMENT`; otherwise the Parcel becomes `READY_TO_LOAD`.
- A positive `refundDueVnd` enqueues an idempotent Outbox refund but does not block `READY_TO_LOAD`.

### POST `/v1/assistant/parcels/{parcelId}/load`

Auth: `ASSISTANT`. The authenticated assistant identity and operator tenant are derived from the
JWT; neither is accepted from the request body. The caller must be assigned to the addressed Trip
under that same operator.

Idempotency: required. `Idempotency-Key` must be a UUID-v4. A same-key/same-payload retry replays
the original response without repeating the Parcel transition, cargo mutation, statistics update,
or Outbox write. Reusing the same key with a different actor, method, path, query, or raw body
returns `422 IDEMPOTENCY_KEY_MISMATCH`.

Request:
```json
{
  "tripId": "uuid",
  "parcelCode": "VRP-20260722-ABCDEFGH"
}
```

Response `200` (`ApiResponse`):
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VRP-20260722-ABCDEFGH",
    "status": "LOADED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-22T10:00:00Z" }
}
```

Errors:
- `401 UNAUTHORIZED` when the access token is missing or invalid.
- `403 FORBIDDEN` when the assistant is not assigned to the Trip or the assigned crew/operator
  tenant does not match.
- `404 PARCEL_NOT_FOUND` when the addressed Parcel is hidden, `tripId` does not match, or the
  scanned `parcelCode` does not match. These cases do not disclose a foreign Parcel.
- `409 INVALID_STATUS` when the Parcel is not `READY_TO_LOAD` or this request loses the transition race.
- `422 IDEMPOTENCY_KEY_MISMATCH` for same-key/different-payload reuse under the shared idempotency
  contract.

Day-29 E2E setup uses an isolated operator-owned Trip graph fixture with its assigned assistant,
vehicle cargo snapshot, and three Parcels. The fixture is created out of band; this contract does
not expose a public/manual Trip-create endpoint.

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

### GET `/internal/v1/parcels/trips/{tripId}/cancel-impact?operatorId={operatorId}`

Auth: Internal JWT. Read-only raw service-to-service projection used by Trip cancellation preview.

Response `200`:
```json
{
  "tripId": "uuid",
  "affectedParcels": [
    {
      "parcelId": "uuid",
      "status": "RESERVED",
      "refundAmountVnd": 35000
    }
  ]
}
```

The result is tenant-scoped by `operatorId`, ordered by `parcelId`, and contains each active
parcel at most once. Settlement v2 contributes `depositPaidVnd + balancePaidVnd - refundedAmountVnd`;
pre-payment/review and loaded/in-transit operational rows contribute zero.

### PATCH `/v1/operator/parcels/{parcelId}/review`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. Idempotency: required. Valid only from `PENDING_OPERATOR_REVIEW`.

Request: `{ "decision": "APPROVE|REJECT", "reason": "optional for approve, required for reject" }`. Price, deposit and payment method are not accepted from Operator input.

`APPROVE` moves to `PENDING_PAYMENT`; the Passenger then calls deposit-payment. `REJECT` moves to `REJECTED`. An unresolved review after 24 hours moves to `CANCELLED` with reason `OPERATOR_REVIEW_TIMEOUT`; no payment or refund exists in either reject/timeout branch.

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

This endpoint is legacy-policy-v1 compatibility only. Settlement policy v2 never creates `REFUND_CONFIRMATION`: a lower final price writes `refundDueVnd`, enqueues an idempotent refund, and moves directly to `READY_TO_LOAD`.

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

### GET `/v1/payments/vnpay-return-status`

Auth: public VNPay browser return. The complete VNPay query string, including
`vnp_TxnRef`, `vnp_TmnCode`, and `vnp_SecureHash`, is required. Payment verifies
HMAC-SHA512 and the configured merchant before reading the persisted transaction.

This endpoint is read-only. It never transitions Payment or Booking; only the signed
VNPay IPN can move `PENDING_REDIRECT` to a terminal state and publish the corresponding
integration event.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "vnPayTxnRef": "VR-BOOKING-20260724-001",
    "paymentId": "uuid",
    "referenceType": "BOOKING",
    "referenceId": "uuid",
    "status": "PENDING_REDIRECT"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-24T10:00:00Z" }
}
```

The HTTPS return bridge polls this resource while displaying a web fallback and may
also open `vietride://payments/return?<original-signed-query>`. Errors:
`401 PAYMENT_SIGNATURE_INVALID`, `404 PAYMENT_NOT_FOUND`, `422 VALIDATION_ERROR`.

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

### POST `/v1/operator/notifications`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`. Idempotency-Key: required.

Creates an in-app announcement and queues an FCM delivery for active `DRIVER` and `ASSISTANT`
recipients. `scope=TRIP` resolves the current crew snapshot for the specified trip and verifies
that the trip belongs to the caller operator. `scope=OPERATOR` resolves all active crew under
the caller operator.

```json
{
  "scope": "TRIP",
  "tripId": "uuid",
  "title": "Thông báo điều hành",
  "body": "Xe xuất bến sớm hơn 15 phút."
}
```

`tripId` is required only for `scope=TRIP`; it is forbidden for `scope=OPERATOR`. `title` is
1–120 characters and `body` is 1–500 characters. Response `202` contains
`{ announcementId, recipientCount }`. Retrying the same actor and Idempotency-Key returns the
original response for 24 hours.

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

### GET `/v1/operator/subscription`

Auth: `OPERATOR_ADMIN`. The operator scope is derived from the access token; no `operatorId` input is accepted.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "subscriptionId": "uuid",
    "status": "ACTIVE",
    "billingPeriod": "MONTHLY",
    "startedAt": "2026-07-14T10:00:00Z",
    "expiresAt": "2026-08-14T10:00:00Z",
    "plan": { "planId": "uuid", "name": "Pro", "price": 500000, "limits": {}, "modules": {} },
    "usage": {},
    "pendingUpgrade": {
      "upgradeAttemptId": "uuid",
      "targetPlan": { "planId": "uuid", "name": "Enterprise", "limits": {}, "modules": {} },
      "amount": 900000,
      "billingPeriod": "MONTHLY",
      "dueAt": "2026-07-14T10:15:00Z",
      "remainingSeconds": 720,
      "latestPayment": { "paymentId": "uuid", "status": "FAILED", "canRetry": true }
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-14T10:00:00Z" }
}
```

`PENDING_PAYMENT` and `EXPIRED` are valid readable states. Errors: `403 FORBIDDEN`, `404 RESOURCE_NOT_FOUND`.

### GET `/v1/operator/subscription-plans`

Auth: `OPERATOR_ADMIN`. Returns active plans only. Response uses the ADR 0004 envelope with `items`; each item has `planId`, `name`, `description`, `pricePerMonth`, `pricePerYear`, `limits`, and `modules`.

### POST `/v1/operator/subscription/upgrade`

Auth: `OPERATOR_ADMIN`. Idempotency-Key: required. `plan` hiện tại không đổi cho đến khi Payment `SUCCEEDED`; target plan chỉ xuất hiện trong `pendingUpgrade`.

Request:
```json
{
  "planId": "uuid",
  "billingPeriod": "MONTHLY",
  "paymentMethod": "VNPAY",
  "returnUrl": "https://app.vietride.vn/operator/subscription/result"
}
```

`billingPeriod` is `MONTHLY` or `YEARLY`. Identity snapshots the selected active plan's server-side price; the client never supplies an amount.

Response `202`:
```json
{
  "success": true,
  "statusCode": 202,
  "data": {
    "subscriptionId": "uuid",
    "upgradeAttemptId": "uuid",
    "status": "PENDING_PAYMENT",
    "paymentId": "uuid",
    "amount": 500000,
    "billingPeriod": "MONTHLY",
    "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
    "dueAt": "2026-07-14T10:15:00Z",
    "activePlan": { "planId": "uuid", "name": "Starter", "limits": {}, "modules": {} },
    "pendingTargetPlan": { "planId": "uuid", "name": "Pro", "limits": {}, "modules": {} }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-14T10:00:00Z" }
}
```

Errors: `403 FORBIDDEN`; `404 RESOURCE_NOT_FOUND`; `409 SUBSCRIPTION_PAYMENT_PENDING`; `422 VALIDATION_ERROR`; `422 IDEMPOTENCY_KEY_MISMATCH`.

For `paymentMethod=WALLET`, a successful atomic OperatorWallet charge returns `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "subscriptionId": "uuid",
    "upgradeAttemptId": "uuid",
    "status": "ACTIVE",
    "paymentId": "uuid",
    "amount": 500000,
    "billingPeriod": "MONTHLY",
    "invoiceStatus": "PENDING"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T10:00:00Z" }
}
```

Additional errors: `402 WALLET_INSUFFICIENT_BALANCE`; `422 IDEMPOTENCY_KEY_REQUIRED`. Replaying the same key and request returns the original response. Reusing the key with a different payload returns `422 IDEMPOTENCY_KEY_MISMATCH`.

### POST `/v1/operator/subscription/upgrade/{upgradeAttemptId}/retry-payment`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` bắt buộc. Chỉ cho phép khi attempt còn trong cửa sổ 15 phút và latest payment là `FAILED` hoặc `EXPIRED`. Mỗi retry tạo `paymentId` và `vnp_TxnRef` mới nhưng không kéo dài `dueAt`.

Response `202` dùng cùng `SubscriptionUpgradeResponseDto` với `paymentRedirectUrl` mới. Errors: `403 SUBSCRIPTION_UPGRADE_FORBIDDEN`; `404 RESOURCE_NOT_FOUND`; `409 SUBSCRIPTION_UPGRADE_EXPIRED`; `409 SUBSCRIPTION_PAYMENT_NOT_RETRYABLE`; `422 IDEMPOTENCY_KEY_REQUIRED`.

VNPay gọi canonical `GET|POST /v1/payments/vnpay-ipn`. `returnUrl` chỉ đưa browser về FE và không được phép mutate Payment hoặc Subscription.

## Invoice, OperatorWallet and Settlement — Day 38

All list endpoints return the ADR 0004 paged envelope with `items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, and `hasPreviousPage`. `pageSize` is `1..100`; `sortDir` is `asc|desc`; unsupported `sortBy` returns `400 INVALID_SORT_FIELD`. Operator scope always comes from trusted JWT claims and is never accepted from query/body.

### GET `/v1/operator/invoices`

Auth: `OPERATOR_ADMIN`. Query: `page?`, `pageSize?`, `status?`, `from?`, `to?`, `sortBy?` (`issuedAt|createdAt|amount|invoiceNumber`), `sortDir?`.

Item shape:

```json
{
  "invoiceId": "uuid",
  "invoiceNumber": "VR-INV-202607-000001",
  "paymentId": "uuid",
  "status": "ISSUED",
  "amount": 500000,
  "billingPeriod": "MONTHLY",
  "periodFrom": "2026-07-15T00:00:00Z",
  "periodTo": "2026-08-15T00:00:00Z",
  "pdfGenerationStatus": "COMPLETED",
  "createdAt": "2026-07-15T10:00:00Z",
  "issuedAt": "2026-07-15T10:01:00Z"
}
```

### GET `/v1/operator/invoices/{invoiceId}`

Auth: `OPERATOR_ADMIN`. Returns the item above plus `planName`, `buyerSnapshot`, `invoiceWebUrl`, and `downloadApiUrl`. A missing or foreign-tenant invoice returns `404 INVOICE_NOT_FOUND` without existence disclosure.

### GET `/v1/operator/invoices/{invoiceId}/download`

Auth: `OPERATOR_ADMIN`. Rate limit: 10 requests/minute per `(userId, invoiceId)`. The only success wire shape is `200 ApiResponse`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "downloadUrl": "https://storage.googleapis.com/...signed...",
    "expiresAt": "2026-07-15T11:00:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T10:00:00Z" }
}
```

The signed URL is generated after authorization, expires within 60 minutes and is never persisted, logged or emitted. Errors: `404 INVOICE_NOT_FOUND`; `500 INVOICE_PDF_GENERATION_FAILED`; `429 RATE_LIMIT_EXCEEDED`.

### POST `/v1/admin/invoices/{invoiceId}/retry`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key`: required. Body is empty. A retryable `FAILED` invoice with attempts `<5` is CAS-transitioned to `PENDING` and enqueued; the request itself does not increment attempts.

Response `202` data: `{ "invoiceId": "uuid", "pdfGenerationStatus": "PENDING", "attemptsUsed": 2 }`.

Errors: `404 INVOICE_NOT_FOUND`; `409 INVOICE_RETRY_ALREADY_PENDING`; `409 INVOICE_RETRY_NOT_ALLOWED`; `422 IDEMPOTENCY_KEY_REQUIRED`; `422 IDEMPOTENCY_KEY_MISMATCH`. Same-key replay returns the original `202`; different keys racing for the same invoice yield one `202` and one `409 INVOICE_RETRY_ALREADY_PENDING`.

### GET `/v1/operator/wallet`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "uuid",
    "balance": 1250000,
    "pendingHoldAmount": 300000,
    "eligibleAmount": 450000,
    "updatedAt": "2026-07-15T10:00:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T10:00:00Z" }
}
```

### GET `/v1/operator/wallet/transactions`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `type?`, `referenceType?`, `from?`, `to?`, `sortBy?` (`createdAt|amount`), `sortDir?`. Items contain `transactionId`, `type`, `amount`, `balanceBefore`, `balanceAfter`, `referenceType`, `referenceId`, `note`, `createdAt`.

### GET `/v1/operator/trip-settlements`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `status?`, `tripId?`, `from?`, `to?`, `sortBy?` (`createdAt|eligibleAt|settledAt|netAmount`), `sortDir?`. Items contain `settlementId`, `tripId`, `status`, `eligibleAt`, `netAmount`, `settlementMethod`, `settledAt`, `createdAt`.

### GET `/v1/operator/ledger`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `tripId?`, `entryType?`, `referenceType?`, `from?`, `to?`, `sortBy?` (`createdAt|amount`), `sortDir?`. Items contain `ledgerEntryId`, `tripId`, `entryType`, signed `amount`, `referenceType`, `referenceId`, `createdAt`. Internal source-event identifiers and sensitive notes are not returned.

### GET `/v1/admin/trip-settlements`

Auth: `SYSTEM_ADMIN`. Query: operator filters plus `operatorId?`, `stuckOnly?`, `severity?`. A stuck row is unresolved `ELIGIBLE` with `activeFailureCode != null`; `HIGH` means failure count `>=3` **or** stuck age `>21 days`.

### POST `/v1/admin/trip-settlements/{settlementId}/settle`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key`: required. Body is empty. Only `PENDING_HOLD|ELIGIBLE` can settle. Response `200` data contains `settlementId`, `tripId`, `operatorId`, `netAmount`, `status`, `settlementMethod: "ADMIN_MANUAL"`, `settledAt`.

Errors: `404 TRIP_SETTLEMENT_NOT_FOUND`; `409 TRIP_SETTLEMENT_ALREADY_SETTLED`; `500 PLATFORM_WALLET_INSUFFICIENT_BALANCE`; idempotency errors. Same-key replay returns the original result; a different manual key losing a concurrent manual/weekly race returns `409 TRIP_SETTLEMENT_ALREADY_SETTLED`.

### GET `/v1/admin/reports/platform?from={from}&to={to}`

Auth: `SYSTEM_ADMIN`. Booking owns the public facade and orchestration; Gateway only proxies and no
service reads another service's database. Booking reads its local earned source and calls the raw
Trip, Parcel, Payment-ledger and Identity endpoints below through Internal JWT.

`from` and `to` are both required RFC 3339 timestamps with UTC offset `Z`; `from < to`; maximum
range is 366 days. Metrics use half-open UTC interval `[from,to)`.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "period": {
      "from": "2026-07-01T00:00:00Z",
      "to": "2026-08-01T00:00:00Z",
      "timezone": "UTC"
    },
    "totals": {
      "completedBookingCount": 120,
      "completedTripCount": 36,
      "deliveredParcelCount": 18,
      "bookingRevenueVnd": 48000000,
      "parcelRevenueVnd": 3200000,
      "netRevenueVnd": 51200000
    },
    "byOperator": [{
      "operatorId": "uuid",
      "operatorName": "Nha xe A",
      "completedBookingCount": 120,
      "completedTripCount": 36,
      "deliveredParcelCount": 18,
      "bookingRevenueVnd": 48000000,
      "parcelRevenueVnd": 3200000,
      "netRevenueVnd": 51200000
    }],
    "generatedAt": "2026-08-01T00:00:01Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-08-01T00:00:01Z" }
}
```

`byOperator` is the union of IDs returned by Booking/Trip/Parcel, sorted by `netRevenueVnd DESC`
then operator ID. Missing Identity summaries remain with `operatorName=null`. Totals must equal the
checked sum of every breakdown row. Parcel and net revenue are signed and may be negative.

Errors: `403 FORBIDDEN`, `422 VALIDATION_ERROR`, `500 REPORT_VALUE_OVERFLOW`,
`503 UPSTREAM_UNAVAILABLE`. A canonical upstream `REPORT_VALUE_OVERFLOW` is propagated as the same
500; timeout, other 5xx, unusable payloads and reconciliation mismatches map to 503. No partial or
stale response is permitted.

### GET `/internal/v1/reports/platform/bookings?from={from}&to={to}`

Auth: Internal JWT only. Raw success payload:

```json
{
  "items": [{
    "operatorId": "uuid",
    "completedBookingCount": 120,
    "bookingRevenueVnd": 48000000
  }]
}
```

Only Booking rows with `status=COMPLETED` and `completedAt` in UTC `[from,to)` contribute.

### GET `/internal/v1/reports/platform/trips?from={from}&to={to}`

Auth: Internal JWT only. Raw success payload:

```json
{
  "items": [{ "operatorId": "uuid", "completedTripCount": 36 }]
}
```

Only Trip rows with `status=COMPLETED` and `completedAt` in UTC `[from,to)` contribute.

### GET `/internal/v1/reports/platform/parcels?from={from}&to={to}`

Auth: Internal JWT only. Raw success payload:

```json
{
  "items": [{
    "operatorId": "uuid",
    "deliveredParcelCount": 18,
    "parcelRevenueVnd": 3200000
  }]
}
```

Only Parcel rows with `status=DELIVERY_CONFIRMED` and `confirmedAt` in UTC `[from,to)` contribute.
Parcel collected amount is signed `depositPaidVnd + balancePaidVnd - refundedAmountVnd` and is never clamped. `forfeitedDepositVnd` is reported separately and is not added a second time.

All three source endpoints validate RFC 3339 UTC half-open ranges. PostgreSQL `SUM(BIGINT)` is
read as NUMERIC and checked per group and total before mapping to Int64. Overflow returns an ADR
0004 error envelope with `500 REPORT_VALUE_OVERFLOW`; internal successes remain raw.

### GET `/v1/admin/platform-wallet`

Auth: `SYSTEM_ADMIN`. Returns `{ platformWalletId, balance, updatedAt }`.

### GET `/v1/admin/platform-wallet/transactions`

Auth: `SYSTEM_ADMIN`. Paged query supports `type?`, `referenceType?`, `from?`, `to?`, `sortBy=createdAt|amount`, `sortDir?`. Items contain transaction identity, direction, positive amount, balance snapshots, reference, note and created time.

### POST `/v1/admin/platform-wallet/adjust`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key`: required.

Request: `{ "type": "CREDIT", "amount": 100000, "note": "Reconciliation correction" }`. `amount` is positive BIGINT VND; `note` is required. Response `200` returns the transaction and new balance. Concurrent DEBIT cannot make balance negative. Errors: `500 PLATFORM_WALLET_INSUFFICIENT_BALANCE`; validation/idempotency errors.

### POST `/v1/admin/operators/{operatorId}/wallet/adjust`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key`: required. Request and response follow platform adjustment but target one OperatorWallet. A DEBIT that would make balance negative returns `402 WALLET_INSUFFICIENT_BALANCE`; unknown operator/wallet returns `404 RESOURCE_NOT_FOUND`.

### GET `/v1/admin/subscription-plans`

Auth: `SYSTEM_ADMIN`. Query: `page?`, `pageSize?`, `includeInactive?`. Returns a paged ADR 0004 envelope.

### POST `/v1/admin/subscription-plans`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: required. Request defines `name`, `description?`, monthly/yearly BIGINT VND prices, all resource limits, and `enableParcel`, `enableShuttle`, `enableRag`. Response `201` returns the created plan. Prices are non-negative multiples of 1,000 VND.

### PATCH `/v1/admin/subscription-plans/{planId}`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: required. Supports mutable plan presentation, prices, limits, module flags, and `isActive`. It never deletes a plan. Response `200` returns the updated plan.

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
  "displayName": "Nguyen Van Tai",
  "avatarUrl": null,
  "role": "DRIVER",
  "operatorId": "uuid",
  "status": "ACTIVE",
  "phone": "+84901234567"
}
```

`operatorId`, `avatarUrl` và `phone` có thể null. Trip DriverSchedule validation yêu cầu `operatorId` khớp operator caller cho `DRIVER`/`ASSISTANT`; Shuttle dispatch còn yêu cầu driver active có `displayName` và `phone` để snapshot vào assignment event.

Error `404` — `RESOURCE_NOT_FOUND`.

### GET `/internal/v1/users/system-admin-recipient-ids`

Auth: valid Internal JWT only. Caller: Notification Service. Never exposed through Gateway.
Identity returns the distinct IDs of non-soft-deleted users whose role is `SYSTEM_ADMIN` and whose
status is `ACTIVE`.

Response `200` is a raw JSON array without an `ApiResponse` success envelope:

```json
["uuid", "uuid"]
```

No active System Admin is a valid raw `200 []`. Ordering is deterministic by user ID. Invalid
Internal JWT returns `401`. No email, phone, name, token, or other PII is returned.

### GET `/internal/v1/users/by-phone?phone={normalizedE164}`

Auth: Internal JWT via `X-Internal-Auth`. Caller: Booking Service. Never exposed through Gateway. The query value must be URI-escaped by the caller and already be canonical Vietnamese E.164 (`^\+84[0-9]{9,10}$`). Identity performs an exact lookup against a non-soft-deleted `users.phone` and returns no PII.

Response `200` is a raw DTO without an `ApiResponse` wrapper:

```json
{ "userId": "uuid" }
```

No match returns HTTP 404 using the standardized internal ADR 0004 error envelope with `error.code = RESOURCE_NOT_FOUND`. Other Identity errors also use the standard error envelope.

Booking maps only the exact Identity 404 `RESOURCE_NOT_FOUND` response to no user and an HTTP-200 empty operator-booking page. Caller-request cancellation propagates unchanged. Identity 401/403, any other 4xx (including a 404 with another or malformed code), 5xx, timeout, circuit-open, transport, or response-deserialization failure becomes the existing FE-facing `502 UPSTREAM_UNAVAILABLE` ADR 0004 error. Retry only transient 5xx/network failures under BSOT §7.6, never 4xx. No phone snapshot or PII duplication is authorized in Booking.

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

### POST `/internal/v1/operators/{operatorId}/quota-allocations`

Auth: Internal JWT. Idempotency-Key: required. Caller: Trip service.

Request:
```json
{
  "resource": "VEHICLES",
  "resourceId": "uuid",
  "periodKey": null
}
```

`periodKey` is required as `yyyy-MM` only for `TRIPS_THIS_MONTH`. The allocation is durable and unique for `(operatorId, resource, resourceId)`; a retry returns the existing allocation. It counts against the limit immediately, preventing concurrent overshoot. There is no distributed transaction with the caller service.

Response `201`: `{ "allocationId": "uuid", "resource": "VEHICLES", "resourceId": "uuid", "periodKey": null }`.

Errors: `402 SUBSCRIPTION_EXPIRED`; `409 SUBSCRIPTION_PAYMENT_PENDING`; `422 SUBSCRIPTION_LIMIT_EXCEEDED`; `422 IDEMPOTENCY_KEY_MISMATCH`; `422 VALIDATION_ERROR`.

### POST `/internal/v1/operators/{operatorId}/quota-allocations/{allocationId}/release`

Auth: Internal JWT. Idempotency-Key: required. Caller: Trip service after its local persistence fails or after a resource is soft-deleted. Releasing an already released allocation is a `200` idempotent no-op. A scheduled Identity reconciliation may release only allocations whose resource is verified absent through the owning service's internal lookup.

### GET `/v1/operator/shuttle-requests`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Tenant lấy từ JWT. Query phân trang theo main Trip.

Response trả `mainTripId`, origin Station, `hardCutoffAt`, tổng pending, các nhóm Booking (`bookingId`, `passengerCount`, `pickupAddress`, `pickupLat`, `pickupLng`, `distanceToStationMeters`, `requestedAt`) và `suggestedBookingOrder`. Gợi ý dùng Haversine, xa nhất trước, hòa thì `requestedAt ASC`; operator có thể đổi thứ tự.

### POST `/v1/operator/shuttle-trips`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` bắt buộc.

```json
{
  "mainTripId": "uuid",
  "driverUserId": "uuid",
  "vehicleId": "uuid",
  "scheduledDepartureTime": "2026-07-13T01:00:00Z",
  "scheduledEndTime": "2026-07-13T02:00:00Z",
  "orderedBookingIds": ["uuid"],
  "notes": "optional"
}
```

Chọn một subset Booking không rỗng. Toàn bộ ticket của một Booking được gán nguyên tử, sức chứa tính theo tổng ticket. Direction và Station được suy ra từ main Trip. `scheduledEndTime` không được sau `departureDateTime - 30 phút`. Driver/vehicle phải active, cùng tenant và không overlap main Trip/ShuttleTrip. Response `201` trả ShuttleTrip cùng số passenger assigned/remaining. Replay cùng idempotency key trả cùng kết quả.

Errors: `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`; `404 VEHICLE_NOT_FOUND`; `404 DRIVER_NOT_FOUND`; `409 SHUTTLE_REQUEST_SET_CHANGED`; `409 SHUTTLE_CAPACITY_EXCEEDED`; `409 SHUTTLE_DRIVER_CONFLICT`; `409 SHUTTLE_VEHICLE_CONFLICT`; `409 SHUTTLE_REQUEST_CUTOFF_PASSED`; `422 VALIDATION_ERROR`.

### Shuttle fields trong Booking

`POST /v1/bookings` và mỗi leg của round-trip nhận optional `shuttlePickup: { address, latitude, longitude }`. Chỉ origin Station active có `supportsShuttle=true` và đủ tọa độ được nhận. Booking dùng `TripSnapshot.departureDateTime` để từ chối request tại/sau T-30 với `409 SHUTTLE_REQUEST_CUTOFF_PASSED`. Khi intent còn active, `edit-pickup` trả `409 SHUTTLE_PICKUP_LOCKED`.

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

`PATCH /v1/operator/stations/{stationId}` updates only `displayNameOverride`,
`counterLocation`, `contactPhone`, and `instructions`. `DELETE` on the same path deactivates
only the OperatorStation mapping. Both mutations require `Idempotency-Key`; linking an inactive
mapping again reactivates it.

### POST `/v1/operator/stops`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active.
Identity validation failures (404, 5xx, transport, circuit-breaker) map to `422 VALIDATION_ERROR` per current BSOT logical-FK rule; non-APPROVED or inactive operators get `403 FORBIDDEN`.

`google_place_id` is an opaque persisted string only; no live Google Maps/Places call in Day 7.

Day 7 does not accept or mutate `shared_suggestion` / `sharedSuggestion`; that write path is deferred.

`DELETE /v1/operator/stops/{id}?replacedByStopId=` is the sole Stop-disable mutation. It is
bodyless, requires `OPERATOR_ADMIN` and a UUID-v4 `Idempotency-Key`, sets `isActive=false`,
preserves `deletedAt`, and leaves historical RouteStop/TripStop rows intact. Replacement is
optional and must be active, same-operator, non-self, and cycle-free. The old synchronous
`STOP_DISABLED_BOOKING_AFFECTED` warning/count behavior is legacy/deprecated for this route;
impact comes only from the asynchronous `booking.stop_disabled.affected` event.

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

### DELETE `/v1/operator/stops/{id}?replacedByStopId=`

Auth: `OPERATOR_ADMIN`; request body: none. `replacedByStopId` is an optional UUID query
parameter. `Idempotency-Key` is required and must be UUID v4. The fingerprint includes the
authenticated actor, method, path, canonical query, and empty raw body. A same-key/same-
fingerprint replay returns the original status and response bytes before current Stop state is
looked up. A same-key/different-fingerprint request returns `422 IDEMPOTENCY_KEY_MISMATCH`.

The first successful request sets `isActive=false`, does not change `deletedAt`, stores the
optional replacement, and publishes exactly one `trip.stop.disabled` Outbox fact. A repeat using
a new key and the same replacement is behavior-idempotent and returns `200`; a different
replacement after disable returns `409 STOP_ALREADY_DISABLED`. A missing/cross-tenant Stop is
masked as `404 STOP_NOT_FOUND`; invalid replacement input is `422 STOP_REPLACEMENT_INVALID`, a
cycle is `422 STOP_REPLACEMENT_CYCLE`, a missing key is `422 IDEMPOTENCY_KEY_REQUIRED`, and a
generic authorization failure is `403 FORBIDDEN`.

Response `200` is exactly `ApiResponse<{ stop: StopDto, warning: null }>`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": { "stop": { "id": "uuid", "isActive": false }, "warning": null },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-25T10:00:00Z" }
}
```

`warning` is a present JSON property whose value is `null`; `ActiveBookingCount` is omitted.
Booking impact is observed only from `booking.stop_disabled.affected`, never by a synchronous
Booking count call. `PATCH /v1/operator/stops/{id}` remains details-update-only: a PATCH leaves
`isActive` and `deletedAt` unchanged and emits no `trip.stop.disabled` Outbox event; DELETE is not
an alias for PATCH.

### PATCH `/v1/operator/stops/{id}`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: required for PATCH and DELETE Stop mutations.

Write requires caller operator to be `APPROVED` and active.

Identity logical-FK/status validation for this write: Identity 404, Identity 5xx, transport failures, and circuit-breaker failures map to `422 VALIDATION_ERROR`; non-APPROVED or inactive operators get `403 FORBIDDEN`.

Tenant isolation: missing Stop or Stop owned by another operator returns `404 STOP_NOT_FOUND` in the ADR 0004 error envelope.

Coordinates validate latitude in [-90, 90] and longitude in [-180, 180]; invalid `latitude` or `longitude` returns `422 VALIDATION_ERROR`.

Request: partial Stop update.

Response `200`: updated Stop DTO.

### Admin Station and Stop management

`SYSTEM_ADMIN` manages canonical stations through `GET/PATCH/DELETE /v1/admin/stations` and
operator-owned stops through `GET/PATCH/DELETE /v1/admin/stops` (list supports `operatorId?`).
Station delete is an ordinary soft-delete and deactivates OperatorStation mappings; it does not
create a canonical redirect. Stop delete follows the replacement and historical-preservation rules
above.

### PATCH `/v1/admin/stations/{id}`

Auth: `SYSTEM_ADMIN`. The existing request contract remains additive and accepts any non-empty
subset of:

```json
{
  "name": "Ben xe Mien Dong Moi",
  "addressStreet": "501 Hoang Huu Nam",
  "locationId": "uuid",
  "city": "Thu Duc",
  "province": "Ho Chi Minh",
  "latitude": 10.8796,
  "longitude": 106.8142,
  "contactPhone": "02812345678",
  "contactEmail": "contact@example.com",
  "operatingHours": { "mon": "05:00-22:00" },
  "facilities": ["waiting_room", "parking"],
  "supportsShuttle": true,
  "isActive": true
}
```

Coordinates must be supplied as a pair and fall in the normal latitude/longitude ranges. Slug is
deterministically regenerated from `name + city + province`; collision uses a station-ID hash
suffix. A Station already merged into another Station cannot be normalized. Response `200` is the
existing canonical Station DTO in an ADR 0004 envelope. The Station update and
`trip.station.normalized` Outbox event commit atomically.

### POST `/v1/admin/stations/{primaryStationId}/merge`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key` is required.

Request:

```json
{ "duplicateId": "uuid" }
```

Primary must be active, non-deleted and canonical. Duplicate must be non-deleted and canonical;
the IDs must differ. Primary wins `name,slug,city,province`; `addressStreet`, `locationId`,
`contactPhone`, `contactEmail`, `operatingHours` and `facilities` are filled from duplicate only when
the primary value is absent, coordinates are merged as one pair, and
`supportsShuttle = primary OR duplicate`. Trip atomically relinks OperatorStation, Route origin and
destination, AlternativeRoute destination, ShuttleTrip Station and prior redirects. A merge that
would make a Route origin equal destination or violate another domain invariant returns
`409 STATION_MERGE_CONFLICT` with no partial side effect.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "primaryStation": {
      "id": "uuid",
      "name": "Ben xe Mien Dong Moi",
      "slug": "ben-xe-mien-dong-moi",
      "city": "Thu Duc",
      "province": "Ho Chi Minh",
      "supportsShuttle": true,
      "isActive": true
    },
    "duplicateStationId": "uuid",
    "relinkedCounts": {
      "operatorMappings": 2,
      "collapsedOperatorMappings": 1,
      "routeOrigins": 1,
      "routeDestinations": 1,
      "alternativeRoutes": 0,
      "shuttleTrips": 0,
      "flattenedRedirects": 1
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T01:00:00Z" }
}
```

The duplicate is deactivated, soft-deleted and redirected directly to primary. OperatorStation
collisions retain the primary mapping, OR `isActive`, fill only nullable primary config, then remove
the duplicate mapping. The write and `trip.station.merged` Outbox event are one transaction.

Errors: `403 FORBIDDEN`, `404 STATION_NOT_FOUND`, `409 STATION_MERGE_CONFLICT`,
`422 VALIDATION_ERROR`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`,
`IDEMPOTENCY_REQUEST_PENDING`.

### GET `/internal/v1/routes/{routeId}/ownership?operatorId={operatorId}`

Auth: internal service authentication required (`X-Internal-Auth: Bearer <jwt>`). This endpoint
is service-to-service only and is not exposed through Gateway.

Response `200`: raw ownership DTO (successful internal response is not wrapped).

```json
{ "routeId": "uuid", "operatorId": "uuid" }
```

The route must exist, be active, not soft-deleted, and belong to `operatorId`. Missing, inactive,
soft-deleted, and cross-operator routes all return `404 ROUTE_NOT_FOUND` in the ADR 0004 error
envelope so tenant existence is not disclosed. Missing or invalid Internal JWT returns `401`.

### GET `/internal/v1/stations/{id}`

Auth: internal service authentication required (`X-Internal-Auth: Bearer <jwt>`).

Response `200`: raw internal Station resolution DTO (successful internal response is not wrapped).

Active canonical Station:

```json
{
  "id": "uuid",
  "name": "Ben xe Mien Dong Moi",
  "city": "Thu Duc",
  "province": "Ho Chi Minh",
  "latitude": 10.8796,
  "longitude": 106.8142,
  "supportsShuttle": true,
  "isMerged": false,
  "canonicalStationId": "uuid"
}
```

A soft-deleted Station created by merge returns the original identity/profile fields with
`isMerged=true` and the terminal `canonicalStationId`. A Station soft-deleted normally, without a
redirect, returns `404 STATION_NOT_FOUND`, as does an unknown ID. Public Station DTOs never expose
soft-deleted redirect rows.

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

Original Day-9 Vehicle/DriverSchedule create and activate writes do not require
`Idempotency-Key`. The Day-22 full DriverSchedule PATCH and its deprecated `/crew` alias explicitly
require a UUID-v4 key as documented below.

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

### PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key`: required UUID v4. `applyTo` is required and has exactly
the two values shown above.

Request is a partial update with exactly these fields:

```json
{
  "departureTime": "08:30:00",
  "dayOfWeek": [1, 3, 5],
  "driverUserId": "uuid",
  "assistantUserId": null,
  "vehicleId": "uuid",
  "validUntil": null,
  "isActive": true
}
```

`routeId` and `validFrom` are not editable. Omitted means unchanged. Explicit `null` clears only
`assistantUserId`, `vehicleId`, or `validUntil`; `validUntil:null` restores an open-ended window.
Explicit `null` for `departureTime`, `dayOfWeek`, `driverUserId`, or `isActive`, and empty,
unknown-only, or malformed bodies return `422 VALIDATION_ERROR`. Missing/invalid `applyTo` also
returns `422 VALIDATION_ERROR`. Changing `departureTime`/`dayOfWeek` through `ALL_PENDING` is the
only canonical path that cascades a new `departureDateTime` to generated Trips;
`departureDateTime` is absent from the Trip PATCH body and changed-field registry. No dedicated
Trip schedule endpoint or Gateway route exists.

For each actual departure change, compute `delta = |newDeparture - oldDeparture|` and compare the
calendar dates in ICT (`Asia/Ho_Chi_Minh`): MINOR is the same ICT date with `delta <= 2h`; MEDIUM
is the same ICT date with `delta > 2h && delta < 6h`; MAJOR is `delta >= 6h` or any ICT date
change.

Scope behavior:

- `FUTURE_ONLY` changes the recurring schedule and leaves every generated Trip unchanged. If the
  schedule is active and has a vehicle, generation creates only uncovered future dates. It does
  not call Booking because no generated Trip is mutated. With
  `vehicleId:null`, it clears only the schedule vehicle and every attempted date is skipped using
  the existing `TripGenerationSkipLog` reason `OTHER` with a message identifying that no vehicle
  is assigned; no new Trip is generated until a vehicle is assigned.
- `ALL_PENDING` applies the effective schedule values to every linked Trip whose status is
  `SCHEDULED|BOARDING`. `vehicleId:null` is rejected with `422 VALIDATION_ERROR` before any
  Booking call or mutation because `Trip.vehicleId` is required. Removing a day cancels pending
  Trips that no longer match. Shortening `validUntil` or setting `isActive=false` only stops future
  generation and never cancels/mutates an already generated Trip; clearing `validUntil` or
  reactivating may generate uncovered future dates only.

Validation/execution order is fixed: tenant-scoped schedule load (missing/cross-tenant is masked
`404 RESOURCE_NOT_FOUND`) → normalize and compute actual changes → same-value no-op `200` → local
scalar/window and null-vehicle rules → tenant/Identity logical-reference validation →
schedule/vehicle/crew overlap validation → branch by `applyTo`. `ALL_PENDING` deterministically
enumerates `SCHEDULED|BOARDING` Trips and fetches every Booking edit-impact projection before any
write/transaction. Capture `now` once for the full preflight. If any affected Trip has a
`CONFIRMED` Booking and either `oldDeparture - now < 2h` or computed
`newDeparture - now < 2h`, return `409 DRIVER_SCHEDULE_EDIT_TOO_LATE`; exact equality on both sides
is allowed. Vehicle conflicts use `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT` before
`TRIP_VEHICLE_SWAP_TOO_LATE`; route-change conflict does not apply because `routeId` is immutable
here. Only after the full batch preflight succeeds may one transaction open and
lock/reload/revalidate in fixed order: schedule first → Trips ordered by
`(departureDateTime,tripId)` → each Trip's seats → stops, using stable repository ordering, apply
all schedule and Trip cascades, stage audits/Outbox, and save/commit once. Each changed departure
stages `trip.trip.schedule_changed` with exact
`{eventId,occurredAt,tripId,operatorId,oldDeparture,newDeparture,severity}` and preserves
`payload.eventId == outbox_events.id == RabbitMQ MessageId`. Any failure rolls back the entire
batch; no transaction spans HTTP.

Response `200`: the updated `DriverScheduleDto` in the ADR 0004 success envelope. A same-value
request also returns the current DTO but creates no audit, event, or generation work.

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

### PATCH `/v1/operator/driver-schedules/{id}/crew`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key`: required UUID v4. **Deprecated for one release.** This
path is an alias to the canonical full PATCH use case with `applyTo=ALL_PENDING`; it has no second
business handler.

```json
{
  "driverUserId": "uuid",
  "assistantUserId": "uuid"
}
```

The body fields map to the canonical `driverUserId`/`assistantUserId` fields. `assistantUserId:null`
clears. Validation, all-or-nothing cascade, Booking preflight, locks, audit/Outbox, and response
are identical to the canonical endpoint. Cross-path reuse of an Idempotency-Key mismatches because
the fingerprint includes normalized path and query. Response `200`: `DriverScheduleDto`.

For every changed future trip, Trip publishes `trip.trip.crew_changed`; Notification sends a
`TRIP_ASSIGNED` notification to newly assigned crew and `TRIP_ASSIGNMENT_REMOVED` to removed
crew. Unchanged crew members receive no notification.

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

### GET `/v1/driver/trips/{tripId}/route`

Auth: `DRIVER` or `ASSISTANT`. The authenticated JWT `sub` must equal the Trip's
`driverUserId` or `assistantUserId`; the caller cannot supply a user or operator identifier.

Response `200` uses the ADR 0004 success envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "routeId": "uuid",
    "pathPolyline": "encoded-google-polyline-precision-5-or-null",
    "originStation": {
      "stationId": "uuid",
      "name": "Bến xe Miền Đông",
      "latitude": 10.801,
      "longitude": 106.714
    },
    "destinationStation": {
      "stationId": "uuid",
      "name": "Bến xe Đà Lạt",
      "latitude": null,
      "longitude": null
    },
    "stops": [
      {
        "stopId": "uuid",
        "name": "Ngã tư Dầu Giây",
        "latitude": 10.947,
        "longitude": 107.221,
        "orderIndex": 1,
        "estimatedArrivalTime": "2026-07-12T03:30:00Z",
        "allowPickup": true,
        "allowDropoff": true
      }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-12T01:00:00Z" }
}
```

`pathPolyline` is the Route's nullable Google encoded polyline (precision-5) and is returned
without decode/re-encode. `stops` uses the immutable TripStop sequence and is ordered by
`orderIndex ASC`; Station coordinates remain nullable. When `pathPolyline` is null, clients may
draw the available coordinates in origin → stops → destination order. The response contains no
PII or operator-management metadata. Unknown Trip/Route returns `404 TRIP_NOT_FOUND`; an existing
Trip not assigned to the caller returns `403 FORBIDDEN`; malformed `tripId` returns
`422 VALIDATION_ERROR`.

## Day 21 — Trip lifecycle automation

Both lifecycle mutations have no request body. They require an `Idempotency-Key` header whose
value is a UUID v4. The idempotency fingerprint is exactly the HTTP method, normalized request
route/path parameters including `tripId`, authenticated `sub`, and canonical empty-body marker;
the authenticated role is not a fingerprint component. Request authentication and
`tripId`/header/body validation may run before a new key is reserved; those validation failures
are not cached and create no idempotency record. A valid new key is reserved atomically as pending
before the command executes. Trip assignment authorization runs downstream in the handler, and
middleware finalizes its response for replay. A retry with the same key and same fingerprint
returns the original HTTP status and exact ADR 0004 response body after the first request
completes, returns `409 IDEMPOTENCY_REQUEST_PENDING` while it is executing, and returns
`422 IDEMPOTENCY_KEY_MISMATCH` if any fingerprint component differs. Clients reuse the same key
only to retry the same logical mutation and use a new UUID-v4 key for a new attempt. Missing or
malformed keys, malformed `tripId`, or any request body return `422 VALIDATION_ERROR` without
changing Trip state.

### POST `/v1/driver/trips/{tripId}/start`

Auth: `DRIVER` only. The authenticated JWT `sub` must equal the Trip's `driverUserId`; an existing
Trip assigned to another user returns `403 FORBIDDEN`. The request has no body and requires the
idempotency semantics above.

Precondition: Trip status is `BOARDING`. A successful transition sets status to `IN_PROGRESS`,
captures `actualDepartureTime`, and publishes `trip.trip.started` through the Trip Outbox in the
same Trip-local transaction. Any other current status returns `409 TRIP_INVALID_TRANSITION`.

Response `200` uses the ADR 0004 success envelope. Every data field is required and non-null:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "IN_PROGRESS",
    "actualDepartureTime": "2026-06-22T01:30:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-22T01:30:00Z" }
}
```

Data schema: `{ tripId: string(uuid), status: "IN_PROGRESS", actualDepartureTime: string(date-time) }`.

Errors: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`;
`409 TRIP_INVALID_TRANSITION`; `409 IDEMPOTENCY_REQUEST_PENDING`;
`422 IDEMPOTENCY_KEY_MISMATCH`; `422 VALIDATION_ERROR`.

### POST `/v1/driver/trips/{tripId}/complete`

Auth: `DRIVER` or `ASSISTANT`. For `DRIVER`, authenticated JWT `sub` must equal
`trip.driverUserId`; for `ASSISTANT`, it must equal `trip.assistantUserId`. Any role/assignment
mismatch returns `403 FORBIDDEN`. The request has no body and requires the idempotency semantics
above.

Precondition: Trip status is `IN_PROGRESS`. A successful transition sets status to `COMPLETED`,
captures `completedAt` and `completedByUserId` from the caller, appends the
`TRIP_COMPLETED_MANUAL` Trip audit row with metadata `{tripId,role}`, and publishes
`trip.trip.completed` through the Trip Outbox atomically in one Trip-local transaction. It does
not read or write Identity and emits no audit integration event. Any other current status returns
`409 TRIP_INVALID_TRANSITION`.

Response `200` uses the ADR 0004 success envelope. Every data field is required and non-null for
this manual endpoint:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "COMPLETED",
    "completedAt": "2026-06-22T05:30:00Z",
    "completedByUserId": "7226afd8-c107-413f-8235-c39e75f7a71f"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-22T05:30:00Z" }
}
```

Data schema: `{ tripId: string(uuid), status: "COMPLETED", completedAt: string(date-time), completedByUserId: string(uuid) }`.

Errors: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`;
`409 TRIP_INVALID_TRANSITION`; `409 IDEMPOTENCY_REQUEST_PENDING`;
`422 IDEMPOTENCY_KEY_MISMATCH`; `422 VALIDATION_ERROR`.

### POST `/v1/driver/trips/{tripId}/stops/{stopId}/depart`

Auth: assigned `DRIVER` or nullable assigned `ASSISTANT` for the same tenant. The request is
bodyless and requires a UUID-v4 `Idempotency-Key` using the lifecycle fingerprint above. The
first execution is valid only when `Trip.status=IN_PROGRESS`, `TripStop.status=ARRIVED`, and
`TripStop.actualDepartureTime IS NULL`. Trip and TripStop are locked (or an equivalent CAS is
used), the timestamp is persisted, then Trip calls the exact Booking pending-count seam. A
positive count emits one `trip.stop.departed_with_pending` Outbox event; zero emits no event.

Response `200` uses the public ADR 0004 envelope and data is exactly `{ tripId, stopId, departedAt, pendingPassengerCount, eventEmitted }`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "stopId": "uuid",
    "departedAt": "2026-06-25T10:00:00Z",
    "pendingPassengerCount": 2,
    "eventEmitted": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-25T10:00:00Z" }
}
```

Same-key/same-fingerprint replays the original status/body bytes before current-state lookup;
same-key/different-fingerprint returns `422 IDEMPOTENCY_KEY_MISMATCH`; a valid new key after
departure returns `409 TRIP_STOP_ALREADY_DEPARTED`. A Trip outside `IN_PROGRESS` returns the
existing `422 TRIP_NOT_IN_PROGRESS`; a `PENDING` or `SKIPPED` stop returns
`422 TRIP_STOP_NOT_ARRIVED`; assignment/tenant failure returns `403 FORBIDDEN`; a Booking count
dependency failure returns `502 UPSTREAM_UNAVAILABLE`; invalid/missing idempotency or UUID input
returns `422 VALIDATION_ERROR`/`422 IDEMPOTENCY_KEY_REQUIRED` as applicable.

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

### Day-22 Trip edit and Booking passenger-impact facts

All facts below use exchange `vietride.events`. Routing keys are exact. For the Day-22 vehicle
swap, schedule-change, and schedule-day-removal cancellation flows below, Trip publishes only Trip
domain facts and Booking owns passenger-impact state plus passenger notification facts. This
Day-22 ownership statement does not replace or alter the existing `trip.trip.route_changed`
registry/consumer behavior.

`trip.trip.vehicle_swapped` — producer Trip; consumers Booking and Notification (crew only):

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "tripId": "uuid",
  "operatorId": "uuid",
  "oldVehicleId": "uuid",
  "newVehicleId": "uuid",
  "oldVehiclePlateNumber": "51B-111.11",
  "newVehiclePlateNumber": "51B-222.22",
  "departureDateTime": "2026-07-20T01:00:00Z",
  "driverUserId": "uuid",
  "assistantUserId": null,
  "seatImpacts": [
    { "bookingId": "uuid", "seatNumbers": ["A01"], "reason": "SEAT_REMOVED" }
  ]
}
```

`assistantUserId` is always serialized and may be null. `seatImpacts[].reason` is exactly
`SEAT_REMOVED|SEAT_DISABLED|SEAT_TYPE_DOWNGRADED`; there are no additional seat-impact fields.
Notification uses this Trip fact only for old/new crew vehicle-assignment messaging. Passenger
messaging is produced after Booking creates its own pending action.

`trip.trip.schedule_changed` — producer Trip; consumer Booking only:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "tripId": "uuid",
  "operatorId": "uuid",
  "oldDeparture": "2026-07-20T01:00:00Z",
  "newDeparture": "2026-07-20T03:00:00Z",
  "severity": "MINOR"
}
```

`severity` is exactly `MINOR|MEDIUM|MAJOR`. Notification never consumes this Trip fact directly.

`trip.trip.cancelled` — producer Trip; consumers Booking and Parcel:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "tripId": "uuid",
  "operatorId": "uuid",
  "cancelledAt": "2026-07-15T10:00:00Z",
  "cancelReason": "DRIVER_SCHEDULE_DAY_REMOVED"
}
```

Day-22 day removal uses the exact reason above and `cancelledAt=occurredAt`, while retaining both
fields because they have distinct domain/event meanings. Booking cancels active Bookings:
`PENDING_PAYMENT` emits the existing `booking.booking.cancelled` with `refundAmount=0`;
`CONFIRMED` emits it with a 100% refund of immutable persisted `Booking.totalAmount`. Payment
refunds only from `booking.booking.cancelled`; Payment and Notification do not consume
`trip.trip.cancelled` directly. Parcel separately owns its cancellation reaction.

Booking publishes exactly these four Day-22 passenger facts, all consumed by Notification. The
two schedule facts are emitted only for Bookings in `CONFIRMED`: MINOR emits the informational
fact, while MEDIUM/MAJOR emits the required fact. A Booking in any other status emits neither
schedule fact.

```text
// booking.booking.seat_reassignment_required
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "pendingActionId": "uuid",
  "deadline": "2026-07-15T14:00:00Z",
  "seatNumbers": ["A01"],
  "reason": "SEAT_REMOVED"
}

// booking.booking.schedule_change_informational (MINOR only)
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "oldDeparture": "2026-07-20T01:00:00Z",
  "newDeparture": "2026-07-20T03:00:00Z",
  "severity": "MINOR"
}

// booking.booking.schedule_change_required (MEDIUM/MAJOR only)
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T10:00:00Z",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "pendingActionId": "uuid",
  "deadline": "2026-07-16T10:00:00Z",
  "oldDeparture": "2026-07-20T01:00:00Z",
  "newDeparture": "2026-07-20T08:00:00Z",
  "severity": "MAJOR"
}

// booking.booking.pending_action_realerted — seat-detail variant
{
  "eventId": "uuid",
  "occurredAt": "2026-07-15T12:00:00Z",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "pendingActionId": "uuid",
  "deadline": "2026-07-15T14:00:00Z",
  "reason": "PENDING_SEAT_ASSIGNMENT",
  "seatNumbers": ["A01"],
  "seatImpactReason": "SEAT_REMOVED"
}
```

The re-alert's schedule-detail variant has the same common fields, `reason=SCHEDULE_CHANGE`, and
exact `{oldDeparture,newDeparture,severity}` where severity is `MEDIUM|MAJOR`; it has no seat
detail. The informational event has no `pendingActionId` or `deadline`. Required schedule-change
events always have both pending-action fields. Booking commits pending-action state and initial
Outbox before ensuring a PostgreSQL-backed Hangfire T+2h re-alert schedule.

Logical scheduling dedupe is by `pendingActionId`; retry/redelivery may create multiple physical
Hangfire jobs. Every execution locks and rechecks action existence, unresolved state, and deadline,
then writes a re-alert Outbox identity deterministically derived from `pendingActionId`; only one
side effect can persist and duplicate jobs no-op. The Rabbit delivery contract is Booking DB commit
→ ensure schedule → ACK. A crash before ensure/ACK is repaired by broker/DLQ replay, which loads the
existing action, emits no duplicate initial event, ensures scheduling, and then ACKs. This adds no
table, column, migration, `realertedAt`, custom poller, or package beyond approved
`Hangfire.AspNetCore` and `Hangfire.PostgreSql`.

Day 22 owns fact publication, Booking pending-action creation, and re-alert delivery. Day 23 owns
passenger accept/reject and scheduled resolution: passenger rejection may refund by severity,
while timeout only auto-accepts and never cancels or refunds. Day 22 does not implement those
actions.

### `parcel.parcel.unloaded`

Producer: Parcel. Consumer: Notification. Exchange: `vietride.events`.

```json
{
  "parcelId": "uuid",
  "tripId": "uuid",
  "userIds": ["sender-user-uuid", "recipient-user-uuid"]
}
```

The Parcel-local transaction enqueues this event only for the winning
`IN_TRANSIT -> UNLOADED` CAS. `userIds` is distinct and always includes the sender; it includes
the recipient account when the Parcel has one. A replay or CAS loser emits no event.

### `parcel.parcel.delivered_pending_confirm`

Producer: Parcel. Consumer: Notification. Exchange: `vietride.events`.

```json
{
  "parcelId": "uuid",
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
  "operatorId": "uuid",
  "tripId": "uuid",
  "userId": "recipient-user-uuid",
  "recipientUserIds": ["recipient-user-uuid"],
  "deliveryToken": "uuid",
  "expiresAt": "2026-07-17T08:05:00Z"
}
```

The Parcel-local transaction enqueues this event only for the winning
`UNLOADED -> DELIVERED_PENDING_CONFIRM` CAS. `userId` and `recipientUserIds` are omitted when no
recipient account is linked. `deliveryToken` is generated only by deliver and expires after
48 hours. A replay or CAS loser emits no event.

### `trip.incident.reported`

Producer: Trip. Consumer: Notification. Exchange: `vietride.events`. Optional fields bị omit khi
không có giá trị; consumer chấp nhận cả omitted và `null` trong giai đoạn tương thích.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-16T03:00:00Z",
  "eventType": "trip.incident.reported",
  "incidentId": "uuid",
  "tripId": "uuid",
  "operatorId": "uuid",
  "reporterUserId": "uuid",
  "category": "TRAFFIC_JAM",
  "description": "Kẹt xe tại nút giao",
  "photoUrls": ["https://storage.example/incident-1.jpg"],
  "latitude": 10.7731,
  "longitude": 106.7032,
  "reportedAt": "2026-07-16T03:00:00Z"
}
```

`occurredAt` và `reportedAt` dùng cùng instant từ `IClock`. Payload không chứa recipient IDs;
Notification resolve active `OPERATOR_ADMIN` theo `operatorId`.

### `trip.stop.arrived`

Producer: Trip. Consumers: Parcel, Notification. Exchange: `vietride.events`.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-16T06:00:00Z",
  "eventType": "trip.stop.arrived",
  "tripId": "uuid",
  "stopId": "uuid",
  "operatorId": "uuid",
  "actorUserId": "uuid",
  "actualArrivalTime": "2026-07-16T06:00:00Z"
}
```

`eventId` là identity dedupe của consumer. `occurredAt` và `actualArrivalTime` dùng cùng instant từ
`IClock`; payload không chứa ETA động và không thay đổi static `TripStop.estimatedArrivalTime`.

### `trip.destination.arrived`

Producer: Trip. Consumer: Parcel. Exchange: `vietride.events`.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-16T06:00:00Z",
  "eventType": "trip.destination.arrived",
  "tripId": "uuid",
  "destinationStationId": "uuid",
  "operatorId": "uuid",
  "actorUserId": "uuid",
  "actualArrivalTime": "2026-07-16T06:00:00Z"
}
```

Event chỉ được phát một lần cho mỗi Trip destination anchor. `destinationStationId` được derive từ
Route; không tạo cross-database foreign key đến Identity cho `actorUserId`.

### `trip.trip.assigned` and `trip.trip.crew_changed`

Producer: Trip. Consumer: Notification. Exchange: `vietride.events`.

`trip.trip.assigned` payload:

```json
{
  "tripId": "uuid",
  "operatorId": "uuid",
  "driverUserId": "uuid",
  "assistantUserId": "uuid|null",
  "routeName": "Sài Gòn - Đà Lạt",
  "vehiclePlateNumber": "51B-123.45",
  "departureDateTime": "2026-07-12T01:00:00+00:00"
}
```

`trip.trip.crew_changed` uses the same trip snapshot fields and additionally includes
`oldDriverUserId` and nullable `oldAssistantUserId`. Notification treats routing key plus broker
message ID as its idempotency identity.

### `trip.stop.disabled`

Producer: Trip. Consumer: Booking. Exchange: `vietride.events`. The exact payload is
`{ eventId, occurredAt, eventType, stopId, operatorId, replacedByStopId? }`; `eventType` and the
AMQP routing key are `trip.stop.disabled`. `eventId == OutboxEvent.Id == RabbitMQ MessageId` and
retries reuse that identity. Booking creates STOP_DISABLED actions from this fact; no synchronous
Booking impact/count call is part of the DELETE route.

### `booking.stop_disabled.affected`

Producer: Booking. Consumer: Notification. The exact payload is
`{ eventId, occurredAt, eventType, stopId, replacedByStopId?, recipientUserIds[], affectedBookingCount }`;
the routing key is `booking.stop_disabled.affected`. `recipientUserIds` is explicit and
deduplicated. `eventId == OutboxEvent.Id == RabbitMQ MessageId`; consumer redelivery is deduped by
that identity.

### `booking.booking.stop_disabled_auto_fallback_applied`

Producer: Booking. Consumer: Notification. Exactly one fact is emitted per resolved action:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-06-25T10:00:00Z",
  "eventType": "booking.booking.stop_disabled_auto_fallback_applied",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "pendingActionId": "uuid",
  "disabledStopId": "uuid",
  "affectedField": "PICKUP",
  "fallbackStationId": "uuid",
  "resolvedAction": "AUTO_FALLBACK_DESTINATION"
}
```

`affectedField` is `PICKUP|DROPOFF`; the routing key is
`booking.booking.stop_disabled_auto_fallback_applied`. Identity is
`eventId == OutboxEvent.Id == RabbitMQ MessageId`.

### `booking.booking.route_change_auto_fallback_applied`

Producer: Booking. Consumer: Notification. Exactly one fact is emitted when an unresolved
ROUTE_CHANGE action is processed strictly after its deadline:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-23T02:00:00Z",
  "eventType": "booking.booking.route_change_auto_fallback_applied",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "pendingActionId": "uuid",
  "originalStopId": "uuid",
  "fallbackDestinationStationId": "uuid",
  "shuttleRequired": true,
  "resolvedAction": "AUTO_FALLBACK_DESTINATION"
}
```

Identity is `eventId == OutboxEvent.Id == RabbitMQ MessageId`. This fact does not imply a Booking
status transition or refund.

### `booking.booking.passenger_no_show_marked`

Producer: Booking. Consumer: Notification. One event is emitted for each Booking transition:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-06-25T10:00:00Z",
  "eventType": "booking.booking.passenger_no_show_marked",
  "bookingId": "uuid",
  "tripId": "uuid",
  "userId": "uuid",
  "bookingStatus": "PARTIAL_NO_SHOW",
  "newlyNoShowPassengerIds": ["uuid"],
  "triggerType": "ALONG_ROUTE",
  "pickupStopId": "uuid"
}
```

`bookingStatus` is `NO_SHOW|PARTIAL_NO_SHOW`, `triggerType` is `ALONG_ROUTE|TERMINAL`, and
`pickupStopId` is omitted for terminal pickup. Identity is
`eventId == OutboxEvent.Id == RabbitMQ MessageId` and duplicate EventIds are ignored.

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

The payload contains exactly the fields above. Day 24 owns the durable TripStop departure
command, positive-count emit condition, and Notification consumer. Notification recipients are
the assigned non-null `driverUserId` and `assistantUserId` only; duplicate crew ids are
deduplicated and a null assistant creates no recipient.

### `trip.station.merged`

Producer: Trip. Consumers: Booking and Identity. Exchange: `vietride.events`. Trip commits the
Outbox row in the same transaction as Station merge. Consumer queues are durable and independent.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-16T01:00:00Z",
  "eventType": "trip.station.merged",
  "actorUserId": "uuid",
  "ipAddress": "203.0.113.10",
  "userAgent": "VietRide Admin Web",
  "primaryStationId": "uuid",
  "duplicateStationId": "uuid",
  "primaryBefore": {
    "id": "uuid",
    "name": "Ben xe Mien Dong Moi",
    "slug": "ben-xe-mien-dong-moi",
    "city": "Thu Duc",
    "province": "Ho Chi Minh",
    "latitude": 10.8796,
    "longitude": 106.8142,
    "supportsShuttle": true,
    "isActive": true
  },
  "duplicateBefore": {
    "id": "uuid",
    "name": "BX Mien Dong",
    "slug": "bx-mien-dong",
    "city": "Thu Duc",
    "province": "Ho Chi Minh",
    "latitude": 10.8797,
    "longitude": 106.8141,
    "supportsShuttle": false,
    "isActive": true
  },
  "primaryAfter": {
    "id": "uuid",
    "name": "Ben xe Mien Dong Moi",
    "slug": "ben-xe-mien-dong-moi",
    "city": "Thu Duc",
    "province": "Ho Chi Minh",
    "latitude": 10.8796,
    "longitude": 106.8142,
    "supportsShuttle": true,
    "isActive": true
  },
  "relinkedCounts": {
    "operatorMappings": 2,
    "collapsedOperatorMappings": 1,
    "routeOrigins": 1,
    "routeDestinations": 1,
    "alternativeRoutes": 0,
    "shuttleTrips": 0,
    "flattenedRedirects": 1
  }
}
```

`ipAddress` and `userAgent` are nullable and intended only for immutable Identity audit columns.
Station snapshots are allow-listed and must not contain contact phone/email. Operational logs must
not emit the full event payload, IP or user-agent.

### `trip.station.normalized`

Producer: Trip. Consumer: Identity. Exchange: `vietride.events`. Payload:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-16T01:00:00Z",
  "eventType": "trip.station.normalized",
  "actorUserId": "uuid",
  "ipAddress": null,
  "userAgent": null,
  "stationId": "uuid",
  "before": {
    "id": "uuid",
    "name": "BX Mien Dong",
    "slug": "bx-mien-dong",
    "city": "Thu Duc",
    "province": "Ho Chi Minh",
    "latitude": 10.8797,
    "longitude": 106.8141,
    "supportsShuttle": false,
    "isActive": true
  },
  "after": {
    "id": "uuid",
    "name": "Ben xe Mien Dong Moi",
    "slug": "ben-xe-mien-dong-moi",
    "city": "Thu Duc",
    "province": "Ho Chi Minh",
    "latitude": 10.8796,
    "longitude": 106.8142,
    "supportsShuttle": true,
    "isActive": true
  }
}
```

The same snapshot allow-list and PII-safe logging rules as `trip.station.merged` apply.

## Day 41–43 — Reporting and reliability contract addendum

This addendum is the current contract for the Sprint 6 reporting and reliability scope. It
supersedes the earlier Day-40 platform-owner wording where it conflicts with the rules below.

### Operator XLSX exports

The following six read-only routes are canonical and are proxied by Gateway to the service that
owns the source database:

| Route | Owner | Sheet | Filename prefix |
|---|---|---|---|
| `GET /v1/operator/reports/bookings/export` | Booking | `Bookings` | `bookings-report` |
| `GET /v1/operator/reports/parcels/export` | Parcel | `Parcels` | `parcels-report` |
| `GET /v1/operator/reports/revenue/export` | Payment | `Revenue` | `revenue-report` |
| `GET /v1/operator/reports/occupancy/export` | Trip | `Occupancy` | `occupancy-report` |
| `GET /v1/operator/reports/cancellation/export` | Booking | `Cancellations` | `cancellation-report` |
| `GET /v1/operator/reports/refunds/export` | Payment | `Refunds` | `refunds-report` |

All routes require `OPERATOR_ADMIN` or `OPERATOR_STAFF`. `operatorId` is read only from the
authenticated operator claim; query/body values are ignored and are not accepted. `from` and `to`
are optional ICT dates, inclusive. The default is the last 30 ICT calendar days including `to`;
the maximum is 92 inclusive days. The service converts the range to UTC `[from,to)` and rejects
invalid or oversized ranges with `422 REPORT_RANGE_INVALID`.

Success is a raw file response with media type
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition:
attachment`, and a deterministic filename ending in `.xlsx`. Errors use the ADR 0004 envelope.
Empty ranges still produce a valid workbook. No report contains passenger, sender or recipient
PII. The existing `GET /v1/operator/parcels/reports/export?format=csv` route remains unchanged.

Workbook columns are stable and typed as follows:

- `Bookings`: `booking_id`, `booking_code`, `trip_id`, `status`, `passenger_count`,
  `total_amount_vnd`, `created_at`, `confirmed_at`, `completed_at`.
- `Parcels`: `parcel_id`, `parcel_code`, `trip_id`, `status`, `size_category`,
  `total_price_vnd`, `deposit_amount_vnd`, `additional_amount_vnd`, `refund_amount_vnd`,
  `created_at`, `confirmed_at`.
- `Revenue`: `entry_id`, `entry_type`, `reference_type`, `reference_id`, `trip_id`, `amount_vnd`,
  `occurred_at`, `note`.
- `Occupancy`: `trip_id`, `route_id`, `status`, `departure_at`, `sellable_seat_count`,
  `booked_seat_count`, `occupancy_percent`.
- `Cancellations`: `booking_id`, `booking_code`, `trip_id`, `status`, `cancelled_at`,
  `cancellation_reason`, `total_amount_vnd`.
- `Refunds`: `entry_id`, `entry_type`, `reference_type`, `reference_id`, `trip_id`, `amount_vnd`,
  `occurred_at`, `note`.

Revenue and refund rows come from immutable Payment `OperatorLedgerEntry`. `BOOKING_GROUP`
allocations are read from the existing Payment context and are not duplicated in a new attribution
table. The shared writer uses ClosedXML `0.105.0`, a delete-on-close seekable temp stream and
async row enumeration; it must not materialize a full output byte array or a duplicate full row
list.

### Platform report stabilization

`GET /v1/admin/reports/platform?from=&to=` remains the public route and keeps its UTC `[from,to)`
metric anchors. Booking owns the public facade from Day 42. Booking may call only internal raw
source endpoints; each source reads its own database. Payment remains the authoritative ledger
source for revenue reconciliation. Redis read-through cache keys are
`platform-report:v1:{fromUtc}:{toUtc}` with a 5-minute TTL and exact UTC boundaries.

The facade performs reconciliation before promoting Stats/cache data. A mismatch, downstream
timeout, unavailable source or malformed payload fails the whole request with `503
UPSTREAM_UNAVAILABLE`; no partial or stale totals are returned. Cache entries must include the
contract version and exact range.

Booking, Trip and Parcel each maintain a per-earned-record projection named respectively
`platform_booking_stats`, `platform_trip_stats` and `platform_parcel_stats`. A source-row trigger
updates the projection in the same local transaction, while a five-minute recurring backfill
rebuilds it idempotently from live rows. Every raw internal source request compares projection and
live aggregates for every operator in the exact UTC range. Any count/revenue mismatch returns
`503 UPSTREAM_UNAVAILABLE`; a recent projection timestamp alone never bypasses reconciliation.

`GET /internal/v1/reports/platform/ledger?from=&to=` is Internal-JWT-only and returns the raw
Payment-owned payload `{ "items": [{ "operatorId", "bookingRevenueVnd", "parcelRevenueVnd" }] }`.
It reads immutable `OperatorLedgerEntry` rows in UTC `[from,to)`, uses checked BIGINT aggregation,
and never calls another service. Booking compares every operator revenue pair with the earned live
Booking/Parcel sources before it publishes or caches the composite report.

### Outbox DLQ review

`GET /v1/admin/outbox/dlq` is an Identity-owned `SYSTEM_ADMIN` read-only facade. It aggregates
per-service DLQ sources for Identity, Trip, Booking, Payment, Parcel and Tracking. Query supports
`cursor?`, `pageSize` (1..100), `service?`, `eventType?`, `sortDir?`; the cursor is an opaque
composite of service, terminal timestamp and event id. The success data is:

```json
{
  "items": [{
    "service": "booking", "eventId": "uuid", "eventType": "booking.booking_confirmed",
    "payload": {}, "retryCount": 6, "lastError": "...",
    "createdAt": "2026-07-22T00:00:00Z", "terminalAt": "2026-07-22T00:01:00Z"
  }],
  "nextCursor": null,
  "unavailableServices": []
}
```

If one source is unavailable, the facade returns `200` with `unavailableServices` and the
available items; it does not fabricate totals. DLQ transition occurs after the sixth failed
publish (`retry_count > 5`) and is unique per service/event id. Replay and purge are out of scope
for v1. Payloads are never written to operational logs.

### Internal Hangfire job health

Each Hangfire-owning .NET service exposes a service-local, non-Gateway route
`GET /internal/jobs/status`, protected by Internal JWT. The raw success payload is an array of
`{ jobId, status, lastRun, nextRun, lagSeconds }`. `lagSeconds` is
`max(0, nowUtc - nextRunUtc)` for overdue jobs and `null` when there is no next run or the job is
disabled. The endpoint is read-only and does not alter schedules, readiness or Hangfire dashboard
exposure.
