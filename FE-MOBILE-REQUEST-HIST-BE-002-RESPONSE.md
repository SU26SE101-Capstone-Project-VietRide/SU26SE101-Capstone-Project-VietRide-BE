# HIST-BE-002 — Phản hồi Backend về điểm đón/trả trong Ticket History

## Trạng thái

`RESOLVED_BE`

- Commit Backend: `cf643c4b` — `fix(history): preserve booked point snapshots`
- Không thay đổi Route origin/destination hiện có.
- Không yêu cầu Mobile tra cứu Trip hiện tại để lấy tên Station hoặc Stop.

## Gap trước khi sửa

Booking đã lưu ID điểm đón/trả hành khách chọn, nhưng Booking History chỉ dùng
`tripSnapshotOriginName` và `tripSnapshotDestName`. Đây là hai đầu của Route, không phải chặng hành
khách đã đặt.

Ví dụ Route `A → B → C → D`:

- Hành khách đặt `C → D` nhưng lịch sử cũ hiển thị `A → D`.
- Một vé Stop-to-Stop hợp lệ như `B → C` cũng bị hiển thị `A → D`.

Ngoài ra, contract cũ không trả pickup ID và không có snapshot tên, địa chỉ hoặc thời gian dự kiến.
Mobile vì vậy không thể phục dựng chặng đã đặt một cách ổn định khi Trip hoặc Route đã thay đổi.

## Backend đã sửa

Booking hiện lưu snapshot riêng cho hai điểm hành khách đã chọn:

- `type`: `STATION` hoặc `STOP`.
- `id`: canonical ID của Station hoặc Stop.
- `displayName`: tên hiển thị tại thời điểm đặt/chỉnh vé.
- `address`: địa chỉ tại thời điểm đặt nếu nguồn Trip có cung cấp; hiện có thể là `null`.
- `plannedAt`: thời gian dự kiến tại điểm đó ở thời điểm đặt/chỉnh vé.

Snapshot được cập nhật trong các luồng:

- Tạo vé một chiều.
- Tạo vé khứ hồi.
- Chỉnh điểm đón.
- Chỉnh điểm trả.
- Chấp nhận điểm thay thế của Route Change khi metadata đã đóng băng có đủ tên và thời gian.

Nếu một mutation tự động chỉ có ID mà không có metadata ổn định, Backend xóa snapshot cũ thay vì giữ
lại tên sai. Booking legacy chưa có snapshot trả point bằng `null`; History không gọi Trip hiện tại để
backfill.

## Contract Booking History

Endpoint:

```http
GET /v1/bookings/history
```

Mỗi item trả thêm `pickupPoint` và `dropoffPoint` ở root:

```json
{
  "bookingId": "booking-uuid",
  "originName": "A",
  "destinationName": "D",
  "routeName": "A - D",
  "pickupPoint": {
    "type": "STOP",
    "id": "stop-c-uuid",
    "displayName": "C",
    "address": null,
    "plannedAt": "2026-09-10T09:15:00+07:00"
  },
  "dropoffPoint": {
    "type": "STATION",
    "id": "station-d-uuid",
    "displayName": "D",
    "address": null,
    "plannedAt": "2026-09-10T12:45:00+07:00"
  },
  "paymentRedirectUrl": null
}
```

`originName`, `destinationName` và `routeName` tiếp tục là metadata của toàn Route.
`pickupPoint` và `dropoffPoint` mới là chặng hành khách đã đặt.

## Contract Passenger History

Endpoint facade:

```http
GET /v1/passenger/history?type=TICKET
```

Hai point được truyền nguyên nghĩa dưới `ticket`:

```json
{
  "type": "TICKET",
  "originName": "A",
  "destinationName": "D",
  "ticket": {
    "routeName": "A - D",
    "pickupPoint": {
      "type": "STOP",
      "id": "stop-c-uuid",
      "displayName": "C",
      "address": null,
      "plannedAt": "2026-09-10T09:15:00+07:00"
    },
    "dropoffPoint": {
      "type": "STATION",
      "id": "station-d-uuid",
      "displayName": "D",
      "address": null,
      "plannedAt": "2026-09-10T12:45:00+07:00"
    }
  }
}
```

Passenger History không gọi Trip để điền point bị thiếu. `trackingTarget` hiện tại vẫn là contract
tracking riêng và không thay thế `pickupPoint`/`dropoffPoint`.

## Mobile cần thay đổi

1. Hiển thị chặng vé bằng `ticket.pickupPoint.displayName → ticket.dropoffPoint.displayName`.
2. Chỉ dùng `originName → destinationName` làm thông tin Route phụ.
3. Dùng `type` và `id` khi cần định danh point; không đoán loại point từ field khác.
4. Chấp nhận `pickupPoint` hoặc `dropoffPoint` bằng `null` đối với dữ liệu legacy.
5. Không map raw Stop ID sang Trip đang hoạt động và không tự khôi phục tên từ Route hiện tại.

## Lưu ý về case `C → B`

Trên Route có thứ tự `A → B → C → D`, `C → B` là chặng đi ngược và bị validation Booking hiện tại
từ chối. Case Stop-to-Stop hợp lệ để xác minh là `B → C`. Nếu sản phẩm thực sự cần cho phép `C → B`,
đó là thay đổi business rule và route-direction riêng, không thuộc HIST-BE-002.

## Verification Backend

- Booking unit tests: `639/639` passed.
- Booking create/edit/history targeted tests: `54/54` passed.
- Booking point/history PostgreSQL integration tests: `4/4` passed.
- Passenger History tests: `16/16` passed.
- Migration apply trên DB rỗng, Down, reapply và pending-model check: passed.
- Booking Release build: `0 warning`, `0 error`.
- Scoped formatter, line-ending check và `git diff --check`: passed.

## Acceptance để Mobile đóng ticket

- Route metadata vẫn hiển thị `A → D` ở khu vực thông tin Route.
- Vé Stop-to-destination hiển thị chặng đã đặt `C → D`.
- Vé Stop-to-Stop hợp lệ hiển thị chặng đã đặt `B → C`.
- Mobile không gọi Trip để resolve tên point.
- Booking legacy có point `null` không bị gán nhầm thành Route origin/destination.
