# Tracking Service — Timeline Hoàn Thành Full Service

> **Quy tắc cho AI**: Khi nhận task liên quan Tracking Service, AI PHẢI đọc file này trước,
> xác định Phase hiện tại (Phase chưa `[x]` đầu tiên), chỉ làm đúng scope Phase đó,
> verify xong mới báo done. TUYỆT ĐỐI không tự chuyển sang Phase tiếp theo.

## Tóm Tắt

Tracking hiện đã xong nền MVP realtime: Socket.IO, `joinTripTracking`, `gps:update`, Redis latest/buffer, Prisma riêng, e2e mock auth. Các bước tiếp theo sẽ hoàn thiện full Tracking Service theo hướng chia nhỏ, mỗi task độc lập, test được bằng e2e/mocks vì các service phụ thuộc như Identity/Trip/Booking/Parcel/Notification chưa hoàn chỉnh. Bỏ qua manual test; nghiệm thu từng task bằng e2e + lint + test + build.

## Phase Progress

- [ ] Phase 1 — Hoàn Thiện Nền Realtime Và Dev Testability
- [ ] Phase 2 — GPS Persistence Batch Job
- [ ] Phase 3 — REST Fallback Endpoints Cho Tracking Data
- [ ] Phase 4 — Dynamic ETA Engine
- [ ] Phase 5 — Approaching Alert
- [ ] Phase 6 — Off-route Detection
- [ ] Phase 7 — Trip Delayed Detection
- [ ] Phase 8 — Outbox Publisher
- [ ] Phase 9 — Real Authorization Adapter
- [ ] Phase 10 — Hardening Và Final Acceptance

---

## Phase 1 — Hoàn Thiện Nền Realtime Và Dev Testability

**Thời lượng:** 1 ngày
**Mục tiêu:** Tracking dễ test độc lập khi chưa có JWT thật và chưa có service phụ thuộc.

### Scope

- Thêm `TRACKING_MOCK_AUTH_ENABLED` chỉ dùng local/test:
  - `passenger-token` -> `PASSENGER`
  - `driver-token` -> `DRIVER`
  - `assistant-token` -> `ASSISTANT`
  - `operator-token` -> `OPERATOR_STAFF`
- Chuẩn hóa authorization adapter thành interface rõ ràng:
  - mock adapter cho e2e/dev
  - HTTP adapter để phase sau gọi Trip/Booking/Parcel
- Thêm e2e cho mock auth mode:
  - connect token hợp lệ
  - token sai -> `UNAUTHORIZED`
  - role không đủ quyền gửi GPS -> `ACCESS_DENIED`

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 2 — GPS Persistence Batch Job

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Redis GPS buffer được flush vào PostgreSQL bằng scheduled repeat job.

### Scope

- Hoàn thiện `gps-batch` queue:
  - repeat interval mặc định 5 phút
  - env `TRACKING_GPS_FLUSH_ENABLED`
  - env `TRACKING_GPS_FLUSH_INTERVAL_MS`
- Batch flush behavior:
  - đọc `tracking:active_trips`
  - đọc `tracking:gps_buffer:{tripId}`
  - validate từng GPS sample
  - insert vào `gps_trails`
  - chỉ xóa buffer sau khi insert thành công
- Thêm guard chống malformed JSON trong buffer.
- E2E/unit test:
  - flush 2 trip thành công
  - buffer rỗng không insert
  - malformed row bị bỏ qua
  - DB insert lỗi thì không clear buffer

### Output hoàn thành

- GPS realtime vẫn chạy
- GPS history bắt đầu được persist

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 3 — REST Fallback Endpoints Cho Tracking Data

**Thời lượng:** 1 ngày
**Mục tiêu:** Dashboard/app có REST fallback khi refresh hoặc khi miss realtime event.

### Scope

- Thêm protected REST endpoints trong tracking service:
  - `GET /api/v1/tracking/trips/:tripId/latest`
  - `GET /api/v1/tracking/trips/:tripId/trail?from&to`
  - `GET /api/v1/tracking/trips/:tripId/eta`
