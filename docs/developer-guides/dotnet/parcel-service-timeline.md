# Parcel Service — Timeline Production

> **Quy tắc cho AI**: Khi nhận task liên quan Parcel Service, AI phải đọc file này trước,
> xác định Phase hiện tại (Phase chưa `[x]` đầu tiên), chỉ làm đúng scope Phase đó,
> verify xong mới báo done. TUYỆT ĐỐI không tự chuyển sang Phase tiếp theo.

## Tóm Tắt

Parcel hiện tại là skeleton:

- Có `Program.cs`.
- Có `PingController`.
- Có `ParcelDbContext`.
- Có health/ping tests.
- Chưa có aggregate, migration nghiệp vụ, endpoint, outbox events, Hangfire jobs, payment/trip/booking clients.

Production direction:

- .NET 8 Clean Architecture.
- EF Core + PostgreSQL schema `vietride_parcel`.
- MediatR CQRS.
- Idempotency cho mutation endpoints.
- Outbox events qua `IIntegrationEventOutbox.EnqueueAsync`.
- Không gọi RabbitMQ trực tiếp từ handler.
- Không query DB service khác.
- Tracking realtime của parcel là tracking theo trip/vehicle qua Tracking Service, không GPS riêng cho kiện hàng.

## Quyết Định Đã Chốt

- Parcel code format: `VRP-yyyyMMdd-XXXXXXXX`.
- `PENDING` nghĩa là đã thanh toán/accepted, đang chờ load.
- `PENDING_PAYMENT` nghĩa là chưa thanh toán xong.
- `EXTRA_LARGE` bắt đầu ở `PENDING_OPERATOR_REVIEW`, chưa charge.
- Sender phải có Passenger account; guest parcel-only là v2.
- Operator không tạo parcel thay user.
- Gateway `/v1/parcels` phải là `mixed` khi làm delivery token.
- Public delivery token endpoints không cần JWT.
- Money dùng `Money.cs` hiện tại, BIGINT VND đến đơn vị đồng.
- Nếu docs conflict: technical context + `db-schema/parcel/schema.sql` thắng, ghi drift vào review note.

## Source Of Truth

- `AGENTS.md`
- `AGENTS_DOTNET.md`
- `SU26SE101_VIETRIDE_technical_context_v7.md`
- `BACKEND_SOURCE_OF_TRUTH.md`
- `VietRide_API_Contract_v1.md`
- `BE_TIMELINE_VU.md`
- `db-schema/parcel/schema.sql`
- `db-schema/parcel/README.md`
- `apps/booking`, `apps/payment`, `apps/trip` để mirror .NET patterns.

## Phase Progress

- [x] Phase 1 — Foundation, Schema, Runtime Wiring
- [x] Phase 2 — Internal Clients Và Test Stubs
- [ ] Phase 3 — Fare Config Và Create Parcel
- [ ] Phase 4 — Payment, Review, Reweigh
- [ ] Phase 5 — Hangfire Jobs
- [ ] Phase 6 — Load, Unload, Tracking Access
- [ ] Phase 7 — Delivery Confirmation Và Gateway
- [ ] Phase 8 — Transfer, Return, Override, PENDING_OPERATOR_ACTION
- [ ] Phase 9 — Events, Stats, Final Acceptance

---

## Phase 1 — Foundation, Schema, Runtime Wiring

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Dựng nền kỹ thuật Parcel Service đúng chuẩn .NET Clean Architecture.

### Scope

- `ApplicationAssemblyMarker`.
- Aggregates `Parcel`, `ParcelRouteFare`, `ParcelStats`.
- Parcel enums.
- `ParcelDbContext.ConfigurePostgresTypes`.
- `Program.cs` MediatR/DbContext/Redis/Messaging wiring. (Hangfire sẽ wire tại Phase 5.)
- Initial EF migration.
- Repository, EF config, DI.
- Architecture/layering tests.

### Output hoàn thành

