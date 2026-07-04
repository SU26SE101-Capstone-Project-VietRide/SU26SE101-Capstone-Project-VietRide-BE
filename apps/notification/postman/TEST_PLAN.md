# Test Plan — VietRide Notification Service

> QA / pre-release test plan cho **notification** service. Output thực thi: Postman collection
> (`VietRide-Notification.postman_collection.json`) + environment (`VietRide-Notification.postman_environment.json`)
> trong cùng thư mục này.

## 1. Ngữ cảnh

| Mục | Giá trị |
|---|---|
| **Service** | VietRide Notification (NestJS, **event-driven**) |
| **Tech stack** | NestJS, Prisma (Postgres schema `vietride_notification`), Redis + BullMQ (queue + idempotency), RabbitMQ (consumer), Zod (validation), `jose` (JWT), SendGrid (email), Firebase FCM (push) |
| **Base URL — gateway** | `http://localhost:3000` → user path `/v1/notifications` |
| **Base URL — service trực tiếp** | `http://localhost:3002` → user path `/v1/notifications`; + `/health`, `/ready`, `/api`, `/internal/v1/emails` |
| **Auth user** | RS256 access token từ Identity (login). Header `Authorization: Bearer <token>`. issuer `vietride-identity`, aud `vietride-api` |
| **Auth internal** | HS256 ký bằng `INTERNAL_JWT_SECRET`. Header `X-Internal-Auth: Bearer <token>`. issuer `vietride-gateway`, aud `vietride-internal` |

### Đặc thù service (đọc trước khi test)
- **Không có HTTP tạo notification.** Notification sinh ra bởi **consumer RabbitMQ** (`core-events.consumer.ts`). ⇒ Test luồng đầy đủ phải **publish event** (folder 02), không phải gọi REST.
- Endpoint email **chỉ enqueue** (BullMQ) → trả `PENDING`. Gửi SendGrid/FCM thật do **worker** chạy nền, không quan sát qua HTTP response (xem §8 cho 2 mode).
- `/internal/v1/emails` **không** route qua gateway → gọi trực tiếp `:3002`.
- Email render template **trước khi enqueue** → thiếu biến bắt buộc của template ⇒ lỗi (xem TC-19, §8 Q6).

## 2. Danh sách endpoint

| # | Method + Route (qua gateway) | Route trực tiếp service | Auth | Mục đích |
|---|---|---|---|---|
| E1 | — | `GET /health` | none | Liveness |
| E2 | — | `GET /ready` | none | Readiness (prisma+redis+rabbitmq) |
| E3 | — | `GET /api` | none | Default/smoke |
| E4 | `GET /v1/notifications` | `GET /v1/notifications` | user JWT | List notification (paging/sort/unreadOnly) |
| E5 | `PATCH /v1/notifications/:id` | `PATCH /v1/notifications/:id` | user JWT | Mark read → 204 |
| E6 | — (không qua gateway) | `POST /internal/v1/emails` | internal JWT | Enqueue email → 202 |

> Routing keys consumer (folder 02), từ `core-events.constants.ts`:
> `booking.booking.confirmed`, `booking.booking.cancelled`, `booking.booking.refunded`, `payment.wallet.credited`, `payment.wallet.debited`.

## 3. Bảng test plan

