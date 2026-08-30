# Phản hồi FE — sửa lỗi Parcel tự mở `MISSING` tại bến cuối

## 1. Kết luận

FE không tự tạo incident trong case đã báo cáo. Nguyên nhân nằm ở Backend Parcel:

- `POST /v1/driver/trips/{tripId}/destination/arrive` phát event `trip.destination.arrived`.
- Parcel consumer cũ lập tức xem mọi kiện terminal còn `LOADED|IN_TRANSIT` là `MISSING`.
- Parcel bị chuyển sang `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION` trước khi phụ xe có thời gian
  gọi unload.

Đây là lỗi nghiệp vụ của BE. Tới bến cuối chỉ là điều kiện mở khóa dỡ hàng; kiện còn trên xe ngay
sau thời điểm arrive là bình thường.

## 2. Backend đã sửa gì

### 2.1. Không mở incident ngay khi tới bến cuối

`trip.destination.arrived` bây giờ chỉ làm mốc cho phép unload tại
`DESTINATION_STATION`. Event này không đổi `ParcelStatus`, không tạo search task và không mở
`MISSING`.

Luồng đúng:

```text
IN_TRANSIT
  -> Driver xác nhận destination/arrive
  -> Parcel vẫn IN_TRANSIT
  -> Assistant quét QR và gọi unload
  -> UNLOADED
  -> deliver
  -> DELIVERED_PENDING_CONFIRM
```

Chỉ khi Trip đã `COMPLETED` mà kiện vẫn còn `LOADED|IN_TRANSIT`, BE mới mở incident hệ thống và
chuyển kiện sang `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION`.

### 2.2. Lưu đúng trạng thái để phục hồi sau incident

Khi `trip.trip.completed` phát hiện kiện chưa xử lý, BE lưu:

```text
pendingActionType = CUSTODY_EXCEPTION
pendingActionResumeStatus = LOADED hoặc IN_TRANSIT
pendingActionReason = TRIP_COMPLETED_WITH_PENDING_PARCEL
```

Nhờ đó khi tìm lại kiện, BE khôi phục đúng trạng thái trước khi xảy ra incident thay vì trả về một
trạng thái mặc định sai.

### 2.3. Thêm đường phục hồi trực tiếp cho Assistant

Endpoint mới:

```http
POST /v1/assistant/parcels/{parcelId}/confirm-found-on-vehicle
Authorization: Bearer <assistant-access-token>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Request:

```json
{
  "incidentId": "8b48c904-1edc-4d18-8353-9d88caa8f877",
  "parcelCode": "VR-PCL-20260829-3J9NY66A",
  "evidenceReferences": ["https://example.com/photo.jpg"],
  "note": "Tìm thấy kiện trong khoang hành lý của xe"
}
```

Validation thực tế:

| Field | Bắt buộc | Quy tắc |
|---|---:|---|
| `incidentId` | Có | UUID khác rỗng |
| `parcelCode` | Có | Mã lấy từ lần quét QR, tối đa 100 ký tự |
| `evidenceReferences` | Không | Mỗi phần tử không rỗng, tối đa 2048 ký tự |
| `note` | Không | Tối đa 2000 ký tự |

Endpoint chỉ chạy khi đồng thời thỏa các điều kiện:

- JWT là `ASSISTANT` đang được phân công cho Trip của kiện.
- `parcelCode` quét được đúng với Parcel trên URL.
- Incident thuộc chính Parcel/operator đó.
- Incident do hệ thống tạo (`reporterSource = SYSTEM`).
- Type là `MISSING` hoặc `MISSING_AFTER_DEPARTURE`.
- Incident đang ở `OPEN`, `SEARCHING`, `ESCALATED` hoặc `SEARCH_EXPIRED`.
- Parcel đang `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION`.
- Trạng thái phục hồi đã lưu là `LOADED` hoặc `IN_TRANSIT`.

Khi thành công, BE xử lý nguyên tử:

1. Ghi custody event `FOUND` tại `VEHICLE` hiện tại của Trip.
2. Hủy các search task còn `OPEN|IN_PROGRESS`.
3. Resolve incident với `resolutionCode = CREW_CONFIRMED_ON_VEHICLE`.
4. Khôi phục Parcel về `LOADED` hoặc `IN_TRANSIT`.
5. Trả lại common Assistant action model để Mobile cập nhật card ngay, không cần refetch.

Response thành công vẫn theo `ApiResponse<T>` (giá trị minh họa, shape đầy đủ):

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelState": {
      "parcelId": "uuid",
      "parcelCode": "VR-PCL-20260829-3J9NY66A",
      "status": "IN_TRANSIT",
      "dropoffLocation": {
        "type": "DESTINATION_STATION",
        "id": "uuid",
        "name": "Bến xe Đồng Nai",
        "orderIndex": null,
        "eta": null
      },
      "paymentState": {
        "depositRequiredVnd": 20000,
        "depositPaidVnd": 20000,
        "balanceRequiredVnd": 80000,
        "balancePaidVnd": 80000,
        "finalPaymentDeadline": null,
        "isFullyPaid": true
      },
      "identityCheckHints": {
        "photoUrl": "https://example.com/parcel.jpg",
        "description": "Documents",
        "expectedWeightKg": 20,
        "actualWeightKg": 20,
        "expectedLengthCm": 40,
        "expectedWidthCm": 30,
        "expectedHeightCm": 20,
        "actualLengthCm": 40,
        "actualWidthCm": 30,
        "actualHeightCm": 20
      }
    },
    "currentCustody": {
      "lastEventType": "FOUND",
      "lastConfirmedLocation": {
        "type": "VEHICLE",
        "id": "uuid",
        "name": "VEHICLE:uuid",
        "orderIndex": null,
        "eta": null
      },
      "lastConfirmedAt": "2026-08-30T00:20:00+07:00",
      "currentTripId": "uuid",
      "currentVehicleId": "uuid",
      "trackingConfidence": "CONFIRMED_SCAN",
      "hasTrackingGap": false
    },
    "activeIncident": null,
    "createdCustodyEvent": {
      "eventType": "FOUND",
      "actualLocationType": "VEHICLE",
      "actualLocationId": "uuid",
      "locationSnapshot": "VEHICLE:uuid",
      "occurredAt": "2026-08-30T00:20:00+07:00",
      "sequence": 4
    },
    "availableActions": ["CUSTODY_SCAN", "UNLOAD", "CUSTODY_EXCEPTION"],
    "warning": "Parcel was confirmed on the vehicle and restored to its transport flow."
  },
  "meta": {
    "traceId": "...",
    "timestamp": "2026-08-30T00:20:00+07:00"
  }
}
```

