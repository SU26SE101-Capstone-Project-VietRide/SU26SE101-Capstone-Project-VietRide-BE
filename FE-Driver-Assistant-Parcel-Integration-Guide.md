# Hướng dẫn sửa FE Driver/Assistant — Parcel check-in, custody và load

## 1. Mục đích

Tài liệu này hướng dẫn Driver/Assistant Mobile tích hợp đúng luồng nhận kiện, cân đo, chất hàng, ghi nhận custody và xử lý vị trí vận hành của chuyến.

Phạm vi tập trung vào lỗi FE đang hiển thị:

> Chưa biết xe đang ở đâu. Chưa có vị trí vận hành của chuyến nên không ghi nhận được lần quét này.

Kết luận: thông báo trên là validation do FE tự đặt sai ngữ cảnh. `currentOperationalLocation` không bắt buộc cho check-in tại bến đầu, cân đo hoặc chất hàng. FE không được dùng field này để chặn các thao tác tại bến đầu.

## 2. Kết luận cần sửa ngay

1. Không dùng `currentOperationalLocation` để xác định bến đầu.
2. Bến đầu phải lấy từ:

   ```text
   data.tripContext.trip.route.origin
   ```

3. Vị trí custody gần nhất của kiện phải lấy từ:

   ```text
   item.currentCustody
   ```

   hoặc:

   ```text
   response.data.currentCustody
   ```

4. `currentOperationalLocation = null` khi chuyến còn `SCHEDULED`, xe chưa đến route stop, xe đã rời stop hoặc đang chạy giữa hai stop là hợp lệ.
5. Sau khi check-in thành công, backend đã tự tạo custody event `CHECKED_IN` tại `ORIGIN_STATION`. FE không cần bắt người dùng quét thêm để “ghi nhận vị trí”.
6. Sau `CHECKED_IN`, thao tác tiếp theo đúng là cân/đo thực tế bằng API `reweigh`, không phải custody scan.
7. Backend đã trả action `REWEIGH` khi parcel ở `CHECKED_IN`. FE nên ưu tiên `availableActions`; fallback theo status tại mục 9 chỉ dùng để tương thích với backend cũ trong thời gian rollout.

## 3. Phân biệt các loại dữ liệu vị trí

| Field | Ý nghĩa thực tế | Dùng cho |
|---|---|---|
| `tripContext.trip.route.origin` | Bến xuất phát của chuyến | Check-in, nhận kiện, hiển thị bến đầu |
| `tripContext.trip.route.destination` | Bến kết thúc của chuyến | Hiển thị bến cuối |
| `tripContext.currentOperationalLocation` | Route stop mà chuyến đang `ARRIVED` và chưa `DEPARTED` | Unload hoặc custody tại một route stop đang hoạt động |
| `currentCustody` | Vị trí đã được xác nhận gần nhất của kiện hàng | Tracking kiện, hiển thị lần quét cuối |
| GPS của xe | Vị trí hỗ trợ vận hành, không phải bằng chứng bàn giao kiện | Bản đồ; không dùng thay custody event |

### `currentOperationalLocation` không phải là gì?

Nó không phải:

- bến xuất phát;
- vị trí GPS liên tục của xe;
- vị trí hiện tại của kiện hàng;
- điều kiện bắt buộc để check-in, reweigh hoặc load.

Backend chỉ trả `currentOperationalLocation` khi có một route stop đang ở trạng thái `ARRIVED` và chưa có thời điểm rời stop. Vì vậy giá trị `null` trước khi chuyến bắt đầu là đúng.

## 4. API lấy dữ liệu cho màn hình Hàng hóa

### Driver/Assistant manifest

```http
GET /v1/assistant/trips/{tripId}/parcels?page=1&pageSize=100
Authorization: Bearer <accessToken>
```

Các nguồn dữ liệu FE cần giữ trong state của màn hình:

```text
data.tripContext.trip
data.tripContext.trip.route.origin
data.tripContext.trip.route.destination
data.tripContext.currentOperationalLocation
data.summary
data.items[]
```

Mỗi item đã có các dữ liệu dùng để dựng card:

```text
parcelId
parcelCode
status
dropoffLocation
currentCustody
activeIncident
paymentState
identityCheckHints
availableActions
```

Không gọi trace hoặc parcel detail riêng cho từng item trong manifest.

## 5. Luồng đúng từ nhận kiện đến chất hàng

