# VietRide API Contract v1

> Source of truth cho controller/DTO scaffolding. Business rules, status machines, entity rationale nằm trong `SU26SE101_VIETRIDE_technical_context_v7.md`.

## Global Conventions

- Public API prefix: `/v1`.
- Internal service-to-service API prefix: `/internal/v1`, require valid Internal JWT, never exposed publicly through Gateway.
- Auth header for public protected endpoints: `Authorization: Bearer <userAccessToken>`.
- Idempotent write endpoints require `Idempotency-Key: <uuid>` where noted.
- Error response: `ApiResponse` envelope `{ success: false, statusCode, error: { code, message, fields? }, meta: { traceId, timestamp } }` — ADR 0004; `error.code` từ BSOT §5.9 registry (UPPER_SNAKE_CASE). `application/problem+json` (RFC 7807) đã DROP.
- Money fields are VND `number` in JSON, stored as BIGINT in DB.
- FE-facing `/v1/*` JSON HTTP responses and Tracking/Notification WebSocket emissions serialize instant fields as RFC 3339 through IANA `Asia/Ho_Chi_Minh`, ending in the resolved `+07:00` offset. Internal HTTP, Redis/Outbox/RabbitMQ events and persistence serialize the same instant as UTC ending in `Z`. Datetime request values must contain `Z` or an explicit offset and are normalized to UTC; a missing offset returns `422 VALIDATION_ERROR`.
- Calendar fields (`date`, date-only `from`/`to`, `TimeOnly`, `dayOfWeek`, recurring schedules) use `Asia/Ho_Chi_Minh`. Date-only ranges are inclusive Vietnam dates and are queried as UTC half-open ranges.
- Public clients receive the Vietnam representation directly and must parse RFC 3339 values instead of comparing raw strings. Example: public `2026-08-10T12:00:00+07:00` and internal `2026-08-10T05:00:00Z` are the same instant.
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `409` — duplicate email:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_REGISTERED", "message": "Email đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` — wrong OTP code:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Mã xác thực không đúng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` — expired OTP:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_EXPIRED", "message": "Mã xác thực đã hết hạn." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` - unknown email or invalid purpose:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Ma xac thuc khong dung." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `409` - email already verified:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_VERIFIED", "message": "Email da duoc xac minh." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `429` - OTP rate limit exceeded:
```json
{
  "success": false,
  "statusCode": 429,
  "error": { "code": "AUTH_OTP_RATE_LIMIT_EXCEEDED", "message": "Too many OTP requests. Please try again later." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `429` - password reset OTP rate limit exceeded:
```json
{
  "success": false,
  "statusCode": 429,
  "error": { "code": "AUTH_OTP_RATE_LIMIT_EXCEEDED", "message": "Too many OTP requests. Please try again later." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` - wrong OTP code or non-eligible account:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_INVALID", "message": "Ma xac thuc khong dung." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` - expired OTP:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_OTP_EXPIRED", "message": "Ma xac thuc da het han." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

### POST `/v1/auth/change-password`

Auth: any authenticated `ACTIVE` user role with a local password. A suspended Operator's restricted
`OPERATOR_ADMIN` session may call this endpoint. `Idempotency-Key` is required.

Request:
```json
{
  "currentPassword": "OldPassword123!",
  "newPassword": "NewPassword123!"
}
```

The new password must be 8..128 characters with at least one letter and one digit, and must differ
from the current password. On success Identity resets DB/Redis login-lockout counters, revokes every
active refresh token with `PASSWORD_CHANGE`, requests Firebase session revocation with reason
`PASSWORD_CHANGED`, and writes `CHANGE_PASSWORD` ActivityLog. The caller must sign in again.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "userId": "uuid",
    "sessionsRevoked": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors:
- `401 AUTH_INVALID_CREDENTIALS` — current password is wrong or the account is Google-only.
- `422 VALIDATION_ERROR` — new password is weak or matches the current password.
- `422 USER_INVALID_STATUS_TRANSITION` — user is not `ACTIVE`.
- Standard idempotency errors from BSOT §5.6.

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
      "status": "ACTIVE",
      "operatorRegistrationStatus": null,
      "avatarUrl": "https://example.com/avatar.png"
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Passenger accounts may receive the same `200` response while `user.status = "PENDING_EMAIL_VERIFICATION"`.
The mobile FE treats that as a restricted session and prompts email OTP verification from Profile.
`user.avatarUrl` is the stored profile avatar and is omitted when null.
`user.operatorRegistrationStatus` is nullable. It is `APPROVED` for a normal operator session,
`SUSPENDED` for the restricted OPERATOR_ADMIN session, and null for users without operator scope.

Error `401` — invalid credentials:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_INVALID_CREDENTIALS", "message": "Email hoặc mật khẩu không đúng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `403` — unverified email for non-passenger accounts:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "AUTH_EMAIL_NOT_VERIFIED", "message": "Email chưa được xác minh." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `403` — account locked:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "AUTH_ACCOUNT_LOCKED", "message": "Tài khoản đã bị khóa." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `403` — OPERATOR_ADMIN/OPERATOR_STAFF belongs to an operator that is not currently `APPROVED`:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Nhà xe chưa được phép truy cập hệ thống." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

For operator-scoped roles, `PENDING` and `REJECTED` continue to return `403 FORBIDDEN`.
When the Operator is `SUSPENDED`, only an `ACTIVE` `OPERATOR_ADMIN` receives a restricted token
bundle with `user.operatorRegistrationStatus = "SUSPENDED"`. `OPERATOR_STAFF`, `DRIVER`, and
`ASSISTANT` receive `403 OPERATOR_SUSPENDED`. A `LOCKED` User always receives
`403 AUTH_ACCOUNT_LOCKED`, even when its Operator is also suspended.

Operator access tokens carry `operatorStatus=APPROVED|SUSPENDED`; the Gateway forwards it in the
Internal JWT. A suspended OPERATOR_ADMIN token is limited to `GET /v1/operator/profile`,
`GET /v1/operator/subscription`, `POST /v1/auth/refresh`, and `POST /v1/auth/logout`. Every other
route returns `403 OPERATOR_SUSPENDED`. After strict rollout, an operator token missing the claim
returns `401 AUTH_TOKEN_INVALID`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Refresh applies the same User/Operator matrix as password and Google login. A refresh token
revoked by operator suspension returns `401 AUTH_TOKEN_INVALID`. A newly issued restricted
OPERATOR_ADMIN refresh token may rotate and preserves `operatorRegistrationStatus=SUSPENDED`.
`LOCKED` returns `403 AUTH_ACCOUNT_LOCKED`; suspended non-admin roles return
`403 OPERATOR_SUSPENDED`.

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
      "status": "ACTIVE",
      "operatorRegistrationStatus": null,
      "avatarUrl": "https://example.com/avatar.png"
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Google login returns the same stored `UserSummaryDto.avatarUrl` as password login. The provider
avatar seeds a newly created User only; linking or re-login never overwrites an existing stored
avatar. The property is omitted when null.
Google login applies the same independent User lock and Operator suspension matrix as password
login and returns the same nullable `user.operatorRegistrationStatus` field.

Error `401` — invalid Google ID token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_GOOGLE_TOKEN_INVALID", "message": "Google ID token signature/expiry/audience invalid." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` — invalid phone format:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_PHONE_INVALID_FORMAT", "message": "Số điện thoại không đúng định dạng." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `409` — duplicate phone:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_PHONE_ALREADY_REGISTERED", "message": "Số điện thoại đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `422` — phone already set:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Phone already exists and cannot be overwritten from this endpoint." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-23T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` — invalid initial-password token:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_INITIAL_PASSWORD_TOKEN_INVALID", "message": "SET_INITIAL_PASSWORD token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `400` — expired initial-password token:
```json
{
  "success": false,
  "statusCode": 400,
  "error": { "code": "AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED", "message": "SET_INITIAL_PASSWORD token đã hết hạn." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `422` — user is not pending initial password:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "USER_INVALID_STATUS_TRANSITION", "message": "User status không cho phép đặt mật khẩu lần đầu." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `422` — invalid device token payload:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Dữ liệu device token không hợp lệ.", "fields": [{ "field": "platform", "message": "platform must be IOS, ANDROID, or WEB." }] },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `422` — invalid device token payload:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "VALIDATION_ERROR", "message": "Dữ liệu device token không hợp lệ.", "fields": [{ "field": "fcmToken", "message": "fcmToken is required." }] },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
| `PARCEL_EVIDENCE_PHOTO` | `ASSISTANT` | `parcel-ops/{operatorId}/{userId}/` |
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-20T17:00:00+07:00" }
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
    "expiresAt": "2026-06-08T17:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `401` — missing or invalid token:
```json
{
  "success": false,
  "statusCode": 401,
  "error": { "code": "AUTH_TOKEN_INVALID", "message": "Token không hợp lệ." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `403` — caller is not an operator admin, cross-operator target, or caller Operator is not currently `APPROVED`:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Bạn không có quyền thực hiện thao tác này." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `404` — target user not found:
```json
{
  "success": false,
  "statusCode": 404,
  "error": { "code": "RESOURCE_NOT_FOUND", "message": "User không tồn tại." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `422` — target user is not pending initial password:
```json
{
  "success": false,
  "statusCode": 422,
  "error": { "code": "USER_INVALID_STATUS_TRANSITION", "message": "Chỉ user ở trạng thái PENDING_INITIAL_PASSWORD mới được gửi lại link đặt mật khẩu." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `403` — caller is not a system admin:
```json
{
  "success": false,
  "statusCode": 403,
  "error": { "code": "FORBIDDEN", "message": "Bạn không có quyền thực hiện thao tác này." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Error `409` — duplicate email:
```json
{
  "success": false,
  "statusCode": 409,
  "error": { "code": "AUTH_EMAIL_ALREADY_REGISTERED", "message": "Email đã được đăng ký." },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
      "createdAt": "2026-07-16T08:00:00+07:00",
      "updatedAt": "2026-07-16T08:00:00+07:00",
      "deletedAt": null
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T08:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T08:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T08:00:00+07:00" }
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
      "createdAt": "2026-07-16T08:00:00+07:00"
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T08:00:00+07:00" }
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
[
  {
    "operatorId": "uuid",
    "operatorName": "Nha xe A",
    "logoUrl": "https://example.test/logo.jpg",
    "contactPhone": "0900000000"
  }
]
```

`logoUrl` and `contactPhone` are nullable additive fields; `operatorName` is preserved for existing
Booking/Payment clients.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-13T08:00:00+07:00" }
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
| `date` | `YYYY-MM-DD`? | null | Calendar day in `Asia/Ho_Chi_Minh` (Asia/Ho_Chi_Minh). Convert local midnight and the next local midnight to the UTC half-open interval `[fromUtc, toUtc)` and filter `trip_current_departure`. Invalid dates return `422 VALIDATION_ERROR`. |
| `passengerPhone` | string? | null | Trim outer whitespace, then apply `PhoneNumber.Normalize`: accept only local `0xxxxxxxxx`/`0xxxxxxxxxx` or canonical `+84xxxxxxxxx`/`+84xxxxxxxxxx`; canonicalize local input to E.164. Internal spaces, hyphens, parentheses, or other separators are invalid and are not stripped. |
| `bookingCode` | string? | null | Trimmed, non-empty, maximum 30 characters, exact case-insensitive match. |
| `search` | string? | null | Maximum 255 characters. OR-search across booking code and buyer snapshot `BuyerDisplayName`; when the input is a valid phone it also exact-matches normalized `BuyerPhone`. This is buyer search, not per-passenger PII. |
| `page` | integer | `1` | Must be `>= 1`. |
| `pageSize` | integer | `20` | Must be `>= 1`; values above 100 are clamped to 100. |
| `sortBy` | string | `createdAt` | Allow-list: `createdAt`, `departureAt`, `bookingCode`, `status`, `totalAmount`; otherwise `400 INVALID_SORT_FIELD`. |
| `sortDir` | string | `desc` | `asc` or `desc`; otherwise `422 VALIDATION_ERROR`. |

`searchIn`, `operatorId`, and `includeDeleted` are not supported. Filters outside the OR fields of `search` combine with it using AND. Every SQL query path first constrains `bookings.operator_id = :claimOperatorId`, before filters and pagination. `sortBy=departureAt` sorts by `trip_current_departure`; there is no `currentDepartureAt` sort key. Sort always adds `id` as the deterministic tie-breaker in the same direction as `sortDir`.

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
      "createdAt": "2026-06-17T19:00:00+07:00"
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-18T08:00:00+07:00" }
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
    "createdAt": "2026-06-17T19:00:00+07:00",
    "seats": [{
      "passengerRecordId": "uuid",
      "ticketId": "uuid",
      "ticketCode": "VT-20260618-ABCDEFGH",
      "seatNumber": "A01",
      "ticketStatus": "CANCELLED",
      "boardingStatus": "PENDING"
    }],
    "statusTimeline": [
      { "status": "PENDING_PAYMENT", "occurredAt": "2026-06-17T19:00:00+07:00", "reasonCode": null },
      { "status": "CANCELLED", "occurredAt": "2026-06-17T19:05:00+07:00", "reasonCode": "USER_INITIATED" }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-18T08:00:00+07:00" }
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
    ],
    "vehicle": {
      "licensePlate": "51B-123.45",
      "vehicleType": { "code": "LIMOUSINE", "displayName": "Limousine" }
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

`vehicle` uses the same DTO as booking/passenger history (`licensePlate` + nullable `vehicleType`). Booking reads it from Trip after the booking is created and fail-opens to `null` if the plate is missing or Trip is unavailable. Create does not invent a vehicle type or plate.

For VNPay, Booking passes the exact Trip seat-lock `expiresAt` as Payment `dueAt`. If the deadline
has already passed during checkout, the request fails with `422 PAYMENT_DEADLINE_PASSED` and
Booking runs its existing seat-release compensation.

Mobile VNPay requests must additionally send `"paymentReturnMode":"MOBILE_SDK"`. A pending
VNPay response includes `paymentReturnMode`, plus
`vnpaySdk: { tmnCode, scheme, isSandbox }`; `paymentId` is the session id used by the app to poll
`GET /v1/payments/sessions/{paymentId}`. Missing mode returns
`426 MOBILE_APP_UPDATE_REQUIRED`; any mode other than `MOBILE_SDK` returns
`422 PAYMENT_RETURN_MODE_INVALID`. While the Mobile channel rollout flag is off, VNPay checkout
returns `503 VNPAY_MOBILE_SDK_DISABLED` and never falls back to the legacy bridge.

Errors include the existing validation/not-found/conflict responses plus:

- `502 UPSTREAM_UNAVAILABLE`: the Trip crew snapshot is unavailable or the Trip has no assigned Driver. Booking returns this before locking seats or creating a payment.

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
  "paymentMethod": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Response `201`:
```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "bookingGroupId": "uuid",
    "outbound": { "bookingId": "uuid", "bookingCode": "VR-20260518-ABCD1234", "totalAmount": 350000, "discountAmount": 50000, "tickets": [{ "ticketId": "uuid", "ticketCode": "VT-20260518-ABCDEFGH", "seatNumber": "A01", "status": "PENDING_PAYMENT", "fareAmount": 400000, "discountAmount": 50000, "paidAmount": 350000 }], "vehicle": { "licensePlate": "51B-123.45", "vehicleType": { "code": "LIMOUSINE", "displayName": "Limousine" } } },
    "return": { "bookingId": "uuid", "bookingCode": "VR-20260519-EFGH5678", "totalAmount": 350000, "discountAmount": 50000, "tickets": [{ "ticketId": "uuid", "ticketCode": "VT-20260519-HGFEDCBA", "seatNumber": "A01", "status": "PENDING_PAYMENT", "fareAmount": 400000, "discountAmount": 50000, "paidAmount": 350000 }], "vehicle": { "licensePlate": "51C-678.90", "vehicleType": { "code": "SLEEPER", "displayName": "Sleeper" } } },
    "grandTotal": 700000,
    "paymentId": "uuid",
    "status": "PENDING_PAYMENT",
    "paymentRedirectUrl": "https://vnpay.vn/...",
    "paymentReturnMode": "MOBILE_SDK",
    "vnpaySdk": { "tmnCode": "merchant-code", "scheme": "vietride", "isSandbox": false }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Rules:
- `paymentMethod=WALLET` is an all-or-nothing checkout across both legs. On success, each leg still has its own Payment record with `referenceType=BOOKING`; the client must never observe a retained first-leg debit if the second leg fails.
- `paymentMethod=VNPAY` may use a combined checkout with `referenceType=BOOKING_GROUP` and one redirect for `grandTotal`.
- `BOOKING_GROUP` is VNPay-only for this endpoint; WALLET success remains two per-booking payments.
- `paymentRedirectUrl` is `null` for WALLET and populated only when VNPay returns a redirect.
- VNPay `Payment.dueAt` is the earlier exact `expiresAt` of the outbound and return seat locks.

Errors include the existing validation/not-found/conflict responses plus:

- `502 UPSTREAM_UNAVAILABLE`: either leg's Trip crew snapshot is unavailable or has no assigned Driver. Booking returns this before the atomic round-trip seat lock or any payment side effect.

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
        "createdAt": "2026-05-01T16:00:00+07:00",
        "departureDateTime": "2026-05-18T08:00:00+07:00",
        "originName": "Bến xe Miền Đông",
        "destinationName": "Bến xe Mỹ Đình",
        "totalAmount": 350000,
        "bookingGroupId": null,
        "tripDirection": null,
        "routeName": "TP.HCM - Hà Nội",
        "pickupPoint": {
          "type": "STOP",
          "id": "pickup-stop-uuid",
          "displayName": "Điểm đón C",
          "address": null,
          "plannedAt": "2026-05-18T10:00:00+07:00"
        },
        "dropoffPoint": {
          "type": "STATION",
          "id": "destination-station-uuid",
          "displayName": "Bến xe Mỹ Đình",
          "address": null,
          "plannedAt": "2026-05-18T20:00:00+07:00"
        },
        "tickets": [
          {
            "ticketId": "uuid",
            "ticketCode": "VT-20260518-ABCDEFGH",
            "seatNumber": "A01",
            "status": "ISSUED",
            "paidAmount": 350000
          }
        ],
        "shuttleRequests": [
          {
            "direction": "INBOUND_TO_STATION",
            "address": "12 Nguyễn Huệ, Quận 1, TP.HCM",
            "latitude": 10.7731,
            "longitude": 106.7032,
            "roadDistanceMeters": 3200,
            "isActive": true,
            "requestedAt": "2026-05-01T16:00:00+07:00",
            "cancelledAt": null
          }
        ],
        "vehicle": {
          "licensePlate": "51B-123.45",
          "vehicleType": {
            "code": "LIMOUSINE",
            "displayName": "Limousine"
          }
        },
        "paymentRedirectUrl": null
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Validation failures return `422 VALIDATION_ERROR`.

`vehicle` is always serialized and is either
`{ "licensePlate": string, "vehicleType": { "code": string, "displayName": string } | null }`
for the Trip's current Vehicle or `null`. System-defined and operator-custom Vehicle Types use the
same public shape. Booking trims all three strings and emits `vehicleType` only when both `code`
and `displayName` are non-blank. During rolling deployment, an older Trip response without
`vehicleType`, or a malformed partial/blank type, preserves the valid plate and returns
`vehicleType=null`. Booking performs one distinct-ID call to
`POST /internal/v1/trips/summaries/batch` for each non-empty history page (up to 100 Trips), so a
vehicle swap is reflected without an N+1 lookup. Missing Trip summaries, blank/malformed plates,
non-success responses, timeouts, and transport failures leave only the affected enrichment null
and do not fail the base history response. No vehicle ID, status, seat layout, capacity, image, or
other management field is exposed.

`shuttleRequests` is always serialized by this public endpoint and is `[]` when the Booking never
requested Shuttle service. Each item is Booking-owned request history with `direction` equal to
`INBOUND_TO_STATION` or `OUTBOUND_FROM_STATION`, the requested service address and coordinates,
nullable road-distance snapshot, current `isActive`, `requestedAt`, and nullable `cancelledAt`.
Both active and inactive intents are returned so cancellation does not erase passenger-visible
history. Items are ordered by `requestedAt ASC, id ASC`. This projection does not enrich Trip-owned
assignment data such as `shuttleTripId`, Vehicle, Driver, pickup order, or dispatch status.

`originName`, `destinationName`, and `routeName` are immutable Route metadata; they do not identify
the passenger's selected travel leg. `pickupPoint` and `dropoffPoint` are the Booking-owned selected
point snapshots. Each non-null point contains `type=STATION|STOP`, its canonical point `id`, nullable
`displayName` and `address`, and nullable `plannedAt`. New and edited Bookings snapshot these values
from the validated Trip response in the same Booking transaction. History never resolves point names
from the current Trip. Legacy rows that predate point snapshots return the affected point as `null`
rather than substituting current Trip data or Route endpoint names.

`paymentRedirectUrl` is the final root property of every item and is always serialized. It is
non-null only for a `PENDING_PAYMENT` Booking whose latest eligible VNPay Payment lookup matches
the owner, reference, exact amount, trusted VNPay authority, and a persisted future `dueAt`.
One-way uses `BOOKING/bookingId`; round-trip uses `BOOKING_GROUP/bookingGroupId` and exact
authoritative group net total.
Payment lookup non-200, malformed payload, or transport failure leaves the field null without
failing the base history response.

### GET `/internal/v1/bookings/history`

Auth: Internal JWT. Caller: Parcel Service. Never exposed through Gateway.

Query: required `userId`, plus the same `status?`, `from?`, `to?`, `page=1`, and `pageSize=20`
semantics as the public Booking history endpoint. It preserves Booking ownership, per-Booking
pagination, nested Ticket summaries, nullable current Vehicle projection, and deterministic
ordering. It does not load or return the public-only `shuttleRequests` field, so Parcel does not
receive passenger Shuttle addresses or coordinates. It does return the same Booking-owned
`pickupPoint` and `dropoffPoint` snapshot contract as public Booking history so the Passenger History
facade does not need to resolve mutable Trip data.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
        "totalCancellations": 4,
        "totalNoShows": 2,
        "totalPartialNoShows": 1,
        "totalCompleted": 113
      }
    ],
    "totalBookings": 120
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
        "totalCancellations": 4,
        "totalNoShows": 2,
        "totalPartialNoShows": 1,
        "totalCompleted": 113
      }
    ],
    "totalBookings": 120
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

### GET `/v1/admin/vouchers`

> Platform voucher list only: returns vouchers where `ownerOperatorId = null`. Read-only — no Idempotency-Key.

Auth: `SYSTEM_ADMIN`.

Query: `fundingType?` (`VIETRIDE_FUNDED` | `OPERATOR_FUNDED`), `isActive?` (bool), `search?` (case-insensitive contains on code/name), `service?` (`BOOKING|PARCEL`, array membership), plus standard `QueryOptions` paging/sort (`page`/`pageSize` clamped 1..100, `sortBy` whitelisted — default `createdAt` `desc`; non-whitelisted → `400 INVALID_SORT_FIELD`). `ownerOperatorId` is not supported on this endpoint and must not expose operator-owned vouchers. `applicableServices` in each item contains `BOOKING`, `PARCEL`, or both. v1 returns only active (non-soft-deleted) vouchers (respects EF `HasQueryFilter(deleted_at == null)`); `includeDeleted` not supported in v1.

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
  "validFrom": "2026-05-31T17:00:00Z",
  "validUntil": "2026-08-31T16:59:59Z",
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

Query: `isActive?` (bool), `search?` (case-insensitive contains on code/name), `service?` (`BOOKING|PARCEL`, array membership), plus standard `QueryOptions` paging/sort (`page`/`pageSize` clamped 1..100, `sortBy` whitelisted — default `createdAt` `desc`; non-whitelisted → `400 INVALID_SORT_FIELD`). No `fundingType` query in v1. v1 returns only non-soft-deleted vouchers.

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
  "validFrom": "2026-05-31T17:00:00Z",
  "validUntil": "2026-08-31T16:59:59Z",
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

Purpose: FE loads the current two-level administrative catalog for a cascading province/ward
origin/destination selector. FE must not hardcode administrative units.

Query:
- Omit `parentCode` to list active `PROVINCE|MUNICIPALITY` roots.
- Send `parentCode=<official-province-code>` to list its active `WARD|COMMUNE|SPECIAL_ZONE` children.
- `type?` accepts `PROVINCE|MUNICIPALITY|WARD|COMMUNE|SPECIAL_ZONE`.
- `search?` filters code/name inside the selected level for autocomplete using case- and
  Vietnamese-accent-insensitive contains matching (`Vung Tau` matches `Vũng Tàu`).

`type`, `parentCode`, and `search` are combined with AND semantics. Omitting `type` preserves the
combined response. An unsupported type returns `422 VALIDATION_ERROR`; a supported type that is
not present at the selected level returns `200` with an empty array.

Response `200`: matching active locations sorted by `sortOrder`, then `name`.
```json
{
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "id": "uuid",
      "code": "26506",
      "name": "Phường Vũng Tàu",
      "type": "WARD",
      "parentId": "uuid",
      "parentCode": "79",
      "parentName": "Thành phố Hồ Chí Minh",
      "isActive": true,
      "sortOrder": 2563
    }
  ],
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-08T07:00:00+07:00" }
}
```

### Admin Location APIs

Auth: `SYSTEM_ADMIN`.

Endpoints:
- `GET /v1/admin/locations?page=&pageSize=&search=&isActive=&type=&parentCode=`
- `POST /v1/admin/locations`
- `PATCH /v1/admin/locations/{id}`
- `DELETE /v1/admin/locations/{id}` soft-deactivates the location.

Admin `search` uses the same case- and Vietnamese-accent-insensitive code/name matching as the
public catalog. `type` accepts `PROVINCE|MUNICIPALITY|WARD|COMMUNE|SPECIAL_ZONE`. `parentCode`
returns direct children of an existing top-level location, including when that parent is inactive;
an absent or non-top-level parent returns `422 VALIDATION_ERROR`. All filters combine with AND and
run before count/paging.

Create/update request:
```json
{
  "code": "26506",
  "name": "Phường Vũng Tàu",
  "type": "WARD",
  "parentCode": "79",
  "sortOrder": 5,
  "isActive": true
}
```

Rules:
- `code` is a unique official numeric string; leading zeroes are significant.
- Root `type` is `PROVINCE|MUNICIPALITY` and forbids `parentCode`.
- Leaf `type` is `WARD|COMMUNE|SPECIAL_ZONE` and requires an active root `parentCode`.
- Duplicate code returns `409 LOCATION_CODE_CONFLICT`.
- Missing/inactive location references in station/stop/trip search validation return `422 VALIDATION_ERROR`.

### GET `/v1/trips/search`

Auth: optional/passenger.

Query:
- Specific station mode: `originStationId`, `destinationStationId`, `departureDate`, `passengerCount`, `allowAlongRoutePickup?`.
- Administrative hierarchy mode: `originProvinceCode`, `originWardCode?`,
  `destinationProvinceCode`, `destinationWardCode?`, `departureDate`, `passengerCount`,
  `allowAlongRoutePickup?`.

New callers should use `originLocationCode?` and `destinationLocationCode?` in hierarchy mode.
The legacy `originWardCode?` and `destinationWardCode?` remain supported. Both naming sets accept
any active leaf type: `WARD`, `COMMUNE`, or `SPECIAL_ZONE`. If both names for one side are supplied,
their trimmed codes must match or the API returns `422 VALIDATION_ERROR`.

If both station IDs and hierarchy codes are sent, a complete station-ID pair wins because it is the more
specific filter. Station-ID mode continues to match exact Route origin/destination terminals.
Hierarchy mode requires both province codes. Each optional ward code must be an active leaf directly
under its corresponding province. Omitting a ward expands the scope to every active leaf directly
under that province. It automatically matches both active Stations and active, non-deleted Stops in
the resolved scopes. A Stop pickup requires the TripStop snapshot `allowPickup=true`; a Stop dropoff
requires `allowDropoff=true`; at least one matched pickup must occur before a matched dropoff by
journey `orderIndex`. `allowAlongRoutePickup` is retained as a deprecated compatibility parameter
and no longer enables/disables this matching. Response keeps concrete Route origin/destination
Stations and additionally returns the requested-location pickup/dropoff points.

Errors:
- `422 VALIDATION_ERROR` if neither a complete station pair nor a complete province pair is provided.
- `422 VALIDATION_ERROR` if a province is missing/inactive/not top-level, or a ward is
  missing/inactive/not a leaf/not a direct child of the selected province.
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
        "pickupPoints": [
          {
            "type": "STATION",
            "stationId": "uuid",
            "stopId": null,
            "name": "Bến xe Miền Đông",
            "address": "292 Đinh Bộ Lĩnh",
            "orderIndex": 0,
            "estimatedTime": "2026-05-18T08:00:00+07:00",
            "allowPickup": true,
            "allowDropoff": false
          }
        ],
        "dropoffPoints": [
          {
            "type": "STOP",
            "stationId": null,
            "stopId": "uuid",
            "name": "Điểm trả Cầu Giấy",
            "address": "Cầu Giấy, Hà Nội",
            "orderIndex": 3,
            "estimatedTime": "2026-05-18T19:30:00+07:00",
            "allowPickup": false,
            "allowDropoff": true
          }
        ],
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Only points in the requested origin/destination Locations that participate in at least one valid
pickup-before-dropoff journey are returned. Point identity is XOR: `STATION` sets only
`stationId`; `STOP` sets only `stopId`. Arrays are ordered by `orderIndex`, then point id.

### GET `/v1/trips/{tripId}`

Auth: protected.

Response `200`: trip detail with route, stations, stops, seat summary, fare summary.

Trip detail includes nullable `alternativeRouteId` and `destinationArrivedAt` (ISO-8601 datetime
with offset). `alternativeRouteId` is `null` until an operator applies an AlternativeRoute.
When it is non-null, `destinationStation`, `estimatedArrivalTime`, and the pending portion of
`stops` describe that effective AlternativeRoute. Already-`ARRIVED` stop snapshots remain at the
front of `stops` as immutable operational history. `destinationArrivedAt` is `null` until the
assigned Driver/Assistant records physical arrival at the effective destination terminal.
Trip detail also includes `plannedEtaQuality=TRAFFIC_AWARE|ROUTE_BASED|FALLBACK`. This is a public quality
classification only; the internal route provider/source is not exposed. `Trip.estimatedArrivalTime`
and every stop `estimatedArrivalTime` are planned timestamps calculated by the backend.
Persisted historical source `GOOGLE_ROUTES` maps to `TRAFFIC_AWARE`, current `GOONG` maps to
`ROUTE_BASED`, and `ROUTE_BASELINE` maps to `FALLBACK`.

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
    "aisles": [{ "afterCol": 2 }],
    "seats": [
      { "seatNumber": "A01", "status": "AVAILABLE", "type": "SLEEPER_LOWER", "row": 1, "col": 1, "deck": 1, "disabledReason": null }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Trip seat-map geometry is an immutable layout snapshot captured when the Trip is created.
Changing the current Vehicle template does not change an existing Trip seat-map. The snapshot
may change only through the documented vehicle substitution/swap flows. `aisles` always exists;
it is `[]` when the captured layout has no aisle. `afterCol` places the aisle after that seat column
for every deck.

### POST `/v1/operator/trips/{tripId}/seats/{seatNumber}/disable`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` is required. The Trip must belong to the operator in
the JWT. Request body: `{ "reason": "Ghế hỏng nội thất" }`.

The transition is `AVAILABLE -> UNAVAILABLE`; `HELD` and `BOOKED` seats return
`409 TRIP_SEAT_IN_USE`. The response is `200 ApiResponse<TripSeatMapDto>` with the latest map,
including nullable `disabledReason` on every seat. Audit is stored in the same local
transaction; no RabbitMQ event is published.

### POST `/v1/operator/trips/{tripId}/seats/{seatNumber}/enable`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` is required. The transition is
`UNAVAILABLE -> AVAILABLE` and clears `disabledReason`; enabling an `AVAILABLE` seat returns
`409 TRIP_SEAT_STATE_CONFLICT`. Both endpoints mask a cross-tenant Trip as
`404 TRIP_NOT_FOUND`; an absent passenger seat (including `DRIVER_AREA`) is
`404 TRIP_SEAT_NOT_FOUND`. Missing/invalid disable reason is `422 VALIDATION_ERROR`.

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
  "departureDateTime": "2026-05-18T01:00:00Z",
  "actualDepartureTime": null,
  "estimatedArrivalTime": "2026-05-18T13:00:00Z",
  "baseFare": 400000,
  "totalDistanceKm": 1700.0,
  "originStation": { "id": "uuid", "name": "Bến xe Miền Đông" },
  "destinationStation": { "id": "uuid", "name": "Bến xe Mỹ Đình" },
  "stops": [
    {
      "stopId": "uuid",
      "name": "Ngã tư Hàng Xanh",
      "isActive": true,
      "orderIndex": 1,
      "allowPickup": true,
      "allowDropoff": false,
      "status": "PENDING",
      "actualArrivalTime": null,
      "estimatedArrivalTime": "2026-05-18T02:30:00Z",
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
- `totalDistanceKm` and each `stops[].distanceFromOriginKm` are nullable. Day-35 proportional
  refund uses the distance path only when the Booking pickup distance, reached-stop distance, and
  Trip total distance needed by the formula are all present. Missing or invalid required distance
  input falls back to the stop-order formula. When those inputs are present but
  `totalDistanceKm - pickupDistance <= 0`, the explicit Technical Context edge rule wins:
  `traveledRatio = 0`; this case does not enter the order fallback.
- The legacy fields above are returned whether `pricingAt` is present or omitted. When
  `pricingAt` is present, the response additionally includes `originalBaseFare`,
  `surchargePercent`, `surchargeAmount`, nullable `surchargePeriodId` /
  `surchargePeriodName`, and the corresponding original/surcharge breakdown on each stop.
  These additive pricing fields are omitted when `pricingAt` is absent so existing operational
  snapshot callers keep the legacy wire shape.
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
  ordered by `orderIndex`; `status` is exactly `PENDING|ARRIVED|SKIPPED`;
  `allowPickup` / `allowDropoff` drive Day-13 pickup/dropoff validation. An unknown status makes
  the operational snapshot malformed and downstream Day-35 consumption fails closed for retry/DLQ.
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
- Every `stops[]` item also exposes nullable `actualDepartureTime` as an additive UTC datetime.
  It is the authoritative proof that the vehicle left that intermediate stop.
- Errors: `404 TRIP_NOT_FOUND`.

### POST `/internal/v1/trips/{tripId}/lock-seats`

Auth: Internal JWT. Idempotency: required (replay with same `Idempotency-Key` returns the
same `seatLockToken`). **All-or-nothing** — if any requested seat is not `AVAILABLE`, no seat
is locked.

Round-trip confirmation uses `POST /internal/v1/trips/round-trip/book-seats` with outbound
and return legs (`tripId`, `seatLockToken`, `bookingId`, `passengerSeatAssignments`). Trip
validates ownership of both locks before changing either leg, persists each leg's `bookingId`
as the owner of its `BOOKED` rows, and commits both legs atomically.

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
    "expiresAt": "2026-05-18T01:10:00Z"
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
      "expiresAt": "2026-05-18T01:10:00Z"
    },
    "return": {
      "tripId": "uuid",
      "seatLockToken": "uuid",
      "lockedSeats": ["A01", "A02"],
      "expiresAt": "2026-05-18T01:10:00Z"
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
`HELD → AVAILABLE` and clears their Redis locks. This endpoint never releases `BOOKED` seats;
confirmed-booking cancellation uses the ownership-checked `booking.booking.cancelled` consumer.

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
(not expired) and owns the seats, then flips them `HELD → BOOKED` and persists the request
`bookingId` as the seat owner. A retry can no-op only for the same booking owner.

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

On `booking.booking.cancelled` with `previousStatus=CONFIRMED`, Trip releases only rows matching
`tripId + bookingId` and clears their owner in the consumer transaction. Duplicate delivery is a
no-op; a legacy payload may still cancel shuttle manifests but cannot mutate main-trip seats.
`previousStatus=PENDING_PAYMENT` keeps the synchronous HELD-seat release above.

### POST `/internal/v1/trips/{sourceTripId}/cargo/transfer`

Auth: valid Internal JWT. Caller: Parcel Service. UUID-v4 `Idempotency-Key` is required.

Request is exactly:

```json
{
  "parcelId": "uuid",
  "targetTripId": "uuid",
  "targetState": "RESERVED",
  "allowCapacityOverflow": false
}
```

`targetState` is exactly `RESERVED|LOADED`. The source and target Trip must differ and belong to
the same operator. Trip locks both rows in ascending UUID order, rechecks the source cargo ledger,
then releases source cargo and creates/restores target cargo in one Trip-local transaction.
`RESERVED` always enforces target capacity. `LOADED` may exceed target capacity only when
`allowCapacityOverflow=true` and the target Trip is server-side verified as
`source=VEHICLE_SUBSTITUTION` for the same operator; the caller flag alone is never proof. A
same-key replay returns the original response without moving counters twice.

Response `200` data:

```json
{
  "parcelId": "uuid",
  "sourceTripId": "uuid",
  "targetTripId": "uuid",
  "targetState": "LOADED",
  "weightKg": 12.5,
  "volumeM3": 0.08
}
```

Errors: `401 AUTH_TOKEN_INVALID`, `404 TRIP_NOT_FOUND`, `404 PARCEL_CARGO_NOT_FOUND`,
`409 TRIP_CARGO_TRANSFER_CONFLICT`, `422 TRIP_CARGO_CAPACITY_EXCEEDED`, and
`422 VALIDATION_ERROR`.

**Current-v1 Trip creation boundary:** `POST /v1/operator/trips` is deferred and intentionally
absent from the public API and Gateway inventories. Existing Trip write APIs below mutate Trips
created by DriverSchedule generation or the documented vehicle-substitution flow; this note does
not reserve a callable base-path POST.

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

The preview is available only while the Trip is `SCHEDULED` or `BOARDING`. Booking and Parcel
use the same classifiers later used by execution. Parcel pre-load statuses
`PENDING_OPERATOR_REVIEW|PENDING_PAYMENT|PENDING|PENDING_ADDITIONAL_PAYMENT|RESERVED|CHECKED_IN|PENDING_FINAL_PAYMENT|READY_TO_LOAD`
contribute `max(depositPaidVnd + balancePaidVnd - refundedAmountVnd, 0)` and will be cancelled
with cargo release. `LOADED|IN_TRANSIT` appear in `affectedParcelIds` but contribute zero because
they move to `PENDING_OPERATOR_ACTION` without immediate refund or release. Terminal/replayed
rows are excluded. `grandTotal` is the Booking and Parcel refund sum.

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
          "estimatedArrivalAt": "2026-07-23T08:45:00+07:00"
        },
        {
          "stopId": null,
          "stationId": "00000000-0000-4000-8000-000000000038",
          "stationName": "Destination station",
          "sequence": 2,
          "estimatedArrivalAt": "2026-07-23T11:50:00+07:00"
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

In that same Trip transaction, a successful route change preserves existing `ARRIVED` TripStop
rows, removes every non-arrived TripStop from the previous effective route, and appends `PENDING`
TripStops from the selected AlternativeRoute in route order. A selected-route stop already present
in the preserved arrival history is not duplicated. Planned stop and destination timestamps use
`actualDepartureTime ?? departureDateTime` as their baseline and the AlternativeRoute duration
snapshots. Tracking consumes `trip.trip.route_changed` to invalidate route-stop/geometry caches
and clear stale ETA/off-route/delay state before processing subsequent GPS updates.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

Errors:
- `404 TRIP_NOT_FOUND` when the Trip is missing or belongs to another operator.
- `404 ROUTE_NOT_FOUND` when the AlternativeRoute is missing, inactive, belongs to another
  parent Route, or belongs to another operator.
- `409 TRIP_NOT_EDITABLE` when the Trip lifecycle does not permit a route change.

This direct `OPERATOR_ADMIN` action remains supported. It does not require a proposal. A successful
direct change atomically marks every `PENDING` route-change proposal for the Trip as `SUPERSEDED`
with `resolutionCode=ROUTE_CHANGED_DIRECTLY` before committing the existing
`trip.trip.route_changed` fact.

### Incident report and Operator incident reads

#### POST `/v1/driver/trips/{tripId}/incident`

Auth: assigned `DRIVER` or `ASSISTANT`. Idempotency: required UUID-v4 `Idempotency-Key`.
The existing request contains `category=TRAFFIC_JAM|VEHICLE_BREAKDOWN|ACCIDENT|WEATHER|OTHER`,
optional trimmed `description` up to 500 characters, at most three owned Firebase HTTPS
`photoUrls`, and optional `latitude`/`longitude` supplied together. The Trip must be
`IN_PROGRESS`. Response `201` returns the created incident identity/timestamp.

#### GET `/v1/operator/incidents`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`; tenant scope comes only from the JWT `operatorId`.
Query: `tripId?`, `category?=TRAFFIC_JAM|VEHICLE_BREAKDOWN|ACCIDENT|WEATHER|OTHER`,
`status?=OPEN|RESOLVED`, inclusive Asia/Ho_Chi_Minh `from?`/`to?` dates, `page?`, `pageSize?`.
Pagination defaults to `1/20` and range is `1..100`. Results are ordered by `reportedAt DESC`,
then `incidentId` for deterministic paging.

Response `200` is `PagedResult<OperatorIncidentDto>` in the standard ADR 0004 envelope. The DTO
used by both list and detail is:

```jsonc
{
  "incidentId": "uuid",
  "category": "VEHICLE_BREAKDOWN",
  "description": "Engine warning and loss of power",
  "photoUrls": ["https://firebasestorage.googleapis.com/..."],
  "latitude": 10.7769,
  "longitude": 106.7009,
  "reportedAt": "2026-08-10T09:30:00+07:00",
  "status": "OPEN",
  "resolvedAt": null,
  "resolvedByUserId": null,
  "resolutionNote": null,
  "trip": {
    "tripId": "uuid",
    "status": "IN_PROGRESS",
    "departureDateTime": "2026-08-10T07:00:00+07:00",
    "route": {
      "routeId": "uuid",
      "name": "Hồ Chí Minh - Đà Lạt",
      "originStation": { "stationId": "uuid", "name": "Bến xe Miền Đông" },
      "destinationStation": { "stationId": "uuid", "name": "Bến xe Đà Lạt" }
    }
  },
  "reporter": {
    "userId": "uuid",
    "displayName": "Nguyễn Văn Tài",
    "role": "DRIVER"
  }
}
```

`status` is derived as `OPEN` when `resolvedAt` is null and `RESOLVED` otherwise. Identity is
batch-enriched for list reads. If the reporter profile is unavailable, the Incident still returns
with `reporter.userId`; `displayName` and `role` are null. Invalid filters, date order, UUID or
pagination return `422 VALIDATION_ERROR`.

#### GET `/v1/operator/incidents/{incidentId}`

Auth and DTO are identical to the list endpoint. Missing and cross-tenant Incident IDs both return
`404 INCIDENT_NOT_FOUND`; malformed/empty UUID returns `422 VALIDATION_ERROR`. This endpoint is
read-only and does not resolve an Incident or publish an event.

#### PATCH `/v1/operator/incidents/{incidentId}/resolve`

Auth: `OPERATOR_ADMIN`; tenant scope comes only from JWT `operatorId`. A UUID-v4
`Idempotency-Key` is required. Request body is `{ "resolutionNote": "..." }`; the note is trimmed
and must contain 1..1000 characters.

The server atomically transitions an open Incident to resolved, setting `resolvedAt` from the
server clock, `resolvedByUserId` from JWT `sub`, and the trimmed `resolutionNote`. Response `200`
returns the updated `OperatorIncidentDto`. Replaying the same Idempotency-Key returns the same
response. Resolving an already resolved Incident with another key returns
`409 INCIDENT_ALREADY_RESOLVED`; missing and cross-tenant IDs are masked as
`404 INCIDENT_NOT_FOUND`. Invalid IDs or notes return `422 VALIDATION_ERROR`.

Changing a Route or replacing a Vehicle never auto-resolves an Incident. Resolve does not publish
an integration event.

### Driver/Assistant Route-Change Proposals

All endpoints below are public only through the Gateway `/v1/*` surface and use the ADR 0004
`ApiResponse<T>` envelope. Driver endpoints require the authenticated `DRIVER` or `ASSISTANT` to
be assigned to the Trip. Operator endpoints require `OPERATOR_ADMIN` from the proposal's operator;
cross-tenant proposal lookup is masked as `404 ROUTE_CHANGE_PROPOSAL_NOT_FOUND`.

`RouteChangeProposalDto` is exactly:

```jsonc
{
  "id": "uuid",
  "tripId": "uuid",
  "operatorId": "uuid",
  "proposedByUserId": "uuid",
  "type": "EXISTING",
  "status": "PENDING",
  "sourceAlternativeRouteId": "uuid",
  "sourceUpdatedAt": "2026-08-04T09:00:00+07:00",
  "incidentId": null,
  "reason": "Traffic congestion ahead",
  "snapshot": {
    "name": "Bypass via Bao Loc",
    "description": "Frozen at proposal creation.",
    "destinationStationId": "uuid",
    "totalDistanceKm": 321.25,
    "estimatedDurationMinutes": 455,
    "pathPolyline": "encoded-google-polyline-precision-5",
    "stops": [
      {
        "stopId": "uuid",
        "orderIndex": 1,
        "estimatedDurationFromOriginMinutes": 80,
        "distanceFromOriginKm": 70.25
      }
    ]
  },
  "decidedByUserId": null,
  "decidedAt": null,
  "rejectionReason": null,
  "resolutionCode": null,
  "supersededByProposalId": null,
  "approvedAlternativeRouteId": null,
  "createdAt": "2026-08-04T09:00:00+07:00",
  "updatedAt": "2026-08-04T09:00:00+07:00"
}
```

`type` is `EXISTING|CUSTOM`; `status` is
`PENDING|APPROVED|REJECTED|SUPERSEDED|EXPIRED`. Resolution codes are
`ANOTHER_PROPOSAL_APPROVED|ROUTE_CHANGED_DIRECTLY|TRIP_NO_LONGER_EDITABLE|SOURCE_ROUTE_CHANGED`.
`snapshot.stops` is ordered by `orderIndex`, then `stopId`. Nullable fields are serialized as
`null`. For `EXISTING`, `sourceAlternativeRouteId` and `sourceUpdatedAt` are non-null. For `CUSTOM`,
both are null. Multiple `PENDING` proposals for one Trip are allowed.

Every paged endpoint in this section returns the selected DTO array inside exact data shape
`{items,page,pageSize,totalItems,totalPages,hasNextPage,hasPreviousPage}`; pagination is not moved
to `meta`.

#### GET `/v1/driver/trips/{tripId}/alternative-routes`

Auth: assigned `DRIVER` or `ASSISTANT`. Query: `page?`, `pageSize?`; defaults are `page=1`,
`pageSize=20`, and both must be in `1..100`.

Response `200`: `PagedResult<AlternativeRouteDto>` containing only active AlternativeRoutes for
the assigned Trip's parent Route, ordered by `name`, then `id`.

Statuses: `200`, `401`, `403`, `404`, `422`.

Errors: `403 FORBIDDEN` for authenticated but unassigned crew; `404 TRIP_NOT_FOUND` for a missing
Trip; `422 VALIDATION_ERROR` for an invalid UUID/pagination value.

#### POST `/v1/driver/trips/{tripId}/route-change-proposals`

Auth: assigned `DRIVER` or `ASSISTANT`. Idempotency: required UUID-v4 `Idempotency-Key`.

EXISTING request is exactly:

```json
{
  "type": "EXISTING",
  "alternativeRouteId": "uuid",
  "incidentId": "uuid",
  "reason": "Traffic congestion ahead"
}
```

CUSTOM request is exactly:

```jsonc
{
  "type": "CUSTOM",
  "route": {
    "name": "Crew-proposed bypass",
    "description": "Avoid the blocked pass.",
    "destinationStationId": "uuid",
    "totalDistanceKm": 318.5,
    "estimatedDurationMinutes": 440,
    "pathPolyline": "encoded-google-polyline-precision-5",
    "stops": [
      {
        "stopId": "uuid",
        "orderIndex": 1,
        "estimatedDurationFromOriginMinutes": 75,
        "distanceFromOriginKm": 68.5
      }
    ]
  },
  "incidentId": null,
  "reason": "Road closure reported by local authority"
}
```

Unknown fields are rejected. `reason` is required, trimmed, and 1..500 characters; custom
`name` is required, trimmed, and at most 255 characters. `incidentId` is optional but, when
present, must identify an Incident belonging to the same Trip. `EXISTING` requires exactly a
non-null `alternativeRouteId` and no `route`; the AlternativeRoute must be active and match
the Trip's Route/operator. `CUSTOM` requires exactly a non-null `route` and no
`alternativeRouteId`; destination Station must have an active OperatorStation mapping for the
proposal operator, and every Stop must be active and belong to that operator. `pathPolyline` is
required, must be a valid Google encoded polyline (precision 5), and must pass the existing
500-metre Station/Stop waypoint validator. Stop IDs and positive `orderIndex` values are unique;
duration/distance values are non-negative. Both types persist an immutable snapshot. Creation
never changes the Trip or AlternativeRoute catalog.

The Trip must be `SCHEDULED|BOARDING|IN_PROGRESS`. Response `201` data is the created
`RouteChangeProposalDto` with `status=PENDING`.

Statuses: `201`, `401`, `403`, `404`, `409`, `422`.

Errors:

- `403 FORBIDDEN`: caller is not assigned to the Trip.
- `404 TRIP_NOT_FOUND`: Trip does not exist.
- `404 ROUTE_NOT_FOUND`: EXISTING source is missing, inactive, cross-route, or cross-operator.
- `404 STATION_NOT_FOUND` / `404 STOP_NOT_FOUND`: CUSTOM snapshot references an unavailable
  destination/Stop.
- `404 INCIDENT_NOT_FOUND`: `incidentId` is missing or does not belong to this Trip.
- `409 TRIP_NOT_EDITABLE`: Trip is outside `SCHEDULED|BOARDING|IN_PROGRESS`.
- `422 VALIDATION_ERROR`: shape, enum, required, length, UUID, uniqueness, or numeric validation.

#### GET `/v1/driver/trips/{tripId}/route-change-proposals`

Auth: assigned `DRIVER` or `ASSISTANT`. Query: `type?=EXISTING|CUSTOM`, `page?`, `pageSize?`;
pagination defaults to `1/20`, range `1..100`. Results include every proposal for that Trip,
regardless of proposer/status, ordered by `createdAt DESC`, then `id`.

Response `200`: `PagedResult<RouteChangeProposalDto>`. Statuses: `200`, `401`, `403`, `404`,
`422`; assignment/not-found behavior matches the candidate-list endpoint, and invalid filters or
pagination return `422 VALIDATION_ERROR`.

#### GET `/v1/operator/route-change-proposals`

Auth: `OPERATOR_ADMIN`. Query: `tripId?`,
`status?=PENDING|APPROVED|REJECTED|SUPERSEDED|EXPIRED`, `type?=EXISTING|CUSTOM`, `page?`,
`pageSize?`; pagination defaults to `1/20`, range `1..100`. Results are tenant-scoped and ordered
by `createdAt DESC`, then `id`.

Response `200`: `PagedResult<RouteChangeProposalDto>`. Statuses: `200`, `401`, `403`, `422`;
invalid UUID/filter/pagination values return `422 VALIDATION_ERROR`.

#### GET `/v1/operator/route-change-proposals/{proposalId}`

Auth: `OPERATOR_ADMIN`. Response `200`: `RouteChangeProposalDto`.

Statuses: `200`, `401`, `403`, `404`, `422`. Missing or cross-tenant IDs return
`404 ROUTE_CHANGE_PROPOSAL_NOT_FOUND`; malformed/empty UUID returns `422 VALIDATION_ERROR`.

#### POST `/v1/operator/route-change-proposals/{proposalId}/approve`

Auth: `OPERATOR_ADMIN`. Idempotency: required UUID-v4 `Idempotency-Key`. Request is bodyless.

Approval locks the Trip and all of its pending proposals. The Trip must still be
`SCHEDULED|BOARDING|IN_PROGRESS`. An EXISTING source must still be active and have the exact
frozen `sourceUpdatedAt`. A CUSTOM snapshot is revalidated against active destination/Stops, then
promoted to a new official AlternativeRoute; there is no global active-route cap. Approval applies
the selected/promoted route, publishes the existing `trip.trip.route_changed`, marks the chosen
proposal `APPROVED`, and marks every other pending proposal for the Trip `SUPERSEDED` with
`resolutionCode=ANOTHER_PROPOSAL_APPROVED` and `supersededByProposalId` set to the winner, all in
one transaction.

Response `200` data is the exact composite:

```jsonc
{
  "proposal": { "id": "uuid", "status": "APPROVED", "approvedAlternativeRouteId": "uuid" },
  "routeChange": {
    "tripId": "uuid",
    "status": "IN_PROGRESS",
    "alternativeRouteId": "uuid",
    "affectedBookings": [
      { "bookingId": "uuid", "candidateStops": [] }
    ]
  }
}
```

`proposal` is the full `RouteChangeProposalDto`, not the abbreviated JSON shown for readability;
`routeChange` is the exact direct change-route response defined above.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

Errors: `404 ROUTE_CHANGE_PROPOSAL_NOT_FOUND` for missing/cross-tenant proposal or missing owned
Trip; `409 ROUTE_CHANGE_PROPOSAL_NOT_PENDING` when already decided or when the Trip became
non-editable (pending rows become `EXPIRED`); `409 ROUTE_CHANGE_PROPOSAL_STALE` when the frozen
EXISTING source changed/deactivated or CUSTOM destination/Stops are no longer valid; idempotency
and malformed UUID failures use the shared `422` contract.

#### POST `/v1/operator/route-change-proposals/{proposalId}/reject`

Auth: `OPERATOR_ADMIN`. Idempotency: required UUID-v4 `Idempotency-Key`.

Request is exactly `{ "reason": "Optional operator note" }`.
`reason` is optional, blank-normalized to null, trimmed, and at most 500 characters. The response
continues to expose the persisted decision note as `rejectionReason`.
Response `200` data is the full `RouteChangeProposalDto` with `status=REJECTED`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`. Missing/cross-tenant proposal returns
`404 ROUTE_CHANGE_PROPOSAL_NOT_FOUND`; any non-pending state returns
`409 ROUTE_CHANGE_PROPOSAL_NOT_PENDING`; invalid body/length/UUID/idempotency returns the shared
`422` response.

### POST `/v1/operator/trips/{tripId}/substitute-vehicle/preview`

Auth: `OPERATOR_ADMIN`-only for the Trip's operator. This is a read-only preview; no
`Idempotency-Key` is required.

Request is exactly `{ "replacementVehicleId": "uuid" }`. The old Trip must be `IN_PROGRESS`; the
replacement vehicle must be active, operator-owned, and different from the old vehicle.

Response `200` data is:

```jsonc
{
  "tripId": "uuid",
  "replacementVehicleId": "uuid",
  "previewToken": "64-character uppercase SHA-256 hex",
  "passengers": [{
    "bookingId": "uuid",
    "passengerId": "uuid",
    "originalSeatNumber": "A2",
    "proposedSeatNumber": null,
    "requiresAdminSelection": true,
    "alternativeSeatNumbers": ["A5", "A10"]
  }],
  "availableSeatNumbers": ["A1", "A5", "A10"]
}
```

The preview reserves each exact usable seat number first (`A1 -> A1`, `A2 -> A2`). A Passenger
whose original seat is absent or disabled has `requiresAdminSelection=true` and must be assigned
an alternative by the Operator Admin. Preview never writes a Trip, Booking, seat, audit, or
Outbox row. If total usable capacity cannot seat every eligible Passenger, it returns
`409 REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

### POST `/v1/operator/trips/{tripId}/substitute-vehicle`

Auth: `OPERATOR_ADMIN`-only for the Trip's operator. `Idempotency-Key` is required UUID v4.

Request is exactly:
```jsonc
{
  "replacementVehicleId": "uuid",
  "incidentId": "uuid",
  "estimatedRecoveryDepartureAt": "2026-07-25T08:30:00Z",
  "reason": "Vehicle breakdown",
  "notifyPassengers": true,
  "previewToken": "64-character uppercase SHA-256 hex",
  "seatAssignments": [
    { "passengerId": "uuid", "newSeatNumber": "A5" }
  ],
  "replacementCrew": {
    "driverId": "uuid",
    "assistantId": "uuid"
  }
}
```

`replacementVehicleId` is a required UUID. `estimatedRecoveryDepartureAt` is a required absolute UTC timestamp.
`reason` is required, trimmed, and at most 500 characters. `notifyPassengers` is optional and defaults to `true`.
`previewToken` and `seatAssignments` are optional only when every Passenger's exact original seat
can be preserved. If any original seat is absent or disabled, `previewToken` must be the latest
token returned by preview and `seatAssignments` must contain exactly one valid available seat for
each affected Passenger. The legacy additive `acknowledgeInsufficientSeats` field remains accepted
for client compatibility but no longer permits an unseated transfer.
`incidentId` is a required UUID belonging to the same Trip and operator. `replacementCrew` is
required and exactly `{driverId,assistantId}`; both are required, non-null UUIDs. The replacement
driver and assistant must be active, operator-owned, conflict-free, and different from the old
crew. The replacement vehicle must be active, operator-owned, and different from the old vehicle.

Unknown fields are rejected, including legacy top-level `newVehicleId`,
`estimatedArrivalMinutes`, top-level `driverId`, and top-level `assistantId`.

The service locks and reloads the old Trip, captures one `disruptedAt`, and requires
`estimatedRecoveryDepartureAt` to be strictly later than the locked `disruptedAt`. Equality or an
earlier value returns `422 VALIDATION_ERROR` with
`fields.estimatedRecoveryDepartureAt = ["must be later than disruptedAt"]`; no Trip child, audit,
or Outbox row is written. The old Trip must be `IN_PROGRESS`, otherwise this substitution-only
endpoint returns `409 TRIP_NOT_SUBSTITUTABLE`.

After locking and revalidating the old Trip, incident, old Vehicle, and replacement Vehicle, the service compares the
replacement layout's usable seats with the distinct eligible passengers in the Booking impact
snapshot. When seats are insufficient, it returns
`409 REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS` before creating a Trip, changing resources, writing
audit, or enqueueing Outbox rows. The ADR 0004 error uses `error.fields` (not `error.details`):

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS",
    "message": "Replacement vehicle does not have enough usable seats.",
    "fields": [
      { "field": "usableSeats", "message": "3" },
      { "field": "passengersToTransfer", "message": "5" },
      { "field": "missingSeats", "message": "2" }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-25T15:00:00+07:00" }
}
```

The Operator cannot acknowledge a true capacity shortage; another replacement vehicle is required.
When capacity is sufficient but an exact seat is absent, missing assignments return
`409 REPLACEMENT_SEAT_ASSIGNMENT_REQUIRED`, invalid/duplicate/unavailable assignments return
`409 REPLACEMENT_SEAT_NOT_AVAILABLE`, and a changed Trip/Vehicle/Booking/layout snapshot returns
`409 REPLACEMENT_SEAT_PREVIEW_STALE`. All failures occur before substitution writes.

On success the old Trip is `DISRUPTED` with `hasSubstitution=true`, and the old Vehicle transitions
from `ACTIVE` to `MAINTENANCE`. The dedicated replacement Trip
is `BOARDING`, has `source=VEHICLE_SUBSTITUTION`, and
`departureDateTime=estimatedRecoveryDepartureAt`. The existing assigned-driver start flow moves
the replacement `BOARDING -> IN_PROGRESS` and captures `actualDepartureTime`.

Response `200`:
```jsonc
{
  "success": true,
  "statusCode": 200,
  "data": {
    "substitutionId": "uuid-v4",
    "oldTripId": "uuid",
    "oldTripStatus": "DISRUPTED",
    "newTripId": "uuid",
    "newTripStatus": "BOARDING",
    "newTripDepartureDateTime": "2026-07-25T15:30:00+07:00",
    "transferStatus": "QUEUED",
    "affectedBookingCount": 2,
    "affectedPassengerCount": 5,
    "pendingSeatAssignmentCount": 0
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-25T15:00:00+07:00" }
}
```

`substitutionId` equals the canonical `trip.trip.vehicle_substituted` eventId.
`affectedBookingCount` counts eligible Booking entries represented in the mapping,
`affectedPassengerCount` counts mapped `BOARDED|PENDING` Passengers, and
`pendingSeatAssignmentCount` is `0` because confirm cannot create an unseated Passenger. No Parcel
count is returned. A same-key replay returns the persisted response as idempotent `200`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

- `401 AUTH_TOKEN_INVALID`
- `403 FORBIDDEN`
- `404 TRIP_NOT_FOUND`
- `409 TRIP_NOT_SUBSTITUTABLE`
- `409 TRIP_VEHICLE_CONFLICT`
- `409 REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS`
- `422 VEHICLE_NOT_ACTIVE`
- `422 VALIDATION_ERROR`

### POST `/v1/operator/trips/{tripId}/disrupt-no-substitution`

Auth: `OPERATOR_ADMIN` for the Trip operator. UUID-v4 `Idempotency-Key` is required.

Request is exactly:

```json
{
  "reason": "Road closure with no replacement vehicle available"
}
```

`reason` is required, trimmed, and 1–500 characters. Only an `IN_PROGRESS` Trip may transition.
The winning transaction sets `status=DISRUPTED`, `hasSubstitution=false`, `disruptedAt=now`, and
the reason, then writes canonical `trip.trip.disrupted` to the Outbox. It does not calculate or
return a Trip-wide traveled ratio.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "status": "DISRUPTED",
    "disruptedAt": "2026-07-30T10:00:00+07:00",
    "hasSubstitution": false,
    "reason": "Road closure with no replacement vehicle available"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-30T10:00:00+07:00" }
}
```

Errors:

- `404 TRIP_NOT_FOUND` for missing or cross-tenant Trip.
- `422 TRIP_NOT_IN_PROGRESS` for `SCHEDULED|BOARDING`.
- `409 TRIP_ALREADY_TERMINAL` for `COMPLETED|CANCELLED|DISRUPTED`.
- `422 VALIDATION_ERROR` for an invalid reason.

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
updates it. Planned recomputation uses ordered Goong Directions legs (`vehicle=car`), adds the
configured stop dwell (default 20 minutes), and falls back to cumulative Route metrics without
failing the mutation. Goong does not accept departure-time/traffic-duration inputs, so new Goong
plans have `plannedEtaQuality=ROUTE_BASED`, never `TRAFFIC_AWARE`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T17:00:00+07:00" }
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

## Operator holiday fare surcharge management (Trip Service)

Gateway prefix: `/v1/operator/fare-surcharges`. Reads allow `OPERATOR_ADMIN|OPERATOR_STAFF`;
writes allow `OPERATOR_ADMIN` only and require a UUID-v4 `Idempotency-Key`.

- `GET /v1/operator/fare-surcharges/settings` returns `{ "isEnabled": false }`; a missing row is
  materialized as the disabled default.
- `PUT /v1/operator/fare-surcharges/settings` accepts `{ "isEnabled": true }` and returns the
  persisted setting.
- `GET /v1/operator/fare-surcharges/periods?page=1&pageSize=20` returns an ADR 0004
  `PagedResult<FareSurchargePeriodDto>` ordered by `startDate`, then `periodId`.
- `POST /v1/operator/fare-surcharges/periods` creates a period from `{ name, startDate, endDate,
  surchargePercent, isActive? }`; `isActive` defaults true and response is `201`.
- `PATCH /v1/operator/fare-surcharges/periods/{periodId}` accepts any non-empty subset of those
  fields and returns the updated DTO.
- `DELETE /v1/operator/fare-surcharges/periods/{periodId}` soft-deletes the period and returns
  `204`.

`FareSurchargePeriodDto` is `{ periodId, name, startDate, endDate, surchargePercent, isActive,
status, createdAt, updatedAt }`; `status` is `UPCOMING|APPLYING|EXPIRED|DISABLED` based on the
current Asia/Ho_Chi_Minh date and activation flag. Dates are inclusive; `name` is trimmed `1..120`, percentage
is an integer `1..100`, and `startDate <= endDate`. Active non-deleted periods for one operator
must not overlap. Errors: `404 FARE_SURCHARGE_PERIOD_NOT_FOUND`, `422
FARE_SURCHARGE_PERIOD_OVERLAP`, or generic `422 VALIDATION_ERROR`.

Trip search/detail keep `baseFare` as the original fare and add `surchargePercent`,
`surchargeAmount`, `effectiveFare`, nullable `surchargePeriodId`, and nullable
`surchargePeriodName`; stop-fare breakdowns expose the same effective adjustment. The internal
Trip snapshot applies the same adjustment only when `pricingAt` is supplied, preserving the
legacy no-`pricingAt` contract. Booking snapshots `effectiveFare` before voucher application.

## Parcel Service

Parcel cargo policy:
- Dimension unit: centimeters; weight unit: kilograms.
- Volume precision: `decimal(10,4)` m3.
- Weight/DIM/chargeable precision: `decimal(8,2)` kg.
- Money is VND `BIGINT` persisted to the đồng. `Money.FromRaw` is pass-through; a fractional
  calculation rounds to the nearest đồng with `MidpointRounding.AwayFromZero`.
- `dimWeightKg = lengthCm × widthCm × heightCm / 6000` and `chargeableWeightKg = max(weightKg, dimWeightKg)`.
- `grossPriceVnd = max(minimumPriceVnd, round(chargeableWeightKg × pricePerKgVnd))`; rounding is to the nearest đồng with `MidpointRounding.AwayFromZero`. There is no kg ceiling and no 1,000-VND floor.
- Size is derived from chargeable weight: `SMALL <= 5`, `MEDIUM <= 15`, `LARGE <= 30`, `EXTRA_LARGE > 30` kg. Client size fields are compatibility hints only. All sizes require a configured route fare and new Parcels start at `PENDING_PAYMENT`; `EXTRA_LARGE` does not require operator pre-review.
- `estimatedTotalPriceVnd = estimatedGrossPriceVnd - min(discountAmountVnd, estimatedGrossPriceVnd)`; final total uses the same clamp against final gross.
- Settlement v2 deposit is 20% of estimated total. Only `READY_TO_LOAD` may transition to `LOADED`.
- `PENDING_OPERATOR_ACTION` is disambiguated by `pendingActionType`; `pendingActionResumeStatus` records the settlement state to resume after recovery.

### GET `/v1/parcels/available-trips`

Auth: `PASSENGER`.

Query: `originStationId`, `destinationStationId`, `departureDate`, `lengthCm`, `widthCm`, `heightCm`, `estimatedWeightKg`; legacy `sizeCategory` is optional and non-authoritative.

Only Trips whose status is `SCHEDULED` are eligible. `BOARDING` Trips are excluded before count
and pagination because the Parcel check-in deadline has already closed when boarding starts.

Backend calculates `volumeM3`, `dimWeightKg`, and `chargeableWeightKg = max(estimatedWeightKg, dimWeightKg)`.
It first resolves the derived size to Routes with an active fare, then asks Trip to apply those
eligible Route IDs before count and pagination. Ordering is stable by `departureDateTime`, then
`tripId`; all paging metadata therefore describes only fare-eligible Trips. Customer response must
not expose raw remaining cargo capacity. Trips that cannot accept both estimated volume and
estimated weight are filtered out.

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
        "quoteToken": "base64url-payload.base64url-signature",
        "quoteExpiresAt": "2026-05-18T07:10:00+07:00",
        "estimatedSizeCategory": "MEDIUM",
        "estimatedGrossPriceVnd": 160000,
        "estimatedDiscountVnd": 10000,
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

`estimatedPriceVnd` is the estimated total after the clamped discount. `depositPercent` is `20`
under settlement policy v2 and is snapshotted on creation. The public item does not serialize
`availableCargoWeightKg`, `availableCargoVolumeM3`, or the internal `priceVnd` alias.

Parcel obtains this page through internal
`POST /internal/v1/trips/parcel-availability/search`. The read-only POST accepts the existing
availability filters plus `eligibleRouteIds`; Trip applies that filter before count/pagination and
also excludes Trips without an assigned Assistant before count/pagination. It returns the existing
raw `PagedResult<ParcelTripAvailabilityItemDto>`. The legacy internal GET
`/internal/v1/trips/parcel-availability` remains available during rollout.

### POST `/v1/parcels`

Auth: `PASSENGER`. Idempotency: required.

The selected Trip must still be `SCHEDULED` when the command executes. Any other status, including
`BOARDING`, returns `409 TRIP_NOT_ACCEPTING_PARCEL`; this closes the race between availability
search and creation when a Trip starts boarding. The Trip must also have an assigned Assistant;
otherwise creation returns `409 PARCEL_ASSISTANT_REQUIRED`. This prevents accepting cargo for a
Trip that has no crew member authorized to check in, load, unload, reconcile, or deliver it.

Request:
```json
{
  "tripId": "uuid",
  "dropoffStopId": "uuid",
  "bookingId": "uuid",
  "itemName": "Thùng quà",
  "description": "Hàng dễ vỡ",
  "quantity": 2,
  "declaredValueVnd": 12000000,
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
  "paymentMethod": "WALLET",
  "voucherCode": null,
  "quoteToken": "base64url-payload.base64url-signature"
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
    "bookingId": "uuid|null",
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
    "settlementPolicyVersion": 2,
    "compensationPolicy": {
      "version": 1,
      "compensationRatePercent": 50,
      "maxCompensationVnd": 30000000,
      "noProofFallbackMultiplier": 4,
      "claimWindowDays": 30,
      "searchSlaHours": 72,
      "decisionSlaBusinessDays": 7,
      "payoutSlaBusinessDays": 3
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

The server derives `estimatedSizeCategory`; old clients may still send `sizeCategory`, but it does
not override calculated size or price. Every size, including `EXTRA_LARGE`, returns
`PENDING_PAYMENT`. A fare for the derived route/size is required; otherwise the server returns
`422 FARE_NOT_CONFIGURED` without writes. Create does not reserve cargo or create a payment.
Capacity is enforced when the deposit soft hold starts and again when the Assistant records actual
measurements.

`quoteToken` is optional during the BE-first rollout. It is a stateless HMAC-SHA256 signed quote
with a default 600-second TTL and binds the sender, Trip/Route/operator, station pair, normalized
dimensions/weight, chargeable weight/category, fare identity/version, DIM and settlement-policy
versions, gross/discount/deposit, timestamps and a random `jti`. When present, client-supplied
category, amount, dimensions or weight cannot reinterpret the signed quote. Signature, expiry,
policy/fare drift and request mismatch return `409 PARCEL_QUOTE_INVALID`,
`PARCEL_QUOTE_EXPIRED`, `PARCEL_QUOTE_STALE`, and `PARCEL_QUOTE_MISMATCH`, respectively. Legacy
requests without a token remain supported.

`quantity` is an immutable positive integer (default `1` for backward compatibility).
`declaredValueVnd` is optional, non-negative integer VND. The response discloses the exact
compensation policy snapshot accepted for this Parcel; future operator policy updates do not
change it.

When `recipient.email` is present, Parcel trims/lowercases it and calls Identity's exact,
non-deleted user lookup. A match is saved as the logical `recipientUserId` in the create
transaction; `404 RESOURCE_NOT_FOUND` saves null. Transport, 5xx, or malformed Identity responses
return `503 UPSTREAM_UNAVAILABLE` without a partial Parcel. Public requests never accept or trust a
recipient user ID.

### GET `/v1/parcels/vouchers/available`

Auth: `PASSENGER`. Query keeps the legacy `tripId`, `sizeCategory`, `paymentMethod?`, and
`orderAmount?` parameters and adds optional `quoteToken`. When a token is supplied, signed quote
values are authoritative and conflicting legacy query values return `409 PARCEL_QUOTE_MISMATCH`.
All derived categories, including `EXTRA_LARGE`, are supported. Token validation uses the same
invalid/expired/stale errors as Parcel creation.

### POST `/v1/parcels/{parcelId}/deposit-payment`

Auth: owning `PASSENGER`. Idempotency: required.

Request: `{ "paymentMethod": "WALLET|VNPAY", "paymentReturnMode": "MOBILE_SDK|null" }`.
`paymentReturnMode` is required and must be `MOBILE_SDK` for VNPay.

The mutation first resumes any pending cargo `RELEASE`, then creates an idempotent soft cargo hold
using estimated weight/volume and creates a Payment whose
`dueAt = min(paymentStartedAt + 15 minutes, latestCheckInAt)`. If no positive payment window
remains, it does not create a Payment or hold. A zero deposit consumes the validated voucher, keeps
the reservation, and moves directly to `RESERVED` without creating a zero-value Payment.

Response `200` data contains `parcelId`, `status`, `depositPaymentId?`, `depositRequiredVnd`,
`depositPaidVnd`, `paymentDueAt?`, `paymentRedirectUrl?`, `paymentReturnMode?`, and `vnpaySdk?`.
Payment success is valid only when authoritative `paidAt < paymentDueAt`; failure, expiry, timeout,
or a post-reserve Payment error moves the Parcel to its terminal recovery state and releases cargo.
If Trip is temporarily unavailable, Parcel persists one idempotent `RELEASE` recovery operation
whose operation ID is reused as the Trip Idempotency-Key. Duplicate sweeps/events resume the same
logical release and never decrement capacity twice. The expiry sweep only claims
`PENDING_PAYMENT` rows whose `depositPaymentId` is null.

### POST `/v1/parcels/{parcelId}/final-payment`

Auth: owning `PASSENGER`. Idempotency: required. Allowed only in `PENDING_FINAL_PAYMENT` and before `finalPaymentDeadline`.

Request: `{ "paymentMethod": "WALLET|VNPAY", "paymentReturnMode": "MOBILE_SDK|null" }`.
`paymentReturnMode` is required and must be `MOBILE_SDK` for VNPay.

The charged amount is server-derived `max(0, balanceRequiredVnd - balancePaidVnd)`. Response data contains `parcelId`, `status`, `balancePaymentId?`, `balanceRequiredVnd`, `balancePaidVnd`, `finalPaymentDeadline`, `paymentRedirectUrl?`, `paymentReturnMode?`, and `vnpaySdk?`. A payment with `paidAt >= finalPaymentDeadline` is not added to `balancePaidVnd`; Payment Service owns capture/refund tracking for that late payment.

For both Parcel VNPay endpoints, missing mode returns `426 MOBILE_APP_UPDATE_REQUIRED`, invalid mode
returns `422 PAYMENT_RETURN_MODE_INVALID`, and a disabled Mobile channel returns
`503 VNPAY_MOBILE_SDK_DISABLED` without a bridge fallback.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "createdAt": "2026-05-01T16:00:00+07:00",
  "totalAmount": 350000,
  "originName": "Bến xe Miền Đông",
  "destinationName": "Bến xe Mỹ Đình",
  "departureDateTime": "2026-05-18T08:00:00+07:00",
  "estimatedArrivalTime": null,
  "ticket": {
    "bookingGroupId": null,
    "tripDirection": null,
    "routeName": "TP.HCM - Hà Nội",
    "pickupPoint": {
      "type": "STOP",
      "id": "pickup-stop-uuid",
      "displayName": "Điểm đón C",
      "address": null,
      "plannedAt": "2026-05-18T10:00:00+07:00"
    },
    "dropoffPoint": {
      "type": "STATION",
      "id": "destination-station-uuid",
      "displayName": "Bến xe Mỹ Đình",
      "address": null,
      "plannedAt": "2026-05-18T20:00:00+07:00"
    },
    "vehicle": {
      "licensePlate": "51B-123.45",
      "vehicleType": {
        "code": "LIMOUSINE",
        "displayName": "Limousine"
      }
    },
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
  "parcel": null,
  "paymentRedirectUrl": null
}
```

For `PARCEL`, `ticket` is null and `parcel` is
`{ bookingId, recipientName, sizeCategory, photoUrl, deliveryMethod }`. Exactly one of `ticket` or
`parcel` is non-null. Journey fields may be null for legacy data or unavailable Trip enrichment.
For `TICKET`, `ticket.vehicle` is always serialized as
`{ licensePlate, vehicleType: { code, displayName } | null }` or `null`; Parcel forwards the
fail-open Booking history projection unchanged and does not call Trip again. The public vehicle
projection contains only the plate and type identity/display fields.
For `TICKET`, `ticket.pickupPoint` and `ticket.dropoffPoint` forward Booking's persisted point
snapshots unchanged. Root `originName` and `destinationName` remain Route endpoint metadata. Parcel
does not call Trip to fill a missing point, and a legacy missing snapshot remains null.
`paymentRedirectUrl` is the final root property and is always serialized. `TICKET` forwards the
value from Booking history without another Payment call. `PARCEL` returns only the latest eligible
deposit/final VNPay URL for the exact owner/reference/amount/deadline: deposit requires
`PENDING_PAYMENT`, `PARCEL/parcelId`, exact `DepositPaymentId`, exact remaining deposit, and
`dueAt <= LatestCheckInAt`; final requires `PENDING_FINAL_PAYMENT`,
`PARCEL_ADDITIONAL/parcelId`, exact `BalancePaymentId`, exact remaining balance, and
`dueAt <= FinalPaymentDeadline`. `PENDING_ADDITIONAL_PAYMENT` is never eligible. Lookup failure
yields null without failing the local history query. `GET /v1/parcels/sent` remains unchanged and
does not expose payment identifiers or settlement deadlines.
Booking unavailability on `TICKET` returns `502 UPSTREAM_UNAVAILABLE`; it must not be represented
as an empty page. Validation failures return `422 VALIDATION_ERROR`.

### GET `/v1/parcels/{parcelId}`

Auth: sender, recipient account, or authorized operator.

Response `200`: parcel detail with `parcelId`, nullable `bookingId`, sender, recipient, trip,
transfer, optional sender `photoUrl`, optional `checkInPhotoUrls` and `deliveryPhotoUrls`, delivery
token state excluding raw token, estimated/actual cargo snapshots, and the canonical settlement
fields: `estimatedGrossPriceVnd`, `finalGrossPriceVnd`, `discountAmountVnd`,
`estimatedTotalPriceVnd`, `finalTotalPriceVnd`, `depositPercent`, `depositRequiredVnd`,
`depositPaidVnd`, `balanceRequiredVnd`, `balancePaidVnd`, `refundDueVnd`, `refundedAmountVnd`,
`forfeitedDepositVnd`, payment IDs, `finalPaymentDeadline`, check-in/reweigh timestamps, fare
snapshots, and `settlementPolicyVersion`.

### POST `/internal/v1/emails`

Owner: Notification. Auth: valid Internal JWT only; never routed through Gateway. UUID-v4
`Idempotency-Key` is required.

Request:

```json
{
  "notificationId": null,
  "dedupeKey": "parcel-delivery-token:<tokenRowId>",
  "toEmail": "recipient@example.com",
  "templateKey": "PARCEL_DELIVERY_LINK",
  "templateData": {
    "deliveryUrl": "https://app.vietride.online/parcels/delivery/confirm?token=<runtime-token>",
    "parcelCode": "VRP-20260730-ABC123",
    "expiresAt": "2026-08-01T03:00:00Z"
  }
}
```

Success is `202 Accepted` in the ADR 0004 envelope and means Notification durably accepted the
delivery. Same-key/same-fingerprint replay returns the original acceptance; mismatch and pending
use the shared idempotency errors. OTP, password URLs, and Parcel delivery URLs are sensitive:
Notification redacts them completely from its audit row and encrypts queue payloads before Redis;
plaintext exists only in request/worker memory and the outbound provider call. Errors:
`401 AUTH_TOKEN_INVALID`, `422 VALIDATION_ERROR|IDEMPOTENCY_KEY_REQUIRED|
IDEMPOTENCY_KEY_MISMATCH`, and `409 IDEMPOTENCY_REQUEST_PENDING`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```
Decision note: invalid, expired, and revoked delivery tokens return 400 with
`PARCEL_DELIVERY_TOKEN_INVALID`, `PARCEL_DELIVERY_TOKEN_EXPIRED`, or
`PARCEL_DELIVERY_TOKEN_REVOKED`. Parcel parses a UUID-v4 token, normalizes lowercase `D`, and
looks up its SHA-256 hash in `parcel_delivery_tokens`; raw tokens are never persisted, logged, or
published in an event.

### POST `/v1/operator/parcels/{parcelId}/resend-delivery-email`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. Idempotency: required. Bodyless.

Valid for `DELIVERED_PENDING_CONFIRM`, or `DELIVERY_REJECTED` while the 15-minute undo window is
still open. The latter is restored to `DELIVERED_PENDING_CONFIRM`. Parcel revokes the current
token row, creates a new UUID-v4 token/hash with a 48-hour expiry, and calls Notification
`POST /internal/v1/emails` using Internal JWT. The internal email uses
`notificationId:null`, `dedupeKey:"parcel-delivery-token:<tokenRowId>"`,
`toEmail=recipientEmail`, `templateKey=PARCEL_DELIVERY_LINK`, UUID-v4 Idempotency-Key equal to the
token-row id, and `templateData={deliveryUrl,parcelCode,expiresAt}`. Parcel commits the
rotation/state only after Notification returns `202`; every other response, timeout, or transport
failure returns `503 UPSTREAM_UNAVAILABLE` without committing the new token/state. A Parcel
without `recipientEmail` returns
`422 PARCEL_RECIPIENT_EMAIL_REQUIRED`.

Response `200` data:

```json
{
  "parcelId": "uuid",
  "status": "DELIVERED_PENDING_CONFIRM",
  "expiresAt": "2026-08-01T10:00:00+07:00"
}
```

Same-key replay returns the original response without another email. Concurrent different-key
rotations use the active-token CAS/partial unique constraint: one wins and the loser returns
`409 RESOURCE_CONFLICT`. Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND`,
`400 PARCEL_NOT_PENDING_CONFIRM`, `422 PARCEL_RECIPIENT_EMAIL_REQUIRED`, and
`503 UPSTREAM_UNAVAILABLE`.

### POST `/v1/crew/parcels/{parcelId}/resend-delivery-email`

Exact behavior and response are the same as the operator endpoint. Auth is assigned
`DRIVER|ASSISTANT` of the Parcel's current Trip. Cross-trip or unassigned callers return
`403 FORBIDDEN`.

### POST `/v1/operator/parcels/{parcelId}/manual-confirm`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. Idempotency: required.

Request:

```json
{
  "confirmNote": "Recipient confirmed by phone at the destination station"
}
```

`confirmNote` is trimmed and 1–500 characters. Valid from `DELIVERED_PENDING_CONFIRM`, including
when no recipient email exists or the token has expired. Success transitions to
`DELIVERY_CONFIRMED`, records actor/note/timestamp, and revokes the active token.

Response `200` uses the existing delivery-confirmation data shape.

Same-key replay returns the original response. A different-key request after confirmation is a
behavioral no-op only when it carries the same actor/note fingerprint; otherwise it returns
`400 PARCEL_NOT_PENDING_CONFIRM`. Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND`,
`400 PARCEL_NOT_PENDING_CONFIRM`, and `422 VALIDATION_ERROR`.

### POST `/v1/crew/parcels/{parcelId}/manual-confirm`

Exact body and behavior match the operator endpoint. Auth is assigned `DRIVER|ASSISTANT` of the
Parcel's current Trip. Existing assistant/operator `confirm-delivery` aliases remain compatible.
For `DELIVERED_PENDING_CONFIRM`, Assistant manifest, shared crew manifest, and crew action-state
all expose both `MANUAL_CONFIRM` and `RESEND_DELIVERY_EMAIL`; Passenger trace/detail never expose
these crew-only actions.

### GET `/v1/assistant/trips/{tripId}/parcels`

Auth: `ASSISTANT`. Read-only; Idempotency-Key is not required.

The caller must be the Assistant currently assigned to `tripId`. Results include all
non-deleted parcels whose current `tripId` and `operatorId` match the authorized trip
crew context. Query: `stopId?`, `status?`, `hasException?`, `search?`, `page` (default `1`) and
`pageSize` (default `20`, maximum `100`). The response is the screen-ready manifest described in
Parcel Reliability v2 below; pagination metadata is nested under `data.pagination`, not at the
top level of `data`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors: `401 UNAUTHORIZED` without a valid access token; `403 FORBIDDEN` when the
caller is not the assigned Assistant, has no operator scope, or the trip is unavailable;
`422 VALIDATION_FAILED` for invalid pagination; `503 TRIP_SERVICE_UNAVAILABLE` when
assignment verification cannot reach Trip service.

### GET `/v1/crew/trips/{tripId}/parcels`

Auth: assigned `DRIVER|ASSISTANT`. This is the shared crew alias of the Assistant manifest and
accepts the same filters/pagination. Besides parcels whose `tripId` equals the authorized Trip,
it includes incoming parcels where `transferTargetTripId=tripId` and
`status=PENDING_TRANSFER_CONFIRM`. Incoming items expose
`transferContext="TRANSFER_IN"`, `sourceTripId`, and `targetTripId`; they remain physically owned
by the source Trip until assigned replacement crew confirms transfer. The legacy Assistant route
remains available for backward compatibility. Actions are role-aware: Assistant rows expose
physical Parcel operations; Driver rows expose incident viewing and
`APPROVE_CUSTODY_EXCEPTION|REJECT_CUSTODY_EXCEPTION` only when the row's
`custodyExceptionApproval.status=PENDING_APPROVAL`. Driver does not receive Assistant cargo
mutation actions.

### POST `/v1/assistant/trips/{tripId}/parcels/qr-scan`

Auth: `ASSISTANT`. The authenticated JWT `sub` and `operatorId` must identify the Assistant
currently assigned to `tripId`. This POST is a read-only QR resolver and is explicitly exempt
from `Idempotency-Key`; it does not change Parcel status, cargo capacity, statistics, or Outbox.

The Passenger App renders a QR image whose complete plain-text payload is the `parcelCode`
returned by Parcel creation. Parcel Service does not generate, decode, or persist QR image data.
The Assistant App decodes the image locally and sends only that plain code:

```json
{
  "parcelCode": "VR-PCL-20260728-ABCDEFGH"
}
```

Current codes must match `^VR-PCL-\d{8}-[A-HJ-NP-Z2-9]{8}$`. The legacy
`VRP-yyyyMMdd-XXXXXXXX` shape remains accepted for existing Parcel rows.

Response `200` in the ADR 0004 envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260728-ABCDEFGH",
    "status": "READY_TO_LOAD",
    "tripId": "uuid",
    "recipientName": "Nguyen Van A",
    "sizeCategory": "SMALL",
    "photoUrl": "https://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-28T17:00:00+07:00" }
}
```

The response status lets the Assistant App choose the next explicit operation. Check-in,
reweigh, load, unload, and delivery remain separate mutation endpoints with their existing
status, assignment, capacity, deadline, and idempotency guards.

Errors: `401 UNAUTHORIZED`; `403 FORBIDDEN` when the caller is not the assigned Assistant or
has no operator scope; `404 PARCEL_NOT_FOUND` when the code is unknown or belongs to another
trip/operator; `422 VALIDATION_ERROR` for a malformed code; `503 TRIP_SERVICE_UNAVAILABLE` when
assignment verification cannot reach Trip service.

### POST `/v1/assistant/parcels/{parcelId}/check-in`

Auth: assigned `ASSISTANT` under the same operator. Idempotency: required.

Request:
```json
{
  "tripId": "uuid",
  "parcelCode": "VR-PCL-20260722-ABCDEFGH",
  "photoUrls": [
    "https://storage.googleapis.com/{bucket}/parcel-ops/{operatorId}/{assistantUserId}/{parcelId}/check-in.webp"
  ]
}
```

`photoUrls` is optional with at most three entries. Each entry must be an absolute HTTPS URL in
the configured Firebase bucket under the exact operator, Assistant uploader, and Parcel path.
Firebase Rules enforce image MIME and the 5 MB object limit. The state transition and evidence
URLs are persisted by the same compare-and-set update.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Decision notes:
- Reweigh is allowed only from `CHECKED_IN`; backend derives actual size and all money values.
- Task reweigh owns estimated reservation → actual reservation. If capacity cannot be updated, status becomes `PENDING_OPERATOR_ACTION` with `pendingActionType=CAPACITY_EXCEEDED` and a `pendingActionResumeStatus`.
- A Trip cargo state conflict returns `409 TRIP_CARGO_STATE_INVALID`; Parcel remains `CHECKED_IN` and must not misclassify the failure as capacity exceeded or persist `PENDING_OPERATOR_ACTION`.
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
  "parcelCode": "VR-PCL-20260722-ABCDEFGH"
}
```

Response `200` (`ApiResponse`):
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260722-ABCDEFGH",
    "status": "LOADED"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-22T17:00:00+07:00" }
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

### POST `/v1/assistant/parcels/{parcelId}/unload`

Auth: assigned `ASSISTANT` under the Parcel operator. Idempotency: required. Body:

```json
{
  "parcelCode": "VR-PCL-20260722-ABCDEFGH",
  "actualLocation": { "kind": "ROUTE_STOP", "id": "uuid" },
  "photoUrls": ["https://storage.googleapis.com/{bucket}/parcel-ops/.../unload.webp"]
}
```

`actualLocation.kind` is `ROUTE_STOP|DESTINATION_STATION`; it is required. `parcelCode` is the QR
identity and must equal the addressed Parcel. `photoUrls` is optional and follows the existing
Parcel evidence URL ownership rules.

Parcel synchronously reads raw `GET /internal/v1/trips/{tripId}/operational-location` before
mutation. For `dropoffStopId != null`, the requested id must equal both `dropoffStopId` and the
Trip's current stop; that stop must be `ARRIVED`, allow drop-off, and have
`actualDepartureAt=null`. For terminal-bound `dropoffStopId=null`, kind must be
`DESTINATION_STATION` and `destinationArrivedAt` must be non-null. A stop that was formerly
`ARRIVED` but has departed is invalid.

Only `IN_TRANSIT` may transition to `UNLOADED`. The winning CAS persists `unloadedAt`, releases the
Trip-owned cargo exactly once using the same idempotency identity, and enqueues one
`parcel.parcel.unloaded` fact. Response `200` data is
`{ "parcelId": "uuid", "parcelCode": "VR-PCL-20260722-ABCDEFGH", "status": "UNLOADED" }`.

Errors: `401 UNAUTHORIZED`; `403 FORBIDDEN` (including a missing/hidden Trip snapshot);
`404 PARCEL_NOT_FOUND`; `409 INVALID_STATUS|SCAN_IDENTITY_MISMATCH|PARCEL_CUSTODY_LOCATION_MISMATCH|IDEMPOTENCY_REQUEST_PENDING`;
`422 DROP_OFF_STOP_NOT_FOUND|DROP_OFF_STOP_NOT_ALLOWED|DROP_OFF_STOP_NOT_ARRIVED|DESTINATION_TERMINAL_NOT_ARRIVED|IDEMPOTENCY_KEY_REQUIRED|IDEMPOTENCY_KEY_MISMATCH`;
`503 TRIP_SERVICE_UNAVAILABLE`.

### POST `/v1/assistant/parcels/{parcelId}/deliver`

Auth: assigned `ASSISTANT` under the Parcel operator. Idempotency: required. Request body is
optional for backward compatibility. When supplied:

```json
{
  "photoUrls": [
    "https://storage.googleapis.com/{bucket}/parcel-ops/{operatorId}/{assistantUserId}/{parcelId}/delivery.webp"
  ]
}
```

`photoUrls` uses the same maximum-three, configured-bucket, and owned-path rules as check-in. Only
`UNLOADED` may transition to `DELIVERED_PENDING_CONFIRM`. When `recipientEmail` is present, the
handler creates a raw UUID-v4 token in memory, persists only its SHA-256 hash/expiry history, and
queues the exact `PARCEL_DELIVERY_LINK` request above through Notification internal HTTP, using
the token-row id for both HTTP idempotency and `parcel-delivery-token:<tokenRowId>` dedupe.
Evidence, token hash and transition timestamps commit only after Notification returns `202`;
dependency failure returns `503 UPSTREAM_UNAVAILABLE` with no transition. When email is absent, the
transition commits without a token and requires manual confirmation. An empty or omitted body
remains valid.

Day-29 E2E setup uses an isolated operator-owned Trip graph fixture with its assigned assistant,
vehicle cargo snapshot, and three Parcels. The fixture is created out of band; this contract does
not expose a public/manual Trip-create endpoint.

### GET `/internal/v1/parcels/{parcelId}`

Auth: valid Internal JWT only. Callers: Tracking and Notification. Never exposed through Gateway.
The endpoint returns terminal as well as active Parcel rows. Notification uses it only as a
fail-closed recipient snapshot when an older event does not carry immutable recipient fields.

Response `200` uses the ADR 0004 envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "tripId": "uuid",
    "status": "REJECTED",
    "senderUserId": "uuid",
    "recipientUserId": "uuid-or-null",
    "operatorId": "uuid",
    "dropoffStopId": "uuid-or-null"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-27T10:00:00Z" }
}
```

Errors: `401 AUTH_TOKEN_INVALID`; `404 PARCEL_NOT_FOUND`. Timeout, auth failure, 5xx or malformed
response is a dependency failure for Notification and must not be interpreted as an empty recipient list.

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

Auth: valid Internal JWT. This retained service-to-service alias is not exposed through Gateway.

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
    "transferConfirmedAt": "2026-05-18T03:00:00Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### POST `/v1/crew/parcels/{parcelId}/confirm-transfer`

Auth: assigned `DRIVER|ASSISTANT` of the target Trip. UUID-v4 `Idempotency-Key` is required.

Request:

```json
{
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2"
}
```

Valid only from `PENDING_TRANSFER_CONFIRM`. Parcel verifies the target-Trip crew, calls
`POST /internal/v1/trips/{sourceTripId}/cargo/transfer` with exact
`{parcelId,targetTripId,targetState:"LOADED",allowCapacityOverflow:true}`. The `true` value is
allowed only because the target was created by the approved Vehicle Substitution flow.

Before external I/O, Parcel must durably claim confirmation:

1. Require `now < transferRequestedAt + 30 minutes` (the timeout is inclusive at equality).
2. CAS `status=PENDING_TRANSFER_CONFIRM AND transferConfirmationClaimId IS NULL`, setting
   `transferConfirmationClaimId=<request Idempotency-Key>`, `transferConfirmationClaimedAt`, and
   `transferConfirmationClaimedByUserId`.
3. The timeout CAS requires the same status, `claimId IS NULL`, and
   `now >= transferRequestedAt + 30 minutes`; therefore it can never escalate after a confirmation
   claim wins.
4. Call Trip with HTTP Idempotency-Key equal to the persisted claim id. On success, CAS the same
   claim to `tripId=targetTripId,status=LOADED,transferConfirmedAt/by`.

An unknown timeout/transport result keeps the claim: retry or the recurring stale-claim recovery
replays the same Trip idempotency key, then finalizes Parcel. A definitive Trip rejection that
guarantees no cargo mutation clears the claim and returns the mapped 4xx. Crash after Trip commit
but before Parcel finalize is repaired by that replay; no second cargo movement occurs. An
authorized target-Trip crew retry with a new request key resumes the persisted claim rather than
creating another one. Same-key replay returns the persisted response.

Response `200` is the same transfer-confirmation data shape above.

The stale-claim recovery runs every five minutes and selects claims at least five minutes old. It
always replays the persisted claim id and target; it never creates a replacement key. Exact
errors: `403 FORBIDDEN` for non-target/unassigned crew; `409 PARCEL_NOT_TRANSFERABLE` for a
non-`PENDING_TRANSFER_CONFIRM` Parcel or mismatched parcel code/target; `409
PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED` when the unclaimed 30-minute deadline has won;
mapped Trip `404 PARCEL_CARGO_NOT_FOUND`, `409 TRIP_CARGO_TRANSFER_CONFLICT`, or `422
TRIP_CARGO_CAPACITY_EXCEEDED`; `503 TRIP_SERVICE_UNAVAILABLE` for an unknown/transport result;
and shared `422 IDEMPOTENCY_KEY_REQUIRED|IDEMPOTENCY_KEY_MISMATCH`, `409
IDEMPOTENCY_REQUEST_PENDING`. A deadline loser never clears or overwrites a winning persisted
claim.

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

The result is tenant-scoped by `operatorId`, ordered by `parcelId`, and contains each classified
non-terminal Parcel at most once. It uses the exact execution classifier:

- `PENDING_OPERATOR_REVIEW|PENDING_PAYMENT|PENDING|PENDING_ADDITIONAL_PAYMENT|RESERVED|CHECKED_IN|PENDING_FINAL_PAYMENT|READY_TO_LOAD`
  contributes `max(depositPaidVnd + balancePaidVnd - refundedAmountVnd,0)`.
- `LOADED|IN_TRANSIT` is included with zero refund and will become `PENDING_OPERATOR_ACTION`.
- Terminal/replayed rows are omitted.

### GET `/v1/operator/parcels`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF`. Read-only; no `Idempotency-Key`.
The service always scopes data by the authenticated `operatorId` claim; clients cannot override
tenant scope with a query parameter.

Optional query parameters:

- `status`: case-insensitive `ParcelStatus`.
- `tripId`: UUID.
- `pendingActionType`: case-insensitive `PendingActionType`.
- `page`: default `1`, minimum `1`.
- `pageSize`: default `20`, range `1..100`.

Response `200` uses the ADR 0004 paged envelope. Items are ordered by `createdAt DESC`, then
`parcelId DESC`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "parcelId": "uuid",
        "parcelCode": "VR-PCL-20260727-ABC123",
        "status": "PENDING_PAYMENT",
        "tripId": "uuid",
        "senderUserId": "uuid",
        "recipientName": "Nguyen Van A",
        "recipientPhone": "+84900000000",
        "estimatedSizeCategory": "EXTRA_LARGE",
        "actualSizeCategory": null,
        "estimatedChargeableWeightKg": 50,
        "actualChargeableWeightKg": null,
        "depositRequiredVnd": 10000,
        "depositPaidVnd": 0,
        "balanceRequiredVnd": 0,
        "balancePaidVnd": 0,
        "refundDueVnd": 0,
        "forfeitedDepositVnd": 0,
        "latestCheckInAt": "2026-07-27T16:30:00+07:00",
        "loadCutoffAt": "2026-07-27T16:50:00+07:00",
        "finalPaymentDeadline": null,
        "pendingActionType": null,
        "pendingActionReason": null,
        "photoUrl": null,
        "createdAt": "2026-07-27T15:00:00+07:00"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-27T15:00:00+07:00" }
}
```

Errors: `403 FORBIDDEN` when `operatorId` scope is missing; `422 VALIDATION_ERROR` for invalid
filters or pagination.

### PATCH `/v1/operator/parcels/{parcelId}/review` (legacy compatibility)

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. Idempotency: required. Valid only for legacy records still in `PENDING_OPERATOR_REVIEW`; new Parcels never enter this state.

Request: `{ "decision": "APPROVE|REJECT", "reason": "optional for approve, required for reject" }`. Price, deposit and payment method are not accepted from Operator input.

`APPROVE` moves a legacy record with a valid fare snapshot to `PENDING_PAYMENT`; a missing or invalid snapshot returns `422 FARE_NOT_CONFIGURED`. The Passenger then calls deposit-payment. `REJECT` moves to `REJECTED`. An unresolved legacy review after 24 hours moves to `CANCELLED` with reason `OPERATOR_REVIEW_TIMEOUT`; no payment or refund exists in either reject/timeout branch.

### POST `/v1/operator/parcels/{parcelId}/request-transfer`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. UUID-v4 `Idempotency-Key` is
required.

Request:
```json
{
  "targetTripId": "uuid",
  "reason": "Trip disrupted, move parcel to next available trip"
}
```

For a cancellation/disruption recovery Parcel in `PENDING_OPERATOR_ACTION`, Parcel calls
`POST /internal/v1/trips/{sourceTripId}/cargo/transfer` with
`targetState=RESERVED,allowCapacityOverflow=false` and propagates the caller's UUID-v4
Idempotency-Key unchanged to Trip. It commits `tripId=targetTripId` and
`status=RESERVED` only after Trip has atomically released the source cargo and reserved the target
cargo. A capacity or dependency failure leaves the Parcel and source cargo unchanged.

Before that Trip call, Parcel persists a `TRANSFER` cargo-recovery operation whose id is the
public UUID-v4 `Idempotency-Key`. Only one `PENDING` cargo-recovery operation may exist per Parcel.
If the Trip outcome is unknown, a retry or the recurring recovery job replays the persisted
source/target/body and the same operation id. A definitive `404|409|422` closes the operation as
failed without changing Parcel state. Trip success is finalized with the Parcel status, operation,
Outbox event and local stats in one Parcel transaction.

Concurrent `/request-transfer` and `/return` calls for the same Parcel resolve to one durable
claim. The loser returns `409 PARCEL_CARGO_RECOVERY_IN_PROGRESS` while the winner is still
`PENDING`; it must not call Trip.

The retained `LOADED|IN_TRANSIT` request branch remains compatible: it records the target and
moves to `PENDING_TRANSFER_CONFIRM`; physical cargo is not moved until assigned target-Trip crew
confirms it.

Response `200` for the retained physical-transfer branch:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "status": "PENDING_TRANSFER_CONFIRM",
    "transferTargetTripId": "uuid"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

For `PENDING_OPERATOR_ACTION` recovery, response data instead contains the new `tripId`,
`status:"RESERVED"`, and `transferTargetTripId:null`.

Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND`, `404 TRIP_NOT_FOUND`,
`409 INVALID_STATUS`, `409 TRIP_CARGO_TRANSFER_CONFLICT`,
`422 TRIP_CARGO_CAPACITY_EXCEEDED`, and `503 TRIP_SERVICE_UNAVAILABLE`. Same-key replay returns
the original result; a different-key concurrent transfer has one Trip-ledger winner.

### POST `/v1/operator/parcels/{parcelId}/cancel`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. UUID-v4 `Idempotency-Key` is
required.

Request:

```json
{
  "reason": "Sender requested cancellation before loading",
  "refundChoice": "POLICY"
}
```

`reason` is required, trimmed, and 1–500 characters. `refundChoice` is exactly
`FULL|POLICY|NO`; omitted defaults to `POLICY`. During the compatibility window the legacy input
aliases `FULL_REFUND|POLICY_REFUND|NO_REFUND` are accepted and normalized.

Every pre-load status is supported:
`PENDING_OPERATOR_REVIEW|PENDING_PAYMENT|PENDING|PENDING_ADDITIONAL_PAYMENT|RESERVED|CHECKED_IN|PENDING_FINAL_PAYMENT|READY_TO_LOAD`.
Every supported manual-cancel status becomes `CANCELLED`; `REJECTED` remains reserved for
review/timeout flows. `FULL` refunds the outstanding collected amount
`outstanding=max(depositPaidVnd + balancePaidVnd - refundedAmountVnd,0)`; `NO` refunds zero.
`POLICY` is
`clamp(round(outstanding * (100-noShowFeePercent)/100, AwayFromZero),0,outstanding)`, with no
1,000-VND floor. A null policy defaults `noShowFeePercent=0`; the value must be finite and in
`[0,100]`. Malformed/out-of-range policy returns `503 UPSTREAM_UNAVAILABLE` without cargo/state/
refund changes. Any active cargo ledger is released before the Parcel transition commits. A
same-key replay returns the persisted result without releasing cargo or publishing refund facts
twice. Parcel propagates the caller's UUID-v4 key unchanged to Trip cargo release.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelId": "uuid",
    "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
    "status": "CANCELLED",
    "tripId": "uuid",
    "refundChoice": "POLICY",
    "refundAmount": 35000
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-30T10:00:00+07:00" }
}
```

Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND`, `409 INVALID_STATUS`,
`409 TRIP_CARGO_TRANSFER_CONFLICT`, `422 INVALID_REFUND_CHOICE`, and
`503 TRIP_SERVICE_UNAVAILABLE|UPSTREAM_UNAVAILABLE` (the latter is malformed or unavailable
policy data).

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

### POST `/v1/operator/parcels/{parcelId}/return`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF` for the Parcel operator. UUID-v4 `Idempotency-Key` is
required. Valid only for `PENDING_OPERATOR_ACTION|TRANSFER_ESCALATED`.

Request:
```json
{
  "returnReason": "Sender requested return after trip disruption"
}
```

Trip cargo release must succeed before Parcel commits `RETURNED`. The handler then emits exactly
one refund for the remaining collected amount
`max(depositPaidVnd + balancePaidVnd - refundedAmountVnd,0)`. Replay or a losing CAS does not
release cargo or refund again. Parcel propagates the caller's UUID-v4 key unchanged to Trip.

For event-driven Trip cancellation/disruption, Parcel deterministically derives one UUID with the
UUID version/variant bits set to v4 from `(sourceEventId,parcelId,cargoAction)` and reuses it on
every retry. Different Parcels/actions therefore never collide in Trip's idempotency store.

Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND`, `409 INVALID_STATUS`,
`409 TRIP_CARGO_TRANSFER_CONFLICT`, `422 VALIDATION_ERROR`, and
`503 TRIP_SERVICE_UNAVAILABLE`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

## Parcel Reliability v2

All mutation endpoints in this section require UUID-v4 `Idempotency-Key`, return ADR 0004
`ApiResponse<T>`, and are tenant-fenced. Public trace never exposes crew PII, internal notes, raw
evidence tokens, or continuous GPS.

### GET `/internal/v1/trips/{tripId}/operational-location`

Auth: Internal JWT. Caller: Parcel. Success is a raw internal DTO:

```json
{
  "tripId": "uuid",
  "tripStatus": "IN_PROGRESS",
  "vehicleId": "uuid",
  "currentStopId": "uuid|null",
  "currentStopStatus": "ARRIVED|null",
  "actualArrivalAt": "2026-08-20T10:00:00Z|null",
  "actualDepartureAt": null,
  "destinationArrivedAt": null
}
```

`currentStopId` is present only for the current `ARRIVED` and not-yet-departed stop. Missing Trip is
`404 TRIP_NOT_FOUND` in an ADR 0004 error envelope.

### GET `/v1/parcels/{parcelId}/trace`

Auth: sender, linked recipient, or same-tenant operator. This is the single screen-ready Passenger
tracking read. Response data contains `parcelSummary,operator,trip,dropoffLocation,currentCustody?,
activeIncident?,forwardingTrip?,claimSummary?,availableActions[],timeline,incidents[],nextUpdateAt?`.
`claimSummary` is omitted/null for a recipient and visible to the sender. `timeline` is
`{items,nextCursor}`; default/max page is 50/100 and `cursor` loads older sequence values. Timeline
entries do not disclose actor identity, internal investigation notes, raw evidence, or a public
reason. GPS never becomes custody proof.

`GET /v1/parcels/sent` and `/received` items add `operator,dropoffLocation,reliability`; only sent
items include claim status. `GET /v1/parcels/{parcelId}` adds `operator,trip,dropoffLocation,
compensationPolicySnapshot,reliabilitySummary,availableActions`. These are additive batch
projections: a page performs one Parcel projection query and at most one Trip batch plus one
Identity batch; nullable display enrichment degrades without hiding the Parcel fact.

### Passenger incident and claim APIs

| Endpoint | Auth/body | Response |
|---|---|---|
| `POST /v1/parcels/{parcelId}/incidents` | Sender or linked recipient; `{ incidentType, description?, evidenceUrls? }` | `201` incident summary; one active incident per Parcel/type |
| `GET /v1/parcels/{parcelId}/incidents` | Sender, linked recipient, same-tenant operator | `200` incident summaries |
| `POST /v1/parcels/{parcelId}/claims` | Sender `PASSENGER`; bodyless | `201` snapshotted claim; requires `LOST_CONFIRMED` within claim window |
| `POST /v1/parcels/{parcelId}/claims/{claimId}/evidence` | Sender; `{ evidenceType, reference, note? }` | `201` updated claim with all evidence metadata, deadlines, frozen policy and `availableActions`; no raw upload token |
| `POST /v1/parcels/{parcelId}/claims/{claimId}/appeal` | Sender; `{ reason }`; claim must be `PAID` or `REJECTED`; UUID-v4 `Idempotency-Key` | `200` original claim unchanged, with the new separate appeal in `data.appeal` |
| `GET /v1/parcels/{parcelId}/claims` | Sender or same-tenant operator; recipient is not authorized | `200` claims and evidence metadata |

The sender is always `beneficiaryUserId`. Claim response freezes `declaredValueVnd,
provenDirectLossVnd,compensationRatePercent,policyCapVnd,cargoAwardVnd,freightRefundVnd,
totalAwardVnd,policyVersion,status,decisionReason,decidedBy,decidedAt,payoutReferenceId,paidAt`.
Legacy `appealReason,appealedByUserId,appealedAt` remain nullable compatibility fields; new writes
use the nested `appeal` resource. The original claim remains `PAID` or `REJECTED` and is never
mutated to `APPEALED`.

Passenger incident creation permits only `DELIVERY_NOT_RECEIVED|DAMAGED|PARTIAL_LOSS` while the
Parcel is `UNLOADED|DELIVERED_PENDING_CONFIRM`. Any other status, including
`DELIVERY_CONFIRMED`, returns `409 PARCEL_INCIDENT_STATUS_NOT_REPORTABLE` with fields
`status,incidentType,allowedStatuses`; no incident, task, custody event, quarantine transition or
Outbox row is committed.

`data.appeal` has
`appealId,claimId,originalClaimStatus,originalTotalAwardVnd,status,reason,submittedByUserId,
submittedAt,revisedProvenDirectLossVnd,revisedCargoAwardVnd,revisedFreightRefundVnd,
revisedTotalAwardVnd,supplementaryAwardVnd,decisionReason,decidedByUserId,decidedAt,
payoutReferenceId,paidAt,availableActions`. One claim may have at most one appeal.

### Assistant/station custody APIs

`GET /v1/assistant/trips/{tripId}/parcels` accepts `stopId,status,hasException,search,page,pageSize`
and returns one manifest screen model: `tripContext {trip,status,route,vehicle,
currentOperationalLocation,orderedStops},summary,items,pagination`. Each item includes named
`dropoffLocation,currentCustody,activeIncident,paymentState,identityCheckHints,availableActions,
custodyExceptionApproval?`. The shared `GET /v1/crew/trips/{tripId}/parcels` uses the caller role:
Assistant gets physical cargo actions; Driver gets approval actions and the pending report summary.
`pagination` is always the nested object `{page,pageSize,totalItems,totalPages,hasNextPage,
hasPreviousPage}`.

QR scan, check-in, load, unload, custody scan, custody exception and deliver return the common
`{parcelState,currentCustody,activeIncident,createdCustodyEvent,availableActions,warning}` model so
the Mobile app updates its card without refetching the manifest.

#### POST `/v1/assistant/parcels/{parcelId}/custody-scan`

Body `{ parcelCode, eventType, actualLocationType, actualLocationId?, locationSnapshot?,
evidenceReferences?, reason? }`. Direct scan event type is limited to
`ACCEPTED|ARRIVED_AT_STOP|HANDOFF|RETURNED_TO_STATION`. QR mismatch returns
`409 SCAN_IDENTITY_MISMATCH`; invalid/missing location returns
`422 PARCEL_CUSTODY_LOCATION_REQUIRED`. `ARRIVED_AT_STOP` and stop `HANDOFF` must match the Trip's
current `ARRIVED`, not-yet-departed operational stop. `ACCEPTED` must be at the Trip origin before
load. A supplied `VEHICLE` identity must match the Trip vehicle. Normal check-in, load, unload,
deliver, and confirm-found mutations already append their own custody facts, so FE must not call
`custody-scan` again after those actions. The backend exposes `CUSTODY_SCAN` only when a direct
supplemental custody fact is valid in the current operational context.

`POST /v1/stations/parcels/{parcelId}/handoff` is a station-facing alias with the same body and
custody semantics.

#### POST `/v1/assistant/parcels/{parcelId}/confirm-found-on-vehicle`

Assigned Assistant only. Requires an `Idempotency-Key` UUID and a fresh QR scan:

```json
{
  "incidentId": "uuid",
  "parcelCode": "VR-PCL-20260830-ABC123",
  "evidenceReferences": ["https://..."],
  "note": "Found in the vehicle cargo bay"
}
```

`evidenceReferences` and `note` are optional. This recovery is limited to an active,
system-created `MISSING|MISSING_AFTER_DEPARTURE` incident whose Parcel is
`PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION` with frozen resume status `LOADED|IN_TRANSIT`.
It verifies the assigned Assistant and QR, reads the Trip vehicle, appends `FOUND` at `VEHICLE`,
cancels outstanding search tasks, resolves the incident with
`CREW_CONFIRMED_ON_VEHICLE`, and restores the frozen Parcel status atomically. It does not apply
to Assistant-reported custody exceptions awaiting supervisor approval.

Response `200` is the common
`{parcelState,currentCustody,activeIncident,createdCustodyEvent,availableActions,warning}` model.
The restored card can immediately expose `UNLOAD` when the prior status was `IN_TRANSIT`.
Errors: `403 FORBIDDEN`, `404 PARCEL_NOT_FOUND|PARCEL_INCIDENT_NOT_FOUND|TRIP_NOT_FOUND`,
`409 SCAN_IDENTITY_MISMATCH|PARCEL_INCIDENT_INVALID_STATUS|INVALID_STATUS|IDEMPOTENCY_KEY_REUSED`,
`422 VALIDATION_ERROR`, `503 TRIP_SERVICE_UNAVAILABLE`.

#### POST `/v1/assistant/parcels/{parcelId}/custody-exception`

Body:

```json
{
  "incidentType": "WRONG_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "uuid|null",
  "locationSnapshot": "Bến B",
  "temporaryExceptionTag": "TMP-20260820-001|null",
  "description": "Kiện dỡ ngoài luồng scan",
  "observedWeightKg": 12.5,
  "evidenceUrls": ["https://..."],
  "reason": "Physical unload already occurred"
}
```

Requires the assigned Assistant's JWT and `Idempotency-Key`. Returns `202` with
`{requestId,parcelId,incidentId,incidentType,incidentStatus,status,actualLocationType,
actualLocationId,locationSnapshot,temporaryExceptionTag,description,observedWeightKg,
evidenceReferences,reason,reportedByUserId,reportedByRole,reportedAt,reviewedAt,reviewedByRole,
reviewedByUserId,reviewNote,approvedCustodyEventId,searchDeadline,availableActions}` where
`searchDeadline=null` while approval is pending. Submission opens the
approval-pending incident and moves the Parcel to `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION`, but
does not append `MANUAL_CUSTODY_EXCEPTION`, start the search SLA/tasks, or allow recovery, lost or
claim actions until approval.

#### GET `/v1/crew/parcels/{parcelId}/custody-exception`

Assigned Driver only. Returns the latest report with physical location, reason/evidence, review
audit and `availableActions=[APPROVE,REJECT]` while pending. This lets Driver Mobile render the
review screen without trusting any assistant-supplied reviewer identity.

#### POST `/v1/crew/parcels/{parcelId}/custody-exception-decision`

Assigned Driver only. Body `{ "decision": "APPROVE|REJECT", "note": "optional" }`. Reviewer
identity is taken from the Driver JWT; no reviewer UUID is accepted. Approval appends the reported
`MANUAL_CUSTODY_EXCEPTION` and starts the search SLA/tasks. Rejection records `SUPERVISOR_REJECTED`, closes the false report, and
restores the Parcel resume status. Returns the updated approval request.

#### POST `/v1/operator/parcel-incidents/{incidentId}/custody-exception-decision`

Same behavior for same-tenant `OPERATOR_STAFF|OPERATOR_ADMIN`. The reviewer identity and role are
taken from the caller JWT. Both decision endpoints require `Idempotency-Key`; a second decision
returns `409 PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED`.

`GET /v1/operator/parcel-incidents/{incidentId}` includes nullable `custodyExceptionApproval`
with the same report/evidence/review shape, so Operator Web can review and decide from one detail
request.

#### POST `/v1/assistant/trips/{tripId}/stops/{stopId}/reconcile`

Body is optional. Use `{}` for normal reconciliation or
`{ "departureOverrideReason": "..." }` only to request permission to leave with unresolved cargo.
The Assistant cannot submit scan IDs, manual-exception IDs, or a reviewer UUID. Parcel derives all
counts from persisted append-only custody facts. Response data:

```json
{
  "expectedCount": 4,
  "scannedCount": 2,
  "manualExceptionCount": 1,
  "unresolvedParcels": [{
    "parcelId": "uuid",
    "parcelCode": "VRP-...",
    "photoUrl": "https://...",
    "expectedDropoff": { "type": "ROUTE_STOP", "id": "uuid", "name": "Bến C" },
    "lastCustody": null,
    "incidentId": "uuid",
    "incidentType": "UNSCANNED_HANDOFF",
    "reason": "No verified unload or manual custody event exists for this stop.",
    "recommendedAction": "SEARCH_VEHICLE_OR_STATION"
  }],
  "canDepart": false,
  "requiresSupervisorApproval": true,
  "departureOverrideRequest": {
    "requestId": "uuid",
    "tripId": "uuid",
    "stopId": "uuid",
    "operatorId": "uuid",
    "unresolvedParcelIds": ["uuid"],
    "departureOverrideReason": "Operational emergency",
    "status": "PENDING_APPROVAL",
    "requestedByUserId": "uuid",
    "requestedByRole": "ASSISTANT",
    "requestedAt": "2026-08-29T16:30:00Z",
    "reviewedByUserId": null,
    "reviewedByRole": null,
    "reviewedAt": null,
    "reviewNote": null,
    "availableActions": ["APPROVE", "REJECT"]
  }
}
```

With unresolved Parcels, `departureOverrideReason` creates or replays a `PENDING_APPROVAL`
request for the exact unresolved snapshot. It does not authorize departure. Each unresolved Parcel
opens `UNSCANNED_HANDOFF`; a committed Trip departure event may additionally open
`MISSING_AFTER_DEPARTURE`. No scan gap directly confirms loss.
`scannedCount` and `manualExceptionCount` come only from matching append-only `UNLOADED` or
approved `MANUAL_CUSTODY_EXCEPTION` facts for the same Trip and stop. FE cannot manufacture a
successful reconciliation by sending Parcel IDs.

#### POST `/v1/assistant/trips/{tripId}/destination/reconcile`

Bodyless. The assigned Assistant calls this after terminal unload attempts and before Driver
completion. Parcel derives terminal `scannedCount`, `manualExceptionCount`, and
`unresolvedParcels` from persisted custody facts. Response adds
`canCompleteTrip,allExpectedParcelsDelivered`, retains `requiresDriverCompletion`, and keeps
`canComplete` as a deprecated one-release alias equal to `canCompleteTrip`. The values come from
the same completion-clearance policy used by Trip: `CLEAR` and `ACKNOWLEDGED_INCIDENTS` set
`canCompleteTrip=true`; only the latter sets `requiresDriverCompletion=true`.
`allExpectedParcelsDelivered` is true only when `scannedCount == expectedCount`; acknowledged
manual exceptions never count as delivered. The client does not submit Parcel ID lists.

#### GET `/v1/crew/parcel-approval-requests`

Assigned `DRIVER` only. Query:
`type=CUSTODY_EXCEPTION|STOP_DEPARTURE`, `status=PENDING_APPROVAL` (default and only supported
status), `page=1`, `pageSize=20` (1–100). Parcel merges both approval resources, batch-fetches Trip
assignment snapshots, filters by tenant/current assigned Driver before paging, and returns:

```json
{
  "items": [{
    "requestId": "uuid",
    "requestType": "CUSTODY_EXCEPTION|STOP_DEPARTURE",
    "status": "PENDING_APPROVAL",
    "tripId": "uuid",
    "parcelId": "uuid|null",
    "incidentId": "uuid|null",
    "stopId": "uuid|null",
    "unresolvedParcelIds": ["uuid"],
    "reason": "Operational exception",
    "evidenceReferences": ["https://..."],
    "requestedByUserId": "uuid",
    "requestedAt": "2026-08-31T09:00:00Z",
    "expiresAt": null,
    "validityCondition": "WHILE_PENDING_AND_CURRENT_TRIP_ASSIGNMENT",
    "availableActions": ["APPROVE", "REJECT"]
  }],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

Requests have no TTL. Stop departure, Trip terminal, changed unresolved snapshot, invalidated
custody state, or removal of the assigned Driver cancels a pending request. On Driver reassignment,
the old Driver immediately loses visibility/decision authority; a still-valid request retains its
identity and emits a new notification fact targeted to the new Driver.

#### Stop-departure approval APIs

| Endpoint | Auth/body | Response |
|---|---|---|
| `GET /v1/crew/parcel-stop-departure-approvals/{requestId}` | Assigned `DRIVER` | `200` `ParcelStopDepartureApprovalResponse` |
| `POST /v1/crew/parcel-stop-departure-approvals/{requestId}/decision` | Assigned `DRIVER`; `{ decision: "APPROVE|REJECT", note? }`; UUID-v4 `Idempotency-Key` | `200` decided request |
| `GET /v1/operator/parcel-stop-departure-approvals/{requestId}` | Same-tenant `OPERATOR_STAFF|OPERATOR_ADMIN` | `200` request |
| `POST /v1/operator/parcel-stop-departure-approvals/{requestId}/decision` | Same-tenant `OPERATOR_STAFF|OPERATOR_ADMIN`; same body/header | `200` decided request |

The reviewer is always the caller from JWT. A client-supplied reviewer user ID is not accepted.
A second decision returns `409 PARCEL_STOP_DEPARTURE_ALREADY_DECIDED`; a hidden/missing request
returns `404 PARCEL_STOP_DEPARTURE_APPROVAL_NOT_FOUND`.

#### GET `/internal/v1/parcels/trips/{tripId}/stops/{stopId}/departure-clearance`

Auth: Internal JWT. Query: required `operatorId` UUID. Caller: Trip, immediately before committing
the stop departure. Success is a raw internal DTO:

```json
{
  "tripId": "uuid",
  "stopId": "uuid",
  "operatorId": "uuid",
  "status": "CLEAR|APPROVED_OVERRIDE|BLOCKED_PENDING_APPROVAL",
  "unresolvedParcelIds": ["uuid"],
  "approvalRequestId": "uuid|null",
  "approvedByUserId": "uuid|null",
  "approvedAt": "2026-08-29T16:35:00Z|null"
}
```

Trip permits departure only for `CLEAR|APPROVED_OVERRIDE`. For
`BLOCKED_PENDING_APPROVAL`, Driver/Assistant departure returns
`409 PARCEL_STOP_RECONCILIATION_REQUIRED` with structured fields
`approvalRequestId,unresolvedParcelIds,requiredAction`. Parcel timeout, invalid response or other
upstream failure maps to `502 UPSTREAM_UNAVAILABLE`; departure is not persisted.

#### Unidentified package APIs

`POST /v1/stations/parcels/unidentified` accepts `{ temporaryExceptionTag, tripId?, locationType,
locationId, locationSnapshot?, description, observedWeightKg?, evidenceReferences? }` and creates
an `UNIDENTIFIED_PACKAGE`. `POST /v1/stations/parcels/unidentified/{packageId}/match` accepts
`{ parcelId }`, marks the temporary record matched, and appends `IDENTIFIED_MANUALLY` for the real
Parcel. Both require same-tenant station/operator authorization.

Operator reads are `GET /v1/operator/unidentified-packages`, `GET .../{packageId}`, and
`GET .../{packageId}/match-candidates`. Candidate items include Parcel code, Trip/route/vehicle,
photo, description, weight, expected stop, and explicit match reasons. Search never auto-matches;
the existing station `POST .../match` remains the supervisor confirmation.

### Operator incident/search/forwarding APIs

The Parcel route deliberately uses `/v1/operator/parcel-incidents` to avoid the Trip operational
incident resource at `/v1/operator/incidents`.

| Endpoint | Body/semantics |
|---|---|
| `GET /v1/operator/parcel-incidents?status=&type=&search=&tripId=&assigneeId=&slaState=&approvalStatus=&from=&to=&page=&pageSize=` | Paged same-tenant screen rows with Parcel, Trip/route/vehicle, expected dropoff, last custody, reporter, task/assignee summary, claim summary, SLA and actions. `approvalStatus` accepts `PENDING_APPROVAL|APPROVED|REJECTED|CANCELLED`. |
| `GET /v1/operator/parcel-incidents/{incidentId}?beforeSequence=&limit=` | Incident, Parcel parties, Trip, current custody, cursor timeline (50 default), enriched assignees, forwarding summary, linked claim and actions |
| `GET .../{incidentId}/forwarding-options?limit=` | Trip-owned route/cargo-compatible choices; returns `503 UPSTREAM_UNAVAILABLE` when Trip cannot calculate |
| `POST .../{incidentId}/assign` | `{ assigneeUserId }`; creates the standard search task set |
| `POST .../{incidentId}/search-scan` | `{ taskId, found, result, evidenceReferences? }` |
| `POST .../{incidentId}/mark-found` | `{ actualLocationType, actualLocationId?, locationSnapshot?, evidenceReferences?, note? }`; appends `FOUND` |
| `POST .../{incidentId}/forward` | `{ targetTripId }`; creates a `PLANNED` leg and returns target Trip, leg, cargo-transfer status and next handoff action |
| `POST .../{incidentId}/resolve` | `{ resolutionCode, note? }`; only recovered/forwarding cases; restores pending Parcel state |
| `POST .../{incidentId}/declare-lost` | `{ note? }`; only at/after search deadline unless already system-expired |

All incident mutations return the updated incident detail read model; FE does not refetch. The
forward response additionally carries `forwardingOperation: { targetTrip, newLeg,
cargoTransferStatus, nextHandoffAction }`. Initially the status is
`AWAITING_CREW_CONFIRMATION` and the action is `CREW_CONFIRM_TRANSFER`; after the assigned crew
confirms custody, the planned leg becomes `ACTIVE`, cargo is transferred with a fresh capacity
check, and the status/action become `TRANSFERRED`/`DELIVER_AT_EXPECTED_DROPOFF`.
Forwarding never edits the old leg and never creates a sender charge. If the Parcel remains on the
original vehicle before its expected stop, no new leg is created.

### Operator claim and policy APIs

`GET /v1/operator/claims?status=&search=&slaState=&from=&to=&page=&pageSize=` is the paged claim
queue. Rows contain Parcel, sender, incident, evidence count, policy snapshot, award, deadline,
funding state, Trip, and actions. `GET /v1/operator/claims/{claimId}` is the single claim-detail
read with evidence, custody, incident, Trip and beneficiary. Staff may read; only
`OPERATOR_ADMIN` may decide.

`POST /v1/operator/claims/{claimId}/decision` requires `OPERATOR_ADMIN` and body
`{ decision: "APPROVE|REJECT", provenDirectLossVnd?, reason }`. Approval calculates the award from
the Parcel's frozen policy; the client cannot provide rate, cap, or award. Approval emits
`parcel.claim.decided`; rejection does not call Payment.

Claim appeals are a separate resource:

| Endpoint | Auth/body | Response |
|---|---|---|
| `GET /v1/operator/claim-appeals?status=&page=&pageSize=` | Same-tenant `OPERATOR_STAFF|OPERATOR_ADMIN`; `status` is a `ParcelClaimAppealStatus` name | `200` `PagedResult<ParcelClaimAppealResponse>` |
| `GET /v1/operator/claim-appeals/{appealId}` | Same roles | `200` appeal detail |
| `POST /v1/operator/claim-appeals/{appealId}/decision` | `OPERATOR_ADMIN`; UUID-v4 `Idempotency-Key`; `{ decision: "UPHOLD|APPROVE_ADJUSTMENT", revisedProvenDirectLossVnd?, reason }` | `200` decided appeal |

`UPHOLD` keeps the original outcome and creates no payout. `APPROVE_ADJUSTMENT` recalculates with
the original frozen rate/cap/fallback and requires the revised total award to exceed the original
paid award. Payment receives only `supplementaryAwardVnd`; the payout unique reference is
`appealId`, not the old claim payout reference. Insufficient operator funds move the appeal to
`FUNDING_PENDING`; a successful compensation event moves it to `PAID`.

`GET /v1/operator/policies/parcel-compensation` returns the active policy. PUT on the same path
accepts:

```json
{
  "compensationRatePercent": 50,
  "maxCompensationVnd": 30000000,
  "noProofFallbackMultiplier": 4,
  "claimWindowDays": 30,
  "searchSlaHours": 72,
  "decisionSlaBusinessDays": 7,
  "payoutSlaBusinessDays": 3,
  "belowDefaultAcknowledged": false
}
```

Rate must be `1..100`; monetary/SLA/window fields must be positive. A rate below 50 or cap below
30,000,000 requires `belowDefaultAcknowledged=true`. Updates create a new version; accepted Parcel
snapshots never change.

Policy GET/PUT responses additionally return `platformDefaultPolicy,isBelowPlatformDefault,
effectiveForNewParcelsOnly=true,updatedAt,updatedBy`, so Operator Web never hard-codes 50%/30m.

Internal-JWT-only `POST /internal/v1/trips/forwarding-options` accepts operator, excluded Trip,
pickup/target typed locations, cargo dimensions, earliest departure and limit. Trip alone evaluates
route order, operator ownership, lifecycle and cargo capacity and returns enriched choices; it is
never exposed through Gateway.

### Reliability errors

`PARCEL_SCAN_REQUIRED`, `PARCEL_CUSTODY_LOCATION_REQUIRED`, `PARCEL_CUSTODY_LOCATION_MISMATCH`,
`PARCEL_CUSTODY_EVENT_DUPLICATE`, `PARCEL_CUSTODY_EVENT_NOT_FOUND`,
`SCAN_IDENTITY_MISMATCH`, `PACKAGE_IDENTITY_MISMATCH`,
`UNIDENTIFIED_PACKAGE_NOT_FOUND`, `PARCEL_INCIDENT_NOT_FOUND`, `PARCEL_INCIDENT_ALREADY_OPEN`,
`PARCEL_INCIDENT_INVALID_STATUS`, `PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND`,
`PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED`,
`PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED`, `PARCEL_SEARCH_TASK_NOT_FOUND`, `PARCEL_SEARCH_TASK_MISMATCH`,
`PARCEL_SEARCH_SLA_NOT_EXPIRED`, `PARCEL_CLAIM_NOT_FOUND`, `PARCEL_CLAIM_WINDOW_NOT_OPEN`,
`PARCEL_INCIDENT_CLAIM_WINDOW_EXPIRED`, `PARCEL_CLAIM_ALREADY_EXISTS`,
`PARCEL_CLAIM_EVIDENCE_REQUIRED`, `PARCEL_CLAIM_VALUE_EXCEEDS_POLICY`,
`PARCEL_CLAIM_ALREADY_DECIDED`, `PARCEL_CLAIM_APPEAL_NOT_ALLOWED`,
`PARCEL_CLAIM_APPEAL_ALREADY_EXISTS`, `PARCEL_CLAIM_APPEAL_NOT_FOUND`,
`PARCEL_CLAIM_APPEAL_ALREADY_DECIDED`, `PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED`,
`PARCEL_CLAIM_FUNDING_PENDING`, `PARCEL_STOP_DEPARTURE_APPROVAL_NOT_FOUND`,
`PARCEL_STOP_DEPARTURE_ALREADY_DECIDED`, `PARCEL_STOP_RECONCILIATION_REQUIRED`,
`POLICY_BELOW_DEFAULT_ACK_REQUIRED`.

## Payment & Wallet Service

### GET `/v1/payments/vnpay-return-status`

Auth: public VNPay Web return. The complete VNPay query string, including
`vnp_TxnRef`, `vnp_TmnCode`, and `vnp_SecureHash`, is required. Payment verifies
HMAC-SHA512, configured merchant, amount, and persisted `OPERATOR_WEB` mode before reading the transaction.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-24T17:00:00+07:00" }
}
```

The Manager Web SPA uses this read-only resource after VNPay redirects it to
`VNPAY_WEB_RETURN_URL`. There is no Gateway payment bridge. Errors:
`401 PAYMENT_SIGNATURE_INVALID`, `404 PAYMENT_NOT_FOUND`, `422 PAYMENT_AMOUNT_INVALID`.

### GET `/v1/payments/vnpay-mobile-sdk-return`

Auth: public, authenticated by the signed VNPay query. This is the technical return URL configured
as `VNPAY_MOBILE_SDK_RETURN_URL`. Payment verifies HMAC, merchant, transaction reference, amount,
and persisted `MOBILE_SDK` mode, performs no database or Outbox mutation, then returns a raw `302`:

- signed `vnp_ResponseCode=00` and `vnp_TransactionStatus=00` -> `http://success.sdk.merchantbackapp`
- signed `vnp_ResponseCode=24` -> `http://cancel.sdk.merchantbackapp`
- any other authentic result -> `http://fail.sdk.merchantbackapp`

Invalid signature/session/amount/mode returns `400`. The VNPay IPN remains the only state-mutating
source of truth.

### GET `/v1/payments/sessions/{sessionId}`

Auth: owning `PASSENGER`. Looks up both Payment and wallet Top-up sessions, returns `404` on an
ownership mismatch, and normalizes status to `PENDING|SUCCEEDED|FAILED|EXPIRED|REFUNDED`.

### POST `/v1/wallet/top-up`

Auth: required. Idempotency: required.

Request:
```json
{
  "amount": 500000,
  "method": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
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
    "paymentRedirectUrl": "https://vnpay.vn/...",
    "paymentReturnMode": "MOBILE_SDK",
    "vnpaySdk": { "tmnCode": "merchant-code", "scheme": "vietride", "isSandbox": false }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Missing `paymentReturnMode` returns `426 MOBILE_APP_UPDATE_REQUIRED`; an unsupported mode returns
`422 PAYMENT_RETURN_MODE_INVALID`; a disabled Mobile channel returns
`503 VNPAY_MOBILE_SDK_DISABLED` without a bridge fallback.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "method": "VNPAY",
  "dueAt": "2026-07-30T12:00:00Z",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "paymentId": "uuid",
    "status": "PENDING_REDIRECT",
    "paymentRedirectUrl": "https://sandbox.vnpayment.vn/...",
    "dueAt": "2026-07-30T12:00:00Z",
    "paymentReturnMode": "MOBILE_SDK",
    "vnpaySdk": { "tmnCode": "merchant-code", "scheme": "vietride", "isSandbox": false }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

The request may omit `dueAt` to use the 15-minute default. Booking VNPay supplies the exact
one-way Trip seat-lock expiry or the earlier round-trip leg expiry. `dueAt <= now` returns
`422 PAYMENT_DEADLINE_PASSED`. A persisted non-null deadline is authoritative; historical
persisted-null Payment rows use the legacy `CreatedAt + 15 minutes` fallback.
For `method=VNPAY`, `paymentReturnMode` is required and must be `MOBILE_SDK`; missing mode returns
`426 MOBILE_APP_UPDATE_REQUIRED`, invalid mode returns `422 PAYMENT_RETURN_MODE_INVALID`, and a
disabled channel returns `503 VNPAY_MOBILE_SDK_DISABLED`. For `method=WALLET`, callers omit the
mode and SDK metadata remains null.

### POST `/internal/v1/payments/redirect-sessions/lookup`

Auth: `X-Internal-Auth: Bearer <internal-jwt>`. Callers: Booking and Parcel. This read-only POST is
never exposed through Gateway, is marked `[SkipIdempotency]`, requires no `Idempotency-Key`, and returns
`Cache-Control: no-store`.

Request:

```json
{
  "userId": "uuid",
  "references": [
    {
      "referenceType": "BOOKING",
      "referenceId": "uuid"
    }
  ]
}
```

`userId` must be non-empty. `references` contains 1–100 unique composite
`(referenceType, referenceId)` values. Allowed case-sensitive types are exactly `BOOKING`,
`BOOKING_GROUP`, `PARCEL`, and `PARCEL_ADDITIONAL`; validation failures return
`422 VALIDATION_ERROR`.

Response `200` is a raw list:

```json
[
  {
    "paymentId": "uuid",
    "referenceType": "BOOKING",
    "referenceId": "uuid",
    "amount": 350000,
    "dueAt": "2026-07-30T12:00:00Z",
    "paymentRedirectUrl": "https://sandbox.vnpayment.vn/..."
  }
]
```

Payment performs one `AsNoTracking` database query. For each requested composite reference it
selects the latest Payment by `createdAt DESC, id DESC` before eligibility checks; an ineligible
latest attempt suppresses the item and never falls back to an older URL. Eligibility requires the
exact owner, valid immutable trusted context, `method=VNPAY`, `status=PENDING_REDIRECT`, persisted
non-null `dueAt > now`, and non-empty redirect URL. The URL must be absolute HTTPS, contain no
credentials, and have the exact authority (host and port) of configured `VNPAY_BASE_URL`.
Eligible results preserve request order; ineligible references are omitted. Signed URLs, query
strings, and response bodies must not be logged.

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

Response `200`: paged notifications. Each item retains the existing `id`, `userId`, `type`,
`title`, `body`, `data`, `readAt`, and `createdAt` fields and adds required semantic navigation:

```json
{
  "action": {
    "type": "OPEN_BOOKING_DETAIL",
    "params": { "bookingId": "uuid" }
  }
}
```

Allowed action types are `OPEN_BOOKING_DETAIL`, `OPEN_CREW_TRIP_BOOKING`, `OPEN_TRIP_DETAIL`,
`OPEN_TRIP_TRACKING`, `OPEN_PARCEL_DETAIL`, `OPEN_WALLET`, `OPEN_SUBSCRIPTION`,
`OPEN_SHUTTLE_TRACKING`, and `NONE`. `NONE` always has empty `params`. Missing or malformed legacy
navigation data resolves to `NONE`; it never fails the inbox read. IDs remain in `data` and
`action.params` for client navigation but system-generated `title`/`body` use human-readable
codes/names or a natural-language fallback instead of raw UUIDs. Existing rows are not backfilled.
For Shuttle notifications, `OPEN_SHUTTLE_TRACKING.params` always contains `shuttleTripId` and
additively preserves `bookingId` plus `pickupOrder` when the event identifies a passenger pickup,
so clients can select the correct stop when one Shuttle Trip serves multiple Booking groups.

`PARCEL_RESERVED` is emitted to the Assistant currently assigned to the Parcel's Trip only after
the sender's deposit succeeds and the Trip cargo reservation is confirmed. It is stored in the
Assistant inbox, queued for FCM push, and resolves to `OPEN_PARCEL_DETAIL` with
`action.params.parcelId`. Driver is not a recipient.

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

### Socket.IO `/notification/socket.io`

Notification realtime uses the default namespace `/` and connects directly through Nginx to
Notification Service; it is not routed through Gateway.

```ts
io("wss://api.vietride.app", {
  path: "/notification/socket.io",
  auth: { token: "<userAccessToken>" }
})
```

Authentication requires an Identity User Access Token (RS256). The server reads
`socket.handshake.auth.token` first and falls back to `Authorization: Bearer <token>` from the
handshake headers. After verification, the server owns room assignment and joins
`notification:user:{sub}` from the verified JWT `sub` claim. Clients cannot provide a `userId`,
select another room or emit a room-join event. Missing, invalid or expired credentials reject the
namespace connection with `connect_error.message = "UNAUTHORIZED"`.

Server event `notification:created` carries one raw notification DTO, without an `ApiResponse`
envelope:

```json
{
  "id": "uuid",
  "type": "BOOKING_CONFIRMED",
  "title": "Đặt vé thành công",
  "body": "Vé của bạn đã được xác nhận.",
  "data": { "bookingId": "uuid" },
  "action": {
    "type": "OPEN_BOOKING_DETAIL",
    "params": { "bookingId": "uuid" }
  },
  "readAt": null,
  "createdAt": "2026-08-11T15:30:00.000+07:00"
}
```

The payload intentionally omits `userId` and legacy `deepLink`; `action` follows the same semantic
resolver as the REST inbox. All instant fields use `Asia/Ho_Chi_Minh` and the resolved `+07:00`
offset. Notification creation persists the row and enqueues FCM before a best-effort realtime emit.
An emit failure is logged but never rolls back or fails the durable notification. Delivery is
at-least-once: replay may emit the same stable notification `id` again, so clients deduplicate by
`id`. `GET /v1/notifications` remains the durable source of truth after reconnect or missed events.

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
socket.emit("joinTripTracking", { tripId, includeRouteSnapshot?: false }, ack)
```

`includeRouteSnapshot` defaults to `false`, preserving the legacy payload and latency. When it is
`true`, Tracking obtains the current effective Route context before joining the room.

Success ack:
```json
{
  "success": true,
  "tripId": "uuid",
  "room": "trip:uuid",
  "scope": "PARCEL_RECIPIENT",
  "routeContext": { "tripId": "uuid", "geometry": null, "intermediateStops": [] },
  "routeVersion": "\"strong-etag-value\""
}
```

`routeContext` and `routeVersion` are present only for the opt-in request. `routeVersion` is the
same strong ETag used by REST route context. If the snapshot cannot be loaded, ack returns
`TRACKING_ROUTE_CONTEXT_UNAVAILABLE` and the socket is not joined to `trip:{tripId}`.

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
- `eta:batch:update`
- `trip:statusChanged`

Tracking broadcasts the operational event `trip:routeDeviation` only to
`trip:crew:{tripId}` and `operator:{operatorId}:fleet`; it is never sent to the shared
`trip:{tripId}` room:

```jsonc
{
  "tripId": "uuid",
  "status": "DEVIATED", // DEVIATED | ROUTE_RESTORED
  "distanceMeters": 850,
  "updatedAt": "2026-08-12T16:30:00+07:00"
}
```

Off-route means a raw GPS coordinate remained more than 500 metres from the cached effective
route for more than 120 seconds. The initial `DEVIATED` creates the existing durable
`tracking.gps.off_route` alert; while the vehicle remains off-route, the first accepted GPS update
at least 60 seconds after the previous realtime emit produces a `DEVIATED` heartbeat with a fresh
distance but no additional Outbox, FCM or inbox notification. The first GPS update at or within
500 metres then emits one `ROUTE_RESTORED`. `updatedAt` is the triggering GPS `recordedAt` instant
serialized by the public `Asia/Ho_Chi_Minh` convention. `distanceMeters` is a required non-negative
integer for both statuses.

Effective AlternativeRoute geometry is used when assigned. `STOPS_ONLY` remains a valid fallback;
missing/unavailable geometry or fewer than two points skips evaluation. Route changes reset the
off-route episode without emitting a false restore. `COMPLETED`, `CANCELLED` and `DISRUPTED` clear
runtime state without emitting `ROUTE_RESTORED`; FE clears its banner from the terminal Trip fact.

Driver và Assistant được Tracking tự động tham gia thêm room nội bộ
`trip:crew:{tripId}`. Khi manifest thay đổi, Tracking chỉ broadcast các event booking vào crew
room; Passenger room không nhận. `booking:created` được giữ tương thích, còn client mới dùng
`booking:updated` với `reason=BOOKING_CREATED|BOOKING_CANCELLED|PASSENGER_BOARDED|BOOKING_TRANSFERRED`.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-08-05T17:00:01.000+07:00",
  "bookingId": "uuid",
  "bookingCode": "VR-20260805-ABCDEFGH",
  "tripId": "uuid",
  "status": "CONFIRMED",
  "ticketCodes": ["VT-20260805-ABCDEFGH"],
  "seatNumbers": ["A01"],
  "departureDateTime": "2026-08-05T19:00:00.000+07:00",
  "passengerCount": 1,
  "pickup": { "stationId": "uuid", "stopId": null, "address": null },
  "dropoff": { "stationId": null, "stopId": "uuid", "address": null },
  "driverUserId": "uuid",
  "assistantUserId": "uuid"
}
```

`eventId` là identity bền vững cho Outbox, RabbitMQ, Notification và Tracking dedupe.
Notification lưu type `BOOKING_CREATED` hoặc crew-facing `BOOKING_CANCELLED` cho từng crew
recipient hiện có với dedupe riêng theo `eventId + recipientUserId`. FCM gửi cả `data.type` và
`data.notificationType` bằng đúng type; array trong FCM data được JSON stringify.

`booking:updated` là signal invalidate/refetch, không thay thế manifest/seat-map REST:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-08-05T17:00:01.000+07:00",
  "tripId": "uuid",
  "bookingId": "uuid",
  "reason": "PASSENGER_BOARDED",
  "bookingCode": "VR-20260805-ABCDEFGH",
  "seatNumbers": ["A01"],
  "passengerRecordId": "uuid",
  "ticketCode": "VT-20260805-ABCDEFGH",
  "boardedAt": "2026-08-05T17:00:01.000+07:00"
}
```

Cancellation chỉ fan-out crew khi `previousStatus=CONFIRMED`, và bỏ qua cancellation phát sinh do
toàn Trip đã terminal. Transfer phát cùng event vào cả crew room Trip cũ và mới, dedupe room nếu
hai Trip id trùng nhau.

Tracking Phase 10 invariants (legacy fields remain compatible; delay fields below are additive):

- GPS is projected onto cached Trip route geometry only when the nearest segment is at most 50 m
  from the raw coordinate. Published coordinates are used by `tracking:latest:{tripId}`, REST and
  Socket.IO. Raw coordinates are used by `tracking:gps_buffer:{tripId}`, `GpsTrail`, idempotency
  fingerprints and off-route detection.
- A geometry cache miss emits raw GPS immediately and warms Trip geometry asynchronously with
  in-flight request deduplication; the live GPS acknowledgement never waits for Trip HTTP.
- ETA uses cumulative route distance and monotonic stop sequence/progress guards. For
  `SCHEDULED|BOARDING`, one calculation targets the origin `STATION`; for `IN_PROGRESS`, it covers
  all remaining `PENDING` stops plus the destination `STATION`. Express Trips and Trips past their
  last intermediate stop therefore still target the destination. Goong Directions is primary only
  when `ROUTING_PROVIDER=GOONG`; `ROUTING_PROVIDER=LOCAL` and provider failures use Local Route
  ETA/direct route projection as the fallback.
  Provider calls are throttled to 60 seconds, require the next stop to change, more than 500 m
  movement, or a next-stop ETA under 15 minutes, and use one per-Trip Redis lock, atomic 60-second
  caches and a three-failure/300-second cooldown.
  A newly selected stop with no cache is calculated immediately even when the previous stop was
  calculated less than 60 seconds ago. `STOPS_ONLY` geometry is refreshed after 30 seconds;
  Route polyline geometry uses the configured longer cache TTL.
- Goong Directions v2 is the current provider contract and uses updated post-merger administrative-boundary
  data. A request is `GET {GOONG_BASE_URL}/v2/direction` with ordered `origin=lat,lng`, semicolon-
  separated ordered `destination` targets, `vehicle=car`, `alternatives=false` and query secret
  `api_key=GOONG_API_KEY`. `GOONG_MAX_DESTINATIONS_PER_REQUEST` defaults to 10. Longer chains are
  chunked without reordering and leg distance/duration are accumulated across chunks. Empty or
  malformed routes, negative/non-finite metrics, wrong leg count or changed waypoint order reject
  the whole batch. Full request URIs/query strings must never be logged.
- `401`, `403`, `429`, `5xx`, timeout, malformed JSON or strict-validation failures count toward the
  existing three-failure/300-second cooldown and use one consistent Local batch; partial provider
  output is never mixed with Local output. Default E2E uses a fake Goong HTTP server. Real Goong
  E2E is opt-in and must receive its key from the environment. Public responses expose only
  `estimateQuality=TRAFFIC_AWARE|ROUTE_BASED|FALLBACK`; `TRAFFIC_AWARE` is historical
  `GOOGLE_ROUTES`, `ROUTE_BASED` is Goong, and `FALLBACK` is Local. Provider names, snap metadata
  and traffic metadata
  remain internal.

`eta:update` remains a STOP-only legacy event, keeps the legacy boolean `delayed`, and adds the
following fields. Station targets are emitted only through `eta:batch:update`:

```json
{
  "tripId": "uuid",
  "stopId": "uuid",
  "etaMinutes": 12,
  "estimatedArrivalTime": "2026-08-05T17:12:00.000+07:00",
  "distanceMeters": 8500,
  "updatedAt": "2026-08-05T17:00:01.000+07:00",
  "estimateQuality": "ROUTE_BASED",
  "delayed": false,
  "delayStatus": "ON_TIME",
  "delayMinutes": 0
}
```

`eta:batch:update` is emitted after the same atomic cache write and contains the ordered physical
targets. `targetKind=STOP` uses `stopId`; origin/destination targets use `targetKind=STATION` and
`stationId`:

```json
{
  "tripId": "uuid",
  "etas": [
    {
      "targetKind": "STOP",
      "stopId": "uuid",
      "sequence": 2,
      "etaMinutes": 12,
      "estimatedArrivalTime": "2026-08-05T17:12:00.000+07:00",
      "distanceMeters": 8500,
      "updatedAt": "2026-08-05T17:00:01.000+07:00",
      "estimateQuality": "ROUTE_BASED"
    },
    {
      "targetKind": "STATION",
      "stationId": "uuid",
      "etaMinutes": 95,
      "estimatedArrivalTime": "2026-08-05T18:35:00.000+07:00",
      "distanceMeters": 112000,
      "updatedAt": "2026-08-05T17:00:01.000+07:00",
      "estimateQuality": "ROUTE_BASED"
    }
  ],
  "updatedAt": "2026-08-05T17:00:01.000+07:00"
}
```

`delayStatus` is `DELAYED`, `ON_TIME` or `UNKNOWN`. When evaluation fails, the server returns
`UNKNOWN`, preserves the last known boolean/delay minutes when available, and does not emit a
recovery event. `trip:statusChanged` is emitted only on a transition and has this shape:

```json
{
  "tripId": "uuid",
  "stopId": "uuid",
  "status": "DELAYED",
  "delayMinutes": 31,
  "updatedAt": "2026-08-05T17:00:01.000+07:00"
}
```

`status` is `DELAYED` when entering delay or when the current delayed stop changes, and
`DELAY_CLEARED` when a previously delayed stop is evaluated on time. Repeated ETA updates do not
repeat the transition. The same payload is used by `shared:eta:update` and
`shared:trip:statusChanged` after public-field filtering; `statusTransition` is internal and is
never sent over Socket.IO.

### GET `/v1/tracking/trips/{tripId}/eta`

Auth: Identity User Access Token and the same Tracking authorization used by the trip socket.
Query: legacy `stopId=<uuid>`, or the explicit pair
`targetKind=STOP&stopId=<uuid>` / `targetKind=STATION&stationId=<uuid>`. A station target must be
the effective origin or destination. When no target is supplied, Tracking walks the current
status-aware target chain and returns its first cached ETA. It does not call an ETA provider
synchronously.

Response `200` uses the ADR 0004 envelope with `data.eta`. A cache hit preserves the existing ETA
fields and adds:

```json
{
  "eta": {
    "tripId": "uuid",
    "stopId": "uuid",
    "etaMinutes": 12,
    "estimatedArrivalTime": "2026-08-05T17:12:00.000+07:00",
    "distanceMeters": 8500,
    "updatedAt": "2026-08-05T17:00:01.000+07:00",
    "estimateQuality": "ROUTE_BASED",
    "delayed": null,
    "delayStatus": "UNKNOWN",
    "delayMinutes": null
  }
}
```

`delayed` is nullable on REST because the current delay cannot be proven. The delay state is used
only when its `stopId` matches the requested ETA stop; a state belonging to another stop returns
`UNKNOWN` and does not clear or apply the warning. A reconnecting client should call this endpoint
to restore the latest known state instead of waiting for another socket event.

### GET `/v1/tracking/trips/{tripId}/etas`

Auth: Identity User Access Token and the same Tracking authorization used by the trip socket and
legacy `/eta` endpoint.

Response `200` uses the ADR 0004 envelope with `data.etas`, ordered by the current physical target
chain: origin only for `SCHEDULED|BOARDING`, or remaining stop sequence followed by destination for
`IN_PROGRESS`. Each item contains `targetKind`, `stopId?`, `stationId?`, `stopName`,
`sequence?`, `etaMinutes`, `estimatedArrivalTime`, `distanceMeters`, `updatedAt`, and
`estimateQuality`. Finalized `ARRIVED|SKIPPED` stops are omitted. A cold/expired cache returns
`{"etas":[]}`; this request never invokes Google synchronously.

### Shuttle tracking

Client joins the authorized Shuttle room:

```ts
socket.emit("joinShuttleTracking", { shuttleTripId }, ack)
```

Success ack preserves the existing common join shape; `tripId` contains the ShuttleTrip ID:

```json
{
  "success": true,
  "tripId": "uuid",
  "room": "shuttle:uuid",
  "scope": "PASSENGER|DRIVER|OPERATOR"
}
```

Only the assigned Shuttle driver may emit `shuttle:gps:update`:

```json
{
  "shuttleTripId": "uuid",
  "latitude": 10.762622,
  "longitude": 106.660172,
  "speedKmh": 30,
  "heading": 90,
  "recordedAt": "2026-08-01T08:00:00.000+07:00"
}
```

The server broadcasts the unchanged GPS payload as `shuttle:gps:update`, then may asynchronously
broadcast `shuttle:eta:update`:

```json
{
  "shuttleTripId": "uuid",
  "nextPickupOrder": 1,
  "etaMinutes": 17,
  "estimatedArrivalTime": "2026-08-01T08:17:00.000+07:00",
  "distanceMeters": 5909,
  "updatedAt": "2026-08-01T08:00:01.000+07:00"
}
```

REST fallback endpoints are:

- `GET /v1/tracking/shuttle-trips/{shuttleTripId}/latest`
- `GET /v1/tracking/shuttle-trips/{shuttleTripId}/eta`
- `GET /v1/tracking/shuttle-trips/{shuttleTripId}/passenger-context`

Shuttle ETA follows `pickupOrder`, skips terminal groups (`PICKED_UP`, `DELIVERED`, `NO_SHOW`,
`CANCELLED`), never regresses below the last published pickup order and uses the Station stop as the
final destination. Goong Directions is primary when `ROUTING_PROVIDER=GOONG`; direct-distance/speed ETA
is the local fallback because Shuttle has no fixed route geometry. Provider calls use a minimum
60-second interval, the existing 500 m movement or ETA-under-15-minute conditions, a per-Shuttle
pickup Redis lock, a 60-second cache and a three-failure/300-second Goong cooldown. GPS persistence,
broadcast and acknowledgement never wait for Goong HTTP. Shuttle state remains under
`tracking:shuttle:*` and does not enter main Trip `GpsTrail`, active-trip, off-route or delay chains.
No `etaSource` or provider metadata is added to the public payload. Default E2E uses a fake Goong
server; real Goong is opt-in and reads `GOONG_API_KEY` only from the environment.

### GET `/v1/tracking/trips/{tripId}/route-geometry`

Auth: Identity User Access Token. Người gọi phải vượt qua tracking authorization hiện hành:
Booking owner, Parcel sender/recipient, assigned Driver/Assistant hoặc cùng Operator tenant.

Response `200` dùng ADR 0004 envelope với `data`:

```json
{
  "tripId": "uuid",
  "tripStatus": "IN_PROGRESS",
  "geometry": {
    "source": "ROUTE_POLYLINE",
    "points": [{ "latitude": 10.0, "longitude": 106.0 }]
  },
  "originStation": {
    "stationId": "uuid",
    "name": "string",
    "latitude": 10.0,
    "longitude": 106.0
  },
  "intermediateStops": [
    {
      "stopId": "uuid",
      "name": "string",
      "sequence": 1,
      "latitude": 10.5,
      "longitude": 106.5
    }
  ],
  "destinationStation": {
    "stationId": "uuid",
    "name": "string",
    "latitude": 11.0,
    "longitude": 107.0
  }
}
```

- `geometry` chỉ chứa polyline thật của Route. Khi Route chưa có polyline, trả `geometry: null`
  nhưng vẫn trả các marker station/stop hợp lệ; client không nối các marker thành tuyến giả.
  Tracking chỉ cache fallback `STOPS_ONLY` trong 30 giây để polyline được Operator bổ sung qua
  `PUT /v1/operator/routes/{id}/geometry` xuất hiện trong Tracking mà không cần API mới.
- `originStation` và `destinationStation` nullable khi station chưa có tọa độ hợp lệ.
- `tripStatus` is the authoritative Trip lifecycle status used to select the ETA target chain.
- `intermediateStops` always comes from the ordered `TripStop` snapshot assigned to this Trip.
  With an AlternativeRoute, only polyline and destination are read from that effective Route; live
  AlternativeRoute stop edits never rewrite the Trip snapshot. A missing assigned AlternativeRoute
  returns `404 TRIP_NOT_FOUND` and never falls back to the base Route. Invalid/missing alternative
  polyline may use `STOPS_ONLY` for that same effective snapshot, never base-route geometry.
- Geometry loại tọa độ ngoài range/trùng liên tiếp, giản lược deterministic tối đa 1.000 điểm và
  luôn giữ điểm đầu/cuối. Public payload không chứa `alertRecipientUserIds`.
- Response đặt `Cache-Control: private, max-age=600` khi có geometry `ROUTE_POLYLINE`; fallback
  `geometry: null` đặt `private, max-age=30`. Cả hai response đều đặt `Vary: Authorization` và
  strong `ETag` tính từ DTO public sau sanitize/simplify. `If-None-Match` khớp trả `304` body rỗng sau
  khi auth.
- Errors: `400 VALIDATION_FAILED`; `401 UNAUTHORIZED`; `403 ACCESS_DENIED`; `404 TRIP_NOT_FOUND`;
  `503 TRACKING_AUTH_UNAVAILABLE`; `503 TRACKING_ROUTE_CONTEXT_UNAVAILABLE`.

### GET `/v1/tracking/shuttle-trips/{shuttleTripId}/passenger-context`

Auth: `PASSENGER`. Passenger được phép khi có ít nhất một manifest của chính họ ở `PENDING` hoặc
`PICKED_UP`; chỉ có terminal manifest (`DELIVERED`, `NO_SHOW`, `CANCELLED`) thì bị từ chối.

Response `200` dùng ADR 0004 envelope với `data`:

```json
{
  "shuttleTripId": "uuid",
  "mainTripId": "uuid",
  "direction": "INBOUND_TO_STATION",
  "ownPickups": [
    {
      "bookingId": "uuid",
      "pickupOrder": 3,
      "serviceAddress": "123 Nguyen Hue, Quan 1",
      "serviceOrder": 3,
      "roadDistanceMeters": 4200,
      "latitude": 10.0,
      "longitude": 106.0,
      "status": "PENDING",
      "stopsBeforePickup": 2
    }
  ],
  "station": {
    "stationId": "uuid",
    "name": "string",
    "latitude": 10.0,
    "longitude": 106.0,
    "pickupOrder": 8
  }
}
```

- Chỉ trả pickup thuộc user hiện tại và station công cộng; không trả booking ID, địa chỉ hoặc tọa
  độ của passenger khác. `station` nullable khi chưa có tọa độ hợp lệ.
- `PICKED_UP` luôn có `stopsBeforePickup=0`. Với `PENDING`, dùng Shuttle ETA `nextPickupOrder`;
  nếu chưa có ETA thì dùng non-terminal pickup đầu tiên, rồi chỉ trả số unique pickup group còn trước
  own pickup, không trả chi tiết các group đó.
- Response đặt `Cache-Control: private, no-store`.
- Errors: `400 VALIDATION_FAILED`; `401 UNAUTHORIZED`; `403 TRACKING_ACCESS_DENIED`;
  `404 SHUTTLE_TRIP_NOT_FOUND`; `503 TRACKING_AUTH_UNAVAILABLE`; `503 TRACKING_CONTEXT_UNAVAILABLE`.

### GET `/v1/tracking/shuttle-trips/{shuttleTripId}/operator-context`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF`. The caller is allowed only when the token `operatorId`
owns the Shuttle Trip. The endpoint reuses the same tenant authorization context as Shuttle latest,
ETA, and realtime room joins.

Response `200` uses the ADR 0004 envelope with `data`:

```json
{
  "shuttleTripId": "uuid",
  "mainTripId": "uuid",
  "direction": "INBOUND_TO_STATION",
  "status": "IN_PROGRESS",
  "stops": [
    {
      "pickupOrder": 1,
      "bookingId": "uuid",
      "latitude": 10.0,
      "longitude": 106.0,
      "status": "PENDING",
      "isStation": false,
      "serviceAddress": "123 Nguyen Hue, Quan 1",
      "serviceOrder": 1,
      "roadDistanceMeters": 4200,
      "passengerCount": 2,
      "pickedUpAt": null,
      "deliveredAt": null,
      "statusReason": null
    }
  ],
  "station": {
    "stationId": "uuid",
    "name": "string",
    "latitude": 10.0,
    "longitude": 106.0,
    "pickupOrder": 3
  }
}
```

- `stops` contains all ordered passenger and Station stops for the owned Shuttle Trip. Passenger
  stop status uses `PENDING`, `PICKED_UP`, `DELIVERED`, `NO_SHOW`, or `CANCELLED`; `bookingId` is
  `null` for the Station stop. `passengerCount` counts manifest passengers and is `null` for the
  Station stop. `pickedUpAt` and `deliveredAt` are nullable lifecycle timestamps. `statusReason`
  contains the reason for both `NO_SHOW` and `CANCELLED`, otherwise it is null. Passenger names and
  phone numbers are not returned, and `scheduledPickupTime` is not part of this runtime contract.
- Internal authorization markers such as `isOwnPickup` and distance snapshot compatibility fields
  are never returned. `station` is nullable when valid Station coordinates are unavailable.
- Response sets `Cache-Control: private, no-store` because passenger service addresses are PII.
- Errors: `400 VALIDATION_FAILED`; `401 UNAUTHORIZED`; `403 TRACKING_ACCESS_DENIED`;
  `404 SHUTTLE_TRIP_NOT_FOUND`; `503 TRACKING_AUTH_UNAVAILABLE`; `503 TRACKING_CONTEXT_UNAVAILABLE`.

### Chia sẻ Main Trip cho người thân

Chỉ `PASSENGER` có Booking ownership của Main Trip được quản lý link. Trip phải chính xác
`IN_PROGRESS` khi tạo link; Parcel ownership không cấp quyền. Một grant active tồn tại cho mỗi
`(tripId, passengerUserId)`, vì vậy nhiều passenger trên cùng Trip có link và quyền revoke độc lập.

#### PUT `/v1/tracking/trips/{tripId}/share-link`

Auth: Identity User Access Token với role `PASSENGER`; sau đó Tracking gọi Booking để yêu cầu
authorization scope chính xác `BOOKING_OWNER` cho Trip. `BOOKING_OWNER` là scope do Booking trả về,
không phải role trong Identity JWT.
`Idempotency-Key`: bắt buộc, UUID v4. Request không có body. Cùng owner gọi lại khi grant còn active
nhận cùng link và `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "shareUrl": "https://app.vietride.vn/trip-sharing#token=v1.grant.signature",
    "expiresAt": "2026-08-04T16:30:00.000+07:00"
  },
  "meta": { "traceId": "req-123", "timestamp": "2026-08-03T16:30:00.000+07:00" }
}
```

Backend kiểm tra Trip lần hai sau khi tạo grant. Nếu Trip vừa terminal, grant bị revoke với
`CREATION_ROLLBACK` và link không được trả. Token nằm trong URL fragment để browser không gửi nó
trong request page/referrer.

#### DELETE `/v1/tracking/trips/{tripId}/share-link`

Auth: Identity User Access Token với role `PASSENGER`. `Idempotency-Key` giống PUT, nhưng DELETE
không gọi lại Booking và không phụ thuộc trạng thái hiện tại của Trip; endpoint chỉ tác động grant
active do chính user đó tạo và luôn idempotent. Nếu `{tripId}` là chuyến cũ đã được thay xe,
Tracking resolve alias đến replacement Trip hiện tại nên owner vẫn revoke được bằng URL cũ:

```json
{
  "success": true,
  "statusCode": 200,
  "data": { "revoked": true },
  "meta": { "traceId": "req-124", "timestamp": "2026-08-03T16:31:00.000+07:00" }
}
```

#### GET `/v1/tracking/shared-trip/context`

Auth: public capability token trong `X-Trip-Share-Token`. Đây là public subpath duy nhất dưới
Gateway prefix `/v1/tracking/shared-trip`; chỉ exact method/path GET được anonymous. Query string
được phép, nhưng POST, trailing slash, `/extra` và encoded suffix vẫn cần Identity JWT.

Response `200` dùng ADR 0004 envelope; `data` có shape cố định:

```json
{
  "status": "IN_PROGRESS",
  "expiresAt": "2026-08-04T16:30:00.000+07:00",
  "lastUpdatedAt": "2026-08-03T16:35:12.000+07:00",
  "vehicle": {
    "location": {
      "latitude": 10.7812,
      "longitude": 106.6981,
      "heading": 42,
      "speedKph": 38,
      "recordedAt": "2026-08-03T16:35:12.000+07:00"
    }
  },
  "route": {
    "originName": "Bến xe Miền Đông",
    "destinationName": "Bến xe Đà Lạt",
    "origin": { "latitude": 10.8142, "longitude": 106.7108 },
    "destination": { "latitude": 11.9404, "longitude": 108.4583 },
    "stops": [
      {
        "name": "Trạm dừng Bảo Lộc",
        "latitude": 11.5475,
        "longitude": 107.8078,
        "sequence": 1
      }
    ],
    "geometry": {
      "type": "LineString",
      "coordinates": [[106.6981, 10.7812], [106.7124, 10.7935]]
    }
  },
  "eta": {
    "estimatedArrivalAt": "2026-08-03T22:10:00.000+07:00",
    "remainingSeconds": 20100,
    "delayMinutes": null,
    "updatedAt": "2026-08-03T16:35:00.000+07:00"
  }
}
```

`route.origin` và `route.destination` chỉ chứa `{ latitude, longitude }`, lấy lần lượt từ
`originStation` và `destinationStation`; mỗi trường trả `null` khi station không có tọa độ hợp lệ.
Hai object này không chứa station ID. `STOPS_ONLY` vẫn trả terminal coordinates hợp lệ nhưng giữ
`route.geometry: null`; Tracking không dựng đường thẳng giả giữa hai terminal.

`lastUpdatedAt`, `vehicle.location`, `route.geometry` và `eta` có thể là `null`; `heading`,
`speedKph`, `eta.delayMinutes` cũng nullable. `route.stops` luôn là mảng, lấy từ ordered
`TripStop` snapshot, lọc tọa độ không hợp lệ và giới hạn tối đa 100 phần tử; chuyến không có điểm
dừng trả `stops: []`. Mỗi phần tử chỉ gồm `name`, `latitude`, `longitude`, `sequence`, không chứa
ID nội bộ. Không dựng GPS, geometry, điểm dừng hoặc ETA giả. Response luôn có:

`status` chỉ có `IN_PROGRESS` hoặc `VEHICLE_REPLACEMENT_PENDING`. Khi Trip bị gián đoạn để thay xe,
link/token, grant ID, owner, `createdAt` và expiry ban đầu được giữ nguyên. Trong lúc replacement còn
`BOARDING` hoặc chưa có GPS mới, context trả `VEHICLE_REPLACEMENT_PENDING`, giữ vị trí cuối của xe
cũ nếu Redis còn dữ liệu và bắt buộc `eta: null`; FE hiển thị đây là "vị trí trước khi đổi xe".
Khi replacement đã `IN_PROGRESS` và có GPS mới, cùng token tự trả `IN_PROGRESS` với dữ liệu chuyến
mới. Chuỗi nhiều lần đổi xe được resolve qua alias có cycle/depth guard; không lần đổi xe nào gia
hạn expiry.

```http
Cache-Control: no-store
Pragma: no-cache
Referrer-Policy: no-referrer
```

Public DTO không được chứa `tripId`, grant/share ID, token/hash, station/stop/booking/ticket/user/
operator ID, seat, email, phone, passenger/driver/assistant data hoặc GPS history.

#### Public Socket.IO `/shared`

Guest kết nối trực tiếp Tracking/Nginx, không qua Gateway HTTP proxy:

```ts
io("wss://api.vietride.app/shared", {
  path: "/tracking/socket.io",
  auth: { shareToken: "v1.<grantId>.<signature>" }
})
```

Server tự join `shared-trip:{tripId}` và `shared-grant:{grantId}`; client không có event tự chọn room.
Identity JWT không dùng được ở namespace `/shared`, và share token không dùng được ở namespace mặc
định. Events public:

- `shared:gps:update`
- `shared:eta:update`
- `shared:trip:statusChanged`
- `shared:trip:vehicleSubstituted` với payload chính xác
  `{ "status": "VEHICLE_REPLACEMENT_PENDING", "occurredAt": "date-time" }`
- `shared:access:revoked` với reason `EXPIRED`, `REVOKED`, `TRIP_ENDED` hoặc `ACCESS_UNAVAILABLE`

Owner revoke chỉ emit/disconnect grant room của họ. Trip terminal emit/disconnect toàn trip room.
Vehicle substitution emit event pending rồi chuyển socket từ room cũ sang replacement room, không
disconnect. Public event không chứa old/new Trip ID, Vehicle ID/biển số hoặc PII. Viewer không còn
nhận GPS room cũ và tự nhận GPS replacement khi chuyến mới bắt đầu chạy.
Socket đặt expiry timer chính xác và mặc định revalidate grant/Trip mỗi 60 giây. Phase 13 giả định một
Tracking replica; Socket.IO Redis adapter cho scale-out không thuộc contract này.

#### Vòng đời, RabbitMQ và bảo mật

- Grant hard-expire sau TTL mặc định 24 giờ, khi owner revoke, khi Trip
  `COMPLETED`/`CANCELLED`, hoặc khi `DISRUPTED` có `hasSubstitution=false`. `DISRUPTED` có
  `hasSubstitution=true` giữ grant để chờ event substitution.
- Tracking subscribe `tracking-trip-share-completed` → `trip.trip.completed`,
  `tracking-trip-share-cancelled` → `trip.trip.cancelled`, và
  `tracking-trip-share-disrupted` → `trip.trip.disrupted`, cùng queue mới
  `tracking-trip-share-vehicle-substituted` → `trip.trip.vehicle_substituted`; tất cả dùng
  `prefetch=1`, dead-letter, 5 retry, delay 10 giây.
- Consumer substitution chuyển mọi grant active `oldTripId → newTripId` trong transaction
  serializable mà không đổi grant ID/token hash/owner/expiry/created time. Nếu cùng owner đã tạo
  grant mới cho replacement trong lúc event trễ, grant mới bị revoke `CREATION_ROLLBACK` và grant
  cũ thắng. Sau DB commit, consumer ghi Redis alias hai chiều với TTL bằng share token, xóa pending
  marker, emit/chuyển socket rồi mới mark processed; retry phải tiếp tục các bước sau DB dù update
  lại bằng 0.
- Token là `v1.<grant UUID>.<base64url HMAC-SHA256>` ký canonical `v1.<grantId>`. PostgreSQL chỉ lưu
  SHA-256 full token; Redis idempotency chỉ lưu fingerprint, grant ID và outcome metadata. Không log
  raw token hoặc `X-Trip-Share-Token`.
- Context rate limit mặc định 60 request/token-hash/phút; socket handshake 20/token-hash/phút. Redis
  lỗi thì fail closed `503`.

Error contract:

| Trường hợp | HTTP | Error code |
|---|---:|---|
| Token malformed/tampered/not found | 401 | `TRACKING_SHARE_TOKEN_INVALID` |
| Token expired/revoked/Trip terminal | 410 | `TRACKING_SHARE_LINK_UNAVAILABLE` |
| PUT không có Booking scope `BOOKING_OWNER` | 403 | `ACCESS_DENIED` |
| Trip không tồn tại | 404 | `TRIP_NOT_FOUND` |
| Trip không `IN_PROGRESS` | 409 | `TRACKING_TRIP_NOT_ACTIVE` |
| Thiếu idempotency key | 422 | `IDEMPOTENCY_KEY_REQUIRED` |
| Key không phải UUID v4 | 422 | `VALIDATION_ERROR` |
| Key dùng lại cho fingerprint khác | 422 | `IDEMPOTENCY_KEY_MISMATCH` |
| Request cùng key đang chạy | 409 | `IDEMPOTENCY_REQUEST_PENDING` |
| Vượt rate limit | 429 | `RATE_LIMITED` |
| Redis rate limiter không khả dụng | 503 | `TRACKING_SHARE_RATE_LIMIT_UNAVAILABLE` |
| Booking authorization lỗi | 503 | `TRACKING_AUTH_UNAVAILABLE` |
| Trip/route dependency lỗi | 503 | `TRACKING_TRIP_UNAVAILABLE` |

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
        "createdAt": "2026-06-01T17:00:00+07:00",
        "updatedAt": "2026-06-01T17:00:00+07:00",
        "approvedAt": "2026-06-01T17:00:00+07:00"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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

Response: Server-Sent Events stream with assistant tokens. Sự kiện `done` trả
`conversationId`, `userMessageId`, `assistantMessageId` và `citations` thân thiện dạng
`[{ "title": "...", "section": "..." | null }]`. Không trả chunk ID, document ID hoặc UUID
nội bộ cho client. Các chunk ID vẫn chỉ được lưu nội bộ để audit và feedback.

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
    "entitlementActive": true,
    "billingPeriod": "MONTHLY",
    "startedAt": "2026-07-14T17:00:00+07:00",
    "expiresAt": "2026-08-14T17:00:00+07:00",
    "plan": { "planId": "uuid", "name": "Pro", "price": 500000, "limits": {}, "modules": {} },
    "usage": {},
    "pendingUpgrade": {
      "upgradeAttemptId": "uuid",
      "targetPlan": { "planId": "uuid", "name": "Enterprise", "limits": {}, "modules": {} },
      "amount": 900000,
      "billingPeriod": "MONTHLY",
      "dueAt": "2026-07-14T17:15:00+07:00",
      "remainingSeconds": 720,
      "latestPayment": { "paymentId": "uuid", "status": "FAILED", "canRetry": true }
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-14T17:00:00+07:00" }
}
```

`PENDING_PAYMENT` and `EXPIRED` are valid readable states. While status is `PENDING_PAYMENT`,
`plan`/`activePlan` remains the sole entitlement source: quota allocation/increment and module
gates such as `enableParcel`, `enableShuttle`, and `enableRag` must not use the pending target plan.
Errors: `403 FORBIDDEN`, `404 RESOURCE_NOT_FOUND`.

### GET `/v1/operator/subscription-plans`

Auth: `OPERATOR_ADMIN`. Returns active plans only. Response uses the ADR 0004 envelope with `items`; each item has `planId`, `name`, `description`, `pricePerMonth`, `pricePerYear`, `limits`, and `modules`.
The list contains active Standard plans plus active Custom plans whose `ownerOperatorId` matches the authenticated operator. Foreign private plans are never returned.

### POST `/v1/operator/subscription/upgrade/quote`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key`: required UUID v4. Request uses the existing upgrade body `{ planId, billingPeriod, paymentMethod }`. Response `201` returns `{ upgradeAttemptId, sourcePlanId, targetPlanId, billingPeriod, paymentMethod, prorationApplied, currentCyclePrice, targetCyclePrice, unusedCredit, proratedTargetAmount, amountDue, periodFrom, periodTo, quotedAt, dueAt, currency: "VND", status: "INITIATED" }`.

For an active paid subscription, billing period must stay unchanged and the target cycle price must be higher. Identity keeps the current cycle boundaries and charges only the double-rounded remaining delta. Trial and effectively expired subscriptions pay full price and open a new cycle. `expiresAt <= quotedAt` is expired. Target quota must not be below current usage. Private Custom plans are visible only to their owner; foreign IDs return `404 RESOURCE_NOT_FOUND`.

Payment independently validates the trusted snapshot period: `MONTHLY` requires `periodFrom < periodTo <= periodFrom.AddMonths(1)` and `YEARLY` requires `periodFrom < periodTo <= periodFrom.AddYears(1)`. A period outside that boundary returns `422 VALIDATION_ERROR` before Payment, Invoice, wallet, ledger, or Outbox persistence.

Errors: `404 RESOURCE_NOT_FOUND`; `409 SUBSCRIPTION_UPGRADE_ALREADY_ACTIVE`; `409 SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE`; `422 SUBSCRIPTION_UPGRADE_AMOUNT_NOT_PAYABLE`; `422 SUBSCRIPTION_UPGRADE_BILLING_PERIOD_MISMATCH`; `422 SUBSCRIPTION_UPGRADE_TARGET_LIMIT_BELOW_USAGE`; `422 IDEMPOTENCY_KEY_MISMATCH`.

### POST `/v1/operator/subscription/upgrade/{upgradeAttemptId}/payment`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key`: required UUID v4 and must be new for each confirm attempt. No request body. Identity locks attempt, subscription, and target plan; then revalidates deadline, source snapshot, current usage, target ownership, and target `isActive` before calling Payment.

- WALLET success: `200` using `SubscriptionUpgradeResponseDto`.
- VNPAY pending redirect: `202` using `SubscriptionUpgradeResponseDto`.
- `402 WALLET_INSUFFICIENT_BALANCE`: no Payment or wallet mutation; attempt remains `INITIATED`, `paymentId=null`, `latestPaymentStatus=NONE`. The same key replays cached 402 for 24 hours; retry after top-up uses a new key before `dueAt`.
- Target deactivated before confirm: `409 SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE`.
- Source or usage changed after quote: `409 SUBSCRIPTION_UPGRADE_QUOTE_STALE`.
- Expired quote: `409 SUBSCRIPTION_UPGRADE_EXPIRED`.

A VNPAY session accepted before plan deactivation may still complete. During `PENDING_PAYMENT`, current-plan entitlement remains authoritative, while usage increments are additionally capped by the quoted target limits.

### Custom subscription requests

Operator endpoints, auth `OPERATOR_ADMIN`:

- `POST /v1/operator/subscription/custom-requests` — idempotent create; one `PENDING_REVIEW` request per operator.
- `GET /v1/operator/subscription/custom-requests` — own requests only.
- `GET /v1/operator/subscription/custom-requests/{requestId}` — foreign valid UUID returns 404.

Request contains all six requested limits, module flags, `preferredBillingPeriod`, and optional `note`. Response contains status, review metadata, rejection reason, and approved private plan ID. Duplicate pending request returns `409 CUSTOM_REQUEST_ALREADY_PENDING`.

### POST `/v1/operator/subscription/upgrade`

Auth: `OPERATOR_ADMIN`. Idempotency-Key: required. `plan` hiện tại không đổi cho đến khi Payment `SUCCEEDED`; target plan chỉ xuất hiện trong `pendingUpgrade`.

Request:
```json
{
  "planId": "uuid",
  "billingPeriod": "MONTHLY",
  "paymentMethod": "VNPAY"
}
```

`billingPeriod` is `MONTHLY` or `YEARLY`. Identity snapshots the selected active plan's server-side price; the client never supplies an amount.
For VNPay, Identity always sends internal `returnMode=OPERATOR_WEB`; Payment resolves
`VNPAY_WEB_RETURN_URL`. The public client cannot provide or override a return URL.

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
    "dueAt": "2026-07-14T17:15:00+07:00",
    "activePlan": { "planId": "uuid", "name": "Starter", "limits": {}, "modules": {} },
    "pendingTargetPlan": { "planId": "uuid", "name": "Pro", "limits": {}, "modules": {} }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-14T17:00:00+07:00" }
}
```

Errors: `403 FORBIDDEN`; `404 RESOURCE_NOT_FOUND`; `409 SUBSCRIPTION_PAYMENT_PENDING`; `422 VALIDATION_ERROR`; `422 IDEMPOTENCY_KEY_MISMATCH`; `503 VNPAY_WEB_DISABLED` while the Web channel rollout flag is off.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T17:00:00+07:00" }
}
```

Additional errors: `402 WALLET_INSUFFICIENT_BALANCE`; `422 IDEMPOTENCY_KEY_REQUIRED`. Replaying the same key and request returns the original response. Reusing the key with a different payload returns `422 IDEMPOTENCY_KEY_MISMATCH`.

### POST `/v1/operator/subscription/upgrade/{upgradeAttemptId}/retry-payment`

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` bắt buộc. Chỉ cho phép khi attempt còn trong cửa sổ 15 phút và latest payment là `FAILED` hoặc `EXPIRED`. Mỗi retry tạo `paymentId` và `vnp_TxnRef` mới nhưng không kéo dài `dueAt`.

Response `202` dùng cùng `SubscriptionUpgradeResponseDto` với `paymentRedirectUrl` mới. Errors: `403 SUBSCRIPTION_UPGRADE_FORBIDDEN`; `404 RESOURCE_NOT_FOUND`; `409 SUBSCRIPTION_UPGRADE_EXPIRED`; `409 SUBSCRIPTION_PAYMENT_NOT_RETRYABLE`; `422 IDEMPOTENCY_KEY_REQUIRED`; `503 VNPAY_WEB_DISABLED` while the Web channel rollout flag is off.

VNPay gọi canonical `GET|POST /v1/payments/vnpay-ipn`. `returnUrl` chỉ đưa browser về FE và không được phép mutate Payment hoặc Subscription.

## Invoice, OperatorWallet and Settlement — Day 38

All list endpoints return the ADR 0004 paged envelope with `items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, and `hasPreviousPage`. `pageSize` is `1..100`; `sortDir` is `asc|desc`; unsupported `sortBy` returns `400 INVALID_SORT_FIELD`. Operator scope always comes from trusted JWT claims and is never accepted from query/body.

Ba danh sách tài chính của operator (`wallet/transactions`, `trip-settlements`, `ledger`) hỗ trợ
`search?` sau khi trim, dài `2..100` ký tự, và `dateField?` mặc định `createdAt`. Search luôn
được áp dụng sau tenant scope. UUID hợp lệ match chính xác transaction/reference/trip/settlement;
chuỗi thường match chính xác hoặc prefix `referenceCode` không phân biệt hoa thường. Transaction
và ledger còn cho phép contains trên `note`; ký tự `%`, `_`, `\` được escape và được hiểu như ký
tự thường. `dateField` hoặc enum filter không hợp lệ trả `400 INVALID_FILTER`.

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
  "periodFrom": "2026-07-15T07:00:00+07:00",
  "periodTo": "2026-08-15T07:00:00+07:00",
  "pdfGenerationStatus": "COMPLETED",
  "createdAt": "2026-07-15T17:00:00+07:00",
  "issuedAt": "2026-07-15T17:01:00+07:00"
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
    "expiresAt": "2026-07-15T18:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T17:00:00+07:00" }
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
    "currency": "VND",
    "awaitingTripCompletionAmount": 1700000,
    "awaitingTripCompletionCount": 6,
    "pendingHoldAmount": 300000,
    "pendingHoldCount": 4,
    "eligibleAmount": 450000,
    "eligibleCount": 2,
    "nextEligibleAt": "2026-07-17T10:00:00Z",
    "nextScheduledSettlementAttemptAt": "2026-07-20T02:00:00Z",
    "lifetimeSettledAmount": 3500000,
    "lastSettlement": {
      "settlementId": "uuid",
      "amount": 450000,
      "method": "AUTO_WEEKLY",
      "settledAt": "2026-07-13T02:00:00Z"
    },
    "withdrawalSupported": false,
    "updatedAt": "2026-07-15T10:00:00Z",
    "calculatedAt": "2026-07-15T10:00:03Z"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-15T17:00:00+07:00" }
}
```

`balance` là tiền đã được credit vào OperatorWallet sau settlement; tiền khách vừa mua vẫn nằm
trong PlatformWallet. `awaitingTripCompletionAmount` là net entitlement của trip đã có canonical
ledger nhưng chưa có settlement marker. `pendingHoldAmount` và `eligibleAmount` đều lấy từ cùng
projection ledger hiện tại, không trộn với snapshot `OperatorTripSettlement.netAmount`. `updatedAt`
chỉ đổi khi balance đổi; `calculatedAt` là thời điểm tính các aggregate. `lifetimeSettledAmount`
chỉ cộng `netAmount` của settlement `SETTLED`, không bao gồm adjustment ví hoặc subscription.

### GET `/v1/operator/wallet/transactions`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `type?`, `referenceType?`, `from?`, `to?`, `dateField?` (chỉ `createdAt`), `search?`, `sortBy?` (`createdAt|amount`), `sortDir?`. Items giữ các field cũ và thêm nullable Release-A `transactionCode`, `signedAmount` (CREDIT dương, DEBIT âm), `currency: "VND"`, nullable `relatedSettlement { settlementId, settlementCode, tripId, tripCode, method }`, `actorType`, nullable `actor`, nullable `adjustmentReason`, `dataCompleteness`, `missingFields`. Text search match prefix `transactionCode` ngoài các điều kiện cũ. `amount` cũ luôn dương. Adjustment liên kết actor qua ledger `referenceId=transactionId`; subscription luôn có `relatedSettlement=null`.

```json
{
  "id": "uuid",
  "transactionCode": "OWT-20260823-7K3M2QPX",
  "type": "CREDIT",
  "amount": 1250000,
  "relatedSettlement": {
    "settlementId": "uuid",
    "settlementCode": "STL-20260823-P9R4TX2W",
    "tripId": "uuid",
    "tripCode": "TRIP-20260816-M5Q7WV3D",
    "method": "AUTO_WEEKLY"
  }
}
```

### GET `/v1/operator/trip-settlements`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `status?`, `tripId?`, `from?`, `to?`, `dateField?` (`createdAt|tripTerminalAt|eligibleAt|settledAt`), `search?`, `sortBy?` (`createdAt|eligibleAt|settledAt|netAmount`), `sortDir?`. Items giữ các field cũ và thêm nullable Release-A `settlementCode`, `tripTerminalAt`, `walletTransactionId`, `financialBreakdown { grossSalesAmount, passengerPaidAmount, vietRideFundedAmount, operatorFundedDiscountAmount, refundAmount, recognizedAdjustmentAmount, netEntitlementAmount }`, `processingState`, `nextScheduledSettlementAttemptAt`, nullable `delayReason`, `attemptCount`, nullable `lastAttemptAt`, nullable `nextRetryAt`, nullable `cancelReason`, nullable `trip { departureAt, routeId, routeName, originName, destinationName, tripCode }`, và `dataCompleteness`. Text search match prefix `settlementCode`/`tripCode` trước count/paging. `settledBy=null` xác định auto weekly; manual settlement trả snapshot admin khi có.

`processingState` là `ON_HOLD|READY_FOR_SETTLEMENT|RETRY_SCHEDULED|COMPLETED|CANCELLED`.
Operator chỉ nhận `delayReason=SYSTEM_PROCESSING_DELAY`; raw
`PLATFORM_WALLET_INSUFFICIENT_BALANCE` chỉ xuất hiện ở admin API. `netAmount` và breakdown dùng
canonical ledger hiện tại; settlement write vẫn khóa row và recompute lần cuối trước movement.
Trip được batch-enrich qua internal Trip API hiện có. Nếu Trip lỗi hoặc thiếu summary, endpoint tài
chính vẫn trả `200`, `trip=null`, `dataCompleteness=PARTIAL`. Settlement cũng trả `PARTIAL` nếu
canonical projection thiếu reconciliation metadata của ledger legacy.

### GET `/v1/operator/ledger`

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `tripId?`, `entryType?`, `referenceType?`, `from?`, `to?`, `dateField?` (`createdAt|occurredAt`), `search?`, `sortBy?` (`createdAt|amount`), `sortDir?`. Items giữ các field cũ và thêm nullable `referenceCode`, `occurredAt`, `occurredAtSource` (`BUSINESS_EVENT|LEDGER_CREATED_AT_FALLBACK`), nullable `operatorFundedVoucherAmount`, nullable `adjustmentReason`, `affectsRevenue`, `affectsSettlement`, nullable `settlement { settlementId, status, eligibleAt, settledAt, walletTransactionId }`, `dataCompleteness`, `missingFields`. Internal source-event identifiers không được trả ra. Automated/event rows dùng `actorType=SYSTEM`; admin adjustment dùng `USER` với actor snapshot khi có.

Không được parse `note` để lấy tiền. `VOUCHER_OPERATOR_FUNDED_AUDIT.amount` luôn `0`; số voucher
nhà xe tài trợ nằm riêng trong `operatorFundedVoucherAmount` và không bị trừ net lần hai. Row cũ
thiếu `referenceCode`, `occurredAt` hoặc voucher metadata trả nullable và `PARTIAL`; `occurredAt`
fallback về `createdAt` với source tương ứng nhưng tiền canonical vẫn được tính bình thường.

Payment context `version=1` mở rộng additive mỗi allocation với nullable `referenceCode` tối đa 64
ký tự đã trim. Booking gửi `BookingCode`, Parcel gửi `ParcelCode`; internal payment-context response
hiện có trả cùng field. JSON cũ không có field vẫn deserialize hợp lệ.

### GET `/v1/admin/trip-settlements`

Auth: `SYSTEM_ADMIN`. Query: operator filters plus `operatorId?`, `stuckOnly?`, `severity?`, `search?`. UUID search matches settlement/trip ID. Text search matches persisted operator name or active failure code by case-insensitive contains, and persisted `settlementCode`, Trip snapshot `tripCode`, hoặc ledger `referenceCode` by case-insensitive prefix. Search is applied before count/paging and performs no live cross-service lookup. Items add nullable Release-A `settlementCode` and `tripCode`. A stuck row is unresolved `ELIGIBLE` with `activeFailureCode != null`; `HIGH` means failure count `>=3` **or** stuck age `>21 days`.

### POST `/v1/admin/trip-settlements/{settlementId}/settle`

Auth: `SYSTEM_ADMIN`. `Idempotency-Key`: required. Body is empty. Only `PENDING_HOLD|ELIGIBLE` can settle. Response `200` data contains `settlementId`, nullable Release-A `settlementCode`, `tripId`, nullable Release-A `tripCode`, `operatorId`, `netAmount`, `status`, `settlementMethod: "ADMIN_MANUAL"`, `settledAt`.

The settlement marker remains the single per-Trip/per-operator row. If recomputed
`netAmount <= 0`, this request returns that row with `status: "CANCELLED"` and
`settlementMethod: "ADMIN_MANUAL"`; it creates no PlatformWallet/OperatorWallet movement and
publishes no settlement event.

Errors: `404 TRIP_SETTLEMENT_NOT_FOUND`; `409 TRIP_SETTLEMENT_ALREADY_SETTLED`; `500 PLATFORM_WALLET_INSUFFICIENT_BALANCE`; idempotency errors. Same-key replay returns the original result; a different manual key losing a concurrent manual/weekly race returns `409 TRIP_SETTLEMENT_ALREADY_SETTLED`.

### GET `/v1/admin/reports/platform?from={from}&to={to}`

Xác thực: `SYSTEM_ADMIN`. Booking sở hữu facade và orchestration; Gateway chỉ proxy và không service
nào đọc database của service khác. Booking lấy các chỉ số vận hành từ Booking/Trip/Parcel, lấy doanh
thu cuối cùng từ Payment ledger, rồi gọi các endpoint raw và Identity bên dưới bằng Internal JWT.

`from` và `to` là hai ngày Asia/Ho_Chi_Minh bắt buộc theo `YYYY-MM-DD`, inclusive, `from <= to`, tối đa 366 ngày.
Booking diễn giải input theo lịch Asia/Ho_Chi_Minh rồi chuẩn hóa ngay thành hai timestamp UTC
`[fromUtc, toUtcExclusive)` để query DB/gọi nguồn nội bộ. Công thức là
`fromUtc = UTC(from 00:00 Asia/Ho_Chi_Minh)` và
`toUtcExclusive = UTC((to + 1 ngày) 00:00 Asia/Ho_Chi_Minh)`; ví dụ
`2026-07-01..2026-07-31` thành `[2026-06-30T17:00:00Z, 2026-07-31T17:00:00Z)`.

Response `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "period": {
      "from": "2026-07-01",
      "to": "2026-07-31",
      "timezone": "Asia/Ho_Chi_Minh"
    },
    "totals": {
      "completedBookingCount": 120,
      "completedTripCount": 36,
      "deliveredParcelCount": 18,
      "netTicketRevenueVnd": 48000000,
      "netParcelRevenueVnd": 3200000,
      "netTransportRevenueVnd": 51200000
    },
    "byOperator": [{
      "operatorId": "uuid",
      "operatorName": "Nha xe A",
      "completedBookingCount": 120,
      "completedTripCount": 36,
      "deliveredParcelCount": 18,
      "netTicketRevenueVnd": 48000000,
      "netParcelRevenueVnd": 3200000,
      "netTransportRevenueVnd": 51200000
    }],
    "generatedAt": "2026-08-01T07:00:01+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-08-01T07:00:01+07:00" }
}
```

`byOperator` là hợp của operator ID từ Booking/Trip/Parcel và Payment ledger, sort theo
`netTransportRevenueVnd DESC` rồi operator ID. Counts vận hành do Booking/Trip/Parcel sở hữu;
Payment ledger sở hữu `netTicketRevenueVnd` và `netParcelRevenueVnd` cuối cùng. Một booking đã thanh
toán ở trạng thái `NO_SHOW` vẫn có thể cộng doanh thu ledger nhưng không cộng
`completedBookingCount`. Summary Identity bị thiếu vẫn giữ `operatorName=null`; totals phải bằng
checked sum của mọi breakdown row. Ticket, Parcel và transport revenue là signed và có thể âm.

Lỗi: `403 FORBIDDEN`, `422 VALIDATION_ERROR`, `500 REPORT_VALUE_OVERFLOW`, `503
UPSTREAM_UNAVAILABLE`. Overflow upstream được propagate cùng HTTP 500; timeout, source unavailable,
payload không dùng được, ledger malformed/duplicate và source-local live/projection mismatch đều trả
503. Ledger-only revenue không phải là mismatch. Không trả partial hoặc stale response. Cache
`platform-report:v3` có TTL tối đa 60 giây; lỗi nguồn không được dùng cache quá hạn.

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

Chỉ Booking rows có `status=COMPLETED` và `completedAt` trong UTC `[from,to)` đóng góp vào
`completedBookingCount`; revenue trong raw Booking payload không thay thế Payment ledger.

### GET `/internal/v1/reports/platform/trips?from={from}&to={to}`

Auth: Internal JWT only. Raw success payload:

```json
{
  "items": [{ "operatorId": "uuid", "completedTripCount": 36 }]
}
```

Chỉ Trip rows có `status=COMPLETED` và `completedAt` trong UTC `[from,to)` đóng góp vào
`completedTripCount`.

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

Chỉ Parcel rows có `status=DELIVERY_CONFIRMED` và `confirmedAt` trong UTC `[from,to)` đóng góp vào
`deliveredParcelCount`; revenue cuối cùng vẫn lấy từ Payment ledger. Parcel collected amount là
signed `depositPaidVnd + balancePaidVnd - refundedAmountVnd` và không bao giờ clamp.
`forfeitedDepositVnd` được báo cáo riêng và không cộng lần hai.

All three source endpoints validate RFC 3339 UTC half-open ranges. PostgreSQL `SUM(BIGINT)` is
read as NUMERIC and checked per group and total before mapping to Int64. Overflow returns an ADR
0004 error envelope with `500 REPORT_VALUE_OVERFLOW`; internal successes remain raw.

### GET `/v1/admin/platform-wallet`

Auth: `SYSTEM_ADMIN`. Returns `{ platformWalletId, balance, updatedAt }`.

### GET `/v1/admin/platform-wallet/transactions`

Auth: `SYSTEM_ADMIN`. Paged query supports `type?`, `referenceType?`, `from?`, `to?`, `search?`, `sortBy=createdAt|amount`, `sortDir?`. UUID search matches transaction/reference ID. Text search matches nullable Release-A `transactionCode` by prefix, note or persisted actor display name by case-insensitive contains, and an exact `referenceType` enum name. Search is applied before count/paging. Items contain transaction identity, nullable `transactionCode`, direction, positive amount, balance snapshots, reference, note and created time.

Ví dụ một item: `{ "id": "uuid", "transactionCode": "PWT-20260823-4F8N2KQJ", "type": "DEBIT", "amount": 1250000, "referenceType": "TRIP_SETTLEMENT", "referenceId": "uuid" }`.

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

For a Custom plan, price/quota/module/owner/source terms are immutable. PATCH may only change `isActive` from true to false while every other field is unchanged. A deactivated Custom plan cannot be reactivated; existing subscriptions keep entitlement, but new quote/confirm is blocked.

### Admin Custom Request review

Auth: `SYSTEM_ADMIN`:

- `GET /v1/admin/subscription-plans/custom-requests?status=`.
- `GET /v1/admin/subscription-plans/custom-requests/{requestId}`.
- `POST /v1/admin/subscription-plans/custom-requests/{requestId}/approve` — `Idempotency-Key` required.
- `POST /v1/admin/subscription-plans/custom-requests/{requestId}/reject` — `Idempotency-Key` required and body `{ "reason": "..." }`.

Both admin GET responses preserve the Custom Request fields and additionally return non-null
`operatorName` beside `operatorId`. The name is resolved by Identity from the owning Operator,
including a soft-deleted Operator, so Admin FE must not issue one Operator-detail request per row.

Approve accepts final independent `pricePerMonth`/`pricePerYear`, six granted limits, and module flags. It atomically creates one owner-scoped immutable Custom plan and marks the request approved. Every granted limit must be at least the operator's locked current usage; otherwise `422 CUSTOM_PLAN_LIMIT_BELOW_CURRENT_USAGE` returns field errors whose message includes requested, granted, and current-usage values. Terminal requests return `409 CUSTOM_REQUEST_ALREADY_REVIEWED`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
        "createdAt": "2026-06-01T17:00:00+07:00",
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

### GET `/v1/admin/operators/{operatorId}`

Auth: `SYSTEM_ADMIN`.

Response `200`: ADR 0004 envelope whose `data` contains `operatorId`, `name`,
`businessRegistrationNumber`, `taxCode`, `contactEmail`, `contactPhone`, `logoUrl`, nested
`address { street, ward, province }`, `representativeName`, `representativePhone`,
`registrationStatus`, `isActive`, `createdAt`, `updatedAt`, `approvedAt`, `rejectedAt`,
`rejectReason`, `suspendedAt`, `suspendReason`, `cancellationPolicy`, `parcelNoShowPolicy`, and
`luggagePolicy`. Bank-account and subscription fields are intentionally excluded.

Errors: `404 RESOURCE_NOT_FOUND` when the operator does not exist; standard `401`/`403` envelopes
for missing authentication or a non-System-Admin caller.

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
      "startedAt": "2026-06-01T17:00:00+07:00",
      "expiresAt": "2026-07-01T17:00:00+07:00",
      "currentOperatorUsers": 1
    }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors:
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — missing reason or invalid lifecycle transition.

Notes: atomically sets `Operator.registrationStatus=REJECTED`, stores reject metadata, and sets the PENDING_APPROVAL subscription to `CANCELLED`. `operator_subscriptions` is not soft-deletable in the canonical DDL, so no `deletedAt` is set.

### POST `/v1/admin/operators/{operatorId}/suspend`

Auth: `SYSTEM_ADMIN`. Idempotency-Key: required, UUID v4.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors:
- `404 RESOURCE_NOT_FOUND` — operator does not exist.
- `422 VALIDATION_ERROR` — missing reason or invalid lifecycle transition.

Notes: suspend writes an ActivityLog with action `SUSPEND_OPERATOR`, actor user ID, operator ID and
source. It revokes every active refresh token for `OPERATOR_ADMIN`, `OPERATOR_STAFF`, `DRIVER`, and
`ASSISTANT` users in the tenant with reason `ADMIN_REVOKE`, and requests Firebase session
revocation for all of them. Existing access tokens expire naturally within 15 minutes; no live
Redis blacklist is used. Suspend does not change any `User.status` to `LOCKED`.

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
        "createdAt": "2026-06-01T17:00:00+07:00",
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
    "initialPasswordExpiresAt": "2026-06-03T17:00:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors:
- `403 FORBIDDEN` — caller is not `OPERATOR_ADMIN`, has no `operatorId`, or caller Operator is not currently `APPROVED`.
- `409 AUTH_EMAIL_ALREADY_REGISTERED` — target email already exists.
- `409 AUTH_PHONE_ALREADY_REGISTERED` — target phone already exists.
- `422 SUBSCRIPTION_LIMIT_EXCEEDED` — creating the target role would exceed the current subscription limit.
- `422 VALIDATION_ERROR` — invalid payload or role outside the allowed set.

### POST `/v1/operator/users/{userId}/lock`

Auth: `OPERATOR_ADMIN` of an `APPROVED` Operator. `Idempotency-Key` is required and the request has
no body. The target lookup is tenant-masked and accepts only a `DRIVER` or `ASSISTANT` whose
`operatorId` exactly equals the caller's claim. An `ACTIVE` target becomes `LOCKED` with
`lockSource=OPERATOR_ADMIN`; active refresh tokens are revoked with `ADMIN_REVOKE`, Firebase session
revocation is requested with `USER_LOCKED`, and `LOCK_USER` is audited. Repeating the action against
an already locked target is ensure-locked success and never downgrades a `SYSTEM_ADMIN` lock.

Response `200`:
```json
{
  "success": true,
  "statusCode": 200,
  "data": { "userId": "uuid", "status": "LOCKED", "statusChanged": true },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

### POST `/v1/operator/users/{userId}/unlock`

Auth and tenant/role masking are identical to operator lock. `Idempotency-Key` is required and the
request has no body. The target must be `LOCKED` with `lockSource=OPERATOR_ADMIN` or
`AUTOMATIC_LOGIN_FAILURE`; unlock restores `lockedFromStatus`, clears `lockSource`, resets DB/Redis
lockout state and does not restore revoked tokens. `SYSTEM_ADMIN` and `LEGACY_UNKNOWN` locks return
`403 FORBIDDEN` and remain unchanged.

Response `200` uses the same shape with restored `status` and `statusChanged=true`.

Both endpoints return:
- `403 FORBIDDEN` — caller is not an operator admin, Operator is not `APPROVED`, or unlock attempts to override a platform/legacy lock.
- `404 RESOURCE_NOT_FOUND` — target is missing, outside tenant, or not `DRIVER|ASSISTANT`.
- `422 USER_INVALID_STATUS_TRANSITION` — requested transition is invalid.
- Standard idempotency errors from BSOT §5.6.

### GET `/v1/operator/profile`

Auth: `OPERATOR_ADMIN` or `OPERATOR_STAFF` for an approved Operator. For a suspended Operator, only
`OPERATOR_ADMIN` may read this endpoint through the restricted Gateway session. Tenant isolation:
operator is resolved from caller `operatorId`.

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
      "province": "Ho Chi Minh City"
    },
    "representativeName": "Nguyen Van Operator",
    "representativePhone": "+84907654321",
    "registrationStatus": "APPROVED",
    "isActive": true,
    "suspendedAt": null,
    "suspendReason": null,
    "cancellationPolicy": [
      { "hoursBeforeDeparture": 24, "feePercent": 10 }
    ],
    "parcelNoShowPolicy": { "noShowFeePercent": 0, "additionalPaymentTimeoutMinutes": 30 },
    "luggagePolicy": { "defaultLuggageKgPerSeat": 10 }
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

Errors:
- `403 FORBIDDEN` — caller is not an operator role or has no `operatorId`.
- `404 RESOURCE_NOT_FOUND` — operator does not exist.

`suspendedAt` and `suspendReason` are non-null only while `registrationStatus=SUSPENDED`.
Suspended non-admin roles and restricted sessions targeting any non-whitelisted route receive
`403 OPERATOR_SUSPENDED` at the Gateway.

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

`operatorId`, `avatarUrl` và `phone` có thể null. Trip DriverSchedule create/activate/update-crew validation yêu cầu `operatorId` khớp operator caller, đúng role `DRIVER`/`ASSISTANT` và `status = ACTIVE`; user `LOCKED` bị từ chối bằng `422 VALIDATION_ERROR`. Shuttle dispatch còn yêu cầu driver active có `displayName` và `phone` để snapshot vào assignment event.

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

### GET `/internal/v1/users/by-email?email={normalizedEmail}`

Auth: Internal JWT via `X-Internal-Auth`. Caller: Parcel Service. Never exposed through Gateway.
The caller trims/lowercases and URI-escapes the email. Identity performs an exact normalized-email
lookup against non-soft-deleted users and returns raw `{ "userId": "uuid" }` without PII. No match
returns `404 RESOURCE_NOT_FOUND`; malformed input returns `422 VALIDATION_ERROR`.

Parcel maps only the exact 404 to `recipientUserId=null`. Identity 5xx, transport/timeout, or a
malformed success/error body becomes FE-facing `503 UPSTREAM_UNAVAILABLE`; Parcel creation remains
atomic and does not persist a partial row.

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
  "entitlementActive": true,
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

### GET `/internal/v1/trips/{tripId}/shuttle-road-distance`

Auth: valid Internal JWT only. Booking calls this endpoint before creating each shuttle intent.
Query `direction=INBOUND_TO_STATION|OUTBOUND_FROM_STATION`, `latitude`, and `longitude` are
required. Inbound measures to the route origin Station; outbound measures to the destination
Station. Trip uses Goong Directions `vehicle=car` and returns raw `{ "distanceMeters": 5000 }`.
Errors are `422 VALIDATION_ERROR`/`422 SHUTTLE_STATION_NOT_SUPPORTED` or
`503 SHUTTLE_DISTANCE_UNAVAILABLE`; there is no Haversine fallback.

### GET `/v1/operator/shuttle-requests`

Shuttle được nhóm theo `mainTripId + direction`, trong đó `direction` là `INBOUND_TO_STATION` hoặc `OUTBOUND_FROM_STATION`. Khoảng cách hiển thị là `roadDistanceMeters` snapshot từ Goong Directions; không dùng Haversine cho điều kiện đủ điều kiện. Giới hạn toàn nền tảng là 10.000 mét, bao gồm cả điểm đúng 10.000 mét.

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Tenant lấy từ JWT. Query phân trang theo main Trip.

Response là `ShuttleRequestPage`, giữ `items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, `hasPreviousPage` và thêm `summary { totalPendingPassengerCount, totalPendingGroupCount }`. Mỗi group trả `mainTripId`, `routeName`, Station theo direction, `direction`, `hardCutoffAt`, `pendingPassengerCount`, `assignedPassengerCount`, `totalShuttlePassengerCount`, `dispatchedShuttleTripCount`, các nhóm Booking (`bookingId`, nullable `bookingCode`, `passengerCount`, `pickupAddress`, `pickupLat`, `pickupLng`, `roadDistanceMeters`, `requestedAt`) và `suggestedBookingOrder`.

`from/to` lọc theo `Trip.departureDateTime` tại ICT. Thứ tự mặc định trước pagination là `hardCutoffAt ASC`, `departureDateTime ASC`, `mainTripId ASC`, `direction ASC`. Thứ tự gợi ý Booking vẫn dùng road-distance snapshot, xa nhất trước, hòa thì `requestedAt ASC`; không dùng Haversine để quyết định eligibility.

Pending shuttle `BookingGroup` responses always include non-null nested `passengers[]`. Each item contains
`passengerUserId`, nullable `displayName` and `phone`, and aggregated `ticketIds[]`. The result
keeps grouping by `mainTripId + direction`, cutoff/distance fields, and `suggestedBookingOrder`.
Identity profile transport failure returns `503 UPSTREAM_UNAVAILABLE`; a missing profile returns
null display fields.

### GET `/v1/operator/shuttle-trips`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Query `page`, `pageSize`, `from`, `to`, and optional
comma-separated `status=SCHEDULED,IN_PROGRESS,COMPLETED,CANCELLED`. `from/to` are Asia/Ho_Chi_Minh dates and
`to` includes the whole day. Without a status filter all statuses, including `CANCELLED`, are
returned. Default ordering is `scheduledDepartureTime DESC, shuttleTripId DESC`.

Response `200`: `PagedResult<OperatorShuttleTripListItemDto>` keeps all existing fields and adds
`mainTrip { tripId, routeName, departureDateTime, estimatedArrivalTime, hardCutoffAt }`,
`station { stationId, name }`, `vehicle.typeDisplayName`, `vehicle.usablePassengerCapacity`, and
`passengerProgress { pending, pickedUp, delivered, noShow, cancelled }`. `passengerCount` counts
non-cancelled passenger manifests; `stopCount` counts unique non-cancelled `pickupOrder` values.
Usable capacity is derived from the vehicle seat layout and excludes disabled seats and
`DRIVER_AREA`; it is not sourced from `totalSeats`. Invalid status returns `422 VALIDATION_ERROR`;
Identity profile transport failure returns `503 UPSTREAM_UNAVAILABLE`.

History items also expose nullable dispatch audit data: `notes`, `createdAt`, `createdBy`,
`cancelledAt`, `cancelReason`, and `cancelledBy`. `createdBy` and `cancelledBy` are actor user IDs.
Rows created before audit capture may return null actor IDs. Cancellation writes dedicated audit
fields and does not append or parse cancellation text in `notes`.

Assignment audit is exposed separately and is immutable. The additive `latestAssignment` field is
nullable; it is the most recent assignment action and must not be inferred from `createdBy`:

```json
"latestAssignment": {
  "action": "REASSIGNED",
  "assignedAt": "2026-08-27T15:30:00+07:00",
  "assignedBy": {
    "userId": "uuid",
    "displayName": "Trần Minh Bình",
    "role": "OPERATOR_ADMIN"
  },
  "reason": "Xe cũ gặp sự cố"
}
```

`latestAssignment` is `null` for ShuttleTrip rows created before assignment-audit capture. The
legacy `createdBy` field remains in the response for compatibility, but it only identifies the
actor who created the ShuttleTrip and is never the fallback for the current assigner.

### GET `/v1/operator/shuttle-trips/{shuttleTripId}/assignment-history`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Tenant is taken from the JWT. A missing ShuttleTrip or
a ShuttleTrip owned by another tenant is masked as `404 SHUTTLE_TRIP_NOT_FOUND`.

Query: `page` (default `1`) and `pageSize` (default `20`, maximum `100`). Response `200` is
`PagedResult<ShuttleAssignmentHistoryItemDto>`, sorted by `assignedAt DESC` (newest first). Each
item contains `id`, `action` (`INITIAL_ASSIGNED` or `REASSIGNED`), `assignedAt`, `assignedBy`
(`userId`, display name, role), nullable `reason`, `previousDriver`, `currentDriver`,
`previousVehicle`, and `currentVehicle`. Driver snapshots contain `id` and nullable
`displayName`; vehicle snapshots contain `id` and `licensePlate`.

The initial create writes exactly one `INITIAL_ASSIGNED` record. Reassignment writes a
`REASSIGNED` record only when the driver or vehicle actually changes; a same-assignment replay
does not create another audit record or `trip.shuttle.reassigned` event. Audit, assignment,
reservations and Outbox are committed atomically. Identity transport failures return
`503 UPSTREAM_UNAVAILABLE`; a missing/inactive actor or actor from another operator returns
`403 FORBIDDEN`. The existing create/reassign response shapes and `trip.shuttle.reassigned`
payload are unchanged.

### GET `/v1/operator/shuttle-trips/{shuttleTripId}/passengers`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`, scoped to the operator tenant from JWT. Response groups
manifest rows by `(pickupOrder, bookingId)` and returns `bookingCode`, `pickupAddress`,
`passengerCount`, and `passengers[] { passengerUserId, displayName, phone, ticketIds }`. Missing
Identity profiles preserve the passenger ID and return null `displayName`/`phone`; Identity
transport failure returns `503 UPSTREAM_UNAVAILABLE`. Response caching is disabled with
`Cache-Control: private, no-store`.

### POST `/v1/operator/shuttle-trips`

Request bắt buộc có thêm `direction`. Inbound dùng origin Station và `scheduledEndTime <= departureDateTime - 30 phút`; outbound dùng destination Station và `scheduledDepartureTime >= estimatedArrivalTime + 30 phút`.

Auth: `OPERATOR_ADMIN`. `Idempotency-Key` bắt buộc.

The authenticated actor is persisted as `createdBy`; clients do not submit this field.

```json
{
  "mainTripId": "uuid",
  "direction": "INBOUND_TO_STATION",
  "driverUserId": "uuid",
  "vehicleId": "uuid",
  "scheduledDepartureTime": "2026-07-13T01:00:00Z",
  "scheduledEndTime": "2026-07-13T02:00:00Z",
  "orderedBookingIds": ["uuid"],
  "notes": "optional"
}
```

Chọn một subset Booking không rỗng. Toàn bộ ticket của một Booking được gán nguyên tử, sức chứa tính theo tổng ticket. Direction và Station được suy ra từ main Trip. `scheduledEndTime` không được sau `departureDateTime - 30 phút`. Driver/vehicle phải active, cùng tenant và không overlap main Trip/ShuttleTrip. Response `201` trả ShuttleTrip cùng số passenger assigned/remaining. Replay cùng idempotency key trả cùng kết quả.

Before dispatch or any Trip/Booking mutation, Trip validates the operator is active/approved and
the active subscription with `requireShuttleModule=true`. Exact guard outcomes are `402
SUBSCRIPTION_EXPIRED`, `403 SUBSCRIPTION_MODULE_DISABLED` when `enableShuttle=false`, and `503
UPSTREAM_UNAVAILABLE` when Identity is unavailable or returns unusable subscription data.

Errors: `402 SUBSCRIPTION_EXPIRED`; `403 FORBIDDEN`; `403 SUBSCRIPTION_MODULE_DISABLED`;
`404 TRIP_NOT_FOUND`; `404 VEHICLE_NOT_FOUND`; `404 DRIVER_NOT_FOUND`; `409
SHUTTLE_REQUEST_SET_CHANGED`; `409 SHUTTLE_CAPACITY_EXCEEDED`; `409
SHUTTLE_DRIVER_CONFLICT`; `409 SHUTTLE_VEHICLE_CONFLICT`; `409
SHUTTLE_REQUEST_CUTOFF_PASSED`; `422 SHUTTLE_DISTANCE_EXCEEDED`; `422 VALIDATION_ERROR`;
`503 SHUTTLE_DISTANCE_UNAVAILABLE`; `503 RESOURCE_TRAVEL_TIME_UNAVAILABLE`; `503 UPSTREAM_UNAVAILABLE`.

Shuttle dispatch also uses the shared Driver/Vehicle interval + turnaround + reposition engine.
Conflicts retain `SHUTTLE_DRIVER_CONFLICT`/`SHUTTLE_VEHICLE_CONFLICT` and carry the canonical
`conflictReason` field. Goong/missing-coordinate failure returns
`503 RESOURCE_TRAVEL_TIME_UNAVAILABLE` and writes no ShuttleTrip or partial reservation.

### PATCH `/v1/operator/shuttle-trips/{shuttleTripId}/assignment`

Auth: `OPERATOR_ADMIN`. Header `Idempotency-Key` is required.

```json
{
  "driverUserId": "uuid",
  "vehicleId": "uuid",
  "reason": "Xe cũ cần bảo trì"
}
```

At least one of `driverUserId` or `vehicleId` and a non-empty `reason` are required. Only a
`SCHEDULED` Shuttle Trip may be reassigned. Driver and vehicle must be active and belong to the
same operator; usable vehicle capacity is derived from the seat layout. The shared availability
engine excludes the Shuttle Trip being edited, and assignment plus reservation replacement commit
atomically. Passenger manifests, pickup order, and schedule are unchanged.

Response `200` returns `shuttleTripId`, the effective `driverUserId`, and the effective `vehicleId`.
Errors include `404 SHUTTLE_TRIP_NOT_FOUND`, `404 DRIVER_NOT_FOUND`, `404 VEHICLE_NOT_FOUND`,
`409 SHUTTLE_TRIP_INVALID_STATE`, `409 SHUTTLE_DRIVER_CONFLICT`, `409 SHUTTLE_VEHICLE_CONFLICT`,
`409 SHUTTLE_CAPACITY_EXCEEDED`, `422 VALIDATION_ERROR`, and `503 RESOURCE_TRAVEL_TIME_UNAVAILABLE`.

### POST `/v1/operator/shuttle-trips/availability-check`

Auth: `OPERATOR_ADMIN`. Read-only and requires no `Idempotency-Key`. Body equals Shuttle create
fields except `notes`; `orderedBookingIds` determines the first/last manifest endpoint. The
response is the same availability shape and 100-conflict cap documented for DriverSchedule.

### POST `/v1/operator/shuttle-trips/route-preview`

Auth: `OPERATOR_ADMIN`. Đây là query tư vấn read-only và không yêu cầu `Idempotency-Key`.
Endpoint không gọi resource availability, không lock/reserve resource và không tạo hoặc sửa
ShuttleTrip, ShuttlePassenger, reservation hay Outbox.

Request:

```json
{
  "mainTripId": "36000000-0000-4000-8000-000000000101",
  "direction": "INBOUND_TO_STATION",
  "scheduledDepartureTime": "2026-09-01T14:30:00+07:00",
  "orderedBookingIds": [
    "36000000-0000-4000-8000-000000000301",
    "36000000-0000-4000-8000-000000000302"
  ]
}
```

Với inbound, BE giữ nguyên thứ tự Booking và ước tính chuỗi `pickup đầu tiên → các pickup còn
lại → origin Station`; không cộng quãng đường từ Station tới pickup đầu tiên.
`hardCutoffAt = mainTrip.departureDateTime - 30 phút` và
`estimatedFinishAt = scheduledDepartureTime + tổng Goong duration + bookingCount ×
SHUTTLE_STOP_SERVICE_MINUTES` (mặc định 5 phút). Tuyến dài được chunk theo
`GOONG_MAX_DESTINATIONS_PER_REQUEST` mà không đổi thứ tự; một chunk không dùng được làm toàn bộ
kết quả thành `UNKNOWN`.

Response `200 LATE_RISK`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "status": "LATE_RISK",
    "estimatedFinishAt": "2026-09-01T15:47:00+07:00",
    "hardCutoffAt": "2026-09-01T15:30:00+07:00",
    "delayMinutes": 17,
    "warningCode": "SHUTTLE_LATE_RISK",
    "lateRiskBlocksCreate": false,
    "basis": "GOONG"
  },
  "meta": {}
}
```

Status semantics:

| `status` | Ý nghĩa | Nullable fields |
|---|---|---|
| `SAFE` | ETA không sau cutoff; `delayMinutes=0`. | `warningCode=null`; ETA, cutoff và `basis=GOONG` có giá trị. |
| `LATE_RISK` | ETA sau cutoff; `delayMinutes` làm tròn lên phút. | Không field nghiệp vụ nào nullable; `warningCode=SHUTTLE_LATE_RISK`. |
| `UNKNOWN` | Goong/config/timeout/response hoặc tọa độ Station không dùng được. | `estimatedFinishAt`, `delayMinutes`, `warningCode`, `basis` là `null`; `hardCutoffAt` vẫn có nếu Main Trip đã load được. |
| `NOT_APPLICABLE` | Outbound; BE không gọi Goong. | ETA, cutoff, delay, warning và basis đều `null`. |

`lateRiskBlocksCreate=false` cho mọi status. Đây không phải bảo đảm create thành công:
`POST /v1/operator/shuttle-trips` giữ nguyên body và `Idempotency-Key`, đồng thời vẫn áp dụng
capacity, resource conflict, cutoff và Booking-state guards hiện hành. Client không gửi `force`,
`warningAccepted` hoặc payload split.

Tenant được lấy từ JWT. Main Trip khác tenant trả `404 TRIP_NOT_FOUND`. Mọi Booking group được
chọn phải còn đầy đủ ở `PENDING_ASSIGNMENT`; selection stale trả `409
SHUTTLE_REQUEST_SET_CHANGED`. Errors khác: `404 STATION_NOT_FOUND`; `422 VALIDATION_ERROR`.

### GET `/v1/driver/shuttle-trips`

Auth: `DRIVER` only. Chỉ trả ShuttleTrip có `driverUserId` trùng với `sub` trong JWT và bỏ qua
trạng thái `CANCELLED`.

Query tùy chọn: `from=YYYY-MM-DD`, `to=YYYY-MM-DD`. Khi không truyền, cửa sổ mặc định là ngày
hiện tại đến ngày hiện tại + 14 ngày theo Asia/Ho_Chi_Minh. `to` không được trước `from`; khoảng truy vấn tối
đa 32 ngày.

Response `200`:

```json
{
  "from": "2026-08-05",
  "to": "2026-08-19",
  "items": [
    {
      "shuttleTripId": "uuid",
      "mainTripId": "uuid",
      "direction": "INBOUND_TO_STATION",
      "status": "SCHEDULED",
      "vehicleId": "uuid",
      "licensePlate": "51B-123.45",
      "scheduledDepartureTime": "2026-08-05T08:00:00+07:00",
      "scheduledEndTime": "2026-08-05T09:00:00+07:00",
      "passengerCount": 2,
      "stopCount": 1
    }
  ]
}
```

Errors: `401 UNAUTHORIZED`; `403 FORBIDDEN`; `422 VALIDATION_ERROR`.

### GET `/v1/driver/shuttle-trips/{shuttleTripId}/manifest`

Auth: `DRIVER` only và chỉ Driver được gán cho ShuttleTrip được đọc. Driver khác nhận `403
FORBIDDEN`; ShuttleTrip không tồn tại nhận `404 SHUTTLE_TRIP_NOT_FOUND`.

Response `200` gồm ShuttleTrip, main Trip, direction, trạng thái, Station, lịch chạy và danh sách
`stops` tăng dần theo `pickupOrder`. Các ShuttlePassenger cùng `bookingId + pickupOrder` được gom
thành một pickup group. Một group phải có cùng trạng thái; dữ liệu mixed status trả `409
SHUTTLE_MANIFEST_INCONSISTENT_STATUS` thay vì báo sai `PENDING`.

```json
{
  "shuttleTripId": "uuid",
  "mainTripId": "uuid",
  "direction": "INBOUND_TO_STATION",
  "status": "SCHEDULED",
  "stationId": "uuid",
  "stationName": "Bến xe Miền Đông",
  "stationLatitude": 10.8012,
  "stationLongitude": 106.7144,
  "scheduledDepartureTime": "2026-08-05T08:00:00+07:00",
  "scheduledEndTime": "2026-08-05T09:00:00+07:00",
  "stops": [
    {
      "pickupOrder": 1,
      "bookingId": "uuid",
      "ticketIds": ["uuid"],
      "passengerCount": 1,
      "pickupAddress": "12 Nguyễn Huệ, Quận 1",
      "pickupLatitude": 10.7731,
      "pickupLongitude": 106.7032,
      "status": "PENDING",
      "pickedUpAt": null,
      "deliveredAt": null,
      "passengerDisplayName": "Nguyễn Văn A",
      "passengerPhone": "0900000000"
    }
  ]
}
```

Không trả ID giấy tờ, dữ liệu thanh toán hoặc thông tin ngoài nghiệp vụ. Errors: `401
UNAUTHORIZED`; `403 FORBIDDEN`; `404 SHUTTLE_TRIP_NOT_FOUND`; `404
SHUTTLE_STATION_NOT_FOUND`; `409 SHUTTLE_MANIFEST_INCONSISTENT_STATUS`.

### POST `/v1/driver/shuttle-trips/{shuttleTripId}/stops/{pickupOrder}/pickup`

Các endpoint driver bổ sung là `POST /v1/driver/shuttle-trips/{shuttleTripId}/stops/{pickupOrder}/delivered`, `POST /v1/driver/shuttle-trips/{shuttleTripId}/stops/{pickupOrder}/no-show`, `POST /v1/driver/shuttle-trips/{shuttleTripId}/start` và `POST /v1/driver/shuttle-trips/{shuttleTripId}/complete`. Chỉ driver được gán có quyền mutation và mọi mutation yêu cầu `Idempotency-Key`.

Auth: assigned `DRIVER` only. `Idempotency-Key` is required. The request has no body.

Atomically changes every `PENDING` passenger manifest at the requested `pickupOrder` to `PICKED_UP`
and records the same pickup timestamp for the whole group. Replaying the operation is a successful
no-op with `pickedUpPassengerCount: 0` when the group is already picked up.

Response `200`:

```json
{
  "shuttleTripId": "uuid",
  "pickupOrder": 1,
  "pickedUpPassengerCount": 2,
  "pickedUpAt": "2026-08-02T08:00:00+07:00"
}
```

Errors: `401 UNAUTHORIZED`; `403 FORBIDDEN`; `404 SHUTTLE_TRIP_NOT_FOUND`; `404
SHUTTLE_PICKUP_NOT_FOUND`; `409 SHUTTLE_TRIP_TERMINAL`; `409 SHUTTLE_PICKUP_NOT_PENDING`; `422
VALIDATION_ERROR`; `422 IDEMPOTENCY_KEY_MISMATCH`.

Delivered/no-show/start/complete/cancel transitions that do not match the state machine return
`409 SHUTTLE_TRIP_INVALID_STATE` or `409 SHUTTLE_PASSENGER_INVALID_STATE`; a blank reason returns
`422 VALIDATION_ERROR`. All Shuttle mutations repeat the active/approved operator and
`enableShuttle=true` subscription guard and are idempotent.

### Shuttle fields trong Booking

Booking hỗ trợ đồng thời `shuttlePickup` cho inbound và `shuttleDropoff` cho outbound, bao gồm từng leg round-trip. Mỗi booking có tối đa một intent active cho mỗi direction. Trip gọi Goong Directions với `vehicle=car`: `distanceMeters <= 10000` được phép, lớn hơn 10000 trả `422 SHUTTLE_DISTANCE_EXCEEDED`, còn lỗi upstream/timeout/thiếu key/response sai trả `503 SHUTTLE_DISTANCE_UNAVAILABLE`. Event mới dùng `shuttleRequests[]`; consumer vẫn đọc `shuttlePickup` cũ như inbound.

Trip configuration is `SHUTTLE_MAX_DISTANCE_KM=10`, `ROUTING_PROVIDER=GOONG|LOCAL`,
`GOONG_API_KEY`, `GOONG_BASE_URL=https://rsapi.goong.io`,
`GOONG_MAX_DESTINATIONS_PER_REQUEST=10`, and `TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS=1500`.
`ROUTING_PROVIDER=LOCAL`, missing Goong configuration, `401`/`403`/`429`/`5xx`, timeout,
malformed response or strict leg validation fails closed for this endpoint.

