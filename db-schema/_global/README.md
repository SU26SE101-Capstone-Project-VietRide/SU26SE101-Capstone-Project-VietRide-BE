# VietRide — DB Schema Global Overview

Master overview cho 8 business services + 0 shared DB. Mỗi service có 1 logical PostgreSQL database riêng trong cùng 1 PG cluster duy nhất (xem v6 Section 3.4).

## Service ↔ Database mapping

| # | Service | Database name | Framework | Extensions | Bootstrap order |
|---|---|---|---|---|---|
| 1 | Identity & User | `vietride_identity` | .NET Core 8 + EF Core 8 | `pgcrypto` | 1 (first) |
| 2 | Trip-Route-Vehicle | `vietride_trip` | .NET Core 8 + EF Core 8 | `pgcrypto` | 2 |
| 3 | Booking | `vietride_booking` | .NET Core 8 + EF Core 8 | `pgcrypto` | 3 (parallel with 4, 5) |
| 4 | Payment & Wallet | `vietride_payment` | .NET Core 8 + EF Core 8 | `pgcrypto` | 3 |
| 5 | Parcel | `vietride_parcel` | .NET Core 8 + EF Core 8 | `pgcrypto` | 3 |
| 6 | Tracking | `vietride_tracking` | NestJS + Prisma | `pgcrypto` | 4 (parallel with 7, 8) |
| 7 | Notification | `vietride_notification` | NestJS + Prisma | `pgcrypto` | 4 |
| 8 | RAG AI | `vietride_rag` | NestJS + Prisma | `pgcrypto`, **`vector`** | 4 |

**Bootstrap order rationale:**
- Step 1 — Identity Service migrate đầu tiên + seed default `SubscriptionPlan` + bootstrap `SYSTEM_ADMIN` (vì 7 service còn lại có logical FK đến User/Operator).
- Step 2 — Trip-Route-Vehicle (Booking/Parcel reference Trip/Route/Stop/Station).
- Step 3 — Booking / Payment / Parcel chạy song song (independent của nhau ở schema layer; chỉ logical FK đến Step 1+2).
- Step 4 — Tracking / Notification / RAG chạy song song (logical FK đến Step 1, 2).

Tất cả service đều **idempotent**: chạy migration 2 lần không lỗi (EF Core / Prisma migrations history tự handle).

## Hangfire schema

Mỗi `.NET service` có `hangfire.*` schema **trong cùng DB của service đó** (không phải DB share riêng):

| Service | Hangfire schema | Jobs (xem README per service cho chi tiết) |
|---|---|---|
| Identity & User | `vietride_identity.hangfire` | OTP cleanup, FCM token stale cleanup |
| Trip-Route-Vehicle | `vietride_trip.hangfire` | Generate Trip, auto-BOARDING, auto-COMPLETED fallback |
| Booking | `vietride_booking.hangfire` | Seat release VNPay timeout, schedule-change auto-accept, PENDING_SEAT_ASSIGNMENT escalation |
| Parcel | `vietride_parcel.hangfire` | Undo-reject 15m, auto-reject EXTRA_LARGE 24h, PENDING auto-reject 30m, etc. |
| Payment & Wallet | `vietride_payment.hangfire` | PENDING_REDIRECT EXPIRED, TopUpRequest EXPIRED, Trip settlement eligibility + weekly PlatformWallet→OperatorWallet settle, Subscription trial expire, etc. |

NestJS services (Tracking / Notification / RAG) KHÔNG dùng Hangfire — dùng **BullMQ** (Redis-backed) cho scheduled job.

## Cross-cutting conventions

Tất cả schema tuân thủ v6 Section 8 conventions:

### Naming
- Table: **plural snake_case** (`users`, `bookings`, `trip_seats`)
- Column: **snake_case** (`passenger_user_id`, `total_amount`)
- PK: `id UUID PRIMARY KEY DEFAULT gen_random_uuid()`
- FK column: `<entity>_id`
- Index: `idx_<table>_<columns>`, unique: `uq_<table>_<columns>`, check: `chk_<table>_<rule>`

### Data types
- **Money (VND):** `BIGINT`. **KHÔNG** dùng DECIMAL/FLOAT/REAL/DOUBLE PRECISION.
- **Timestamps:** `TIMESTAMPTZ` (timezone-aware UTC). KHÔNG `TIMESTAMP` (naive).
- **`departureTime`:** `TIME` (no timezone) — semantic local ICT.
- **UUID:** `UUID` type với `gen_random_uuid()` default.
- **JSON config:** `JSONB`.
- **pgvector embedding:** `vector(1536)`.

### Money CHECK constraints
- `Wallet.balance >= 0`, `PlatformWallet.balance >= 0`, `OperatorWallet.balance >= 0` (CHECK).
- `wallet_transactions.amount > 0` (positive, type determines direction).
- `operator_ledger_entries.amount` signed (no CHECK; sign per entryType).
- `payments.amount >= 0`, `parcels.deposit_amount >= 0`, etc.

