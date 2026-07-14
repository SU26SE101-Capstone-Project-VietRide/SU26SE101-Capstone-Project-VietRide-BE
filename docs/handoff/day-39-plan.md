# Day 39 — Driver Ops Incident + TripStop/Terminal Arrival, Revision 3

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 39, SCV-120.
- **Prior checklist**: `docs/handoff/day-38-checklist.md` — `READY`; kế thừa Trip locking/Outbox, Gateway explicit route và Notification recipient provider đã hoàn tất ở Day 38.
- **Plan status**: APPROVED — PLAN-REVIEW + SOT re-review thông qua ngày 2026-07-14; chưa cấp quyền triển khai.

## Mục tiêu và phạm vi

Triển khai vertical slice báo sự cố cho Driver/Assistant trên Trip đang `IN_PROGRESS`: lưu Incident trong Trip DB, ghi Outbox cùng transaction, resolve đúng active `OPERATOR_ADMIN` và tạo in-app/push notification. Sửa TripStop arrival về canonical Driver API, assignment-scope và concurrency-safe để chỉ một request chuyển `PENDING -> ARRIVED`. Bổ sung arrival anchor riêng cho destination terminal vì express Trip hợp lệ có thể không có `TripStop`; cả hai loại anchor phải mở khóa Parcel UNLOAD thật.

Day 39 đồng thời harden shared Redis idempotency vì middleware hiện chỉ hash body, không reserve request đang xử lý và có thể replay nhầm hai arrival body rỗng ở path khác nhau. Sửa đúng seam Parcel liên quan để giữ canonical `IN_TRANSIT -> UNLOADED -> DELIVERED_PENDING_CONFIRM`; ngoài seam arrival/unload/deliver này không sửa Parcel. Không làm Incident resolve/list API, backend upload ảnh, GPS auto-arrival, Trip auto-disrupt/cancel, Operator arrival alias, hoặc sửa source Identity/Tracking.

## Tiêu chí thành công (DoD)

- [ ] Assigned `DRIVER` hoặc `ASSISTANT` báo Incident qua Gateway, nhận `201 ApiResponse`, Trip giữ nguyên và đúng một Incident/Outbox được commit.
- [ ] Notification chỉ fan-out in-app/push tới active `OPERATOR_ADMIN` cùng operator; retry/dedupe đúng khi Identity lỗi hoặc Rabbit giao lặp.
- [ ] TripStop arrival canonical trả `200`, set `actualArrivalTime`, giữ nguyên `estimatedArrivalTime` và phát đúng một `trip.stop.arrived`; destination arrival set một lần `destinationArrivedAt` và phát đúng một `trip.destination.arrived` kể cả express Trip không có stop.
- [ ] Pending stop trước `IN_PROGRESS` trả `422 TRIP_NOT_IN_PROGRESS`; `ARRIVED/SKIPPED` với key mới trả `409 TRIP_STOP_ALREADY_FINALIZED`; route Operator cũ trả `404`.
- [ ] Concurrent same-key request không chạy handler hai lần; concurrent arrival hai key có đúng một winner và một Outbox.
- [ ] Parcel UNLOAD thật trả `422` trước đúng arrival anchor, sau arrival chỉ chuyển `IN_TRANSIT -> UNLOADED`; action deliver riêng mới chuyển `UNLOADED -> DELIVERED_PENDING_CONFIRM` và sinh delivery token/event.
- [ ] Migration Incident fresh/up/down/reapply, full verification và `npm run e2e:day39` đều exit code `0`.

## Hợp đồng REST

### POST `/v1/driver/trips/{tripId}/incident`

Auth: User JWT, role `DRIVER | ASSISTANT`; JWT `sub` phải bằng `Trip.DriverUserId` hoặc `Trip.AssistantUserId`. `Idempotency-Key` bắt buộc. Trip chỉ hợp lệ khi `IN_PROGRESS`; `tripId`, reporter và operator derive server-side.

```json
{
  "category": "TRAFFIC_JAM",
  "description": "Kẹt xe tại nút giao",
  "photoUrls": ["https://storage.example/incident-1.jpg"],
  "latitude": 10.7731,
  "longitude": 106.7032
}
```

Validation/normalization:

- `category`: case-sensitive `TRAFFIC_JAM | VEHICLE_BREAKDOWN | ACCIDENT | WEATHER | OTHER`.
- `description`: optional, trim; whitespace-only thành `null`; tối đa 500 ký tự sau trim.
- `photoUrls`: optional, trim từng phần tử, tối đa 3 absolute HTTPS URL; absent/empty thành `null`; giữ input order.
- GPS optional nhưng latitude/longitude phải cùng có hoặc cùng vắng; bounds `[-90,90]` và `[-180,180]`.

