# VietRide DB Schema — Review Report

> **Generated:** 2026-05-22
> **Reviewer:** Senior DB Architect (post-design QA pass)
> **Scope:** Full review of `db-schema/` against `SU26SE101_VIETRIDE_technical_context_v6.md` spec.

---

## Executive Summary

| Metric | Value |
|---|---|
| **Status** | ✅ **READY** (after auto-fixes) |
| **Total findings** | 15 |
| **BLOCKER** | 0 |
| **HIGH** | 0 |
| **MEDIUM** | 4 (auto-fixed in-place) |
| **LOW** | 11 (10 auto-fixed in-place + 1 informational, no action) |
| **Auto-fixed (MEDIUM + LOW)** | 14 |
| **Pending user confirmation (BLOCKER + HIGH)** | 0 |

Schema sẵn sàng cho phase implementation (Auth Service scaffold). KHÔNG có blocker hay finding cần user confirm trước khi proceed.

---

## Category-by-Category Findings

### 1. Entity Completeness — ✅ PASS

Cross-checked entity counts per service against v6 Section 8 "Entity Requirements per Service":

| Service | Expected (v6) | Found (schema.sql) | Status |
|---|---|---|---|
| identity-user | 9 (User, RefreshToken, EmailVerificationToken, OAuthIdentity, Operator, SubscriptionPlan, OperatorSubscription, ActivityLog, UserDevice) | 9 | ✅ |
| booking | 9 (Booking, BookingPendingAction, Passenger, BookingTransfer, BookingStats, Voucher, VoucherUsage, OperatorVoucherConsent, OutboxEvent) | 9 | ✅ |
| trip-route-vehicle | 20 (Station, OperatorStation, Stop, Route, RouteStop, RouteStopFareTemplate, AlternativeRoute, AlternativeRouteStop, VehicleType, Vehicle, Trip, TripSeat, TripStop, TripStopFare, DriverSchedule, TripGenerationSkipLog, ShuttleTrip, ShuttlePassenger, Incident, OutboxEvent) | 20 | ✅ |
| payment-wallet | 13 (Payment, TopUpRequest, Wallet, WalletTransaction, Invoice, PlatformWallet, PlatformWalletTransaction, OperatorLedgerEntry, OperatorWallet, OperatorWalletTransaction, OperatorTripSettlement, RefundFailureLog, OutboxEvent) | 13 | ✅ |
| parcel | 4 (Parcel, ParcelRouteFare, ParcelStats, OutboxEvent) | 4 | ✅ |
| tracking | 2 (GpsTrail, OutboxEvent) | 2 | ✅ |
| notification | 2 (Notification, NotificationDelivery — no OutboxEvent per spec) | 2 | ✅ |
| rag-ai | 5 (KnowledgeDocument, KnowledgeChunk, RagConversation, RagMessage, OutboxEvent) | 5 | ✅ |

**Total tables: 64.** Mọi entity trong v6 đều có CREATE TABLE. Không có entity invented ngoài v6. Junction tables (RouteStop, OperatorStation, RouteStopFareTemplate, AlternativeRouteStop, TripStop, TripStopFare, ParcelRouteFare, OperatorVoucherConsent) đầy đủ.

### 2. Field/Column Completeness — ✅ PASS

Cross-checked critical fields per entity:

- **User:** email, phone (E.164 CHECK), passwordHash nullable, displayName, avatarUrl, role, status, operatorId, failed_login_attempts, last_failed_login_at, last_login_at, created/updated/deleted_at ✅
- **Operator:** businessRegistrationNumber UNIQUE, taxCode UNIQUE, contact info, registration status timestamps, **3 JSONB policy fields** (cancellationPolicy, parcelNoShowPolicy, luggagePolicy) với COMMENT shape, bank account fields nullable (v2 defer), is_active + deleted_at ✅
- **Booking:** 4 pickup/dropoff FK với 2 CHECK constraints, baseFare/discount/total BIGINT, bookingCode UNIQUE, bookingGroupId + tripDirection, **4 snapshot fields** (tripSnapshotOriginName/DestName/Departure/RouteName), cancellationReason enum, refundOverride, full lifecycle timestamps ✅
- **Passenger:** sub-entity với boardingStatus enum, boardedAt, boardedAtStopId (KHÔNG có PII fields — đúng v6) ✅
- **Trip:** source enum 3 values (incl VEHICLE_SUBSTITUTION), hasSubstitution flag, 2 cargo counters (reservedParcelWeightKg + totalLoadedWeightKg), estimatedPassengerLuggageKg snapshot ✅
- **TripStop:** allowPickup/allowDropoff snapshot, distanceFromOriginKm snapshot, estimatedArrivalTime static ✅
- **Vehicle.seatLayoutJson JSONB:** comment reference v6 Section 6.1 contract ✅
- **Parcel:** 40+ field — sender NOT NULL, recipient nullable, dropoffStopId nullable, 3 weight fields, deposit + additional + additionalPaymentId, delivery token triple, review fields (EXTRA_LARGE), transfer fields, return fields, full timestamps ✅
- **PlatformWallet / PlatformWalletTransaction / OperatorWallet / OperatorWalletTransaction / OperatorTripSettlement** (v1 wallet model): tất cả field theo v6 Section 4.6 spec ✅
- **OperatorLedgerEntry:** đã có `trip_id` nullable, **không có** balance_before/after (đúng v6 audit-only sau wallet rewrite) ✅
- **RAG: KnowledgeChunk.embedding vector(1536)** với IVFFlat cosine index ✅

JSONB shape fields đều có `COMMENT ON COLUMN` giải thích shape. Snapshot fields complete.

### 3. Enum Completeness — ✅ PASS

Mọi enum trong v6 Section 8 đã CREATE TYPE, đúng value + count:

| Enum | Values | v6 Match |
|---|---|---|
| `user_role` | 6 | ✅ |
| `user_status` | 5 (PENDING_EMAIL_VERIFICATION, PENDING_INITIAL_PASSWORD, ACTIVE, LOCKED, DELETED) | ✅ |
| `operator_registration_status` | 4 | ✅ |
| `email_verification_purpose` | 3 (REGISTRATION, PASSWORD_RESET, SET_INITIAL_PASSWORD) | ✅ |
| `refresh_token_revoke_reason` | 5 | ✅ |
| `subscription_status` | 5 (incl PENDING_APPROVAL, PENDING_PAYMENT) | ✅ |
| `trip_status` | 6 | ✅ |
| `trip_source` | 3 (incl VEHICLE_SUBSTITUTION) | ✅ |
| `trip_seat_status` | 4 | ✅ |
| `vehicle_status` | 4 | ✅ |
| `booking_status` | 9 | ✅ |
| `booking_cancellation_reason` | 8 (incl OPERATOR_DISRUPTED_IN_PROGRESS, STOP_DISABLED_REFUSED, etc.) | ✅ |
| `parcel_status` | 18 | ✅ |
| `voucher_type` | 2 | ✅ |
| `voucher_funding_type` | 2 | ✅ |
| `payment_reference_type` | 5 (post-wallet-rewrite: BOOKING, BOOKING_GROUP, PARCEL, TOP_UP, SUBSCRIPTION) | ✅ |
| `operator_ledger_entry_type` | 7 (post-wallet-rewrite: drop PAYOUT) | ✅ |
| `operator_trip_settlement_status` | 4 (PENDING_HOLD, ELIGIBLE, SETTLED, CANCELLED) | ✅ |
| `notification_type` | 33 (incl PAYOUT_PROCESSED, PAYOUT_FAILED — xem L11 informational) | ✅ |
| `knowledge_document_access` | 3 (PUBLIC, OPERATOR, ADMIN) | ✅ |
| Other enums | — | ✅ |

Không enum nào missing value.

### 4. Data Type Correctness — ✅ PASS