| TC | Endpoint | Loại | Input | Kết quả mong đợi |
|---|---|---|---|---|
| TC-01 | E1 | happy | — | 200, `data.status=ok`, `data.service=notification` |
| TC-02 | E2 | happy | — | **200** + `dependencies` đều `ok` (run chính). Nhánh 503 = chaos test thủ công, tách riêng |
| TC-03 | E3 | happy | — | 200, `success=true`, có `data.message` |
| TC-04 | Login | happy | email+password seed | 200, có `accessToken` + `user.id` → lưu env |
| TC-05 | Login | auth | sai password | 4xx, `success=false` |
| TC-06 | E-event | happy (E2E) | publish `booking.booking.confirmed` + poll | Notification `BOOKING_CONFIRMED` của user xuất hiện; capture `notificationId` |
| TC-07 | E4 | happy | token hợp lệ | 200, envelope phân trang đủ field, `items[*].userId == userId` |
| TC-08 | E4 | happy | `unreadOnly=true` | 200, mọi item `readAt=null` |
| TC-09 | E4 | validation | `pageSize=101` | 400 `VALIDATION_FAILED` |
| TC-10 | E4 | validation | `sortBy=hackerField` | 400 |
| TC-11 | E4 | auth | thiếu token | 401 |
| TC-12 | E4 | auth | token sai | 401 |
| TC-13 | E5 | happy | `{read:true}` trên notificationId | 204, no body |
| TC-14 | E5 | idempotency | gọi lại TC-13 | 204 (không lỗi, không double-write) |
| TC-15 | E4 | side-effect | unreadOnly sau mark-read | id vừa đọc **không** còn trong list |
| TC-16 | E5 | business error | uuid không tồn tại / không sở hữu | 404 `NOTIFICATION_NOT_FOUND` (chống IDOR) |
| TC-17 | E5 | validation | param không phải uuid | 400 |
| TC-18 | E5 | validation | `{read:false}` | 400 (chỉ chấp nhận literal `true`) |
| TC-19 | E5 | auth | thiếu token | 401 |
| TC-20 | **E-event dedupe** | idempotency | **republish cùng `message_id`** | routed=true nhưng **đúng 1** notification cho `bookingId` (Redis idem + dedupeKey unique) |
| TC-21 | E6 | happy | body có `message` (biến bắt buộc) | 202, `data.status=PENDING`, lưu `emailDeliveryId` |
| TC-22 | E6 | side-effect | gọi lại TC-21 | 202 + `id` MỚI (không dedupe — §7) |
| TC-23 | E6 | auth | thiếu `X-Internal-Auth` | 401 |
| TC-24 | E6 | auth | token sai chữ ký | 401 |
| TC-25 | E6 | validation | `toEmail` sai format | 400 `VALIDATION_FAILED` |
| TC-26 | E6 | validation | `templateKey` ngoài enum | 400 |
| TC-27 | E6 | validation | thiếu `templateData` | 400 |

> Mapping folder: `00`=TC01-03, `01`=TC04-05, `02`=TC06, `03`=TC07-12, `04`=TC13-19, `05`=TC20, `06`=TC21-27.

## 4. Luồng E2E (thứ tự Collection Runner)

1. **00 Health** — service sống & sẵn sàng.
2. **01 Login** — lấy `accessToken`/`userId` thật từ Identity.
3. **02 Seed Event** — publish `booking.booking.confirmed` qua RabbitMQ Mgmt API → **poll** list tới khi consumer + mapper + DB + FCM-enqueue tạo notification → capture `notificationId`, `seedBookingId`, `seedMessageId`.
4. **03 List** — kiểm tra liệt kê/paging/validation/auth.
5. **04 Mark read** — 204 → idempotent 204 → verify side-effect (biến mất khỏi unreadOnly) → các nhánh 404/400/401.
6. **05 Event dedupe** — republish **cùng `message_id`** → assert chỉ còn **đúng 1** notification cho `bookingId`.
7. **06 Internal emails** — enqueue (payload đủ biến) → 202 → các nhánh auth/validation.

> **Bắt buộc đặt Runner Delay ~1000ms** để bước poll (folder 02) có khoảng nghỉ giữa các lần retry.

## 5. Biến môi trường Postman