Response `201` data:

```json
{
  "incidentId": "uuid",
  "tripId": "uuid",
  "reportedByUserId": "uuid",
  "category": "TRAFFIC_JAM",
  "description": "Kẹt xe tại nút giao",
  "photoUrls": ["https://storage.example/incident-1.jpg"],
  "latitude": 10.7731,
  "longitude": 106.7032,
  "reportedAt": "2026-07-16T03:00:00Z"
}
```

Errors: `403 FORBIDDEN`; `404 TRIP_NOT_FOUND`; `422 TRIP_NOT_IN_PROGRESS`; `422 VALIDATION_ERROR`; `422 IDEMPOTENCY_KEY_REQUIRED`; `422 IDEMPOTENCY_KEY_MISMATCH`; `409 IDEMPOTENCY_REQUEST_PENDING`.

### POST `/v1/driver/trips/{tripId}/stops/{stopId}/arrive`

Auth/assignment giống Incident. Body rỗng, `Idempotency-Key` bắt buộc. Canonical route duy nhất là `/v1/driver/...`; gỡ hẳn action `/v1/operator/trips/{tripId}/stops/{stopId}/arrive`, không alias/deprecation.

Response `200` giữ DTO hiện hữu:

```json
{
  "tripId": "uuid",
  "stopId": "uuid",
  "status": "ARRIVED",
  "actualArrivalTime": "2026-07-16T03:15:00Z"
}
```

Thứ tự business checks: Trip tồn tại → caller được assign → TripStop tồn tại/được lock → stop còn `PENDING` → Trip đúng `IN_PROGRESS` → mutate. Vì vậy new key trên `ARRIVED/SKIPPED` luôn trả `409`, còn pending stop trên Trip khác `IN_PROGRESS` trả `422`.

Errors: `403 FORBIDDEN`; `404 TRIP_NOT_FOUND | TRIP_STOP_NOT_FOUND`; `409 TRIP_STOP_ALREADY_FINALIZED`; `422 TRIP_NOT_IN_PROGRESS`; idempotency errors như Incident.

### POST `/v1/driver/trips/{tripId}/destination/arrive`

Canonical arrival anchor cho destination terminal, không dùng “stop cuối cùng” thay thế. Auth/assignment/idempotency và precondition `IN_PROGRESS` giống TripStop arrival. Endpoint body rỗng, derive `destinationStationId` từ Route snapshot phía server, atomically set `Trip.destinationArrivedAt` và `Trip.destinationArrivedByUserId`; không complete Trip và không thay `Trip.status`.

Response `200`:

```json
{
  "tripId": "uuid",
  "destinationStationId": "uuid",
  "status": "ARRIVED",
  "actualArrivalTime": "2026-07-16T04:30:00Z"
}
```

Same-key replay giữ nguyên response; key mới sau khi đã arrive trả `409 TRIP_DESTINATION_ALREADY_ARRIVED`. Missing Trip/Route snapshot trả `404 TRIP_NOT_FOUND`; non-progress trả `422 TRIP_NOT_IN_PROGRESS`; unassigned caller trả `403 FORBIDDEN`. Express Trip có zero `TripStop` vẫn gọi endpoint này bình thường.

### Parcel unload/deliver contract correction

- `POST /v1/assistant/parcels/{parcelId}/unload`: giữ route hiện hữu, chỉ nhận `IN_TRANSIT`. Với `dropoffStopId != null`, matching TripStop phải `ARRIVED`; với `dropoffStopId == null`, Trip snapshot phải có `destinationArrivedAt`. Thiếu anchor trả lần lượt `422 DROP_OFF_STOP_NOT_ARRIVED` hoặc `422 DESTINATION_TERMINAL_NOT_ARRIVED`. Success `200` chỉ set `status=UNLOADED`, `unloadedAt`, release cargo một lần và phát `parcel.parcel.unloaded`; delivery token/pending-confirm timestamp còn `null`.
- `POST /v1/assistant/parcels/{parcelId}/deliver`: mutation mới, body rỗng, chỉ nhận `UNLOADED`, bắt buộc `Idempotency-Key`. Success `200` chuyển `UNLOADED -> DELIVERED_PENDING_CONFIRM`, set `deliveredPendingConfirmAt`, sinh token 48 giờ và phát `parcel.parcel.delivered_pending_confirm`; không release cargo lần hai. Existing `/confirm-delivery` tiếp tục là bước xác nhận cuối từ `DELIVERED_PENDING_CONFIRM`, không bị repurpose.
- Cả hai endpoint giữ role/tenant/assigned-assistant authorization hiện hữu, ADR 0004 và CAS race safety. Status sai trả `409 INVALID_STATUS`; missing Parcel trả `404 PARCEL_NOT_FOUND`.