`POST /v1/bookings` và mỗi leg của round-trip nhận optional `shuttlePickup: { address, latitude, longitude }` cho inbound và `shuttleDropoff: { address, latitude, longitude }` cho outbound. Chỉ origin/destination Station tương ứng, active, có `supportsShuttle=true` và đủ tọa độ được nhận; Stop dọc tuyến không được dùng làm điểm shuttle. Booking dùng `TripSnapshot.departureDateTime` để từ chối request tại/sau T-30 với `409 SHUTTLE_REQUEST_CUTOFF_PASSED`. Khi intent còn active, `edit-pickup`/`edit-dropoff` bị khóa theo direction.

### GET `/v1/stations/search`

Auth: public.

Purpose: passenger/FE station autocomplete. Mutation endpoints remain operator/admin-only.

Query: `q`, `city?`, `ward?`, `locationId?`, `locationScopeCode?`.

`q` is required. Blank or empty `q` is invalid and returns `422 VALIDATION_ERROR`.

Matching: accent-insensitive contains via `unaccent(name) ILIKE unaccent('%' || q || '%')`.

`locationId` keeps its exact Station-location semantics. `locationScopeCode` accepts either a
two-digit root code (active root plus active direct leaves) or a five-digit active leaf code
(exact). The parameters are mutually exclusive. Unknown, inactive, malformed, or unsupported
codes return `422 VALIDATION_ERROR`; `q`, `city`, and `ward` continue narrowing the selected scope.