```text
QR scan
  → CHECK_IN
  → REWEIGH
  → chờ thanh toán bổ sung nếu có
  → READY_TO_LOAD
  → LOAD
  → LOADED
```

### Theo trạng thái Parcel

| Parcel status | FE cần hiển thị | API chính |
|---|---|---|
| `RESERVED` | Quét nhận kiện | `check-in` |
| `CHECKED_IN` | Cân/đo thực tế | `reweigh` |
| `PENDING_FINAL_PAYMENT` | Chờ khách thanh toán phần chênh lệch | Không cho load |
| `READY_TO_LOAD` | Chất kiện lên xe | `load` |
| `LOADED` | Đã ở trên xe | Không gọi load lại |
| `IN_TRANSIT` | Theo dõi đến stop cần dỡ | `unload`/custody exception theo màn hình tương ứng |

### Theo trạng thái Trip

```text
SCHEDULED → BOARDING → IN_PROGRESS
```

- `SCHEDULED`: check-in tại bến đầu có thể thực hiện nếu chưa quá deadline.
- `BOARDING`: giai đoạn phù hợp để hoàn tất cân/đo, thanh toán chênh lệch và chất hàng.
- `IN_PROGRESS`: chuyến đã chạy; `currentOperationalLocation` chỉ có giá trị khi xe đang dừng tại một route stop được ghi nhận `ARRIVED`.

Không yêu cầu driver start chuyến mới cho phép assistant check-in. FE cũng không được tự buộc `trip.status === IN_PROGRESS` cho check-in, reweigh hoặc load.

> Lưu ý contract hiện tại: backend đang kiểm soát check-in/reweigh/load bằng trạng thái Parcel, phân công assistant và các deadline; handler load hiện chưa bắt buộc Trip phải ở `BOARDING`. FE không nên tự tạo một state machine khác với backend.

## 6. Request chính xác cho từng thao tác

Tất cả URL dưới đây dùng base URL của môi trường FE đang cấu hình. Các mutation phải gửi access token; các endpoint được đánh dấu idempotent phải có một UUID mới cho mỗi thao tác nghiệp vụ mới và tái sử dụng cùng UUID khi retry đúng thao tác đó.

### 6.1. Quét QR để đọc kiện thuộc chuyến

```http
POST /v1/assistant/trips/{tripId}/parcels/qr-scan
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "parcelCode": "VRP-..."
}
```

Endpoint này dùng để đọc/xác thực kiện theo chuyến, không thay thế check-in.

### 6.2. Check-in nhận kiện tại bến đầu

```http
POST /v1/assistant/parcels/{parcelId}/check-in
Authorization: Bearer <accessToken>
Idempotency-Key: <uuid>
Content-Type: application/json
```

```json
{
  "tripId": "89136f0c-2f83-479a-9009-e92bf7a6c755",
  "parcelCode": "VRP-...",
  "photoUrls": [
    "https://example.com/parcel-check-in.jpg"
  ]
}
```

Kết quả đúng:

- Parcel chuyển từ `RESERVED` sang `CHECKED_IN`.
- Backend tự tạo custody event `CHECKED_IN`.
- Custody có location type `ORIGIN_STATION` và snapshot tên bến đầu.
- FE cập nhật card trực tiếp từ response mutation, không bắt buộc refetch manifest.

FE không gọi `custody-scan` với event type `CHECKED_IN`. Event này do endpoint check-in tạo.

### 6.3. Cân/đo thực tế

```http
POST /v1/assistant/parcels/{parcelId}/reweigh
Authorization: Bearer <accessToken>
Idempotency-Key: <uuid>
Content-Type: application/json
```

```json
{
  "actualLengthCm": 40,
  "actualWidthCm": 30,
  "actualHeightCm": 20,
  "actualWeightKg": 5.5
}
```

Các giá trị trên phải lớn hơn `0`.

Sau reweigh:

- nếu không phát sinh tiền chênh lệch, Parcel có thể chuyển sang `READY_TO_LOAD`;
- nếu có tiền chênh lệch, Parcel chuyển `PENDING_FINAL_PAYMENT` và chỉ được load sau khi hoàn tất thanh toán để trở thành `READY_TO_LOAD`.

### 6.4. Chất kiện lên xe

```http
POST /v1/assistant/parcels/{parcelId}/load
Authorization: Bearer <accessToken>
Idempotency-Key: <uuid>
Content-Type: application/json
```

```json
{
  "tripId": "89136f0c-2f83-479a-9009-e92bf7a6c755",
  "parcelCode": "VRP-..."
}
```

