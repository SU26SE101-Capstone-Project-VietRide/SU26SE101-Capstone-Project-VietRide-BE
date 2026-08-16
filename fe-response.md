# FE handoff — Manual boarding và start chuyến sớm

> Cập nhật: 2026-08-17
>
> Phạm vi: Trip lifecycle qua API Gateway
>
> Đối tượng sử dụng: FE Driver và FE Operator Admin

## 1. Mục tiêu và flow bắt buộc

Tính năng này cho phép mở boarding thủ công trước giờ khởi hành để phục vụ vận hành và demo.
Không có API `force-start` và không được nhảy trực tiếp từ `SCHEDULED` sang `IN_PROGRESS`.

Flow đúng:

```text
SCHEDULED
   │
   │ POST .../boarding
   ▼
BOARDING
   │
   │ POST /v1/driver/trips/{tripId}/start
   ▼
IN_PROGRESS
```

- Driver hoặc Operator Admin có thể chuyển `SCHEDULED -> BOARDING`.
- Chỉ Driver được gán vào chuyến mới có thể chuyển `BOARDING -> IN_PROGRESS`.
- API `/start` hiện có được phép gọi ngay sau khi boarding thành công, không cần chờ đến giờ
  khởi hành dự kiến.
- Boarding và start là hai mutation độc lập, bắt buộc dùng hai `Idempotency-Key` khác nhau.

## 2. Danh sách endpoint

Tất cả request phải gọi qua API Gateway bằng base URL của môi trường FE, không gọi trực tiếp Trip
Service và không tự gửi `X-Internal-Auth`.

| Chức năng | Method và path | Role được phép | Body |
|---|---|---|---|
| Driver mở boarding | `POST /v1/driver/trips/{tripId}/boarding` | `DRIVER` được gán vào Trip | Không có |
| Nhà xe mở boarding | `POST /v1/operator/trips/{tripId}/boarding` | `OPERATOR_ADMIN` cùng tenant | Không có |
| Driver bắt đầu chuyến | `POST /v1/driver/trips/{tripId}/start` | `DRIVER` được gán vào Trip | Không có |

Không cấp quyền:

- `ASSISTANT` không được gọi hai API boarding và không được gọi `/start`.
- `OPERATOR_STAFF` không được gọi API boarding.
- Operator Admin có thể mở boarding nhưng không được start chuyến thay Driver.

`tripId` phải là UUID hợp lệ.

## 3. Header bắt buộc

```http
Authorization: Bearer <user-access-token>
Idempotency-Key: <uuid-v4>
```

Quy tắc quan trọng:

- Tạo UUID v4 bằng `crypto.randomUUID()`.
- Không dùng cùng một key cho boarding và start.
- Không gửi JSON body, kể cả `{}` hoặc `null`.
- Không cần gửi `Content-Type: application/json` cho các request bodyless này.
- Có thể gửi `X-Request-Id` nếu FE đã có cơ chế trace; nếu không, Gateway sẽ tạo.

Ví dụ hai key khác nhau:

```ts
const boardingIdempotencyKey = crypto.randomUUID();
const startIdempotencyKey = crypto.randomUUID();
```

## 4. API mở boarding

### 4.1 Driver mở boarding

```http
POST /v1/driver/trips/2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2/boarding
Authorization: Bearer <driver-token>
Idempotency-Key: 249b0b3d-a6a2-4fc3-b5ec-1dcb6b4c188a
```

Điều kiện:

- Token có role `DRIVER`.
- JWT `sub` phải đúng bằng `driverUserId` của Trip.
- Trip đang ở `SCHEDULED`, hoặc đã ở `BOARDING` để nhận kết quả no-op thành công.

### 4.2 Operator Admin mở boarding

```http
POST /v1/operator/trips/2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2/boarding
Authorization: Bearer <operator-admin-token>
Idempotency-Key: 87664383-441b-4bdc-8194-bd58d854e196
```

Điều kiện:

- Token có role `OPERATOR_ADMIN`.
- `operatorId` trong JWT phải cùng tenant với Trip.
- Trip đang ở `SCHEDULED`, hoặc đã ở `BOARDING` để nhận kết quả no-op thành công.