Day-7 exception: this endpoint intentionally uses `q` (not BSOT §5.8 `search`) because `technical_context_v7` line 523 is higher-priority for the OperatorStation Management flow.

`pg_trgm` is enabled only for canonical schema compatibility with the deferred `idx_stations_name_trgm ... gin_trgm_ops WHERE FALSE` placeholder. Trigram similarity search and distance-from-operator-coordinates ranking are deferred.

Response `200`: `StationSearchResult[]` in the ADR 0004 success envelope.

`StationSearchResult` shape:
```json
{
  "id": "uuid",
  "name": "Bến xe Miền Tây",
  "city": "Ho Chi Minh City",
  "ward": "An Lac",
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
  "locationCode": "26506",
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
- create branch requires exactly one of `locationId` or official leaf `locationCode`. The Location
  must be active `WARD|COMMUNE|SPECIAL_ZONE` with an active root parent. `city` and `ward`
  compatibility snapshots are derived from that hierarchy; client text is not authoritative.
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
        "ward": "An Lac",
        "latitude": 10.7212345,
        "longitude": 106.6267890,
        "addressStreet": "Kinh Dương Vương",
        "supportsShuttle": true
      }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
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
  "locationCode": "26506",
  "address": "123 Hồng Bàng, Quận 6",
  "googlePlaceId": "ChIJ1234567890"
}
```

