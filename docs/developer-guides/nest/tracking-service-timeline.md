# Tracking Service — Timeline Production

> **Quy tắc cho AI**: Khi nhận task liên quan Tracking Service, AI PHẢI đọc file này trước,
> xác định Phase hiện tại (Phase chưa `[x]` đầu tiên), chỉ làm đúng scope Phase đó,
> verify xong mới báo done. TUYỆT ĐỐI không tự chuyển sang Phase tiếp theo.

## Tóm Tắt

Tracking hiện đã có nền MVP realtime: Socket.IO, `joinTripTracking`, `gps:update`,
Redis latest/buffer, Prisma riêng, và verifier JWT thật bằng JWKS từ Identity Service.
Identity Service đã tồn tại và là source of truth cho User Access Token:

- Algorithm: RS256
- Issuer: `vietride-identity`
- Audience: `vietride-api`
- JWKS: `/v1/.well-known/jwks.json`
- Token claims: `sub`, `role`, `email`, optional `operatorId`

Từ thời điểm này, Tracking phải đi theo hướng production:

- KHÔNG thêm mock-auth/string-token flow làm đường chính.
- KHÔNG dùng `passenger-token`, `driver-token`, `assistant-token`, `operator-token`.
- Socket.IO Tracking không route qua Gateway theo `apps/gateway/src/config/routes.ts`; FE kết nối trực tiếp qua Nginx/direct upgrade và gửi Identity access token trong `auth.token` hoặc `Authorization: Bearer <token>`.
- Tracking tự verify User Access Token bằng Identity JWKS.
- Authorization theo trip vẫn phải tách riêng: Identity chỉ xác thực user/role; Trip/Booking/Parcel mới xác nhận user có quyền xem/gửi GPS cho trip cụ thể.

Mỗi phase phải test được bằng e2e/unit theo hướng production. Nếu cần test JWT trong CI, dùng RSA test key/JWKS tương thích Identity, không dùng mock auth adapter cho logic production.

## Phase Progress

- [x] Phase 1 — Identity-backed Realtime Foundation Và FE Socket Contract
- [x] Phase 2 — GPS Persistence Batch Job
- [x] Phase 3 — REST Fallback Endpoints Cho Tracking Data
- [x] Phase 4 — Dynamic ETA Engine
- [x] Phase 5 — Approaching Alert
- [x] Phase 6 — Off-route Detection
- [x] Phase 7 — Trip Delayed Detection
- [x] Phase 8 — Outbox Publisher
- [x] Phase 9 — Trip/Booking/Parcel Authorization Providers
- [x] Phase 10 — Hardening Và Final Acceptance
- [x] Phase 11 — Shuttle GPS Và Google Routes ETA

---

## Phase 1 — Identity-backed Realtime Foundation Và FE Socket Contract

**Thời lượng:** 1 ngày
**Mục tiêu:** Tracking realtime dùng JWT thật của Identity, FE call được Socket.IO, và test không dựa vào mock token.

### Scope

- Chuẩn hóa User Access Token verification:
  - dùng `JoseUserJwtVerifier` làm đường production mặc định.
  - verify issuer `vietride-identity`, audience `vietride-api`.
  - lấy public key từ `JWT_PUBLIC_KEY_URL` trỏ tới Identity JWKS.
  - giữ `USER_JWT_PUBLIC_KEY` chỉ như test/local override bằng RSA public key thật, không phải mock auth.
  - thêm `clockTolerance` ngắn nếu cần đồng bộ với Gateway behavior.
- Chuẩn hóa FE Socket.IO contract:
  - path: `/tracking/socket.io`.
  - FE gửi access token qua `auth: { token: accessToken }`.
  - hỗ trợ fallback `Authorization: Bearer <token>` cho client/tooling.
  - invalid/missing token bị reject connect với `UNAUTHORIZED`.
