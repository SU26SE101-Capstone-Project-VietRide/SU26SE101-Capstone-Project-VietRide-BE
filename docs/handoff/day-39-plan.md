# Day 39 - Kế hoạch

- **Timeline ref**: `BE_TIMELINE_VU.md` Day 39, SCV-120.
- **Prior checklist**: `docs/handoff/day-38-checklist.md` - không tìm thấy trong worktree.
- **Plan status**: DRAFT - chờ PLAN-REVIEW.

## Mục tiêu

Hoàn thiện luồng Driver báo sự cố trong Trip service, lưu Incident và phát sự kiện outbox để Notification service tạo cảnh báo cho Operator. Sửa luồng TripStop arrival hiện có để chỉ Driver hoặc Assistant được phân công trên đúng chuyến có thể xác nhận khi Trip đang `IN_PROGRESS`; trạng thái `ARRIVED` là tín hiệu mở khóa thao tác UNLOAD của Parcel. Không có thay đổi tự động nào đối với `Trip.status` khi báo Incident.

## Tiêu chí thành công (DoD)

- [ ] Driver/Assistant được phân công báo Incident hợp lệ với category, mô tả tùy chọn tối đa 500 ký tự, tối đa ba URL ảnh và GPS; Incident được lưu, Outbox ghi cùng transaction và Notification tạo alert/push cho Operator.
- [ ] `Trip.status` không đổi sau khi báo Incident.
- [ ] Arrival hợp lệ set đúng `TripStop.actualArrivalTime` và `TripStop.status=ARRIVED`; Parcel có thể dùng trạng thái này để cho phép UNLOAD tại điểm trả.
- [ ] Arrival khi Trip chưa `IN_PROGRESS` bị từ chối bằng HTTP/error code đã được chốt; TripStop không còn `PENDING` bị từ chối và không phát event trùng.
- [ ] `dotnet build apps/trip/VietRide.Trip.sln -c Release`, `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes`, `dotnet test apps/trip/VietRide.Trip.sln -c Release`, và các test Notification liên quan đều qua.

## Thay đổi hợp đồng

- Thêm vào `VietRide_API_Contract_v1.md` endpoint báo sự cố Driver và endpoint arrival theo route cuối cùng được chốt; cả hai mutation bắt buộc `Idempotency-Key`, trả `ApiResponse<T>`, User JWT và role `DRIVER`/`ASSISTANT`.
- Bổ sung/chuẩn hóa registry payload event `trip.incident.reported` trong `BACKEND_SOURCE_OF_TRUTH.md`: tối thiểu `incidentId`, `tripId`, `category`, `reporterUserId`; payload delivery phải có đủ thông tin định tuyến recipient theo cơ chế Notification hiện hành.
- Migration EF mới cho bảng `incidents` và enum `incident_category` trong schema `vietride_trip`. `db-schema/trip-route-vehicle/schema.sql` hiện đã có DDL chuẩn, nên chỉ cập nhật file này nếu migration phát hiện sai khác thực tế, không tạo schema song song.
- Không có Gateway route mới dự kiến: Gateway đã route `/v1/driver` và `/v1/assistant` đến Trip service cho `DRIVER`/`ASSISTANT`.

## Tasks