Exactly one of `locationId` or `locationCode` is required. It must resolve to an active
`WARD|COMMUNE|SPECIAL_ZONE` under an active province/municipality; top-level, inactive, missing,
or ambiguous references return `422 VALIDATION_ERROR`.

Response `201`: created Stop DTO in ADR 0004 envelope. Every public/operator/admin `StopDto`
includes the canonical leaf `locationId` plus `city` and `ward` names resolved from the current
Location hierarchy. `city` is the parent province/municipality display name and `ward` is the
`WARD|COMMUNE|SPECIAL_ZONE` display name. FE does not need a second Location lookup to render the
Stop address. These fields are read-time projections, not duplicated Stop persistence columns.

```json
{
  "id": "uuid",
  "name": "Trạm dừng Phú Lâm",
  "locationId": "uuid",
  "city": "Thành phố Hồ Chí Minh",
  "ward": "Phường Vũng Tàu",
  "address": "123 Hồng Bàng"
}
```

### GET `/v1/operator/stops`

Auth: `OPERATOR_STAFF`, `OPERATOR_ADMIN`.

Query: `page?`, `pageSize?`, `search?`, `isActive?`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-25T17:00:00+07:00" }
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

`GET /v1/admin/stations` accepts `page?`, `pageSize?`, `search?`, `isActive?`,
`supportsShuttle?`, `sortBy?` (`name|createdAt|updatedAt`) and `sortDir?` (`asc|desc`). Search is
case- and Vietnamese-accent-insensitive over name, city, ward, addressStreet and slug. The
backwards-compatible default remains `name asc`; filters and sort apply before count/paging.

