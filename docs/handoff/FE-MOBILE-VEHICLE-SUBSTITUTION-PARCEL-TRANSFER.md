# Handoff FE/Mobile — đổi xe do sự cố và chuyển hàng

## Quy tắc đã chốt

- Operator bắt buộc chọn **xe mới, tài xế mới và phụ xe mới**. Không gửi `null`, không dùng lại xe hoặc crew cũ.
- `incidentId` bắt buộc và phải thuộc đúng chuyến/operator.
- Sau khi thay xe, chuyến cũ là `DISRUPTED`, xe cũ tự chuyển `MAINTENANCE`. Chuyến mới giữ nguyên tuyến chính, ở trạng thái `BOARDING`.
- Xe mới do tài xế/phụ xe mới lái tới tọa độ sự cố. Không có `dispatchId`, tuyến cứu hộ, polyline hay tracking riêng cho đoạn đi tới sự cố; crew mở ứng dụng bản đồ ngoài bằng tọa độ nhận được.

## Request operator

```http
POST /v1/operator/trips/{tripId}/substitute-vehicle
Idempotency-Key: <uuid-v4>
```

```json
{
  "replacementVehicleId": "uuid-xe-moi",
  "replacementCrew": {
    "driverId": "uuid-tai-xe-moi",
    "assistantId": "uuid-phu-xe-moi"
  },
  "incidentId": "uuid-su-co",
  "estimatedRecoveryDepartureAt": "2026-08-29T04:30:00Z",
  "reason": "Xe hỏng tại điểm dừng",
  "notifyPassengers": true
}
```

Thiếu `replacementCrew`, thiếu `driverId`/`assistantId`, hoặc truyền `null` đều trả `422`. Backend cũng từ chối crew/xe cũ, crew không active, khác operator, trùng lịch và sự cố không thuộc chuyến.

Thông báo/event `trip.trip.vehicle_substituted` có thể cung cấp `incidentId`, `incidentLatitude`, `incidentLongitude`, `incidentDescription`, `newTripId`, `newVehicleId`, `newVehiclePlateNumber`, `newDriverId`, `newAssistantId`. Crew mới dùng các trường vị trí để đi tới điểm sự cố.

## Trạng thái vé và hàng

Vé được Booking chuyển sang chuyến mới ngay sau khi event được xử lý. Hàng vẫn bảo vệ custody qua hai bước:

```text
LOADED hoặc IN_TRANSIT, tripId = chuyến cũ
        |
        | event đổi xe
        v
PENDING_TRANSFER_CONFIRM,
tripId = chuyến cũ,
transferTargetTripId = chuyến mới
        |
        | crew xác nhận sau khi đã nhận hàng vật lý
        v
LOADED, tripId = chuyến mới, transferTargetTripId = null
```

Không được coi hàng là đã nằm trên xe mới trước bước xác nhận cuối.

## Manifest crew mới

`GET /v1/crew/trips/{tripId}/parcels` dùng chung cho tài xế/phụ xe mới và trả cả:

- hàng có `tripId` bằng chuyến đang xem;
- hàng có `transferTargetTripId` bằng chuyến đang xem và `status = PENDING_TRANSFER_CONFIRM`.

Mỗi hàng incoming có thêm:

```json
{
  "transferContext": "TRANSFER_IN",
  "sourceTripId": "uuid-chuyen-cu",
  "targetTripId": "uuid-chuyen-moi"
}
```

`availableActions` chứa `CONFIRM_TRANSFER` cho kiện hàng đang chờ nhận.

Crew hiển thị nhóm “Hàng chờ nhận từ xe cũ” và xác nhận từng kiện sau khi nhận thực tế:

Route cũ `GET /v1/assistant/trips/{tripId}/parcels` vẫn giữ cho Mobile Assistant hiện tại.

```http
POST /v1/crew/parcels/{parcelId}/confirm-transfer
Idempotency-Key: <uuid-v4>
```

Chỉ tài xế/phụ xe được gán vào chuyến thay thế có quyền xác nhận. Deadline là 30 phút; quá hạn chuyển `TRANSFER_ESCALATED` để operator xử lý.

## Hiển thị Passenger Mobile

- Vé: hiển thị chuyến/xe mới ngay khi Booking đồng bộ.
- Hàng: hiển thị “Đang chờ xác nhận chuyển sang xe mới” khi `PENDING_TRANSFER_CONFIRM`.
- Chỉ hiển thị hàng thuộc chuyến mới sau khi backend trả `status = LOADED` và `tripId = newTripId`.
- Tracking chỉ chuyển sang `newTripId` khi chuyến mới start; không vẽ tuyến cứu hộ.

## Audit và trạng thái xe

Audit `VEHICLE_SUBSTITUTION_TRIGGERED` ghi actor, incident, lý do, thời điểm, replacement Trip và before/after của xe, tài xế, phụ xe, cùng trạng thái xe cũ (`ACTIVE` → `MAINTENANCE`). Các màn hình phân phối/gán xe chỉ được chọn vehicle `ACTIVE`; xe đã chuyển `MAINTENANCE` không xuất hiện trong danh sách xe khả dụng.