- `latest` đọc Redis `tracking:latest:{tripId}`.
- `trail` đọc PostgreSQL `gps_trails`, phân trang hoặc giới hạn time range.
- `eta` đọc Redis `tracking:eta:{tripId}:{stopId}` theo dữ liệu cached.
- E2E:
  - missing/invalid auth -> 401
  - unauthorized trip -> 403
  - latest not found -> 404 hoặc `{ latest: null }` theo contract nội bộ
  - trail trả đúng order `recordedAt ASC`
- Ghi chú: Swagger không bắt buộc ở phase này; test bằng e2e.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 4 — Dynamic ETA Engine

**Thời lượng:** 2-3 ngày
**Mục tiêu:** Tính ETA động và broadcast `eta:update`.

### Scope

- Thêm ETA module:
  - nhận GPS update từ `LocationService`
  - lấy route stops snapshot qua `TripDataProvider` interface
  - tính ETA đến stop tiếp theo
  - cache Redis `tracking:eta:{tripId}:{stopId}` TTL 60s
- Điều kiện recalculate:
  - xe di chuyển hơn 500m từ lần tính ETA trước
  - hoặc ETA còn dưới 15 phút
- Không ghi đè `TripStop.estimatedArrivalTime` trong DB.
- Broadcast Socket.IO:
  - event `eta:update`
  - room `trip:{tripId}`
- E2E/unit:
  - GPS update chưa vượt threshold -> không recalculate
  - GPS update vượt 500m -> update Redis + broadcast
  - ETA dưới 15 phút -> recalculate
  - TripDataProvider fail -> không crash GPS realtime

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 5 — Approaching Alert

**Thời lượng:** 2 ngày
**Mục tiêu:** Khi ETA tới pickup stop đạt ngưỡng, tạo event cho Notification Service.

### Scope

- Thêm `ApproachingAlertService`:
  - wave 1: ETA <= 30 phút
  - wave 2: ETA <= 10 phút
- Dedupe Redis:
  - `tracking:approaching_notified:{tripId}:{bookingId}:w1`
  - `tracking:approaching_notified:{tripId}:{bookingId}:w2`
- Dữ liệu booking lấy qua `BookingDataProvider` interface.
- Event tạo qua Outbox:
  - `ApproachingAlert`
  - payload gồm `tripId`, `bookingId`, `stopId`, `etaMinutes`, `wave`
- E2E/unit:
  - wave 1 publish đúng 1 lần
  - wave 2 publish đúng 1 lần
  - booking `CANCELLED`/`NO_SHOW` không publish
  - terminal pickup không xử lý ở Tracking, vì theo spec thuộc Trip service job

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 6 — Off-route Detection

**Thời lượng:** 2-3 ngày
**Mục tiêu:** Detect xe lệch tuyến và tạo `OffRouteAlert`.

### Scope

- Thêm route geometry provider:
  - phase đầu dùng mocked route polyline trong e2e
  - production adapter gọi Trip-Route-Vehicle sau
- Algorithm:
  - khoảng cách tới route segment gần nhất > 500m
  - liên tục > 2 phút mới alert
  - Redis `tracking:off_route_since:{tripId}` để lưu timer
- Khi xe quay lại route trước 2 phút:
  - clear timer
  - không publish alert
- Event qua Outbox:
  - `OffRouteAlert`
  - payload gồm `tripId`, `latitude`, `longitude`, `distanceMeters`, `detectedAt`
- E2E/unit:
  - GPS drift ngắn không alert
  - lệch tuyến liên tục đủ 2 phút -> publish 1 alert
  - quay lại route -> clear Redis key

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 7 — Trip Delayed Detection

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Detect delayed overlay từ dynamic ETA, không thêm field DB.

### Scope

- Thêm delayed detection job:
  - chạy repeat mỗi 5 phút
  - đọc dynamic ETA Redis
  - so với static ETA từ `TripDataProvider`
  - nếu dynamic ETA - static ETA > 30 phút -> Outbox `TripDelayed`
- Dedupe delayed event theo trip/stop/window để tránh spam.
- Broadcast Socket.IO:
  - `trip:statusChanged` hoặc `eta:update` kèm delayed flag, theo contract hiện tại.
