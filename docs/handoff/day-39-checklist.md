# Day 39 — Final checklist

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 39 — Driver Ops incident + TripStop arrival (`SCV-120`)
- **Plan**: `docs/handoff/day-39-plan.md` — Revision 3
- **Ngày audit**: 2026-07-16
- **Status**: ✅ READY

## Kết quả DoD

- [x] ✅ Assigned `DRIVER` và `ASSISTANT` báo Incident qua Gateway nhận `201 ApiResponse`; Incident được normalize đúng, Trip không đổi trạng thái và mỗi execution chỉ commit một Incident + một Outbox.
- [x] ✅ `trip.incident.reported` chỉ fan-out tới active `OPERATOR_ADMIN` cùng operator; staff, inactive admin và cross-operator bị loại; Identity outage retry và duplicate delivery không tạo notification trùng.
- [x] ✅ TripStop arrival canonical trả `200`, chỉ chuyển `PENDING -> ARRIVED`, set `actualArrivalTime`, giữ nguyên static ETA và phát đúng một `trip.stop.arrived`.
- [x] ✅ Destination arrival tạo one-shot `destinationArrivedAt`/`destinationArrivedByUserId`, không complete Trip, phát đúng một `trip.destination.arrived` và hỗ trợ express Trip không có TripStop.
- [x] ✅ Arrival trước `IN_PROGRESS` trả `422 TRIP_NOT_IN_PROGRESS`; finalized stop/destination với key mới trả `409`; route Operator cũ trả `404`.
- [x] ✅ Same-key idempotency không chạy handler hai lần; two-key stop/destination race chỉ có một winner và một business Outbox.
- [x] ✅ Parcel unload bị chặn trước matching stop/destination anchor; sau anchor chỉ chuyển `IN_TRANSIT -> UNLOADED` và release cargo một lần.
- [x] ✅ Action deliver riêng chỉ chuyển `UNLOADED -> DELIVERED_PENDING_CONFIRM`, sinh token 48 giờ + một event/delivery, không release cargo lần hai; recipient confirm hiện hữu tiếp tục hoạt động.
- [x] ✅ Hai Trip migration fresh/up/down/reapply, full verification matrix và `npm run e2e:day39` đều pass; isolated stack cleanup hoàn toàn.
- [x] ✅ Timeline Review đã thực thi: Incident không tự đổi `Trip.status`; arrival trước Trip `IN_PROGRESS` trả đúng HTTP `422`.

## Tasks hoàn tất

- Task 39.0 — Chốt baseline và harden shared idempotency — ✅
- Task 39.1 — Incident aggregate, migration, API và Outbox — ✅
- Task 39.2 — Operator recipient resolution và Incident notification — ✅
- Task 39.3 — Chuyển/siết TripStop arrival và thêm destination-terminal anchor — ✅
- Task 39.4 — Sửa Parcel terminal gate và canonical two-step delivery — ✅
- Task 39.5 — Real-stack E2E, Postman và final gate — ✅

## Nhóm file thay đổi

- `libs/dotnet/VietRide.Shared.Web/`, `tests/dotnet/VietRide.Shared.Web.UnitTests/` — idempotency v2 fingerprint, response/processing keys, owner-safe Lua finalize/release và regression tests.
- `apps/trip/` — Incident aggregate/API/Outbox/migration; canonical stop arrival; destination arrival anchor, typed events, internal Trip snapshot và PostgreSQL race/migration tests.
- `apps/notification/` — parse/map/consume `trip.incident.reported`, Identity operator-recipient resolution, retry/dedupe và PII-safe notification tests.
- `apps/parcel/` — matching arrival gates, assigned-assistant authorization, two-step unload/deliver CAS, token/event separation và persistence tests.
- `apps/booking/tests/`, `apps/payment/tests/` — stateful Redis v2 test doubles để supporting integration suites mô phỏng đúng Lua finalize/replay; không đổi production source.
- `BACKEND_SOURCE_OF_TRUTH.md`, `VietRide_API_Contract_v1.md`, `db-schema/trip-route-vehicle/schema.sql` — endpoint/event/error/schema registry và changelog canonical.
- `infra/docker/docker-compose.day39-e2e.yml`, `scripts/run-day39-driver-ops-e2e.mjs`, `package.json` — isolated real-stack harness, deterministic seed, migration reconciliation và cleanup.
- `docs/api/postman/` — cumulative Day 39 folder/environment với canonical routes, deterministic IDs và runtime token placeholders.

## Verification run

