# [FE → BE] Đề xuất: cảnh báo lệch lộ trình realtime — socket event `trip:routeDeviation`

> Ngày gửi: 2026-08-12 · Từ: FE Driver/Assistant app · Trạng thái: **BE ĐÃ TRIỂN KHAI — CHỜ DEPLOY**
>
> Tách từ `BE-GAPS.md` mục 7 thành bản riêng.

---

## 1. Vấn đề

App tài xế đã nâng màn Bản đồ tuyến thành **màn theo dõi hành trình** (Mapbox,
vị trí xe realtime, 2026-08-12). Nghiệp vụ cần: khi xe **rời khỏi polyline
tuyến** quá ngưỡng (đi nhầm đường, né trạm, sự cố), crew của chuyến và điều hành
phải được cảnh báo ngay — không đợi ai đó nhìn màn hình giám sát phát hiện thủ công.

Hiện **không có nguồn phát tín hiệu nào** cho việc này:

- Socket `/tracking` đã có `eta:update` và `trip:statusChanged` (delay
  detection) nhưng **không có event lệch tuyến**.
- FE không nên tự tính client-side: app tài xế chỉ có polyline sau khi gọi
  `route-geometry`, tính toán trên máy yếu tốn pin, và quan trọng nhất là
  **điều hành sẽ không nhận được gì** — trong khi BE đã có sẵn cả hai đầu vào
  (polyline tuyến trong DB + GPS xe qua `gps:update`).

**Thiết kế đã chốt:** BE tính lệch tuyến **server-side** từ GPS đã nhận; app chỉ
hiển thị cảnh báo; điều hành nhận cùng event để theo dõi.

## 2. Hiện trạng FE (đã tích hợp sẵn, chỉ thiếu nguồn phát)

| Hạ tầng | Trạng thái FE |
| --- | --- |
| Socket `/tracking` production (GPS ACK, `eta:update`, `trip:statusChanged`) | ✅ Chạy thật trên máy Android |
| Listener `trip:routeDeviation` (`src/features/tracking/tracking-socket.ts` — `onRouteDeviation`) | ✅ Đã đăng ký, chờ event |
| Hook listen-only `useRouteDeviation` (`src/features/tracking/use-route-deviation.ts`) — join room theo `tripId`, tự join lại khi reconnect | ✅ Xong |
| Banner đỏ ở màn theo dõi hành trình (`route-map-screen.tsx`) + card Định vị hành trình (`role-screens.tsx`) | ✅ Xong |
| Tự ẩn cảnh báo khi nhận `ROUTE_RESTORED` **hoặc** sau 5 phút không có event mới (lưới an toàn nếu BE lỡ phát / app rớt mạng) | ✅ Xong |
| Type `TripRouteDeviationEvent` (`src/api/types.ts`) — khai `status` kèm `\| string` để không vỡ khi BE thêm giá trị | ✅ Xong |

→ **BE deploy là chạy, FE không phải sửa gì thêm.**

## 3. Đề xuất

Thêm broadcast vào room `trip:<tripId>` (cùng chỗ đang phát `trip:statusChanged`),
phát cho crew của chuyến + điều hành:

```jsonc
{
  "event": "trip:routeDeviation",
  "payload": {
    "tripId": "…",
    "status": "DEVIATED",          // hoặc "ROUTE_RESTORED"
    "distanceMeters": 850,
    "updatedAt": "2026-08-12T09:30:00Z"
  }
}
```

Chi tiết từng field:

- **`status`**
  - `"DEVIATED"`: xe lệch quá ngưỡng. Đề xuất ngưỡng: **cách polyline > 500 m
    liên tục ≥ 2 phút** — tránh false positive khi GPS nhiễu hoặc xe tấp lề tạm.
    BE chốt con số cuối.
  - `"ROUTE_RESTORED"`: xe đã về lại tuyến (cùng pattern
    `DELAYED`/`DELAY_CLEARED` của delay detection). Cần có để app tắt cảnh báo
    chủ động thay vì chờ timeout 5 phút.
- **`distanceMeters`**: khoảng cách hiện tại từ xe tới polyline, để app hiện
  "lệch ~850 m". Optional với `ROUTE_RESTORED`.
- **`updatedAt`**: ISO 8601, thời điểm BE tính.

Quy tắc nghiệp vụ:

- Chuyến đang chạy **`alternativeRouteId`** thì tính lệch theo **polyline tuyến
  thay thế**, không phải tuyến gốc (nếu không, xe đi đúng tuyến thay thế sẽ báo
  lệch suốt chuyến).
