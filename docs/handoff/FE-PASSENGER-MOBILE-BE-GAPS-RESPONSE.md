# Passenger Mobile — BE gaps response

**Ngày handoff:** 2026-09-01
**Phạm vi:** `TRACKING-BE-001`, `NOTIF-BE-008`, `BOOKING-BE-002`, `PARCEL-BE-001`

## Kết luận

| Gap | Kết luận BE | FE cần làm |
|---|---|---|
| `TRACKING-BE-001` | Không thay đổi lifecycle/API. Create share link chỉ hợp lệ khi Trip hiện tại là `IN_PROGRESS`. | Refetch Booking History sau disruption để lấy replacement `tripId`, đọc Trip detail/status, chỉ hiện Create Share khi replacement là `IN_PROGRESS`. |
| `NOTIF-BE-008` | Đã sửa semantic action của `VEHICLE_SUBSTITUTED`: có `bookingId` thì mở Booking; chỉ có `tripId` thì mở Trip. | Điều hướng theo `action`; không tự suy luận từ `newTripId`. |
| `BOOKING-BE-002` | Đã đồng bộ `seatNumber` nullable xuyên Booking history và Passenger history. | Chấp nhận `null` và hiển thị “Đang chờ xếp ghế”; không dùng ghế audit của Ticket làm fallback. |
| `PARCEL-BE-001` | Availability hỗ trợ Station, Stop và location hierarchy; response có `dropoffPoints`. | Chọn điểm giao từ `dropoffPoints`; gửi `dropoffStopId` đúng semantic khi create. |

## 1. Tracking sau vehicle substitution

BE giữ nguyên quy tắc tạo tracking share link: Trip được truyền vào create phải đang ở đúng trạng thái `IN_PROGRESS`. Replacement Trip còn `SCHEDULED` hoặc `BOARDING` chưa đủ điều kiện.

Luồng FE đề nghị:

1. Khi màn hình đang giữ old Trip ở trạng thái `DISRUPTED`, ẩn nút **Create Share**.
2. Refetch Booking History và lấy `tripId` hiện hành của Booking.
3. Fetch Trip detail bằng replacement `tripId`.
4. Chỉ hiện/enable **Create Share** khi status là `IN_PROGRESS`.

Existing share URL không cần tạo lại: token hiện hữu tự resolve sang xe/Trip thay thế. Revoke bằng old share ID vẫn hoạt động.

## 2. Notification action

`VEHICLE_SUBSTITUTED` được resolve như sau:

```json
{
  "action": {
    "type": "OPEN_BOOKING_DETAIL",
    "params": { "bookingId": "..." }
  }
}
```

- Nếu có cả `bookingId` và `tripId`, `bookingId` thắng.
- Nếu notification Operator/Crew chỉ có `tripId`, action fallback là `OPEN_TRIP_DETAIL`.
- Dữ liệu thiếu hoặc UUID sai trả `NONE`.
- REST inbox `action` và FCM `actionType`/`actionParams` dùng cùng resolver nên có cùng semantic.

FE phải dùng semantic action BE trả về; không dùng `newTripId` để tự đoán đích điều hướng.

## 3. Operational seat nullable

`seatNumber` trong Booking History và Passenger History là `string | null`.

- Có ghế: hiển thị chuỗi ghế bình thường.
- Chưa xếp lại ghế sau substitution: BE trả `null`; FE hiển thị **“Đang chờ xếp ghế”**.
- Không fallback sang `Ticket.seatNumber`: trường đó là ghế audit lịch sử, không phải ghế vận hành hiện tại.

## 4. Parcel availability và create

`GET /v1/parcels/available-trips` yêu cầu `originStationId` và đúng một destination mode:

- `destinationStationId`; hoặc
- `dropoffStopId`; hoặc
- `destinationProvinceCode`, kèm optional `destinationLocationCode`.

Không gửi mode nào, gửi nhiều mode, hoặc gửi `destinationLocationCode` không kèm province sẽ nhận `422 VALIDATION_ERROR`.

Mỗi Trip trả `dropoffPoints` đã được lọc và sắp xếp:

```json
{
  "type": "STOP",
  "stationId": null,
  "stopId": "...",
  "name": "Điểm trả Quận 1",
  "orderIndex": 2,
  "estimatedArrivalTime": "2026-09-10T10:30:00+07:00"
}
```

Identity của điểm giao là XOR:

- `type = STATION`: có `stationId`, `stopId = null`;
- `type = STOP`: có `stopId`, `stationId = null`.

Khi create Parcel:

- người dùng chọn `STOP` → gửi `dropoffStopId` tương ứng;
- người dùng chọn destination `STATION` → gửi `dropoffStopId: null`;
- không map Stop thành Station và không fan-out gọi Trip detail để tự dựng Stop catalog.

Mọi `dropoffPoints` của cùng Trip dùng chung một quote/giá. Stop không làm thay đổi giá và quote token không bind Stop. BE sẽ revalidate Stop theo Trip snapshot khi create; Stop đã inactive/biến mất trả `DROP_OFF_STOP_NOT_FOUND` và không tạo Parcel.

### 4.1. Luồng FE tìm chuyến và chọn điểm trả hàng

Trong lần search đầu tiên, FE chưa biết `dropoffStopId`. Passenger chỉ cần chọn:

1. bến gửi (`originStationId`);
2. tỉnh/thành nhận (`destinationProvinceCode`);
3. optional địa điểm con (`destinationLocationCode`);
4. ngày gửi và thông tin kích thước/khối lượng kiện hàng.

FE dùng **location mode** để BE tìm cả:

- Trip có destination Station nằm trong location được chọn; và
- Trip có Stop hợp lệ nằm trong location được chọn, kể cả khi destination Station cuối tuyến nằm ở nơi khác.

Ví dụ tuyến thực tế:

```text
Hồ Chí Minh ──► Long An ──► Tiền Giang ──► Cần Thơ
                   ▲
             allowDropoff=true
```

Passenger chọn gửi từ Hồ Chí Minh đến Long An. FE gọi:

```http
GET /v1/parcels/available-trips
  ?originStationId=<hcm-station-id>
  &destinationProvinceCode=<long-an-province-code>
  &departureDate=2026-09-05
  &lengthCm=20
  &widthCm=15
  &heightCm=10
  &estimatedWeightKg=2
  &page=1
  &pageSize=20
```

Nếu passenger chọn một location con của Long An, FE gửi đồng thời:

```text
destinationProvinceCode=<long-an-province-code>
destinationLocationCode=<selected-leaf-location-code>
```

Không gửi `destinationStationId` hoặc `dropoffStopId` cùng location mode.

BE vẫn có thể trả chuyến Hồ Chí Minh → Cần Thơ vì chuyến đi qua Stop Long An:

```json
{
  "tripId": "<hcm-can-tho-trip-id>",
  "originStation": {
    "id": "<hcm-station-id>",
    "name": "Bến xe Hồ Chí Minh"
  },
  "destinationStation": {
    "id": "<can-tho-station-id>",
    "name": "Bến xe Cần Thơ"
  },
  "departureDateTime": "2026-09-05T08:00:00+07:00",
  "estimatedPriceVnd": 50000,
  "quoteToken": "<quote-token>",
  "dropoffPoints": [
    {
      "type": "STOP",
      "stationId": null,
      "stopId": "<long-an-stop-id>",
      "name": "Điểm dừng Long An",
      "orderIndex": 1,
      "estimatedArrivalTime": "2026-09-05T09:30:00+07:00"
    }
  ]
}
```

FE phải hiển thị rõ tuyến xe và điểm nhận hàng, ví dụ:

```text
Chuyến: Hồ Chí Minh → Cần Thơ
Khởi hành: 08:00
Nhận hàng tại: Điểm dừng Long An
Dự kiến đến điểm nhận: 09:30
Giá: 50.000đ
```

Không chỉ hiển thị “Hồ Chí Minh → Cần Thơ”, vì passenger có thể hiểu nhầm kiện hàng sẽ được gửi đến bến cuối Cần Thơ.

FE chỉ cho chọn các phần tử BE trả trong `dropoffPoints`; không tự dựng thêm Stop từ Trip detail hoặc map Stop thành Station. Nếu một Trip có nhiều điểm trả phù hợp, passenger phải chọn chính xác một `dropoffPoint` trước khi tiếp tục.

### 4.2. State FE cần giữ sau khi passenger chọn chuyến

FE giữ nguyên các giá trị từ kết quả availability:

```text
tripId
quoteToken
selectedDropoffPoint.type
selectedDropoffPoint.stationId
selectedDropoffPoint.stopId
```

Một `quoteToken` của Trip dùng chung cho mọi `dropoffPoints` trong Trip đó. FE không cần search lại quote khi passenger chỉ đổi giữa các điểm trả của cùng kết quả Trip, miễn quote chưa hết hạn và dữ liệu search khác không thay đổi.

### 4.3. Payload tạo Parcel

Nếu passenger chọn điểm loại `STOP`, FE gửi `dropoffStopId` của điểm đã chọn:

```http
POST /v1/parcels
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "tripId": "<hcm-can-tho-trip-id>",
  "dropoffStopId": "<long-an-stop-id>",
  "bookingId": null,
  "itemName": "Tài liệu",
  "description": "Hồ sơ cần giao",
  "sizeCategory": "SMALL",
  "lengthCm": 20,
  "widthCm": 15,
  "heightCm": 10,
  "estimatedWeightKg": 2,
  "photoUrl": null,
  "recipient": {
    "fullName": "Nguyễn Văn A",
    "phoneNumber": "+84901234567",
    "email": null
  },
  "deliveryMethod": "TERMINAL_PICKUP",
  "paymentMethod": "WALLET",
  "voucherCode": null,
  "quoteToken": "<quote-token-from-availability>",
  "declaredValueVnd": 100000,
  "quantity": 1
}
```

Nếu passenger chọn điểm loại `STATION`, FE vẫn dùng `tripId` và `quoteToken` của Trip nhưng gửi:

```json
{
  "dropoffStopId": null
}
```

`stationId` trong `dropoffPoints` dùng để nhận diện/hiển thị điểm được chọn; request create hiện không nhận `dropoffStationId`. Giá trị `dropoffStopId: null` có nghĩa là nhận hàng tại destination Station của Trip.

### 4.4. Xử lý dữ liệu thay đổi sau search

BE kiểm tra lại Trip và Stop khi tạo Parcel. Nếu Stop bị tắt, bị xóa hoặc không còn thuộc Trip sau thời điểm search, BE trả:

```text
422 DROP_OFF_STOP_NOT_FOUND
```

Khi nhận lỗi này, FE cần:

1. thông báo “Điểm nhận hàng không còn khả dụng”;
2. quay lại/refetch availability với điều kiện search hiện tại;
3. yêu cầu passenger chọn lại chuyến hoặc `dropoffPoint`;
4. không tự retry create với `dropoffStopId: null`, vì hành vi đó có thể đổi nơi nhận hàng sang bến cuối mà passenger không đồng ý.

Nếu quote hết hạn hoặc thông tin kiện hàng/ngày gửi thay đổi, FE phải gọi lại availability để nhận quote mới trước khi create.
