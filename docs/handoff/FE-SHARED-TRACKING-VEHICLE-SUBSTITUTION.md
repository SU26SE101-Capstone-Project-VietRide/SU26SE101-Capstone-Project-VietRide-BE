# Handoff FE — giữ Share Tracking khi chuyến đổi xe

## Mục tiêu

Trang public Share Tracking phải tiếp tục hoạt động bằng **đúng URL và token hiện tại** khi chuyến
đang chạy được thay xe. Người xem không cần mở link mới, đăng nhập lại hoặc reconnect socket thủ
công.

Backend đã hoàn thành phần chuyển grant và realtime trong commit `15387d4a`.

## Phạm vi FE cần sửa

FE chỉ cần sửa trang public Share Tracking:

- chấp nhận thêm context status `VEHICLE_REPLACEMENT_PENDING`;
- lắng nghe event `shared:trip:vehicleSubstituted`;
- giữ nguyên share token và kết nối socket hiện tại;
- hiển thị banner đang đổi xe;
- trong trạng thái pending, gắn nhãn vị trí đang hiển thị là **“Vị trí trước khi đổi xe”**;
- bỏ banner khi nhận GPS mới hoặc khi context trở lại `IN_PROGRESS`.

FE không cần sửa cách tạo link, không gọi API đổi xe, không xử lý check-in/chuyển ghế và không cần
biết `oldTripId`, `newTripId`, Vehicle ID hoặc biển số xe.

## Luồng hoàn chỉnh

1. Người xem mở share URL hiện tại và lấy token từ URL fragment như đang làm.
2. FE dùng token đó cho REST context và Socket.IO namespace `/shared`.
3. Khi Backend xử lý đổi xe, socket nhận `shared:trip:vehicleSubstituted` nhưng **không bị
   disconnect**.
4. FE giữ marker cuối đang có, hiển thị banner đổi xe và tạm ẩn ETA.
5. Backend tự chuyển socket sang room của replacement Trip.
6. Khi xe mới bắt đầu gửi GPS, FE nhận `shared:gps:update` trên chính socket cũ, cập nhật marker và
   bỏ banner.
7. Nếu trang reload trong lúc chờ xe mới, REST context trả `VEHICLE_REPLACEMENT_PENDING` và có thể
   trả vị trí cuối của xe cũ để FE khôi phục đúng UI.

## REST context

```http
GET /v1/tracking/shared-trip/context
X-Trip-Share-Token: v1.<grantId>.<signature>
```

`data.status` hiện có hai giá trị:

| Status | Ý nghĩa | FE xử lý |
|---|---|---|
| `IN_PROGRESS` | Chuyến hiện tại đang chạy và đã có GPS mới | Hiển thị tracking bình thường, không hiện banner đổi xe |
| `VEHICLE_REPLACEMENT_PENDING` | Đang chờ replacement Trip bắt đầu gửi GPS | Hiện banner, gắn nhãn marker cũ và không hiển thị ETA |

Ví dụ response trong lúc chờ xe mới:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "status": "VEHICLE_REPLACEMENT_PENDING",
    "expiresAt": "2026-09-01T08:00:00.000+07:00",
    "lastUpdatedAt": "2026-08-31T15:05:00.000+07:00",
    "vehicle": {
      "location": {
        "latitude": 10.7812,
        "longitude": 106.6981,
        "heading": 42,
        "speedKph": 0,
        "recordedAt": "2026-08-31T15:05:00.000+07:00"
      }
    },
    "route": {
      "originName": "Bến xe Miền Đông",
      "destinationName": "Bến xe Đà Lạt",
      "origin": { "latitude": 10.8142, "longitude": 106.7108 },
      "destination": { "latitude": 11.9404, "longitude": 108.4583 },
      "stops": [],
      "geometry": null
    },
    "eta": null
  },
  "meta": {
    "traceId": "req-123",
    "timestamp": "2026-08-31T15:06:00.000+07:00"
  }
}
```

Lưu ý:

- `vehicle.location` có thể là `null` nếu Backend không còn GPS cũ trong Redis.
- Khi status pending, `eta` luôn là `null`; FE không được giữ hoặc tự tính ETA cũ.
- `lastUpdatedAt` và `vehicle.location.recordedAt` là thời điểm của dữ liệu đang hiển thị, không
  phải thời điểm hoàn tất đổi xe.
- FE không dựng GPS, ETA hoặc đường thẳng giả khi field tương ứng là `null`.

## Socket.IO public

Giữ nguyên cách kết nối hiện tại:

```ts
io("wss://api.vietride.app/shared", {
  path: "/tracking/socket.io",
  auth: { shareToken }
});
```

Event mới:

```ts
socket.on("shared:trip:vehicleSubstituted", (payload) => {
  // payload chỉ có status và occurredAt
});
```

Payload chính xác:

```json
{
  "status": "VEHICLE_REPLACEMENT_PENDING",
  "occurredAt": "2026-08-31T15:06:00.000+07:00"
}
```

Payload không có và FE không được chờ các field sau:

- `oldTripId`, `newTripId`;
- Vehicle ID, biển số xe;
- driver, assistant, operator;
- Booking, Ticket, seat hoặc dữ liệu người dùng.

Backend tự chuyển socket từ room cũ sang room mới. FE không emit event chọn room, không reconnect
với Trip ID khác và không thay token.

## Gợi ý state handling

```ts
type SharedTripStatus = "IN_PROGRESS" | "VEHICLE_REPLACEMENT_PENDING";

