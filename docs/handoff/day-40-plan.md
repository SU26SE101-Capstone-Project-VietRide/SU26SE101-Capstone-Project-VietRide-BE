# Day 40 - Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` -> Day 40 - Admin users + Station cleanup + Reports backend (Jira: SCV-122)
- **Prior checklist**: `docs/handoff/day-39-checklist.md` — `READY` (audit 2026-07-16); không còn blocker prerequisite cho Day 40.
- **Day 38 baseline**: `docs/handoff/day-38-checklist.md` PASS; `trip.trip.completed` và Booking completion consumer đã tồn tại.
- **Plan status**: APPROVED - PLAN-REVIEW 2026-07-15

## Objective

Day 40 cung cấp bộ API quản trị platform dành riêng cho `SYSTEM_ADMIN`: danh mục user, lock/unlock, ActivityLog bất biến, Station normalize/merge và báo cáo earned metrics toàn hệ thống. Thiết kế giữ ranh giới database giữa Identity, Trip, Booking, Parcel và Payment; mọi cross-service flow dùng Internal JWT hoặc Outbox/RabbitMQ. Day 40 tạo live indexed report baseline; Day 42 mới chuyển hot query sang Stats + Redis cache.

## Scope boundaries

### In scope

- `GET /v1/admin/users`, lock/unlock user và audit.
- `GET /v1/admin/activity-logs` cùng application/DB immutability.
- Mở rộng `PATCH /v1/admin/stations/{id}` hiện có để audit normalize mà không regression contract.
- `POST /v1/admin/stations/{primaryId}/merge` với canonical redirect và relink.
- Booking/Identity consumers của Station events.
- Booking/Trip/Parcel internal earned-report sources.
- Payment-owned `GET /v1/admin/reports/platform` orchestration.
- Gateway, Postman cumulative folder và isolated real-stack E2E.

### Out of scope

- Access-token denylist; auto-merge Station; Reporting Service mới.
- Cross-DB query/FK/write; rewrite Booking terminal/historical snapshot.
- Report cache, Excel export, occupancy/cancellation/no-show analytics.
- Notification cho lock/unlock/Station cleanup; Payment/refund mutation do Station merge.
- Dependency mới, secret, production credential hoặc thay đổi thuộc Day 41/42.

## Success criteria (DoD - binary, verifiable)

- [ ] Admin list/filter/page/sort user; không leak secret fields.
- [ ] Mọi password/Google login, refresh, forgot/reset password, failed-login/OTP-failure persistence và admin lock/unlock của cùng một User được tuyến tính hóa bằng row lock PostgreSQL; không có token/OTP/password/counter ghi từ snapshot stale.
- [ ] Lock revoke refresh tokens, chặn login/refresh/reset-password và ghi đúng một audit record cho mỗi logical idempotent request.
- [ ] Unlock reset DB + Redis lockout state, khôi phục đúng `lockedFromStatus`, không phục hồi token cũ hoặc vô tình verify email; race với failed login chỉ cho các outcome tuyến tính đã định nghĩa.
- [ ] ActivityLog query theo actor/action/date; direct SQL `UPDATE/DELETE` bị DB từ chối.
- [ ] Normalize Station giữ toàn bộ contract/slug behavior hiện có và phát audit event.
- [ ] Merge relink mọi Trip FK/OperatorStation atomically, không tạo redirect chain hay partial state.
- [ ] Booking có redirect Station bền vững; mọi luồng ghi Station persist canonical ID, Booking active relink eventual và Booking terminal giữ source ID/snapshot.
- [ ] Platform report trả earned metrics đúng UTC `[from,to)` và totals bằng `byOperator`.
- [ ] Gateway/RBAC/Swagger/Postman và real-stack E2E pass.

## Contract changes

### Admin users

`GET /v1/admin/users`

```text
search?
role?
status?
operatorId?
includeDeleted=false
page=1
pageSize=20
sortBy=createdAt
sortDir=desc
```

- `search`: case-insensitive trên email, displayName và phone.
- `pageSize` 1..100; sort allow-list: `createdAt,email,displayName,role,status`.
- `includeDeleted=false` giữ global query filter.
- `includeDeleted=true` dùng `IgnoreQueryFilters`; `status=DELETED` khi flag false trả empty page.
- Item: `id,email,displayName,phone,avatarUrl,role,status,operatorId,createdAt,updatedAt,deletedAt?`.
- Không trả password hash, OAuth subject, token hay failed-login internals.

`POST /v1/admin/users/{userId}/lock`

- Body rỗng; bắt buộc `Idempotency-Key`; cấm self-lock (`403 FORBIDDEN`).
- Manual admin lock chỉ cho `ACTIVE -> LOCKED` và set `lockedFromStatus=ACTIVE`; nếu target đã `LOCKED` thì command vẫn là `200` ensure-locked với `statusChanged=false`, giữ nguyên `lockedFromStatus`, revoke mọi refresh token còn active và audit request này. Các trạng thái khác trả `USER_INVALID_STATUS_TRANSITION`.
- Response: `userId,status:"LOCKED",statusChanged`.
- Lock/recheck User, update status khi cần, revoke mọi active refresh token với `ADMIN_REVOKE`, insert `LOCK_USER` ActivityLog trong một DB transaction.
- Audit dùng `ActivityLog.userId=callerAdminId`; metadata allow-list `targetUserId,previousStatus,newStatus,statusChanged`, không nhét User/request/token payload.
- Không thêm access-token denylist; access token cũ tồn tại tới expiry.

`POST /v1/admin/users/{userId}/unlock`

- Body rỗng; bắt buộc `Idempotency-Key`; cấm self-unlock (`403 FORBIDDEN`) để access token cũ của admin đang `LOCKED` không tự đảo trạng thái trong thời gian token còn hạn.
- `LOCKED -> lockedFromStatus`; giá trị hợp lệ là `ACTIVE` hoặc `PENDING_EMAIL_VERIFICATION`. Reset `failedLoginAttempts`, `lastFailedLoginAt` và clear `lockedFromStatus` trong cùng transaction. Missing/invalid origin là invariant violation, không mặc định promote thành `ACTIVE`.
- Sau khi giữ row lock User, xóa Redis `identity:login_lockout:{userId}` rồi mới đổi DB state; Redis failure rollback command và User vẫn `LOCKED`. Nếu DB commit fail sau Redis reset, DB `LOCKED` vẫn là nguồn chặn login; retry unlock sẽ reset lại Redis.
- Insert `UNLOCK_USER`; refresh token đã revoke không được phục hồi.
- Response: `userId,status,statusChanged:true`; `status` là trạng thái được restore, không hardcode `ACTIVE`.
- Audit dùng actor/metadata policy giống lock.