## Idempotency v2

- Response key: `<service>:idem:v2:response:{SHA256(idempotencyKey)}`; processing key: `<service>:idem:v2:processing:{SHA256(idempotencyKey)}`. Cache entry lưu request fingerprint, status, content type và response bytes.
- Fingerprint = SHA-256 của một frame UTF-8 có length-prefix cho từng phần: authenticated `sub`, method chuẩn hóa uppercase, `PathBase + Path`, canonical query và raw body bytes. Không nối chuỗi bằng delimiter mơ hồ; cùng key nhưng khác actor/method/path/query/body trả `422 IDEMPOTENCY_KEY_MISMATCH`.
- Canonical query lấy mọi key/value pair từ ASP.NET request, sort ordinal theo key rồi value và giữ duplicate values; raw JSON khác whitespace/property order được xem là request khác.
- Request đầu acquire Redis processing lock bằng `SET NX EX`, TTL 120 giây, random owner token. Loser khi winner còn chạy trả `409 IDEMPOTENCY_REQUEST_PENDING`; nếu response vừa xuất hiện thì replay.
- Chỉ owner được release. Exception/5xx release và không cache; response `<500` lưu nguyên status/body TTL 24 giờ rồi release; replay không gọi downstream.
- Middleware xử lý `POST/PATCH/PUT/DELETE` khi có header. Endpoint dùng `RequireIdempotencyKey` để buộc header và trả exact `IDEMPOTENCY_KEY_REQUIRED`.
- Cache legacy `<service>:idem:{key}` chỉ có body hash không được trusted để replay cross-path. Nếu legacy entry còn tồn tại thì fail closed bằng `422 IDEMPOTENCY_KEY_MISMATCH`; rollout đồng bộ shared lib, để key cũ tự hết hạn tối đa 24 giờ, không flush Redis business keys.

## Persistence Incident

EF phải khớp DDL đã có trong `db-schema/trip-route-vehicle/schema.sql`; không sửa canonical DDL nếu migration test không chứng minh drift:

```text
incidents:
  id UUID PK
  trip_id UUID NOT NULL -> trips.id ON DELETE RESTRICT
  reported_by_user_id UUID NOT NULL
  category incident_category NOT NULL
  description TEXT NULL
  photo_urls JSONB NULL
  latitude DECIMAL(10,7) NULL
  longitude DECIMAL(10,7) NULL
  reported_at TIMESTAMPTZ NOT NULL
  resolved_at TIMESTAMPTZ NULL
  resolved_by_user_id UUID NULL
  resolution_note TEXT NULL
  created_at / updated_at TIMESTAMPTZ NOT NULL
```

Enum PostgreSQL có đúng 5 labels. Map tại `TripDbContext.ConfigurePostgresEnums`, `OnModelCreating` và `TripPostgresTypeMapper`; `photo_urls` dùng JSONB converter + structural `ValueComparer`. Thêm `DbSet`, repository/DI, ba index canonical và migration reversible. Fresh DB phải migrate rồi reload Npgsql types trước enum round-trip.

Trip destination arrival dùng hai nullable columns mới trên `trips`: `destination_arrived_at TIMESTAMPTZ` và `destination_arrived_by_user_id UUID`. Đây là operational anchor riêng, không alias `completed_at`, vì Day-38 auto-complete có thể không chứng minh xe đã tới bến. Migration phải update canonical Trip DDL, EF configuration/snapshot và có `Down()` đảo ngược; `destination_arrived_by_user_id` là logical user reference, không tạo cross-DB FK.

## Integration events

`trip.incident.reported`:

```text
eventId, occurredAt, incidentId, tripId, operatorId,
reporterUserId, category, description?, photoUrls?,
latitude?, longitude?, reportedAt
```

`trip.stop.arrived`:

```text
eventId, occurredAt, tripId, stopId, operatorId,
actorUserId, actualArrivalTime
```

`trip.destination.arrived`:

```text
eventId, occurredAt, tripId, destinationStationId, operatorId,
actorUserId, actualArrivalTime
```

