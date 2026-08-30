# Driver/Assistant Mobile — Parcel Reliability v2 FE Agent Playbook

> Tài liệu thực thi dành cho dev/AI agent phụ trách ứng dụng Driver/Assistant. Phải tách quyền DRIVER và ASSISTANT dù dùng chung app. Không được biến mọi endpoint có chữ “scan” thành nút quét trên card.

## 1. Quy tắc không được vi phạm

- Base URL production: `https://api.vietride.online`.
- JWT quyết định role và người thao tác; không truyền `assistantUserId`, `reviewerUserId`, `supervisorApprovalUserId` từ FE.
- Mọi mutation có `Idempotency-Key: <uuid>`.
- Dùng `GET /v1/crew/trips/{tripId}/parcels` làm manifest chính cho cả DRIVER và ASSISTANT. Endpoint `/v1/assistant/trips/{tripId}/parcels` chỉ dành riêng Assistant và không thay thế role-aware crew manifest.
- Chỉ render action có trong từng item `availableActions`.
- `currentOperationalLocation == null` trước chuyến là bình thường; vẫn check-in/load được nếu các điều kiện khác hợp lệ.
- `check-in`, `load`, `unload`, `deliver`, `confirm-found-on-vehicle` tự ghi custody. Không gọi thêm `custody-scan` sau các action này.
- Không đặt nút “Quét ghi nhận vị trí” cố định trên mọi card.
- GPS/fake GPS không phải custody proof và không tự tạo incident.

## 2. Phân vai trên cùng ứng dụng

### ASSISTANT

Được thực hiện QR lookup, check-in, reweigh, load, unload, deliver, custody exception report, custody scan khi BE cho phép, confirm found on vehicle và stop/destination reconciliation theo assignment. Station handoff và unidentified-package APIs hiện dành cho `OPERATOR_ADMIN,OPERATOR_STAFF`, không dành cho JWT Assistant.

### DRIVER

Được xem crew manifest, xem incident và duyệt/reject custody exception hoặc departure override của Trip mình được phân công. Driver không dùng API check-in/load/unload của Assistant.

Nếu app login DRIVER mà vẫn render `CHECK_IN`, `LOAD`, `UNLOAD`, `CUSTODY_SCAN`, đó là lỗi FE.

## 3. Contract FE cũ phải sửa

| Thay đổi | Migration bắt buộc |
|---|---|
| Manifest mới có `tripContext`, `summary`, `items`, `pagination` | Đọc `pagination` lồng trong `data`, không đọc meta phân trang cũ. |
| `currentOperationalLocation` nullable | Không chặn check-in/load khi null trước chuyến. |
| Driver manifest trả inline `custodyExceptionApproval` và actions approve/reject | Render inbox duyệt ngay trong card/detail, không yêu cầu UUID supervisor. |
| Stop reconcile body chỉ còn `departureOverrideReason` optional | Xóa `scannedParcelIds`, `manualExceptionParcelIds`; JSON unmapped bị BE từ chối. |
| Destination reconcile bodyless | Không gửi danh sách ID do FE tự tổng hợp. |
| `searchDeadline` nullable | Chờ duyệt không có countdown. |
| `CONFIRM_FOUND_ON_VEHICLE` áp dụng cả incident `UNSCANNED_HANDOFF` | Render theo action, không giới hạn cứng ở `MISSING`. |
| `CUSTODY_SCAN` chỉ được trả trong operational context hợp lệ | Xóa nút cố định; render secondary CTA chỉ khi action tồn tại. |
| Mutation trả `parcelState`, `currentCustody`, `activeIncident`, `createdCustodyEvent`, `availableActions`, `warning` | Patch card từ response, không refetch manifest sau mọi thao tác. |

## 4. Manifest screen model

```http
GET /v1/crew/trips/{tripId}/parcels?stopId=&status=&hasException=&search=&page=1&pageSize=20
```

Roles: `DRIVER,ASSISTANT`. `page >= 1`, `pageSize` từ `1` đến `100`.

Response chính:

```text
tripContext.trip
tripContext.currentOperationalLocation?
tripContext.orderedStops[]
summary { total, checkedIn, loaded, expectedAtCurrentStop, unloaded, exceptionCount, unresolvedCount }
items[]
pagination { page, pageSize, totalItems, totalPages, hasNextPage, hasPreviousPage }
```

Mỗi item có:

```text
parcelId, parcelCode, status
recipientName, recipientPhone
dropoffStopId, dropoffLocation
size/weight/balance fields
currentCustody, activeIncident, paymentState, identityCheckHints
availableActions[]
transferContext, sourceTripId, targetTripId
custodyExceptionApproval?
```

`identityCheckHints` dùng đối chiếu kiện thật với QR; không dùng để tự động kết luận sai tem. Nếu ảnh/mô tả/cân không khớp, Assistant report exception.

## 5. Happy case từ nhận kiện tới giao

### 5.1. Quét QR để lookup

```http
POST /v1/assistant/trips/{tripId}/parcels/qr-scan
```

```json
{ "parcelCode": "VR-PCL-..." }
```

Đây là lookup/validation, không phải custody event và không tự check-in/load/unload. Endpoint này không yêu cầu `Idempotency-Key` vì không mutate state.

### 5.2. Check-in tại bến xuất phát

Chỉ render khi item có `CHECK_IN`:

```http
POST /v1/assistant/parcels/{parcelId}/check-in
Idempotency-Key: <uuid>
```

```json
{
  "tripId": "uuid",
  "parcelCode": "VR-PCL-...",
  "photoUrls": ["https://owned-firebase-image-url"]
}
```

Không gọi `custody-scan` sau check-in. Check-in đã ghi custody tương ứng.

### 5.3. Cân/kích thước thực tế

Chỉ render khi item có `REWEIGH`:

```json
{
  "actualLengthCm": 40,
  "actualWidthCm": 30,
  "actualHeightCm": 20,
  "actualWeightKg": 5.2
}
```

Gọi `POST /v1/assistant/parcels/{parcelId}/reweigh`. Tất cả số phải lớn hơn `0`. Nếu phát sinh tiền còn thiếu, chờ Passenger thanh toán; không bypass bằng load.

### 5.4. Load trước khi Driver start Trip

Chỉ render khi item có `LOAD`:

```json
{
  "tripId": "uuid",
  "parcelCode": "VR-PCL-..."
}
```

Gọi `POST /v1/assistant/parcels/{parcelId}/load`. Trip `SCHEDULED/BOARDING` và `currentOperationalLocation == null` không phải lý do chặn FE. BE kiểm tra assignment, Trip, payment và cargo.

Không hiện `LOAD` cho Driver. Không gọi custody scan để “tạo ORIGIN_STATION trước” nếu check-in/load đã hợp lệ.

### 5.5. Driver start/arrive/depart

Lifecycle Trip dùng API Trip của app hiện tại. Parcel phản ứng qua event/backend; FE không cập nhật hàng loạt Parcel bằng GPS hoặc tự gọi custody scan cho mọi kiện khi xe đến stop.

- Arrive stop xác lập `currentOperationalLocation` ở Trip.
- Parcel không tự tạo `MISSING` chỉ vì arrive/destination arrive.
- Trước depart, Assistant thực hiện unload đúng kiện rồi reconcile stop.

### 5.6. Unload bắt buộc quét đúng QR

Chỉ render khi item có `UNLOAD` và xe đang ở stop/destination hợp lệ:

```http
POST /v1/assistant/parcels/{parcelId}/unload
Idempotency-Key: <uuid>
```

```json
{
  "parcelCode": "VR-PCL-...",
  "actualLocation": {
    "kind": "ROUTE_STOP",
    "id": "current-stop-uuid"
  },
  "photoUrls": ["https://owned-firebase-image-url"]
}
```

Tại bến cuối dùng location kind/id đúng contract BE hiện hành. Không lấy stop người dùng chọn tùy ý; lấy từ `tripContext.currentOperationalLocation`/destination context.