### 4.3 Cửa sổ boarding thủ công

Backend cho phép boarding khi:

```text
departureDateTime <= now + TRIP_MANUAL_BOARDING_EARLY_WINDOW_MINUTES
```

- Giá trị mặc định hiện tại là 180 phút.
- Đúng tại biên T-180 được phép.
- Trước cửa sổ này trả `409 TRIP_BOARDING_TOO_EARLY`.
- Backend là nguồn quyết định cuối cùng vì cửa sổ có thể thay đổi bằng cấu hình. FE có thể dùng
  180 phút làm UX hint hiện tại nhưng không nên coi phép tính phía client là authorization.

### 4.4 Response boarding thành công

HTTP `200 OK`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "BOARDING"
  },
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-08-17T14:30:00+07:00"
  }
}
```

Nếu Trip đã ở `BOARDING`, backend vẫn trả đúng `200` và payload trên. Đây là no-op an toàn;
backend không phát event hoặc ghi audit lần hai.

## 5. API start chuyến ngay sau boarding

```http
POST /v1/driver/trips/2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2/start
Authorization: Bearer <assigned-driver-token>
Idempotency-Key: 31daaf6d-d61c-4904-ac67-b9c2fd666923
```

Điều kiện:

- Chỉ `DRIVER` được gán vào Trip.
- Trip bắt buộc đang ở `BOARDING`.
- Không còn kiểm tra phải gần `departureDateTime`; có thể start ngay sau manual boarding dù giờ
  dự kiến còn xa.
- Driver và Vehicle không được còn bị một main Trip hoặc ShuttleTrip khác giữ ở trạng thái
  resource reservation `ACTIVE`.

HTTP `200 OK`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "2f0cc13f-2207-4b62-9e0f-82f67f5a5bc2",
    "status": "IN_PROGRESS",
    "actualDepartureTime": "2026-08-17T14:31:00+07:00"
  },
  "meta": {
    "traceId": "req-def456",
    "timestamp": "2026-08-17T14:31:00+07:00"
  }
}
```

`actualDepartureTime` là thời gian backend thực sự xử lý start, không phải thời gian dự kiến của
Trip và không nên tự tính ở FE.

## 6. Response envelope và TypeScript types

Không đọc response như DTO raw. Tất cả response public đều dùng `ApiResponse<T>`.

```ts
export interface ApiMeta {
  traceId: string;
  timestamp: string;
}

export interface ApiSuccess<T> {
  success: true;
  statusCode: number;
  message?: string;
  data: T;
  meta: ApiMeta;
}

export interface ApiErrorField {
  field: string;
  message: string;
}

export interface ApiFailure {
  success: false;
  statusCode: number;
  error: {
    code: string;
    message: string;
    fields?: ApiErrorField[];
  };
  meta: ApiMeta;
}

export interface StartBoardingData {
  tripId: string;
  status: 'BOARDING';
}

export interface StartTripData {
  tripId: string;
  status: 'IN_PROGRESS';
  actualDepartureTime: string;
}
```

FE nên điều hướng theo HTTP status và `error.code`, không parse `error.message` để quyết định
logic. `meta.traceId` nên được ghi vào log hoặc hiển thị trong màn hình hỗ trợ khi cần đối soát.

## 7. Ví dụ API client bằng `fetch`

Ví dụ dưới đây cố ý nhận `idempotencyKey` từ bên ngoài để caller có thể giữ nguyên key khi retry
sau timeout hoặc lỗi mạng.