Chỉ gọi khi Parcel là `READY_TO_LOAD`. Thành công sẽ chuyển Parcel sang `LOADED` và tạo custody event tương ứng.

### 6.5. Custody scan bổ sung

```http
POST /v1/assistant/parcels/{parcelId}/custody-scan
Authorization: Bearer <accessToken>
Idempotency-Key: <uuid>
Content-Type: application/json
```

Body contract:

```json
{
  "parcelCode": "VRP-...",
  "eventType": "ACCEPTED",
  "actualLocationType": "ORIGIN_STATION",
  "actualLocationId": "3ce01b86-713a-4c44-bc65-6e6f2ef4640a",
  "locationSnapshot": "Bến xe Miền Tây",
  "evidenceReferences": [],
  "reason": "Accepted at origin station"
}
```

Các `eventType` được endpoint này chấp nhận trực tiếp:

```text
ACCEPTED
ARRIVED_AT_STOP
HANDOFF
RETURNED_TO_STATION
```

Các `actualLocationType` hợp lệ:

```text
ORIGIN_STATION
DESTINATION_STATION
ROUTE_STOP
VEHICLE
WAREHOUSE
```

Quy tắc `actualLocationId`:

- bắt buộc với mọi loại location trừ `VEHICLE`;
- có thể để `null` với `VEHICLE`.

Custody scan là thao tác nghiệp vụ bổ sung. Nó không phải bước bắt buộc ngay sau check-in.

## 7. Cách chọn location đúng trên FE

```ts
type LocationInput = {
  actualLocationType: string;
  actualLocationId: string | null;
  locationSnapshot: string | null;
};

function getOriginLocation(manifest: any): LocationInput {
  const origin = manifest.tripContext.trip.route.origin;

  return {
    actualLocationType: 'ORIGIN_STATION',
    actualLocationId: origin.id,
    locationSnapshot: origin.name,
  };
}

function getCurrentRouteStopLocation(manifest: any): LocationInput | null {
  const current = manifest.tripContext.currentOperationalLocation;

  if (!current) return null;

  return {
    actualLocationType: 'ROUTE_STOP',
    actualLocationId: current.id,
    locationSnapshot: current.name,
  };
}
```

Quy tắc gọi:

| Tình huống | Location source |
|---|---|
| Nhận/check-in ở bến đầu | `trip.route.origin` |
| Scan custody `ACCEPTED` tại bến đầu | `trip.route.origin` |
| Dỡ tại route stop hiện tại | `currentOperationalLocation` |
| Handoff ở bến cuối | `trip.route.destination` |
| Hiển thị nơi kiện được xác nhận gần nhất | `currentCustody.lastConfirmedLocation` |

Nếu `currentOperationalLocation` là `null`:

- không chặn check-in;
- không chặn reweigh;
- không chặn load chỉ vì field này null;
- chỉ chặn thao tác thật sự cần một route stop hiện tại, ví dụ unload tại route stop.

## 8. Xử lý response mutation

