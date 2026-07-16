# Day 38 — Final checklist

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 38 — Invoice PDF + PlatformWallet + Settlement
- **Plan**: `docs/handoff/day-38-plan.md` — Revision 6
- **Ngày audit**: 2026-07-14
- **Status**: ✅ READY

## Kết quả DoD

- [x] ✅ Payment lưu `context JSONB` server-side, chỉ cho phép attach khi còn `{}`, bao phủ booking/parcel allocation và subscription billing snapshot; Phase-A readiness và quarantine được E2E xác nhận.
- [x] ✅ Money handler gốc ghi PlatformWallet và OperatorLedger trong local transaction; replay WALLET/VNPay/refund không tạo movement hoặc ledger trùng; booking/parcel không bị trừ platform fee.
- [x] ✅ `identity.operator.approved` bootstrap đúng một OperatorWallet bằng durable dedupe; Identity backfill và lazy-create race đều giữ `UNIQUE(operator_id)`.
- [x] ✅ `trip.trip.completed|disrupted` tạo tối đa một settlement cho `(operator_id, trip_id)`; `hasSubstitution` là audit-only trong Payment và E2E chứng minh economics không đổi.
- [x] ✅ Eligibility chạy `0 19 * * *` UTC, weekly settlement chạy `0 2 * * 1` UTC; settlement cân bằng PlatformWallet DEBIT với OperatorWallet CREDIT đúng `netAmount`.
- [x] ✅ Thiếu PlatformWallet balance giữ settlement `ELIGIBLE`, tăng failure metadata, retry không giới hạn theo tuần, alert Redis 24 giờ; thành công xóa active failure và loại row khỏi stuck query.
- [x] ✅ Subscription WALLET atomically DEBIT OperatorWallet và CREDIT PlatformWallet; WALLET/VNPay phát chung `payment.subscription.payment_succeeded`.
- [x] ✅ Mỗi subscription payment có tối đa một Invoice; counter tháng atomic; PDF chỉ chuyển `ISSUED` sau upload; stale PROCESSING vẫn tiêu attempt và dừng sau attempt thứ năm.
- [x] ✅ Download tạo URL mới TTL 60 phút, không persist signed URL, tenant-isolated và rate limit 10 request/phút/user/invoice.
- [x] ✅ Manual settle, retry Invoice và adjustment bắt buộc `Idempotency-Key`; same-key replay trả response gốc, different-key race không double movement/job.
- [x] ✅ Notification gửi in-app, push và email Invoice đúng OPERATOR_ADMIN; settlement dùng `netAmount`; logger redaction và E2E không thấy email/full payload/signed URL.
- [x] ✅ `npm run e2e:day38` pass 26/26 trên isolated PostgreSQL, Redis, RabbitMQ và service thật; toàn bộ build, format, test và migration gate liên quan đều xanh.

## Tasks hoàn tất

- Task 38.0 — Chốt Contract/SOT và architecture baseline — ✅
- Task 38.1 — Booking/Parcel payment context và canonical events — ✅
- Task 38.2 — Trip terminal events end-to-end — ✅
- Task 38.3 — Payment persistence cho Invoice, wallet, ledger và settlement — ✅
- Task 38.4 — Atomic revenue ledger, PlatformWallet và OperatorWallet bootstrap — ✅
- Task 38.5 — Settlement engine, scheduler và stuck-operation alert — ✅
- Task 38.6 — Subscription payment bằng OperatorWallet — ✅
- Task 38.7 — Invoice PDF, GCS/Firebase adapter, reconciliation và retry — ✅
- Task 38.8 — Operator/Admin APIs, Swagger, Gateway và Postman — ✅
- Task 38.9 — Notification consumers, email và PII-safe observability — ✅
- Task 38.10 — Isolated real-stack E2E, migration và verification gate — ✅

## Nhóm file thay đổi

- `apps/payment/` — aggregate Invoice/OperatorWallet/ledger/settlement, trusted context, money handlers, consumers, jobs, PDF/storage, APIs, migrations và tests.
- `apps/identity/` — WALLET subscription flow, canonical success handling, operator-wallet backfill marker/job, migrations và tests.
- `apps/trip/` — driver/assistant complete endpoint, terminal events, fallback completion, locking/audit và tests.
- `apps/booking/`, `apps/parcel/` — server-owned payment context snapshot và internal reconciliation endpoints.
- `apps/gateway/` — explicit Day-38 operator/admin/driver routes và route tests.
- `apps/notification/` — Invoice/settlement consumers, recipient resolution, push/in-app/email, Prisma migration và logger redaction.
- `libs/shared/contracts/` — canonical Day-38 event schemas, exports và contract tests.
- `db-schema/`, `VietRide_API_Contract_v1.md`, `BACKEND_SOURCE_OF_TRUTH.md` — canonical DDL, REST/event/error registry và changelog.
- `infra/docker/`, `scripts/run-day38-invoice-settlement-e2e.mjs`, `package.json` — isolated real-stack harness, deterministic seed và cleanup.
- `docs/api/postman/`, `docs/runbooks/day-38-invoice-settlement-rollout.md` — cumulative Postman folder, local variables và rollout/runbook.

## Verification run