- Parcel có database schema production-ready.
- Enum PostgreSQL resolve được runtime.
- Không có cross-service FK.
- Build green.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter Architecture
```

### Hoàn thành ngày 2026-06-25 (sửa lần 2)

- Domain enums (5): `ParcelStatus`, `ParcelSizeCategory`, `ParcelReviewDecision`, `ParcelDeliveryMethod`, + `OutboxEventStatus` (kế thừa từ shared).
- Domain entities (3): `Parcel` (37 fields, 2 factory methods), `ParcelRouteFare` (composite PK), `ParcelStats`.
- `ApplicationAssemblyMarker`.
- EF Core configs cho cả 3 entities (snake_case, enum type mapping, constraints, indexes).
- Repository interfaces + implementations (3 repos).
- `ParcelDbContext.ConfigurePostgresTypes` (4 enum mappings, `NpgsqlNullNameTranslator`).
- `InfrastructureServiceCollectionExtensions` (Redis, JWT, repos).
- `InternalJwtTokenFactory` (HS256).
- `Program.cs` wiring: DbContext, MediatR, Messaging, Redis, Idempotency.
- `ParcelDbContextDesignFactory` (cho design-time migration).
- Architecture layering tests (NetArchTest, 4 rules) — **4 passed**.
- **Initial EF migration** (`InitialCreate`): vô hiệu `parcels`, `parcel_route_fares`, `parcel_stats`, `outbox_events` — schema matching `schema.sql`.
- Build Release — **0 errors, 0 warnings**.
- `dotnet format --verify-no-changes` — **clean**.

### Sửa lần 2 (sau review ngày 2026-06-26)

1. **pgcrypto**: Thêm `CREATE EXTENSION IF NOT EXISTS "pgcrypto"` vào migration's Up/Down.
2. **Enum labels UPPER_SNAKE_CASE**: Đổi từ generic `HasPostgresEnum<T>()` sang non-generic `HasPostgresEnum("name", Enum.GetNames<T>())` — migration annotation đúng `PENDING_OPERATOR_REVIEW,SMALL,...` như schema.sql.
3. **outbox_events.status default string**: Sửa `Column<int>.HasDefaultValue(0)` thành `Column<string>.HasDefaultValueSql("'PENDING'")` — khớp Booking/Payment.
4. **parcel_stats created_at**: `.Ignore(x => x.CreatedAt)` — table upsert không có created_at theo schema.sql.
5. **delivery_method default**: Sửa `DEFAULT 0` thành `DEFAULT ('TERMINAL_PICKUP')` — PG sẽ reject integer default trên enum column.
6. **7 filtered indexes** bổ sung: `additional_payment_deadline`, `transfer_target_trip_id`, `additional_payment_id`, `reviewed_by_user_id`, `confirmed_by_user_id`, `transfer_confirmed_by_user_id`, `returned_by_user_id`.
7. **ParcelRouteFareRepository.GetByIdAsync(Guid) sai**: Composite PK `{RouteId, SizeCategory}`. Đổi contract — interface tự định nghĩa method, không kế thừa `IRepository<ParcelRouteFare, Guid>`. `FindByRouteAndSizeAsync(string)` → `FindByCompositeAsync(Guid, ParcelSizeCategory)`.
8. **Hangfire chưa wire**: Gỡ Hangfire khỏi Phase 1 scope (ghi chú wire tại Phase 5).

---

## Phase 2 — Internal Clients Và Test Stubs

**Thời lượng:** 1 ngày
**Mục tiêu:** Parcel giao tiếp đúng rule với Trip, Payment, Booking, Identity.

### Scope

- `ITripServiceClient`.
- `IPaymentServiceClient`.
- `IBookingServiceClient`.
- optional `IIdentityServiceClient`.
- Internal JWT, correlation ID, retry/circuit breaker.
- Dev/test stubs.

### Output hoàn thành

- Parcel validate được logical FK qua internal HTTP/stub.
- Tests không phụ thuộc service thật.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "InternalClient|Stub"
```

### Carry-over (dependency blockers cho Phase sau)

1. **Booking real endpoint chưa tồn tại**: `GET /internal/v1/bookings/{bookingId}` cần được expose ở Booking service trước khi bật real mode hoặc trước Phase 3 (create parcel cần validate booking reference). Hiện tại real client sẽ 404; dev stub hoạt động bình thường.
2. **Payment validators chỉ nhận BOOKING / BOOKING_REFUND**: Payment service cần mở rộng `ChargePaymentCommandValidator` (cho `PARCEL`) và `RefundToWalletCommandHandler` (cho `PARCEL_REFUND`) trước Phase 3/4 khi cần real payment/refund. Parcel client gửi `PARCEL` / `PARCEL_REFUND` là forward-looking seam.

---

## Phase 3 — Fare Config Và Create Parcel

**Thời lượng:** 2 ngày
**Mục tiêu:** Operator cấu hình giá, passenger xem chuyến còn nhận hàng và tạo parcel.

### Scope

- `POST /v1/operator/parcel-route-fares`.
- `GET /v1/operator/parcel-route-fares`.
- `PATCH /v1/operator/parcel-route-fares/{routeId}/{sizeCategory}`.
- `GET /v1/parcels/available-trips`.
- `POST /v1/parcels`.
- `bookingId` optional.
- Normal parcel -> `PENDING_PAYMENT`.
- `EXTRA_LARGE` -> `PENDING_OPERATOR_REVIEW`.