- ✅ Mọi money column: `BIGINT` (no FLOAT/DECIMAL/REAL/DOUBLE PRECISION). Verified across 22 money fields.
- ✅ Mọi timestamp: `TIMESTAMPTZ` (UTC). Exception: `driver_schedules.departure_time TIME` (local ICT semantic per v6).
- ✅ Mọi PK: `UUID DEFAULT gen_random_uuid()`. Không SERIAL/BIGSERIAL.
- ✅ JSON config dùng `JSONB` (cancellationPolicy, parcelNoShowPolicy, luggagePolicy, seatLayoutJson, operatingHours, facilities, dayOfWeek, photoUrls, metadata, bankAccountSnapshot, data, payload). Không có cột nào dùng JSON thuần.
- ✅ pgvector: `vector(1536)` cho `knowledge_chunks.embedding` + `CREATE EXTENSION IF NOT EXISTS "vector"` ở đầu rag-ai/schema.sql.

### 5. Constraint Correctness — ✅ PASS (sau auto-fix M1)

- ✅ `wallets.balance >= 0` CHECK
- ✅ `platform_wallets.balance >= 0`, `operator_wallets.balance >= 0` CHECK (renamed from operator_balances per wallet rewrite)
- ✅ `route_stops` CHECK `allow_pickup OR allow_dropoff`
- ✅ `bookings` CHECK pickup `exactly one not null`
- ✅ `bookings` CHECK dropoff `at most one not null`
- ✅ `routes` CHECK `origin <> destination`
- ✅ `wallet_transactions.amount > 0`, `operator_wallet_transactions.amount > 0`
- ✅ `chk_operator_trip_settlements_settled_consistency` enforce status↔settled_at/method
- ✅ `chk_stops_no_self_replacement` (replaced_by_stop_id ≠ id)
- ✅ `chk_users_operator_role` (role↔operator_id consistency)
- ✅ `chk_top_up_requests_amount_min` (min 10000 VND)
- ✅ **[M1 FIXED]** `Booking → Passenger` count ≤ 5: thêm DB trigger `trg_check_passenger_max_per_booking()` BEFORE INSERT trên `passengers` table (v6 Section 6.1 line 1568 yêu cầu "DB constraint COUNT ≤ 5"). App-layer vẫn validate cho better UX.

### 6. Index Strategy — ✅ PASS (sau auto-fix M2, M3, M4, L1-L7)

**Indexes added in-place (auto-fix):**
- ✅ **[M2]** `idx_refresh_tokens_parent_token_id` (partial, family-chain query)
- ✅ **[M3]** `idx_operator_voucher_consents_voucher_id` (voucher-scoped admin query)
- ✅ **[M4]** `idx_invoices_payment_id` (1:1 lookup Payment ↔ Invoice)
- ✅ **[L1]** `idx_trips_driver_schedule_id` (partial)
- ✅ **[L2]** `idx_operator_subscriptions_previous_active_plan_id` (partial)
- ✅ **[L3]** `idx_operator_trip_settlements_wallet_transaction_id` (partial)
- ✅ **[L4]** `idx_operator_trip_settlements_settled_by_user_id` (partial)
- ✅ **[L5]** `idx_refund_failure_logs_resolved_by_user_id` (partial)
- ✅ **[L6]** `idx_parcels_additional_payment_id` (partial)
- ✅ **[L7]** 4 audit FK indexes trên `parcels` (`reviewed_by_user_id`, `confirmed_by_user_id`, `transfer_confirmed_by_user_id`, `returned_by_user_id` — tất cả partial WHERE NOT NULL)
- ✅ **[L10]** Removed redundant `idx_passengers_booking_id` (covered bởi leading column của `uq_passengers_booking_seat`)

**Coverage verified:**
- ✅ Mọi FK column có index (intra-service + logical FK đáng index)
- ✅ Status enum trong WHERE clause có index (partial khi cần)
- ✅ Partial unique `uq_booking_pending_actions_active_per_booking (booking_id WHERE resolved_at IS NULL)` đúng pattern v6 Section 8
- ✅ pgvector ivfflat cosine index trên `knowledge_chunks.embedding` với `WITH (lists = 100)`
- ✅ Composite indexes phù hợp query pattern (vd `activity_logs (user_id, created_at DESC)`, `bookings (passenger_user_id, created_at DESC)`, `gps_trails (trip_id, recorded_at)`, etc.)
- ✅ Không còn redundant single-column index