socket.on("shared:trip:vehicleSubstituted", ({ status, occurredAt }) => {
  if (status !== "VEHICLE_REPLACEMENT_PENDING") return;

  setTrackingStatus(status);
  setReplacementStartedAt(occurredAt);
  setEta(null);
  // Giữ marker hiện tại nhưng đổi nhãn thành “Vị trí trước khi đổi xe”.
});

socket.on("shared:gps:update", ({ location }) => {
  setVehicleLocation(location);
  setTrackingStatus("IN_PROGRESS");
  setReplacementStartedAt(null);
  // GPS này đến từ replacement room vì Backend đã chuyển socket.
});
```

Khi fetch/refetch REST context:

- lấy `data.status` làm trạng thái nguồn sự thật;
- nếu pending thì luôn `setEta(null)`;
- nếu `IN_PROGRESS` thì bỏ banner;
- không cache response vì Backend trả `Cache-Control: no-store`.

## UI đề xuất

Banner khi pending:

> Chuyến xe đang được đổi sang phương tiện thay thế. Vị trí bên dưới là vị trí cuối trước khi đổi
> xe. Tracking sẽ tự tiếp tục khi xe mới bắt đầu di chuyển.

Nhãn marker:

> Vị trí trước khi đổi xe

Không hiển thị thông báo yêu cầu người dùng mở link mới hoặc refresh trang.

## Link owner và revoke

Không thay đổi API tạo link. Nếu màn hình owner vẫn đang giữ `oldTripId`, request DELETE cũ tiếp tục
hoạt động vì Backend tự resolve sang replacement Trip:

```http
DELETE /v1/tracking/trips/{oldTripId}/share-link
Authorization: Bearer <identity-jwt>
Idempotency-Key: <uuid-v4>
```

Sau khi revoke, public viewer tiếp tục nhận `shared:access:revoked` với reason `REVOKED` như contract
hiện tại.

## Lỗi và reconnect

| Trường hợp | FE xử lý |
|---|---|
| Socket reconnect trong lúc đổi xe | Dùng lại đúng share token, sau đó fetch context để khôi phục status |
| Context trả `401 TRACKING_SHARE_TOKEN_INVALID` | Hiển thị link không hợp lệ |
| Context trả `410 TRACKING_SHARE_LINK_UNAVAILABLE` | Hiển thị link đã hết hạn/thu hồi/chuyến đã kết thúc |
| Context trả `429 RATE_LIMITED` | Backoff, không polling dồn dập |
| Context trả `503` | Hiển thị trạng thái tạm thời không khả dụng và retry có backoff |

Việc đổi xe không gia hạn `expiresAt`. FE vẫn dùng expiry ban đầu để xử lý trạng thái hết hạn.

## Checklist nghiệm thu FE

- Share URL và token không đổi sau vehicle substitution.
- Nhận event pending mà socket không disconnect.
- Event public không yêu cầu internal Trip/Vehicle ID hoặc PII.
- Banner và nhãn “Vị trí trước khi đổi xe” xuất hiện khi pending.
- ETA bị xóa khi pending.
- Reload giữa lúc đổi xe khôi phục đúng banner từ REST context.
- GPS mới cập nhật marker và tự bỏ banner.
- Viewer không còn nhận GPS room cũ sau khi đã chuyển room.
- DELETE bằng old Trip ID vẫn revoke link thành công.
- Luồng completed, cancelled, disrupted không substitution, owner revoke và expiry vẫn giữ UI lỗi
  hiện tại.
