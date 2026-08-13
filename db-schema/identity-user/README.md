# Identity & User Service — DB Schema

## Overview

Identity & User Service quản lý **authentication, authorization, user profile, operator profile và SaaS subscription**. Đây là service nền tảng — mọi service khác tham chiếu logical FK đến `User` và `Operator` ở đây qua HTTP REST hoặc snapshot field.

- **Database:** `vietride_identity`
- **Framework:** .NET Core 8 + EF Core 8 Migrations
- **Extensions:** `pgcrypto` (gen_random_uuid)
- **Hangfire schema:** `hangfire.*` trong cùng DB này (jobs: OTP cleanup, FCM token stale cleanup) — Hangfire tự tạo khi app khởi động, KHÔNG cần định nghĩa thủ công trong `schema.sql`.

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `Operator` | Nhà xe vận tải. SaaS tenant cấp cao nhất. | `businessRegistrationNumber` UNIQUE, `taxCode` UNIQUE, `registrationStatus`, `cancellationPolicy` JSONB, `parcelNoShowPolicy` JSONB, `luggagePolicy` JSONB, bank account fields |
| `User` | Account + profile cho mọi role. | `email` UNIQUE, `phone` UNIQUE (E.164), `passwordHash` nullable (Google OAuth), `role`, `status`, `operatorId` nullable |
| `OAuthIdentity` | Map User ↔ Google OAuth identity. | `provider`, `providerSubject` (Google sub) |
| `RefreshToken` | Refresh token rotation, family-based reuse detection. | `tokenHash`, `familyId`, `parentTokenId` self-FK |
| `EmailVerificationToken` | OTP/Token cho REGISTRATION, PASSWORD_RESET, SET_INITIAL_PASSWORD. | `purpose`, `code`, `failedAttempts` (brute-force limit) |
| `UserDevice` | FCM token per device (multi-device). | `fcmToken`, `platform`, `lastActiveAt` |
| `ActivityLog` | Audit log user actions. | `action`, `metadata` JSONB |
| `SubscriptionPlan` | SaaS plan catalog (System Admin định nghĩa). | Resource limits + module flags |
| `OperatorSubscription` | 1-1 với Operator: plan hiện tại, usage counters, billing period. | `status`, `expiresAt`, `previousActivePlanId` (cho revert), counter fields |
| `OutboxEvent` | Outbox giao dịch của Identity. | `eventType`, `payload`, `status`, retry metadata |
| `OutboxDlq` | Các event Outbox đã thất bại terminal để System Admin review. | unique `eventId`, payload, `retryCount`, `terminalAt` |

## Design Decisions