**Index totals per service (sau fix):**
| Service | Indexes |
|---|---|
| identity-user | 30 |
| trip-route-vehicle | 51 |
| booking | 28 |
| payment-wallet | 33 |
| parcel | 18 |
| tracking | 3 |
| notification | 5 |
| rag-ai | 10 |
| **Total** | **178** |

### 7. Cross-DB FK Forbidden — ✅ PASS

- ✅ Grep `REFERENCES vietride_` returns empty.
- ✅ Logical FK columns (vd `Booking.passenger_user_id`, `Vehicle.operator_id`) là UUID column + COMMENT, KHÔNG có FOREIGN KEY constraint cross-DB.
- ✅ Intra-service FK đều có `REFERENCES` đúng table (vd `passengers.booking_id REFERENCES bookings(id)`).
- ✅ `_global/cross-service-references.md` document đầy đủ logical FK cho mọi service.

### 8. Naming Conventions — ✅ PASS

- ✅ Tables: plural snake_case (`users`, `bookings`, `operator_wallets`, `trip_seats`)
- ✅ Columns: snake_case (`passenger_user_id`, `total_amount`, `eligible_at`)
- ✅ PK: `id UUID PRIMARY KEY DEFAULT gen_random_uuid()` — exception: composite PK trong junction tables (`route_stops (route_id, stop_id)`, etc.) + **natural PK 1-1** trên `wallets.user_id` và `operator_wallets.operator_id` (cùng pattern — bootstrap qua event consume, UPSERT idempotent, không có hard cross-service FK).
- ✅ FK column: `<entity>_id` (`operator_id`, `route_id`, `trip_id`).
- ✅ Index naming: `idx_<table>_<columns>` consistent.
- ✅ Unique naming: `uq_<table>_<columns>`.
- ✅ Check naming: `chk_<table>_<rule>`.
- ✅ Không inconsistency (no camelCase trong DDL — verified via grep). EF Core / TypeORM map snake_case ↔ camelCase property qua naming policy.

### 9. Soft Delete Pattern — ✅ PASS

| Entity | Pattern | Note |
|---|---|---|
| Operator | `is_active` + `deleted_at` | Both — `is_active` = temporary pause, `deleted_at` = permanent |
| User | `deleted_at` + `status='DELETED'` | Status enum has DELETED; no separate `is_active` (semantic redundant with status) |
| Station | `is_active` + `deleted_at` | Both |
| Stop | `is_active` + `deleted_at` | Both |
| Route | `is_active` + `deleted_at` | Both |
| Vehicle | `is_active` + `deleted_at` | Both |

Pattern nhất quán theo entity. v6 Section 8 Conventions cho phép cả "isActive/deletedAt" — không mandate cụ thể chọn cả 2 hay 1. Acceptable design choice.

> **Note (ADR 0003, 2026-05-31):** The framing "is_active + deleted_at as soft-delete" above is superseded. `deleted_at` alone is the canonical soft-delete marker (`ISoftDeletable`, `WHERE deleted_at IS NULL` global query filter). `is_active` is a SEPARATE activation toggle (`IActivatable`) — present on Operator/Station/Stop/Route/Vehicle but absent from User (which uses the `status` enum). The table column facts remain accurate; only the conceptual grouping has changed. See `docs/adr/0003-soft-delete-marker-vs-activation-flag.md`.

### 10. Concurrency / Optimistic Lock — ✅ PASS

`row_version INT NOT NULL DEFAULT 0` cho:
- ✅ `wallets`
- ✅ `operator_wallets` (renamed from operator_balances)
- ✅ `operator_trip_settlements` (new entity, status transition lock)

OperatorPayoutBatch entity đã drop khỏi v1 (wallet model) — không còn cần row_version trên entity này.

