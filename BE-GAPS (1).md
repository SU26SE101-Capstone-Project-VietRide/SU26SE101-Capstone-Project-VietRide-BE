# VietRide Driver App — Đề xuất & Yêu cầu phối hợp Backend

> Các điểm cần Backend xác nhận / bổ sung sau khi FE tích hợp bộ API trong
> `docs/Implements` cho app **Driver / Assistant**. Mỗi mục nêu: hiện trạng →
> ảnh hưởng → **đề xuất giải pháp** để BE tham khảo.
>
> Phạm vi: chỉ Android. Hạng mục **Push Notification** tách thành bản riêng
> (`BE-PUSH-NOTIFICATION.md`). Cập nhật: 2026-07-12.

---

## Tóm tắt nhanh

| # | Hạng mục | Trạng thái FE | Cần Backend |
|---|---|---|---|
| 1 | Parcel — Assistant lấy danh sách kiện theo chuyến | Lớp API xong, UI đang mock | **Bổ sung endpoint list** (blocker) |
| 2 | Tracking Socket.IO realtime | Client xong, đang tắt sau cờ | **Deploy/expose `/tracking/socket.io` production** |
| 3 | Route polyline cho Driver | Có contract/backend endpoint | FE tích hợp `GET /v1/driver/trips/{tripId}/route` |

---

## 1. Parcel — thiếu endpoint để Assistant lấy danh sách kiện theo chuyến  ⛔ BLOCKER

**Hiện trạng.** App Assistant đã có sẵn 3 thao tác theo `parcelId`:
`POST /v1/assistant/parcels/{id}/reweigh | confirm-delivery | unload`.
Tuy nhiên **không có endpoint nào để Assistant liệt kê các kiện của chuyến** mình
phụ trách nhằm lấy `parcelId`. Các endpoint list hiện có chỉ phục vụ Passenger
(`/v1/parcels/available-trips`, `/v1/parcels/received`) hoặc Internal
(`/internal/v1/trips/{tripId}/tracking-authorization/parcels`).

**Ảnh hưởng.** Màn "Hàng ký gửi" của app buộc phải chạy dữ liệu mock; không thể nối
API thật dù toàn bộ lớp gọi API + hạ tầng `Idempotency-Key` đã sẵn sàng.

**Đề xuất giải pháp (ưu tiên phương án A).**

**Phương án A — endpoint list theo chuyến (khuyến nghị):**
```
GET /v1/assistant/trips/{tripId}/parcels?page=1&pageSize=20
Auth: role ASSISTANT (đề xuất cho phép cả DRIVER nếu tài xế cũng cần xem)
```
Trả `PagedResult` với các trường tối thiểu app cần để render + thao tác:
```json
{
  "items": [
    {
      "parcelId": "uuid",
      "parcelCode": "PRC123456",
      "status": "LOADED",
      "recipientName": "Nguyen Van A",
      "recipientPhone": "0900000000",
      "dropoffStopId": "uuid",
      "sizeCategory": "SMALL",
      "estimatedWeightKg": 2.5,
      "description": "Gói hàng nhỏ"
    }
  ],
  "page": 1, "pageSize": 20, "totalItems": 1,
  "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false
}
```

**Phương án B — luồng quét QR:** nếu nghiệp vụ thực tế là quét QR trên kiện, xin BE
**chốt QR encode gì** (đề xuất encode thẳng `parcelId`). Khi đó app quét → gọi
`GET /v1/parcels/{parcelId}` (đã có) là đủ, không cần list. Nếu QR chỉ chứa
`parcelCode`, cần thêm endpoint tra `parcelCode → parcelId`.

> Ghi chú: FE đã xác nhận `GET /v1/parcels/{parcelId}` cho phép Assistant cùng
> `operatorId` xem chi tiết — chỉ thiếu bước lấy được `parcelId` ban đầu.

---

## 2. Tracking — Socket.IO realtime chưa sẵn sàng trên production

