# Day 40 — Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 40 — Admin users + Station cleanup + Reports backend (Jira: SCV-122)
- **Prior checklist**: `docs/handoff/day-39-checklist.md` (`not found`)
- **Plan status**: DRAFT → (reviewer) APPROVED / REVISION-REQUIRED

## Objective

Day 40 cung cấp các thao tác quản trị platform cho System Admin: truy vấn và khóa/mở khóa người dùng, tra cứu audit log bất biến, hợp nhất và chuẩn hóa Station canonical, cùng điểm vào báo cáo tổng hợp toàn platform. Các thao tác phải giữ ranh giới database giữa services, RBAC `SYSTEM_ADMIN`, ADR 0004 và tính đúng đắn của mapping `OperatorStation`. Báo cáo chỉ được triển khai khi đã xác định rõ service sở hữu aggregation và định nghĩa số liệu; không được truy vấn chéo database.

## Success criteria (DoD — binary, verifiable)

- [ ] System Admin hợp nhất được hai Station trùng lặp; mọi `OperatorStation` hợp lệ sau merge trỏ tới Station đích và không vi phạm unique `(operator_id, station_id)`.
- [ ] System Admin truy vấn được activity log theo action, user và khoảng ngày; không có API hoặc luồng code nào sửa/xóa activity log đã ghi.
- [ ] `GET /v1/admin/reports/platform?from=&to=` trả về các số tổng hợp đã được chốt contract cho doanh thu, chuyến và parcel trong khoảng thời gian hợp lệ.
- [ ] `GET /v1/admin/users` hỗ trợ bộ lọc đã được chốt contract; lock/unlock thay đổi trạng thái đúng, có audit log và bị từ chối với caller không phải `SYSTEM_ADMIN`.
- [ ] Các endpoint mới đi qua Gateway, dùng `ApiResponse<T>`, có Swagger và có integration/E2E tests cho happy path, RBAC và validation.

## Contract changes

- Chuẩn hóa tất cả public route Day 40 có prefix `/v1` theo `SU26SE101_VIETRIDE_technical_context_v7.md` § API endpoint conventions. Timeline viết dạng rút gọn không có `/v1`.
- Bổ sung contract chi tiết cho `GET /v1/admin/users`, hai action lock/unlock, `GET /v1/admin/activity-logs`, `PATCH /v1/admin/stations/{id}`, merge Station và `GET /v1/admin/reports/platform` vào `VietRide_API_Contract_v1.md` trước khi worker viết endpoint.
- Mâu thuẫn cần quyết định: technical context quy định `POST /v1/admin/stations/{primary}/merge { duplicateId }`; timeline ghi `POST /admin/stations/merge` với source → target. Không triển khai route nào trước khi chọn canonical path/body.
- Gateway cần các prefix `GET/POST/PATCH /v1/admin/*` được route tới đúng owner service sau Task 40.0. `routes.ts` hiện có `/v1/admin/users` và `/v1/admin/booking-stats`, nhưng chưa có activity logs, admin Station hoặc platform reports.
- Không có routing key/migration mới được suy diễn. Nếu audit Station phải được ghi vào Identity `ActivityLog`, Task 40.0 phải chốt event contract và outbox/consumer; Trip không được ghi trực tiếp sang Identity DB.

## Tasks

### Task 40.0 — Pre-reqs / architecture baseline: khóa contract và ownership liên service
| Field | Value |
|---|---|
| stack/owner | cross-cutting — kiến trúc/API contract |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `VietRide_API_Contract_v1.md`; `apps/gateway/src/config/routes.ts`; `docs/handoff/day-40-plan.md` (chỉ cập nhật câu trả lời đã được human xác nhận) |
| forbidden scope | Không sửa `.env`, secret, `.agents/**`, `.codex/**`, database schema, service implementation, migration, event consumer/publisher, git operations hoặc các endpoint ngoài Day 40. |
| depends on | — |
| invariant flags | Public API `/v1`; `SYSTEM_ADMIN` RBAC; ADR 0004; action mutation phải có `Idempotency-Key` hoặc exception được BSOT nêu rõ; no cross-DB FK/read-write; không tự phát minh event/routing key; docs dưới `docs/` viết tiếng Việt có dấu. |
| acceptance | Quyết định được ghi rõ và được human chấp thuận cho: (1) canonical request/response, error codes, pagination/filter/sort allow-list và idempotency của users, lock/unlock, activity logs; (2) canonical merge route/body, source-record lifecycle, xử lý collision `(operatorId,targetStationId)` và phạm vi các FK Station ngoài `OperatorStation`; (3) nơi đặt endpoint reports, nguồn dữ liệu/service-to-service contract, định nghĩa `revenue`, `trips`, `parcels`, timezone và biên `from/to`; (4) cơ chế audit Station không vi phạm ranh giới service. Gateway route table chỉ chứa route đã có downstream owner đã chốt. |
| source citations | `BE_TIMELINE_VU.md:401-409`; `SU26SE101_VIETRIDE_technical_context_v7.md:600-606, 3517-3539`; `VietRide_API_Contract_v1.md:760-790, 792-842`; `AGENTS.md` Quick references; `AGENTS_DOTNET.md` Domain conventions. |