### Output hoàn thành

- Operator config được fare.
- Passenger tạo được parcel booking-attached hoặc parcel-only.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "ParcelRouteFare|CreateParcel|AvailableTrips"
```

---

## Phase 4 — Payment, Review, Reweigh

**Thời lượng:** 2 ngày
**Mục tiêu:** Hoàn thiện thanh toán, duyệt EXTRA_LARGE, cân lại và phụ phí.

### Scope

- Consume payment events.
- Initiate payment qua `IPaymentServiceClient`.
- `PATCH /v1/operator/parcels/{parcelId}/review`.
- `POST /v1/assistant/parcels/{parcelId}/reweigh`.
- Additional payment flow.

### Output hoàn thành

- Deposit success chuyển `PENDING_PAYMENT -> PENDING`.
- EXTRA_LARGE approve/reject chạy được.
- Reweigh tạo additional payment khi cần.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "Payment|Review|Reweigh"
```

---

## Phase 5 — Hangfire Jobs

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Không để Parcel bị kẹt trạng thái treo.

### Scope

- Wire Hangfire infrastructure: NuGet, DI (`AddHangfire`, `UseHangfireServer`), dashboard.
- Review timeout.
- Additional payment timeout.
- Pending parcel after trip started auto-reject.
- Transfer confirm timeout.
- Operator action re-alert.
- Delivery pending confirm reminder.
- Reject undo window.

### Output hoàn thành

- Không còn trạng thái treo vô hạn.
- Jobs idempotent, guarded by expected status.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "Job|Timeout|Reminder"
```

---

## Phase 6 — Load, Unload, Tracking Access

**Thời lượng:** 2 ngày
**Mục tiêu:** Vận hành hàng trên chuyến xe và hỗ trợ tracking realtime theo trip.

### Scope

- `POST /internal/v1/parcels/{parcelId}/mark-loaded`.
- `POST /v1/assistant/parcels/{parcelId}/unload`.
- `GET /v1/parcels/received`.
- `GET /v1/parcels/{parcelId}`.
- `GET /internal/v1/parcels/{id}`.
- `GET /internal/v1/parcels/{id}/access-check?userId=...`.
- Consume `trip.trip.started`: `LOADED -> IN_TRANSIT`.
- Consume `trip.trip.completed`: affected unresolved `IN_TRANSIT`/operational parcels move to the correct terminal or operator-action path per technical context.
- Parcel detail/list.
- Capacity guarded updates.

### Output hoàn thành

- Assistant load/unload được hàng đúng trip/stop.
- Tracking Service authorize được sender/recipient/operator.
- Parcel realtime tracking hoạt động qua room `trip:{tripId}`.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "Load|Unload|Tracking|TripStarted"
```

---

## Phase 7 — Delivery Confirmation Và Gateway

**Thời lượng:** 2 ngày
**Mục tiêu:** Người nhận xác nhận/reject hàng bằng public token email link.

### Scope

- `POST /v1/parcels/delivery/confirm`.
- `POST /v1/parcels/delivery/reject`.
- Gateway route `/v1/parcels` đổi sang `mixed`.
- Public subpaths cho confirm/reject.
- Token TTL 48h.
- Reject undo 15 phút.

### Output hoàn thành

- Recipient confirm/reject được qua public email link.
- Các Parcel endpoint khác vẫn cần JWT.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "Delivery|Token|Reject"
npx nx run gateway:test
```

---

## Phase 8 — Transfer, Return, Override, PENDING_OPERATOR_ACTION

**Thời lượng:** 2 ngày
**Mục tiêu:** Xử lý sự cố vận hành như hủy chuyến, đổi xe, transfer, return.

### Scope

- `POST /v1/operator/parcels/{parcelId}/request-transfer`.
- `POST /internal/v1/parcels/{parcelId}/confirm-transfer`.
- `POST /v1/operator/parcels/{parcelId}/return`.
- `PATCH /v1/operator/parcels/{parcelId}/status`.
- Trip cancel/disrupted/substitution event handling.
- `PENDING_OPERATOR_ACTION`.

### Output hoàn thành

- Có đường xử lý sự cố vận hành: transfer, return, cancel, override có audit.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet test apps/parcel/VietRide.Parcel.sln -c Release --filter "Transfer|Return|Override|OperatorAction"
```

---

## Phase 9 — Events, Stats, Final Acceptance

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Hoàn thiện integration events, stats, idempotency audit và production acceptance.

### Scope

- Emit all Parcel outbox events.
- Maintain `ParcelStats`.
- Idempotency audit.
- Event registry drift audit.
- Full build/format/test/smoke-test.