FE phải dùng `data.parcelState.status`, `data.currentCustody`, `data.activeIncident` và
`data.availableActions` trả về thay vì tự suy diễn state machine.

## 3. FE Driver/Assistant cần sửa

### 3.1. Màn manifest

Đọc `items[].availableActions` từ:

```http
GET /v1/assistant/trips/{tripId}/parcels
```

Khi mảng có:

```text
CONFIRM_FOUND_ON_VEHICLE
```

hãy hiển thị nút như **“Đã tìm thấy trên xe”**. Không tự hiển thị nút này chỉ dựa vào
`status=PENDING_OPERATOR_ACTION`; BE đã tính đủ quyền, nguồn incident, type và trạng thái phục hồi.

### 3.2. Khi người dùng bấm “Đã tìm thấy trên xe”

1. Yêu cầu quét QR vật lý của kiện.
2. Lấy `incidentId` từ `item.activeIncident.incidentId`.
3. Tạo một UUID-v4 mới cho `Idempotency-Key`.
4. Gọi `confirm-found-on-vehicle`.
5. Thay card hiện tại bằng `response.data`; không cần gọi lại manifest.
6. Chỉ retry network timeout bằng đúng `Idempotency-Key` cũ. Một thao tác mới phải dùng key mới.

Ví dụ Axios:

```js
const idempotencyKey = crypto.randomUUID();

const response = await api.post(
  `/v1/assistant/parcels/${parcelId}/confirm-found-on-vehicle`,
  {
    incidentId: activeIncident.incidentId,
    parcelCode: scannedParcelCode,
    evidenceReferences: photoUrl ? [photoUrl] : null,
    note: "Tìm thấy kiện trong khoang hành lý của xe"
  },
  {
    headers: {
      "Idempotency-Key": idempotencyKey
    }
  }
);

updateParcelCard(response.data.data);
```

### 3.3. Không dùng nhầm API

| Tình huống | API đúng |
|---|---|
| Tới bến cuối, kiện bình thường và đang trên xe | `POST .../unload` sau khi quét QR |
| System đã mở `MISSING`, crew tìm thấy kiện vẫn trên xe | `POST .../confirm-found-on-vehicle` |
| Crew phát hiện dỡ sai, mất dấu hoặc bất thường vật lý mới | `POST .../custody-exception` |
| Chỉ ghi thêm một mốc bàn giao/vị trí hợp lệ | `POST .../custody-scan` |

Không gọi `custody-exception` để đóng một system incident đã tìm thấy. API đó tạo báo cáo mới và
đi vào luồng Driver/Operator phê duyệt.

## 4. Luồng bến cuối sau bản sửa