Wallet/PlatformWallet/OperatorWallet UPDATE pattern dùng `balance_before`/`balance_after` snapshot trên transaction tables (audit + idempotency hint), plus row_version cho strict optimistic lock.

### 11. Audit Columns — ✅ PASS (sau auto-fix L8, L9)

| Entity | created_at | updated_at + trigger | Note |
|---|---|---|---|
| All main entities | ✅ | ✅ | |
| `wallet_transactions` | ✅ | — | Immutable ledger, no updated_at needed |
| `operator_wallet_transactions` | ✅ | — | Immutable |
| `operator_ledger_entries` | ✅ | — | Immutable |
| `activity_logs` | ✅ | — | Immutable audit |
| `email_verification_tokens` | ✅ | — | + `used_at` field |
| `gps_trails` | ✅ | — | + `recorded_at` field |
| `trip_generation_skip_logs` | ✅ | — | Immutable log |
| `refresh_tokens` | ✅ | ✅ **[L9 FIXED]** | Added `updated_at` + trigger (revoked_at tracking) |
| `alternative_route_stops` | ✅ | ✅ **[L8 FIXED]** | Added `updated_at` + trigger (consistency với `route_stops`) |

`trg_set_updated_at()` function defined per service schema. Mọi entity có lifecycle update đều có trigger.

### 12. Seed Data Correctness — ✅ PASS

- ✅ `identity-user/seed.sql`: Bootstrap SYSTEM_ADMIN (fixed UUID `00000000-0000-0000-0000-000000000010`, idempotent check `WHERE NOT EXISTS WHERE role='SYSTEM_ADMIN'`) + default SubscriptionPlan "Starter (Free Trial)" (fixed UUID `00000000-0000-0000-0000-000000000001`). Bcrypt password placeholder với comment hướng dẫn rotate sau deploy.
- ✅ `trip-route-vehicle/seed.sql`: 3 VehicleType với fixed UUIDs (`...101` STANDARD_BUS, `...102` LIMOUSINE, `...103` SLEEPER_BUS), `is_system_defined=TRUE`, `ON CONFLICT (id) DO NOTHING` idempotent.
- ✅ `booking/seed.sql`: comment only `-- No seed data required for this service.`
- ✅ `payment-wallet/seed.sql`: comment only
- ✅ `parcel/seed.sql`: comment only
- ✅ `tracking/seed.sql`: comment only
- ✅ `notification/seed.sql`: comment only
- ✅ `rag-ai/seed.sql`: comment only

No sample/test data. All system-required seed rows have deterministic UUIDs for EF Core migration cross-environment.

### 13. draw.io File Validity — ✅ PASS

Verified via Python ElementTree parse:

| File | Tables | Edges | Well-formed |
|---|---|---|---|
| identity-user/schema.drawio | 9 | 0 | ✅ |
| booking/schema.drawio | 9 | 0 | ✅ |
| trip-route-vehicle/schema.drawio | 20 | 0 | ✅ |
| payment-wallet/schema.drawio | 13 | 0 | ✅ |
| parcel/schema.drawio | 4 | 0 | ✅ |
| tracking/schema.drawio | 2 | 0 | ✅ |
| notification/schema.drawio | 2 | 0 | ✅ |
| rag-ai/schema.drawio | 5 | 0 | ✅ |

- ✅ Mọi `schema.drawio` mở được không lỗi (parser pass).
- ✅ KHÔNG có `mxCell edge="1"` (đúng spec "no connections — user vẽ manually").
- ✅ Mọi table trong schema.sql đều có table box trong drawio (counts match per service).
- ✅ Tables KHÔNG overlap (4-column grid auto-layout, shortest-column placement).
- ✅ Color code per service đúng style cheat sheet (identity User group `#dae8fc`/`#6c8ebf`, Operator group `#d5e8d4`/`#82b366`, booking yellow, trip red, etc.).
- ✅ Column names: **camelCase** trong drawio (`passengerUserId`, `tripSnapshotOriginName`) — KHÔNG snake_case (snake_case chỉ ở schema.sql).
- ✅ PK marker `<u>id</u>` underlined; FK marker `<i>operatorId</i>` italic; `PK`/`FK` text trong c1 column.