`GET /v1/admin/stations/summary` is `SYSTEM_ADMIN`-only, accepts no query keys and returns:

```json
{ "total": 100, "active": 90, "inactive": 10, "supportsShuttle": 24 }
```

Counts include all non-soft-deleted Stations; `supportsShuttle` is independent of activation.

### PATCH `/v1/admin/stations/{id}`

Auth: `SYSTEM_ADMIN`. The existing request contract remains additive and accepts any non-empty
subset of:

```json
{
  "name": "Ben xe Mien Dong Moi",
  "addressStreet": "501 Hoang Huu Nam",
  "locationId": "uuid",
  "city": "Ho Chi Minh City",
  "ward": "Thu Duc",
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
deterministically regenerated from `name + city + ward`; collision uses a station-ID hash
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
the IDs must differ. Primary wins `name,slug,city,ward`; `addressStreet`, `locationId`,
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
      "city": "Ho Chi Minh City",
      "ward": "Thu Duc",
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-16T08:00:00+07:00" }
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
  "city": "Ho Chi Minh City",
  "ward": "Thu Duc",
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
  "createdAt": "2026-06-10T17:00:00+07:00",
  "updatedAt": "2026-06-10T17:00:00+07:00"
}
```

`returnRouteId` is nullable and one-way: setting Route A `returnRouteId = B` does not mutate Route B.

