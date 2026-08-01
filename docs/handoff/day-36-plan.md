# Day 36 — Shuttle backend plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 36 (Jira: SCV-116)
- **Prior checklist**: `docs/handoff/day-35-checklist.md` (`not found`)
- **Plan status**: DRAFT → (reviewer) APPROVED / REVISION-REQUIRED

## Objective
Triển khai luồng shuttle v1 thủ công cho hành khách đặt vé tại Station hỗ trợ shuttle: đăng ký nhu cầu, điều phối bởi operator và gán vào chuyến shuttle. Shuttle là aggregate thuộc Trip service; Booking chỉ giữ giao dịch đặt vé chính và không tạo payment riêng. Tracking tái sử dụng Socket.IO của Tracking service nhưng phải cô lập shuttle trong room `shuttle:{shuttleTripId}`. Day 36 chuẩn bị dữ liệu và contract cho driver/passenger tracking, không tự động tạo/hủy shuttle, không tính phí và không triển khai route-change fallback ngoài quyết định contract được phê duyệt.

## Success criteria (DoD — binary, verifiable)
- [ ] Passenger có thể gửi yêu cầu shuttle hợp lệ khi đặt booking tại Station `supportsShuttle=true`, với địa chỉ và tọa độ yêu cầu.
- [ ] Operator chỉ xem được yêu cầu pending của chính `operatorId` trong `GET /v1/operator/shuttle-requests`.
- [ ] Operator tạo `ShuttleTrip` thủ công với driver/vehicle hợp lệ; các `ShuttlePassenger` được chọn được gán nguyên tử và chuyển từ `PENDING_ASSIGNMENT` sang `PENDING`.
- [ ] Một yêu cầu shuttle tại Station không hỗ trợ shuttle bị từ chối bằng error code đã được contract/BSOT phê duyệt; không có `ShuttlePassenger` bị tạo.
- [ ] Passenger/driver được phân quyền đúng có thể dùng Socket.IO room shuttle và GPS shuttle được broadcast tách biệt khỏi `trip:{mainTripId}`.
- [ ] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` cho Trip và Booking; `npx nx lint tracking`, `npx nx test tracking`, `npx nx build tracking` đều xanh, cùng migration up/down và kiểm thử E2E các đường REST/socket mới.

## Contract changes
Các contract dưới đây chưa tồn tại trong `VietRide_API_Contract_v1.md`; chỉ Task 36.0 được phép bổ sung sau khi các Open questions được human quyết định:

- Mở rộng `POST /v1/bookings` request/response để diễn tả shuttle pickup/dropoff và trạng thái đăng ký; vẫn yêu cầu `Idempotency-Key` theo BSOT §5.6.
- Thêm `GET /v1/operator/shuttle-requests` và `POST /v1/operator/shuttle-trips`, role, pagination/filter, payload gán passenger, error codes, `Idempotency-Key` cho mutation, và Gateway route. Không suy diễn endpoint cancel/start/complete ở Day 36.
- Thêm internal HTTP seam hoặc integration event cho Booking → Trip tạo request shuttle và Trip/Tracking authorization. Không tái sử dụng hoặc đổi payload của event registry hiện có khi chưa ghi rõ owner/consumer/idempotency.
- Bổ sung Socket.IO event/ack contract cho `joinShuttleTracking` và `gps:update` với shuttle identity, cùng quy tắc authorization. Không thay đổi room `trip:{tripId}` của main trip.
- Đồng bộ BSOT §5.6, §7.2, §7.3, §13, API contract và canonical DDL khi có quyết định; không thêm error/event key không được phê duyệt.

## Tasks

### Task 36.0 — Pre-reqs / architecture baseline: chốt contract, ownership và cross-service flow Shuttle
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `db-schema/trip-route-vehicle/schema.sql` và `db-schema/trip-route-vehicle/README.md` chỉ khi reconciliation DDL cần thiết; `apps/gateway/src/config/routes.ts` chỉ sau khi API prefixes/roles được contract chốt; `docs/handoff/day-36-plan.md` chỉ để cập nhật Open questions thành quyết định đã được human xác nhận. |
| forbidden scope | `.env`, secrets, tất cả code `apps/trip/**`, `apps/booking/**`, `apps/tracking/**`, `libs/**`, migration generated, package/dependency change, các docs không nêu trên, git ops; không implement endpoint/event/socket trong task docs baseline. |
| depends on | —. Task 36.1–36.5 phụ thuộc Task 36.0; không parallel-safe vì các task sau cần contract và ownership đã chốt. |
| invariant flags | LF cho `.md`/`.sql`; ADR 0004 `ApiResponse<T>`; error code UPPER_SNAKE_CASE chỉ từ registry đã cập nhật; mutation idempotency Redis SETNX 24h; logical FK liên DB, không cross-DB FK; tenant isolation từ JWT `operatorId`; routing key theo `<svc>.<aggregate>.<verb_past>`; Gateway chỉ forward `Idempotency-Key`; không thay đổi main-trip Socket.IO semantics. |
| acceptance | Contract quyết định đầy đủ request/response/status/error/auth/pagination cho 3 REST surfaces; xác định rõ one-of pickup/dropoff shuttle model, thời điểm tạo request (PENDING_PAYMENT hay CONFIRMED), cơ chế Booking→Trip, và authorization shuttle tracking. BSOT có registry nội bộ/event/idempotency/Gateway phù hợp; canonical DDL vẫn khớp enum/table/index hiện có; các Q1–Q7 được human resolve trước dispatch code. |
| source citations | `BE_TIMELINE_VU.md:363-371`; `SU26SE101_VIETRIDE_technical_context_v7.md:4261-4370` (§6.14); `VietRide_API_Contract_v1.md:10`, `BACKEND_SOURCE_OF_TRUTH.md:1246-1270` (§5.6), `:1666-1788` (§7.2–7.3), `apps/gateway/src/config/routes.ts:124-205`. |

### Task 36.1 — Shuttle domain aggregate, EF mapping và reversible Trip migration
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | scaffold-aggregate + ef-migration |
| owned files (write set) | new `apps/trip/src/VietRide.Trip.Domain/Entities/ShuttleTrip.cs`, `ShuttlePassenger.cs`, `ShuttleDirection.cs`, `ShuttleTripStatus.cs`, `ShuttlePassengerStatus.cs`; new repository abstractions/configurations/repositories under `apps/trip/src/VietRide.Trip.Application/Abstractions/Repositories/` and `apps/trip/src/VietRide.Trip.Infrastructure/Persistence/{Configurations,Repositories}/`; `apps/trip/src/VietRide.Trip.Infrastructure/TripDbContext.cs`; `InfrastructureServiceCollectionExtensions.cs`; generated files in `apps/trip/src/VietRide.Trip.Infrastructure/Migrations/`; focused `apps/trip/tests/**` domain/persistence tests. |
| forbidden scope | Booking/Tracking/Gateway code; `db-schema/**` except a conflict found by Task 36.0; `.env`, secrets, new NuGet packages, payment flow, auto-trigger/cutoff Hangfire jobs, route-change fallback behavior, git ops. |
| depends on | 36.0. Parallel-safe = no with 36.2/36.3 because their handlers depend on repository/model contracts. |
| invariant flags | CRLF `.cs`; `shuttle_trips` and `shuttle_passengers` map exactly to canonical DDL including enum values, nullable `shuttle_trip_id`, indexes and update triggers; `main_trip_id`, `station_id`, `vehicle_id` are same-DB FKs; `operator_id`, `driver_user_id`, `booking_id` remain logical FKs; no soft-delete columns absent from schema; UTC timestamps; only valid state transitions (`PENDING_ASSIGNMENT→PENDING`, shuttle `SCHEDULED` initial); migration has reversible `Down()`. |
| acceptance | Migration up creates both tables, three enums and all listed indexes exactly as `schema.sql:547-597`; migration down cleanly removes Day-36 objects without removing earlier objects; EF snapshot/DbSets/repositories compile; domain tests reject invalid transitions/invalid assignment and persistence test proves nullable unassigned request is representable; Trip build, format, tests and NetArchTest green. |
| source citations | `db-schema/trip-route-vehicle/schema.sql:39-47,547-597,680-683`; `db-schema/trip-route-vehicle/README.md` Entity List/Cross-service References; `SU26SE101_VIETRIDE_technical_context_v7.md:4261-4357`; `AGENTS_DOTNET.md` EF Core, migration and logical-FK rules. |

### Task 36.2 — Booking request extension và reliable ShuttlePassenger registration seam
| Field | Value |
|---|---|
| stack/owner | dotnet / Booking + Trip integration boundary |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event (chỉ khi Task 36.0 chọn event) |
| owned files (write set) | Booking request/command/validator/handler/DTO files rooted at `apps/booking/src/VietRide.Booking.{Api,Application}/Features/Bookings/CreateBooking/`; `apps/booking/src/VietRide.Booking.Api/Controllers/BookingsController.cs`; Trip consumer or internal controller/handler/client files selected by Task 36.0 under `apps/trip/src/VietRide.Trip.{Api,Application,Infrastructure}/`; booking/trip focused unit, integration and contract tests; event payload contracts only at the owner path selected in 36.0. |
| forbidden scope | DB schema/migration changes outside 36.1; Payment behavior or amount; automatic shuttle dispatch/cancel; route-change fallback; Gateway route edits; Tracking implementation; `.env`, secrets, new dependencies, cross-DB FK, git ops. |
| depends on | 36.0, 36.1. Parallel-safe = no with 36.3 because creation must establish the pending-request model first. |
| invariant flags | Preserve existing seat-lock/payment compensation and booking idempotency; main booking remains independently confirmable and shuttle is free (no Payment record/charge/refund); only terminal Station with `supportsShuttle=true` may register; Stop never supports shuttle; validate address nonblank and latitude/longitude bounds; exactly the direction semantics chosen in 36.0; cross-service write is reliable/idempotent and does not claim atomic DB transaction across Booking/Trip; logical `booking_id` only. |
| acceptance | Contract-approved booking request produces exactly one pending ShuttlePassenger per approved semantic unit, only at the contract-selected lifecycle point; unsupported Station rejection leaves no request; payment/seat-lock compensation cannot leave an orphaned confirmed shuttle request; retry of the same Booking idempotency key cannot duplicate a request; integration tests cover supported/unsupported Station, invalid coordinates, payment failure/compensation, duplicate delivery/retry, and tenant ownership. Booking and Trip build/format/tests green. |
| source citations | `BE_TIMELINE_VU.md:364-370`; `SU26SE101_VIETRIDE_technical_context_v7.md:1851-1857,4268-4357`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommand.cs:10-24`; `apps/booking/src/VietRide.Booking.Api/Controllers/Requests/CreateBookingRequest.cs`; `BACKEND_SOURCE_OF_TRUTH.md:1246-1270,1666-1788`. |

### Task 36.3 — Operator shuttle request query và manual ShuttleTrip creation
| Field | Value |
|---|---|
| stack/owner | dotnet / Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | new CQRS query/command/DTO/validator/handler files under `apps/trip/src/VietRide.Trip.Application/Features/Shuttle/`; `apps/trip/src/VietRide.Trip.Api/Controllers/ShuttleController.cs` (or existing Trip controller only if it owns the exact route); Trip repository abstractions/implementations/configurations needed for filtered query and atomic assignment; Identity validation client abstractions/implementation only if existing client lacks required lookup; `apps/gateway/src/config/routes.ts`; Trip API/unit/integration tests and Swagger tests. |
| forbidden scope | Booking create flow beyond consuming the stable request model; schema/migration shape beyond 36.1; Tracking Socket.IO behavior; passenger/driver lifecycle endpoints, notification/cancel flows, auto-dispatch, payment, `.env`, secrets, new NuGet/TS deps, git ops. |
| depends on | 36.0, 36.1, 36.2. Parallel-safe = no with 36.4 for authorization identity, otherwise disjoint after 36.2 but default serial. |
| invariant flags | Thin controller → `MediatR.Send`; ADR 0004 envelope and Swagger response annotations; GET returns `PagedResult` with allow-listed filter/sort; both endpoints scope every read/write by caller `operatorId`; create uses contract-approved role and `Idempotency-Key`; validate main trip/operator, station `supportsShuttle`, vehicle active/owned, driver role/ownership and driver/vehicle conflict; perform ShuttleTrip create plus selected pending-request assignment in one Trip DB transaction; never assign a request already linked to another shuttle. |
| acceptance | `GET /v1/operator/shuttle-requests` returns only unassigned pending requests for caller tenant and contract-approved grouping/filter; `POST /v1/operator/shuttle-trips` creates exactly one SCHEDULED ShuttleTrip and atomically assigns only eligible selected requests, yielding `PENDING`; cross-tenant, unsupported station, inactive/wrong-owner vehicle, invalid driver and duplicate/already-assigned requests return contract-approved errors with no partial state; retries respect idempotency; Gateway route enforces approved operator roles; API/integration tests and Trip verification suite green. |
| source citations | `BE_TIMELINE_VU.md:366-370`; `SU26SE101_VIETRIDE_technical_context_v7.md:4313-4347`; `db-schema/trip-route-vehicle/schema.sql:549-597`; `BACKEND_SOURCE_OF_TRUTH.md:1167,1246-1270,1666-1710`; `apps/gateway/src/config/routes.ts:124-205`; `AGENTS_DOTNET.md` Controller/CQRS/Idempotency sections. |

### Task 36.4 — Shuttle Tracking authorization, room isolation và GPS broadcast reuse
| Field | Value |
|---|---|
| stack/owner | nest / Tracking |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files (write set) | `apps/tracking/src/location/location.gateway.ts`; `apps/tracking/src/location/location.constants.ts`; shuttle DTO/schema and authorization-adapter/client files under `apps/tracking/src/{location,authorization}/`; module/provider wiring only when required; `apps/tracking/src/**/*.spec.ts`; `apps/tracking-e2e/**` or existing Tracking E2E location; `scripts/test-tracking-phase*.js` only if the existing phase workflow requires it. |
| forbidden scope | Trip/Booking domain behavior and migrations; Gateway; Prisma schema unless Task 36.0 proves a local persistence need; main-trip `joinTripTracking` contract/regression; new npm dependencies; `.env`, secrets, `TASK.md`, `CHANGELOG_AI.md`, git ops. |
| depends on | 36.0, 36.1, 36.3. Parallel-safe = no: relies on the finalized Trip authorization/internal seam. |
| invariant flags | LF `.ts`; reuse RS256/JWKS handshake authentication and existing adapter pattern; separate `shuttle:{shuttleTripId}` room, never `trip:{mainTripId}`; every join/GPS action authorizes user against shuttle manifest/driver assignment through the 36.0 seam; no user-supplied room name; validate UUID/location payload with Zod; maintain existing pino/no `console.log`, Socket.IO ack error shape and Redis namespace ownership; no bypass of passenger/driver tenant authorization. |
| acceptance | Authorized assigned passenger can join only its shuttle room; assigned shuttle driver can emit GPS that reaches only that room; unassigned passenger, main-trip passenger without shuttle assignment, wrong driver, malformed UUID and unauthenticated socket are denied; existing main-trip join and GPS E2E tests stay green; lint/test/build and Socket.IO E2E verification pass. |
| source citations | `BE_TIMELINE_VU.md:368`; `SU26SE101_VIETRIDE_technical_context_v7.md:4277-4282,4341-4351`; `apps/tracking/src/location/location.gateway.ts:51-171`; `BACKEND_SOURCE_OF_TRUTH.md:92,106,1666-1710`; `AGENTS_NESTJS.md:29-36,224-239,260-292`. |

### Task 36.5 — Cross-service acceptance suite và operational verification
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | smoke-test |
| owned files (write set) | focused API/contract/integration tests under `apps/booking/tests/**`, `apps/trip/tests/**`, `apps/tracking-e2e/**`; `docs/api/postman/vietride.postman_collection.json` only when Task 36.0 has approved endpoint examples and the existing collection convention supports them; `docs/handoff/day-36-plan.md` Progress tracker only. |
| forbidden scope | Feature code redesign; production config, `.env`, secrets; schema changes; new dependencies; unrelated Postman collections/docs; git ops. |
| depends on | 36.0–36.4. Parallel-safe = no; final integration gate. |
| invariant flags | Test with distinct operator tenants; preserve ADR 0004 envelopes and idempotency replay semantics; do not rely on real Google Maps, Firebase, or production credentials; no fake success that bypasses the Trip/Booking/Tracking authorization seams. |
| acceptance | Automated flow: valid booking shuttle selection → exactly-once pending request → tenant-scoped operator list → manual ShuttleTrip + passenger assignment → authorized shuttle Socket.IO GPS; negative flow proves non-`supportsShuttle` Station rejection and no persistent request; re-run detects no duplicate; service health endpoints remain green. All required build/format/test/lint commands from Success criteria pass or failures are recorded with owner. |
| source citations | `BE_TIMELINE_VU.md:363-371`; `SU26SE101_VIETRIDE_technical_context_v7.md:4313-4357`; `AGENTS_DOTNET.md` Build/Test; `AGENTS_NESTJS.md:38-46,250-292`; `BACKEND_SOURCE_OF_TRUTH.md:1246-1270,2660-2675`. |

## Dispatch order
1. Task 36.0 → mandatory architecture/contract gate. No code dispatch until Q1–Q7 are resolved and plan review approves the resulting decision set.
2. Task 36.1 → migration/domain baseline.
3. Task 36.2 → Booking registration seam after 36.1.
4. Task 36.3 → operator query/create after the pending-request seam is stable.
5. Task 36.4 → Tracking after Trip authorization surface exists.
6. Task 36.5 → end-to-end verification after all feature tasks.

## Progress tracker
> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task` (Step 3) with the task's review verdict. **Informational only — NOT audit evidence.** `/audit-day` MUST re-verify every task independently against the SOT; it must never treat a completed row (or a worker self-report) as proof. A row is bookkeeping, not a passed audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 36.0 | todo | — | — | Chờ human quyết định Q1–Q7. |
| 36.1 | todo | — | — | Phụ thuộc 36.0. |
| 36.2 | todo | — | — | Phụ thuộc 36.0, 36.1. |
| 36.3 | todo | — | — | Phụ thuộc 36.0–36.2. |
| 36.4 | todo | — | — | Phụ thuộc 36.0, 36.1, 36.3. |
| 36.5 | todo | — | — | Final integration gate. |

Legend: todo / in progress / done (reviewer APPROVED + human `/verify`) / done-with-carryover / blocked

## Reopening addendum — 2026-07-31

Day 36 feature code exists, but closure is reopened because the real E2E is not green. This
addendum does not rewrite the original plan, questions, or tracker history. The approved repair
scope and verification now live in
`docs/handoff/day-36-43-fe-gap-repair-plan.md`:

- Harness-readable idempotency labels must map to process-local memoized UUID-v4 values; the same
  label replays the same key, while different labels and a fresh process receive fresh UUIDs.
- `BookingShuttleConfirmedIntegrationEventHandler` must join the durable
  `EfIntegrationEventInbox<TripDbContext>` transaction through `IUnitOfWork` instead of opening a
  nested transaction.
- Closure requires five confirmed Bookings, 15 Tickets, 15 unique Shuttle manifests, complete
  Inbox markers, replay without duplicates, and no confirmation message in DLQ.

The old rows below remain historical planning context. Final repaired evidence belongs in
`docs/handoff/day-36-checklist.md` and the combined repair checklist.

## Open questions
Các điểm dưới đây không được API contract/BSOT hiện tại quyết định. Cần human resolve trước khi dispatch Task 36.0/code.

**Q1 — Shape của booking shuttle request.** Passenger chọn shuttle ở `pickup`, `dropoff`, hay cả hai trong cùng booking? Với booking nhiều ghế, một `ShuttlePassenger` là booking owner hay một row cho từng ticket/passenger seat? `ShuttlePassenger` schema không có `passenger_id`, trong khi technical context gọi manifest “từng người”.

**Q2 — Lifecycle và reliability của request.** `ShuttlePassenger` được tạo khi Booking `PENDING_PAYMENT`, khi `CONFIRMED`, hay cần reservation/compensation riêng cho VNPay timeout và booking cancel? Booking DB và Trip DB tách biệt, nên cần chọn explicit integration event (tên/payload/consumer/idempotency) hoặc internal HTTP saga; hiện BSOT §7.3 không có shuttle event.

**Q3 — Authoritative Station/direction mapping.** Với INBOUND, booking pickup phải bằng origin Station; với OUTBOUND, booking dropoff phải bằng destination Station hay có thể là terminal khác? Các trường `stationId` không có trong `shuttle_passengers`, nên tiêu chí gán request vào ShuttleTrip cần được contract xác định rõ.

**Q4 — Operator manual assignment selection.** `POST /operator/shuttle-trips` nhận danh sách `shuttlePassengerIds` cụ thể hay phải tự gán toàn bộ pending request theo `(mainTripId, direction)` như prose §6.14? Timeline nói “links ShuttlePassenger records” còn technical context nói “assign tất cả matching”, đây là khác biệt product behavior.

**Q5 — Day-36 lifecycle scope.** Timeline chỉ yêu cầu create/list/link nhưng technical context mô tả operator cancel request, driver picked-up/delivered, và ShuttleTrip start/complete. Xác nhận các mutation/notification đó deferred hay phải có endpoint/consumer trong Day 36.

**Q6 — Tracking GPS persistence and authorization.** Tracking schema/implementation hiện model `tripId` cho GPS. Shuttle có dùng `shuttleTripId` trực tiếp (cần local model/query-key evolution) hay adapter map shuttle sang `mainTripId` nhưng room tách biệt? Cần chốt internal Trip endpoint/payload xác thực passenger/driver shuttle và REST latest-location semantics.

**Q7 — Subscription module gate.** `enableShuttle` được mô tả là module flag nhưng Day 37 mới làm lifecycle/enforcement. Day 36 có phải enforce flag ngay, hay defer chính thức đến Day 37 để không block baseline trial? 
