# Handoff Web Operator — đổi xe do sự cố

## Phạm vi Web

Web Operator chịu trách nhiệm chọn đầy đủ tài nguyên thay thế, gửi yêu cầu đổi xe và hiển thị kết quả điều phối. Web không cần tạo điều phối cứu hộ, tuyến cứu hộ, polyline hoặc màn hình tracking riêng cho đoạn xe mới đi từ nhà xe tới điểm sự cố.

## Quy tắc bắt buộc

- Chọn đủ **xe mới, tài xế mới và phụ xe mới**.
- Không cho gửi `null` đối với `replacementCrew`, `driverId` hoặc `assistantId`.
- Không cho chọn lại xe, tài xế hoặc phụ xe của chuyến cũ.
- `incidentId` bắt buộc và phải là sự cố thuộc đúng chuyến/operator.
- Chỉ hiển thị xe có `status = ACTIVE` trong bộ chọn xe thay thế.
- Cảnh báo trước khi xác nhận: xe cũ sẽ chuyển từ `ACTIVE` sang `MAINTENANCE`.

## API đổi xe

Quyền gọi API: chỉ `OPERATOR_ADMIN` được phép thực hiện substitution. `OPERATOR_STAFF` chỉ được xem và điều phối dữ liệu xe/sự cố; nếu gọi API này phải nhận `403 FORBIDDEN`.

```http
POST /v1/operator/trips/{tripId}/substitute-vehicle
Idempotency-Key: <uuid-v4>
Content-Type: application/json
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
  "notifyPassengers": true,
  "acknowledgeInsufficientSeats": false
}
```

Mỗi lần thay đổi body sau một response phải dùng `Idempotency-Key` UUID-v4 mới. Không tái sử dụng key cũ cho request có nội dung khác.

Ví dụ response thành công:

```json
{
  "substitutionId": "uuid-substitution",
  "oldTripId": "uuid-trip-cu",
  "oldTripStatus": "DISRUPTED",
  "newTripId": "uuid-trip-moi",
  "newTripStatus": "BOARDING",
  "newTripDepartureDateTime": "2026-08-29T04:30:00Z",
  "transferStatus": "QUEUED",
  "affectedBookingCount": 12,
  "affectedPassengerCount": 15,
  "pendingSeatAssignmentCount": 0
}
```

Web dùng `newTripId` để refresh chuyến thay thế. `transferStatus = QUEUED` chỉ cho biết luồng chuyển hàng đang chờ crew xác nhận; không được coi các kiện hàng là đã sang xe mới trước khi có confirm thành công.

## Validation và lỗi cần hiển thị

| Trường hợp | HTTP/mã lỗi | Cách Web xử lý |
|---|---|---|
| Thiếu/null crew, incident hoặc UUID rỗng | `422 VALIDATION_ERROR` | Giữ form và đánh dấu trường bắt buộc |
| Xe thay thế không `ACTIVE` | `422 VEHICLE_NOT_ACTIVE` | Refresh danh sách xe và yêu cầu chọn lại |
| Crew không active, sai role hoặc khác operator | `422 VALIDATION_ERROR` | Hiển thị lỗi tại tài xế/phụ xe |
| Incident không thuộc chuyến | `422 VALIDATION_ERROR` | Yêu cầu chọn đúng sự cố của chuyến |
| Chọn lại xe cũ | `409 TRIP_VEHICLE_SAME_AS_OLD` | Không cho xác nhận |
| Chọn lại bất kỳ crew cũ nào | `409 TRIP_CREW_SAME_AS_OLD` | Không cho xác nhận |
| Xe hoặc crew trùng lịch | `409 TRIP_VEHICLE_CONFLICT` hoặc `409 TRIP_CREW_CONFLICT` | Yêu cầu chọn tài nguyên khác |
| Chuyến không còn được phép đổi xe | `409 TRIP_NOT_SUBSTITUTABLE` | Đóng form và refresh trạng thái chuyến |
| Xe mới thiếu ghế | `409 REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS` | Hiển thị số ghế thiếu; chỉ retry với xác nhận rõ ràng và key mới |

## Before/after để Web hiển thị

Ví dụ trước khi đổi:

```text
Chuyến: TRIP-OLD — IN_PROGRESS
Xe: 51B-11111 — ACTIVE
Tài xế: Nguyễn Văn A
Phụ xe: Trần Văn A
```

Sau khi API thành công:

```text
TRIP-OLD  → DISRUPTED
51B-11111 → MAINTENANCE

TRIP-NEW  → BOARDING
Xe mới    → 51B-22222
Tài xế    → Nguyễn Văn B
Phụ xe    → Trần Văn B
Tuyến     → giữ tuyến chính và các stop PENDING còn lại
```

Web nên hiển thị banner thành công có `newTripId`, xe mới, crew mới và thời gian dự kiến tiếp tục hành trình. Không hiển thị rằng hàng đã sang xe mới ngay tại thời điểm này.

## Audit và màn hình phương tiện

Audit `VEHICLE_SUBSTITUTION_TRIGGERED` có:

- người thao tác (`actorUserId`);
- `incidentId`, lý do và thời điểm;
- xe/tài xế/phụ xe cũ và mới;
- trạng thái xe cũ trước/sau;
- `replacementTripId`.

Sau đổi xe, màn hình phương tiện phải hiển thị xe cũ là `MAINTENANCE`. Các form tạo/kích hoạt lịch và phân phối chuyến không được tiếp tục dùng xe này cho tới khi trạng thái được đưa về `ACTIVE` qua nghiệp vụ phương tiện phù hợp.

## Checklist nghiệm thu Web

- Không thể submit khi thiếu xe/tài xế/phụ xe/incident.
- Không gửi field crew với giá trị `null`.
- Có cảnh báo xe cũ chuyển `MAINTENANCE`.
- Mapping đầy đủ các lỗi `422` và `409` nêu trên.
- Sau thành công hiển thị Trip mới `BOARDING`, xe và crew mới.
- Không vẽ tuyến cứu hộ và không coi hàng là đã sang xe mới.