Dùng typed events; `occurredAt` lấy cùng instant `IClock` với `reportedAt`/`actualArrivalTime`. Optional Incident fields vắng thì producer omit; consumer chấp nhận omitted/null và normalize. Mutation + Outbox cùng EF transaction. BSOT phải đăng ký cả hai arrival event, mở rộng Incident payload, thêm ba Trip endpoints cùng Parcel deliver endpoint vào §5.6, thêm `TRIP_NOT_IN_PROGRESS`, `TRIP_STOP_NOT_FOUND`, `TRIP_STOP_ALREADY_FINALIZED`, `TRIP_DESTINATION_ALREADY_ARRIVED` và `DESTINATION_TERMINAL_NOT_ARRIVED` vào §5.9 rồi append changelog. API contract/BSOT phải giữ Parcel state machine two-step, không ratify direct transition hiện tại. Không assert payload `eventId` bằng Outbox row ID: publisher hiện dùng Outbox ID làm AMQP `MessageId`.

## Notification flow

1. Consumer parse canonical Incident payload trước external I/O; chỉ payload parse failure mới là poison message được mark processed.
2. Resolve qua `OPERATOR_RECIPIENT_PROVIDER`, đã bind tới `IdentityOperatorRecipientProvider` gọi `GET /internal/v1/operators/{operatorId}/recipient-users`.
3. Dedupe user IDs, map một `INCIDENT_REPORTED` mỗi active admin; dedupe key `trip.incident.reported:{eventId}:{userId}:INCIDENT_REPORTED`; existing FCM queue tạo delivery.
4. Empty recipients là success/no-op. Identity timeout/auth/non-2xx/invalid response, DB hoặc enqueue failure phải release processing lock và throw để Rabbit retry.
5. Ưu tiên payload `eventId` làm consumer identity; fallback AMQP message ID chỉ cho legacy payload. Không log description, photo URL, GPS hoặc full payload.
6. `INCIDENT_REPORTED` đã có trong Prisma/DDL; không tạo Notification migration.

## Tasks

### Task 39.0 — Chốt baseline và harden shared idempotency

| Field | Value |
|---|---|
| stack/owner | cross-cutting / Shared.Web + Trip contract |
| implement agent | worker |
| review agent | reviewer + dotnet-reviewer |
| skill | none |
| owned files | `libs/dotnet/VietRide.Shared.Web/Middleware/IdempotencyMiddleware.cs`; `libs/dotnet/VietRide.Shared.Web/DependencyInjection/IdempotencyServiceCollectionExtensions.cs`; `tests/dotnet/VietRide.Shared.Web.UnitTests/Middleware/IdempotencyMiddlewareTests.cs`; `apps/trip/src/VietRide.Trip.Api/Filters/RequireIdempotencyKeyAttribute.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/Api/RequireIdempotencyKeyAttributeTests.cs`; Day-39 sections trong `VietRide_API_Contract_v1.md` và `BACKEND_SOURCE_OF_TRUTH.md`. |
| forbidden scope | Không làm Day-43 full coverage audit; không sửa business handlers, other services, dependencies, `.env`, secrets, `.agents/**`, `.codex/**`, git operations. |
| depends on | none; hard gate trước mọi Day-39 mutation; parallel-safe: no. |
| invariant flags | ADR 0004; response TTL 24h; owner-safe lock; không cache 5xx; không log raw key/body/token; CRLF `.cs`, LF docs; no package mới. |
| acceptance | Unit test first/replay/mismatch/pending, empty body khác path, concurrent same-key chỉ một downstream, lock cleanup, 5xx. Missing header trả `422 IDEMPOTENCY_KEY_REQUIRED`; shared build/format/test xanh; docs đúng v2. |
| source citations | BSOT §5.6/§5.9/§9.8; current `IdempotencyMiddleware.cs`; AGENTS mutation rule. |