Nếu QR sai, stop sai hoặc stop đã departed: giữ card nguyên trạng, hiển thị `error.fields` và không gọi exception tự động. Chỉ người dùng xác nhận kiện thực tế đã bị thao tác sai mới mở form report.

### 5.7. Deliver và recipient confirm

Sau unload, chỉ render `DELIVER` khi action có:

```http
POST /v1/assistant/parcels/{parcelId}/deliver
Idempotency-Key: <uuid>
```

```json
{
  "photoUrls": ["https://owned-firebase-image-url"]
}
```

Crew không tự xác nhận thay recipient trừ luồng manual confirm được BE cho phép và có ghi chú bắt buộc. Recipient hoàn tất bằng delivery token.

## 6. Custody là gì và khi nào dùng `custody-scan`

Custody là chuỗi bằng chứng “ai đang giữ kiện, ở đâu, lúc nào”. Nó khác business status:

- `check-in/load/unload/deliver` vừa làm nghiệp vụ vừa tự ghi custody.
- `custody-scan` chỉ thêm một mốc bàn giao/vị trí vật lý bổ sung; không thay business status.

Chỉ render `CUSTODY_SCAN` khi BE trả action này. Đặt trong menu phụ “Ghi nhận bàn giao/vị trí”, không đặt cạnh nút nghiệp vụ chính như Load/Unload.

```json
{
  "parcelCode": "VR-PCL-...",
  "eventType": "HANDOFF",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "uuid",
  "locationSnapshot": "Quầy hàng hóa stop A",
  "evidenceReferences": [],
  "reason": "Bàn giao cho nhân viên bến"
}
```

Ma trận BE hiện kiểm soát:

| Event | Vị trí/điều kiện |
|---|---|
| `ACCEPTED` | `ORIGIN_STATION`, đúng bến xuất phát, trước load. |
| `ARRIVED_AT_STOP` | `ROUTE_STOP`, trùng operational location đang `ARRIVED`. |
| `HANDOFF` | Stop/bến hiện tại của Trip. |
| `RETURNED_TO_STATION` | Station hợp lệ trong return flow. |

`HANDOFF` cũng chấp nhận `VEHICLE` khi `actualLocationId` đúng vehicle của Trip. FE phải lấy vehicle ID từ `tripContext.trip.vehicle`; không cho nhập/chọn vehicle tùy ý.

Không dùng custody scan cho các việc sau: nhận QR lookup, check-in, load, cập nhật xe đã tới mỗi stop, unload, deliver, báo mất. Các việc này đã có endpoint riêng hoặc do Trip/backend xử lý.

## 7. Custody exception hai bước

### 7.1. Assistant báo cáo

Chỉ gọi khi người vận hành phát hiện hoặc có căn cứ về bất thường vật lý: đặt nhầm bến, sai tem/kiện, không xác định được kiện, hoặc custody gap. Không gọi tự động chỉ vì unload trả 409.

```http
POST /v1/assistant/parcels/{parcelId}/custody-exception
Idempotency-Key: <uuid>
```

```json
{
  "incidentType": "WRONG_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "uuid-or-null",
  "locationSnapshot": "Bến Bình Dương",
  "temporaryExceptionTag": null,
  "description": "Nghi đã để kiện tại khu vực trả hàng",
  "observedWeightKg": null,
  "evidenceUrls": [],
  "reason": "Đối soát thiếu kiện trên xe"
}
```

Assistant không truyền UUID người duyệt. Response HTTP `202` có request/incident status chờ duyệt, `searchDeadline = null`; chưa được hiển thị là đang chạy SLA 72 giờ.

Ảnh không bắt buộc trong tình huống chưa tìm thấy kiện. Có thể bổ sung evidence khi crew/station tìm thấy.

### 7.2. Driver duyệt bằng JWT của chính mình

Driver đọc manifest. Khi item có `APPROVE_CUSTODY_EXCEPTION`/`REJECT_CUSTODY_EXCEPTION`, dùng `custodyExceptionApproval.requestId`/incident context để mở chi tiết:

```http
GET /v1/crew/parcels/{parcelId}/custody-exception
```

Quyết định:

```http
POST /v1/crew/parcels/{parcelId}/custody-exception-decision
Idempotency-Key: <uuid>
```

