# BE phản hồi — Realtime WebSocket gaps

**Ngày phản hồi:** 2026-08-11  
**Báo cáo nguồn:** `FE-REPORT-realtime-websocket-gaps-2026-08-10.md`  
**Phạm vi:** Operator Web; Notification Socket.IO và các điểm siết chặt Tracking liên quan trong báo cáo.

## Kết luận

BE đã xử lý đầy đủ các gap thuộc trách nhiệm Backend trong báo cáo:

- Notification Socket.IO đã được đăng ký và khởi tạo thực sự.
- Notification mới được phát realtime sau khi persist và enqueue FCM thành công.
- Nginx đã route trực tiếp `/notification/socket.io/` đến Notification Service.
- Hai route WebSocket Tracking và Notification đều forward đầy đủ proxy headers.
- Tracking đã bổ sung log an toàn cho trường hợp thiếu `operatorId` hoặc không tìm thấy fleet projection.
- Contract Notification realtime đã được tài liệu hóa chính thức.

Các thay đổi Tracking không làm đổi path, tên event hoặc payload hiện hành của FE.

## Phản hồi từng gap

### 1. Notification gateway chưa được đăng ký DI — Đã sửa

`NotificationsRealtimeGateway` đã được thêm vào `NotificationsModule`. NestJS hiện khởi tạo gateway
và mở Socket.IO với path `/notification/socket.io`.

### 2. Notification chưa cài WebSocket adapter/CORS — Đã sửa

Notification đã gắn Socket.IO adapter trong bootstrap và đọc allowlist từ
`NOTIFICATION_CORS_ORIGIN`.

Production cho phép các frontend origin sau:

```env
NOTIFICATION_CORS_ORIGIN=https://app.vietride.online,https://vietride.online
```

Wildcard `*` bị từ chối trong production. `PUBLIC_APP_URL` không thay thế cấu hình CORS này.

### 3. Tạo notification nhưng không emit realtime — Đã sửa

Luồng hiện tại:

1. Persist notification.
2. Enqueue FCM.
3. Best-effort emit `notification:created`.

Nếu realtime emit lỗi, notification đã persist và FCM job không bị rollback. Khi event được replay,
notification giữ nguyên `id`; client cần deduplicate theo `id`.

### 4. Nginx chưa route Notification Socket.IO — Đã sửa

Nginx đã có route riêng:

```text
/notification/socket.io/ -> notification:3002
```

Route nằm trước frontend catch-all nên handshake không còn rơi vào SPA. Gateway không proxy route
WebSocket này.

### 5. Tracking WebSocket thiếu forwarding headers — Đã sửa

Route `/tracking/socket.io/` và `/notification/socket.io/` đều forward `Host`, `X-Real-IP`,
`X-Forwarded-For`, `X-Forwarded-Proto`, `Upgrade` và `Connection`.

Không có thay đổi contract phía FE Tracking.

### 6. `fleet:gps:update` phụ thuộc `operatorId` — Đã siết khả năng chẩn đoán

BE giữ đúng tenant isolation hiện hành:

- Chỉ phát vào room của operator sở hữu trip.
- Không phát chéo operator.
- Thiếu `operatorId` hoặc fleet projection không làm fail ACK của `gps:update`.
- Hai trường hợp bị bỏ qua đã có debug log an toàn để truy vết dữ liệu Identity/Trip.

FE không cần đổi payload hoặc event Tracking cho mục này. BE vẫn cần kiểm tra dữ liệu production nếu
có tài khoản Driver thực tế thiếu `OperatorId`.

### 7. Contract Notification realtime chưa có — Đã bổ sung

Contract chính thức như sau.

## Việc FE cần làm

### Kết nối Notification Socket.IO

Kết nối đến public backend origin đi qua Nginx, dùng namespace mặc định `/`:

```ts
import { io } from "socket.io-client";

const notificationSocket = io(API_ORIGIN, {
  path: "/notification/socket.io",
  autoConnect: false,
  auth: {
    token: accessToken,
  },
});

notificationSocket.connect();
```

Không nối vào Gateway service/container port và không thêm `/v1` vào socket path.

### Xác thực và reconnect

- Dùng Identity User Access Token RS256 trong `auth.token`.
- BE vẫn hỗ trợ fallback `Authorization: Bearer <token>` cho tooling.
- Token thiếu, sai hoặc hết hạn trả `connect_error.message === "UNAUTHORIZED"`.
- Trước khi reconnect sau khi refresh token, cập nhật lại `socket.auth.token`; không tái sử dụng token
  hết hạn vô hạn.

Ví dụ:

```ts
notificationSocket.auth = { token: refreshedAccessToken };
notificationSocket.connect();
```

### Room và quyền truy cập

- Server tự join room `notification:user:{sub}` từ JWT đã xác minh.
- FE không gửi `userId` để chọn room.
- FE không emit event join room cho Notification.

### Event cần lắng nghe

```ts
notificationSocket.on("notification:created", (notification) => {
  // Deduplicate theo notification.id rồi cập nhật inbox/unread count.
});
```

Payload là DTO thô, không bọc `ApiResponse`:

```json
{
  "id": "notification-id",
  "type": "BOOKING_CREATED",
  "title": "...",
  "body": "...",
  "data": {},
  "action": {
    "type": "...",
    "params": {}
  },
  "readAt": null,
  "createdAt": "2026-08-11T10:30:00+07:00"
}
```

Payload realtime không có `userId`, không có `deepLink` và không có envelope. FE điều hướng bằng
`action`; khi `action.type === "NONE"` thì chỉ hiển thị nội dung notification.

### Đồng bộ bền vững

Socket.IO có ngữ nghĩa at-least-once. FE phải:

- Deduplicate theo `id` giữa các lần nhận/reconnect.
- Gọi `GET /v1/notifications` khi mở màn hình hoặc reconnect để bù event bị bỏ lỡ.
- Xem REST inbox là nguồn dữ liệu bền vững.
- Có thể giữ polling làm fallback, nhưng không cần dùng polling 15 giây làm luồng realtime chính.

### Tracking

FE tiếp tục dùng contract Tracking hiện hành:

```text
path: /tracking/socket.io
auth.token: Identity access token
```

Không có event hoặc payload Tracking nào bị đổi bởi lần sửa này. Hai việc FE đã tự ghi nhận trong
báo cáo vẫn nên hoàn thành:

- Refresh access token trước khi reconnect để tránh vòng lặp `UNAUTHORIZED`.
- Tránh mở hai socket Tracking song song không cần thiết trên cùng màn Operations.

## Checklist nghiệm thu FE

- Web từ `https://app.vietride.online` hoặc `https://vietride.online` kết nối Notification thành công.
- Token thiếu/sai/hết hạn nhận đúng `UNAUTHORIZED`.
- Notification mới xuất hiện ngay qua `notification:created`.
- Chỉ đúng người nhận nhận được event; user khác không nhận.
- Event có cùng `id` với row lấy từ REST inbox.
- Payload không có `userId`, không có `deepLink`, timestamp có offset `+07:00`.
- Reconnect không tạo item trùng và REST có thể bù event bị bỏ lỡ.
- Tracking hiện hành vẫn nhận đúng các event GPS/ETA/fleet như trước.

## Điều kiện rollout

Các thay đổi chỉ có hiệu lực trên môi trường sau khi BE deploy đồng thời Notification image và cấu
hình Nginx mới, đồng thời khai báo đúng `NOTIFICATION_CORS_ORIGIN` trên server. Trước thời điểm deploy,
FE vẫn có thể gặp hành vi polling cũ dù code FE đã đúng contract mới.