Các mutation Parcel Reliability trả screen-ready state theo cấu trúc chung:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "parcelState": {},
    "currentCustody": {},
    "activeIncident": null,
    "createdCustodyEvent": {},
    "availableActions": [],
    "warning": null
  },
  "meta": {
    "traceId": "..."
  }
}
```

Sau mutation thành công, FE cần merge các field sau vào card hiện tại:

```ts
function applyParcelMutation(card: any, response: any) {
  const result = response.data;

  return {
    ...card,
    ...result.parcelState,
    currentCustody: result.currentCustody,
    activeIncident: result.activeIncident,
    availableActions: result.availableActions,
    warning: result.warning,
  };
}
```

Không refetch toàn bộ manifest sau mỗi lần scan nếu response đã chứa state mới.

## 9. Xử lý `availableActions` trong thời gian rollout

Backend mới đã thêm `REWEIGH` cho trạng thái `CHECKED_IN` và không còn quảng cáo `CHECK_IN` cho trạng thái legacy `PENDING`. FE cần dựng CTA từ `availableActions` kết hợp thứ tự ưu tiên nghiệp vụ, không lấy phần tử đầu tiên làm CTA chính.

Trong thời gian production có thể còn chạy phiên bản backend cũ, FE có thể giữ fallback tương thích sau:

```ts
function getAssistantActions(parcel: {
  status: string;
  availableActions?: string[];
}) {
  const actions = new Set(parcel.availableActions ?? []);

  if (parcel.status === 'CHECKED_IN') {
    actions.add('REWEIGH');
  }

  return [...actions];
}
```

Ưu tiên CTA theo status:

```ts
function getPrimaryParcelAction(status: string) {
  switch (status) {
    case 'RESERVED':
      return 'CHECK_IN';
    case 'CHECKED_IN':
      return 'REWEIGH';
    case 'PENDING_FINAL_PAYMENT':
      return 'WAIT_FOR_PAYMENT';
    case 'READY_TO_LOAD':
      return 'LOAD';
    case 'LOADED':
      return 'NONE';
    case 'IN_TRANSIT':
      return 'UNLOAD_WHEN_AT_EXPECTED_STOP';
    default:
      return 'NONE';
  }
}
```

Không biến `CUSTODY_SCAN` thành CTA chính cho mọi Parcel. Nó chỉ là thao tác bổ sung khi nghiệp vụ thực tế cần ghi nhận một custody event.

## 10. Xử lý `currentCustody` có location ID null

Backend mới resolve bến đầu từ Trip trước khi chuyển trạng thái và lưu cả station ID vào custody `CHECKED_IN`/`LOADED`. Dữ liệu được tạo bởi phiên bản cũ vẫn có thể có `lastConfirmedLocation.id = null`.

Ví dụ hợp lệ:

```json
{
  "lastEventType": "CHECKED_IN",
  "lastConfirmedLocation": {
    "type": "ORIGIN_STATION",
    "id": null,
    "name": "Bến xe Miền Tây"
  },
  "trackingConfidence": "CONFIRMED_SCAN",
  "hasTrackingGap": false
}
```

FE phải xử lý như sau:

- không hiển thị “không biết kiện ở đâu” chỉ vì `id` null;
- nếu `trackingConfidence === 'CONFIRMED_SCAN'`, hiển thị tên snapshot và thời gian xác nhận;
- khi cần gửi một custody scan mới ở origin, lấy ID từ `tripContext.trip.route.origin.id`, không copy ID null từ `currentCustody`.

Không bắt người dùng scan lại để sửa dữ liệu lịch sử. FE tiếp tục hỗ trợ bản ghi legacy bằng snapshot và `trackingConfidence`.

## 11. Mapping lỗi FE cần hiển thị

Response lỗi chuẩn:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "ERROR_CODE",
    "message": "...",
    "fields": {}
  },
  "meta": {
    "traceId": "..."
  }
}
```

| `error.code` | Cách xử lý FE |
|---|---|
| `FORBIDDEN` | Không được phân công cho chuyến/kiện này; đóng action và tải lại quyền hoặc manifest |
| `PARCEL_NOT_FOUND` | Không tìm thấy kiện trong phạm vi được phép; không tiết lộ dữ liệu tenant khác |
| `INVALID_STATUS` | State trên máy đã cũ hoặc gọi sai bước; cập nhật card/refetch manifest một lần |
| `PARCEL_CHECK_IN_CLOSED` | Đã quá hạn check-in; hướng dẫn liên hệ operator |
| `PARCEL_LOAD_CUTOFF_PASSED` | Đã quá hạn cân/load; hướng dẫn liên hệ operator |
| `SCAN_IDENTITY_MISMATCH` | QR không khớp kiện/chuyến; giữ kiện, yêu cầu xác minh danh tính kiện |
| `PARCEL_CUSTODY_LOCATION_REQUIRED` | Request custody thiếu location hoặc location ID bắt buộc |
| `PARCEL_CUSTODY_LOCATION_MISMATCH` | Đang thao tác ở sai stop; hiển thị expected/actual stop từ `error.fields` nếu backend trả |
| `TRIP_CARGO_CAPACITY_EXCEEDED` | Xe không đủ sức chứa; không retry liên tục, chuyển operator xử lý |
| `TRIP_SERVICE_UNAVAILABLE` | Lỗi upstream tạm thời; cho phép retry an toàn bằng cùng `Idempotency-Key` |
| `RACE_LOST` | Có thao tác đồng thời; refetch state một lần trước khi hiển thị hành động tiếp theo |

Không tự suy ra lỗi từ `currentOperationalLocation === null`. Chỉ hiển thị lỗi location khi backend thực sự trả error response tương ứng hoặc thao tác đang yêu cầu một route stop hiện tại.

## 12. Logic màn hình đề xuất