Errors: `RESOURCE_NOT_FOUND`, `FORBIDDEN`, `USER_INVALID_STATUS_TRANSITION`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`, `IDEMPOTENCY_REQUEST_PENDING`.

Lock/unlock và Station merge bắt buộc dùng shared `VietRide.Shared.Web.Idempotency.RequireIdempotencyAttribute`; no-body endpoints set `AllowRequestBody=false`. Không gọi controller-local `RequireIdempotencyKey()`/Trip legacy filter cho ba endpoint này. Shared middleware là SOT cho reservation/replay: missing header -> `422 IDEMPOTENCY_KEY_REQUIRED`, same key + khác fingerprint -> `422 IDEMPOTENCY_KEY_MISMATCH`, concurrent same request đang giữ processing reservation -> `409 IDEMPOTENCY_REQUEST_PENDING`, completed replay trả nguyên status/body và không dispatch MediatR lần hai. Task 40.1 sở hữu seam/tests shared này; Task 40.4 phụ thuộc 40.1 và chỉ consume attribute đã freeze.

#### Identity per-user serialization protocol

Mọi path có thể đọc `User.status` rồi phát token hoặc ghi auth state phải dùng cùng một protocol; không được chỉ thêm lock vào admin endpoint:

```text
password Login (existing User)
GoogleLogin (linked User hoặc match email đã tồn tại)
Refresh
ForgotPassword
ResetPassword + password-reset OTP failure persister
FailedLoginPersister
Admin LockUser / UnlockUser
```

1. Lookup email/OAuth subject/token hash trước serialized write chỉ là routing hint để tìm `userId`; không được phát token hoặc quyết định final status từ snapshot này. Password hash có thể verify sơ bộ ngoài row lock để tránh giữ lock khi bcrypt chạy.
2. Mở PostgreSQL transaction và `SELECT users ... FOR UPDATE` theo `userId`. Nếu một operation cần nhiều User thì lock theo canonical UUID lowercase format `D`, ordinal ascending.
3. Re-read và recheck `User.status` trên row đã lock. Repository `GetUserForUpdate` phải force SQL/reload hoặc detach snapshot đã track; không được để EF identity map trả lại entity cũ mà không query DB.
4. Canonical lock order sau User là: `EmailVerificationToken` theo UUID ascending -> refresh-token row/family theo canonical UUID string ordinal ascending -> ActivityLog/Outbox insert. Không path nào được lock token trước rồi quay lại lock User.
5. Password login sai không được giữ row lock ở ambient Login transaction khi gọi fresh-scope persister, tránh self-deadlock; persister là linearization point của failed attempt. Password login đúng phải lock/reload User rồi verify lại password hash trên entity đã lock trước khi update `lastLoginAt`, reset DB/Redis state và insert refresh token. Google login hiện hữu làm status recheck/token insert dưới cùng row lock. Chỉ return access/refresh token sau DB commit.
6. Refresh handler delegate toàn bộ mutation cho fresh-scope `IRefreshSessionExecutor`; ambient handler không giữ User/token lock. Executor lookup token-hash làm hint, mở transaction, lock User trước, re-read presented RefreshToken `FOR UPDATE`, recheck owner/revocation/expiry/status rồi rotate hoặc revoke family theo reuse rule và commit trước khi trả outcome. Existing `IRefreshTokenFamilyRevoker` phải được refactor thành same-context helper/delegate của executor, không được mở scope thứ hai sau khi executor đã lock User. Handler chỉ throw `AUTH_TOKEN_INVALID` sau khi executor đã commit outcome; vì vậy reuse revocation không rollback theo exception và không self-deadlock.
7. Forgot-password chỉ dùng lookup email trước transaction như routing hint. Với User hiện hữu, executor lock/reload User; chỉ `ACTIVE` mới được revoke/tạo `PASSWORD_RESET` OTP và Outbox trong cùng transaction. Lock-first trả generic success nhưng không OTP/Outbox; forgot-first có thể phát OTP, nhưng reset sau lock vẫn bị từ chối.
8. Reset-password và password-reset OTP failure dùng fresh executor chung: lock/reload User trước, yêu cầu `ACTIVE`, sau đó lock/re-read OTP row, validate/mark-used hoặc increment failed attempts, đổi password và revoke refresh token trong cùng transaction. Lock-first không consume OTP, không đổi password và không revoke/restore token; reset-first commit xong thì admin lock chạy sau và giữ final `LOCKED`.
9. `FailedLoginPersister` đổi contract thành `PersistAsync(userId)`; trong fresh transaction nó lock và reload User, chỉ xử lý status cho phép password login (`ACTIVE`, hoặc passenger `PENDING_EMAIL_VERIFICATION`), increment Redis dưới row lock rồi gọi `RecordFailedLogin` trên entity vừa reload. Khi threshold đạt, `User` set `lockedFromStatus` bằng status vừa reload trước khi chuyển `LOCKED`. Không gọi `DbSet.Update` với entity/snapshot từ Login handler; nếu User đã `LOCKED` hoặc không còn login-eligible thì no-op DB/Redis.
10. Google account chưa tồn tại không có row để lock; unique email/OAuth constraints quyết định create race. Sau khi create thành công, token issue vẫn nằm trong transaction sở hữu row mới.

Schema Identity thêm `users.locked_from_status user_status NULL`, backfill row `LOCKED` cũ thành `ACTIVE`, check chỉ nhận `ACTIVE|PENDING_EMAIL_VERIFICATION` và check `status=LOCKED` khi và chỉ khi origin khác null. Manual lock chỉ ghi `ACTIVE`; password lockout có thể ghi `ACTIVE` hoặc `PENDING_EMAIL_VERIFICATION`. Task 40.0 cập nhật state machine để pending passenger có nhánh password-lockout `PENDING_EMAIL_VERIFICATION -> LOCKED -> PENDING_EMAIL_VERIFICATION`; verify email vẫn là đường duy nhất lên `ACTIVE` từ pending.

Linearized outcomes bắt buộc:

- Lock vs successful password/Google login/refresh: nếu auth commit trước, lock commit sau phải revoke refresh token vừa tạo/rotate; access token đã trả vẫn sống tới expiry. Nếu lock commit trước, auth path recheck thấy `LOCKED` và không tạo/rotate token.
- Lock vs failed login: nếu lock commit trước, persister thấy `LOCKED` và no-op. Nếu failed-login commit trước, admin command chạy sau; kể cả attempt đó vừa auto-lock, ensure-locked vẫn revoke refresh tokens và ghi đúng một `LOCK_USER` audit cho logical request.
- Unlock vs failed login: nếu failed-login commit trước, unlock chạy sau và restore đúng origin (`ACTIVE` hoặc `PENDING_EMAIL_VERIFICATION`) với DB/Redis clean. Nếu unlock commit trước, failed attempt chạy sau được tính là attempt mới trên status vừa restore; không outcome nào đổi pending-email thành active nếu chưa verify.
- Không outcome nào được phục hồi refresh token đã `ADMIN_REVOKE`; không bổ sung access-token denylist ở Day 40.

### Activity logs

`GET /v1/admin/activity-logs`

```text
userId?   # actor
action?
from?
to?
page=1
pageSize=20
```

- RFC 3339 UTC `[from,to)`; default `createdAt DESC,id DESC`.
- Item: `id`, actor summary, `action`, `metadata`, `ipAddress`, `userAgent`, `createdAt`.
- Metadata không chứa password, OTP hay token.
- `IActivityLogRepository` chỉ Add/read; PostgreSQL trigger chặn mọi `UPDATE/DELETE`.
- Schema thêm `source_event_id UUID NULL`, partial unique index và global `(created_at DESC,id DESC)` index.

### Existing Station PATCH baseline

`AdminStationsController` và `PATCH/DELETE /v1/admin/stations/{id}` đã tồn tại. Day 40 mở rộng implementation hiện có, không tạo controller song song và không thu hẹp request.

PATCH tiếp tục hỗ trợ:

```text
name, addressStreet, locationId, city, province,
latitude, longitude, contactPhone, contactEmail,
operatingHours, facilities, supportsShuttle, isActive
```

- Giữ deterministic slug hiện tại từ `name + city + province`.
- Slug trùng dùng station-ID hash suffix; không thêm `STATION_SLUG_CONFLICT`.
- Ít nhất một field; coordinates gửi theo cặp và đúng range.
- Không normalize Station đã merged.
- Update + `trip.station.normalized` Outbox trong một transaction.

### Station merge

`POST /v1/admin/stations/{primaryStationId}/merge`

```json
{ "duplicateId": "uuid" }
```

- `SYSTEM_ADMIN`; bắt buộc `Idempotency-Key`.
- Primary active/non-deleted/chưa merged; duplicate non-deleted/chưa merged; IDs khác nhau.
- Lock hai Station theo UUID ascending; recheck preconditions sau lock.

Primary profile wins: `name,slug,city,province`.

Nullable profile fields giữ primary; chỉ lấp từ duplicate khi primary null: `addressStreet,locationId,contactPhone,contactEmail,operatingHours,facilities`. Coordinates merge như một cặp. `supportsShuttle = primary OR duplicate`.

Relink trong một Trip DB transaction:

```text
OperatorStation.stationId
Route.originStationId
Route.destinationStationId
AlternativeRoute.destinationStationId
ShuttleTrip.stationId
Station.mergedIntoStationId của redirect cũ
```

OperatorStation collision:

- Giữ primary row; `isActive = primary OR duplicate`.
- Chỉ lấp nullable config primary từ duplicate khi primary null.
- Xóa duplicate mapping sau merge; không vi phạm unique `(operatorId,stationId)`.

Preflight từ chối merge nếu Route thành origin=destination hoặc domain invariant fail. Trả `409 STATION_MERGE_CONFLICT`; không partial relink/soft-delete/Outbox.

Finalize duplicate:

```text
isActive=false
deletedAt=now
mergedIntoStationId=primaryId
```

Schema thêm self-FK nullable `merged_into_station_id REFERENCES stations(id) ON DELETE RESTRICT`, check khác chính nó và partial lookup index `WHERE merged_into_station_id IS NOT NULL`. Redirect cũ trỏ duplicate được flatten trực tiếp sang primary.

Response gồm primary Station, duplicate ID và counts: operator mappings, collapsed mappings, route origins/destinations, alternative routes, shuttle trips, flattened redirects.

### Internal Station resolution

`GET /internal/v1/stations/{id}` dùng internal DTO riêng:

1. Active canonical -> `200`, `isMerged=false`, `canonicalStationId=id`.
2. Soft-deleted do merge, có redirect -> `200`, original identity fields, `isMerged=true`, `canonicalStationId=target`.
3. Soft-deleted thông thường, không redirect -> `404 STATION_NOT_FOUND`.
4. ID không tồn tại -> `404 STATION_NOT_FOUND`.

Chỉ case 2 dùng `IgnoreQueryFilters`. Public Station search/DTO không expose deleted Station.

### Station events

`trip.station.merged`:

```text
eventId, occurredAt, eventType,
actorUserId, ipAddress?, userAgent?,
primaryStationId, duplicateStationId,
primaryBefore, duplicateBefore, primaryAfter,
relinkedCounts
```

`trip.station.normalized`:

```text
eventId, occurredAt, eventType,
actorUserId, ipAddress?, userAgent?,
stationId, before, after
```

Snapshots chỉ gồm allow-list Station fields và không chứa contact phone/email. `ipAddress/userAgent` chỉ dành cho immutable ActivityLog; structured operational logs không được in full event payload, phone, email, IP hoặc user-agent. Trip Outbox commit cùng Station transaction. Booking và Identity dùng durable queues riêng.

### Booking Station merge consumer

- Consume `trip.station.merged` bằng durable queue `booking.station-merged`.
- Booking DB thêm `booking_station_redirects` làm cả redirect graph cục bộ và durable event marker:

```text
duplicate_station_id UUID PRIMARY KEY
canonical_station_id UUID NOT NULL
source_event_id UUID NOT NULL UNIQUE
occurred_at TIMESTAMPTZ NOT NULL
created_at TIMESTAMPTZ NOT NULL
updated_at TIMESTAMPTZ NOT NULL
CHECK duplicate_station_id <> canonical_station_id
INDEX (canonical_station_id)
```

- Không có cross-DB FK. Một `duplicate_station_id` chỉ được merge một lần; same `source_event_id` replay ACK, nhưng same duplicate với event/target khác là poison conflict và không được mark processed.
- Canonical resolver follow redirect tối đa 32 hop với visited set. Cycle, self-target hoặc chain quá giới hạn làm transaction rollback, structured error và RabbitMQ retry/DLQ; không insert marker và không update Booking.
- Event có thể đến out-of-order. `A -> B` sau `B -> C` phải persist `A -> C`; `B -> C` sau `A -> B` phải flatten cả `A` và `B` trực tiếp tới `C`. Mọi row redirect cũ giữ `source_event_id` gốc khi flatten.

#### Booking Station serialization protocol

- Lock namespace dùng PostgreSQL transaction advisory lock: `pg_advisory_xact_lock(hashtextextended('booking-station:' || stationId::text, 0))`. UUID luôn sort theo lowercase format `D`, ordinal ascending trước khi gọi; hash collision chỉ gây over-serialization, không làm mất mutual exclusion.
- Consumer pre-read redirect graph để lấy `{primary,duplicate}`, các node trên primary path và mọi alias hiện resolve tới duplicate; acquire toàn bộ lock set theo UUID ascending, re-read graph dưới lock. Nếu graph mới cần thêm ID chưa lock thì rollback/retry transaction tối đa 3 lần; sau đó NACK transient để queue retry. Điều này giữ lock order nhất quán giữa các event chain đồng thời.
- Trong transaction ổn định: resolve primary canonical, upsert duplicate redirect, flatten aliases, bulk-update Booking active và commit redirect/event marker cùng nhau.
- Bulk update pickup/dropoff mọi alias trong merge set -> canonical chỉ cho `PENDING_PAYMENT` và `CONFIRMED`.
- `CreateBooking` và `CreateRoundTripBooking` collect union mọi Station ID distinct từ request **và Trip snapshot vừa fetch** (origin, destination và Station-valued pickup/dropoff candidates), acquire cùng advisory locks UUID ascending, rồi canonicalize cả request lẫn snapshot dưới lock trước mọi equality/domain validation. Chỉ canonical request IDs được persist; display-name/departure snapshot lịch sử vẫn lấy từ Trip snapshot và không bị rewrite.
- `EditPickup` và `EditDropoff` collect union Station ID mới, Station ID hiện tại trên Booking và mọi Station ID liên quan trong Trip snapshot, acquire lock/canonicalize cả hai phía, sau đó lock/reload Booking row `FOR UPDATE` trước mutation. Handler không được `Update` một aggregate snapshot stale; chỉ entity vừa reload mới được đổi pickup/dropoff. Edit sang Stop vẫn lock/reload Booking và canonicalize Trip snapshot station IDs dùng trong validation.
- `rg` hiện xác nhận chỉ bốn path trên ghi `pickup_station_id/dropoff_station_id`; architecture/integration test phải fail nếu có station-writing path mới bypass canonicalizer.
- Marker-before-inflight-commit gap được đóng bằng lock chung: writer commit trước thì consumer đợi và relink row vừa tạo/edit; consumer commit trước thì writer đợi, đọc redirect bền vững và persist canonical ID. Sau khi cả hai hoàn tất, không có active Booking trỏ duplicate.

- Không update `COMPLETED,EXPIRED,CANCELLED,NO_SHOW,PARTIAL_NO_SHOW,REFUNDED,DISRUPTED`.
- `booking_shuttle_intents` không lưu Station ID riêng; không cần relink.
- Không publish Payment/refund event.

### Identity Station audit consumer

- `trip.station.merged -> STATION_MERGED`.
- `trip.station.normalized -> STATION_NORMALIZED`.
- `ActivityLog.userId=actorUserId`, `source_event_id=eventId`.
- `ipAddress/userAgent` từ event được persist vào cột audit tương ứng khi có; không copy vào metadata.
- Atomic `INSERT ... ON CONFLICT DO NOTHING`; duplicate ACK.
- Missing actor -> structured error + shared DLQ; không tạo fake User.

### Platform report

`GET /v1/admin/reports/platform?from=&to=`

- `SYSTEM_ADMIN`; RFC 3339 UTC; bắt buộc cả hai; `from < to`; tối đa 366 ngày.
- Payment sở hữu public orchestration nhưng không đọc foreign DB.

Earned metrics:

```text
Booking: status=COMPLETED, anchor completed_at,
         completedBookingCount, SUM(total_amount)