**Hiện trạng.** Client đã hoàn tất luồng phát GPS realtime foreground
(`expo-location` → Socket.IO `gps:update`, path `/tracking/socket.io`, auth bằng
access token trong handshake). Tuy nhiên probe production ngày 2026-07-10:
```
GET https://api.vietride.online/tracking/socket.io/?EIO=4&transport=polling
→ HTTP 200 text/plain: "VietRide API edge. Backend at /v1/."
```
Đây không phải Socket.IO handshake (kỳ vọng body bắt đầu bằng `0{...}`) → route
chưa được Nginx/hạ tầng áp dụng, hoặc Tracking service chưa expose.

**Ảnh hưởng.** FE đang **tạm tắt** kết nối Socket.IO sau cờ
`EXPO_PUBLIC_TRACKING_ENABLED` (mặc định off) để app không cố kết nối vào endpoint
chưa hoạt động. Khi BE sẵn sàng, FE chỉ cần bật cờ, không phải sửa code.

**Đề xuất giải pháp.**
- Expose `/tracking/socket.io/` qua Nginx/edge, proxy tới Tracking service (WebSocket
  upgrade: `proxy_set_header Upgrade $http_upgrade; Connection "upgrade"`).
- Xác nhận handshake trả `0{...}` tại `https://api.vietride.online/tracking/socket.io/?EIO=4&transport=polling`.
- Lưu ý `TRACKING_CORS_ORIGIN` production **không được** để `*` (service sẽ crash khi
  `NODE_ENV=production`) — set origin cụ thể.
- Báo FE khi deploy xong để bật cờ và test luồng `joinTripTracking` + `gps:update`.

---

## 3. Route polyline cho Driver/Assistant  ✅ BACKEND READY

**Hiện trạng.** Nhóm API polyline (`GET /v1/operator/routes/{id}`) chỉ cho
`OPERATOR_ADMIN` / `OPERATOR_STAFF`; Gateway chặn `DRIVER` / `ASSISTANT`. App hiện
điều hướng bằng cách mở app bản đồ ngoài theo tên trạm đích (không có toạ độ).

**Ảnh hưởng.** Chưa vẽ được tuyến/điểm dừng thực tế trên bản đồ trong app.

**Giải pháp Backend.** Cung cấp endpoint **read-only theo ngữ cảnh chuyến / assignment**
thay vì mở quyền endpoint operator:
```
GET /v1/driver/trips/{tripId}/route
Auth: DRIVER / ASSISTANT (chỉ chuyến được phân công)
```
Trả tối thiểu `pathPolyline` (Google encoded polyline, precision-5) + danh sách stop
kèm toạ độ để app decode và vẽ. Trả `pathPolyline: null` khi route chưa có hình học
(app fallback vẽ theo stops).

Response còn trả `originStation` / `destinationStation` với toạ độ nullable và `stops`
theo thứ tự snapshot của chuyến. Backend chỉ cho JWT `sub` trùng `driverUserId` hoặc
`assistantUserId` của chuyến; trip tồn tại nhưng không được phân công trả `403 FORBIDDEN`.
FE có thể bỏ workaround chỉ mở bản đồ ngoài sau khi tích hợp endpoint này.

> Đây là hạng mục **tùy nhu cầu sản phẩm**, không chặn các luồng hiện tại.

---

## Phụ lục — các điểm FE đã tự xử lý (không cần Backend làm gì)

Ghi lại để hai bên nắm, tránh trùng lặp:
- **Sửa prefix path sai phía client:** `notifications` và `rag/chat` trước đây FE gọi
  nhầm `/api/v1/…`, nay đã đổi đúng `/v1/…` theo tài liệu.
- **Mark-read notification:** đã chuyển sang `POST /v1/notifications/{id}/read` (không
  body) và xử lý đúng `204 No Content`.
- **RAG feedback:** đã tích hợp `POST /v1/rag/messages/{id}/feedback` (lấy
  `assistantMessageId` từ event `done` của luồng SSE) — nút 👍/👎 trong màn trợ lý.
- **Trip status:** app đã map đủ `SCHEDULED / BOARDING / IN_PROGRESS / COMPLETED /
  CANCELLED / DISRUPTED`.