| Lệnh/gate | Kết quả | Bằng chứng |
|---|---|---|
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | Không có format/EOL drift. |
| Shared tests | ✅ PASS | Web `86/86`; Messaging `4/4`; Persistence `4/4`. |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | Không có format drift. |
| Trip tests | ✅ PASS | Unit `288/288`; PostgreSQL integration `119/119`. |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| Identity tests | ✅ PASS | Unit `249/249`; integration `145/145`. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| Booking tests | ✅ PASS | Unit `338/338`; integration `50/50`. |
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| Payment tests | ✅ PASS | Unit `87/87`; integration `31/31`. |
| `dotnet build apps/parcel/VietRide.Parcel.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)` |
| `dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes` | ✅ PASS | Không có format drift. |
| Parcel tests | ✅ PASS | Unit `166/166`; PostgreSQL integration `20/20`. |
| `npx nx run notification:lint` | ✅ PASS | `0` error; `2` warning non-null assertion có sẵn ngoài hunk Day 39. |
| `npx nx run notification:test` | ✅ PASS | `22/22` suites, `125/125` tests. |
| `npx nx run notification:test:e2e` | ✅ PASS | `7/7` suites, `14/14` tests. |
| `npx nx run notification:build` | ✅ PASS | Exit `0`; chỉ có source-map warning từ dependency/generated Prisma. |
| `npx nx run gateway:test` | ✅ PASS | `7/7` suites, `146/146` tests. |
| `npx nx run gateway:lint` | ✅ PASS | Không có lint error. |
| Trip EF migration gate | ✅ PASS | Scratch DB apply full chain; rollback `AddTripDestinationArrival`, rollback `AddTripIncidents`, reapply; enum/table/FK/index/JSONB/precision/destination columns/history đều đúng. |
| Real-stack health/readiness | ✅ PASS | PostgreSQL, Redis, RabbitMQ, Identity, Trip, Parcel, Notification và Gateway đều sẵn sàng trước seed/scenario trong isolated harness. |
| `npm run e2e:day39` | ✅ PASS | Final run `14/14`, exit `0`: Incident, notification retry/dedupe, stop/destination races, Parcel two-step và direct DB/Redis/RabbitMQ assertions đều pass. |
| Timeline Day 39 Review execution | ✅ PASS | E2E-01 xác nhận Trip status bất biến; E2E-09/E2E-10 xác nhận pre-state `422`, finalized/race semantics và old route `404`. |
| Cumulative Postman artifact | ✅ PASS | Collection/environment parse được; Day 39 có `23` requests, token values để trống, không chứa credential thật. Runtime acceptance dùng Node harness vì cần race/retry/DB assertions. |
| `git diff --check` | ✅ PASS | Không có whitespace error. |
| EOL/CPM/dependency invariant | ✅ PASS | Changed/untracked `.cs` dùng CRLF; MD/JSON/MJS/YAML/SQL/TS dùng LF; không thêm dependency hoặc `.csproj` version drift; MediatR vẫn `11.1.0`. |
| Cleanup verification | ✅ PASS | `cleanup PASS`; không còn container hoặc volume thuộc compose project `day39-e2e`. |

## Contract / event / schema đã ship

- REST: `POST /v1/driver/trips/{tripId}/incident`; `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive`; `POST /v1/driver/trips/{tripId}/destination/arrive`; `POST /v1/assistant/parcels/{parcelId}/deliver`; unload contract được sửa về `UNLOADED` only.
- Events: canonical `trip.incident.reported`, `trip.stop.arrived`, `trip.destination.arrived`; `parcel.parcel.unloaded` và `parcel.parcel.delivered_pending_confirm` được tách đúng business action.
- Trip schema/migrations: `20260715122504_AddTripIncidents` và `20260715133857_AddTripDestinationArrival`; Incident enum/table/index/JSONB/GPS và `trips.destination_arrived_*` đồng bộ EF/DDL.
- Idempotency: SHA-256 key namespace v2, actor/method/path/query/raw-body fingerprint, processing TTL 120 giây, response TTL 24 giờ, owner-safe finalize/release, exact required/mismatch/pending errors.
- Error registry: `TRIP_NOT_IN_PROGRESS`, `TRIP_STOP_NOT_FOUND`, `TRIP_STOP_ALREADY_FINALIZED`, `TRIP_DESTINATION_ALREADY_ARRIVED`, `DESTINATION_TERMINAL_NOT_ARRIVED`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`, `IDEMPOTENCY_REQUEST_PENDING` và các Parcel status/anchor errors liên quan.
- BSOT event/error/endpoint registry và changelog đã cập nhật đến version `1.34.0`, gồm Incident notification, arrival anchors, idempotency v2 và Parcel two-step.

## Known gaps và carry-over Day 40

- Không còn gap chức năng trong phạm vi Day 39.
- Notification còn `2` lint warning có sẵn và build có source-map warning từ dependency/generated Prisma; tất cả command exit `0`, không phát sinh từ logic Day 39.
- Cumulative Postman giữ token Day 39 rỗng để không commit secret; isolated Node harness là runtime acceptance authoritative cho retry, race và persistence assertions.
- Day 40 có thể dựa vào canonical Driver/Assistant routes, destination arrival anchor, shared idempotency v2 và Parcel two-step đã được khóa ở Day 39.

## Ghi chú cho planning Day 40

- Timeline gốc ghi route arrival rút gọn `/assistant/trip-stops/{id}/arrive`; canonical API/BSOT đã khóa route `/v1/driver/trips/{tripId}/stops/{stopId}/arrive` cho cả role `DRIVER | ASSISTANT`, không tạo alias Operator/Assistant cũ.
- Không suy diễn `Trip.completedAt` là destination arrival; mọi flow terminal-bound tiếp tục dùng `destinationArrivedAt` riêng.
- Không gộp lại unload và deliver; cargo release chỉ ở unload, token/notification chỉ ở deliver.
