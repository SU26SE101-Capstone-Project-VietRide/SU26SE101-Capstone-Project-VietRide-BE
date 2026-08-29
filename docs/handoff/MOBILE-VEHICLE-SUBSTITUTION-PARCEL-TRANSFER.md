# Handoff Mobile — đổi xe do sự cố và chuyển hàng

## Phạm vi Mobile

Tài liệu này áp dụng cho Crew Mobile và Passenger Mobile. Operator Web thực hiện lệnh đổi xe; Mobile nhận kết quả, hướng dẫn crew mới tới điểm sự cố, bảo vệ custody hàng hóa và chuyển tracking sang chuyến mới đúng thời điểm.

## Crew mới nhận thông báo

Event nguồn là `trip.trip.vehicle_substituted`. Notification dành cho tài xế/phụ xe mới cung cấp các trường chính:

```json
{
  "incidentId": "uuid-su-co",
  "incidentLatitude": 10.7626,
  "incidentLongitude": 106.6602,
  "incidentDescription": "Km 20, Quốc lộ 1A",
  "newTripId": "uuid-chuyen-moi",
  "newVehicleId": "uuid-xe-moi",
  "newVehiclePlateNumber": "51B-22222",
  "newDriverId": "uuid-tai-xe-moi",
  "newAssistantId": "uuid-phu-xe-moi"
}
```

Mobile hiển thị xe/chuyến được gán và nút “Mở bản đồ”. Khi có đủ tọa độ, mở ứng dụng bản đồ ngoài bằng `incidentLatitude`/`incidentLongitude`. Nếu tọa độ không có, hiển thị `incidentDescription`; không hiển thị chuỗi `null, null`.

Không tạo `dispatchId`, tuyến cứu hộ, polyline hoặc realtime tracking riêng cho đoạn crew đi tới sự cố.

## Hàng hóa: không đổi Trip ngay

Vé và hàng có lifecycle khác nhau. Hàng vẫn giữ custody hai bước:

```text
LOADED hoặc IN_TRANSIT
tripId = TRIP-OLD
        |
        | event đổi xe
        v
PENDING_TRANSFER_CONFIRM
tripId = TRIP-OLD
transferTargetTripId = TRIP-NEW
        |
        | crew mới nhận hàng vật lý và xác nhận
        v
LOADED
tripId = TRIP-NEW
transferTargetTripId = null
```

Mobile không được hiển thị kiện hàng là “đã lên xe mới” khi status còn `PENDING_TRANSFER_CONFIRM`.

## Manifest của crew mới

```http
GET /v1/crew/trips/{newTripId}/parcels?page=1&pageSize=20
Authorization: Bearer <crew-token>
```

Endpoint dùng chung cho role `DRIVER|ASSISTANT`, trả cả:

- hàng có `tripId = newTripId`;
- hàng incoming có `transferTargetTripId = newTripId` và `status = PENDING_TRANSFER_CONFIRM`.

Ví dụ item incoming:

```json
{
  "parcelId": "uuid-kien-hang",
  "parcelCode": "VR-PCL-001",
  "status": "PENDING_TRANSFER_CONFIRM",
  "transferContext": "TRANSFER_IN",
  "sourceTripId": "TRIP-OLD",
  "targetTripId": "TRIP-NEW",
  "availableActions": ["CONFIRM_TRANSFER"]
}
```

Crew Mobile nên tạo nhóm “Hàng chờ nhận từ xe cũ”. Chỉ item incoming của Trip hiện tại mới hiển thị hành động `CONFIRM_TRANSFER`; crew chuyến cũ không được thấy nút xác nhận của crew đích.

Route Assistant cũ vẫn còn để tương thích:

```http
GET /v1/assistant/trips/{tripId}/parcels
```

## Xác nhận nhận hàng

Sau khi kiểm tra và nhận kiện hàng thực tế:

```http
POST /v1/crew/parcels/{parcelId}/confirm-transfer
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Chỉ tài xế/phụ xe được gán vào Trip đích có quyền xác nhận. Thành công trả hàng về `LOADED`, đổi `tripId` sang Trip mới và xóa `transferTargetTripId`.

Deadline xác nhận là 30 phút. Quá hạn, hàng chuyển `TRANSFER_ESCALATED`; Mobile hiển thị đã quá hạn và hướng dẫn liên hệ Operator, không tự coi là chuyển thành công.

## Passenger Mobile

- Vé chuyển sang Trip/xe mới sau khi Booking xử lý event đổi xe.
- Hàng `PENDING_TRANSFER_CONFIRM` hiển thị “Đang chờ xác nhận chuyển sang xe mới”.
- Chỉ hiển thị hàng thuộc Trip mới khi backend trả `status = LOADED` và `tripId = newTripId`.
- Không yêu cầu hành khách check-in lại chỉ vì đổi xe; giữ trạng thái boarding/transfer do Booking trả về.

## Tracking

- Không hiển thị tuyến từ nhà xe tới điểm sự cố.
- Không subscribe tracking cứu hộ riêng.
- Chỉ chuyển subscription/tracking context sang `newTripId` khi Trip mới đã start và ở `IN_PROGRESS`.
- Trước khi Trip mới start, hiển thị trạng thái đang chờ xe thay thế thay vì giả lập vị trí xe trên tuyến chính.

## Checklist nghiệm thu Mobile

- Crew mới nhận đúng xe, Trip và vị trí sự cố.
- Nút mở bản đồ dùng tọa độ thật hoặc fallback mô tả.
- Manifest Trip mới hiển thị hàng `TRANSFER_IN`.
- Chỉ crew Trip mới thấy và gọi `CONFIRM_TRANSFER`.
- Hàng chưa confirm không được hiển thị là đã nằm trên xe mới.
- Passenger phân biệt rõ vé đã đổi và hàng đang chờ chuyển.
- Tracking chỉ chuyển sang Trip mới sau khi Trip mới start.