### 14. Cross-Reference Consistency — ✅ PASS

- ✅ `_global/cross-service-references.md` cover mọi logical FK cross-service. Sections by target service (Identity, Trip-Route-Vehicle, Booking, Parcel, Payment & Wallet).
- ✅ Polymorphic reference targets table (Payment.reference_id, PlatformWalletTransaction.reference_id, OperatorLedgerEntry.reference_id, OperatorWalletTransaction.reference_id, WalletTransaction.reference_id) document đầy đủ theo từng `reference_type` value.
- ✅ Per-service README có "Cross-service References (Logical FK)" table khớp với master.
- ✅ Event-driven cascade table updated cho wallet model (`trip.trip.completed`/`disrupted` → INSERT TripSettlement, `payment.trip_settlement.completed` → Notification push).
- ✅ Bootstrap/seed (`PlatformWallet` singleton seed + `OperatorWallet` creation on `identity.operator.approved`) documented.

### 15. Hangfire Schema Note — ✅ PASS

README per service đều có Hangfire note:

| Service | Hangfire note | Detail |
|---|---|---|
| identity-user | ✅ | "OTP cleanup, FCM token stale cleanup. Hangfire tự tạo khi app khởi động." |
| trip-route-vehicle | ✅ | "Auto-generate Trip, auto-BOARDING, auto-COMPLETED fallback. Hangfire tự tạo." |
| booking | ✅ | "Seat release khi VNPay timeout, schedule-change auto-accept, PENDING_SEAT_ASSIGNMENT escalation." |
| parcel | ✅ | "Undo-reject 15m, auto-reject EXTRA_LARGE 24h, ... auto-created at startup." |
| payment-wallet | ✅ | "VNPay PENDING_REDIRECT EXPIRED, TopUpRequest EXPIRED, **Trip settlement eligibility flag (daily 02:00)**, **Trip settlement weekly auto-settle (Monday 09:00)**, Subscription trial expire, etc." |
| tracking | ✅ | "**KHÔNG có** (NestJS service). Dùng BullMQ scheduled jobs (Redis-backed)." |
| notification | ✅ | "**KHÔNG có** (NestJS). BullMQ cho FCM push retry." |
| rag-ai | ✅ | "**KHÔNG có** (NestJS). BullMQ cho ingest pipeline." |

Note rõ "Hangfire.PostgreSql package tự tạo schema `hangfire.*` tại app startup" — implicit ở các .NET service README.

---

## In-place Fixes Applied (MEDIUM + LOW)