- Chuẩn hóa role behavior hiện tại:
  - `PASSENGER`, `OPERATOR_STAFF`, `OPERATOR_ADMIN` có thể `joinTripTracking` nếu provider cho phép.
  - chỉ `DRIVER` và `ASSISTANT` được gửi `gps:update`.
  - role không đủ quyền gửi GPS trả ack `{ success: false, error: "ACCESS_DENIED" }`.
- Chuẩn hóa authorization boundary:
  - giữ interface `TrackingAuthorizationAdapter`.
  - Phase 1 chỉ được dùng production-shaped adapter tối thiểu, không mock token.
  - adapter Phase 1 có thể role-gate tạm thời trong khi Trip/Booking/Parcel endpoints chưa sẵn sàng.
  - ghi rõ TODO cho Phase 9 để thay bằng HTTP providers kiểm quyền theo trip thật.
- Chuẩn hóa response/cross-cutting wiring của Tracking:
  - HTTP endpoint trong Tracking phải dùng `ApiResponseExceptionFilter` và `ApiResponseInterceptor`.
  - không dùng ProblemDetails cho REST.
- Thêm e2e Socket.IO theo hướng Identity:
  - valid RS256 Identity-style token connect thành công.
  - invalid token -> connect error `UNAUTHORIZED`.
  - `joinTripTracking` token hợp lệ -> ack success/allowed theo adapter hiện tại.
  - passenger/operator gửi `gps:update` -> `ACCESS_DENIED`.
  - driver/assistant gửi `gps:update` -> ack success và ghi Redis latest/buffer bằng mock Redis client trong test.
- Chuẩn hóa Nx target e2e:
  - nếu repo chưa có e2e target, thêm target `tracking:test:e2e`.
  - không dùng `tracking-e2e:e2e` trừ khi project `tracking-e2e` được tạo rõ ràng.

### Output hoàn thành

- FE có thể kết nối Tracking Socket.IO bằng access token thật từ Identity.
- Tracking không cần mock auth để chạy production.
- Test có thể tạo RS256 token bằng test key tương thích Identity.
- Phase 1 được tick chỉ sau khi lint/test/e2e/build pass.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 2 — GPS Persistence Batch Job

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Redis GPS buffer được flush vào PostgreSQL bằng scheduled repeat job.

### Scope

- Hoàn thiện `gps-batch` queue:
  - repeat interval mặc định 5 phút.
  - env `TRACKING_GPS_FLUSH_ENABLED`.
  - env `TRACKING_GPS_FLUSH_INTERVAL_MS`.
- Batch flush behavior:
  - đọc `tracking:active_trips`.
  - đọc `tracking:gps_buffer:{tripId}`.
  - validate từng GPS sample.
  - insert vào `gps_trails`.
  - chỉ xóa buffer sau khi insert thành công.
- Thêm guard chống malformed JSON trong buffer.
- E2E/unit test:
  - flush 2 trip thành công.
  - buffer rỗng không insert.
  - malformed row bị bỏ qua.
  - DB insert lỗi thì không clear buffer.

### Output hoàn thành

- GPS realtime vẫn chạy.
- GPS history bắt đầu được persist.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
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
- REST endpoints phải verify Identity User Access Token:
  - `Authorization: Bearer <Identity access token>`.
  - missing/invalid token -> 401 envelope.
- Mỗi endpoint phải check trip authorization qua `TrackingAuthorizationAdapter`.
- `latest` đọc Redis `tracking:latest:{tripId}`.
- `trail` đọc PostgreSQL `gps_trails`, giới hạn time range hoặc pagination.
- `eta` đọc Redis `tracking:eta:{tripId}:{stopId}` theo dữ liệu cached.
- E2E:
  - missing/invalid auth -> 401.
  - unauthorized trip -> 403.
  - latest not found -> 404 hoặc `{ latest: null }` nếu contract nội bộ chọn cách này.
  - trail trả đúng order `recordedAt ASC`.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 4 — Dynamic ETA Engine