- Tuyến chưa có `PathPolyline` (`geometry: null` — xem `BE-GAPS.md` mục 6.1) thì
  **không phát event** — không có chuẩn để so.
- Trong lúc đang `DEVIATED`, nếu tiện thì phát lại event theo nhịp (ví dụ mỗi
  60s, `distanceMeters` mới) — khớp luôn với lưới an toàn 5 phút phía FE; không
  bắt buộc.

## 4. Câu hỏi cho BE

1. Ngưỡng phát hiện (khoảng cách + thời gian duy trì) BE chốt là bao nhiêu? FE
   đề xuất 500 m / 2 phút nhưng theo con số của BE.
2. Đang `DEVIATED` mà chuyến kết thúc (`COMPLETED`/`CANCELLED`) thì có phát
   `ROUTE_RESTORED` chốt sổ không, hay FE tự clear theo trạng thái chuyến? (FE
   hiện tự clear khi đổi chuyến — hỏi để khớp hành vi.)
3. Có ghi bản ghi notification (inbox `GET /v1/notifications`) hoặc lưu log lệch
   tuyến cho Operator xem lại không, hay chỉ broadcast tức thời? FE không cần
   inbox cho driver, nhưng điều hành có thể cần lịch sử.
4. Event có phát cho cả role điều hành qua kênh nào (cùng room `trip:<tripId>`
   hay dashboard riêng)? FE hỏi để biết phạm vi, không ảnh hưởng app driver.

## 5. Ưu tiên

- **P1:** Phát `DEVIATED` + `ROUTE_RESTORED` theo schema mục 3 — FE đã tích hợp
  xong toàn bộ phần nhận/hiển thị, chỉ chờ nguồn phát.
- **P2:** Nhịp phát lại 60s khi đang lệch + log/inbox cho điều hành — làm sau được.

---

*Liên quan: `BE-GAPS.md` mục 6 (polyline `route-geometry`, pattern
`DELAYED`/`DELAY_CLEARED` của `trip:statusChanged`), `API-Tracking.md` (socket
`/tracking`, `gps:update`, room `trip:<tripId>`).*

---

## 6. Phản hồi chính thức từ BE (2026-08-12)

### 6.1 Kết luận

**Chấp nhận nhu cầu realtime P1, nhưng không chấp nhận nguyên trạng toàn bộ mô tả và contract ở
mục 1–5.** Backend đã có chuỗi phát hiện/cảnh báo lệch tuyến; khoảng trống thực tế là chưa có
Socket.IO event trạng thái lệch tuyến và chưa có transition trở lại tuyến.

Hiện trạng đã kiểm chứng trong code/SOT:

- Tracking đã tính khoảng cách từ GPS raw tới effective route, với ngưỡng canonical
  **`distance > 500 m` liên tục `> 120 giây`**.
- Khi đủ ngưỡng, Tracking tạo Outbox `OffRouteAlert`, publish RabbitMQ
  `tracking.gps.off_route` **một lần cho mỗi đợt lệch tuyến**.
- Notification đã consume event, resolve người nhận là **crew hiện tại + operator admins**, lưu
  notification `OFF_ROUTE_ALERT`, rồi xử lý realtime inbox/FCM theo luồng Notification. Vì vậy
  nhận định “không có nguồn phát tín hiệu nào” chỉ đúng đối với event
  `trip:routeDeviation` trên socket Tracking, không đúng với toàn bộ backend.
- Khi xe trở lại trong ngưỡng, Tracking hiện chỉ xóa state Redis; chưa emit
  `ROUTE_RESTORED` và cũng chưa tạo notification khôi phục.
- Sau lần alert đầu tiên, Tracking hiện không phát lại theo nhịp 60 giây.

### 6.2 Contract realtime BE đồng ý bổ sung

Tên event FE đã tích hợp được giữ nguyên:

```jsonc
{
  "event": "trip:routeDeviation",
  "payload": {
    "tripId": "00000000-0000-4000-8000-000000000033",
    "status": "DEVIATED", // DEVIATED | ROUTE_RESTORED
    "distanceMeters": 850,
    "updatedAt": "2026-08-12T09:30:00Z"
  }
}
```

Quy ước field:

- `tripId`: UUID của chuyến.
- `status`: union đóng `DEVIATED | ROUTE_RESTORED`; FE có thể giữ fallback `| string` để tương
  thích tiến hóa nhưng không nên hiển thị giá trị lạ như một trạng thái đã hỗ trợ.
- `distanceMeters`: số nguyên không âm và **bắt buộc ở cả hai trạng thái**. Với
  `ROUTE_RESTORED`, đây là khoảng cách đo tại GPS update làm xe trở lại tuyến. Việc giữ một shape
  cố định đơn giản hơn cho client và tránh khác biệt optional/null.