### Task 39.1 — Incident aggregate, migration, API và Outbox

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip-Route-Vehicle |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `scaffold-aggregate`, `ef-migration`, `add-endpoint`, `add-integration-event` |
| owned files | `apps/trip/src/VietRide.Trip.Domain/Entities/Incident.cs`, `IncidentCategory.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/IIncidentRepository.cs`; new `Application/Features/Incidents/ReportIncident/*`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Configurations/IncidentConfiguration.cs`, `Repositories/IncidentRepository.cs`, `TripDbContext.cs`, `DependencyInjection/TripPostgresTypeMapper.cs`, `DependencyInjection/InfrastructureServiceCollectionExtensions.cs`, `Migrations/<timestamp>_AddTripIncidents*`, `TripDbContextModelSnapshot.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/DriverController.cs`, `Controllers/Requests/ReportIncidentRequest.cs`; matching Trip unit/integration tests; Incident sections trong API Contract/BSOT. |
| forbidden scope | Không sửa Trip state machine, arrival, Notification, Gateway route table, Identity/Parcel/Tracking, Firebase upload, dependencies, env/secrets/git. |
| depends on | 39.0; parallel-safe: no vì cùng DriverController/docs với 39.3. |
| invariant flags | JWT assignment; `IN_PROGRESS` only; ambient transaction + Trip `FOR UPDATE`; Incident không mutate Trip; Outbox atomic; schema `vietride_trip`; no cross-DB FK; MediatR v11; CRLF. |
| acceptance | Driver/Assistant `201`; exact validation/auth/state/idempotency errors; Trip snapshot bất biến; one Incident + one Outbox; payload exact; migration fresh/up/down/reapply + enum/JSONB/GPS round-trip; rollback atomic. |
| source citations | technical context v7 §4.2 lines 495-501; Day-39 timeline; Trip DDL; AGENTS_DOTNET EF/Outbox/API. |

### Task 39.2 — Operator recipient resolution và Incident notification

| Field | Value |
|---|---|
| stack/owner | nest / Notification |
| implement agent | nest-worker |
| review agent | nest-reviewer + reviewer seam gate |
| skill | `vietride-nest-event` |
| owned files | `apps/notification/src/notifications/trip-tracking-alert-notification.mapper.ts`; `trip-tracking-alert-events.consumer.ts`; `trip-tracking-alert-notification.mapper.spec.ts`; `trip-tracking-alert-events.consumer.spec.ts`; `trip-tracking-alert-events.consumer.e2e-spec.ts`; `identity-operator-recipient.provider.spec.ts` only when needed to lock invalid-response retry behavior. |
| forbidden scope | Không sửa Prisma/migration, Identity source, generic parcel/subscription consumer, email, Gateway/Trip, dependencies, env/secrets/git. |
| depends on | 39.1 canonical event; parallel-safe: no trong dirty tree. |
| invariant flags | Inject provider token; active admin only; eventId dedupe; processed chỉ sau success; provider failures retry; PII-safe pino; LF. |
| acceptance | Payload không recipient IDs vẫn fan-out; staff/inactive/cross-operator excluded; duplicate IDs/eventId không duplicate; empty recipients success; malformed event processed; Identity invalid body/timeout/5xx và DB failure release+throw; lint/unit/E2E/build xanh. |
| source citations | BSOT event registry; `IdentityOperatorRecipientProvider`; `GetOperatorRecipientUsersQueryHandler`; Nest event rules. |

### Task 39.3 — Chuyển/siết TripStop arrival và thêm destination-terminal anchor

| Field | Value |
|---|---|
| stack/owner | dotnet / Trip-Route-Vehicle |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer + reviewer contract gate |
| skill | `add-endpoint`, `add-integration-event`, `ef-migration` |
| owned files | `apps/trip/src/VietRide.Trip.Api/Controllers/DriverController.cs`; `OperatorTripsController.cs` chỉ để gỡ stop-arrival action; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/ArriveTripStop*` và new `ArriveTripDestination*` command/response/typed events; `apps/trip/src/VietRide.Trip.Domain/Entities/Trip.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/ITripStopRepository.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/TripStopRepository.cs`; `TripDbContext.cs`, `TripConfiguration.cs`, migration `AddTripDestinationArrival*` và model snapshot; internal Trip snapshot DTO/handler để expose `destinationArrivedAt`; matching Trip unit/integration tests; arrival sections trong API Contract/BSOT/Trip DDL. |
| forbidden scope | Không đổi TripStop schema/ETA, Parcel/Notification/Identity/Gateway source, Tracking GPS, Trip completion semantics/fallback, other Operator actions, dependencies, env/secrets/git. |
| depends on | 39.0; dispatch sau 39.2 để tránh chồng hunk; parallel-safe: no. |
| invariant flags | Canonical Driver routes; assignment; ambient transaction; lock Trip trước stop; `PENDING -> ARRIVED`; destination anchor independent from `completedAt`; `IClock`; ETA immutable; Outbox atomic; logical user FK only; CRLF. |
| acceptance | Stop và destination endpoints nhận assigned Driver/Assistant `200`; unassigned `403`; missing `404`; non-progress `422`; finalized/already-arrived `409`; replay stable; old Operator route `404`. PostgreSQL race mỗi anchor = one 200, one 409, one timestamp/event; express Trip zero stops vẫn destination-arrive; auto-complete không tự set destination anchor; internal snapshot expose đúng field; migration up/down/reapply; existing stop-arrival Notification consumer vẫn parse. |
| source citations | technical context v7 §3598-3610 và express Trip line 1924; timeline Day 28 line 289 + Day-39 Review; Trip/TripStop DDL; current handler/controller; Day-38 complete/fallback; Gateway Driver route. |