### Task 40.1 — Identity: admin user directory, lock/unlock và activity-log read model
| Field | Value |
|---|---|
| stack/owner | dotnet / Identity Service |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | `apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs`; `apps/identity/src/VietRide.Identity.Api/Controllers/AdminActivityLogsController.cs` (new); `apps/identity/src/VietRide.Identity.Api/Controllers/Requests/AdminUserQueryRequest.cs` (new, nếu cần); `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListUsers/**` (new); `apps/identity/src/VietRide.Identity.Application/Features/Admin/LockUser/**` (new); `apps/identity/src/VietRide.Identity.Application/Features/Admin/UnlockUser/**` (new); `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListActivityLogs/**` (new); `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IUserRepository.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IActivityLogRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/UserRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/ActivityLogRepository.cs`; `apps/identity/tests/**` (new/changed tests); `VietRide_API_Contract_v1.md` only as finalized by Task 40.0. |
| forbidden scope | Không sửa Identity auth/token/JWKS flow, password hashing, schema/migration trừ khi Task 40.0 chứng minh cần thiết, Operator lifecycle, các service khác, Gateway route table, `.env`, secrets hoặc git operations. Không thêm endpoint ghi/sửa/xóa `ActivityLog`. |
| depends on | 40.0 |
| invariant flags | CRLF `.cs`; MediatR v11; thin controller → `ISender.Send`; `SYSTEM_ADMIN` only; `UserStatus.LOCKED` không đăng nhập/refresh; ActivityLog append-only; action `LOCK_USER`/`UNLOCK_USER`; audit ghi cùng transaction với thay đổi User; `ApiResponse<T>`; QueryOptions page/pageSize ≤100; read không cần idempotency; mutation tuân thủ quyết định Task 40.0; không log password/token. |
| acceptance | Contract tests xác nhận filters/pagination/sort allow-list của `GET /v1/admin/users`; lock và unlock chỉ được System Admin gọi, chuyển state theo policy đã chốt và tạo đúng một immutable audit record chứa actor/target context; `GET /v1/admin/activity-logs` lọc action/user/from/to, sort newest first theo default, không trả dữ liệu nhạy cảm; non-admin nhận `403 FORBIDDEN`; invalid filter/date nhận error code contract; existing login/refresh từ user `LOCKED` tiếp tục bị chặn; build, `dotnet format --verify-no-changes`, unit và integration tests của Identity đều pass. |
| source citations | `BE_TIMELINE_VU.md:402-403, 407-408`; `SU26SE101_VIETRIDE_technical_context_v7.md:600-614`; `db-schema/identity-user/schema.sql:51-60, 284-299`; `apps/identity/src/VietRide.Identity.Domain/Entities/User.cs:326-330`; `apps/identity/src/VietRide.Identity.Domain/Entities/ActivityLog.cs:5-57`; `apps/identity/src/VietRide.Identity.Domain/Enums/ActivityLogAction.cs:3-28`; `VietRide_API_Contract_v1.md:760-790`. |