| Biến | Nguồn | Ghi chú |
|---|---|---|
| `mode` | điền tay | `safe-local` (fake provider) hoặc `staging-real` (provider thật) — §8 |
| `baseUrlGateway` / `baseUrlService` | điền tay | `:3000` / `:3002` |
| `notificationsPath` | điền tay | `/v1/notifications` (gateway) **hoặc** `/v1/notifications` (trực tiếp service) |
| `identityEmail` / `identityPassword` | điền tay | tài khoản PASSENGER **đã seed** |
| `internalJwtSecret` | điền tay | = `INTERNAL_JWT_SECRET` của môi trường test (≥32 ký tự) |
| `rabbitMgmtUrl` / `rabbitMgmtUser` / `rabbitMgmtPass` / `rabbitVhost` / `rabbitExchange` | điền tay | RabbitMQ Management API để publish event (folder 02) |
| `accessToken` / `userId` / `userRole` | auto (Login) | |
| `internalToken` | auto (pre-request folder 06) | ký HS256 |
| `seedMessageId` / `seedBookingId` / `seedPollAttempt` | auto (folder 02) | `seedMessageId` được republish y hệt ở folder 05 |
| `notificationId` | auto (Seed poll / List) | input cho mark-read |
| `seededNotificationId` | điền tay (fallback) | xem §6 |
| `emailDeliveryId` | auto (Enqueue email) | |
| `toEmail` | điền tay | người nhận test |

## 6. Setup / Teardown dữ liệu test

**Seed trước khi test:**
1. **Tài khoản PASSENGER** trong Identity (cho `identityEmail`/`identityPassword`).
2. **`INTERNAL_JWT_SECRET`** test khớp với `internalJwtSecret`.
3. **RabbitMQ Management** bật (port 15672) + exchange topic `vietride.events` tồn tại + consumer service đang chạy (queue đã bind).
4. **Notification cho user**: dùng **folder 02 Seed Event** (cách chuẩn — đi qua consumer + mapper + dedupe + FCM enqueue).

> **Điều kiện bắt buộc khi publish event** (theo `core-events.consumer.ts` / `core-event-notification.mapper.ts`):
> - Phải set `properties.message_id` — thiếu ⇒ consumer ném `MISSING_MESSAGE_ID`.
> - `payload.userId`, `payload.bookingId` phải là **uuid hợp lệ** — sai schema ⇒ ZodError bị **nuốt im lặng** (đánh dấu processed, **không** tạo notification) ⇒ poll sẽ thất bại mà không có lỗi rõ ràng.
> - `payload.userId` phải = `userId` của tài khoản login thì GET list mới thấy.

**Fallback/Debug (KHÔNG dùng cho test luồng):** insert trực tiếp 1 row vào `vietride_notification.notifications` rồi điền `seededNotificationId`. Cách này **bỏ qua** consumer, Redis idempotency, `dedupeKey`, mapper và **FCM enqueue** (`notifications.service.ts` chỉ enqueue FCM khi `created`) → chỉ phục vụ debug nhanh nhóm mark-read, không phản ánh hành vi thật.

**Teardown:**
- Xóa email delivery rác từ TC-21/22: `DELETE FROM vietride_notification.email_deliveries WHERE to_email = '<toEmail>'`.
- Xóa notification + delivery của các `bookingId` seed (`data->>'bookingId'`) nếu muốn DB sạch.
- Xóa key Redis idempotency nếu cần chạy lại cùng `message_id` trong vòng 24h: `notification:idem:processed:booking.booking.confirmed:<messageId>` (mặc định mỗi lần Seed dùng `message_id` mới nên thường không cần).

## 7. Idempotency & side-effect

| Đối tượng | Tính chất | Kỳ vọng |
|---|---|---|
| `PATCH /v1/notifications/:id` | idempotent | Gọi lại → luôn 204, `readAt` chỉ set lần đầu — **TC-14** |
| **Consumer event** | idempotent theo `message_id` | Republish cùng `message_id` → Redis `processed` skip + `dedupeKey` unique chặn → **đúng 1** notification — **TC-20** (core) |
| `POST /internal/v1/emails` | **non-idempotent**, **không dedupe** | Gọi lại tạo delivery id MỚI — **TC-22**. Nếu nghiệp vụ cần chống gửi trùng → §8 Q3 |

> `dedupeKey = {routingKey}:{messageId}:{userId}:{type}` được tính **server-side** từ `message_id`; publisher không tự đặt được. Vì vậy test dedupe = **republish cùng `message_id`** (không phải tự gửi dedupeKey).

## 8. Cần mock / cần xác nhận với team