### Task 39.4 — Sửa Parcel terminal gate và canonical two-step delivery

| Field | Value |
|---|---|
| stack/owner | dotnet / Parcel |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer + reviewer cross-service seam gate |
| skill | `add-endpoint` |
| owned files | `apps/parcel/src/VietRide.Parcel.Api/Controllers/AssistantParcelsController.cs`; new deliver command/handler/response dưới `Application/Features/Parcels/Deliver/`; existing `Application/Features/Parcels/Unload/*`; `Application/Abstractions/Repositories/IParcelRepository.cs`; `Infrastructure/Persistence/Repositories/ParcelRepository.cs`; Trip snapshot contract/client/dev stub only để nhận `destinationArrivedAt`; matching Parcel unit/integration tests; exact Parcel endpoint/state/error/event sections trong API Contract/BSOT. |
| forbidden scope | Không đổi Parcel schema/status enum, load/transfer/recipient-confirm flows, Trip source, Identity/Notification/Gateway source, dependencies, env/secrets/git. |
| depends on | 39.3 internal snapshot contract; parallel-safe: no. |
| invariant flags | `dropoffStopId != null` dùng đúng matching stop; null dùng destination anchor, không dùng last intermediate stop; unload chỉ `IN_TRANSIT -> UNLOADED`; deliver chỉ `UNLOADED -> DELIVERED_PENDING_CONFIRM`; token chỉ sinh ở deliver; cargo release chỉ unload; CAS + local Outbox atomic; CRLF. |
| acceptance | Stop-bound và terminal-bound Parcel đều fail `422` trước anchor và unload `200` sau anchor; express Trip zero stops pass sau destination arrival. Unload set only `UNLOADED/unloadedAt`, token null, one unloaded event + one cargo release; deliver set pending-confirm/token/expiry, one pending-confirm event, no second release; replay/races không duplicate; existing final confirmation vẫn hoạt động từ pending-confirm; Parcel build/format/test xanh. |
| source citations | timeline Day 27-28, đặc biệt Day 28 line 289; technical context v7 lines 508-509 và express line 1924; BSOT §8.3 lines 1992-1996; current unload handler/repository drift. |

### Task 39.5 — Real-stack E2E, Postman và final gate

| Field | Value |
|---|---|
| stack/owner | cross-cutting / QA integration |
| implement agent | worker |
| review agent | reviewer |
| skill | none |
| owned files | `infra/docker/docker-compose.day39-e2e.yml`; `scripts/run-day39-driver-ops-e2e.mjs`; `package.json` script; Day-39 folder trong cumulative Postman collection/environment. |
| forbidden scope | Không sửa production service source, tạo Dockerfile/dependency mới, dùng credential thật, seed output dưới test, reuse/xóa developer volumes, sửa env/agent config/git. |
| depends on | 39.1, 39.2, 39.3, 39.4; parallel-safe: no. |
| invariant flags | Real PostgreSQL/Redis/RabbitMQ/services; API qua Gateway; finite poll; deterministic UUID; no mock DB/HTTP; isolated cleanup `down -v`; LF. |
| acceptance | `npm run e2e:day39` cover scenarios/assertions dưới đây, exit 0, cleanup pass; cumulative Postman parse được và không chứa secret. |
| source citations | Day-39 DoD/Review + Day-28 carry-over; Day-36/38 harness pattern; canonical Parcel status machine. |

## E2E real stack

### Harness

```text
infra/docker/docker-compose.day39-e2e.yml
scripts/run-day39-driver-ops-e2e.mjs
npm run e2e:day39
```

Stack thật: PostgreSQL, Redis, RabbitMQ, Identity, Trip, Parcel, Notification, Gateway. Dùng compose project/network/ports/volumes riêng; `down -v` trước run và trong `finally`. Base Gateway depends Booking/Payment nên harness start infra, rồi `up --no-deps` Identity/Trip/Parcel/Notification/Gateway. Overlay bắt buộc `Parcel: Trip__BaseUrl=http://trip:5002`, `Trip__UseDevStub=false`; Notification development fake FCM và `IDENTITY_INTERNAL_BASE_URL=http://identity:5001`. Không cần Dockerfile Day 39.

Harness chờ health/ready, migrate qua startup, seed prerequisite bằng `psql`, mint JWT development ngắn hạn nhưng không log token/key, gọi REST qua Gateway, poll async có timeout, assert persistence và luôn cleanup.