**Thời lượng:** 2-3 ngày
**Mục tiêu:** Tính ETA động và broadcast `eta:update`.

### Scope

- Thêm ETA module:
  - nhận GPS update từ `LocationService`.
  - lấy route stops snapshot qua `TripDataProvider` interface.
  - tính ETA đến stop tiếp theo.
  - cache Redis `tracking:eta:{tripId}:{stopId}` TTL 60s.
- Điều kiện recalculate:
  - xe di chuyển hơn 500m từ lần tính ETA trước.
  - hoặc ETA còn dưới 15 phút.
- Không ghi đè `TripStop.estimatedArrivalTime` trong DB.
- Broadcast Socket.IO:
  - event `eta:update`.
  - room `trip:{tripId}`.
- E2E/unit:
  - GPS update chưa vượt threshold -> không recalculate.
  - GPS update vượt 500m -> update Redis + broadcast.
  - ETA dưới 15 phút -> recalculate.
  - TripDataProvider fail -> không crash GPS realtime.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 5 — Approaching Alert

**Thời lượng:** 2 ngày
**Mục tiêu:** Khi ETA tới pickup stop đạt ngưỡng, tạo event cho Notification Service.

### Scope

- Thêm `ApproachingAlertService`:
  - wave 1: ETA <= 30 phút.
  - wave 2: ETA <= 10 phút.
- Dedupe Redis:
  - `tracking:approaching_notified:{tripId}:{bookingId}:w1`.
  - `tracking:approaching_notified:{tripId}:{bookingId}:w2`.
- Dữ liệu booking lấy qua `BookingDataProvider` interface.
- Event tạo qua Outbox:
  - `ApproachingAlert`.
  - payload gồm `tripId`, `bookingIds`, `stopId`, `etaMinutes`, `wave`, `userId?` nếu provider biết passenger.
- E2E/unit:
  - wave 1 publish đúng 1 lần.
  - wave 2 publish đúng 1 lần.
  - booking `CANCELLED`/`NO_SHOW` không publish.
  - terminal pickup không xử lý ở Tracking, vì theo spec thuộc Trip service job.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 6 — Off-route Detection

**Thời lượng:** 2-3 ngày
**Mục tiêu:** Detect xe lệch tuyến và tạo `OffRouteAlert`.

### Scope

- Thêm route geometry provider:
  - production-shaped provider interface.
  - adapter production gọi Trip-Route-Vehicle khi endpoint sẵn sàng.
  - e2e có thể dùng fake provider trong Nest testing module, không mock auth.
- Algorithm:
  - khoảng cách tới route segment gần nhất > 500m.
  - liên tục > 2 phút mới alert.
  - Redis `tracking:off_route_since:{tripId}` để lưu timer.
- Khi xe quay lại route trước 2 phút:
  - clear timer.
  - không publish alert.
- Event qua Outbox:
  - `OffRouteAlert`.
  - payload gồm `tripId`, `latitude`, `longitude`, `distanceMeters`, `durationSeconds`, `detectedAt`, `userIds?`.
- E2E/unit:
  - GPS drift ngắn không alert.
  - lệch tuyến liên tục đủ 2 phút -> publish 1 alert.
  - quay lại route -> clear Redis key.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 7 — Trip Delayed Detection

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Detect delayed overlay từ dynamic ETA, không thêm field DB.

### Scope

- Thêm delayed detection job:
  - chạy repeat mỗi 5 phút.
  - đọc dynamic ETA Redis.
  - so với static ETA từ `TripDataProvider`.
  - nếu dynamic ETA - static ETA > 30 phút -> Outbox `TripDelayed` voi `etaNew`, `delayMinutes`, `userIds?` neu provider biet recipients.
- Dedupe delayed event theo trip/stop/window để tránh spam.
- Broadcast Socket.IO:
  - `trip:statusChanged` hoặc `eta:update` kèm delayed flag, theo contract hiện tại.