### Output hoàn thành

- Parcel end-to-end production journey hoàn chỉnh.
- Timeline tick hết phase.
- Carry-over nếu có được ghi rõ.

### Verify

```bash
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes
dotnet test apps/parcel/VietRide.Parcel.sln -c Release
```

---

## Public Interfaces / Events Cần Hoàn Thành

### REST

- `POST /v1/operator/parcel-route-fares`
- `GET /v1/operator/parcel-route-fares`
- `PATCH /v1/operator/parcel-route-fares/{routeId}/{sizeCategory}`
- `GET /v1/parcels/available-trips`
- `POST /v1/parcels`
- `GET /v1/parcels/received`
- `GET /v1/parcels/{parcelId}`
- `PATCH /v1/operator/parcels/{parcelId}/review`
- `POST /v1/assistant/parcels/{parcelId}/reweigh`
- `POST /internal/v1/parcels/{parcelId}/mark-loaded`
- `POST /v1/assistant/parcels/{parcelId}/unload`
- `POST /v1/parcels/delivery/confirm`
- `POST /v1/parcels/delivery/reject`
- `POST /v1/operator/parcels/{parcelId}/request-transfer`
- `POST /internal/v1/parcels/{parcelId}/confirm-transfer`
- `POST /v1/operator/parcels/{parcelId}/return`
- `PATCH /v1/operator/parcels/{parcelId}/status`
- `GET /internal/v1/parcels/{id}`
- `GET /internal/v1/parcels/{id}/access-check?userId=...`

### RabbitMQ / Outbox

- `parcel.parcel.created`
- `parcel.parcel.loaded`
- `parcel.parcel.unloaded`
- `parcel.parcel.delivered_pending_confirm`
- `parcel.parcel.delivery_confirmed`
- `parcel.parcel.delivery_rejected`
- `parcel.parcel.cancelled`
- `parcel.parcel.rejected`
- `parcel.parcel.returned`
- `parcel.parcel.auto_rejected`
- `parcel.parcel.review_requested`
- `parcel.parcel.transfer_initiated`
- `parcel.refund.initiated`

### Inbound events

- `payment.payment.succeeded`
- `payment.payment.failed`
- `payment.payment.expired`
- `trip.trip.started`
- `trip.trip.completed`
- `trip.trip.cancelled`
- `trip.trip.disrupted`
- vehicle substitution event if present in Trip service.

### Redis

- `parcel:idem:{key}` or equivalent key from shared idempotency middleware.
- Hangfire/Redis keys only through existing infrastructure, no ad hoc business state unless explicitly justified.

## Ghi Chú Đồng Bộ Production Sau Phase

1. Dev stubs như `DevTripServiceClient`, `DevPaymentServiceClient`, `DevBookingServiceClient` chỉ dùng cho local/test. Production phải dùng real HTTP clients.
2. Hangfire jobs chỉ chạy ở môi trường không phải `Testing`.
3. `PENDING` parcel không load sau khi trip `IN_PROGRESS` và hết window phải được auto-reject bằng job.
4. Public delivery token endpoint phải đi qua Gateway mixed route; không mở public toàn bộ `/v1/parcels`.
5. Tracking realtime của Parcel là theo `trip:{tripId}` trong Tracking Service; Parcel chỉ authorize quyền truy cập.
6. Cargo capacity source of truth thuộc Trip/counter integration; Parcel không tự tạo source of truth thứ hai.
7. Outbox event dùng `IIntegrationEventOutbox.EnqueueAsync(eventType, payloadJson)`, không gọi RabbitMQ publisher trực tiếp.
8. Nếu phát hiện drift giữa `AGENTS.md` và code hiện tại, ưu tiên code/shared lib đang chạy nhưng ghi drift vào review note.

## Quy Tắc Cập Nhật Sau Mỗi Phase

Sau khi phase xong, agent phải sửa chính file timeline:

- Tick phase trong `Phase Progress`.
- Ghi thêm `Kết quả phase` dưới phase đó:
  - Ngày hoàn thành.
  - Implement agent.
  - Review agent.
  - Verify đã chạy.
  - Kết quả review.
  - Carry-over.
- Nếu phase chưa pass, không tick; chỉ ghi carry-over.

## Cách Resume Hôm Sau

Người dùng chỉ cần nói:

> Đọc AGENTS.md, AGENTS_DOTNET.md và docs/developer-guides/dotnet/parcel-service-timeline.md. Tiếp tục phase Parcel đầu tiên chưa tick, không làm phase sau.

Agent sẽ đọc timeline, thấy phase đầu tiên chưa `[x]`, rồi làm đúng phase đó.