```ts
type BoardingActorRole = 'DRIVER' | 'OPERATOR_ADMIN';

export class ApiRequestError extends Error {
  constructor(
    public readonly httpStatus: number,
    public readonly response: ApiFailure,
  ) {
    super(response.error.message);
    this.name = 'ApiRequestError';
  }
}

async function postBodyless<T>(
  apiBaseUrl: string,
  path: string,
  accessToken: string,
  idempotencyKey: string,
): Promise<ApiSuccess<T>> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': idempotencyKey,
    },
    // Không thêm body và không JSON.stringify({}).
  });

  const payload = (await response.json()) as ApiSuccess<T> | ApiFailure;

  if (!response.ok || !payload.success) {
    throw new ApiRequestError(response.status, payload as ApiFailure);
  }

  return payload;
}

export function startBoarding(params: {
  apiBaseUrl: string;
  tripId: string;
  actorRole: BoardingActorRole;
  accessToken: string;
  idempotencyKey: string;
}) {
  const prefix = params.actorRole === 'DRIVER' ? 'driver' : 'operator';

  return postBodyless<StartBoardingData>(
    params.apiBaseUrl,
    `/v1/${prefix}/trips/${params.tripId}/boarding`,
    params.accessToken,
    params.idempotencyKey,
  );
}

export function startTrip(params: {
  apiBaseUrl: string;
  tripId: string;
  accessToken: string;
  idempotencyKey: string;
}) {
  return postBodyless<StartTripData>(
    params.apiBaseUrl,
    `/v1/driver/trips/${params.tripId}/start`,
    params.accessToken,
    params.idempotencyKey,
  );
}
```

`ApiRequestError` có thể dùng error class chung sẵn có của FE. Không bắt buộc copy nguyên mẫu này
nếu dự án đã có Axios/fetch wrapper, nhưng wrapper phải giữ request body thật sự rỗng.

## 8. Idempotency và retry đúng cách

Backend cache nguyên HTTP status và response body của request hoàn tất dưới `500` trong 24 giờ.
Do đó FE cần phân biệt retry cùng thao tác với một thao tác mới.

| Tình huống | Key cần dùng | Hành vi FE |
|---|---|---|
| User double-click khi request đầu còn chạy | Cùng key | Disable nút; nếu nhận pending thì chờ rồi retry cùng key |
| Timeout, mất mạng, không biết server đã xử lý chưa | Cùng key | Retry chính endpoint, tripId và user với key cũ |
| `5xx` | Cùng key | Có thể retry theo backoff; response `5xx` không được cache |
| `409 IDEMPOTENCY_REQUEST_PENDING` | Cùng key | Chờ ngắn rồi retry; không tạo mutation mới |
| Boarding đã trả `200` và chuẩn bị gọi start | Key mới | Start là một mutation khác |
| `TRIP_BOARDING_TOO_EARLY`, sau đó đợi đến đúng cửa sổ | Key mới | Key cũ sẽ replay lại lỗi `409` đã cache |
| Resource conflict đã được xử lý xong | Key mới | Đây là một lần start mới sau khi external state đã đổi |
| User chủ động bấm thử lại sau một lỗi nghiệp vụ cuối cùng | Key mới | Không tái dùng response lỗi đã cache |

Không được:

- Dùng key boarding cho `/start` — backend trả `422 IDEMPOTENCY_KEY_MISMATCH`.
- Tạo key mới ngay lập tức sau timeout — có thể tạo hai request cạnh tranh không cần thiết.
- Tự động gọi `/start` lại bằng key mới khi chưa biết request trước đã thành công hay chưa.

Khuyến nghị UI:

- Sinh key ngay khi user bắt đầu một action, không sinh lại trong mỗi lần retry HTTP.
- Giữ key trong state hoặc `sessionStorage` cho đến khi có kết quả chắc chắn.
- Disable button trong lúc mutation đang chạy.
- Xóa key sau `200` hoặc sau lỗi nghiệp vụ cuối cùng. Khi điều kiện đã thay đổi và user thực hiện
  action mới, tạo key mới.

## 9. Danh sách mã lỗi và cách xử lý FE