- E2E/unit:
  - delay <= 30 phút -> không publish.
  - delay > 30 phút -> publish.
  - duplicate detection không publish lại liên tục.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 8 — Outbox Publisher

**Thời lượng:** 1-2 ngày
**Mục tiêu:** Outbox events được publish sang RabbitMQ `vietride.events`.

### Scope

- Thêm Outbox module:
  - poll `outbox_events` status `PENDING`/`FAILED`.
  - mark `PUBLISHING`.
  - publish RabbitMQ.
  - mark `PUBLISHED`.
  - fail thì tăng `retryCount`, lưu `lastError`.
- Routing keys:
  - `trip.trip.delayed`.
  - `tracking.gps.off_route`.
  - `tracking.gps.approaching_stop`.
- E2E/unit:
  - pending event publish success -> `PUBLISHED`.
  - publish fail -> `FAILED`, retry count tăng.
  - malformed payload không crash poller.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 9 — Trip/Booking/Parcel Authorization Providers

**Thời lượng:** 2 ngày, phụ thuộc service khác có endpoint nội bộ
**Mục tiêu:** `joinTripTracking`, REST fallback, và `gps:update` verify quyền trên trip thật.

### Scope

- Implement production HTTP providers:
  - Booking: passenger booking owner có quyền xem trip.
  - Trip: driver/assistant/operator thuộc trip/operator.
  - Parcel: sender/recipient thuộc parcel trên trip.
- Tất cả HTTP call nội bộ dùng Internal JWT convention:
  - HS256.
  - issuer `vietride-gateway` hoặc caller service được chấp nhận theo config.
  - audience `vietride-internal`.
  - header `X-Internal-Auth: Bearer <jwt>`.
  - TTL 120s.
- Identity access token chỉ dùng để xác định user/role ban đầu; không gửi token user sang service khác nếu không cần.
- Authorization failure policy:
  - not member of trip -> `ACCESS_DENIED`.
  - trip not found -> `TRIP_NOT_FOUND`.
  - downstream unavailable/timeout -> `TRACKING_AUTH_UNAVAILABLE` hoặc mapped 503 nếu API contract chọn.
- E2E dùng fake HTTP downstream server/provider:
  - passenger owner -> allowed.
  - parcel recipient -> allowed.
  - unrelated user -> denied.
  - driver/assistant đúng trip -> được `gps:update`.
  - downstream timeout -> error policy đúng.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
