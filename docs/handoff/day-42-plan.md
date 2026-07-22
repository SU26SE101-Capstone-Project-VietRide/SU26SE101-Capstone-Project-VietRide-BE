# Day 42 — Ổn định báo cáo platform và cache

- **Trạng thái**: APPROVED — quyết định contract đã khóa theo Day 40 và mục tiêu Day 41–43.
- **Phạm vi**: materialized stats, reconciliation, Booking-owned platform facade, Redis cache và performance.

## Contract đã khóa

- Giữ nguyên `GET /v1/admin/reports/platform?from=&to=` và metric anchor UTC `[from,to)` của Day 40.
- Booking là facade platform; Payment ledger là nguồn doanh thu authoritative và không tạo bảng attribution mới.
- Mỗi nguồn Booking/Trip/Parcel/Payment chỉ đọc database của chính service; không có cross-DB query hoặc foreign key.
- Redis cache TTL 5 phút; key gồm exact UTC range và `platform-report:v1`; cache miss gọi đủ downstream.
- Downstream lỗi, timeout, malformed payload hoặc reconciliation mismatch làm cả request trả `503`, không trả partial hoặc stale totals.
- Reconciliation phải đối chiếu `BookingStats`, `ParcelStats` và nguồn earned live; mismatch được ghi structured log và không được promote hot read.
- Date range ICT inclusive ở public query, chuyển thành UTC `[from,to)`; mặc định 30 ngày, tối đa 92 ngày.

## Tasks

### 42.0 — Contract/SOT và reconciliation model

Ghi cache key, version, stats freshness, mismatch/error semantics, performance SLO và composite response contract vào SOT/API contract; không đổi public route.

### 42.1 — Validate/materialize stats

BookingStats, ParcelStats và Trip equivalent phải có projection/index và job/backfill idempotent từ earned live metrics. Reconciliation theo operator và range dùng BIGINT checked arithmetic; không promote khi mismatch.

### 42.2 — Booking platform facade và Redis cache

Di chuyển orchestration public từ Payment sang Booking bằng internal Payment/Trip/Parcel/Identity clients; cache read-through Redis 5 phút, single-flight theo key, bounded payload và invalidate sau reconciliation update. Payment giữ ledger reconciliation source, không còn là public owner.

### 42.3 — Gateway/RBAC/Swagger/Postman compatibility

Gateway chỉ proxy public route tới Booking, giữ SYSTEM_ADMIN/RBAC và không expose internal source routes. Cập nhật Swagger và cumulative Postman, kiểm tra cache hit/miss, stale rejection, range và upstream failure.

### 42.4 — Performance và real-stack acceptance

Seed benchmark 20 operators, 100.000 bookings/payments, 50.000 parcels và 10.000 trips; đo cold/warm query, cache hit ratio, memory và latency. `GET /v1/admin/reports/platform` phải đạt typical one-month dưới 2 giây trong isolated stack; chạy migration up/down/reapply và E2E thật.

## Verification gate

`dotnet build/format/test` cho Booking, Payment, Trip, Parcel; Redis/PostgreSQL reconciliation tests; Gateway lint/test/build; isolated PostgreSQL/Redis/RabbitMQ/API E2E; không dùng mock DB hoặc mock HTTP trong acceptance.

## Dispatch order

```text
42.0 → 42.1 → 42.2 → 42.3 → 42.4
```

## Progress

| Task | Status | Verification |
|---|---|---|
| 42.0 | done | Contract/SOT, ownership, cache key/TTL và fail-closed semantics đã đồng bộ. |
| 42.1 | done | Booking/Trip/Parcel projections, reconciliation và idempotent backfill đã pass. |
| 42.2 | done | Booking facade, Redis read-through 5 phút, single-flight và invalidation đã pass. |
| 42.3 | done | Gateway/RBAC/Swagger/cumulative Postman compatibility đã pass. |
| 42.4 | done | Real-stack benchmark 20/100k/100k/50k/10k, cold 388 ms, warm 104 ms và migration gate đã pass. |