### Task 40.2 — Trip: merge Station canonical và normalize có kiểm soát
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip Service |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | `apps/trip/src/VietRide.Trip.Api/Controllers/AdminStationsController.cs` (new); `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/MergeStationsRequest.cs` (new); `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/NormalizeStationRequest.cs` (new); `apps/trip/src/VietRide.Trip.Application/Features/Admin/Stations/MergeStations/**` (new); `apps/trip/src/VietRide.Trip.Application/Features/Admin/Stations/NormalizeStation/**` (new); `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IStationRepository.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IOperatorStationRepository.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/Station.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/OperatorStation.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/StationRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/OperatorStationRepository.cs`; `apps/trip/tests/**` (new/changed tests); `VietRide_API_Contract_v1.md` only as finalized by Task 40.0. |
| forbidden scope | Không sửa public/operator Station create-search-link contract, Route/Trip/Booking/Parcel records ngoài scope FK đã chốt, Identity DB, foreign DB, migration/schema nếu không có quyết định 40.0, Gateway route table, `.env`, secrets hoặc git operations. Không hard-delete Station hay `OperatorStation` nếu chưa được user phê duyệt rõ chính sách lifecycle. |
| depends on | 40.0 |
| invariant flags | CRLF `.cs`; MediatR v11; `SYSTEM_ADMIN`; `Station` canonical platform-level, `OperatorStation` unique `(operatorId, stationId)`; soft-delete `deleted_at` tách `is_active`; một EF transaction cho merge/relink/lifecycle source; no cross-DB FK; no cross-service direct write; ADR 0004; mutation idempotency theo decision 40.0; audit theo cơ chế đã chốt. |
| acceptance | Merge từ source khác target, cả hai active/non-deleted; tất cả mapping source được xử lý atomically theo collision policy đã chốt và sau commit không còn duplicate `(operatorId,targetStationId)`; rollback không để state nửa chừng; source lifecycle và mọi reference ngoài `OperatorStation` tuân thủ quyết định 40.0; normalize chỉ cập nhật các field contract cho phép (name/coords/address/operatingHours), validate tọa độ/JSON và duy trì slug uniqueness; caller không phải System Admin bị `403`; source/target không tồn tại trả error contract; audit có actor, source, target/before-after data theo policy; build, format và Trip unit/integration tests pass. |
| source citations | `BE_TIMELINE_VU.md:404-405, 407-408`; `SU26SE101_VIETRIDE_technical_context_v7.md:601-606, 3519-3539`; `db-schema/trip-route-vehicle/schema.sql:86-141`; `apps/trip/src/VietRide.Trip.Domain/Entities/Station.cs:7-145`; `apps/trip/src/VietRide.Trip.Domain/Entities/OperatorStation.cs:8-70`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/OperatorStationConfiguration.cs:14-83`. |

### Task 40.3 — Platform reports: implementation của entry point đã có owner
| Field | Value |
|---|---|
| stack/owner | cross-cutting; owner service phải được Task 40.0 chốt |
| implement agent | worker (phân rã thành dotnet-worker/nest-worker theo owner đã chốt) |
| review agent | reviewer (và dotnet-reviewer/nest-reviewer theo từng service) |
| skill | add-endpoint; add-integration-event chỉ khi Task 40.0 phê duyệt event mới |
| owned files (write set) | Sau Task 40.0 mới được cố định. Các nguồn đọc đã tồn tại và chỉ được dùng qua boundary đã chốt: `apps/booking/src/VietRide.Booking.Api/Controllers/AdminBookingStatsController.cs`; `apps/booking/src/VietRide.Booking.Application/Features/BookingStats/GetAdminBookingStatsAggregate/**`; `apps/parcel/src/VietRide.Parcel.Application/Abstractions/Repositories/IParcelStatsRepository.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Persistence/Repositories/ParcelStatsRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `apps/gateway/src/config/routes.ts`; test projects tương ứng. |
| forbidden scope | Không query trực tiếp DB/schema của service khác; không tạo reporting service, materialized view, shared database, Kafka/RabbitMQ event, cache hoặc dependency mới khi chưa có quyết định Task 40.0; không thay đổi semantics của `booking_stats`/`parcel_stats`; không sửa `.env`, secrets hoặc thực hiện git operations. |
| depends on | 40.0 |
| invariant flags | `SYSTEM_ADMIN`; public `/v1`; ADR 0004; `from/to` contract và timezone rõ ràng; BIGINT VND, không decimal; aggregation nhất quán theo source of truth; no cross-DB FK/read; internal calls dùng Internal JWT nếu cần; Gateway route có role guard; read endpoint không cần `Idempotency-Key`; cache chỉ khi policy/invalidations được xác định. |
| acceptance | `GET /v1/admin/reports/platform?from=&to=` chỉ trả `200` cho System Admin và có schema response đã được chốt; khoảng ngày invalid nhận validation error; response đối chiếu được với fixture data từ tất cả source owners, không double-count, không vượt phạm vi ngày, và phân biệt rõ booking revenue/parcel revenue nếu contract yêu cầu; số trips lấy từ source chính thức đã chốt, không suy ra từ số booking; test chứng minh không dùng cross-service database context; Gateway forwards đúng endpoint/RBAC; build/lint/format/tests của mọi service touched pass. |
| source citations | `BE_TIMELINE_VU.md:406-408`; `SU26SE101_VIETRIDE_technical_context_v7.md:624-625`; `db-schema/booking/schema.sql:247-281`; `db-schema/parcel/schema.sql:258-275`; `apps/booking/src/VietRide.Booking.Api/Controllers/AdminBookingStatsController.cs:9-42`; `apps/booking/src/VietRide.Booking.Application/Features/BookingStats/GetAdminBookingStatsAggregate/GetAdminBookingStatsAggregateItemResult.cs:3-12`; `apps/parcel/src/VietRide.Parcel.Domain/Entities/ParcelStats.cs:5-21`; `AGENTS.md` Cross-DB FK; `AGENTS_DOTNET.md` Domain conventions. |

## Dispatch order

1. Task 40.0 → bắt buộc, serial. Plan không được chuyển sang APPROVED trước khi toàn bộ câu hỏi contract/owner được trả lời.
2. Task 40.1 và Task 40.2 → song song được sau 40.0 vì write set service-local, Gateway route đã được Task 40.0 khóa.
3. Task 40.3 → sau 40.0 và chỉ dispatch khi owner/data contract được chốt; có thể chạy song song với 40.1/40.2 nếu owner/write set không đụng nhau.
4. Sau mỗi task: `/implement-task 40.x` → review verdict → human `/verify`; không tự chuyển trạng thái Done.

## Progress tracker

> Orchestrator bookkeeping — main thread cập nhật sau mỗi `/implement-task`; đây không phải bằng chứng audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 40.0 | ⬜ todo | — | — | Cần giải quyết mâu thuẫn contract/ownership trước dispatch. |
| 40.1 | ⬜ todo | — | — | Identity admin users và activity logs. |
| 40.2 | ⬜ todo | — | — | Trip Station merge/normalize. |
| 40.3 | ⬜ todo | — | — | Blocked tới khi owner/metric reports được chốt. |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Open questions

1. Canonical API cho Station merge là `POST /v1/admin/stations/{primary}/merge { duplicateId }` theo technical context, hay `POST /v1/admin/stations/merge` source → target theo Day 40 timeline? Hãy chốt path, field names và response.
2. Khi source và target đã có `OperatorStation` cùng `operatorId`, policy là gì cho `isActive` và các fields riêng (`displayNameOverride`, `counterLocation`, `contactPhone`, `instructions`)? Có được xóa mapping source hay phải preserve history? Khi merge, có phải re-link cả Route/Trip và các logical reference ngoài Trip DB hay chỉ `OperatorStation` như timeline?
3. Source Station sau merge sẽ soft-delete, deactivate, hay giữ active với marker replacement? Hiện entity/schema không có `mergedIntoStationId`.
4. Station merge/normalize audit phải được lưu ở đâu? Identity `ActivityLog` có FK nội bộ tới User; Trip không được ghi chéo DB. Cần chốt event/outbox contract hoặc một audit store hợp lệ, và actor/audit payload tối thiểu.
5. `GET /v1/admin/users` bao gồm các role nào, filter/sort nào, và lock/unlock dùng route/method/body nào? Có được lock chính caller, System Admin khác, user `DELETED`, hay trạng thái pending không? Unlock đưa user về `ACTIVE` hay khôi phục trạng thái trước lock?
6. Activity log cần chính xác endpoint response, filter enum/action, inclusive/exclusive `from/to`, pagination/sort và semantics `userId`: actor thực hiện hay target bị tác động? SOT chỉ nêu entity/query pattern, chưa có REST contract.
7. Service nào sở hữu `GET /v1/admin/reports/platform`? Không có Reporting service; Booking/Parcel/Trip có database riêng. Cần chốt sơ đồ aggregation qua internal API/events, đồng thời định nghĩa revenue (booking, parcel, gross/net/refund), trips (created/scheduled/completed), parcels (created/paid/delivered), timezone và được phép cache hay không.
8. Reports có phải trả breakdown theo operator hay chỉ total platform? Technical context nêu cả so sánh operator, còn Day 40 chỉ nêu aggregate numbers.