### Soft delete
- `is_active boolean` + `deleted_at timestamptz` cho: Operator, User, Station, Stop, Route, Vehicle.
- Partial unique indexes (`WHERE deleted_at IS NULL`) cho fields đáng được tái sử dụng sau soft delete.

### Audit columns standard
```sql
created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
-- + trigger auto-update updated_at on UPDATE (trg_set_updated_at function)
```

### Concurrency
- `row_version INT NOT NULL DEFAULT 0` cho entity có optimistic lock: `wallets`, `platform_wallets`, `operator_wallets`, `operator_trip_settlements`.

### Index baseline
- PK auto-indexed.
- Mọi FK column có index (intra-service + logical).
- Mọi enum status xuất hiện trong WHERE business flow → có index (thường partial).
- Mọi timestamp có range query (created_at, expires_at) → có index.

## PgBouncer pool config (gợi ý)

Per service connection pool — 8 business services + 1 Gateway:

```ini
# pgbouncer.ini
[databases]
vietride_identity     = host=postgres port=5432 dbname=vietride_identity     pool_size=20
vietride_trip         = host=postgres port=5432 dbname=vietride_trip         pool_size=20
vietride_booking      = host=postgres port=5432 dbname=vietride_booking      pool_size=20
vietride_payment      = host=postgres port=5432 dbname=vietride_payment      pool_size=20
vietride_parcel       = host=postgres port=5432 dbname=vietride_parcel       pool_size=15
vietride_tracking     = host=postgres port=5432 dbname=vietride_tracking     pool_size=15
vietride_notification = host=postgres port=5432 dbname=vietride_notification pool_size=10
vietride_rag          = host=postgres port=5432 dbname=vietride_rag          pool_size=10

[pgbouncer]
pool_mode = transaction
max_client_conn = 500
default_pool_size = 15
reserve_pool_size = 5
```

PostgreSQL config: `max_connections = 200` (PgBouncer giữ actual connections xuống ~80-120 thay vì 8 services × 100 pool = 800).

## Cross-service FK policy

**Cross-DB FK constraint BỊ CẤM ở DB layer.** Mọi reference đến entity ở service khác là **LOGICAL FK** (column UUID + comment). Enforcement:

- Validate khi tạo/update qua HTTP REST `GET /internal/v1/<resource>/{id}` với Internal JWT.
- Snapshot pattern (vd `Booking.tripSnapshot*`) cho read query không cần cross-service call.
- Cascade behavior: app-layer event-driven (consume `UserDeleted`/`OperatorSuspended` event để cleanup).

Xem `cross-service-references.md` cho danh sách đầy đủ logical FK + enforcement notes.

## How to run

```bash
# 1. Start cluster
docker compose up -d postgres pgbouncer redis rabbitmq

# 2. Create databases (1 cluster, 8 logical DBs)
psql -U postgres -h localhost <<EOF
CREATE DATABASE vietride_identity;
CREATE DATABASE vietride_trip;
CREATE DATABASE vietride_booking;
CREATE DATABASE vietride_payment;
CREATE DATABASE vietride_parcel;
CREATE DATABASE vietride_tracking;
CREATE DATABASE vietride_notification;
CREATE DATABASE vietride_rag;
EOF

# 3. Run schema + seed per service (in bootstrap order)
psql -U postgres -h localhost -d vietride_identity -f db-schema/identity-user/schema.sql
psql -U postgres -h localhost -d vietride_identity -f db-schema/identity-user/seed.sql

psql -U postgres -h localhost -d vietride_trip -f db-schema/trip-route-vehicle/schema.sql
psql -U postgres -h localhost -d vietride_trip -f db-schema/trip-route-vehicle/seed.sql

# ... (booking, payment-wallet, parcel can run in parallel after above)
psql -U postgres -h localhost -d vietride_booking      -f db-schema/booking/schema.sql
psql -U postgres -h localhost -d vietride_payment      -f db-schema/payment-wallet/schema.sql
psql -U postgres -h localhost -d vietride_parcel       -f db-schema/parcel/schema.sql

# ... (tracking, notification, rag-ai)
psql -U postgres -h localhost -d vietride_tracking     -f db-schema/tracking/schema.sql
psql -U postgres -h localhost -d vietride_notification -f db-schema/notification/schema.sql
psql -U postgres -h localhost -d vietride_rag          -f db-schema/rag-ai/schema.sql

# 4. .NET services boot → Hangfire auto-creates `hangfire.*` schema in each service DB.
# 5. SYSTEM_ADMIN log in with seed credentials → CHANGE PASSWORD immediately.
```

## Files in this directory

- `_drawio_generator.py` — helper Python script generates all 8 `schema.drawio` files from inline spec. Re-run after editing entity list.
- `README.md` — this file.
- `cross-service-references.md` — full logical FK list across services.
- `erd-drawing-master-guide.md` — cross-service overview ERD drawing guide.