Trip:    status=COMPLETED, anchor completed_at,
         completedTripCount
Parcel:  status=DELIVERY_CONFIRMED, anchor confirmed_at,
         deliveredParcelCount,
         SUM(deposit_amount + additional_amount - refund_amount)
```

Không dùng Payment ledger time, Booking CONFIRMED, Parcel paid/loaded hoặc stale Stats counters.

Response:

```text
period { from,to,timezone:"UTC" }
totals {
  completedBookingCount, completedTripCount, deliveredParcelCount,
  bookingRevenueVnd, parcelRevenueVnd, netRevenueVnd
}
byOperator[] { operatorId,operatorName?, same metrics }
generatedAt
```

`netRevenueVnd = bookingRevenueVnd + parcelRevenueVnd`. `byOperator` là union IDs từ ba sources, sort revenue desc rồi operator ID. Totals phải bằng sum breakdown.

PostgreSQL `SUM(BIGINT)` được đọc dưới dạng `NUMERIC`; Booking/Parcel source kiểm tra từng group và total nằm trong `Int64` trước khi map DTO. Payment aggregate dùng checked `long` cho mọi count/revenue/totals. Overflow ở source hoặc orchestrator trả `500 REPORT_VALUE_OVERFLOW`, không wrap, saturate hoặc trả partial; Task 40.0 đăng ký code này. `parcelRevenueVnd` và `netRevenueVnd` là signed BIGINT, không clamp về 0: refund snapshot lớn hơn deposit + additional tạo metric âm hợp lệ và totals vẫn phải bằng sum breakdown.

### Internal report contracts

```text
GET /internal/v1/reports/platform/bookings?from=&to=
{ items:[{operatorId,completedBookingCount,bookingRevenueVnd}] }