- **`User.email` partial unique** trên `LOWER(email)` với điều kiện `deleted_at IS NULL` — cho phép tái dùng email sau soft delete (compliance + GDPR-style anonymization).
- **`User.phone` partial unique** với `deleted_at IS NULL AND phone IS NOT NULL` — `SYSTEM_ADMIN` được phép có phone NULL; passenger Google OAuth ban đầu có phone NULL cho tới khi complete-profile.
- **`User.operator_id` CHECK constraint** enforce role-operator consistency: DRIVER/ASSISTANT/OPERATOR_STAFF/OPERATOR_ADMIN bắt buộc có `operatorId`; PASSENGER/SYSTEM_ADMIN bắt buộc NULL. Tránh phải validate app-layer thuần.
- **User lock provenance:** `users.lock_source` bắt buộc đúng lúc `status=LOCKED`; `SYSTEM_ADMIN` có precedence, còn `OPERATOR_ADMIN` chỉ quản lý lock của cùng-tenant `DRIVER`/`ASSISTANT` với source `OPERATOR_ADMIN` hoặc `AUTOMATIC_LOGIN_FAILURE`. Dữ liệu lock cũ được backfill `LEGACY_UNKNOWN` và chỉ System Admin mở.
- **Password sessions:** self change revokes active refresh tokens với `PASSWORD_CHANGE`; OTP reset tiếp tục dùng `PASSWORD_RESET`. Cả hai đều giữ access JWT stateless hết hạn tự nhiên.
- **`Operator.businessRegistrationNumber` + `Operator.taxCode` partial unique** trên `deleted_at IS NULL` — chống self-resubmit spam (re-register cùng số đăng ký kinh doanh sau khi REJECTED bị block trừ khi Admin reset).
- **`RefreshToken.parentTokenId` self-FK** dùng `ON DELETE SET NULL` thay vì CASCADE để tránh phá chain audit (đôi khi cần giữ child record cho forensics).
- **`OperatorSubscription.operatorId` UNIQUE** — 1 operator có đúng 1 subscription active tại 1 thời điểm; nâng cấp = update plan trên record này, không tạo record mới (lifecycle qua `status` machine).
- **Default `SubscriptionPlan` "Starter (Free Trial)"** seed với UUID cố định `00000000-0000-0000-0000-000000000001` để deterministic cross-environment. KHÔNG seed Pro/Enterprise plan — System Admin tạo qua Admin Web.
- **Bootstrap SYSTEM_ADMIN** KHÔNG nằm trong `seed.sql`/EF seed migration và KHÔNG dùng placeholder password. Identity Service startup seeder tạo admin đầu tiên từ env vars `SYSTEM_ADMIN_BOOTSTRAP_EMAIL`, `SYSTEM_ADMIN_BOOTSTRAP_PASSWORD`, optional `SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME`, với idempotent check `WHERE NOT EXISTS (SELECT 1 FROM users WHERE role='SYSTEM_ADMIN')`.
- **Soft delete pattern**: `deleted_at TIMESTAMPTZ` is the canonical soft-delete marker for both `users` and `operators` (ADR 0003). `is_active boolean` on `operators` is a SEPARATE activation toggle (temporary pause/resume — not a delete); `users` has no `is_active` and uses the `status` enum (`ACTIVE`/`LOCKED`/`DELETED`) for its activation axis instead.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `uq_users_email` | `LOWER(email)` | partial unique | Email login lookup; case-insensitive |
| `uq_users_phone` | `phone` | partial unique | Phone uniqueness across system |
| `idx_users_operator_id` | `operator_id` | partial B-tree | Operator list users (tenant query) |
| `idx_users_role_status` | `role, status` | B-tree composite | Filter active users by role |
| `uq_operators_business_reg_number` | `business_registration_number` | partial unique | Spec — duplicate detection at register |
| `uq_operators_tax_code` | `tax_code` | partial unique | Spec — duplicate detection at register |
| `idx_operators_registration_status` | `registration_status` | B-tree | Admin queue "pending operators" |
| `uq_oauth_identities_provider_subject` | `(provider, provider_subject)` | unique | Google login lookup |
| `uq_oauth_identities_user_provider` | `(user_id, provider)` | unique | 1 Google identity per user |
| `uq_refresh_tokens_token_hash` | `token_hash` | unique | Refresh lookup |
| `idx_refresh_tokens_family_id` | `family_id` | B-tree | Reuse-detection family revoke |
| `idx_refresh_tokens_expires_at` | `expires_at` | partial B-tree (`revoked_at IS NULL`) | Cleanup expired tokens |
| `uq_email_verification_tokens_code_purpose` | `(code, purpose)` | unique | Token redeem lookup |
| `idx_email_verification_tokens_user_purpose` | `(user_id, purpose)` | partial | Rate-limit checks |
| `uq_user_devices_user_fcm_token` | `(user_id, fcm_token)` | unique | Upsert FCM token |
| `idx_user_devices_fcm_token` | `fcm_token` | partial | FCM cleanup on UNREGISTERED |
| `idx_user_devices_last_active_at` | `last_active_at` | partial | Weekly stale cleanup |
| `idx_activity_logs_user_id_created_at` | `(user_id, created_at DESC)` | B-tree | "Show last N actions" Admin view |
| `idx_subscription_plans_is_active` | `is_active` | B-tree | List plans for upgrade UI |
| `idx_operator_subscriptions_status` | `status` | B-tree | Hangfire jobs (EXPIRED, PENDING_PAYMENT scans) |
| `idx_operator_subscriptions_expires_at` | `expires_at` | partial (`status='ACTIVE'`) | Trial expire daily job |
| `idx_outbox_events_status_created` | `(status, created_at)` partial | B-tree | Outbox worker poll |
| `uq_outbox_dlq_event_id` | `event_id` | unique | One terminal row per event |
| `idx_outbox_dlq_terminal_event_id` | `(terminal_at, event_id)` | B-tree | Composite cursor review theo contract |

