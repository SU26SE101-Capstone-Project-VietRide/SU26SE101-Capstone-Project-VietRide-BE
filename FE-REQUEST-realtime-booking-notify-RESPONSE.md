# Phản hồi Backend — Realtime booking notify cho Driver/Assistant

**Ngày cập nhật:** 2026-08-08
**Trạng thái:** Đã implement và xác minh tự động
**Service:** Booking, Notification, Tracking
**Nguồn yêu cầu:** `FE-REQUEST-realtime-booking-notify.md`

## Kết luận

Backend đã bao phủ booking mới, booking hủy, passenger boarded và booking transfer/đổi ghế do
vehicle substitution. FE tiếp tục dùng manifest/seat-map REST làm source of truth; FCM và Socket.IO
chỉ là tín hiệu điều hướng, invalidate và refetch.

## 1. Push và notification inbox

Driver và Assistant được gán cho Trip nhận:

- `BOOKING_CREATED` khi booking chuyển sang `CONFIRMED`.
- `BOOKING_CANCELLED` khi booking đang `CONFIRMED` bị hủy và rời manifest.

FCM data dùng đúng type FE có thể branch trực tiếp:

```json
{
  "type": "BOOKING_CREATED",
  "notificationType": "BOOKING_CREATED",
  "eventId": "uuid",
  "tripId": "uuid",
  "bookingId": "uuid",
  "bookingCode": "VR-20260808-ABCDEFGH",
  "seatNumbers": "[\"A01\"]",
  "departureDateTime": "2026-08-08T03:00:00.000Z",
  "deepLink": "vietride://driver/trips/{tripId}/bookings/{bookingId}"
}
```

Tất cả FCM data là string. Inbox `GET /v1/notifications` lưu object data gốc. Mỗi recipient có
dedupe key theo `eventId + recipientUserId`; Driver và Assistant trùng user chỉ nhận một bản ghi.

## 2. Socket.IO và auth

- Path giữ nguyên: `/tracking/socket.io`.
- Auth giữ nguyên Identity access token qua `auth.token` hoặc Bearer header.
- Client gọi `joinTripTracking({ tripId })`; Tracking authorize rồi tự thêm Driver/Assistant vào
  room nội bộ `trip:crew:{tripId}`.
- Không tạo namespace mới. Passenger room không nhận booking operational events.

`booking:created` được giữ để tương thích. FE mới dùng:

```ts
socket.on('booking:updated', ({ tripId, reason, eventId }) => {
  // dedupe eventId, invalidate manifest/seat-map của tripId rồi refetch REST
});
```

Reason hỗ trợ:

| Reason | Khi phát | Field bổ sung |
|---|---|---|
| `BOOKING_CREATED` | Booking vừa CONFIRMED | `bookingCode`, `seatNumbers` |
| `BOOKING_CANCELLED` | Booking CONFIRMED bị hủy | `cancellationReason` |
| `PASSENGER_BOARDED` | Passenger được tick boarded | `passengerRecordId`, `ticketCode`, `boardedAt` |
| `BOOKING_TRANSFERRED` | Vehicle substitution chuyển booking/ghế | `oldTripId`, `newTripId`, `transfers[]` |

Transfer được emit vào cả crew room Trip cũ và mới. Backend chưa có API đổi ghế phổ thông riêng;
`BOOKING_TRANSFERRED` phản ánh đúng flow transfer hiện có.

## 3. Recovery và polling

Socket/FCM không phải source of truth. Khi nhận tín hiệu hoặc reconnect, FE đọc lại manifest và
seat-map hiện có. Polling 20–30 giây khi màn hình focus vẫn là fallback phía FE và không cần thay
đổi Backend.

## 4. Bằng chứng kiểm thử

| Gate | Kết quả |
|---|---|
| Booking targeted unit tests | 71/71 pass |
| Booking targeted integration tests | 6/6 pass với PostgreSQL local |
| Shared event contracts | 136/136 pass |
| Notification project tests | 292/292 pass |
| Tracking project tests | 299/299 pass |
| Tracking Socket.IO e2e | 18/18 pass |
| Booking Release build | Pass, 0 warning, 0 error |
| Notification build | Pass; dependency source-map warnings không chặn build |
| Tracking build | Pass; 1 dependency source-map warning không chặn build |

## Giới hạn xác minh

- Chưa gửi FCM tới thiết bị thật trong vòng kiểm thử này.
- Chưa chạy full workspace regression; completion gate dùng targeted Booking và full project
  contracts/Notification/Tracking theo phạm vi thay đổi.
- Cần smoke lại RabbitMQ, Redis, Identity JWT, Trip lookup và Firebase credentials trên môi trường
  deploy trước khi gọi là production verified.