```json
{
  "decision": "APPROVE",
  "note": "Đã đối soát khoang xe và xác nhận cần tìm kiếm"
}
```

`decision` chỉ `APPROVE` hoặc `REJECT`. Reviewer lấy từ JWT Driver; Driver phải được phân công đúng Trip. Approve mới ghi `MANUAL_CUSTODY_EXCEPTION`, bắt đầu search và set deadline. Reject đóng incident và trả Parcel về trạng thái trước report.

## 8. Tìm thấy kiện trên xe

Khi item có `CONFIRM_FOUND_ON_VEHICLE`, Assistant phải quét đúng QR kiện thật:

```http
POST /v1/assistant/parcels/{parcelId}/confirm-found-on-vehicle
Idempotency-Key: <uuid>
```

```json
{
  "incidentId": "uuid",
  "parcelCode": "VR-PCL-...",
  "evidenceReferences": [],
  "note": "Tìm thấy trong khoang cuối xe"
}
```

Áp dụng cho các incident phù hợp như `MISSING`, `MISSING_AFTER_DEPARTURE`, `UNSCANNED_HANDOFF`. BE resolve incident và khôi phục Parcel về `LOADED/IN_TRANSIT`; FE patch card từ response. Không gọi `mark-found` operator cho trường hợp đã xác nhận trực tiếp trên xe.

## 9. Stop reconciliation và rời điểm

Sau khi xử lý các kiện dự kiến tại stop:

```http
POST /v1/assistant/trips/{tripId}/stops/{stopId}/reconcile
Idempotency-Key: <uuid>
```

Happy body:

```json
{}
```

BE tự tính từ manifest/custody:

```text
expectedCount, scannedCount, manualExceptionCount
unresolvedParcels[]
canDepart, requiresSupervisorApproval, departureOverrideRequest?
```

Tuyệt đối không gửi `scannedParcelIds` hay `manualExceptionParcelIds`. Client cũ gửi field này sẽ bị từ chối vì request disallow unknown fields.

Nếu `canDepart = true`, app cho Driver dùng lifecycle depart của Trip. Nếu unresolved:

- Hiển thị từng `unresolvedParcels` với code/ảnh/expected dropoff/last custody/incident/recommended action.
- Assistant có thể nhập `departureOverrideReason` để tạo yêu cầu duyệt, nhưng không tự approve.
- Driver duyệt qua `/v1/crew/parcel-stop-departure-approvals/{requestId}/decision` bằng JWT.
- Nút “Rời điểm này” không cập nhật vị trí từng Parcel; nó là lifecycle Trip và bị clearance chặn nếu chưa reconcile/approve.

## 10. Destination reconciliation và complete Trip

Arrive destination chỉ xác nhận xe tới bến và mở unload. Sau khi unload các kiện bến cuối:

```http
POST /v1/assistant/trips/{tripId}/destination/reconcile
Idempotency-Key: <uuid>
```

Không gửi body danh sách Parcel. Response:

```text
expectedCount, scannedCount, manualExceptionCount
unresolvedParcels[]
canComplete, requiresDriverCompletion
```

Driver chỉ complete Trip khi clearance cho phép. Nếu unresolved, BE tạo `UNSCANNED_HANDOFF/SEARCHING`, không kết luận `MISSING` và không kết luận mất hàng. `trip.completed` không được FE dùng để tự mở incident.

## 11. Wrong stop và forwarding

- Nếu unload tại stop sai bị BE từ chối và kiện vẫn trên xe: giữ kiện, tiếp tục đúng stop; không tạo forwarding leg.
- Nếu thực tế kiện đã nằm tại bến sai: Assistant report custody exception; Driver/operator duyệt; operator search/mark found và chọn forwarding Trip.
- Khi operator tạo forward, crew của target Trip thấy transfer context và gọi:

```http
POST /v1/crew/parcels/{parcelId}/confirm-transfer
Idempotency-Key: <uuid>
```

```json
{ "parcelCode": "VR-PCL-..." }
```