## Cross-service References (Logical FK)

Identity & User là **target** của nhiều logical FK từ service khác. KHÔNG có cross-service FK xuất phát từ DB này (chỉ Operator/User UUID được service khác lưu).

| Source service | Source column | Target entity here | Enforcement |
|---|---|---|---|
| Booking | `Booking.passengerUserId` | `User.id` | app-layer validate via HTTP `GET /internal/v1/users/{id}` |
| Booking | `Booking.operatorId` | `Operator.id` | app-layer validate |
| Booking | `Voucher.createdByUserId` | `User.id` (SYSTEM_ADMIN) | app-layer validate |
| Booking | `OperatorVoucherConsent.operatorId/respondedByUserId` | `Operator.id` / `User.id` | app-layer validate |
| Trip | `Trip.operatorId/driverUserId/assistantUserId/cancelledByUserId/completedByUserId` | `Operator/User` | app-layer validate |
| Trip | `Stop.operatorId`, `Route.operatorId`, `Vehicle.operatorId`, etc. | `Operator.id` | app-layer validate |
| Trip | `DriverSchedule.driverUserId/assistantUserId` | `User.id` (role DRIVER/ASSISTANT) | app-layer validate |
| Payment | `Payment.userId`, `Wallet.userId`, `TopUpRequest.userId` | `User.id` | app-layer validate |
| Payment | `Payment.operatorId`, `Invoice.operatorId`, `OperatorWallet.operatorId`, `OperatorWalletTransaction.operatorId`, `OperatorTripSettlement.operatorId`, `OperatorLedgerEntry.operatorId` | `Operator.id` | app-layer validate |
| Parcel | `Parcel.senderUserId/recipientUserId/reviewedByUserId/...` | `User.id` | app-layer validate |
| Parcel | `Parcel.operatorId` | `Operator.id` | app-layer validate |
| Tracking | `GpsTrail.tripId` (transitively → operator) | none direct | implicit via trip |
| Notification | `Notification.userId` | `User.id` | app-layer validate |
| RAG AI | `KnowledgeDocument.uploadedByUserId/approvedByUserId`, `RagConversation.userId` | `User.id` | app-layer validate |

Xem `_global/cross-service-references.md` cho danh sách đầy đủ.

## Migration Strategy

- **Tool:** EF Core Migrations (`dotnet ef migrations add <Name>`). Migration history bảng mặc định `__EFMigrationsHistory` của EF Core.
- **Bootstrap order:** Identity Service migrate **trước** mọi service khác. Default `SubscriptionPlan` chạy qua `seed.sql`/EF seed data; bootstrap `SYSTEM_ADMIN` chạy ở Identity Service startup seeder sau migrate, từ `SYSTEM_ADMIN_BOOTSTRAP_*` env vars.
- **Breaking change policy:** ENUM `user_role`, `operator_registration_status`, etc. mở rộng bằng `ALTER TYPE ... ADD VALUE`. KHÔNG rename/remove enum value trong 1 release (cần migration 2-phase nếu cần).
- **Soft delete data retention:** Anonymize PII (email, phone, name) sau 90 ngày `deleted_at` (cron job — không thuộc DB schema layer).

## Open Questions

Không có. Section 5 + Section 8 trong v6 đã đầy đủ spec cho mọi entity ở service này.