`pathPolyline` is nullable and appears on Route detail/mutation responses only. `GET /v1/operator/routes` returns `PagedResult<RouteListItemDto>` with the same fields except `pathPolyline`, preventing a large geometry string per list item. Each list item additionally includes `departureSchedules`, containing all recurring DriverSchedules owned by the caller operator for that Route, including inactive, expired, and future schedules. A Route without schedules returns `departureSchedules: []`.

Each `departureSchedules` item has this shape:

```json
{
  "id": "uuid",
  "dayOfWeek": [1, 3, 5],
  "departureTime": "08:00:00",
  "timeZone": "Asia/Ho_Chi_Minh",
  "validFrom": "2026-07-01",
  "validUntil": "2026-12-31",
  "isActive": true
}
```

`dayOfWeek` uses `1=Monday` through `7=Sunday`. `departureTime` is a timezone-free `TIME` value interpreted with the returned constant `timeZone: "Asia/Ho_Chi_Minh"`. `validUntil` is nullable. Items are ordered by `departureTime`, then `validFrom`, then `id`.

### POST `/v1/operator/routes`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Write requires caller operator to be `APPROVED` and active; non-APPROVED or inactive operators get
`403 FORBIDDEN`. Before any Route validation or persistence, Trip also validates a general active
subscription with `requireShuttleModule=false`; Route creation never depends on `enableShuttle`.
An expired subscription returns `402 SUBSCRIPTION_EXPIRED`, and unavailable/malformed Identity
data returns `503 UPSTREAM_UNAVAILABLE`.

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

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Optional `search` follows BSOT §5.8 and matches Route `name` hoặc prefix nullable `code`; `isActive` is a boolean activation filter applied before count/paging. Release A accepts optional `code` on create/full-create and returns it on Route list/detail/mutation DTOs; Release B makes it required for new Route after FE rollout. Code is normalized uppercase, unique per active/non-deleted operator scope, and duplicates return `409 ROUTE_CODE_DUPLICATED`.

Response `200`: `PagedResult<RouteListItemDto>` in the ADR 0004 success envelope.

```json
{
  "id": "uuid",
  "code": "SG-DL-01",
  "name": "Hồ Chí Minh - Đà Lạt",
  "originStationId": "uuid",
  "destinationStationId": "uuid",
  "isActive": true
}
```

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
  "createdAt": "2026-06-10T17:00:00+07:00",
  "updatedAt": "2026-06-10T17:00:00+07:00"
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
  "createdAt": "2026-06-10T17:00:00+07:00",
  "updatedAt": "2026-06-10T17:00:00+07:00"
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
  "effectiveFrom": "2026-06-30T17:00:00Z",
  "effectiveUntil": "2026-07-31T17:00:00Z"
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
  "createdAt": "2026-06-10T17:00:00+07:00",
  "updatedAt": "2026-06-10T17:00:00+07:00"
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
      "createdAt": "2026-06-10T17:00:00+07:00",
      "updatedAt": "2026-06-10T17:00:00+07:00"
    }
  ],
  "createdAt": "2026-06-10T17:00:00+07:00",
  "updatedAt": "2026-06-10T17:00:00+07:00"
}
```

Each main Route may have any number of active AlternativeRoutes; there is no global active-count cap.
AlternativeRoute stops are an independent stop sequence and do not reuse RouteStop rows.

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

AlternativeRoute delete deactivates the row by setting `isActive=false`; it is not a hard-delete and `alternative_routes` has no `deleted_at`.

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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-10T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-10T17:00:00+07:00" }
}
```

`ALTERNATIVE_ROUTE_LIMIT_EXCEEDED` is retired and is no longer emitted. Creating a third or later
active AlternativeRoute follows the same success contract as any other valid create request.