### Deterministic seed

UUID prefix `39000000-...`, time derive từ `now`, seed idempotent.

- **Identity**: Operator A approved; active admin + device; active staff + device; inactive admin + device; Operator B + active admin/device; assigned Driver/Assistant; unassigned same-tenant crew; cross-tenant crew.
- **Trip**: Station/Route/Vehicle/Stops thỏa FK; Trip `IN_PROGRESS`, `BOARDING`, `SCHEDULED`, terminal, stop-race và destination-race; stops `PENDING/ARRIVED/SKIPPED`. Thêm express Trip zero stops và Trip auto-completed nhưng chưa có destination anchor. Không seed Incident/Day-39 Outbox/arrival output.
- **Parcel/Trip cargo**: Hai Parcel `IN_TRANSIT`: một có pending `dropoffStopId`, một terminal-bound có `dropoffStopId=null` trên express Trip; matching `trip_cargo_parcels` state `LOADED`, Trip loaded weight/volume counters khớp. Thêm Parcel `UNLOADED` riêng để test deliver. Không seed token, unload/deliver Outbox hoặc Notification output.

### Black-box scenarios

1. Driver và Assistant Incident success; response/normalization đúng; Trip không đổi; exact Incident/Outbox counts.
2. Invalid enum, description 501, bốn ảnh, HTTP/relative URL, GPS thiếu cặp/out-of-range, missing key → exact error, zero side effect.
3. Unassigned/cross-tenant/role sai `403`; missing Trip `404`; BOARDING/SCHEDULED/terminal `422`.
4. Sequential replay giữ ID/timestamp/body; different body/path/actor `422`; concurrent same key chỉ một execution, loser replay hoặc `409 PENDING`.
5. Outbox `PUBLISHED`; chỉ active admin A nhận một `INCIDENT_REPORTED` + one delivery `SENT`; excluded users nhận zero; Operator notification API thấy alert.
6. Republish cùng payload `eventId` nhưng transport ID khác → không lookup/persist/deliver thêm.
7. Identity retry: boot Notification để declare queue, stop Notification, report Incident + đợi publish; stop Identity, start Notification, xác nhận chưa processed; restart Identity, poll đúng một notification, retry/DLQ rỗng.
8. Driver/Assistant TripStop arrival `PENDING -> ARRIVED`; UTC timestamp, ETA unchanged, one event; replay stable.
9. TripStop non-progress `422`; finalized `409`; unassigned/cross-tenant `403`; missing IDs `404`; old Operator route `404`; race hai keys → one `200`, one `409`, one timestamp/Outbox.
10. Destination arrival trên Trip thường và express zero-stop: `IN_PROGRESS -> destinationArrivedAt` nhưng Trip status giữ nguyên; replay stable. Non-progress `422`, already-arrived `409`, auth/missing errors đúng; race hai keys chỉ một event. Auto-completed Trip không có anchor và không được coi là đã tới bến.
11. Stop-bound Parcel unload trước stop arrival trả `422 DROP_OFF_STOP_NOT_ARRIVED`; terminal-bound trước destination arrival trả `422 DESTINATION_TERMINAL_NOT_ARRIVED`, kể cả express Trip zero stops và auto-completed Trip.
12. Sau đúng anchor, unload từng Parcel trả `200 UNLOADED`; `unloadedAt` set, delivery token/pending-confirm timestamp vẫn null, chỉ một unloaded event, Trip cargo ledger `RELEASED` và counters giảm đúng một lần.
13. Deliver trên `UNLOADED` trả `200 DELIVERED_PENDING_CONFIRM`, sinh token/expiry và đúng một pending-confirm event/delivery; không release cargo lần hai. Same-key replay và two-key race không duplicate; existing recipient/manual confirm tiếp tục chạy từ pending-confirm.

### Direct assertions

- Trip: `incidents`, `trip_stops`, `trip_cargo_parcels`, `trips.destination_arrived_*`, `outbox_events`, migration history.
- Parcel: `parcels`, `outbox_events`; two-step status, token/timestamps, cargo-release count và no duplicate.
- Notification: `notifications`, `notification_deliveries`; type/user/dedupe/status và excluded recipients.
- Redis: API response TTL gần 24h, processing lock clear; consumer processed TTL, processing clear; mismatch/pending không overwrite.
- RabbitMQ: Outbox `PUBLISHED`, side effect tồn tại, duplicate không nhân bản, retry/DLQ cuối test rỗng.

HTTP 2xx đơn thuần không đủ để pass.

## Postman cumulative collection

