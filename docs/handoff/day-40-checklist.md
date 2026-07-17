# Day 40 — Final checklist

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 40 — Admin users + Station cleanup + Reports backend (`SCV-122`)
- **Plan**: `docs/handoff/day-40-plan.md` — APPROVED
- **Ngày audit**: 2026-07-17
- **Status**: ✅ READY

## Kết quả DoD

- [x] ✅ Danh mục user hỗ trợ search/filter/page/sort/includeDeleted, chỉ dành cho `SYSTEM_ADMIN` và không trả password hash, OAuth subject, token hoặc failed-login internals.
- [x] ✅ Lock/unlock dùng shared idempotency, PostgreSQL per-User serialization và cùng lock order với password/Google login, refresh, forgot/reset password, failed-login và OTP failure; các race chỉ tạo outcome tuyến tính.
- [x] ✅ Lock revoke refresh token; unlock khôi phục đúng `lockedFromStatus`, reset DB + Redis lockout state, không phục hồi token cũ và không promote user chờ xác minh email thành `ACTIVE`.
- [x] ✅ ActivityLog query đúng actor/action/UTC `[from,to)`; consumer audit Station idempotent theo `source_event_id`; trigger PostgreSQL chặn direct `UPDATE` và `DELETE`.
- [x] ✅ Station normalize giữ contract/slug hiện hữu và phát Outbox; merge relink toàn bộ Trip-owned FK cùng OperatorStation atomically, collapse collision và flatten redirect trực tiếp về canonical Station.
- [x] ✅ Internal Station resolution phân biệt canonical, merged và ordinary deleted; public API không lộ Station đã xóa.
- [x] ✅ Booking lưu redirect bền vững, canonicalize mọi writer bằng advisory lock, relink Booking active và giữ nguyên lịch sử terminal; replay, out-of-order, cycle và consumer/writer race không tạo chain hoặc partial state.
- [x] ✅ Booking/Trip/Parcel internal report chỉ đọc earned terminal metrics theo UTC `[from,to)`; Parcel giữ signed net revenue và mọi phép cộng/SUM đều kiểm tra overflow về `Int64`.
- [x] ✅ Payment gọi song song các source bằng Internal JWT, trả totals bằng `byOperator`, giữ operator thiếu là `null`, ánh xạ overflow thành `500 REPORT_VALUE_OVERFLOW` và upstream failure thành `502` không partial.
- [x] ✅ Gateway longest-prefix/RBAC, Swagger/downstream contract, Postman tích lũy 14 request và isolated real-stack E2E `20/20` đều pass.
- [x] ✅ Timeline Review đã thực thi: Identity và Booking race chạy tối thiểu 50 vòng/case; report boundary/signed/overflow/upstream-failure, migration up/down/reapply và cleanup isolated stack đều pass.

## Tasks hoàn tất

- Task 40.0 — Chốt contract/SOT, error/event/schema registry và verification baseline — ✅
- Task 40.1 — Admin user directory, lock/unlock và per-User serialization — ✅
- Task 40.2 — Immutable ActivityLog và internal operator summaries — ✅
- Task 40.3 — Station redirect persistence và aggregate relink primitives — ✅
- Task 40.4 — Station normalize/merge, canonical resolution và Outbox — ✅
- Task 40.5 — Booking Station redirect, canonicalization và serialization — ✅
- Task 40.6 — Identity Station audit consumers — ✅
- Task 40.7 — Booking completed-report source — ✅
- Task 40.8 — Trip completed-report source — ✅
- Task 40.9 — Parcel earned-report source — ✅
- Task 40.10 — Payment platform-report orchestrator — ✅
- Task 40.11 — Gateway routes và cumulative Postman — ✅
- Task 40.12 — Isolated real-stack E2E acceptance — ✅

## Nhóm file thay đổi

- `apps/identity/` — admin user lifecycle, serialization executors, immutable ActivityLog, Station audit consumers, migrations và race/integration tests.
- `apps/trip/` — Station merge/normalize/canonical resolution, relink repositories, Outbox, internal report source, migrations và PostgreSQL tests.
- `apps/booking/` — durable Station redirects, advisory-lock canonicalizer, writer/consumer serialization, internal report source, migrations và race tests.
- `apps/parcel/` — signed earned-report source, partial index/migration và boundary/overflow tests.
- `apps/payment/` — platform-report orchestration, Internal JWT clients, checked aggregation và upstream failure mapping.
- `apps/gateway/`, `docs/api/postman/` — SYSTEM_ADMIN routes/RBAC và folder Day 40 tích lũy gồm 14 request.
- `infra/docker/docker-compose.day40-e2e.yml`, `scripts/run-day40-admin-reports-e2e.mjs`, `package.json` — isolated stack, deterministic seed, black-box acceptance, persistence assertions và cleanup.
- Các SOT/contract/schema canonical — Day 40 endpoint/event/error/migration/report anchors và Day 42 deferral được đồng bộ.

## Verification run