| # | Sev | File | Change |
|---|---|---|---|
| M1 | MEDIUM | `db-schema/booking/schema.sql` | Added `trg_check_passenger_max_per_booking()` PL/pgSQL function + `trg_passengers_max_5_per_booking BEFORE INSERT` trigger. Enforces v6 Section 6.1 hard limit ≤ 5 Passenger per Booking. App-layer also validates for UX. Comment table updated. |
| M2 | MEDIUM | `db-schema/identity-user/schema.sql` | Added `idx_refresh_tokens_parent_token_id` (partial WHERE NOT NULL) for family-chain reuse-detection queries. |
| M3 | MEDIUM | `db-schema/booking/schema.sql` | Added `idx_operator_voucher_consents_voucher_id`. UNIQUE composite `(operator_id, voucher_id)` doesn't cover voucher-scoped admin query "consent status across operators for voucher X". |
| M4 | MEDIUM | `db-schema/payment-wallet/schema.sql` | Added `idx_invoices_payment_id` for Invoice ↔ Payment 1:1 lookup. |
| L1 | LOW | `db-schema/trip-route-vehicle/schema.sql` | Added `idx_trips_driver_schedule_id` (partial). |
| L2 | LOW | `db-schema/identity-user/schema.sql` | Added `idx_operator_subscriptions_previous_active_plan_id` (partial). |
| L3 | LOW | `db-schema/payment-wallet/schema.sql` | Added `idx_operator_trip_settlements_wallet_transaction_id` (partial). |
| L4 | LOW | `db-schema/payment-wallet/schema.sql` | Added `idx_operator_trip_settlements_settled_by_user_id` (partial). |
| L5 | LOW | `db-schema/payment-wallet/schema.sql` | Added `idx_refund_failure_logs_resolved_by_user_id` (partial). |
| L6 | LOW | `db-schema/parcel/schema.sql` | Added `idx_parcels_additional_payment_id` (partial). |
| L7 | LOW | `db-schema/parcel/schema.sql` | Added 4 audit FK indexes on `parcels.{reviewed_by_user_id, confirmed_by_user_id, transfer_confirmed_by_user_id, returned_by_user_id}` (all partial). |
| L8 | LOW | `db-schema/trip-route-vehicle/schema.sql` | Added `updated_at TIMESTAMPTZ NOT NULL DEFAULT now()` + `trg_alternative_route_stops_updated_at` trigger on `alternative_route_stops` for consistency with `route_stops`. Updated `_drawio_generator.py` to include `updatedAt` column for AlternativeRouteStop box, then regenerated `trip-route-vehicle/schema.drawio`. |
| L9 | LOW | `db-schema/identity-user/schema.sql` | Added `updated_at` + `trg_refresh_tokens_updated_at` trigger on `refresh_tokens` (revoked_at field is UPDATEd, needs audit timestamp). Updated `_drawio_generator.py` to include `updatedAt` column for RefreshToken box, then regenerated `identity-user/schema.drawio`. |
| L10 | LOW | `db-schema/booking/schema.sql` | Removed redundant `idx_passengers_booking_id` (covered by leading column of UNIQUE `uq_passengers_booking_seat`). |

**Total in-place fixes: 14.** All updates atomic — schema.sql + drawio (where column added) + comments consistent.

---

## Post-Seal Refactor — Wallet natural PK (2026-05-25)

User-driven design alignment: `wallets` table đổi từ synthetic `id` PK + `UNIQUE(user_id)` → **natural `user_id` PK** để đồng nhất với `operator_wallets.operator_id` PK pattern. Hệ quả:

| File | Change |
|---|---|
| `payment-wallet/schema.sql` | `wallets`: drop `id` column + `UNIQUE(user_id)` → `user_id UUID PRIMARY KEY`. `wallet_transactions`: drop hard FK `wallet_id REFERENCES wallets(id)` → `user_id UUID NOT NULL` (logical FK, mirror `operator_wallet_transactions`). Rename index `idx_wallet_transactions_wallet_id_created_at` → `idx_wallet_transactions_user_id_created_at`. Drop redundant `idx_wallets_user_id`. |
| `payment-wallet/schema.drawio` | Wallet: row 1 `<u>id</u>` → `<u>userId</u>`, remove row 2 (was FK userId), table height 240 → 210. WalletTransaction: row 2 `<i>walletId</i>` → `<i>userId</i>`. |
| `payment-wallet/README.md` | New "Wallet PK convention" design decision section; entity table updated; index strategy table updated; cross-service refs table updated. |
| `_global/cross-service-references.md` | `Wallet.user_id` row marked as PK 1-1; new row for `WalletTransaction.user_id` logical FK; bootstrap note updated with UPSERT ON CONFLICT. |
| `SU26SE101_VIETRIDE_technical_context_v7.md` | Section 6.5 Wallet entity description + WalletTransaction reference field + optimistic lock query example updated. Section 8 Payment & Wallet Service entity bullet updated. |

**Justification:** Symmetry với `operator_wallets`; bootstrap UPSERT trên natural PK = idempotent perfect cho RabbitMQ at-least-once delivery; tiết kiệm 16 bytes/row + 1 redundant index. Không vi phạm cross-DB FK rule (vốn đã không có hard FK cross-service ngay từ đầu). Integrity vẫn enforce qua 4 lớp: event-driven create + HTTP validate at write + tenant filter middleware + cascade event on delete.