Thêm folder Day 39 vào collection/environment hiện hữu. Cover Incident Driver/Assistant, validation/assignment/state, replay/mismatch/pending; TripStop/destination arrival success/finalized/pre-state/old route; Notification list; stop/terminal arrival → Parcel unload → deliver. Runtime IDs/tokens lấy từ deterministic seed; race, transient retry và DB assertions giữ trong Node harness.

## Verification gate

```text
dotnet build libs/dotnet/VietRide.Libs.sln -c Release
dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes
dotnet test libs/dotnet/VietRide.Libs.sln -c Release

dotnet build apps/trip/VietRide.Trip.sln -c Release
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes
dotnet test apps/trip/VietRide.Trip.sln -c Release

dotnet build apps/identity/VietRide.Identity.sln -c Release
dotnet test apps/identity/VietRide.Identity.sln -c Release
dotnet build apps/booking/VietRide.Booking.sln -c Release
dotnet test apps/booking/VietRide.Booking.sln -c Release
dotnet build apps/payment/VietRide.Payment.sln -c Release
dotnet test apps/payment/VietRide.Payment.sln -c Release
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release

npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
npx nx run gateway:test
npx nx run gateway:lint

npm run e2e:day39
git diff --check
```

Migration gate tạo scratch DB, chạy `dotnet ef database update --project apps/trip/src/VietRide.Trip.Infrastructure`, rollback lần lượt qua `AddTripDestinationArrival` và `AddTripIncidents`, rồi reapply cả hai; assert Incident enum/table/FK/index/JSONB/precision cùng destination-arrival columns/audit mapping.

E2E summary bắt buộc:

```text
seed PASS
idempotency PASS
incident api/outbox PASS
operator notification PASS
identity retry PASS
stop/destination arrival race PASS
parcel unload/deliver two-step PASS
database assertions PASS
cleanup PASS
```

## Dispatch order

```text
39.0 idempotency/contract baseline
  -> 39.1 Incident aggregate + API + Outbox
  -> 39.2 Notification recipient resolution
  -> 39.3 TripStop + destination arrival anchors
  -> 39.4 Parcel terminal gate + unload/deliver two-step
  -> 39.5 real-stack E2E + Postman + final verification
```

Dispatch tuần tự vì worktree đang chứa Day-38 changes trong DriverController, Trip repositories, Notification, API/BSOT, compose, Postman và package.json. Worker chỉ sửa hunk thuộc write set, không reset/revert. Sau từng task: stack reviewer → `/verify`; cuối ngày: `/audit-day 39`.

## Progress tracker

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 39.0 | ⬜ todo | — | — | Hard gate trước mutations. |
| 39.1 | ⬜ todo | — | — | Incident vertical slice. |
| 39.2 | ⬜ todo | — | — | Active-admin fan-out/retry. |
| 39.3 | ⬜ todo | — | — | Stop/destination arrival + race safety. |
| 39.4 | ⬜ todo | — | — | Parcel terminal anchor + canonical two-step. |
| 39.5 | ⬜ todo | — | — | Day-wide acceptance. |

## Business invariants và assumptions

- Một successful Incident request tạo một Incident + Outbox; distinct idempotency keys có thể đại diện các báo cáo khác nhau.
- Incident chỉ ghi audit, không thay đổi bất kỳ field Trip nào.
- Chỉ assigned Driver/Assistant thao tác; không tin operator/reporter từ client.
- TripStop và destination terminal đều final-arrive một lần; arrival không đổi static ETA, không complete Trip và auto-complete không tạo physical arrival anchor.
- Parcel tiếp tục dùng synchronous Trip snapshot để gate UNLOAD; `dropoffStopId` chọn đúng stop anchor, null chọn destination anchor, không suy diễn last stop. Arrival event không thay thế seam này.
- UNLOAD và DELIVER là hai business action riêng: cargo release tại `UNLOADED`; token/notification chỉ tại `DELIVERED_PENDING_CONFIRM`.
- Recipient là active `OPERATOR_ADMIN` only; không fallback staff/reporter/cross-operator.
- Identity/Tracking source không đổi; Parcel chỉ đổi exact unload/deliver seam của Task 39.4; Gateway `/v1/driver` và `/v1/assistant` đã đủ nên không sửa route table.
- E2E dùng provider adapter thật của Notification development path, không mock service/DB và không cần production FCM credential.

## Open questions

Không còn câu hỏi mở. Route, role, Trip state, GPS nullability, recipient, destination anchor và Parcel two-step semantics đã được khóa trong plan này; các quyết định mới bám timeline Day 28 carry-over và canonical state machine thay vì code drift hiện tại.