```text
Driver: POST /v1/driver/trips/{tripId}/destination/arrive
  -> thành công
  -> Parcel vẫn IN_TRANSIT

Assistant: GET /v1/assistant/trips/{tripId}/parcels
  -> kiện bình thường có availableActions chứa UNLOAD

Assistant quét QR:
POST /v1/assistant/parcels/{parcelId}/unload
{
  "actualLocation": {
    "kind": "DESTINATION_STATION",
    "id": "destinationStationId"
  },
  "photoUrls": [],
  "parcelCode": "VR-PCL-..."
}
  -> UNLOADED

Assistant: POST /v1/assistant/parcels/{parcelId}/deliver
  -> DELIVERED_PENDING_CONFIRM
```

Không gọi complete Trip trước khi hoàn tất unload/reconciliation. Nếu Trip bị complete trong khi
kiện chưa unload, fallback system incident là hành vi có chủ ý.

## 5. Incident do hệ thống và incident do Assistant báo khác nhau

### System incident

- Nguồn: Trip completion hoặc departure/reconciliation detector.
- Có thể vào `SEARCHING` ngay và có `searchDeadline`.
- Không cần Driver duyệt việc mở search.
- Nếu kiện vẫn trên xe và API cho phép, Assistant dùng `CONFIRM_FOUND_ON_VEHICLE`.

### Assistant-reported custody exception

- Nguồn: `POST .../custody-exception` do Assistant chủ động báo.
- Ban đầu là approval pending; chưa bắt đầu search SLA.
- Driver hoặc Operator Staff/Admin phải duyệt bằng JWT của chính người duyệt.
- Không dùng `confirm-found-on-vehicle` để bypass bước phê duyệt này.

## 6. Hiển thị `searchDeadline`

BE trả `searchDeadline` dưới dạng ISO-8601 đầy đủ, và SLA mặc định là 72 giờ tính từ lúc search bắt
đầu. FE không được chỉ hiển thị `HH:mm`, vì deadline sau 72 giờ có thể cùng giờ/phút nhưng khác ngày.

Nên hiển thị một trong hai dạng:

```text
Hạn tìm: 00:08 02/09/2026
```

hoặc:

```text
Còn 2 ngày 23 giờ
```

Nếu app chỉ hiển thị `00:08`, người dùng có thể hiểu sai là chỉ còn vài phút dù ngày deadline nằm
sau đó ba ngày.

## 7. Error handling cho endpoint mới

| HTTP/error.code | Ý nghĩa FE |
|---|---|
| `401` | Access token thiếu/hết hạn; chạy refresh/login flow chung |
| `403 FORBIDDEN` | Assistant không được phân công, sai operator hoặc incident không thuộc Parcel |
| `404 PARCEL_NOT_FOUND` | Parcel không tồn tại/không còn khả dụng |
| `404 PARCEL_INCIDENT_NOT_FOUND` | Incident ID không tồn tại |
| `404 TRIP_NOT_FOUND` | Trip không tồn tại |
| `409 SCAN_IDENTITY_MISMATCH` | QR vừa quét không phải kiện trên card |
| `409 PARCEL_INCIDENT_INVALID_STATUS` | Incident không phải system missing active hoặc đã được xử lý |
| `409 INVALID_STATUS` | Parcel không còn ở trạng thái có thể phục hồi |
| `409 IDEMPOTENCY_KEY_REUSED` | Key đã dùng cho thao tác khác; không tự retry với payload mới |
| `422 VALIDATION_ERROR` | Body sai UUID, thiếu `parcelCode` hoặc vượt giới hạn chuỗi |
| `503 TRIP_SERVICE_UNAVAILABLE` | Chưa xác minh được xe; giữ màn hình và cho retry với cùng key |

## 8. Mapping 9 `ParcelIncidentType`

Danh sách FE đã map là đúng và không thay đổi:

```text
MISSING
WRONG_STOP
DELIVERY_NOT_RECEIVED
PARTIAL_LOSS
DAMAGED
SCAN_IDENTITY_MISMATCH
PACKAGE_IDENTITY_MISMATCH
UNSCANNED_HANDOFF
MISSING_AFTER_DEPARTURE
```

## 9. Checklist FE nghiệm thu

- Tới destination không tự hiện `MISSING`.
- Sau destination arrival, kiện `IN_TRANSIT` vẫn có action `UNLOAD`.
- Trip completed còn kiện chưa unload tạo system incident đúng thiết kế.
- System incident đủ điều kiện trả action `CONFIRM_FOUND_ON_VEHICLE`.
- QR sai trả `SCAN_IDENTITY_MISMATCH`, không đổi incident/Parcel.
- QR đúng phục hồi `IN_TRANSIT|LOADED`, `activeIncident` biến mất và custody mới là `FOUND` tại
  `VEHICLE`.
- Retry cùng request/key không tạo custody event thứ hai.
- Assistant-reported incident pending approval không hiển thị action phục hồi tắt duyệt.
- `searchDeadline` hiển thị cả ngày và giờ hoặc remaining duration.