- `updatedAt`: ISO 8601 có offset, lấy từ `recordedAt` của GPS update dùng để đánh giá, không phải
  thời gian xử lý cục bộ của server.

BE không đổi integration event `tracking.gps.off_route` hiện có và không tạo
`tracking.gps.route_restored` chỉ để phục vụ banner. `ROUTE_RESTORED` là transition realtime của
Tracking; notification inbox vẫn chỉ ghi cảnh báo `OFF_ROUTE_ALERT` ban đầu.

### 6.3 Kênh và người nhận

Không broadcast cảnh báo này vào room chung `trip:<tripId>`, vì room đó còn có passenger và các
đối tượng được cấp quyền tracking khác. Broadcast cùng payload vào đúng hai room:

- `trip:crew:<tripId>` cho Driver/Assistant đã được authorize và join chuyến;
- `operator:<operatorId>:fleet` cho Operator Dashboard.

Driver/Assistant sau `joinTripTracking` hiện được server tự join cả room chung và room crew, nên
listener hiện tại của FE không cần đổi tên event. Operator Dashboard dùng `joinOperatorFleet`.

### 6.4 Route dùng để đo

- Nếu Trip có `alternativeRouteId`, internal route-geometry đã trả geometry của
  **AlternativeRoute đang có hiệu lực**, không fallback về Route gốc. Yêu cầu đo theo tuyến thay
  thế là đúng và backend hiện đã đáp ứng.
- Nếu polyline của effective route hợp lệ, `geometrySource=ROUTE_POLYLINE` và Tracking đo theo
  polyline đó.
- Nếu `PathPolyline` null/malformed, contract hiện hành trả
  `geometrySource=STOPS_ONLY` và Tracking vẫn đo theo đường nối snapshot origin → TripStops →
  destination. Vì vậy đề xuất “`geometry: null` thì không phát event” **không đúng với contract
  hiện tại**. FE không cần tự phân nhánh theo geometry source; tuy nhiên BE ghi nhận
  `STOPS_ONLY` có độ chính xác thấp hơn đường thực và có thể gây false positive trên tuyến cong.
- Nếu Tracking chưa warm được route geometry hoặc geometry có ít hơn hai điểm thì lần GPS đó được
  fail-open: không đánh giá và không phát cảnh báo.

### 6.5 Trả lời bốn câu hỏi của FE

1. **Ngưỡng:** chốt theo technical context và code hiện tại: khoảng cách `> 500 m` liên tục
   `> 2 phút`. Khi khoảng cách `<= 500 m`, timer/state được clear. Không dùng `>=` ở hai biên.
2. **Trip kết thúc:** FE phải clear banner khi Trip chuyển sang `COMPLETED`, `CANCELLED` hoặc
   `DISRUPTED`; không chờ `ROUTE_RESTORED`. Một terminal lifecycle không có nghĩa xe đã trở lại
   polyline, nên BE không phát fact giả `ROUTE_RESTORED` để “chốt sổ”. Phần triển khai BE cần
   dọn off-route runtime state khi nhận terminal event.
3. **Lịch sử:** đã có notification inbox `GET /v1/notifications` với type
   `OFF_ROUTE_ALERT` cho crew hiện tại và operator admins, đồng thời có FCM/realtime notification
   theo hạ tầng Notification. V1 chưa có bảng/audit timeline riêng cho toàn bộ lịch sử deviation
   hoặc bản ghi `ROUTE_RESTORED`.
4. **Operator:** nhận cảnh báo realtime Tracking qua room
   `operator:<operatorId>:fleet`; không phụ thuộc việc dashboard join từng room Trip. Operator
   admins đồng thời nhận durable `OFF_ROUTE_ALERT` qua Notification. `OPERATOR_STAFF` có thể xem
   realtime fleet nhưng không nằm trong tập người nhận notification off-route hiện tại.

### 6.6 Lưu ý tích hợp FE

- Timeout tự ẩn sau 5 phút tiếp tục là fail-safe UI. BE đã triển khai heartbeat `DEVIATED` theo
  GPS, không quá một lần mỗi 60 giây, nên banner được làm mới khi xe vẫn lệch. Nguồn clear chuẩn
  vẫn là `ROUTE_RESTORED` hoặc terminal Trip status.
- Heartbeat chỉ phát Socket.IO với `distanceMeters` mới; không tạo thêm Outbox/notification nên
  không spam inbox hoặc FCM.
- Code và API contract đã được cập nhật; FE có thể dùng nguyên event/listener hiện tại sau khi
  phiên bản BE chứa thay đổi này được deploy.