GET /internal/v1/reports/platform/trips?from=&to=
{ items:[{operatorId,completedTripCount}] }

GET /internal/v1/reports/platform/parcels?from=&to=
{ items:[{operatorId,deliveredParcelCount,parcelRevenueVnd}] }

POST /internal/v1/operators/summaries/batch
{ operatorIds:[...] }
```

- Internal JWT only.
- Identity accepts tối đa 500 distinct non-empty IDs; empty -> empty list; response sort ID ascending.
- Include soft-deleted operator được request.
- Read-only POST không yêu cầu idempotency; BSOT ghi explicit exception.
- Payment gọi ba report APIs song song, timeout 5 giây; lookup Identity theo chunks 500.
- Missing operator giữ metrics với `operatorName=null` và warning.
- Upstream `500 REPORT_VALUE_OVERFLOW` được Payment nhận diện và propagate thành cùng `500 REPORT_VALUE_OVERFLOW`; mọi upstream unavailable/timeout/5xx khác hoặc payload unusable -> `502 UPSTREAM_UNAVAILABLE`. Không case nào trả partial response/cache/Payment DB write.

### Report indexes

```text
Booking: (completed_at,operator_id)
         WHERE status='COMPLETED' AND completed_at IS NOT NULL
Trip:    (completed_at,operator_id)
         WHERE status='COMPLETED' AND completed_at IS NOT NULL
Parcel:  (confirmed_at,operator_id)
         WHERE status='DELIVERY_CONFIRMED' AND confirmed_at IS NOT NULL