```

---

## Phase 10 — Hardening Và Final Acceptance

**Thời lượng:** 1 ngày
**Mục tiêu:** Ổn định đường GPS realtime và hoàn tất chấp nhận production cho route snap, ETA,
Redis state và Google Routes mà không thay đổi payload Socket/REST công khai.

### Scope

- GPS raw/published: project GPS lên route geometry đã cache trong ngưỡng cố định 50 m.
  Latest/Socket/REST dùng điểm published; buffer Redis, `GpsTrail`, fingerprint idempotency và
  off-route luôn dùng điểm raw. Cache miss chỉ phát raw, gọi warm Trip geometry bất đồng bộ và
  dedupe request đang bay; không chờ Trip HTTP trên live GPS path.
- ETA: dùng một `RouteGeometryProvider` cho snap, off-route và Local ETA. Local ETA chiếu vehicle
  và stop lên polyline, tính khoảng cách tích lũy, giữ sequence/progress không lùi; không dùng
  Haversine làm ETA chính. Google Routes là primary khi `GOOGLE_ROUTES_ENABLED=true`, Local là
  fallback; lỗi Google liên tiếp ba lần mở cooldown 300 giây.
- Redis ETA: cache `tracking:eta:{tripId}:{stopId}` 60 giây, state 24 giờ, lock từng trip/stop,
  khoảng cách di chuyển 500 m hoặc ETA dưới 15 phút, và tối thiểu 60 giây giữa hai lần gọi provider.
- Cấu hình: `GOOGLE_ROUTES_API_KEY` bắt buộc khi bật flag; E2E mặc định dùng fake Google HTTP server.
  Real Google E2E chỉ chạy khi `RUN_REAL_GOOGLE_E2E=true`.
- Tương thích Trip: chấp nhận và chuẩn hóa `null` cho `alertRecipientUserIds`, `estimatedArrivalTime`
  và các field tùy chọn liên quan; unit test phải dùng JSON envelope thực tế của Trip.
- Swagger trên production: chỉ đăng ký `/docs` và `/docs-json` khi `TRACKING_SWAGGER_ENABLED=true`.
  Kiểm thử smoke trên hệ thống thật bằng token thật vẫn là gate vận hành bắt buộc sau deploy.
- Thêm health/ready rõ hơn:
  - `/health` liveness.
  - `/ready` check Redis/RabbitMQ/Prisma nếu cần.
- Review env:
  - `JWT_PUBLIC_KEY_URL`.
  - `USER_JWT_PUBLIC_KEY` chỉ cho local/test RSA public key override.
  - Redis TTL constants và batch intervals.
  - không có mock-auth production flag.
- Security review:
  - không log token/request body nhạy cảm.
  - Socket.IO auth error không leak chi tiết token.
  - CORS/origin config phù hợp deploy.
- Full verification:
  ```bash
  npx nx run tracking:lint
  npx nx run tracking:test
  npx nx run tracking:test:e2e
  npx nx run tracking:build
  node scripts/test-tracking-phase10.js
  git diff --check
  npm run lint:ts
  npm run test:ts
  npm run build:ts
  ```

---

## Phase 11 — Shuttle GPS Và ETA

**Điều kiện:** Phase 10 đã đóng và toàn bộ gate lint/unit/E2E/build xanh.

### Scope

- Socket.IO: `joinShuttleTracking`, `shuttle:gps:update`, `shuttle:eta:update`.
- Passenger có manifest `PENDING`, driver được assign hoặc operator cùng tenant mới được truy cập.
- Lấy manifest/stops qua internal Trip API; không broadcast PII của Booking khác.
- Tài xế được gán gọi `POST /v1/driver/shuttle-trips/{shuttleTripId}/stops/{pickupOrder}/pickup`
  với `Idempotency-Key` để Trip chuyển nguyên tử cả nhóm `PENDING` sang `PICKED_UP`.
- ETA đi theo `pickup_order`, bỏ nhóm terminal (`PICKED_UP`, `DELIVERED`, `NO_SHOW`, `CANCELLED`),
  không lùi xuống dưới thứ tự ETA đã ghi và dùng origin Station làm điểm cuối.
- Google Routes dùng `DRIVE` và `TRAFFIC_AWARE` làm provider chính khi `GOOGLE_ROUTES_ENABLED=true`;
  Haversine và tốc độ GPS là fallback cục bộ vì Shuttle không có route geometry cố định.
- Recalculate sau tối thiểu 60 giây khi di chuyển trên 500 m, ETA dưới 15 phút hoặc chuyển sang
  `pickup_order` mới. Ba lỗi Google liên tiếp mở cooldown 300 giây.
- Redis riêng: `tracking:shuttle:latest:{id}` TTL 300 giây, `tracking:shuttle:gps_buffer:{id}` tối đa
  1000/TTL 24 giờ, `tracking:shuttle:eta:{id}:{order}` TTL 60 giây,
  `tracking:shuttle:eta_state:{id}` và lock `tracking:shuttle:eta_lock:{id}:{order}`.
- GPS được ghi, broadcast và ack trước; Google HTTP chạy bất đồng bộ và chỉ phát
  `shuttle:eta:update` khi hoàn tất. Public Socket/REST payload không thêm `etaSource`.
- Shuttle không được ghi `tracking:active_trips`, main `GpsTrail`, hoặc kích hoạt off-route/delay chain của main Trip.
- Bổ sung REST latest/ETA fallback và test authorization, TTL, cancellation, tenant isolation.

### Verify

```bash
npx nx run tracking:lint
npx nx run tracking:test
npx nx run tracking:test:e2e
npx nx run tracking:build
node scripts/test-tracking-phase11-shuttle-google.js
git diff --check
```

Real Google E2E chỉ chạy khi `RUN_REAL_GOOGLE_E2E=true`; E2E mặc định dùng fake Google HTTP server.

## Public Interfaces / Events Cần Hoàn Thành

- Socket.IO:
  - path `/tracking/socket.io`
  - auth `auth.token = <Identity access token>`
  - fallback header `Authorization: Bearer <Identity access token>`
  - `joinTripTracking`
  - `gps:update`
  - `eta:update`
  - `trip:statusChanged`
- REST fallback:
  - `GET /api/v1/tracking/trips/:tripId/latest`
  - `GET /api/v1/tracking/trips/:tripId/trail`
  - `GET /api/v1/tracking/trips/:tripId/eta`
- Identity JWT:
  - issuer `vietride-identity`
  - audience `vietride-api`
  - JWKS `/v1/.well-known/jwks.json`
  - claims `sub`, `role`, `email`, optional `operatorId`
- Redis:
  - `tracking:latest:{tripId}`
  - `tracking:gps_buffer:{tripId}`
  - `tracking:eta:{tripId}:{stopId}`
  - `tracking:off_route_since:{tripId}`
  - `tracking:active_trips`
  - `tracking:approaching_notified:{tripId}:{bookingId}:w{1|2}`
- RabbitMQ / Outbox:
  - `trip.trip.delayed`
  - `tracking.gps.off_route`
  - `tracking.gps.approaching_stop`

## Ghi chú đồng bộ production sau Phase 10

- `NoopTripDataProvider`, `NoopBookingDataProvider`, và `NoopRouteGeometryProvider` chỉ là placeholder khi Trip/Booking/Route chưa có API nội bộ thật cho route stops, pickup bookings, static ETA, route geometry và recipient users.
- Khi các API nội bộ thật đã sẵn sàng, phải thay các `Noop*Provider` bằng HTTP provider production dùng `X-Internal-Auth: Bearer <internal-jwt>` theo chuẩn HS256, issuer `vietride-gateway`, audience `vietride-internal`, TTL 120 giây.
- Tracking authorization cho `joinTripTracking`, REST fallback và `gps:update` đã đi qua HTTP adapter production-shaped. Không được quay lại role-gate hoặc mock token làm đường chính.
- Local/dev có thể để các worker flag ở `false` để tránh job chạy khi dependency chưa đủ. Khi Tracking, Notification, Redis, RabbitMQ và downstream service đã sẵn sàng production, phải bật:
  - `TRACKING_GPS_FLUSH_ENABLED=true`
  - `TRACKING_TRIP_DELAY_ENABLED=true`
  - `TRACKING_OUTBOX_PUBLISH_ENABLED=true`
- Redis của Tracking là state vận hành, không chỉ là cache: latest GPS, GPS buffer, ETA cache, off-route timer, approaching/delayed dedupe đều phụ thuộc Redis. Deploy production phải có Redis ổn định và không coi mất Redis data là vô hại.

## Assumptions

- Identity Service đã có và là source of truth cho User Access Token.
- Tracking Socket.IO không route qua Gateway; Tracking phải tự verify JWT.
- Test JWT được tạo bằng RSA test key/JWKS tương thích Identity, không dùng mock auth token.
- Khi Trip/Booking/Parcel endpoint chưa sẵn sàng, giữ provider interface và role-gate tối thiểu, nhưng không gọi đây là production authorization hoàn chỉnh.
- Không thêm dependency mới trừ khi task cụ thể cần và USER approve trước.
- Không xóa/rename/move file nếu không có lệnh rõ ràng từ USER.
- Không implement `Trip.isDelayed`; delayed chỉ là Redis/event overlay theo source-of-truth.