Error envelope mẫu:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "TRIP_BOARDING_TOO_EARLY",
    "message": "Trip is outside the manual boarding window."
  },
  "meta": {
    "traceId": "req-error-123",
    "timestamp": "2026-08-17T12:00:00+07:00"
  }
}
```

### 9.1 Lỗi dùng chung và boarding

| HTTP | `error.code` | Khi xảy ra | Xử lý FE đề xuất |
|---:|---|---|---|
| 401 | `AUTH_TOKEN_INVALID` | Token thiếu, sai hoặc không còn hợp lệ | Chạy auth/refresh flow chung; nếu đăng nhập lại cùng user thì có thể retry cùng logical key |
| 403 | `FORBIDDEN` | Sai role; Driver không được gán vào Trip | Ẩn/disable action theo role, báo không có quyền và reload Trip |
| 404 | `TRIP_NOT_FOUND` | Trip không tồn tại; Operator Admin gọi Trip khác tenant cũng bị mask thành 404 | Báo không tìm thấy chuyến, quay lại danh sách hoặc reload |
| 409 | `TRIP_BOARDING_TOO_EARLY` | Trip còn nằm ngoài cửa sổ manual boarding | Hiển thị chưa đến giờ mở boarding; giữ trạng thái `SCHEDULED` |
| 409 | `TRIP_INVALID_TRANSITION` | Boarding từ trạng thái khác `SCHEDULED/BOARDING`, ví dụ `IN_PROGRESS`, `CANCELLED`, `COMPLETED` | Reload Trip; không ép state local |
| 409 | `IDEMPOTENCY_REQUEST_PENDING` | Request cùng key vẫn đang xử lý | Giữ loading, chờ và retry cùng key |
| 422 | `IDEMPOTENCY_KEY_REQUIRED` | Thiếu header `Idempotency-Key` | Lỗi tích hợp FE; bổ sung UUID v4 |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Tái dùng key cho endpoint/tripId/user/method khác | Lỗi quản lý key; dừng retry mù và kiểm tra code FE |
| 422 | `VALIDATION_ERROR` | `tripId` hoặc key sai format, hoặc request có body | Đọc `error.fields`, sửa request; không gửi `{}` |

Lưu ý phân quyền:

- Driver gọi Trip của Driver khác nhận `403 FORBIDDEN`.
- Operator Admin gọi Trip khác tenant nhận `404 TRIP_NOT_FOUND`, không phải `403`.
- `ASSISTANT` và `OPERATOR_STAFF` bị Gateway chặn bằng `403 FORBIDDEN`.

### 9.2 Lỗi riêng khi start

| HTTP | `error.code` | Khi xảy ra | Xử lý FE đề xuất |
|---:|---|---|---|
| 409 | `TRIP_INVALID_TRANSITION` | Gọi start khi Trip còn `SCHEDULED` hoặc đã rời `BOARDING` | Nếu `SCHEDULED`, gọi boarding trước bằng key riêng; với trạng thái khác thì reload |
| 409 | `TRIP_DRIVER_CONFLICT` | Driver/Assistant của Trip vẫn bị resource reservation khác giữ, thường là ShuttleTrip `ACTIVE` | Hiển thị phải hoàn tất/hủy đúng luồng shuttle hoặc xử lý assignment; sau khi xử lý xong thử lại bằng key mới |
| 409 | `TRIP_VEHICLE_CONFLICT` | Vehicle vẫn bị main Trip/ShuttleTrip khác giữ `ACTIVE` | Hiển thị xung đột xe; xử lý chuyến đang giữ xe rồi thử lại bằng key mới |

Resource conflict có thể kèm `error.fields` để FE hiển thị hoặc log chi tiết:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "TRIP_DRIVER_CONFLICT",
    "message": "DRIVER has an unavailable assignment window.",
    "fields": [
      { "field": "conflictReason", "message": "RESOURCE_ACTIVE" },
      { "field": "resourceRole", "message": "DRIVER" },
      { "field": "resourceId", "message": "<uuid>" },
      { "field": "conflictingSourceType", "message": "SHUTTLE_TRIP" },
      { "field": "conflictingSourceId", "message": "<uuid>" },
      { "field": "blockingUntil", "message": "2026-08-17T14:45:00.0000000+07:00" }
    ]
  },
  "meta": {
    "traceId": "req-conflict-123",
    "timestamp": "2026-08-17T14:30:00+07:00"
  }
}
```