- E2E/unit:
  - delay <= 30 phút -> không publish
  - delay > 30 phút -> publish
  - duplicate detection không publish lại liên tục

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 8 — Outbox Publisher

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Outbox events được publish sang RabbitMQ `vietride.events`.

### Scope

- Thêm Outbox module:
  - poll `outbox_events` status `PENDING`/`FAILED`
  - mark `PUBLISHING`
  - publish RabbitMQ
  - mark `PUBLISHED`
  - fail thì tăng `retryCount`, lưu `lastError`
- Routing keys:
  - `tracking.trip.delayed`
  - `tracking.route.off_route`
  - `tracking.vehicle.approaching`
- E2E/unit:
  - pending event publish success -> `PUBLISHED`
  - publish fail -> `FAILED`, retry count tăng
  - malformed payload không crash poller

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 9 — Real Authorization Adapter

**Thời lượng:** 2 ngày, phụ thuộc service khác có endpoint
**Mục tiêu:** `joinTripTracking` verify quyền thật qua Trip/Booking/Parcel.

### Scope

- Implement HTTP adapters:
  - Booking: passenger booking owner có quyền xem trip
  - Trip: driver/assistant/operator thuộc trip/operator
  - Parcel: sender/recipient thuộc parcel trên trip
- Giữ mock adapter cho test/local.
- Tất cả HTTP call dùng internal JWT từ Gateway convention hoặc service-to-service config phù hợp.
- E2E dùng mock HTTP server:
  - passenger owner -> allowed
  - parcel recipient -> allowed
  - unrelated user -> denied
  - downstream timeout -> `ACCESS_DENIED` hoặc `TRACKING_AUTH_UNAVAILABLE` theo error policy

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking-e2e:e2e
npx nx run tracking:build
```

---

## Phase 10 — Hardening Và Final Acceptance

**Thời lượng:** 1 ngày
**Mục tiêu:** Full tracking service ổn định, test coverage đủ, không phụ thuộc manual.

### Scope

- Cleanup app skeleton:
  - bỏ hoặc giữ `/api` hello endpoint tùy convention, nhưng không ảnh hưởng tracking.
- Thêm health/ready rõ hơn:
  - `/health` liveness
  - `/ready` check Redis/RabbitMQ/Prisma nếu cần
- Review env:
  - Redis TTL constants
  - batch intervals
  - mock auth disabled mặc định
- Full verification:
  ```bash
  npx nx run tracking:lint
  npx nx run tracking:test
  npx nx run tracking-e2e:e2e
  npx nx run tracking:build
  npm run lint:ts
  npm run test:ts
  npm run build:ts
  ```

---

## Public Interfaces / Events Cần Hoàn Thành

- Socket.IO:
  - `joinTripTracking`
  - `gps:update`
  - `eta:update`
  - `trip:statusChanged`
- REST fallback:
  - `GET /api/v1/tracking/trips/:tripId/latest`
  - `GET /api/v1/tracking/trips/:tripId/trail`
  - `GET /api/v1/tracking/trips/:tripId/eta`
- Redis:
  - `tracking:latest:{tripId}`
  - `tracking:gps_buffer:{tripId}`
  - `tracking:eta:{tripId}:{stopId}`
  - `tracking:off_route_since:{tripId}`
  - `tracking:active_trips`
  - `tracking:approaching_notified:{tripId}:{bookingId}:w{1|2}`
- RabbitMQ / Outbox:
  - `tracking.trip.delayed`
  - `tracking.route.off_route`
  - `tracking.vehicle.approaching`

## Assumptions

- Manual test được bỏ qua trong giai đoạn này; e2e là tiêu chí nghiệm thu chính.
- Khi service phụ thuộc chưa có, dùng provider interface + mock adapter trong e2e.
- Không thêm dependency mới trừ khi task cụ thể cần và USER approve trước.
- Không xóa/rename/move file nếu không có lệnh rõ ràng từ USER.
- Không implement `Trip.isDelayed`; delayed chỉ là Redis/event overlay theo source-of-truth.