## Pending User Confirmation (BLOCKER + HIGH)

**None.** Schema passes review without requiring user intervention on critical findings.

---

## Informational (No Action Required)

| # | Topic | Detail |
|---|---|---|
| L11 | `notification_type` enum naming clarity | Enum vẫn có `PAYOUT_PROCESSED` + `PAYOUT_FAILED` values (matches v6 line 4691 verbatim). Sau khi rewrite Section 4.6 thành wallet model, naming "PAYOUT" hơi unclear (technically operator wallet credit, không phải bank payout). Semantic vẫn valid (operator nhận tiền). Khuyến nghị v2: rename → `TRIP_SETTLEMENT_COMPLETED` + `TRIP_SETTLEMENT_FAILED` cùng với V6 update. Không fix v1 để giữ alignment với v6 sealed spec. |

---

## Recommendations (post-review)

### For Immediate Next Phase (Auth Service Scaffold)

1. **Build EF Core DbContext** từ `identity-user/schema.sql`:
   - `VietRide.Identity.Infrastructure.Persistence.IdentityDbContext`
   - Use EF Core fluent API `[Index]`/`HasIndex()` matching schema indexes
   - Apply `[ConcurrencyCheck]` on `row_version` columns
   - `OnModelCreating` config naming policy: PascalCase entity ↔ snake_case table/column
   - HasData seeding for SubscriptionPlan (fixed UUID `00000000-0000-0000-0000-000000000001`)
   - Bootstrap SYSTEM_ADMIN seeding via env vars + `WHERE NOT EXISTS` check (idempotent migration)
2. **Migrate first**: `dotnet ef migrations add InitialIdentity --project src/VietRide.Identity.Infrastructure`, then `dotnet ef database update`.
3. **Bootstrap event handlers** sẵn sàng cho Wallet/PlatformWallet/OperatorWallet creation (Step 2 onwards services).
4. **Smoke test** sample queries: User lookup by email, Operator subscription join, OAuth identity link.

### Cross-Service Considerations

1. **Outbox pattern**: cùng pattern across 7 services (Notification excepted). Mỗi service implement `BackgroundService` poll mỗi 5s → publish RabbitMQ.
2. **Event handler order**: Payment Service seed `PlatformWallet` singleton → Identity Service publish `operator.approved` → Payment Service create `OperatorWallet`. Defensive UPSERT (ON CONFLICT DO NOTHING) cho idempotent redelivery.
3. **TripSettlement insert handler**: Payment Service consume `trip.trip.completed` / `trip.trip.disrupted` → IF SUM(ledger entries) > 0 → INSERT settlement record. UPSERT trên `(operator_id, trip_id)` để idempotent.

### Optional Optimizations for v2

1. **Partitioning** `gps_trails` theo `recorded_at` monthly khi > 100M rows.
2. **HNSW index** thay IVFFlat cho `knowledge_chunks.embedding` nếu cần recall tốt hơn.
3. **Materialized views** cho dashboard reporting (booking_stats, parcel_stats) thay refresh-by-event nếu eventual consistency window quá rộng.
4. **Bank Withdrawal flow** (v2 in v6 list): thêm entity `OperatorWithdrawalRequest`, `operator_wallet_transaction_ref` enum thêm value `WITHDRAWAL`.
5. **Counter staff seat disable** (v2 contingency): chỉ relax authorization endpoint — schema không đổi.
6. **Notification type rename** (v2): `PAYOUT_PROCESSED` / `PAYOUT_FAILED` → `TRIP_SETTLEMENT_COMPLETED` / `TRIP_SETTLEMENT_FAILED` cùng với v6 update.

---

## Sign-off

✅ **Schema sealed.** Ready for implementation phase. No outstanding BLOCKER / HIGH findings. All MEDIUM + LOW fixes applied in-place with parallel drawio regeneration. Cross-references consistent across docs.