### Task 39.0 - Bổ sung Incident aggregate và persistence chuẩn EF Core

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip-Route-Vehicle |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | scaffold-aggregate, ef-migration |
| owned files (write set) | `apps/trip/src/VietRide.Trip.Domain/Entities/Incident.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/IncidentCategory.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IIncidentRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/IncidentConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/IncidentRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/<timestamp>_AddIncidents.cs`; migration designer và `TripDbContextModelSnapshot.cs`; test persistence/domain Incident trong `apps/trip/tests/VietRide.Trip.UnitTests/` và `apps/trip/tests/VietRide.Trip.IntegrationTests/` |
| forbidden scope | Không sửa Gateway, Parcel, Notification, Identity, `.env`, secrets, `.agents/**`, `.codex/**`, contract/event registry, package versions, git operations; không sửa DDL canonical trừ khi chứng minh drift với migration. |
| depends on | Không có. |
| invariant flags | `.cs`/`.csproj` CRLF; MediatR v11; không NuGet mới; schema `vietride_trip`; snake_case; Incident thuộc Trip service; FK chỉ nội DB (`incidents.trip_id -> trips.id`); audit columns; JSONB `photo_urls`; không đổi Trip status. |
| acceptance | Entity chứa đúng fields SOT: `tripId`, `reportedByUserId`, category, description nullable, `photoUrls` JSONB nullable, lat/lng nullable, `reportedAt`, resolution fields nullable. Category chỉ gồm `TRAFFIC_JAM`, `VEHICLE_BREAKDOWN`, `ACCIDENT`, `WEATHER`, `OTHER`. EF map enum native Npgsql, DDL/migration reversible (`Up`/`Down`), index theo schema; migration chạy trên database trống và test round-trip persistence qua. |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md` §4.2 lines 495-501; `BACKEND_SOURCE_OF_TRUTH.md` §1.2 line 88, §entity registry line 1071; `db-schema/trip-route-vehicle/schema.sql` lines 49-51, 600-621; `AGENTS_DOTNET.md` EF Core/Migrations. |

### Task 39.1 - Báo Incident từ Driver và fan-out cảnh báo Operator

| Field | Value |
|---|---|
| stack/owner | cross-cutting / Trip + Notification + Gateway contract |
| implement agent | worker |
| review agent | reviewer |
| skill | add-endpoint, add-integration-event |
| owned files (write set) | `apps/trip/src/VietRide.Trip.Api/Controllers/DriverController.cs`; request DTO trong `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/`; command, validator, handler, response DTO trong `apps/trip/src/VietRide.Trip.Application/Features/Incidents/`; test Incident endpoint/handler trong `apps/trip/tests/VietRide.Trip.UnitTests/` và `apps/trip/tests/VietRide.Trip.IntegrationTests/`; `apps/notification/src/notifications/trip-tracking-alert-notification.mapper.ts`; `apps/notification/src/notifications/trip-tracking-alert-notification.mapper.spec.ts`; Notification consumer/resolver file đang đăng ký `mapTripTrackingAlertToNotifications`; `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md` |
| forbidden scope | Không sửa Trip status machine, Parcel unload handler, Tracking GPS flow, Firebase upload implementation, Identity schema, Gateway routes (trừ khi route review chứng minh thiếu), dependency/package version, `.env`, secrets, `.agents/**`, `.codex/**`, git operations. |
| depends on | 39.0. |
| invariant flags | `Idempotency-Key` bắt buộc; API envelope ADR 0004; User JWT; Gateway role và service authorization đều kiểm tra role + caller phải là `Trip.DriverUserId` hoặc `Trip.AssistantUserId`; FluentValidation; mô tả tối đa 500; URL ảnh tối đa 3; Outbox `trip.incident.reported` ghi trong transaction Incident; RabbitMQ `vietride.events`; consumer at-least-once idempotent; không cross-DB FK; không auto-disrupt/cancel/đổi Trip status. |
| acceptance | POST đúng endpoint đã chốt tạo một Incident và một outbox message; body invalid, category ngoài enum, >3 URL, description >500, crew không thuộc Trip, Trip không tồn tại đều trả lỗi envelope phù hợp và không ghi Incident/outbox. Notification parse event, resolve recipient Operator bằng cơ chế Identity nội bộ hiện có, tạo `INCIDENT_REPORTED` in-app record và đẩy vào push pipeline; duplicate delivery không tạo notification trùng. Unit test payload event/validation/authorization; integration test chứng minh persistence và atomic outbox. |
| source citations | `BE_TIMELINE_VU.md` Day 39 lines 392-399; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.2 lines 495-501; `BACKEND_SOURCE_OF_TRUTH.md` event registry line 1779; `apps/notification/src/notifications/trip-tracking-alert-notification.mapper.ts` lines 23-42, 100-107, 182-183; `AGENTS.md` Quick references; `AGENTS_DOTNET.md` Outbox/API/Idempotency. |

### Task 39.2 - Sửa TripStop arrival cho crew và siết precondition

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip-Route-Vehicle |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | `apps/trip/src/VietRide.Trip.Api/Controllers/DriverController.cs` (hoặc controller Driver/Assistant mới nếu route đã chốt); `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorTripsController.cs` để gỡ/chặn endpoint operator cũ theo quyết định tương thích; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/ArriveTripStopCommand.cs`; `ArriveTripStopCommandHandler.cs`; `ArriveTripStopResponse.cs`; unit/integration tests arrival trong `apps/trip/tests/VietRide.Trip.UnitTests/` và `apps/trip/tests/VietRide.Trip.IntegrationTests/`; `VietRide_API_Contract_v1.md` |
| forbidden scope | Không đổi schema TripStop; không đổi `estimatedArrivalTime`; không sửa Parcel code trong task này; không thay đổi event payload/routing key `trip.stop.arrived` ngoài trường cần cho authorization audit; không sửa Tracking/Redis ETA, Gateway nếu route đã có, dependencies, secrets, `.agents/**`, `.codex/**`, git operations. |
| depends on | Không có; có thể chạy song song 39.0 nếu không cùng worker/worktree, nhưng phải chờ contract route được chốt trước khi merge. |
| invariant flags | `Idempotency-Key`; User JWT; `DRIVER`/`ASSISTANT` và caller đúng crew snapshot của Trip; Trip bắt buộc `IN_PROGRESS` (không chấp nhận `BOARDING`); TripStop bắt buộc `PENDING`; `actualArrivalTime` lấy từ `IClock.UtcNow`; event Outbox cùng transaction; `estimatedArrivalTime` immutable; `ARRIVED` là state được Parcel đọc để mở UNLOAD; ApiResponse/error code UPPER_SNAKE_CASE. |
| acceptance | Route cũ `/v1/operator/trips/{tripId}/stops/{stopId}/arrive` không còn cho Operator vượt quyền (xóa/chuyển endpoint hoặc trả deprecation theo quyết định contract). Crew đúng Trip khi `IN_PROGRESS` làm `PENDING -> ARRIVED`, set timestamp UTC, phát đúng một `trip.stop.arrived`; caller cùng role nhưng không thuộc Trip nhận 403; `BOARDING`, `SCHEDULED` và terminal status nhận HTTP/error code đã chốt (timeline yêu cầu 422 trước `IN_PROGRESS`); ARRIVED/SKIPPED không đổi dữ liệu/event. Có test regression rằng Parcel-side authorization dựa trên TripStop `ARRIVED` vẫn nhận snapshot/status đúng. |
| source citations | `BE_TIMELINE_VU.md` Day 39 lines 396-399; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.2 lines 503-509, §TripStop lines 3598-3611, §parcel unload lines 2929-2941; `db-schema/trip-route-vehicle/schema.sql` lines 491-514; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/ArriveTripStopCommandHandler.cs`; `apps/gateway/src/config/routes.ts` lines 192-201. |

## Thứ tự dispatch

1. Resolve các Open questions bắt buộc, sau đó Task 39.0.
2. Task 39.1 sau 39.0 để dùng Incident repository/migration; xác nhận consumer Notification và contract trước khi code.
3. Task 39.2 độc lập với 39.0 ở mức code nhưng chạy sau khi chốt route/HTTP semantics để không tạo contract sai; có thể thực hiện song song với 39.1 ở worktree riêng.
4. Sau từng task: review bởi agent chỉ định, sau đó `/verify`. Kết thúc Day 39 bằng `/audit-day 39`.

## Progress tracker

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 39.0 | todo | - | - | - |
| 39.1 | todo | - | - | Chờ recipient semantics cho Operator. |
| 39.2 | todo | - | - | Chờ route và HTTP 422/409 được chốt. |

## Rủi ro và câu hỏi mở

1. **Route arrival mâu thuẫn SOT**: Timeline ghi `POST /assistant/trip-stops/{id}/arrive`; technical context ghi `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive`; code hiện tại là operator route. Chốt một canonical route trước khi dispatch 39.2, rồi cập nhật API contract và tránh duy trì hai mutation route không có chính sách deprecation.
2. **HTTP semantics mâu thuẫn**: timeline Review yêu cầu arrival trước `IN_PROGRESS` là **422**, nhưng handler hiện dùng `CodedConflictException` (409) và technical context chỉ nêu precondition. Cần chốt status/error code canonical (đề xuất: `422 TRIP_NOT_IN_PROGRESS`) trước khi implement/test.
3. **Recipient Incident**: BSOT registry chỉ liệt kê `{ incidentId, tripId, category, reporterUserId }`, trong khi Notification mapper Trip hiện đòi `userId`/`userIds`/`recipientUserIds`. Chốt Notification sẽ resolve active `OPERATOR_ADMIN`/`OPERATOR_STAFF` từ `operatorId` bằng Identity internal API, hay Trip phải publish snapshot recipient IDs. Không được phát event thiếu recipient làm consumer parse-fail.
4. **GPS nullability**: Timeline nói Incident kèm GPS, còn technical context và canonical schema cho `latitude`/`longitude` nullable. Chốt GPS bắt buộc ở API hay optional khi thiết bị không cấp quyền; không thay DB nullability nếu không có quyết định SOT.
5. **Incident Trip state**: SOT nói không auto-change `Trip.status`, nhưng không nói precondition Trip status cho report. Chốt cho phép report ở tất cả status trừ terminal hay chỉ `IN_PROGRESS`; không tự suy diễn vì ảnh hưởng audit và UX.