```

Mỗi index có reversible EF migration và canonical DDL sync.

### Gateway routes

```text
/v1/admin/users            -> Identity
/v1/admin/activity-logs    -> Identity
/v1/admin/stations         -> Trip
/v1/admin/reports/platform -> Payment
```

Gateway chỉ proxy/RBAC, `SYSTEM_ADMIN` only; internal routes không public; longest-prefix tests bắt buộc.

## Tasks

### Task 40.0 - Contract/SOT baseline
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `BACKEND_SOURCE_OF_TRUTH.md`; `VietRide_API_Contract_v1.md`; `SU26SE101_VIETRIDE_technical_context_v7.md`; `BE_TIMELINE_VU.md` |
| forbidden scope | Service code/schema/migration, Gateway implementation, dependencies, `.env`, secrets, git ops |
| depends on | Day 39 audit/checklist |
| invariant flags | `/v1`; ADR 0004; registry sync; no cross-DB; docs LF/Vietnamese where under `docs/` |
| acceptance | Exact public/internal contracts, `lockedFromStatus` state machine, `REPORT_VALUE_OVERFLOW`, shared idempotency/event/error registries, Identity per-user serialization/linearized outcomes, Booking redirect DDL/advisory-lock/canonicalization protocol, Day 42 deferral và changelog đều được freeze; PLAN-REVIEW passes |
| source citations | Timeline 401-408; technical context 600-614, 2168-2205; BSOT 5.6/5.9/7.2/7.3 |

### Task 40.1 - Identity admin user directory/lifecycle
| Field | Value |
|---|---|
| stack/owner | dotnet / Identity |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | `apps/identity/src/VietRide.Identity.Api/Controllers/AdminUsersController.cs`; `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListUsers/**`; `apps/identity/src/VietRide.Identity.Application/Features/Admin/LockUser/**`; `apps/identity/src/VietRide.Identity.Application/Features/Admin/UnlockUser/**`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/Login/**`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin/**`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/Refresh/**`; `apps/identity/src/VietRide.Identity.Application/Abstractions/IFailedLoginPersister.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/ILoginLockoutCounter.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/IRefreshSessionExecutor.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/IRefreshTokenFamilyRevoker.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IUserRepository.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IRefreshTokenRepository.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IOAuthIdentityRepository.cs`; `apps/identity/src/VietRide.Identity.Domain/Entities/User.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/UserRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/RefreshTokenRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OAuthIdentityRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/FailedLoginPersister.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/RedisLoginLockoutCounter.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/RefreshSessionExecutor.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/RefreshTokenFamilyRevoker.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Auth/LoginCommandHandlerTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/GoogleLoginCommandHandlerTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Auth/RefreshCommandHandlerTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/Security/FailedLoginPersisterTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/Security/RefreshSessionExecutorTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/AdminUsers/**`; `apps/identity/tests/VietRide.Identity.IntegrationTests/Api/AdminUsersEndpointsTests.cs`; `apps/identity/tests/VietRide.Identity.IntegrationTests/AdminUserLifecycleRaceTests.cs` |
| owned files (continued) | `apps/identity/src/VietRide.Identity.Application/Features/Auth/ForgotPassword/**`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/ResetPassword/**`; `apps/identity/src/VietRide.Identity.Application/Abstractions/IPasswordResetSessionExecutor.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/IOtpFailedAttemptPersister.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IEmailVerificationTokenRepository.cs`; `apps/identity/src/VietRide.Identity.Domain/Entities/EmailVerificationToken.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/UserConfiguration.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/EmailVerificationTokenRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/PasswordResetSessionExecutor.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/OtpFailedAttemptPersister.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/IdentityDbContext.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/*AddLockedFromStatus*`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs`; `db-schema/identity-user/schema.sql`; `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs`; `libs/dotnet/VietRide.Shared.Web/Idempotency/RequireIdempotencyAttribute.cs`; `libs/dotnet/VietRide.Shared.Web/Idempotency/IdempotencyFingerprint.cs`; `libs/dotnet/VietRide.Shared.Web/DependencyInjection/IdempotencyServiceCollectionExtensions.cs`; `tests/dotnet/VietRide.Shared.Web.UnitTests/Middleware/IdempotencyMiddlewareTests.cs`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Auth/{ForgotPasswordCommandHandlerTests.cs,ResetPasswordCommandHandlerTests.cs}`; `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/Security/PasswordResetSessionExecutorTests.cs`; `apps/identity/tests/VietRide.Identity.IntegrationTests/PasswordResetLifecycleRaceTests.cs` |
| forbidden scope | Access-token denylist/JWT format/JWKS/signing keys; đổi lockout threshold/window; regression endpoint create-admin hiện có trong `AdminUsersController`; Operator lifecycle; ActivityLog schema/read API thuộc 40.2; service khác; dependency/NuGet mới; `.env`, secret, git ops |
| depends on | 40.0 |
| invariant flags | `SYSTEM_ADMIN`; shared required idempotency; single per-user PostgreSQL serialization; lock order User -> EmailVerificationToken -> RefreshToken -> ActivityLog/Outbox; fresh locked entity for failed login/OTP failure; `lockedFromStatus`; `ADMIN_REVOKE`; Redis reset under row lock; no access-token denylist; CRLF |
| acceptance | List filters/includeDeleted/sort/page và secret-field exclusion pass; self-lock/self-unlock forbidden; ensure-locked semantics/shared-idempotency/revoke/audit pass; login/Google/refresh/forgot/reset không ghi auth state sau earlier lock commit; auth/reset trước lock có refresh token bị revoke; refresh reuse family revocation commit trước 401 và không deadlock; pending-email auto-lock rồi unlock vẫn pending; real-PostgreSQL barrier tests lock-vs-success-login, lock-vs-failed-login, unlock-vs-failed-login, lock-vs-forgot-password và lock-vs-reset-password chạy lặp ít nhất 50 vòng/case, assert chỉ linearized outcomes, không lost update/stale status/OTP/password write; migration up/down/reapply, shared idempotency unit tests và Identity build/format/test green |
| source citations | `BACKEND_SOURCE_OF_TRUTH.md:1451-1454,1674-1677,2074-2077`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/Login/LoginCommandHandler.cs:45-112`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin/GoogleLoginCommandHandler.cs:43-101`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/Refresh/RefreshCommandHandler.cs:44-104`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/ForgotPassword/ForgotPasswordCommandHandler.cs:53-92`; `apps/identity/src/VietRide.Identity.Application/Features/Auth/ResetPassword/ResetPasswordCommandHandler.cs:42-79`; `apps/identity/src/VietRide.Identity.Infrastructure/Security/FailedLoginPersister.cs:21-46`; `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs`; `db-schema/identity-user/schema.sql:148-244` |

### Task 40.2 - Immutable ActivityLog/read API
| Field | Value |
|---|---|
| stack/owner | dotnet / Identity |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint; ef-migration |
| owned files | `apps/identity/src/VietRide.Identity.Api/Controllers/AdminActivityLogsController.cs`; `apps/identity/src/VietRide.Identity.Api/Controllers/InternalOperatorsController.cs`; `apps/identity/src/VietRide.Identity.Application/Features/Admin/ListActivityLogs/**`; `apps/identity/src/VietRide.Identity.Application/Features/Internal/Operators/GetOperatorSummaries/**`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IActivityLogRepository.cs`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IOperatorRepository.cs`; `apps/identity/src/VietRide.Identity.Domain/Entities/ActivityLog.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/IdentityDbContext.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/ActivityLogConfiguration.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/ActivityLogRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Repositories/OperatorRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/*AddImmutableActivityLogReadModel*`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs`; `db-schema/identity-user/schema.sql`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/AdminActivityLogs/**`; `apps/identity/tests/VietRide.Identity.UnitTests/Application/Internal/Operators/GetOperatorSummaries/**`; `apps/identity/tests/VietRide.Identity.IntegrationTests/ActivityLogImmutabilityTests.cs` |
| forbidden scope | Sửa semantics auth/token/lockout ngoài compile fix cho interface 40.1; Station event consumer/action enum migration thuộc 40.6; ActivityLog update/delete API/repository; service khác; dependency mới; secret/git |
| depends on | 40.0, 40.1 |
| invariant flags | append-only app+DB; actor semantics; source-event idempotency; CRLF |
| acceptance | Query actor/action/UTC `[from,to)`/seek-stable sort/page/index pass; batch operator summaries tối đa 500, include soft-deleted và deterministic order; repository chỉ Add/read; direct PostgreSQL `UPDATE` và `DELETE` đều bị trigger từ chối; `source_event_id` partial unique; mọi existing ActivityLog writer và Identity build/format/test pass |
| source citations | `BE_TIMELINE_VU.md:403,408`; `SU26SE101_VIETRIDE_technical_context_v7.md:608-613`; `db-schema/identity-user/schema.sql:292-316`; `apps/identity/src/VietRide.Identity.Application/Abstractions/Repositories/IActivityLogRepository.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Persistence/Configurations/ActivityLogConfiguration.cs` |

### Task 40.3 - Station canonical persistence
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files | `apps/trip/src/VietRide.Trip.Domain/Entities/Station.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/OperatorStation.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/Route.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/AlternativeRoute.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/ShuttleTrip.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IStationRepository.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IOperatorStationRepository.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IRouteRepository.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IAlternativeRouteRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/StationConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/OperatorStationConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/RouteConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/AlternativeRouteConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/ShuttleTripConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/StationRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/OperatorStationRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/RouteRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/AlternativeRouteRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/*AddStationMergeRedirect*`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/TripDbContextModelSnapshot.cs`; `db-schema/trip-route-vehicle/schema.sql`; `apps/trip/tests/VietRide.Trip.UnitTests/Domain/StationMergeTests.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/StationMergePersistenceTests.cs` |
| forbidden scope | Controller/public DTO/event behavior thuộc 40.4; Booking/Identity DB; route/vehicle/shuttle behavior ngoài relink primitive; dependency mới; secret/git |
| depends on | 40.0 |
| invariant flags | self-FK redirect; soft-delete separate isActive; no redirect chain; CRLF |
| acceptance | Reversible migration add self-FK/check/index; domain/repository primitives lock/relink/flatten/collapse collision atomically; route origin-destination conflict preflight; migration up/down/reapply và existing Station/Route/Shuttle tests pass; no public behavior change trước 40.4 |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md:601-606`; `db-schema/trip-route-vehicle/schema.sql:76-141,167-274,560-597`; `apps/trip/src/VietRide.Trip.Domain/Entities/Station.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/OperatorStationConfiguration.cs` |

### Task 40.4 - Station normalize/merge APIs + Outbox
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint; add-integration-event |
| owned files | `apps/trip/src/VietRide.Trip.Api/Controllers/AdminStationsController.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/InternalStationsController.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/UpdateAdminStationRequest.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/MergeStationsRequest.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Stations/UpdateAdminStation*`; `apps/trip/src/VietRide.Trip.Application/Features/Stations/MergeStations/**`; `apps/trip/src/VietRide.Trip.Application/Features/Internal/Stations/**`; `apps/trip/src/VietRide.Trip.Application/Events/StationMergedIntegrationEvent.cs`; `apps/trip/src/VietRide.Trip.Application/Events/StationNormalizedIntegrationEvent.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IStationRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/StationRepository.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/Features/Stations/**`; `apps/trip/tests/VietRide.Trip.IntegrationTests/AdminStationMergeEndpointTests.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/InternalStationResolutionTests.cs` |
| owned files (continued) | `apps/trip/src/VietRide.Trip.Api/Filters/RequireIdempotencyKeyAttribute.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/Api/RequireIdempotencyKeyAttributeTests.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/SharedIdempotencyMiddlewareIntegrationTests.cs` |
| forbidden scope | Controller Station song song; thu hẹp PATCH fields hoặc đổi deterministic slug behavior; foreign DB/direct Booking write; schema ngoài 40.3; dependency mới; secret/git |
| depends on | 40.1, 40.3 |
| invariant flags | one transaction; deterministic locks; primary profile policy; Outbox; shared `RequireIdempotencyAttribute`; no controller-local reservation |
| acceptance | PATCH giữ toàn bộ field/validation/slug baseline và enqueue đúng một normalized event; merge dùng shared middleware, exact required/mismatch/pending/replay semantics, auth/profile/relink/collision/redirect flatten/conflict rollback pass; concurrent merge cùng Station chỉ một state hợp lệ; Outbox cùng transaction; internal canonical/deleted lookup đúng contract; build/format/test green |
| source citations | `BE_TIMELINE_VU.md:404-408`; `SU26SE101_VIETRIDE_technical_context_v7.md:601-606`; `apps/trip/src/VietRide.Trip.Api/Controllers/AdminStationsController.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/UpdateAdminStationRequest.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Stations/UpdateAdminStationHandler.cs` |

### Task 40.5 - Booking Station merge consumer
| Field | Value |
|---|---|
| stack/owner | dotnet / Booking |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | ef-migration |
| owned files | `apps/booking/src/VietRide.Booking.Domain/Entities/BookingStationRedirect.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingStationRedirectRepository.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Services/IBookingStationCanonicalizer.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditPickup/EditPickupCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditDropoff/EditDropoffCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/BookingDbContext.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/BookingStationRedirectConfiguration.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingStationRedirectRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Services/BookingStationCanonicalizer.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Messaging/StationMergedIntegrationEvent.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Messaging/StationMergedIntegrationEventHandler.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/*AddBookingStationRedirects*`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs`; `db-schema/booking/schema.sql`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateBookingCommandHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateRoundTripBookingCommandHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/EditPickupCommandHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/EditDropoffCommandHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Infrastructure/StationMergedIntegrationEventHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/StationWritingArchitectureTests.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/StationMergeSerializationTests.cs` |
| forbidden scope | Rewrite terminal Booking/snapshot; Payment/refund/outbox side effects; `booking_shuttle_intents` schema; Trip/Identity code; generic shared transaction behavior; dependency mới; secret/git |
| depends on | 40.0 |
| invariant flags | durable local redirect; advisory xact lock by sorted Station UUID; canonicalize every station write; active-only relink; redirect-as-marker + updates atomic; out-of-order flatten; cycle guard; at-least-once safe; CRLF |
| acceptance | Migration up/down/reapply; same event replay no-op; conflicting duplicate/cycle no marker/no update; A->B->C events in both orders/concurrently flatten to C; cả request và Trip snapshot được canonicalize dưới union lock; Create, round-trip, edit pickup và edit dropoff persist canonical IDs; real-PostgreSQL barrier đặt sau Trip snapshot fetch nhưng trước advisory lock, chạy consumer-vs-each writer ít nhất 50 iterations và luôn end với no false reject/no active Booking on duplicate/one durable marker/no terminal rewrite; architecture test enumerates all station-column writers; build/format/test green |
| source citations | `db-schema/booking/schema.sql:79-111`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs:188-210`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs:499-545`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditPickup/EditPickupCommandHandler.cs:88-103`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditDropoff/EditDropoffCommandHandler.cs:74-90`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs:71-112` |

### Task 40.6 - Identity Station audit consumer
| Field | Value |
|---|---|
| stack/owner | dotnet / Identity |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files | `apps/identity/src/VietRide.Identity.Domain/Enums/ActivityLogAction.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Messaging/StationMergedIntegrationEvent.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Messaging/StationNormalizedIntegrationEvent.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Messaging/StationMergedIntegrationEventHandler.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Messaging/StationNormalizedIntegrationEventHandler.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/IdentityDbContext.cs`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/*AddStationAuditActions*`; `apps/identity/src/VietRide.Identity.Infrastructure/Migrations/IdentityDbContextModelSnapshot.cs`; `db-schema/identity-user/schema.sql`; `apps/identity/tests/VietRide.Identity.UnitTests/Infrastructure/StationAuditEventHandlerTests.cs`; `apps/identity/tests/VietRide.Identity.IntegrationTests/StationAuditConsumerTests.cs` |
| forbidden scope | Notification/fake actor; admin ActivityLog query/trigger/source-event column thuộc 40.2; auth/token lifecycle; Trip/Booking code; dependency mới; secret/git |
| depends on | 40.0, 40.2 |
| invariant flags | source-event unique; shared DLQ; PII-safe logs |
| acceptance | Enum migration reversible/synced; merge/normalize map đúng action/actor/source event và persist audit IP/user-agent đúng cột khi có; event replay tạo đúng một immutable log; missing actor hoặc payload invalid không tạo fake row, không mark processed và đi shared retry/DLQ; structured operational logs không chứa phone/email/IP/user-agent/full payload; build/format/test green |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md:608-613`; `db-schema/identity-user/schema.sql:292-316`; `apps/identity/src/VietRide.Identity.Domain/Enums/ActivityLogAction.cs`; `libs/dotnet/VietRide.Shared.Messaging/` consumer retry/DLQ baseline |

### Task 40.7 - Booking earned-report source
| Field | Value |
|---|---|
| stack/owner | dotnet / Booking |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint; ef-migration |
| owned files | `apps/booking/src/VietRide.Booking.Api/Controllers/InternalPlatformReportsController.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Internal/Reports/PlatformBookings/**`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/BookingConfiguration.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/BookingDbContext.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/*AddCompletedBookingReportIndex*`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs`; `db-schema/booking/schema.sql`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Internal/Reports/PlatformBookings/**`; `apps/booking/tests/VietRide.Booking.IntegrationTests/InternalPlatformBookingReportTests.cs` |
| forbidden scope | `BookingStats` làm source Day 40; Station redirect/canonicalizer/consumer đã chốt ở 40.5 ngoài compile fix; public Booking API; service khác; dependency mới; secret/git |
| depends on | 40.5 |
| invariant flags | COMPLETED only; BIGINT; UTC boundaries; Internal JWT |
| acceptance | Internal JWT/RFC3339/range boundary pass; chỉ `COMPLETED` + `completed_at` trong `[from,to)`; PostgreSQL `SUM(BIGINT)` đọc `NUMERIC`, checked Int64 conversion cho từng group/total và exact `REPORT_VALUE_OVERFLOW` khi vượt range; seeded và live TripCompleted lifecycle tăng đúng một lần; `EXPLAIN` dùng partial index trên fixture đủ lớn; migration up/down/reapply và build/format/test green |
| source citations | `SU26SE101_VIETRIDE_technical_context_v7.md:2168-2205`; `db-schema/booking/schema.sql:79-127`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/TripEvents/HandleTripCompletedCommandHandler.cs`; `docs/handoff/day-38-checklist.md` |

### Task 40.8 - Trip completed-report source
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint; ef-migration |
| owned files | `apps/trip/src/VietRide.Trip.Api/Controllers/InternalPlatformReportsController.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Internal/Reports/PlatformTrips/**`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/TripRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/TripConfiguration.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/*AddCompletedTripReportIndex*`; `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/TripDbContextModelSnapshot.cs`; `db-schema/trip-route-vehicle/schema.sql`; `apps/trip/tests/VietRide.Trip.UnitTests/Features/Internal/Reports/PlatformTrips/**`; `apps/trip/tests/VietRide.Trip.IntegrationTests/InternalPlatformTripReportTests.cs` |
| forbidden scope | Station merge/normalize files ngoài compile fix sau 40.4; Trip lifecycle mutation; public Trip API; service khác; dependency mới; secret/git |
| depends on | 40.4 |
| invariant flags | COMPLETED only; UTC; Internal JWT; CRLF |
| acceptance | Internal auth/range/status/boundary/grouping pass; chỉ `COMPLETED` + `completed_at`; live manual/automatic completion không double-count row; `EXPLAIN` dùng partial index; migration up/down/reapply và build/format/test green |
| source citations | `db-schema/trip-route-vehicle/schema.sql:344-418`; `apps/trip/src/VietRide.Trip.Domain/Entities/Trip.cs`; `docs/handoff/day-38-checklist.md`; `BE_TIMELINE_VU.md:406-408` |

### Task 40.9 - Parcel earned-report source
| Field | Value |
|---|---|
| stack/owner | dotnet / Parcel |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint; ef-migration |
| owned files | `apps/parcel/src/VietRide.Parcel.Api/Controllers/InternalPlatformReportsController.cs`; `apps/parcel/src/VietRide.Parcel.Application/Features/Internal/Reports/PlatformParcels/**`; `apps/parcel/src/VietRide.Parcel.Application/Abstractions/Repositories/IParcelRepository.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Persistence/Repositories/ParcelRepository.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Persistence/Configurations/ParcelConfiguration.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/ParcelDbContext.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Migrations/*AddConfirmedParcelReportIndex*`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Migrations/ParcelDbContextModelSnapshot.cs`; `db-schema/parcel/schema.sql`; `apps/parcel/tests/VietRide.Parcel.UnitTests/Features/Internal/Reports/PlatformParcels/**`; `apps/parcel/tests/VietRide.Parcel.IntegrationTests/InternalPlatformParcelReportTests.cs` |
| forbidden scope | Parcel lifecycle/pricing/refund mutation; `ParcelStats` hoặc report cache làm source; public Parcel API; service khác; dependency mới; secret/git |
| depends on | 40.0 |
| invariant flags | DELIVERY_CONFIRMED only; BIGINT; UTC; Internal JWT |
| acceptance | Chỉ `DELIVERY_CONFIRMED` + `confirmed_at` trong `[from,to)`; công thức signed `deposit + additional - refund` không clamp, PostgreSQL NUMERIC SUM checked về Int64 và exact `REPORT_VALUE_OVERFLOW` khi vượt range; boundary/non-terminal/negative-net fixtures pass; `EXPLAIN` dùng partial index; migration up/down/reapply và build/format/test green |
| source citations | `db-schema/parcel/schema.sql:70-139`; `apps/parcel/src/VietRide.Parcel.Domain/Entities/Parcel.cs`; `apps/parcel/src/VietRide.Parcel.Infrastructure/Persistence/Configurations/ParcelConfiguration.cs`; `BE_TIMELINE_VU.md:406-408` |

### Task 40.10 - Payment platform-report orchestrator
| Field | Value |
|---|---|
| stack/owner | dotnet / Payment |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | `apps/payment/src/VietRide.Payment.Api/Controllers/AdminPlatformReportsController.cs`; `apps/payment/src/VietRide.Payment.Application/Features/Admin/PlatformReports/**`; `apps/payment/src/VietRide.Payment.Application/Abstractions/ExternalClients/IBookingPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Application/Abstractions/ExternalClients/ITripPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Application/Abstractions/ExternalClients/IParcelPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Application/Abstractions/ExternalClients/IIdentityOperatorSummaryClient.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/Http/BookingPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/Http/TripPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/Http/ParcelPlatformReportClient.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/Http/IdentityOperatorSummaryClient.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/Http/InternalJwtTokenFactory.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/payment/tests/VietRide.Payment.UnitTests/Features/Admin/PlatformReports/**`; `apps/payment/tests/VietRide.Payment.UnitTests/Infrastructure/Http/PlatformReportClients/**`; `apps/payment/tests/VietRide.Payment.IntegrationTests/AdminPlatformReportEndpointTests.cs` |
| forbidden scope | Payment DB report metric/write/migration; cache/Stats/Day 42; Gateway aggregation; service khác; dependency mới; secret/git |
| depends on | 40.2, 40.7, 40.8, 40.9 |
| invariant flags | parallel reads; Internal JWT; timeout; no partial; checked BIGINT |
| acceptance | SYSTEM_ADMIN/RFC3339/max-range validation; ba metric calls chạy song song với timeout 5 giây, Identity chunk 500; union/sort/totals/name-null/signed negative metrics đúng; local hoặc canonical upstream overflow trả exact `500 REPORT_VALUE_OVERFLOW`; upstream timeout/5xx khác/payload unusable trả canonical 502, không partial/cache/DB write; unit HTTP contract + integration RBAC + build/format/test green |
| source citations | `BE_TIMELINE_VU.md:406-408`; `AGENTS.md` Cross-DB FK/Internal JWT rules; `AGENTS_DOTNET.md` Internal JWT/ApiResponse/BIGINT rules; internal contracts đã freeze tại Task 40.0 |

### Task 40.11 - Gateway/Postman
| Field | Value |
|---|---|
| stack/owner | nest / Gateway |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files | `apps/gateway/src/config/routes.ts`; `apps/gateway/src/config/routes.spec.ts`; `apps/gateway/src/proxy/proxy.access-gates.spec.ts`; `apps/gateway/src/proxy/proxy.middleware.spec.ts`; `docs/api/postman/vietride.postman_collection.json`; `docs/api/postman/vietride.local.postman_environment.json` |
| forbidden scope | Aggregation/business logic trong Gateway; internal routes public; downstream service code; collection Postman riêng; dependency mới; secret/git |
| depends on | 40.1, 40.2, 40.4, 40.10 |
| invariant flags | SYSTEM_ADMIN; longest-prefix; LF; Swagger downstream canonical |
| acceptance | Exact method/path owner + longest-prefix pass; SYSTEM_ADMIN allow và mọi role khác deny; internal routes absent; cumulative Postman folder cover happy/validation/idempotency/RBAC/report failure with runtime variables; JSON LF; `npm run lint:ts`, `npx nx test gateway --ci --passWithNoTests` và `npx nx build gateway` green; black-box Gateway E2E thuộc 40.12 |
| source citations | `apps/gateway/src/config/routes.ts`; `apps/gateway/src/config/routes.spec.ts`; `apps/gateway/src/proxy/proxy.access-gates.spec.ts`; `AGENTS_NESTJS.md` Gateway reference implementation/route conventions |

### Task 40.12 - Real-stack E2E acceptance
| Field | Value |
|---|---|
| stack/owner | cross-stack |
| implement agent | worker |
| review agent | reviewer |
| skill | smoke-test |
| owned files | `infra/docker/docker-compose.day40-e2e.yml`; `infra/docker/day40-e2e/**`; `scripts/run-day40-admin-reports-e2e.mjs`; `package.json` |
| forbidden scope | Production bypass/mock DB/HTTP/in-memory repository; existing local developer volumes/data; production credentials; service business implementation; dependency/package-lock change; secret/git |
| depends on | 40.1, 40.2, 40.3, 40.4, 40.5, 40.6, 40.7, 40.8, 40.9, 40.10, 40.11 |
| invariant flags | isolated real PostgreSQL/Redis/RabbitMQ/API; cleanup finally |
| acceptance | Isolated stack lifecycle/health/migration/seed/JWT/API/Rabbit polling/direct persistence/cleanup pass; Identity và Booking race loops dùng PostgreSQL thật; no mock side effects; `npm run e2e:day40` exit 0 và in đủ summary bắt buộc |
| source citations | `BE_TIMELINE_VU.md:401-408`; `infra/docker/docker-compose.day38-e2e.yml`; `scripts/run-day38-invoice-settlement-e2e.mjs`; `.agents/skills/smoke-test/SKILL.md` |

## Dispatch order

```text
Day 39 audit
  -> 40.0
      |-> 40.1 -> 40.2 -> 40.6
      |-> 40.3
      |-> 40.5 -> 40.7
      `-> 40.9

40.1 + 40.3 -> 40.4 -> 40.8
40.2 + 40.7 + 40.8 + 40.9 -> 40.10
40.1 + 40.2 + 40.4 + 40.10 -> 40.11
40.1..40.11 -> 40.12
```

- 40.1, 40.3, 40.5 và 40.9 được mở khóa sau 40.0 và parallel-safe vì write set thuộc service khác nhau; 40.4 chờ cả shared idempotency seam 40.1 và Trip persistence 40.3.
- 40.7 bắt buộc sau 40.5 vì cùng Booking repository/DbContext/model snapshot; sau đó 40.7 và 40.9 parallel-safe.
- 40.8 không chạy song song 40.3/40.4 vì cùng Trip model snapshot.
- 40.6 chạy sau 40.2 để tránh cùng ActivityLog migration files.

## Real-stack E2E

Artifacts:

```text
infra/docker/docker-compose.day40-e2e.yml
scripts/run-day40-admin-reports-e2e.mjs
npm run e2e:day40
```

Services: PostgreSQL, Redis, RabbitMQ, Identity, Trip, Booking, Parcel, Payment, Gateway. Isolated compose project; startup migrations; UUID prefix `40000000-...`; short-lived dev JWT; no secret logging; `down -v` trong `finally`.

### Deterministic seed

- Identity: System Admin caller/target, active/locked/pending/deleted users, locked users có origin `ACTIVE` và `PENDING_EMAIL_VERIFICATION`, password-reset OTP/outbox fixtures, Operator A/B/deleted operator, refresh-token families, ActivityLogs, Redis lockout và user riêng cho từng race loop.
- Trip: primary/duplicate/prior redirect/ordinary deleted Station, mapping collision, Route/AlternativeRoute/ShuttleTrip refs, merge-conflict fixture, completed/non-completed trips tại boundaries.
- Booking: active bookings trỏ duplicate, terminal historical bookings, redirect chains A/B/C, cycle-poison IDs, Create/round-trip/edit race fixtures, completed/non-completed report fixtures; không seed output redirect/marker cần kiểm thử.
- Parcel: delivered/non-terminal fixtures, deposit/additional/refund combinations và boundary timestamps.
- Payment: startup prerequisites only; không seed report metrics.

### Black-box scenarios

1. User list filter/paging/sort/includeDeleted/RBAC.
2. Lock/revoke/login/refresh/replay/self-lock/self-unlock.
3. Repeated Identity race trên cùng user bằng barrier đồng thời: lock-vs-successful password login, lock-vs-Google login và lock-vs-refresh. Chấp nhận auth-first rồi refresh bị `ADMIN_REVOKE`, hoặc lock-first rồi không có token mới; không chấp nhận token issue sau earlier lock commit.
4. Repeated Identity race lock-vs-failed-login và unlock-vs-failed-login. Assert đúng outcome tuyến tính, Redis/DB counter khớp, `FailedLoginPersister` không ghi stale status; pending-email auto-lock/unlock restore `PENDING_EMAIL_VERIFICATION`, không promote `ACTIVE`.
5. Repeated lock-vs-forgot-password và lock-vs-reset-password bằng barrier. Lock-first không OTP/Outbox/password change/token revoke; password-flow-first commit hợp lệ nhưng lock sau giữ final `LOCKED` và revoke token.
6. Unlock DB+Redis reset/new login; token đã revoke không sống lại.
7. Concurrent replay lock/unlock và Station merge qua shared idempotency: exact required/mismatch/pending codes, một MediatR dispatch và byte-equivalent completed replay.
8. Activity query + direct SQL immutability.
9. Normalize giữ fields/slug behavior + one audit event.
10. Merge profile/all Trip refs/redirect flatten/audit.
11. Booking consumer tạo durable redirect; active relink, terminal history preserved; duplicate event chỉ một redirect/side effect.
12. Out-of-order/parallel `A -> B`, `B -> C` ở cả hai thứ tự; assert `A -> C`, `B -> C`, không chain/cycle. Cycle-poison event rollback và không marker.
13. Repeated consumer-vs-`CreateBooking`, consumer-vs-`CreateRoundTripBooking`, consumer-vs-`EditPickup`, consumer-vs-`EditDropoff`; barrier đặt sau stale Trip snapshot fetch, trước advisory lock. Canonicalize request + snapshot, không false reject và không active Booking trỏ duplicate.
14. Ordinary deleted lookup 404; merged lookup canonical 200.
15. Trip merge conflict/concurrency/idempotency.
16. Earned report boundaries/totals/operator breakdown, signed negative Parcel net giữ nguyên và source/orchestrator overflow trả `REPORT_VALUE_OVERFLOW`.
17. Live lifecycle: create Booking, complete Trip qua API, poll Booking COMPLETED, report tăng một lần; replay completion event không double-count.
18. Range/RBAC validation.
19. Stop Parcel -> 502/no partial; restart -> success.
20. Duplicate Station events -> một Booking redirect/marker và một Identity log.

### Direct persistence assertions

```text
Identity: users.locked_from_status, refresh_tokens,
          email_verification_tokens, activity_logs, outbox_events
Trip: stations, operator_stations, routes,
      alternative_routes, shuttle_trips, outbox_events
Booking: bookings, booking_station_redirects
Parcel: parcels
Redis: login lockout + idempotency keys
RabbitMQ: Outbox PUBLISHED + side effects + no duplicates
```

## Verification gate

```text
dotnet build apps/identity/VietRide.Identity.sln -c Release
dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes
dotnet test apps/identity/VietRide.Identity.sln -c Release
dotnet build apps/trip/VietRide.Trip.sln -c Release
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes
dotnet test apps/trip/VietRide.Trip.sln -c Release
dotnet build apps/booking/VietRide.Booking.sln -c Release
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes
dotnet test apps/booking/VietRide.Booking.sln -c Release
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes
dotnet test apps/parcel/VietRide.Parcel.sln -c Release
dotnet build apps/payment/VietRide.Payment.sln -c Release
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes
dotnet test apps/payment/VietRide.Payment.sln -c Release
dotnet test tests/dotnet/VietRide.Shared.Web.UnitTests/VietRide.Shared.Web.UnitTests.csproj -c Release
npm run lint:ts
npx nx test gateway --ci --passWithNoTests
npx nx build gateway
migration up/down/reapply
Identity real-PostgreSQL race suite (>=50 iterations/case)
Identity password-reset/locked-origin race suite (>=50 iterations/case)
Booking Station serialization race suite (>=50 iterations/writer)
npm run e2e:day40
```

Required E2E summary:

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

## Deploy order

1. Apply Identity, Trip, Booking và report-index migrations.
2. Deploy Identity audit consumer và Booking merge consumer; verify durable bindings.
3. Deploy Trip Station producer/endpoints.
4. Deploy Booking/Trip/Parcel report sources.
5. Deploy Payment orchestrator.
6. Deploy Gateway routes.
7. Run smoke matrix.
8. Rollback application reverse order; giữ additive columns/indexes khi app rollback để không mất redirect/audit data.

## Progress tracker

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 40.0 | todo | - | - | Ready to dispatch sau khi user `ok/go` |
| 40.1 | todo | - | - | Identity users |
| 40.2 | todo | - | - | ActivityLog |
| 40.3 | todo | - | - | Station persistence |
| 40.4 | todo | - | - | Station APIs/events |
| 40.5 | todo | - | - | Booking consumer |
| 40.6 | todo | - | - | Identity consumer |
| 40.7 | todo | - | - | Booking report |
| 40.8 | todo | - | - | Trip report |
| 40.9 | todo | - | - | Parcel report |
| 40.10 | todo | - | - | Payment report |
| 40.11 | todo | - | - | Gateway/Postman |
| 40.12 | todo | - | - | Real-stack E2E |

## Open questions

Không còn open question. Mọi contract, ownership, metric, Station profile/collision policy, historical-data rule, failure behavior, verification và deploy order đã được khóa.