**Hai chế độ test provider ngoài (đặt qua biến `mode`):**

| Mode | FCM / SendGrid | Cách verify |
|---|---|---|
| `safe-local` | **fake provider** (không set `FCM_*`/`SENDGRID_API_KEY`) → không gửi ra ngoài | Chỉ verify enqueue (202 PENDING) + notification tạo trong DB. An toàn cho CI/local |
| `staging-real` | provider **thật** (có `SENDGRID_API_KEY`/`FCM_*`) | Verify qua **bảng delivery** (`notification_deliveries`/`email_deliveries` chuyển SENT), **log worker**, và **thiết bị/inbox** thật |

| # | Vấn đề | Trạng thái |
|---|---|---|
| Q1 | **SendGrid sandbox** | Staging có SendGrid sandbox/test mode không, hay dùng key thật? **Cần xác nhận.** |
| Q2 | **FCM verify** | Cách verify push ở staging-real (token thiết bị thật? đọc `notification_deliveries`?). **Cần xác nhận.** |
| Q3 | **Dedupe email nội bộ** | POST 2 lần tạo 2 delivery. Nghiệp vụ có cho phép trùng không? Nếu không → cần idempotency key. **Cần xác nhận.** |
| Q4 | **Verify notification từ event** | Đã giải quyết bằng folder 02 (publish + poll). Cần xác nhận RabbitMQ Mgmt khả dụng ở môi trường test. |
| Q5 | **Mã lỗi login sai** (TC-05) | Do Identity quyết định (`INVALID_CREDENTIALS`?). Bổ sung assert `error.code` khi xác nhận. |
| Q6 | **Thiếu biến template ⇒ 500** | Renderer ném `Error` (vd `EMAIL_TEMPLATE_MISSING_MESSAGE`) → trả **500**, không phải 400 (Zod chỉ check là object). **Cần xác nhận** có nên trả 400 không. |
| Q7 | **Exchange type** | Giả định `vietride.events` là **topic** exchange. Cần xác nhận để routing key dấu chấm khớp binding. |

## 9. Nice to have (edge case hiếm, tách riêng — không trộn run chính)

- E2: nhánh **503** readiness — hạ 1 dependency (tắt redis/rabbitmq) rồi assert 503 `NOTIFICATION_DEPENDENCY_UNAVAILABLE`. Chaos test thủ công.
- Dedupe **lớp DB** riêng: xoá key Redis `processed:*` trước rồi republish cùng `message_id` → ép `dedupeKey` unique (P2002) chặn ở DB.
- E4: `page` vượt `totalPages` → 200, `items` rỗng, `hasNextPage=false`.
- E4: `pageSize=0` / `page=0` → 400 (min=1).
- E5: 2 tài khoản thật — user B mark-read notification của user A → 404 (bản đầy đủ của TC-16).
- E6: template cần biến mà thiếu (vd `OPERATOR_SUBSCRIPTION_NOTICE` thiếu `message`) → kỳ vọng theo Q6.
- E6: internal token **hết hạn** (sửa `exp` quá khứ trong pre-request) → 401.
- Mapper các routing key còn lại (`booking.cancelled/refunded`, `payment.wallet.credited/debited`) → lặp folder 02 với routing key + payload tương ứng, assert `type` đúng.

## 10. Cách chạy

1. Import 2 file JSON (collection + environment), chọn environment **VietRide Notification - Local (safe-local)**.
2. Điền: `identityEmail`, `identityPassword`, `internalJwtSecret`, `rabbitMgmtUser/Pass` (và `mode` nếu test real).
3. Khởi động stack: `docker compose -f infra/docker/docker-compose.yml up` (gateway :3000, notification :3002, identity, postgres, redis, rabbitmq + mgmt :15672).
4. Chạy bằng **Collection Runner** theo thứ tự folder 00→06, **đặt Delay ~1000ms** (cho bước poll folder 02).
5. Để test trực tiếp service thay vì gateway: đổi `notificationsPath=/v1/notifications` và sửa các request user sang `{{baseUrlService}}`.