| Lệnh/gate | Kết quả | Bằng chứng |
|---|---|---|
| Identity build/format | ✅ PASS | Release build `0 Warning(s), 0 Error(s)`; `dotnet format --verify-no-changes` sạch. |
| Identity tests | ✅ PASS | Unit `269/269`; PostgreSQL integration `153/153`. |
| Trip build/format | ✅ PASS | Release build `0 Warning(s), 0 Error(s)`; `dotnet format --verify-no-changes` sạch. |
| Trip tests | ✅ PASS | Unit `310/310`; PostgreSQL integration `132/132`. |
| Booking build/format | ✅ PASS | Release build `0 Warning(s), 0 Error(s)`; `dotnet format --verify-no-changes` sạch. |
| Booking tests | ✅ PASS | Unit `351/351`; PostgreSQL integration `68/68`. |
| Parcel build/format | ✅ PASS | Release build `0 Warning(s), 0 Error(s)`; `dotnet format --verify-no-changes` sạch. |
| Parcel tests | ✅ PASS | Unit `175/175`; PostgreSQL integration `24/24`. |
| Payment build/format | ✅ PASS | Release build `0 Warning(s), 0 Error(s)`; `dotnet format --verify-no-changes` sạch. |
| Payment tests | ✅ PASS | Unit `98/98`; integration `35/35`. |
| Shared Web tests | ✅ PASS | `88/88`. |
| Identity race gates | ✅ PASS | 5 nhóm race dùng PostgreSQL thật, `50` vòng/case; gồm auth/failed-login/password-reset/locked-origin invariants. |
| Booking Station serialization | ✅ PASS | 4 writer variants × `50` vòng với PostgreSQL advisory lock thật. |
| `npm run lint:ts` | ✅ PASS | 14 project; chỉ còn 2 Notification warning có sẵn, không có error. |
| Gateway test/build | ✅ PASS | `158/158`; build exit `0`. |
| Full TypeScript test/build | ✅ PASS | 10 project test pass; 10 project build pass, chỉ có dependency/generated source-map warning. |
| EF migration gate | ✅ PASS | Identity, Trip, Booking và Parcel migration apply; rollback Day 40; reapply; schema/history đúng. |
| `npm run e2e:day40` | ✅ PASS | Exit `0`, `20/20`: API/RBAC, race, idempotency, audit, Station, Booking relink, report, outage/recovery, persistence và migration đều pass. |
| Real-stack dependencies | ✅ PASS | PostgreSQL, Redis, RabbitMQ, Identity, Trip, Booking, Parcel, Payment và Gateway thật; không mock DB/HTTP/repository. |
| Cumulative Postman | ✅ PASS | Collection/environment parse được; folder Day 40 có đúng `14` request và UUID prefix `40000000-...`. |
| `git diff --check` | ✅ PASS | Không có whitespace error. |
| EOL/BOM invariant | ✅ PASS | `LINE_ENDINGS_AND_BOM_PASS 240`; C# dùng CRLF, TS/JSON/MD/MJS/YAML/SQL dùng LF và không file nào có UTF-8 BOM. |
| Dependency/commit invariants | ✅ PASS | MediatR `11.1.0`; không `.csproj Version=`; không dependency observability bị cấm; nhánh không có `Co-Authored-By`. |
| Cleanup verification | ✅ PASS | Harness in `cleanup PASS`; không giữ volume/container của compose project Day 40. |

## E2E acceptance summary

```text
seed PASS
admin users PASS
lock/unlock PASS
identity race invariants PASS
password reset lock race PASS
locked origin restore PASS
shared idempotency PASS
activity immutability PASS
station normalize PASS
station merge PASS
booking relink PASS
booking station race invariants PASS
audit consumers PASS
platform report PASS
signed/overflow report PASS
upstream failure PASS
database assertions PASS
cleanup PASS
```

## Contract / event / schema đã hoàn tất

- REST admin: user list, lock/unlock, ActivityLog list, Station normalize/merge và platform report; internal Station/operator/report endpoints không được Gateway expose.
- Events: `trip.station.normalized` và `trip.station.merged`; Trip Outbox cùng transaction, Booking/Identity consumers idempotent và PII-safe.
- Identity schema: `locked_from_status`, immutable ActivityLog read model, `source_event_id` unique marker và Station audit actions.
- Trip schema: `merged_into_station_id`, canonical redirect index và partial completed-report index.
- Booking schema: `booking_station_redirects` không cross-DB FK và partial completed-report index.
- Parcel schema: partial `DELIVERY_CONFIRMED` report index; signed net dùng PostgreSQL `NUMERIC` rồi checked-convert về `Int64`.
- Reporting ownership: Payment chỉ orchestrate read-only qua Internal JWT; không cross-DB read/write, không cache hoặc Stats materialization trong Day 40.

## Known gaps và carry-over

- Không còn gap chức năng trong phạm vi Day 40.
- Hai Notification lint warning và source-map warning từ dependency/generated source đã tồn tại, tất cả verification command vẫn exit `0`.
- Day 42 mới materialize Stats, thêm Redis report cache và advanced analytics; Day 40 giữ live indexed earned-report baseline.
- Chưa chuyển mục trong `TASK.md` sang Done hoặc ghi `CHANGELOG_AI.md`; hai bước này chờ người dùng xác nhận manual test theo workflow của repo.