Không phụ thuộc vào `error.message` tiếng Anh trong ví dụ. Map nội dung hiển thị từ `error.code`
và dùng `fields` để bổ sung ngữ cảnh.

## 10. Mapping UI được khuyến nghị

### Màn hình Driver

| Trip status | Nút boarding | Nút start |
|---|---|---|
| `SCHEDULED` | Hiện cho assigned Driver; backend quyết định cửa sổ | Ẩn hoặc disable |
| `BOARDING` | Có thể ẩn hoặc hiển thị disabled/đã mở | Enable ngay |
| `IN_PROGRESS` | Ẩn | Ẩn hoặc hiển thị trạng thái đang chạy |
| `CANCELLED`, `COMPLETED` | Ẩn | Ẩn |

Sau boarding `200`, cập nhật local state thành `BOARDING` và refetch Trip nếu màn hình cần thêm
dữ liệu mới. Sau start `200`, lấy `status` và `actualDepartureTime` từ response để cập nhật UI.

### Màn hình Operator

- Chỉ hiện nút mở boarding cho `OPERATOR_ADMIN`.
- Không hiện cho `OPERATOR_STAFF`.
- Sau boarding thành công, hiển thị Trip ở `BOARDING`.
- Không tự gọi `/start` từ màn hình Operator; Driver được gán phải thực hiện bước này.

## 11. Hangfire và race — FE không cần xử lý đặc biệt

`AutoBoardingJob` vẫn tự mở boarding tại T-30. Manual boarding và Hangfire dùng cùng row lock và
recheck trạng thái, nên:

- Hangfire chạy trước: manual boarding trả `200 BOARDING` no-op.
- Manual boarding chạy trước: Hangfire thấy không còn `SCHEDULED` và no-op.
- Driver đã start: Hangfire không thể đổi ngược `IN_PROGRESS` về `BOARDING`.
- Chỉ có đúng một boarding event cho transition thật.

Vì vậy FE chỉ cần coi `200 BOARDING` là thành công, bất kể transition do manual API hay Hangfire
đã thực hiện trước đó.

Nếu một tác nhân hủy Trip giữa boarding và start, `/start` trả `409 TRIP_INVALID_TRANSITION`. FE
phải reload Trip và không tự ép chuyến sang `IN_PROGRESS`.

## 12. Kịch bản demo chuẩn

1. Hoàn thành đặt vé, parcel và shuttle khi main Trip còn `SCHEDULED`.
2. Hoàn tất shuttle để giải phóng Driver/Vehicle khỏi resource reservation `ACTIVE`.
3. Driver hoặc Operator Admin gọi API boarding với key A.
4. FE nhận `200`, cập nhật Trip thành `BOARDING`.
5. Thực hiện quét vé, check-in và xếp parcel nếu kịch bản cần.
6. Assigned Driver gọi API `/start` với key B, trong đó `B != A`.
7. FE nhận `200`, cập nhật `IN_PROGRESS` và `actualDepartureTime` từ response.

## 13. Checklist trước khi bàn giao FE

- [ ] Request đi qua Gateway và dùng đúng base URL theo môi trường.
- [ ] Driver và Operator Admin dùng đúng boarding path tương ứng.
- [ ] Không hiển thị action cho `ASSISTANT` hoặc `OPERATOR_STAFF`.
- [ ] Mỗi request có Bearer token và UUID-v4 `Idempotency-Key`.
- [ ] Request hoàn toàn bodyless; không gửi `{}`.
- [ ] Boarding và start dùng hai key khác nhau.
- [ ] Retry do timeout/pending dùng lại key của cùng logical action.
- [ ] FE đọc DTO trong `response.data`, không đọc ở root response.
- [ ] FE branch theo `error.code`, không branch theo `error.message`.
- [ ] Sau `TRIP_INVALID_TRANSITION`, FE reload trạng thái Trip.
- [ ] Sau resource conflict, hoàn tất/xử lý shuttle hoặc assignment trước khi start lại bằng key mới.
- [ ] Operator Admin chỉ boarding; assigned Driver mới thực hiện start.