## Trip Vehicle and Driver Schedule Management (Day 9)

### Role matrix and shared rules

| Method | Role(s) |
|---|---|
| `POST`, `PATCH` | `OPERATOR_ADMIN` only |
| `GET` list/by-id | `OPERATOR_ADMIN`, `OPERATOR_STAFF` |

All public responses use the ADR 0004 `ApiResponse<T>` envelope. Success responses include `{ success, statusCode, data, meta }`; errors include `{ success: false, statusCode, error: { code, message, fields? }, meta }`.

Original Day-9 Vehicle/DriverSchedule create and activate writes do not require
`Idempotency-Key`. They are two explicit members of the canonical 20-route system-wide exemption
inventory and carry auditable runtime exemption metadata; callers must not add a key as a hidden
precondition. The Day-22 full DriverSchedule PATCH and its deprecated `/crew` alias explicitly
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
  "createdAt": "2026-06-11T17:00:00+07:00",
  "updatedAt": "2026-06-11T17:00:00+07:00",
  "currentAssignment": null,
  "nextAssignment": {
    "sourceType": "TRIP",
    "tripId": "uuid",
    "shuttleTripId": null,
    "driverUserId": "uuid",
    "plannedStartAt": "2026-08-11T08:00:00Z",
    "plannedEndAt": "2026-08-11T10:00:00Z",
    "status": "RESERVED",
    "startStationId": "uuid",
    "endStationId": "uuid"
  }
}
```

`currentAssignment` is the Vehicle reservation whose status is `ACTIVE`; `nextAssignment` is the
nearest future `RESERVED` reservation. Both are nullable and may reference either a main Trip or a
ShuttleTrip. Vehicle does not persist a fixed/current driver.

The catalog contains the three platform-seeded system types:

| `code` | `defaultSeatCount` |
|---|---:|
| `STANDARD_BUS` | 45 |
| `LIMOUSINE` | 9 |
| `SLEEPER_BUS` | 40 |

`isSystemDefined=true` blocks deletion in the application layer. Day 9 exposes the catalog as read-only; it does not expose a VehicleType delete endpoint.

### GET `/v1/vehicle-types`

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

Query: `page?`, `pageSize?`, `search?`, `searchIn?`, `sortBy?`, `sortDir?`, `vehicleTypeId?`, `status?`, `isActive?`.

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
  "createdAt": "2026-06-11T17:00:00+07:00",
  "updatedAt": "2026-06-11T17:00:00+07:00",
  "currentAssignment": {
    "sourceType": "TRIP",
    "tripId": "uuid",
    "shuttleTripId": null,
    "driverUserId": "uuid",
    "plannedStartAt": "2026-08-11T08:00:00+07:00",
    "plannedEndAt": "2026-08-11T10:00:00+07:00",
    "status": "ACTIVE",
    "startStationId": "uuid",
    "endStationId": "uuid"
  },
  "nextAssignment": null
}
```

`currentAssignment` is the vehicle's `ACTIVE` reservation; `nextAssignment` is its nearest
future `RESERVED` reservation. Both are nullable and apply equally to main Trip and ShuttleTrip.
Driver ownership is assignment-scoped; Vehicle does not expose or persist a fixed
`currentDriverId`.

### POST `/v1/operator/vehicles`

Auth: `OPERATOR_ADMIN`.

Idempotency-Key: not required by BSOT §5.6.

Before Vehicle validation or persistence, Trip validates the caller operator is active/approved
and has a general active subscription with `requireShuttleModule=false`. Vehicle creation never
depends on `enableShuttle`. Exact guard failures are `402 SUBSCRIPTION_EXPIRED`, `403 FORBIDDEN`,
and `503 UPSTREAM_UNAVAILABLE`.

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

Pagination follows BSOT §5.7 defaults (`page=1`, `pageSize=20`, max `100`). Search and sort follow BSOT §5.8; the allowed search field is `licensePlate`. `vehicleTypeId` is exact. `status` accepts only `ACTIVE|MAINTENANCE|OFF_DUTY|RETIRED`; `isActive` is an independent boolean and no `INACTIVE` Vehicle status exists. Only non-soft-deleted Vehicles owned by the caller's operator are returned.

Response `200`: `PagedResult<VehicleDto>` in the ADR 0004 success envelope. `VehicleDto` keeps
`totalSeats` for compatibility and also returns computed `usablePassengerCapacity`, which counts
layout seats where `disabled=false` and `type != DRIVER_AREA`.

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
  "timeZone": "Asia/Ho_Chi_Minh",
  "validFrom": "2026-07-01",
  "validUntil": "2026-12-31",
  "baseFare": 400000,
  "isActive": true,
  "createdAt": "2026-06-11T17:00:00+07:00",
  "updatedAt": "2026-06-11T17:00:00+07:00"
}
```

`vehicleId`, `assistantUserId`, `validUntil`, and `baseFare` are nullable. `dayOfWeek` is a JSON array using `1=Monday`, `2=Tuesday`, ..., `7=Sunday`. `departureTime` is a timezone-free `TIME` value interpreted with the returned constant `timeZone: "Asia/Ho_Chi_Minh"`. `baseFare` is an optional recurring override in VND; generated Trips use `DriverSchedule.baseFare ?? Route.baseFare`.

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
  "baseFare": 400000,
  "isActive": true
}
```

Validation:
- `dayOfWeek` must be a non-empty JSON array containing only integers from `1` through `7`. An empty array, a non-integer entry, or an entry outside `1..7` returns `422 VALIDATION_ERROR` with `error.fields.dayOfWeek`.
- `validUntil`, when present, must be on or after `validFrom`; otherwise return `422 VALIDATION_ERROR` with `error.fields.validUntil`.
- `baseFare`, when present, must be a non-negative BIGINT-compatible VND amount persisted to the đồng. Omitted or `null` means future generated Trips fall back to `Route.baseFare`.
- `routeId` must resolve to an active Route owned by the caller's operator. A missing, inactive, or cross-operator Route returns `404 ROUTE_NOT_FOUND`.
- `vehicleId`, when present, must resolve to a non-soft-deleted Vehicle owned by the caller's operator; otherwise return `404 VEHICLE_NOT_FOUND`.
- `driverUserId` must resolve through Identity `GET /internal/v1/users/{userId}` to a user with `role=DRIVER` under the caller operator. Missing Identity user, wrong role, wrong operator, or upstream logical-FK validation failure returns `422 VALIDATION_ERROR` with `error.fields.driverUserId`.
- `assistantUserId`, when present, must resolve through Identity `GET /internal/v1/users/{userId}` to a user with `role=ASSISTANT` under the caller operator. Missing Identity user, wrong role, wrong operator, or upstream logical-FK validation failure returns `422 VALIDATION_ERROR` with `error.fields.assistantUserId`.
- An active schedule conflicts when the same `driverUserId` has any intersecting `dayOfWeek`, the same `departureTime` interpreted in `Asia/Ho_Chi_Minh`, and an overlapping `[validFrom, validUntil]` window. Return `409 TRIP_DRIVER_CONFLICT`.
- Canonical availability supersedes the exact-departure sentence above: Driver, Assistant, and
  Vehicle must satisfy `next.start >= previous.end + 30 minutes + repositionTravelTime` against
  both adjacent assignments across main Trip and ShuttleTrip. Reposition uses Goong Directions
  `vehicle=car`; unavailable travel-time input returns `503 RESOURCE_TRAVEL_TIME_UNAVAILABLE`. Conflict
  responses retain `TRIP_DRIVER_CONFLICT`/`TRIP_VEHICLE_CONFLICT` and add
  `error.fields.conflictReason=TIME_OVERLAP|TURNAROUND_REQUIRED|REPOSITION_REQUIRED|RESOURCE_ACTIVE`.

Response `201`: `DriverScheduleDto` in the ADR 0004 success envelope.

Creating a DriverSchedule persists the recurring assignment and, when active, is the Day-11 trigger for Trip generation enqueue after the schedule commit succeeds. Day 9 shipped persistence only; the Day-11 contract closes the deferred driver/assistant role+operator validation carryover.

### POST `/v1/operator/driver-schedules/availability-check`

Auth: `OPERATOR_ADMIN`. Read-only preview; no `Idempotency-Key` and no reservation is created. Body
contains the DriverSchedule create fields except `baseFare` and `isActive`. Backend derives route
duration and endpoint Stations. Response `200`:

```json
{
  "available": false,
  "turnaroundMinutes": 30,
  "hasMore": false,
  "conflicts": [{
    "resourceRole": "DRIVER",
    "resourceId": "uuid",
    "reason": "REPOSITION_REQUIRED",
    "conflictingSourceType": "TRIP",
    "conflictingSourceId": "uuid",
    "sampleRequestedStartAt": "2026-08-11T10:01:00Z",
    "blockingUntil": "2026-08-11T13:31:00Z",
    "earliestFeasibleStartAt": "2026-08-11T13:31:00Z",
    "requiredTravelMinutes": 181,
    "turnaroundMinutes": 30
  }]
}
```

At most 100 conflicts are returned in occurrence order; `hasMore=true` means the result was
truncated. Mutation endpoints always recheck under lock, so preview is never a reservation.

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
  "baseFare": 420000,
  "isActive": true
}
```

`routeId` and `validFrom` are not editable. Omitted means unchanged. Explicit `null` clears only
`assistantUserId`, `vehicleId`, `validUntil`, or `baseFare`; `validUntil:null` restores an open-ended
window and `baseFare:null` restores Route-fare fallback for Trips generated later.
Explicit `null` for `departureTime`, `dayOfWeek`, `driverUserId`, or `isActive`, and empty,
unknown-only, or malformed bodies return `422 VALIDATION_ERROR`. Missing/invalid `applyTo` also
returns `422 VALIDATION_ERROR`. Changing `departureTime`/`dayOfWeek` through `ALL_PENDING` is the
only canonical path that cascades a new `departureDateTime` to generated Trips;
`departureDateTime` is absent from the Trip PATCH body and changed-field registry. No dedicated
Trip schedule endpoint or Gateway route exists.

For each actual departure change, compute `delta = |newDeparture - oldDeparture|` and compare the
calendar dates in Asia/Ho_Chi_Minh (`Asia/Ho_Chi_Minh`): MINOR is the same Asia/Ho_Chi_Minh date with `delta <= 2h`; MEDIUM
is the same Asia/Ho_Chi_Minh date with `delta > 2h && delta < 6h`; MAJOR is `delta >= 6h` or any Asia/Ho_Chi_Minh date
change.

Scope behavior:

- `FUTURE_ONLY` changes the recurring schedule and leaves every generated Trip unchanged. If the
  schedule is active and has a vehicle, generation creates only uncovered future dates. It does
  not call Booking because no generated Trip is mutated. With
  `vehicleId:null`, it clears only the schedule vehicle and every attempted date is skipped using
  the existing `TripGenerationSkipLog` reason `OTHER` with a message identifying that no vehicle
  is assigned; no new Trip is generated until a vehicle is assigned. A supplied `baseFare` is set
  or cleared only in this scope; same-value is a no-op. Each newly generated Trip snapshots
  `DriverSchedule.baseFare ?? Route.baseFare` once.
- `ALL_PENDING` applies the effective schedule values to every linked Trip whose status is
  `SCHEDULED|BOARDING`. `vehicleId:null` is rejected with `422 VALIDATION_ERROR` before any
  Booking call or mutation because `Trip.vehicleId` is required. Removing a day cancels pending
  Trips that no longer match. Shortening `validUntil` or setting `isActive=false` only stops future
  generation and never cancels/mutates an already generated Trip; clearing `validUntil` or
  reactivating may generate uncovered future dates only. Supplying `baseFare` with `ALL_PENDING`
  returns `422 VALIDATION_ERROR` on `baseFare` before any write or downstream call; generated Trip
  fares are changed only through the existing Trip PATCH contract.

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

Auth: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Query: `page?`, `pageSize?`, `routeId?`, `driverUserId?`, `isActive?`, `vehicleTypeId?`, `search?`. `search` is an OR across Route name, assigned Vehicle license plate, and active assigned Driver/Assistant display name; the crew-name IDs are resolved through the Internal Identity endpoint before Trip count/paging. Identity failure returns `503 UPSTREAM_UNAVAILABLE`. `vehicleTypeId` filters the assigned Vehicle before paging. DriverSchedule is recurring weekly only; `isOneTime` is not supported. Response is a paged schedule list. Each item retains the existing schedule IDs and fields, and adds `route` (including `originStation`/`destinationStation`), nullable `vehicle` (including `imageUrls`), and nullable `driver`/`assistant` summaries `{ id, displayName, avatarUrl, role, operatorId, status }`.

### GET `/internal/v1/operators/{operatorId}/crew/search?search=`

Auth: valid Internal JWT only; not exposed through Gateway. `search` is required, trimmed and at
most 255 characters. It matches active `DRIVER|ASSISTANT` display names within the exact operator.
Success `200` is a raw array without an `ApiResponse` wrapper:

```json
[{ "userId": "uuid", "displayName": "Nguyen Van A", "role": "DRIVER" }]
```

### GET `/internal/v1/routes/search?operatorId=&search=`

Auth: valid Internal JWT only; not exposed through Gateway. Both parameters are required;
`search` is trimmed and at most 255 characters. Search is limited to non-soft-deleted Routes owned
by `operatorId` and matches Route name or origin/destination Station text. Success `200` is raw:

```json
{ "routeIds": ["uuid"] }
```

### Read-model additions

- `GET /v1/stations/{id}` is public and returns the full active `StationDto`; missing/inactive returns `404 STATION_NOT_FOUND`.
- `GET /v1/operator/stations` is paged/searchable for operator staff/admin and returns mapping fields plus the full canonical `station` object. Its `search` is case- and Vietnamese-accent-insensitive over Station name.
- `GET /v1/admin/stations?search=` applies the same matching over Station name and the derived `city`/`ward` snapshots.
- `GET /v1/operator/stops?search=` and `GET /v1/admin/stops?search=` apply the same matching over Stop name/address.
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-11T17:00:00+07:00" }
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
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-11T17:00:00+07:00" }
}
```

## Day 18 — Driver operational schedule

### GET `/v1/driver/me/schedule?from={yyyy-MM-dd}&to={yyyy-MM-dd}`

Auth: `DRIVER` or `ASSISTANT`.

Returns only Trips assigned to the authenticated JWT `sub`, where the caller is either the
Trip's `driverUserId` or `assistantUserId`. A caller cannot supply or override a user identifier.

`from` and `to` are `Asia/Ho_Chi_Minh` calendar dates and are inclusive at both ends. Both parameters
must be supplied together or omitted together. When both are omitted, the range defaults to the
current Asia/Ho_Chi_Minh date through current Asia/Ho_Chi_Minh date plus 14 days. Supplying exactly one parameter, or a
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
        "departureDateTime": "2026-06-30T08:00:00+07:00",
        "estimatedArrivalTime": "2026-06-30T11:00:00+07:00",
        "status": "SCHEDULED",
        "assignmentRole": "DRIVER"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T10:00:00+07:00"
  }
}
```

Trips are ordered by `departureDateTime`, then by `tripId`. Date filtering converts the inclusive
Asia/Ho_Chi_Minh date range to UTC boundaries before querying. No Trip state is mutated.

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
        "estimatedArrivalTime": "2026-07-12T10:30:00+07:00",
        "allowPickup": true,
        "allowDropoff": true
      }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-12T08:00:00+07:00" }
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

All lifecycle mutations below have no request body. They require an `Idempotency-Key` header whose
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

### POST `/v1/driver/trips/{tripId}/boarding`

Auth: `DRIVER` only. The authenticated JWT `sub` must equal the Trip's `driverUserId`; an existing
Trip assigned to another Driver returns `403 FORBIDDEN`. `ASSISTANT` is not allowed. The request
has no body and requires the idempotency semantics above.

### POST `/v1/operator/trips/{tripId}/boarding`

Auth: `OPERATOR_ADMIN` only. The Trip must belong to the caller's JWT `operatorId`; missing or
cross-tenant Trips are both masked as `404 TRIP_NOT_FOUND`. `OPERATOR_STAFF` is not allowed. The
request has no body and requires the idempotency semantics above.

Both endpoints implement the same manual `SCHEDULED -> BOARDING` transition. It is allowed when
`departureDateTime <= now + TRIP_MANUAL_BOARDING_EARLY_WINDOW_MINUTES`; the configurable window
defaults to 180 minutes and equality is allowed. An otherwise eligible Trip outside the window
returns `409 TRIP_BOARDING_TOO_EARLY`. A Trip already in `BOARDING` returns the same `200` response
as a no-op without a second audit or event; every other current status returns
`409 TRIP_INVALID_TRANSITION`.

A real transition appends `TRIP_BOARDING_STARTED_MANUAL` with the authenticated actor and exact
metadata `{tripId,role}`, and publishes exactly one existing `trip.trip.boarding_started` event
with `{tripId,boardingStartedAt}` in the same Trip-local transaction. Manual boarding and
`AutoBoardingJob` lock the same Trip row and recheck status after the lock, so their race cannot
duplicate the event or move `IN_PROGRESS` back to `BOARDING`.

Response `200` uses the ADR 0004 success envelope. Data is exactly `{tripId,status}`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "BOARDING"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-22T06:30:00+07:00" }
}
```

Errors: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`;
`409 TRIP_BOARDING_TOO_EARLY`; `409 TRIP_INVALID_TRANSITION`;
`409 IDEMPOTENCY_REQUEST_PENDING`; `422 IDEMPOTENCY_KEY_MISMATCH`;
`422 VALIDATION_ERROR`.

### POST `/v1/driver/trips/{tripId}/start`

Auth: `DRIVER` only. The authenticated JWT `sub` must equal the Trip's `driverUserId`; an existing
Trip assigned to another user returns `403 FORBIDDEN`. The request has no body and requires the
idempotency semantics above.

Precondition: Trip status is `BOARDING`. There is no earliest-time guard once boarding has opened,
so the assigned Driver may call this endpoint immediately after either manual or automatic
boarding. Calling it directly from `SCHEDULED` remains `409 TRIP_INVALID_TRANSITION`; clients must
first call a boarding endpoint and then call start with a different `Idempotency-Key`. A successful
transition sets status to `IN_PROGRESS`,
captures `actualDepartureTime`, and publishes `trip.trip.started` through the Trip Outbox in the
same Trip-local transaction. The existing assigned-resource activation check remains authoritative;
an `ACTIVE` conflicting Driver/Vehicle/Shuttle reservation blocks start without changing Trip
status. Any other current status returns `409 TRIP_INVALID_TRANSITION`.

Response `200` uses the ADR 0004 success envelope. Every data field is required and non-null:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "IN_PROGRESS",
    "actualDepartureTime": "2026-06-22T08:30:00+07:00"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-22T08:30:00+07:00" }
}
```

Data schema: `{ tripId: string(uuid), status: "IN_PROGRESS", actualDepartureTime: string(date-time) }`.

Errors: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`;
`409 TRIP_INVALID_TRANSITION`; `409 IDEMPOTENCY_REQUEST_PENDING`;
`422 IDEMPOTENCY_KEY_MISMATCH`; `422 VALIDATION_ERROR`.

### POST `/v1/driver/trips/{tripId}/stops/{stopId}/arrive` and destination arrival

Assigned `DRIVER|ASSISTANT` only. Under a Trip lock, Trip locks the ordered TripStop snapshot by
`orderIndex,stopId`. An intermediate stop may arrive only after every earlier non-skipped stop has
`actualDepartureTime`; destination may arrive only after every non-skipped stop has departed.
Violation returns `409 TRIP_STOP_SEQUENCE_VIOLATION` with fields
`blockingStopId,target,requiredAction=DEPART_BLOCKING_STOP`. Successful Trip detail reload exposes
the persisted nullable `stops[].actualDepartureTime`.

### POST `/v1/driver/trips/{tripId}/complete`

Auth: `DRIVER` or `ASSISTANT`. For `DRIVER`, authenticated JWT `sub` must equal
`trip.driverUserId`; for `ASSISTANT`, it must equal `trip.assistantUserId`. Any role/assignment
mismatch returns `403 FORBIDDEN`. The request has no body and requires the idempotency semantics
above.

Preconditions: Trip status is `IN_PROGRESS` and `destinationArrivedAt` is present. The destination
guard runs under the Trip lock before Parcel clearance; failure returns
`409 TRIP_DESTINATION_NOT_ARRIVED` with
`requiredAction=ARRIVE_DESTINATION_BEFORE_COMPLETION`. A successful transition sets status to `COMPLETED`,
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
    "completedAt": "2026-06-22T12:30:00+07:00",
    "completedByUserId": "7226afd8-c107-413f-8235-c39e75f7a71f"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-22T12:30:00+07:00" }
}
```

Data schema: `{ tripId: string(uuid), status: "COMPLETED", completedAt: string(date-time), completedByUserId: string(uuid) }`.

Errors: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`;
`409 TRIP_INVALID_TRANSITION`; `409 TRIP_DESTINATION_NOT_ARRIVED`;
`409 IDEMPOTENCY_REQUEST_PENDING`;
`422 IDEMPOTENCY_KEY_MISMATCH`; `422 VALIDATION_ERROR`.

### POST `/v1/driver/trips/{tripId}/stops/{stopId}/depart`

Auth: assigned `DRIVER` or nullable assigned `ASSISTANT` for the same tenant. The request is
bodyless and requires a UUID-v4 `Idempotency-Key` using the lifecycle fingerprint above. The
first execution is valid only when `Trip.status=IN_PROGRESS`, `TripStop.status=ARRIVED`, and
`TripStop.actualDepartureTime IS NULL`. Trip and TripStop are locked (or an equivalent CAS is
used), the timestamp is persisted, then Trip calls the exact Booking pending-count seam. Every
success emits one `trip.stop.departed` operational fact; a positive passenger count additionally
emits one `trip.stop.departed_with_pending` warning event.

Response `200` uses the public ADR 0004 envelope and data is exactly `{ tripId, stopId, departedAt, pendingPassengerCount, eventEmitted }`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid",
    "stopId": "uuid",
    "departedAt": "2026-06-25T17:00:00+07:00",
    "pendingPassengerCount": 2,
    "eventEmitted": true
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-25T17:00:00+07:00" }
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

Returns paid Booking passenger records without adding per-passenger identity. The assigned crew may
see the Booking buyer snapshot only while the Trip is `BOARDING` or `IN_PROGRESS`; the buyer fields
are null in every other Trip status. Items are ordered by the Trip snapshot stop `orderIndex`. A
terminal pickup (`pickupStationId` set and `pickupStopId` null) is treated as the origin with
`orderIndex = 0` and sorts first. The response sets `Cache-Control: private, no-store`.

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
        "boardingStatus": "PENDING",
        "pickupPointName": "Ngã tư Hàng Xanh",
        "buyerName": "Nguyễn Văn A",
        "buyerPhone": "+84888151546"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T10:00:00+07:00"
  }
}
```

`buyerName` and `buyerPhone` identify the Booking buyer/contact, not the individual occupying the
seat; every seat under the same `bookingCode` therefore carries the same values. The phone remains
canonical E.164. Missing/legacy/redacted buyer snapshots return null values, and a deleted-user
display marker is never exposed. `pickupPointName` is the Trip Stop name, or the origin Station name
when `pickupStop` is null; it may be null during a rolling deployment from an older Trip service.

The manifest includes Booking status `CONFIRMED`, `PARTIAL_NO_SHOW`, or `NO_SHOW` and only Ticket
status `ISSUED` or `USED`. It does not fabricate an item or buyer contact for an unmatched Trip
`BOOKED` seat. No eligible ticket returns `200` with `items: []`, not `404`. Unknown trip returns
`404 TRIP_NOT_FOUND`; validation failures return `422 VALIDATION_ERROR`.

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
    "boardedAt": "2026-06-30T10:00:00+07:00",
    "boardedAtStopId": null,
    "ticketId": "uuid",
    "ticketCode": "VT-20260630-ABCDEFGH",
    "ticketStatus": "USED"
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T10:00:00+07:00"
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

### POST `/v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm`

Auth: `DRIVER` or `ASSISTANT`. The caller must be assigned to the replacement Trip. This endpoint
is bodyless and requires an `Idempotency-Key` that is required UUID v4.

The active matching transfer is the row for the Passenger whose Booking currently points to
`newTripId`; it must have a non-null `newSeatNumber`. The first confirmation updates only that
BookingTransfer to `CONFIRMED` and persists `confirmedAt` plus `confirmedByUserId`. It never
rewrites Passenger boarding history or Ticket usage and never changes sibling transfer rows.

Response `200`:
```jsonc
{
  "success": true,
  "statusCode": 200,
  "data": {
    "bookingTransferId": "uuid",
    "passengerId": "uuid",
    "newTripId": "uuid",
    "confirmationStatus": "CONFIRMED",
    "confirmedAt": "2026-07-25T16:00:00+07:00",
    "confirmedByUserId": "uuid"
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-07-25T16:00:00+07:00" }
}
```

A same-key replay returns the persisted confirmation values as idempotent `200`. An
already-confirmed request also returns the persisted confirmation values as idempotent `200`.

Statuses: `200`, `401`, `403`, `404`, `409`, `422`.

- `401 AUTH_TOKEN_INVALID`
- `403 FORBIDDEN`: caller is not assigned to the replacement Trip.
- `404 BOOKING_TRANSFER_NOT_FOUND`: matching active transfer is absent.
- `409 BOOKING_TRANSFER_SEAT_PENDING`: `newSeatNumber` is null.
- `422 VALIDATION_ERROR`: route input is invalid.

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
        "boardingStatus": "PENDING",
        "bookingCode": "VR-20260630-ABCD1234",
        "buyerName": "Nguyễn Văn A",
        "buyerPhone": "+84888151546"
      }
    ]
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-06-30T10:00:00+07:00"
  }
}
```

With `ticketCode`, the response contains exactly one passenger item. Legacy `bookingCode` may
return multiple issued/used ticket items for the booking. The scan is read-only; ticking a
passenger uses the separate boarding-passenger endpoint. `buyerName` and `buyerPhone` follow the
same Booking-buyer meaning, `BOARDING|IN_PROGRESS` visibility window, null/redaction rules, and
E.164/no-store policy as the manifest endpoint. Because the scan is read-only, it does not require
an `Idempotency-Key` header.

Error responses use the ADR 0004 envelope:

- `403 FORBIDDEN`: caller is not the trip's assigned driver or assistant.
- `404 BOOKING_NOT_FOUND`: the code is unknown, the booking is not `CONFIRMED`, or the ticket is not `ISSUED`/`USED`.
- `422 BOOKING_NOT_FOR_THIS_TRIP`: the code belongs to a different trip.
- `422 VALIDATION_ERROR`: the route parameter or booking-code format is invalid.

## Integration Event Contracts

### Route-change proposal lifecycle facts

Trip produces five lifecycle routing keys on `vietride.events`:
`trip.route_change_proposal.created`, `.approved`, `.rejected`, `.superseded`, and `.expired`.
Notification consumes all five. The created fact resolves all active `OPERATOR_ADMIN` users for
`operatorId` and creates one `ROUTE_CHANGE_PROPOSAL_CREATED` notification per resolved admin.
Approved, rejected, superseded, and expired each notify `proposedByUserId` with the matching type
`ROUTE_CHANGE_PROPOSAL_APPROVED|ROUTE_CHANGE_PROPOSAL_REJECTED|ROUTE_CHANGE_PROPOSAL_SUPERSEDED|ROUTE_CHANGE_PROPOSAL_EXPIRED`.
Consumer processing is idempotent by `eventId`/RabbitMQ `MessageId`; redelivery creates neither a
duplicate Notification row nor duplicate push. Every key uses the same exact payload field set;
nullable fields are serialized as `null`:

```jsonc
{
  "eventId": "uuid",
  "occurredAt": "2026-08-04T02:00:00Z",
  "proposalId": "uuid",
  "tripId": "uuid",
  "operatorId": "uuid",
  "proposedByUserId": "uuid",
  "actorUserId": "uuid|null",
  "proposalType": "EXISTING",
  "status": "PENDING",
  "sourceAlternativeRouteId": "uuid|null",
  "approvedAlternativeRouteId": "uuid|null",
  "incidentId": "uuid|null",
  "reason": "Traffic congestion ahead",
  "rejectionReason": null,
  "resolutionCode": null,
  "supersededByProposalId": null
}
```

`proposalType=EXISTING|CUSTOM`; status is the post-transition state. `created.actorUserId` is the
proposer; `approved|rejected` uses the deciding admin; automatic expiry uses null. Supersede uses
the approving/direct-change admin when available. `eventId` equals the Outbox row id and RabbitMQ
MessageId. Approval also emits the pre-existing `trip.trip.route_changed` fact in the same
Trip-local transaction; the proposal lifecycle fact never duplicates its affected-Booking fields.

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

`trip.trip.disrupted` — producer Trip; consumers Booking, Parcel, and Payment:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-30T03:00:00Z",
  "tripId": "uuid",
  "operatorId": "uuid",
  "terminalAt": "2026-07-30T03:00:00Z",
  "tripCode": "TRIP-20260730-M5Q7WV3D",
  "hasSubstitution": false,
  "reason": "Road closure"
}
```

The event never carries `traveledRatio`. Booking processes only `hasSubstitution=false`, computes
the ratio independently for each eligible Booking from the internal Trip snapshot, transitions it
to `DISRUPTED`, and emits canonical `booking.booking.cancelled` with the frozen refund amount for
Payment. `hasSubstitution=true` is audit/settlement-only and must not trigger a disruption refund.

`booking.booking.disrupted` — producer Booking; consumer Notification only:

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-30T03:00:01Z",
  "bookingId": "uuid",
  "bookingCode": "VR-20260730-ABCDEFGH",
  "tripId": "uuid",
  "operatorId": "uuid",
  "userId": "uuid",
  "traveledRatio": 0.4,
  "refundAmount": 300000,
  "cancellationReason": "OPERATOR_DISRUPTED_IN_PROGRESS"
}
```

Notification uses this fact for the passenger-facing disruption message. Payment does not bind
it. `booking.booking.cancelled` remains the sole Booking refund trigger.

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
  "eventId": "uuid",
  "occurredAt": "2026-07-15T08:05:00Z",
  "parcelId": "uuid",
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
  "operatorId": "uuid",
  "tripId": "uuid",
  "userId": "recipient-user-uuid",
  "recipientUserIds": ["recipient-user-uuid"],
  "expiresAt": "2026-07-17T08:05:00Z"
}
```

The Parcel-local transaction enqueues this event only for the winning
`UNLOADED -> DELIVERED_PENDING_CONFIRM` CAS. `userId` and `recipientUserIds` are omitted when no
recipient account is linked; `expiresAt` is omitted when no recipient email/token exists.
`eventId` equals the Outbox row id and RabbitMQ MessageId. The event never contains a raw token or
URL; email delivery is a direct Internal-JWT HTTP call. A replay or CAS loser emits no event.

### `parcel.parcel.delivery_confirmation_realerted`

Producer: Parcel. Consumer: Notification. Exchange: `vietride.events`.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-24T08:05:00Z",
  "parcelId": "uuid",
  "parcelCode": "VR-PCL-20260518-P7K3D9Q2",
  "operatorId": "uuid",
  "tripId": "uuid",
  "expiredAt": "2026-07-17T08:05:00Z"
}
```

The daily reminder emits this operator-only fact only when the active token has been expired for
at least seven days and the reminder claim wins. It updates `lastReminderAt` but does not rotate a
token or transition Parcel status.

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

Producer: Trip. Consumer: Notification. Exchange: `vietride.events`. Parcel does not consume this
fact; unload eligibility is read synchronously from the Trip snapshot.

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

Producer: Trip. Consumer: Parcel. Exchange: `vietride.events`. Terminal unload reads the dedicated
Trip operational-location endpoint synchronously. Parcel consumes this fact as a delivery-readiness
anchor only: arrival opens the normal terminal unload window and must not quarantine a terminal-bound
parcel that is still `LOADED|IN_TRANSIT`. If `trip.trip.completed` later observes the Parcel still
loaded/in transit, Parcel freezes the resume status and opens the system missing-search workflow.

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

### `trip.stop.departed`

Producer: Trip. Consumer: Parcel. Exchange: `vietride.events`. This operational fact is emitted
for every committed stop departure so missing-parcel detection does not depend on whether Booking
also has pending passengers.

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-06-30T03:00:00Z",
  "eventType": "trip.stop.departed",
  "tripId": "uuid",
  "stopId": "uuid",
  "operatorId": "uuid",
  "departedAt": "2026-06-30T03:00:00Z"
}
```