| Lệnh/gate | Kết quả | Bằng chứng |
|---|---|---|
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet build apps/parcel/VietRide.Parcel.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet format <solution> --verify-no-changes` cho Payment, Identity, Trip, Booking, Parcel | ✅ PASS | Cả 5 solution không có format drift. |
| Payment tests | ✅ PASS | Unit `85/85`; PostgreSQL integration `31/31`. |
| Identity tests | ✅ PASS | Unit `249/249`; integration `145/145` khi chạy các class PostgreSQL tuần tự. Full assembly không dùng làm gate vì fixture cũ cùng lúc `DROP DATABASE`. |
| Trip tests | ✅ PASS | Unit `235/235`; integration `69/69`. |
| Booking tests | ✅ PASS | Unit `324/324`; integration `48/48`. |
| Parcel tests | ✅ PASS | Unit `153/153`; integration `18/18`. |
| Shared contracts build/test | ✅ PASS | Build pass; `39/39` tests. |
| Gateway lint/test/build | ✅ PASS | Lint pass; `146/146` tests; build pass. |
| Notification Prisma/lint/unit/E2E/build | ✅ PASS | Prisma validate/generate pass; lint pass với 2 warning có sẵn; unit `114/114`; E2E `14/14`; build pass, chỉ có source-map warning từ dependency. |
| Payment EF migration fresh/up/down/reapply | ✅ PASS | Fresh DB, rollback về migration trước Day 38 và reapply đều pass; exact enum/index/check assertions pass. |
| Identity EF migration fresh/up/down/reapply | ✅ PASS | Fresh DB, rollback và reapply đều pass; exact schema assertions pass. |
| Notification Prisma migration | ✅ PASS | Fresh DB apply đủ 9 migrations; baseline 8 migrations nâng lên Day-38 migration pass; enum/column/index assertions pass. |
| `npm run e2e:day38` | ✅ PASS | Isolated real stack pass `26/26`, đủ 13 business gates và `cleanup PASS`. |
| Phase-A legacy rollout E2E | ✅ PASS | VNPay callback HMAC thật + replay; hydrate qua Booking internal HTTP; ledger backfill đúng một lần; readiness `ready=true`, pending `0`, succeeded `0`, quarantined `>=1`. |
| Persistence/messaging assertions | ✅ PASS | PostgreSQL/Redis assertions, Outbox `PUBLISHED`, RabbitMQ consumer side effects và event replay dedupe đều pass. |
| Cumulative Postman artifact | ✅ PASS | Collection/environment parse được; Day 38 folder dùng runtime IDs/tokens và không tạo collection riêng. Runtime acceptance dùng harness vì cần async DB/race assertions. |
| `git diff --check` | ✅ PASS | Không whitespace error. |
| CPM/dependency/license | ✅ PASS | Không `.csproj PackageReference Version=`; MediatR `11.1.0`; PDFsharp-MigraDoc `6.2.3` theo CPM/MIT; Noto Sans kèm OFL-1.1; không dependency thương mại mới. |
| Commit/EOL invariants | ✅ PASS | 30 commit gần nhất không có `Co-Authored-By`; 267 file thay đổi được kiểm tra, `.cs/.csproj/.props` CRLF và TS/JSON/MD/MJS/Prisma/SQL/YAML LF đúng policy. |
| Cleanup verification | ✅ PASS | Đã xóa `day38-verification-postgres` và `day38-verification-redis`; không còn container tên Day 38. |

## Contract / event / schema đã ship

- REST: subscription upgrade hỗ trợ `VNPAY|WALLET`; driver Trip complete; operator Invoice/wallet/ledger/settlement reads; admin Invoice retry, wallet adjustments và manual settlement.
- Events: mở rộng `payment.payment.succeeded/refunded`; thêm canonical `payment.subscription.payment_succeeded`, `payment.invoice.issued`, `payment.trip_settlement.completed`; chuẩn hóa `trip.trip.completed|disrupted`.
- Payment schema: immutable `payments.context`, invoices/counter, operator wallets/transactions, operator ledger, trip settlements, processed-event marker, failure metadata và unique/check/partial indexes.
- Identity schema: subscription payment method/attempt fields và operator-wallet backfill marker.
- Notification schema: Invoice/settlement notification types và email delivery support.
- Error registry: Invoice, settlement, insufficient balance, idempotency và rate-limit codes trong plan/API/BSOT đã đồng bộ.
- BSOT registry và changelog §13 đã được cập nhật cho contract, event, error và persistence convention mới.

## Known gaps và carry-over Day 39

- Không còn gap chức năng trong phạm vi Day 38.
- Production GCS signed URL không gọi credential thật trong CI/E2E; isolated E2E dùng storage adapter Development có cùng TTL/path contract. Deploy phải cung cấp ADC/workload identity và bucket private theo runbook, không commit credential.
- Hai lint warning Notification và source-map warning từ dependency không phát sinh bởi logic Day 38, không làm thay đổi runtime acceptance.
- Day 39 có thể dựa vào Trip terminal event, Gateway explicit-route pattern, Outbox/idempotency và Notification recipient/redaction đã được Day 38 khóa.

## Ghi chú cho planning Day 39

- `docs/handoff/day-39-plan.md` được tạo trước checklist này nên dòng “prior checklist không tìm thấy” đã cũ; khi dispatch Day 39 phải dùng checklist này làm baseline.
- Không mở rộng Day 38 sang bank withdrawal, hóa đơn điện tử pháp lý hoặc platform fee booking/parcel nếu chưa có quyết định SOT mới.