Không sửa trip/leg cũ trên client. Sau confirm transfer, dùng target Trip manifest và luồng load/in-transit/unload bình thường theo action BE trả.

## 12. Kiện chưa định danh — không phải màn Assistant

Các API dưới đây hiện chỉ nhận JWT `OPERATOR_ADMIN,OPERATOR_STAFF`. App Assistant không render form/nút gọi chúng; nếu sản phẩm có màn station riêng thì dùng đúng operator role:

```http
POST /v1/stations/parcels/unidentified
Idempotency-Key: <uuid>
```

```json
{
  "temporaryExceptionTag": "TMP-001",
  "tripId": "uuid-or-null",
  "locationType": "WAREHOUSE",
  "locationId": "uuid",
  "locationSnapshot": "Kho lost-and-found",
  "description": "Hộp carton nâu mất tem",
  "observedWeightKg": 5.2,
  "evidenceReferences": ["https://..."]
}
```

Mobile không tự match candidate. Operator chọn candidate và supervisor xác nhận qua endpoint match.

## 13. CTA mapping bắt buộc

| `availableActions` | Role | CTA/UI | API |
|---|---|---|---|
| `CHECK_IN` | Assistant | Nhận kiện | `/check-in` |
| `REWEIGH` | Assistant | Cân lại | `/reweigh` |
| `LOAD` | Assistant | Chất lên xe | `/load` |
| `UNLOAD` | Assistant | Quét dỡ kiện | `/unload` |
| `DELIVER` | Assistant | Bàn giao người nhận | `/deliver` |
| `CUSTODY_EXCEPTION` | Assistant | Báo bất thường | `/custody-exception` |
| `CUSTODY_SCAN` | Assistant | Menu phụ ghi nhận bàn giao | `/custody-scan` |
| `CONFIRM_FOUND_ON_VEHICLE` | Assistant | Xác nhận tìm thấy trên xe | `/confirm-found-on-vehicle` |
| `VIEW_INCIDENT` | Cả hai | Xem trạng thái, read-only | Từ manifest/detail phù hợp |
| `APPROVE_CUSTODY_EXCEPTION` | Driver | Duyệt báo cáo | crew decision |
| `REJECT_CUSTODY_EXCEPTION` | Driver | Từ chối báo cáo | crew decision |

Nếu action không có: không render nút, kể cả FE nghĩ status “có vẻ hợp lệ”.

## 14. Error handling

- `401`: refresh token theo auth flow, retry một lần.
- `403`: sai role/assignment/operator; không chuyển sang gửi ID người khác.
- `404`: resource bị che hoặc không tồn tại; refresh manifest.
- `409 PARCEL_CUSTODY_LOCATION_MISMATCH`: không mutate card; hiển thị expected/actual nếu có trong `error.fields`.
- `409 PARCEL_DESTINATION_RECONCILIATION_REQUIRED`: mở destination reconciliation, không bypass complete.
- `422`: body/enum/unknown field sai; sửa client contract.
- `503`: upstream tạm lỗi; giữ nguyên idempotency key khi retry mutation.

## 15. Checklist giao cho AI agent Driver/Assistant

- [ ] Tách UI theo JWT role, không theo màn hình dùng chung.
- [ ] Chuyển manifest chính sang `/v1/crew/trips/{tripId}/parcels`.
- [ ] `currentOperationalLocation == null` không chặn check-in/load.
- [ ] Không gọi custody scan sau check-in/load/unload/deliver.
- [ ] Không có nút custody scan cố định.
- [ ] Xóa mọi supervisor/reviewer UUID khỏi request body.
- [ ] Stop reconcile chỉ gửi `{}` hoặc `{ "departureOverrideReason": "..." }`.
- [ ] Destination reconcile không gửi body danh sách ID.
- [ ] Driver approval dùng JWT Driver và đúng crew endpoints.
- [ ] Mutation patch card từ response screen model.
- [ ] Không dùng GPS để cập nhật Parcel hoặc tự mở missing.
- [ ] Test pre-trip load, wrong stop, pending approval, reject, found-on-vehicle, unresolved reconciliation và destination complete guard.