Parcel uses this fact only as a reconciliation trigger. Custody location remains sourced from
confirmed scans/events; a matching expected drop-off without unload opens
`MISSING_AFTER_DEPARTURE` idempotently.

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
    "city": "Ho Chi Minh City",
    "ward": "Thu Duc",
    "latitude": 10.8796,
    "longitude": 106.8142,
    "supportsShuttle": true,
    "isActive": true
  },
  "duplicateBefore": {
    "id": "uuid",
    "name": "BX Mien Dong",
    "slug": "bx-mien-dong",
    "city": "Ho Chi Minh City",
    "ward": "Thu Duc",
    "latitude": 10.8797,
    "longitude": 106.8141,
    "supportsShuttle": false,
    "isActive": true
  },
  "primaryAfter": {
    "id": "uuid",
    "name": "Ben xe Mien Dong Moi",
    "slug": "ben-xe-mien-dong-moi",
    "city": "Ho Chi Minh City",
    "ward": "Thu Duc",
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
    "city": "Ho Chi Minh City",
    "ward": "Thu Duc",
    "latitude": 10.8797,
    "longitude": 106.8141,
    "supportsShuttle": false,
    "isActive": true
  },
  "after": {
    "id": "uuid",
    "name": "Ben xe Mien Dong Moi",
    "slug": "ben-xe-mien-dong-moi",
    "city": "Ho Chi Minh City",
    "ward": "Thu Duc",
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

Payment revenue/refund workbooks preserve every legacy identifier column and add reconciliation
columns in this exact order: `entry_id`, `reference_code`, `trip_code`, `entry_type`,
`reference_type`, `reference_id`, `trip_id`, `amount_vnd`,
`occurred_at_asia_ho_chi_minh`, `note`. `reference_code` is the persisted Booking/Parcel code.
`trip_code` prefers the Payment settlement snapshot and may be blank when Trip enrichment is
temporarily unavailable; the workbook still succeeds and retains `trip_id`.

All routes require `OPERATOR_ADMIN` or `OPERATOR_STAFF`. `operatorId` is read only from the
authenticated operator claim; query/body values are ignored and are not accepted. `from` and `to`
are optional Asia/Ho_Chi_Minh dates, inclusive. The default is the last 30 Asia/Ho_Chi_Minh calendar days including `to`;
the maximum is 92 inclusive days. The service converts the range to UTC `[from,to)` and rejects
invalid or oversized ranges with `422 REPORT_RANGE_INVALID`.

Success is a raw file response with media type
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition:
attachment`, and a deterministic filename ending in `.xlsx`. Errors use the ADR 0004 envelope.
Empty ranges still produce a valid workbook. No report contains passenger, sender or recipient
PII. Legacy CSV `GET /v1/operator/parcels/reports/export?format=csv` vẫn giữ filename/MIME/counts,
nhưng breaking đổi ba cột tiền thành `grossParcelRevenueVnd`, signed `parcelRefundsVnd` và
`netParcelRevenueVnd`; cả ba lấy từ Payment cho cùng khoảng ngày Asia/Ho_Chi_Minh.

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
list. Revenue XLSX áp dụng đúng canonical signed predicate của Payment; Refund XLSX chỉ là subset
`BOOKING_REFUND|PARCEL_REFUND`, không tự suy ra refund từ note hoặc wallet transaction.

### Platform report stabilization

`GET /v1/admin/reports/platform?from=&to=` vẫn là public route nhận ngày Asia/Ho_Chi_Minh inclusive. Booking
chuẩn hóa thành UTC half-open `[fromUtc,toUtcExclusive)` trước mọi query/internal call. Từ Day 42,
Booking sở hữu facade và chỉ gọi internal raw source; mỗi source đọc database của chính mình.
Booking/Trip/Parcel sở hữu các count vận hành. Payment ledger là nguồn authoritative cho
`netTicketRevenueVnd` và `netParcelRevenueVnd` cuối cùng; paid `NO_SHOW` có thể tạo revenue mà không tạo
completed booking count. Redis read-through cache dùng key
`platform-report:v3:{fromUtc}:{toUtc}`, TTL 60 giây và boundary UTC chính xác.

Facade chỉ reconciliation dữ liệu live/projection trong từng source vận hành trước khi promote
Stats/cache. Source-local mismatch, downstream timeout, source unavailable, payload malformed,
ledger malformed hoặc duplicate đều trả `503 UPSTREAM_UNAVAILABLE`; không trả partial hay stale
totals. Chênh lệch giữa operational amount và Payment ledger không phải lỗi reconciliation vì
Payment ledger là authority cuối cùng. Cache entry phải có contract version và exact range.

Booking, Trip and Parcel each maintain a per-earned-record projection named respectively
`platform_booking_stats`, `platform_trip_stats` and `platform_parcel_stats`. A source-row trigger
updates the projection in the same local transaction, while a five-minute recurring backfill
rebuilds it idempotently from live rows. Mỗi raw internal source request đối chiếu projection với
live operational aggregates của chính source đó cho từng operator trong exact UTC range. Mọi
source-local count hoặc field vận hành mismatch trả `503 UPSTREAM_UNAVAILABLE`; projected timestamp
mới không được dùng để bypass reconciliation.

`GET /internal/v1/reports/platform/ledger?from=&to=` chỉ dành cho Internal JWT và trả raw payload
legacy do Payment sở hữu `{ "items": [{ "operatorId", "bookingRevenueVnd", "parcelRevenueVnd" }] }`;
Booking map hai field nội bộ này sang tên public `netTicketRevenueVnd`/`netParcelRevenueVnd`.
Payment đọc immutable `OperatorLedgerEntry` trong UTC `[from,to)`, checked BIGINT aggregation và
không gọi service khác. Ledger malformed/duplicate hoặc unavailable là `503`; Booking không so
revenue ledger với operational amount để bác bỏ ledger-only revenue trước khi publish/cache report.

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
    "createdAt": "2026-07-22T07:00:00+07:00", "terminalAt": "2026-07-22T07:01:00+07:00"
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

## Backend UI gaps — canonical contract addendum (2026-07-29)

This addendum supersedes the external UI-gap backlog and older endpoint notes where they conflict.
All public successes remain wrapped in ADR 0004; snippets below show only `data`. All mutations
require `Idempotency-Key`. Internal successes are raw DTOs and require Internal JWT.

### Scope exclusions and compatibility

- `GET /v1/admin/operators` is unchanged; no `BE-ADM-02` projection is added.
- Admin Station endpoints and frontend changes are outside this scope.
- Existing single-size Parcel fare endpoints are unchanged. The substitution endpoint follows its
  later seat-preserving preview/confirm contract above; the legacy acknowledgement cannot bypass
  a true capacity shortage.
- Release A introduces nullable `tripCode` and Route `code` additively; fare-history versioning is
  still outside scope.
- Additive objects do not remove, rename or retype existing response fields.

### GET `/v1/operator/trips`

Auth: `OPERATOR_ADMIN` only. The operator is read from the JWT; `operatorId` is not accepted.

Query: `search?`, `status?`, `from?`, `to?`, `page=1`, `pageSize=20` (1..100),
`sortBy=departureAt`, `sortDir=asc|desc`. `from`/`to` are Asia/Ho_Chi_Minh dates and `from <= to`.
`search` matches nullable `tripCode`/`route.code` by prefix, route name case-insensitively and vehicle plate after removing separators. Items add nullable Release-A `tripCode`; nested `route` adds nullable `code`. Trip detail and internal batch summaries expose the same codes additively.
The date range is inclusive theo lịch Asia/Ho_Chi_Minh nhưng mọi filter persistence dùng timestamp UTC
`[fromUtc, toUtcExclusive)`, với hai mốc được chuẩn hóa từ local midnight; không query DB bằng
timestamp mang offset Asia/Ho_Chi_Minh.

Each paged item is:

```json
{
  "tripId": "uuid",
  "tripCode": "TRIP-20260729-M5Q7WV3D",
  "status": "IN_PROGRESS",
  "route": {
    "routeId": "uuid",
    "code": "SG-DL-01",
    "name": "HCM - Đà Lạt",
    "originName": "Hồ Chí Minh",
    "destinationName": "Đà Lạt"
  },
  "vehicle": {
    "vehicleId": "uuid",
    "licensePlate": "51B-12345",
    "status": "MAINTENANCE"
  },
  "driver": { "userId": "uuid", "displayName": "Nguyễn Văn A", "phone": "0900000000" },
  "assistant": null,
  "departureAt": "2026-07-29T08:00:00+07:00",
  "arrivalEstimate": "2026-07-29T15:00:00+07:00",
  "canSubstituteVehicle": true
}
```

`canSubstituteVehicle` uses the existing substitution domain precondition. Cross-tenant trips are
not returned. `OPERATOR_STAFF` receives `403 FORBIDDEN`.

### Booking buyer projection

`GET /v1/operator/bookings` and `GET /v1/operator/bookings/{id}` add the same nullable `buyer`
object without changing passenger filters or fields:

```json
{
  "buyer": {
    "userId": "uuid",
    "displayName": "Nguyễn Văn A",
    "phone": "0900000000",
    "email": "a@example.com",
    "avatarUrl": "https://example.test/avatar.jpg"
  }
}
```

`buyer` is the account that created/paid for the Booking, never the first passenger. New Bookings
persist this snapshot. Nullable legacy rows are filled by an idempotent application backfill and
may use one bounded Identity batch read while incomplete; migrations never call Identity. A
soft-deleted buyer is always redacted to `displayName = "Người dùng đã xóa"` with phone, email and
avatar null. This paragraph supersedes the older no-PII-snapshot note for these buyer fields only.

### Financial management projection

Existing `GET /v1/admin/trip-settlements` items add nullable `operator` and `settledBy` objects.
Existing `GET /v1/admin/platform-wallet/transactions` items add `actorType` (`USER|SYSTEM`) and a
nullable `actor` object `{ userId, displayName, email, role }`. Future authenticated manual writes
persist actor snapshots atomically. Historical actors that cannot be proven remain null; automated
events/jobs use `actorType=SYSTEM`. No wallet balance or settlement-state behavior changes.
`operator` is `{ operatorId, name, logoUrl, contactPhone }`; `settledBy` uses the exact actor shape
`{ userId, displayName, email, role }`.

### Generic Policy — RAG owner

Routes:

| Route | Role | Tenant |
|---|---|---|
| `GET /v1/policies` | any authenticated role | platform, or platform + requested operator |
| `GET /v1/policies/{policyId}` | any authenticated role | published user-facing Policy |
| `GET/POST /v1/admin/policies` | `SYSTEM_ADMIN` | platform (`operatorId=null`) |
| `GET/PATCH/DELETE /v1/admin/policies/{policyId}` | `SYSTEM_ADMIN` | platform |
| `GET/POST /v1/operator/policies` | `OPERATOR_ADMIN` | caller operator |
| `GET/PATCH/DELETE /v1/operator/policies/{policyId}` | `OPERATOR_ADMIN` | caller operator |

Consumer reads only expose active, non-deleted `FOR_USER` Policies. `GET /v1/policies` accepts
optional `operatorId`, `category`, `search`, standard pagination,
`sortBy=updatedAt|createdAt|title|version` and `sortDir=asc|desc`. Without `operatorId`, the list
contains platform Policies only; with `operatorId`, pagination applies to the combined platform and
requested-operator result. The consumer response omits `policyType`, `active`, `createdBy` and all
audit/concurrency fields. A detail that is missing, inactive, deleted or not `FOR_USER` returns
`404 POLICY_NOT_FOUND`.

List query supports `policyType=FOR_OPERATOR|FOR_USER`, `category`, `active`, `search`, standard
pagination, `sortBy=updatedAt|createdAt|title|version` and `sortDir=asc|desc`.

Create body and response use the following canonical field names:

```json
{
  "id": "uuid",
  "operatorId": null,
  "title": "Chính sách hoàn vé",
  "description": "Quy định hoàn vé áp dụng toàn hệ thống",
  "content": "Nội dung Markdown hoặc plain text",
  "policyType": "FOR_OPERATOR",
  "category": "REFUND",
  "version": 1,
  "active": true,
  "createdBy": {
    "userId": "uuid",
    "displayName": "System Admin",
    "email": "admin@vietride.vn"
  },
  "createdAt": "2026-07-29T17:00:00+07:00",
  "updatedAt": "2026-07-29T17:00:00+07:00"
}
```

Create accepts all fields above except server-managed `id`, `operatorId`, `version`, actor and
timestamps. `title`, `description`, `content` and `category` are required after trim. Create starts
at version 1; title/description/content/category changes increment once; activation-only changes do
not. Changing `policyType` is a content edit and increments the version. PATCH body is
`{ version, title?, description?, content?, policyType?, category?, active? }`,
requires at least one changed field and accepts no actor/operator/timestamp field. Delete is
soft-delete and distinct from inactive. PATCH requires the current `version`;
mismatch is `409 POLICY_VERSION_CONFLICT`; missing, deleted or cross-tenant resources use
`404 POLICY_NOT_FOUND`. RAG writes the Policy and immutable `PolicyAuditLog` in one Prisma
transaction. These resources are unrelated to RAG `KnowledgeDocument` and Identity operator
cancellation/luggage/no-show JSON.

Each audit row is `{ id, policyId, action, before, after, actor, occurredAt }`, where `action` is
`CREATE|UPDATE|ACTIVATE|DEACTIVATE|DELETE`, `before/after` are nullable canonical Policy snapshots,
and `actor` is `{ userId, displayName, email, role }`. Actor ID/role come from the verified JWT and
display fields from Identity; no actor field is accepted from the request. A PATCH containing any
content field records one `UPDATE` even when it also toggles `active`; only an active-only PATCH
records `ACTIVATE` or `DEACTIVATE`. Identity actor lookup failure aborts the transaction and returns
`503 UPSTREAM_UNAVAILABLE` without a Policy or audit row.

### GET `/v1/operator/parcel-route-fares`

Auth: `OPERATOR_ADMIN|OPERATOR_STAFF`; tenant comes only from the JWT. Query accepts `routeId?`,
`sizeCategory?`, `page?`, `pageSize?` and `search?`. When `search` is present, Parcel calls the
Internal Trip Route search and applies returned route IDs before count/paging. It does not persist
or denormalize Route/Station names. No route match returns a normal empty page; Trip lookup failure
returns `503 UPSTREAM_UNAVAILABLE`.

The list is grouped and paged by distinct `routeId`; one route occupies one item and `totalItems`
counts routes rather than physical fare rows. Each item is `{routeId,fares}` where `fares` contains
only persisted rows, ordered `SMALL|MEDIUM|LARGE|EXTRA_LARGE`, and each fare is
`{sizeCategory,priceVnd,effectiveFrom,effectiveUntil}`. Route, size, search and effective-window
filters select qualifying routes; after route paging, every persisted fare for each selected route
is returned so clients can render one batch-edit form. Missing categories are omitted rather than
synthesized.

```json
{
  "items": [
    {
      "routeId": "11111111-1111-1111-1111-111111111111",
      "fares": [
        {
          "sizeCategory": "SMALL",
          "priceVnd": 10000,
          "effectiveFrom": "2026-08-27T16:30:00Z",
          "effectiveUntil": null
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
}
```

`sortBy=priceVnd|effectiveFrom` remains route-level: ascending uses the minimum value among fare
rows that satisfy the filters, descending uses the maximum, and `routeId` in the same direction is
the deterministic tie-break. The default remains `effectiveFrom desc`. Clients compare the
returned `(effectiveFrom,effectiveUntil)` pairs: a mixed route must require an explicit common
window and confirmation before calling the batch endpoint, because that write normalizes every
submitted category to the request-level window.

### PUT `/v1/operator/parcel-route-fares/{routeId}/batch`

Auth: `OPERATOR_ADMIN`; route ownership comes from JWT tenant. Body:

```json
{
  "effectiveFrom": "2026-07-31T17:00:00Z",
  "effectiveUntil": null,
  "items": [
    { "sizeCategory": "SMALL", "priceVnd": 50000 },
    { "sizeCategory": "MEDIUM", "priceVnd": 80000 },
    { "sizeCategory": "LARGE", "priceVnd": 120000 }
  ]
}
```

`items` has 1..4 unique current enum values; `priceVnd` is a positive whole VND amount;
`effectiveUntil`, when present, is after `effectiveFrom`. One transaction upserts the current row
identified by the existing physical key `(routeId, sizeCategory)` after verifying that route owner
equals the JWT operator. Any invalid item rolls back the whole batch. Response returns `routeId`
and all requested items with persisted effective values and `created`. No schema migration or fare
history is part of this endpoint.

### Operator Parcel projections

`GET /v1/operator/parcels` additively returns nested `trip`, `route`, `sender` and `recipient`, plus
current size/weight/volume, price/refund and timestamps. It retains existing flat fields for one
compatibility release and does not require `tripCode`. At most one Parcel page query, one Trip batch
and one Identity batch are permitted.

`GET /v1/operator/parcels/{parcelId}` is new and returns that projection plus base/deposit/
additional/discount/refund/forfeiture amounts, voucher, deadlines/pending action, load/unload/
delivery timestamps, dimensions and ordered status history. Both routes are tenant-isolated. Route,
station and vehicle display values use stable nullable Parcel snapshots with bounded Trip fallback
until application backfill completes.

Status history items are `{ status, occurredAt, actorType, actorId, source, reason }`. Existing
Parcels have a single `MIGRATION_BASELINE` item; unavailable historical transitions are not
fabricated.

### Dashboard aggregates

Existing BookingStats routes add `groupBy=month` while retaining existing group values. Month/day
buckets use `Asia/Ho_Chi_Minh`, zero-fill missing buckets and reconcile item totals with summary.
Their `from/to` parameters are inclusive Asia/Ho_Chi_Minh dates and use the UTC half-open conversion described
for Operator Trip search; the maximum requested range is 366 inclusive days.

`GET /v1/operator/parcel-stats?from=&to=&groupBy=status|route&limit=` is `OPERATOR_ADMIN`-only,
tenant-scoped and uses stable route snapshots so inactive routes retain historical names.
Its `from/to` range uses the same inclusive Asia/Ho_Chi_Minh rule.

BookingStats không trả bất kỳ field tiền nào. Month items là
`{ date, totalBookings, totalCancellations }`; operator variant thêm `totalCompleted` và các count
no-show hiện có. `date` là ngày Asia/Ho_Chi_Minh đầu tiên của tháng. Parcel status items là `{ key, count }`
where `key` is a current ParcelStatus; route items are
`{ routeId, routeName, parcelCount }`. Both Parcel shapes return `totalParcels`.

`GET /v1/admin/dashboard/summary?from=&to=` is a Booking-owned `SYSTEM_ADMIN` facade. It combines
BookingStats with Identity internal metrics and does not call Platform Report. `activeUsers` means
the account is currently non-deleted/unlocked and its latest `lastLoginAt` is in the period.
`activeOperators` is the intersection of operators currently non-deleted,
`APPROVED + isActive` and operators with at least one BookingStats booking in the period. The
previous period is the immediately preceding equal-length Asia/Ho_Chi_Minh range. `userDistribution` counts
current non-deleted users by role (locked users remain in this distribution), while
`operatorStatusDistribution` counts current non-deleted operators by registration status.
The range is 1..366 inclusive Asia/Ho_Chi_Minh days; missing, reversed or oversized ranges return
`422 VALIDATION_ERROR`.
Identity timeout, 5xx or malformed metrics fail the whole Dashboard with
`503 UPSTREAM_UNAVAILABLE`; no partial summary/distribution is returned.

Booking lấy toàn bộ năm comparison doanh thu từ Payment internal admin summary; BookingStats chỉ
cung cấp count. The response data shape is:

```json
{
  "period": { "from": "2026-01-01", "to": "2026-12-31", "timezone": "Asia/Ho_Chi_Minh" },
  "totalProjectRevenueVnd": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "netTransportRevenueVnd": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "netTicketRevenueVnd": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "netParcelRevenueVnd": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "subscriptionRevenueVnd": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "activeOperators": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "activeUsers": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "bookings": { "currentValue": 0, "previousValue": 0, "changePercent": 0, "trend": "FLAT" },
  "userDistribution": [{ "role": "PASSENGER", "count": 0 }],
  "operatorStatusDistribution": [{ "status": "APPROVED", "count": 0, "percent": 0 }]
}
```

### Revenue analytics — Payment owner

> **Cảnh báo ngữ nghĩa:** các field revenue dưới đây là KPI quản trị, không phải báo cáo kế toán
> pháp lý hay số tiền mặt hành khách đã trả. `VOUCHER_VIETRIDE_FUNDED_CREDIT` là quyền lợi doanh thu
> VietRide cấp cho nhà xe và được cộng vào revenue dù không phải passenger cash. Không dùng các số
> này để suy ra số dư ví hoặc tiền đã settlement.

Payment là financial source of truth. Canonical whitelist duy nhất:

- `BOOKING_REVENUE`, `BOOKING_REFUND`, `VOUCHER_VIETRIDE_FUNDED_CREDIT` có
  `referenceType=BOOKING`;
- `PARCEL_REVENUE`, `PARCEL_REFUND`, `VOUCHER_VIETRIDE_FUNDED_CREDIT` có
  `referenceType=PARCEL`;
- `ADJUSTMENT` chỉ khi
  `adjustmentReason=VIETRIDE_FUNDED_VOUCHER_REVERSAL` và reference là `BOOKING|PARCEL`.

`note` chỉ phục vụ audit, tuyệt đối không phải predicate tài chính. Loại khỏi revenue:
`VOUCHER_OPERATOR_FUNDED_AUDIT`, `MANUAL_WALLET_ADJUSTMENT`,
`GENERIC_BOOKING_REFUND_ENTITLEMENT`, `LEGACY_UNCLASSIFIED` và mọi type/reference khác.

Taxonomy bắt buộc cho `ADJUSTMENT`:

| `adjustmentReason` | Ràng buộc | Ý nghĩa revenue |
|---|---|---|
| `VIETRIDE_FUNDED_VOUCHER_REVERSAL` | `amount < 0`, reference `BOOKING|PARCEL` | được nhận diện trong category tương ứng |
| `GENERIC_BOOKING_REFUND_ENTITLEMENT` | `amount = 0`, reference `BOOKING` | marker quyền được hoàn chung, không phải dòng tiền/revenue |
| `MANUAL_WALLET_ADJUSTMENT` | `amount != 0`, reference `MANUAL` | điều chỉnh ví thủ công, không phải recognized revenue |
| `LEGACY_UNCLASSIFIED` | chỉ dành cho dữ liệu lịch sử chưa phân loại | không được application tạo mới và không tính revenue |

Mọi `ADJUSTMENT` phải có reason; mọi entry type khác phải để reason null. DB CHECK enforce presence
và amount/reference semantics này.

```text
netTicketRevenueVnd      = canonical BOOKING entries
netParcelRevenueVnd      = canonical PARCEL entries
netTransportRevenueVnd   = netTicketRevenueVnd + netParcelRevenueVnd
subscriptionRevenueVnd   = SUBSCRIPTION payment SUCCEEDED, anchor succeededAt
totalProjectRevenueVnd   = netTransportRevenueVnd + subscriptionRevenueVnd
```

`paidToOperatorsVnd` là settlement cash-flow độc lập (`SETTLED`, anchor `settledAt`), nằm dưới
object `settlement` và không thuộc bất kỳ công thức revenue nào. Subscription v1 không có
refund/proration; nếu nghiệp vụ này thay đổi phải mở contract mới trước khi sửa công thức.

`GET /v1/admin/revenue/analytics?from=&to=&groupBy=month&top=5` is `SYSTEM_ADMIN`-only; `top` is
clamped 1..20. The range is 1..366 inclusive Asia/Ho_Chi_Minh days and `groupBy` is exactly `month`.
Monthly buckets are zero-filled and reconcile with summary. Response data contains:

- `summary.revenue`: comparisons cho `totalProjectRevenueVnd`, `netTransportRevenueVnd`,
  `netTicketRevenueVnd`, `netParcelRevenueVnd`, `subscriptionRevenueVnd`;
- `summary.settlement.paidToOperatorsVnd`: comparison độc lập;
- `monthly[]`: `{ month, revenue: { totalProjectRevenueVnd, netTransportRevenueVnd,
  netTicketRevenueVnd, netParcelRevenueVnd, subscriptionRevenueVnd }, settlement: {
  paidToOperatorsVnd } }`;
- `topOperators[]`: `{ rank, operatorId, operatorName, logoUrl, revenueVnd, vehicleCount }`, xếp
  hạng bằng canonical net transport revenue, không phải payout;
- `generatedAt`: UTC.

`GET /v1/operator/revenue/analytics` is `OPERATOR_ADMIN`-only and gets the tenant from JWT. Exactly
one mode is required:

- month: `?month=YYYY-MM`; trả rolling 12 tháng kết thúc ở tháng chọn, comparison với tháng liền
  trước và có `routePerformance`;
- year: `?year=YYYY&groupBy=month`; trả Jan–Dec, comparison với calendar year trước và omit
  `routePerformance`. Năm trước không có dữ liệu vẫn trả đủ comparison zero, không omit field.

Summary fields là `netRevenueVnd`, `netTicketRevenueVnd`, `netParcelRevenueVnd`,
`averageNetRevenuePerTripVnd`. Monthly items là
`{ month, netRevenueVnd, netTicketRevenueVnd, netParcelRevenueVnd, tripCount }`.
Route items dùng field `netRevenueVnd`. Refund được bucket theo `createdAt` của kỳ phát sinh, vì vậy
một tháng không có sale mới vẫn có thể âm do refund lịch sử; UI không được clamp về 0.

Với mọi comparison: previous=0/current=0 trả `changePercent=0, trend=FLAT`;
previous=0/current>0 trả `changePercent=null, trend=UP`; previous=0/current<0 trả
`changePercent=null, trend=DOWN`; previous khác 0 dùng công thức phần trăm hiện hành. Mọi response
financial có thể trễ tối đa 60 giây do cache Payment. Query lỗi không dùng stale cache.

### Internal revenue summaries — không qua Gateway

Hai endpoint dưới đây yêu cầu Internal JWT, dùng Asia/Ho_Chi_Minh inclusive range, trả raw DTO thành công và
không được đăng ký Gateway:

- `GET /internal/v1/revenue/admin-summary?from=YYYY-MM-DD&to=YYYY-MM-DD` trả
  `totalProjectRevenueVnd`, `netTransportRevenueVnd`, `netTicketRevenueVnd`,
  `netParcelRevenueVnd`, `subscriptionRevenueVnd`, `paidToOperatorsVnd`, `period`, `generatedAt`;
- `GET /internal/v1/revenue/operators/{operatorId}/summary?from=YYYY-MM-DD&to=YYYY-MM-DD` trả
  `netRevenueVnd`, `netTicketRevenueVnd`, `netParcelRevenueVnd`, `grossParcelRevenueVnd`, signed
  `parcelRefundsVnd`, `period`, `operatorId`, `generatedAt`.

Booking Dashboard/Platform Report và Parcel report gọi các endpoint này với timeout tổng 5 giây,
tối đa một retry GET transient, circuit mở sau 5 operation lỗi trong 30 giây, sau đó half-open một
probe. Unavailable, timeout, malformed hoặc circuit-open trả `503 UPSTREAM_UNAVAILABLE`; không
fallback sang BookingStats/ParcelStats money.

### Operator Parcel report summary và legacy CSV

`GET /v1/operator/parcels/reports/summary?from=&to=` giữ counts từ Parcel nhưng money từ Payment.
Response data là `{ operatorId, from, to, totalParcels, totalLoaded, totalDelivered, totalRejected,
totalReturned, grossParcelRevenueVnd, parcelRefundsVnd, netParcelRevenueVnd, source }`.
`parcelRefundsVnd` signed, thường âm; `netParcelRevenueVnd = grossParcelRevenueVnd +
parcelRefundsVnd`. `source` chỉ mô tả nguồn counts (`ParcelStats|ParcelsFallback`), không mô tả nguồn
money; money luôn từ Payment. Hai field cũ `totalRevenue`/`totalRefunded` không còn alias.

`GET /v1/operator/parcels/reports/export?format=csv` dùng đúng cùng summary và header:
`operatorId,from,to,totalParcels,totalLoaded,totalDelivered,totalRejected,totalReturned,
grossParcelRevenueVnd,parcelRefundsVnd,netParcelRevenueVnd,source`. Parcel không cache full response;
độ trễ tài chính tối đa chỉ do Payment cache 60 giây. `GET /v1/operator/parcel-stats` giữ nguyên
count/status/route và không phải financial endpoint.

### Breaking field mapping cho FE/BI

| Surface | Field cũ | Field mới / hành vi |
|---|---|---|
| Admin Dashboard | `totalRevenue` | năm comparison: `totalProjectRevenueVnd`, `netTransportRevenueVnd`, `netTicketRevenueVnd`, `netParcelRevenueVnd`, `subscriptionRevenueVnd` |
| BookingStats Admin/Operator | `totalRevenue` trong item/totals | bỏ hoàn toàn; không có alias |
| Platform Report | `bookingRevenueVnd`, `parcelRevenueVnd`, `netRevenueVnd` | `netTicketRevenueVnd`, `netParcelRevenueVnd`, `netTransportRevenueVnd` |
| Admin Revenue Analytics | `grossRevenueVnd`, `platformRevenueVnd`, top-level `paidToOperatorsVnd` | nested `summary.revenue.*`, `summary.settlement.paidToOperatorsVnd` |
| Operator Revenue Analytics | `totalRevenueVnd`, `ticketRevenueVnd`, `parcelRevenueVnd`, `averageRevenuePerTripVnd` | `netRevenueVnd`, `netTicketRevenueVnd`, `netParcelRevenueVnd`, `averageNetRevenuePerTripVnd` |
| Parcel summary/CSV | `totalRevenue`, `totalRefunded` | `grossParcelRevenueVnd`, signed `parcelRefundsVnd`, `netParcelRevenueVnd` |

### Internal UI-gap projections

All routes below are Internal-JWT-only, return raw success DTOs and are never registered in
Gateway:

| Route | Owner | Request/response rule |
|---|---|---|
| `GET /internal/v1/users?ids=<uuid>&ids=<uuid>` | Identity | Existing 1..100 batch adds phone/email/avatar/status and includes soft-deleted IDs as redacted users |
| `POST /internal/v1/operators/summaries/batch` | Identity | Existing read-only batch adds logo/contact phone; response remains deterministic by operator ID |
| `GET /internal/v1/admin/dashboard/identity-metrics?from=&to=` | Identity | Raw `{ activeUserCount, approvedActiveOperatorIds, userRoleCounts, operatorStatusCounts }`; Asia/Ho_Chi_Minh range and current-state semantics described above |
| `POST /internal/v1/trips/summaries/batch` | Trip | Body `{ tripIds }`, 1..100 distinct IDs; raw trip/route/station/vehicle/crew/timing summaries, missing IDs omitted |
| `POST /internal/v1/operators/vehicle-counts/batch` | Trip | Body `{ operatorIds }`, 1..100 distinct IDs; raw `{ operatorId, vehicleCount }[]` |
| `GET /internal/v1/operators/{operatorId}/route-performance?month=YYYY-MM` | Trip | Raw Asia/Ho_Chi_Minh-month `{ routeId, routeName, originName, destinationName, tripCount, completedTripCount }[]` |
| `GET /internal/v1/revenue/admin-summary?from=&to=` | Payment | Raw canonical project revenue + independent payout summary; Asia/Ho_Chi_Minh inclusive |
| `GET /internal/v1/revenue/operators/{operatorId}/summary?from=&to=` | Payment | Raw canonical operator ticket/parcel summary, gồm gross Parcel và signed refund; Asia/Ho_Chi_Minh inclusive |
| `POST /internal/v1/revenue/backfills/parcel-voucher-reversals?dryRun=true|false` | Payment | Internal maintenance; mặc định dry-run, raw `{ scannedRefundCount, candidateCount, skippedExistingCount, legacyUnclassifiedCount, totalAdjustmentVnd, appliedCount }` |

User batch items are `{ id, displayName, phone, email, avatarUrl, role, operatorId, status,
deleted }`; deleted rows keep only `id`, `role`, `operatorId`, `status`, `deleted=true` and the
redacted display name. Operator batch items preserve the existing field name and are
`{ operatorId, operatorName, logoUrl, contactPhone }`.

Trip summary items are `{ tripId, status, departureAt, arrivalEstimate, route { routeId, name,
originName, destinationName }, vehicle { vehicleId, licensePlate, status, vehicleType { code,
displayName } }, driverUserId, assistantUserId }`. Vehicle type may be system-defined or custom.
Identity metric maps are arrays of `{ role, count }` and `{ status, count }`;
approved operator IDs are distinct and sorted. Vehicle-count and route-performance responses are
the exact array item shapes in the table and are sorted by ID/route name for deterministic callers.

Internal 4xx is never retried. Timeout, transport, 5xx or malformed payload maps at the public
facade to its documented `UPSTREAM_UNAVAILABLE` response. Caller cancellation propagates unchanged.

## 2026-08-05 Route, Station address and Operations extension

This section supersedes older field and endpoint descriptions where they conflict.

### Station address

- Public and internal Station DTOs use `city` plus nullable `ward`; `province` is removed.
- `city` is the province or centrally governed municipality; `ward` is the commune, ward or
  special zone. New Station create/update requests require both values, while migrated legacy
  Stations may return `ward=null` until an administrator completes the address.
- Search is `GET /v1/stations/search?q=&city=&ward=&locationId=&locationScopeCode=`. `locationId`
  keeps exact matching. A two-digit root code includes the active root plus active direct leaves;
  a five-digit leaf code is exact. The two location parameters conflict, and unknown/inactive or
  invalid codes return `422 VALIDATION_ERROR`. `q`, `city`, and `ward` continue narrowing results.

### Route map and composite writes

- `GET /v1/operator/routes` remains the lightweight `RouteListItemDto` projection and does not
  add polyline or stops.
- `GET /v1/operator/routes/{id}` and successful Route create, patch, geometry and composite
  mutations return `RouteDto` with ordered `stops[]`. Each stop contains RouteStop fields plus
  `stopId`, `name`, `address`, `latitude`, `longitude`, and `isActive`.
- `POST /v1/operator/routes/full` and `PUT /v1/operator/routes/{id}/full` require a UUID-v4
  `Idempotency-Key`. They atomically write the Route, optional geometry, and the complete ordered
  RouteStop collection. Full update cannot change origin or destination.
- `POST /v1/operator/routes/{id}/stops`, `DELETE /v1/operator/routes/{id}/stops/{stopId}` and
  `PUT /v1/operator/routes/{id}/full` synchronize the resulting RouteStop collection into existing
  TripStop snapshots only for future `SCHEDULED` Trips on the main Route that have no active
  `PENDING_PAYMENT|CONFIRMED` Booking and no visible `HELD|BOOKED` TripSeat. `BOARDING`,
  `IN_PROGRESS`, terminal and AlternativeRoute Trips keep their prior snapshot. Retained manual
  TripStop fares remain unchanged; fares for removed stops are deleted. Each changed Trip writes a
  `TRIP_STOP_SNAPSHOT_SYNCED` audit with the authenticated actor user ID.
- The Booking impact check is a best-effort cross-service preflight, not a distributed lock. A
  Booking that read the old Trip snapshot but has not yet acquired a seat hold can race with the
  RouteStop commit. Strict prevention requires a future TripStop snapshot version/reservation
  protocol between Booking snapshot reads, seat locking and RouteStop mutations.
- When a precision-5 Google polyline is present, the server derives route distance and duration
  (55 km/h, duration rounded up to a minute) and derives missing stop cumulative metrics by
  projection onto the nearest polyline segment. Client-provided stop metrics take precedence.
  Without a polyline, `manualMetrics` is required for create; clearing geometry without
  `manualMetrics` preserves the current Route metrics.
- `GET /v1/operator/routes/{routeId}/stop-metrics` returns the ordered effective stop metrics.
- Duplicate normalized origin/destination/name combinations return `409 ROUTE_DUPLICATED` with
  the oldest conflicting Route ID. Invalid duplicate stops and order return
  `422 ROUTE_STOP_DUPLICATED` and `422 ROUTE_STOP_ORDER_INVALID`.

### Operations and realtime

- `GET /v1/tracking/operator/fleet-latest?status=&include=shuttle` returns latest GPS items for the
  caller's operator only. `include=shuttle` is optional and is the only accepted `include` value.
  Every item is discriminated by `kind`:

  ```json
  {
    "items": [
      {
        "kind": "TRIP",
        "tripId": "uuid",
        "latitude": 10.51,
        "longitude": 106.12,
        "speedKmh": 47.5,
        "headingDeg": 215,
        "recordedAt": "2026-08-15T03:00:00.000Z",
        "status": "IN_PROGRESS"
      },
      {
        "kind": "SHUTTLE",
        "shuttleTripId": "uuid",
        "mainTripId": "uuid",
        "latitude": 10.76,
        "longitude": 106.66,
        "speedKmh": 24,
        "headingDeg": 120,
        "recordedAt": "2026-08-15T03:00:00.000Z",
        "status": "IN_PROGRESS"
      }
    ],
    "generatedAt": "2026-08-15T03:00:01.000Z"
  }
  ```

  Main Trip behavior is unchanged except for the additive `kind: "TRIP"`. Shuttle items are
  included only when `include=shuttle` and `status` is absent or `IN_PROGRESS`; completed,
  cancelled, scheduled, missing-GPS, malformed, and expired Shuttle values are omitted. Shuttle
  GPS `heading` is exposed as `headingDeg`. A Shuttle item never places its ID in `tripId`.
  Invalid `include` returns `400 VALIDATION_FAILED`; projection or Redis failure returns
  `503 TRACKING_FLEET_UNAVAILABLE`.
- Tracking obtains active tenant-scoped Shuttle IDs through internal-JWT-only
  `GET /internal/v1/operators/{operatorId}/tracking-shuttle-trips`, which returns the raw array
  `{ shuttleTripId, mainTripId, status: "IN_PROGRESS" }[]`. The projection performs no Identity,
  Vehicle, or passenger-profile enrichment.
- ETA accepts legacy `stopId` or an explicit stop/station target. If no target is supplied, it
  returns the first cached target from the status-aware origin → stops → destination chain. Cold
  cache returns `{ eta: null }`; GET never invokes the provider synchronously. `stopName` is
  present only inside a non-null ETA object.
- Operator sockets support `joinOperatorFleet`; only an operator principal can join its own fleet
  room. GPS produces `fleet:gps:update`; proposal events produce `routeProposal:created` and
  `routeProposal:resolved` in that room.
- DriverSchedule keeps the existing PATCH update with
  `applyTo=FUTURE_ONLY|ALL_PENDING`. `PATCH .../{id}/deactivate` is behavior-idempotent and does
  not mutate generated Trips. `DELETE .../{id}` requires UUID-v4 `Idempotency-Key`, soft-deletes
  only a schedule with no generated Trips, returns `200 ApiResponse<{deleted:true}>`, and otherwise
  returns `409 SCHEDULE_HAS_TRIPS` with `tripCount`.
- Operator Trip list items include nullable `sourceScheduleId`; all six existing status filters
  remain supported.

### Route proposal delivery

The five existing proposal integration events remain canonical. CREATED notifies operator admins
and the proposer. APPROVED, REJECTED, SUPERSEDED and EXPIRED notify the current Driver, current
Assistant and proposer after recipient-ID deduplication. Notification persistence precedes FCM
enqueue, and crew lookup failure is retryable.

## 2026-08-07 Mobile gap and operator recovery extension

This section supersedes older endpoint descriptions where they conflict.

- Passenger `GET /v1/trips/search` returns only `SCHEDULED` trips. Booking creation still validates
  the Trip lifecycle as the final write-time guard.
- `POST /v1/bookings/round-trip` requires the return Trip route to equal the outbound Route's
  configured `returnRouteId`; mismatch returns `422 BOOKING_ROUND_TRIP_INVALID` with
  `fields:[{field:"return.tripId",message:"Return trip does not use the configured return route."}]`
  before any seat hold or payment. `409 BOOKING_SEAT_UNAVAILABLE` identifies conflicts using
  `outbound.seatNumbers` and `return.seatNumbers`; one-way booking keeps `seatNumbers`.
- Public Trip route geometry follows the effective Route. An assigned AlternativeRoute supplies
  its polyline and destination while ordered stops always come from the Trip's assigned
  `TripStop` snapshot. Its public ETag changes when the effective Route assignment changes;
  `effectiveRouteId` and `tripStatus` exist on the internal Trip response.
- `GET /v1/tracking/trips/{tripId}/eta` keeps legacy `stopId`. New callers send either
  `targetKind=STOP&stopId=<uuid>` or `targetKind=STATION&stationId=<uuid>`. Invalid kind/id pairs
  return `400 VALIDATION_FAILED`. A non-null response discriminates the target with `targetKind`
  and the matching `stopId` or `stationId`; cold cache remains `{eta:null}`.
- `GET /v1/tracking/trips/{tripId}/etas` is the preferred display read for intercity Trips. It
  returns only cached current targets in route order: origin for pre-departure, then remaining
  stops and destination while in progress. It never triggers a synchronous provider call; cold
  cache is `{etas:[]}`. FE falls back to Trip detail planned timestamps and never computes route
  distance or ETA locally.
- Passenger history items add nullable root `trackingTarget`: `{kind:"STOP",stopId}` for an
  along-route drop-off, `{kind:"STATION",stationId}` for a destination terminal, otherwise null.
- `POST /v1/notifications/read-all` requires User JWT and UUID-v4 `Idempotency-Key`, has an empty
  body and returns `{markedCount,readAt}`. The server persists a per-user/key cutoff in Redis for
  24 hours before atomically marking only unread rows created on or before that cutoff.
- `GET /v1/notifications` orders by `createdAt DESC, id DESC` and adds nullable `nextCursor` while
  retaining existing page fields. Passing `cursor` uses opaque snapshot keyset pagination;
  malformed or filter-mismatched cursors return `400 VALIDATION_FAILED`.
- `POST /v1/admin/operators/{operatorId}/reactivate` requires `SYSTEM_ADMIN`, UUID-v4
  `Idempotency-Key`, and an empty body. It permits only `SUSPENDED -> APPROVED`, sets
  `isActive=true`, preserves subscription/approval/suspension metadata, and does not restore
  revoked refresh tokens. Invalid state returns `422 VALIDATION_ERROR`.
- RAG chat HTTP 429 advertises the canonical error code `RAG_RATE_LIMIT_EXCEEDED`.

## 2026-08-14 Search/filter and summary extension

All date-only ranges in this extension are inclusive Asia/Ho_Chi_Minh business dates. Search is
trimmed, limited to 100 characters, and applied before count/paging. Every touched list rejects
unknown query keys with `422 VALIDATION_ERROR`; sort fields remain explicit allow-lists.

- `GET /v1/operator/vouchers` additionally accepts `type=PERCENT_OFF|FIXED_AMOUNT` and
  `validAt=date`; `sortBy` additionally accepts `usedCount`. A selected `validAt` matches a
  voucher whose validity window overlaps that business date. Each item adds `usedCount`.
- `GET /v1/admin/vouchers/summary` and `GET /v1/operator/vouchers/summary` return
  `{total,active,booking,parcel,expiringIn7Days}`. Admin is platform-only; operator is scoped from
  the JWT. These endpoints accept no query parameters.
- `GET /v1/operator/booking-stats` adds `noShowPassengerCount` to every date/month bucket and to
  the top-level response. Existing `totalNoShows` remains a booking count.
- `GET /v1/operator/parcel-route-fares` additionally accepts `sortBy=priceVnd|effectiveFrom`,
  `sortDir=asc|desc`, `effectiveAt=date`, and `status=ACTIVE|SCHEDULED|EXPIRED`. `effectiveAt`
  alone means ACTIVE for that date. `GET /v1/operator/parcel-route-fares/summary` returns
  `[{routeId,configuredSizeCategories,hasActiveWindow,hasScheduledWindow}]` for the caller tenant.
- `GET /v1/operator/parcels` additionally accepts `search`, `from`, `to`,
  `dateField=createdAt|finalPaymentDeadline`, `sizeCategory`, `routeId`, and
  `sortBy=createdAt|finalPaymentDeadline` plus `sortDir`. Search covers parcel code, recipient
  name/phone, and sender name/phone. More than 1,000 sender matches returns
  `422 SEARCH_TOO_BROAD`; Identity failure returns `503 UPSTREAM_UNAVAILABLE`.
- Trip operator lists add the following parameters: stations `isActive`, `supportsShuttle`,
  `sortBy=name|createdAt|updatedAt`, `sortDir`; stops `isActive`, `routeId`; driver schedules
  `dayOfWeek=1..7`, `departureFrom`, `departureTo`, `effectiveAt`, `assistantUserId`,
  `sortBy=departureTime|effectiveFrom`, `sortDir`; incidents `search`, `reportedByUserId`,
  `sortBy=reportedAt|resolvedAt`, `sortDir`; routes `originStationId`, `destinationStationId`,
  `sortBy=name|totalDistanceKm|estimatedDurationMinutes`, `sortDir`; pending shuttle requests
  `from`, `to`, `mainTripId`, `search`. Shuttle `status` and `unassignedOnly` are not supported
  because this resource is intrinsically the pending/unassigned queue.
- `GET /v1/admin/operators` additionally accepts `isActive`, `from`, `to`, and
  `dateField=createdAt|approvedAt`. `GET /v1/admin/operators/summary` accepts no query and returns
  `{total,pending,approved,suspended,rejected,active}`. `GET /v1/admin/operators/export` accepts
  the list filters/sort except paging and returns UTF-8 BOM RFC-4180 CSV. `GET /v1/admin/users`
  additionally accepts `from` and `to` over `createdAt`.
- `GET /v1/operator/invoices` additionally accepts `search`, matching `invoiceNumber` by
  case-insensitive contains and exact `paymentId` when the value is a UUID.
- `GET /internal/v1/users/search?search=` requires Internal JWT and is not exposed by Gateway.
  Success is raw `{userIds:[uuid...]}` for at most 1,000 non-deleted display-name/phone matches;
  more matches returns ADR-0004 `422 SEARCH_TOO_BROAD` without a partial list.