```ts
function buildParcelCard(parcel: any, tripContext: any) {
  const primaryAction = getPrimaryParcelAction(parcel.status);
  const currentStop = tripContext.currentOperationalLocation;

  return {
    ...parcel,
    primaryAction,
    origin: tripContext.trip.route.origin,
    currentOperationalLocation: currentStop,
    canCheckIn: parcel.status === 'RESERVED',
    canReweigh: parcel.status === 'CHECKED_IN',
    canLoad: parcel.status === 'READY_TO_LOAD',
    canUnloadAtRouteStop:
      parcel.status === 'IN_TRANSIT' && currentStop !== null,
  };
}
```

Nội dung UI cho case trong ảnh cần đổi từ:

```text
Quét ghi nhận vị trí
→ Chưa biết xe đang ở đâu
```

thành:

```text
Parcel CHECKED_IN
Vị trí xác nhận: Bến xe Miền Tây
CTA chính: Cân/đo thực tế
```

## 13. Các khoảng hở backend FE cần biết

### 13.1. Tương thích action list khi rollout

Backend mới đã sửa resolver. FE có thể giữ fallback tại mục 9 cho đến khi tất cả môi trường đã deploy cùng phiên bản.

### 13.2. Dữ liệu custody legacy có thể thiếu origin ID

Check-in/load mới lưu origin ID. Với dữ liệu cũ, FE dùng `route.origin.id` khi cần tạo custody scan origin và vẫn coi custody có `trackingConfidence = CONFIRMED_SCAN` là hợp lệ.

### 13.3. Action response không chứa toàn bộ trip context

FE cần giữ `tripContext` lấy từ manifest trong store/screen state. Không gọi API trip hoặc manifest theo từng parcel sau mỗi mutation.

### 13.4. Load chưa bị backend khóa theo Trip `BOARDING`

Contract thực tế hiện tại cho phép handler quyết định bằng Parcel status, assignment, deadline và cargo state. FE không được yêu cầu Trip phải `IN_PROGRESS`. Nếu đội sản phẩm muốn chỉ load trong `BOARDING`, backend cần được sửa trước để FE và backend có cùng rule.

## 14. Checklist nghiệm thu cho FE Driver/Assistant

- [ ] Mở manifest của trip `SCHEDULED` có `currentOperationalLocation = null` mà màn hình không báo lỗi.
- [ ] Parcel `RESERVED` cho phép quét và check-in tại `route.origin`.
- [ ] Check-in thành công cập nhật card sang `CHECKED_IN` mà không refetch toàn trang.
- [ ] Sau check-in, CTA chính là `REWEIGH`, không phải “Quét ghi nhận vị trí”.
- [ ] `currentCustody.lastConfirmedLocation.id = null` nhưng có snapshot/confidence vẫn hiển thị đúng bến.
- [ ] Reweigh có chênh lệch tiền chuyển UI sang chờ thanh toán.
- [ ] Chỉ hiện nút load khi Parcel là `READY_TO_LOAD`.
- [ ] Load thành công cập nhật card sang `LOADED` từ mutation response.
- [ ] Không yêu cầu driver start chuyến để assistant check-in/reweigh/load.
- [ ] Chỉ yêu cầu `currentOperationalLocation` cho thao tác ở route stop như unload.
- [ ] Custody scan origin dùng `route.origin.id`, không dùng `currentOperationalLocation`.
- [ ] Retry mutation dùng lại cùng `Idempotency-Key`; thao tác mới dùng UUID mới.
- [ ] `INVALID_STATUS` hoặc `RACE_LOST` chỉ refetch một lần, không tạo vòng lặp retry.
- [ ] Không hiển thị actor/evidence nội bộ hoặc dữ liệu parcel ngoài trip được phân công.

## 15. Case đã xác minh trước khi backend fix được deploy

Case kiểm tra có:

```text
parcelId: cb1f063d-e5e8-437f-9d63-10382775935b
tripId: 89136f0c-2f83-479a-9009-e92bf7a6c755
tripStatus: SCHEDULED
origin: Bến xe Miền Tây
currentOperationalLocation: null
parcelStatus: CHECKED_IN
currentCustody.lastEventType: CHECKED_IN
currentCustody.trackingConfidence: CONFIRMED_SCAN
currentCustody.hasTrackingGap: false
```

Kết quả lịch sử này chứng minh:

- check-in đã thành công tại bến đầu;
- `currentOperationalLocation = null` không có nghĩa là mất vị trí kiện;
- popup trong ảnh là do FE dùng sai field và chặn sai luồng;
- bước tiếp theo phải là reweigh, không phải bắt custody scan để tạo `ORIGIN_STATION` lần nữa.
