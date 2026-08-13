# [BE → FE] Search/Filter gap đã hoàn tất — hướng dẫn tích hợp chuẩn

> Ngày bàn giao: 2026-08-13
> Đối tượng: FE Admin và Manager
> Trạng thái BE: **ĐÃ IMPLEMENT VÀ E2E PASS**

## 1. Kết luận và nguyên tắc nối API

FE không nối nhầm service hay public endpoint. Các query trước đây chưa nằm trong API Contract và
một số controller .NET cũ đã bỏ qua chúng. BE đã bổ sung các khả năng P0–P2 đã thống nhất và bật
strict-query validation trên toàn bộ GET endpoint trong đợt này.

Tất cả public endpoint dưới đây tiếp tục gọi qua Gateway:

```text
<GATEWAY_BASE_URL>/v1/...
```

FE chỉ gửi user access token:

```http
Authorization: Bearer <RS256-user-access-token>
```

FE **không gọi trực tiếp** port/service Identity, Trip, Booking, Payment hoặc Parcel; không tự tạo
và không gửi `X-Internal-Auth`.

## 2. Breaking behavior cần sửa ngay ở FE

Các query lạ không còn bị bỏ qua. Response chuẩn:

```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more query parameters are not supported.",
    "fields": [
      {
        "field": "isOneTime",
        "message": "Query parameter 'isOneTime' is not supported."
      }
    ]
  },
  "meta": {
    "traceId": "request-trace-id",
    "timestamp": "2026-08-13T17:00:00+07:00"
  }
}
```

FE phải bỏ ngay:

| Endpoint | Query cũ phải bỏ | Thay bằng |
|---|---|---|
| `/v1/operator/driver-schedules` | `isOneTime` | Không có query thay thế; bỏ filter/card vì domain chỉ có lịch lặp tuần. |
| `/v1/operator/routes` | `status` | `isActive=true|false`. |
| `/v1/operator/vehicles` | `status=INACTIVE` | Dùng `isActive=false`; Vehicle status không có `INACTIVE`. |
| Các endpoint trong tài liệu này | `fetchAll`, `includeDeleted`, query tự đặt | Chỉ dùng allow-list của từng endpoint. |

`error.fields` là **array**, không phải object map. FE nên map bằng `field`:

```ts
const fieldErrors = Object.fromEntries(
  (error.fields ?? []).map((item) => [item.field, item.message]),
);
```

## 3. Quy ước chung cho list API

Các list public trả ADR 0004 envelope:

```ts
export type ApiResponse<T> = {
  success: true;
  statusCode: number;
  data: T;
  meta: { traceId: string; timestamp: string };
};

export type PagedResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
};
```

Quy tắc FE:

- Không gửi key có giá trị `undefined`, `null` hoặc chuỗi rỗng nếu user chưa chọn filter.
- Khi `search` hay filter thay đổi, reset `page=1`.
- Debounce search khoảng 300–500 ms và hủy request trước bằng `AbortController`.
- Dùng `data.totalItems` cho tổng bản ghi, không dùng `data.items.length`.
- Không tải mọi page rồi lọc lại ở client.
- Các filter cấp cao kết hợp bằng AND, trừ các field được mô tả là OR bên trong `search`.
- `pageSize` tối đa 100 ở các endpoint .NET trong đợt này.

Helper đề nghị:

```ts
function cleanQuery(input: Record<string, unknown>) {
  return Object.fromEntries(
    Object.entries(input).filter(([, value]) =>
      value !== undefined && value !== null && value !== ''
    ),
  );
}
```

## 4. Bảng tổng hợp API FE cần đổi

| Màn hình | Endpoint | Query mới/chốt | FE cần làm |
|---|---|---|---|
| Lịch chạy | `GET /v1/operator/driver-schedules` | `search`, `vehicleTypeId` | Bind server-side; bỏ `isOneTime`. |
| Phương tiện | `GET /v1/operator/vehicles` | `vehicleTypeId`, `status`, `isActive` | Phân biệt activation và operational status. |
| Tuyến | `GET /v1/operator/routes` | `isActive` | Đổi `status` thành boolean. |
| Khu vực | `GET /v1/admin/locations` | `type`, `parentCode` | Bỏ `fetchAllAdminLocations`. |
| Bến xe | `GET /v1/admin/stations` | `supportsShuttle`, sort/search mở rộng | Chuyển hoàn toàn sang server-side. |
| Thống kê bến | `GET /v1/admin/stations/summary` | không có query | Dùng cho 4 summary card. |
| Voucher hệ thống | `GET /v1/admin/vouchers` | `search`, `service` | Bind search/tab service. |
| Voucher nhà xe | `GET /v1/operator/vouchers` | `search`, `service` | Bind search/tab service. |
| Đặt vé | `GET /v1/operator/bookings` | `search` | Bỏ heuristic đoán phone/code cho ô search tổng quát. |
| Đối soát | `GET /v1/admin/trip-settlements` | `search` | Search server-side trước paging. |
| Giao dịch nền tảng | `GET /v1/admin/platform-wallet/transactions` | `search` | Search server-side trước paging. |
| Bảng giá hàng | `GET /v1/operator/parcel-route-fares` | `search` | Không fetch-all; FE không gọi internal route search. |
| Chính sách | `GET /v1/admin/policies` | đã có `search`, `category`, `active` | Chỉ bind query có sẵn; BE không đổi API. |

## 5. Chi tiết từng endpoint

### 5.1. Driver schedules

```http
GET /v1/operator/driver-schedules
```

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

Allow-list:

```text
page, pageSize, routeId, driverUserId, isActive, search, vehicleTypeId
```

`search` OR-match:

- tên Route;
- biển số Vehicle được gán;
- display name của Driver;
- display name của Assistant.

`vehicleTypeId` lọc loại của Vehicle được gán. Search/filter chạy trước count và paging.

Ví dụ:

```http
GET /v1/operator/driver-schedules?page=1&pageSize=20&search=Nguyen%20Van%20A&vehicleTypeId=<uuid>&isActive=true
```

FE cần:

- bỏ `isOneTime` khỏi query, URL state, filter state và card thống kê;
- render `route`, `vehicle`, `driver`, `assistant` đã có trong mỗi item, không gọi N+1 để enrich;
- nếu nhận `503 UPSTREAM_UNAVAILABLE`, hiển thị trạng thái tạm thời và cho retry; không đổi thành
  empty list vì kết quả chưa được xác định.

### 5.2. Vehicles

```http
GET /v1/operator/vehicles
```

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

Allow-list:

```text
page, pageSize, search, searchIn, sortBy, sortDir, vehicleTypeId, status, isActive
```

Giá trị `status` hợp lệ:

```text
ACTIVE | MAINTENANCE | OFF_DUTY | RETIRED
```

`isActive` là cờ activation riêng. Ví dụ:

```http
# Xe đang bảo trì, vẫn được bật trong danh mục
GET /v1/operator/vehicles?status=MAINTENANCE&isActive=true

# Xe đã tắt khỏi danh mục, bất kể operational status
GET /v1/operator/vehicles?isActive=false
```

Không gửi `status=INACTIVE`; query đó trả `422`.

### 5.3. Routes

```http
GET /v1/operator/routes?page=1&pageSize=20&search=Da%20Lat&isActive=true
```

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

Allow-list:

```text
page, pageSize, search, isActive
```

FE đổi dropdown:

```ts
const routeActivationQuery = {
  ALL: undefined,
  ACTIVE: true,
  INACTIVE: false,
} as const;
```

Không gửi `status`.

### 5.4. Admin locations

```http
GET /v1/admin/locations
```

Auth: `SYSTEM_ADMIN`.

Allow-list:

```text
page, pageSize, search, isActive, type, parentCode
```

`type`:

```text
PROVINCE | MUNICIPALITY | WARD | COMMUNE | SPECIAL_ZONE
```

`parentCode` trả con trực tiếp của một top-level location. Parent inactive vẫn tìm được con. Parent
không tồn tại hoặc không phải top-level trả `422 VALIDATION_ERROR` tại field `parentCode`.

Ví dụ:

```http
GET /v1/admin/locations?page=1&pageSize=50&type=WARD&parentCode=79&isActive=true&search=Vung%20Tau
```

Tất cả filter kết hợp AND trước count/paging. FE phải bỏ luồng 34 request
`fetchAllAdminLocations`; picker tỉnh/thành và bảng location đều dùng API phân trang này.

### 5.5. Admin stations

```http
GET /v1/admin/stations
```

Auth: `SYSTEM_ADMIN`.

Allow-list:

```text
page, pageSize, search, isActive, supportsShuttle, sortBy, sortDir
```

`search` không phân biệt hoa thường/dấu tiếng Việt trên:

```text
name | city | ward | addressStreet | slug
```

Sort:

```text
sortBy=name|createdAt|updatedAt
sortDir=asc|desc
default=name asc
```

Ví dụ màn danh sách mới sửa trước:

```http
GET /v1/admin/stations?page=1&pageSize=20&supportsShuttle=true&sortBy=updatedAt&sortDir=desc
```

Summary card:

```http
GET /v1/admin/stations/summary
```

Không gửi query cho `/summary`. Qua Gateway, dữ liệu nằm trong envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "total": 100,
    "active": 90,
    "inactive": 10,
    "supportsShuttle": 24
  },
  "meta": {}
}
```

Merge-target picker dùng lại list API:

```http
GET /v1/admin/stations?page=1&pageSize=20&search=<keyword>&isActive=true&sortBy=name&sortDir=asc
```

Không tải toàn bộ Station. Loại primary station hiện tại khỏi options ở FE sau khi nhận một page.

### 5.6. Admin vouchers

```http
GET /v1/admin/vouchers
```

Auth: `SYSTEM_ADMIN`.

Allow-list:

```text
fundingType, isActive, search, service, page, pageSize, sortBy, sortDir
```

- `search`: contains, không phân biệt hoa thường trên `code OR name`.
- `service`: `BOOKING | PARCEL`, kiểm tra phần tử trong `applicableServices`.
- Endpoint chỉ trả platform voucher (`ownerOperatorId = null`).

```http
GET /v1/admin/vouchers?page=1&pageSize=20&search=SUMMER&service=BOOKING&isActive=true
```

### 5.7. Operator vouchers

```http
GET /v1/operator/vouchers
```

Auth: `OPERATOR_ADMIN`.

Allow-list:

```text
isActive, search, service, page, pageSize, sortBy, sortDir
```

Behavior của `search`/`service` giống admin, nhưng BE luôn lấy owner từ JWT. FE không gửi
`ownerOperatorId` hoặc `fundingType`.

```http
GET /v1/operator/vouchers?page=1&pageSize=20&search=KHAITRUONG&service=PARCEL
```

### 5.8. Operator bookings

```http
GET /v1/operator/bookings
```

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

Allow-list:

```text
status, tripId, date, passengerPhone, bookingCode, search,
page, pageSize, sortBy, sortDir
```

`search` OR-match:

- booking code;
- buyer snapshot `BuyerDisplayName`;
- buyer snapshot `BuyerPhone` nếu input normalize được về số Việt Nam E.164.

BE không bổ sung passenger-name PII và không search tên từng passenger. UI nên ghi placeholder
`Mã đặt vé, tên người đặt hoặc số điện thoại` thay vì `Tên hành khách`.

Các query cũ vẫn dùng được:

- `bookingCode`: exact case-insensitive;
- `passengerPhone`: resolve chính xác passenger user qua Identity;
- `search`: ô tìm kiếm tổng quát.

Nếu gửi chung, filter cấp cao kết hợp AND:

```http
GET /v1/operator/bookings?search=Nguyen&status=CONFIRMED&tripId=<uuid>&page=1&pageSize=20
```

FE nên bỏ heuristic `isPhoneSearch` cho ô tổng quát và gửi nguyên chuỗi vào `search`. Chỉ dùng
`passengerPhone`/`bookingCode` cho filter exact chuyên biệt nếu UI vẫn có các field đó.

### 5.9. Admin trip settlements

```http
GET /v1/admin/trip-settlements
```

Auth: `SYSTEM_ADMIN`.

Allow-list:

```text
page, pageSize, operatorId, status, tripId, stuckOnly, severity,
from, to, sortBy, sortDir, search
```

Search:

- UUID: exact settlement ID hoặc Trip ID;
- text: persisted Trip/reference code theo prefix;
- text: persisted operator name hoặc active failure code theo contains.

Search chạy trước count/paging và không gọi live sang service khác.

```http
GET /v1/admin/trip-settlements?page=1&pageSize=20&search=VR-20260813&status=ELIGIBLE
```

FE bỏ filter `records.filter(...)` trên page hiện tại; mỗi thay đổi search phải refetch page 1.

### 5.10. Admin platform-wallet transactions

```http
GET /v1/admin/platform-wallet/transactions
```

Auth: `SYSTEM_ADMIN`.

Allow-list:

```text
page, pageSize, type, referenceType, from, to, sortBy, sortDir, search
```

Search:

- UUID: transaction ID hoặc reference ID;
- text: note hoặc persisted actor display name theo contains;
- enum text chính xác: reference type, ví dụ `MANUAL_ADJUSTMENT`.

```http
GET /v1/admin/platform-wallet/transactions?page=1&pageSize=20&search=MANUAL_ADJUSTMENT&sortBy=createdAt&sortDir=desc
```

FE không lọc `records` của page hiện tại.

### 5.11. Operator parcel route fares

```http
GET /v1/operator/parcel-route-fares
```

Auth: `OPERATOR_ADMIN | OPERATOR_STAFF`.

Allow-list:

```text
routeId, sizeCategory, page, pageSize, search
```

`search` tìm Route name hoặc origin/destination Station text trong đúng tenant. FE chỉ gọi public
Parcel endpoint trên; Parcel tự gọi internal Trip API.

```http
GET /v1/operator/parcel-route-fares?page=1&pageSize=20&search=Da%20Lat&sizeCategory=SMALL
```

- Không có Route khớp: `200`, page rỗng bình thường.
- Trip tạm unavailable: `503 UPSTREAM_UNAVAILABLE`; FE hiển thị retry, không coi là không có fare.
- Không fetch-all Route/Fare để join/filter tại client.

### 5.12. RAG policies — BE đã có sẵn

```http
GET /v1/admin/policies
```

Auth: `SYSTEM_ADMIN`.

Query đã có:

```text
policyType=FOR_OPERATOR|FOR_USER
category=<string>
active=true|false
search=<string>
page, pageSize
sortBy=updatedAt|createdAt|title|version
sortDir=asc|desc
```

Ví dụ:

```http
GET /v1/admin/policies?policyType=FOR_USER&category=REFUND&active=true&search=hoan%20ve&sortBy=updatedAt&sortDir=desc
```

FE chỉ cần bind query; không có API mới và không dùng `isActive` ở endpoint này — tên đúng là
`active`.

## 6. Internal API — FE không được nối

BE có thêm hai endpoint để các service lọc trước paging:

```http
GET /internal/v1/operators/{operatorId}/crew/search?search=...
GET /internal/v1/routes/search?operatorId=...&search=...
```

Đây là service-to-service API:

- chỉ nhận Internal JWT;
- không expose qua Gateway;
- success trả raw DTO, không phải public ADR envelope;
- FE không gọi, không proxy và không lưu Internal JWT.

FE vẫn chỉ gọi `/v1/operator/driver-schedules` và `/v1/operator/parcel-route-fares`.

## 7. Error handling chung

| HTTP | Code thường gặp | Cách xử lý FE |
|---|---|---|
| `401` | `UNAUTHORIZED`, `AUTH_TOKEN_INVALID` | Refresh/login theo flow chung. |
| `403` | `FORBIDDEN` | Không có quyền/tenant claim; không retry tự động. |
| `422` | `VALIDATION_ERROR` | Map `error.fields[]` vào UI; log query key/value đã gửi. |
| `503` | `UPSTREAM_UNAVAILABLE` | Hiển thị lỗi tạm thời + retry; không thay bằng empty state. |

FE nên log `meta.traceId` cùng endpoint/query khi báo lỗi để BE tra log nhanh.

## 8. Mẫu API adapter

```ts
async function getOperatorBookings(
  input: {
    search?: string;
    status?: string;
    tripId?: string;
    page: number;
    pageSize: number;
  },
  signal?: AbortSignal,
) {
  const params = new URLSearchParams(
    cleanQuery({
      ...input,
      search: input.search?.trim() || undefined,
    }) as Record<string, string>,
  );

  return apiClient.get<ApiResponse<PagedResult<OperatorBooking>>>(
    `/v1/operator/bookings?${params.toString()}`,
    { signal },
  );
}
```

Không thêm query key để lưu UI state. UI-only state như tab/card có thể nằm trong URL của FE nhưng
phải tách khỏi object gửi cho API.

## 9. Checklist bàn giao cho FE

- [ ] Bỏ `isOneTime` và card lịch một lần.
- [ ] Route gửi `isActive`, không gửi `status`.
- [ ] Vehicle dùng đúng `status` enum và `isActive` riêng.
- [ ] Location/Station/Voucher/Parcel chuyển sang server-side hoàn toàn; bỏ fetch-all.
- [ ] Station card dùng `/v1/admin/stations/summary`.
- [ ] Station merge picker dùng list search/paging hiện tại.
- [ ] Booking ô tổng quát gửi `search`, không đoán phone/code ở FE.
- [ ] Settlement/Platform transaction bỏ client-filter trên page hiện tại.
- [ ] Policies dùng `active`, không dùng `isActive`.
- [ ] Không gọi hai `/internal/v1/...` endpoint.
- [ ] Reset page và cancel request cũ khi search/filter thay đổi.
- [ ] Map `422 error.fields[]` và giữ `meta.traceId` khi report lỗi.
- [ ] Phân biệt empty result `200` với upstream failure `503`.

## 10. Xác nhận kiểm thử từ BE

BE đã chạy targeted unit/integration tests và E2E bằng Gateway + các service + PostgreSQL thật.
Ma trận liên quan pass **62/62**, bao gồm:

- search từng nguồn và filter-before-count/paging;
- tenant isolation;
- strict unknown-query `422`;
- enum/query cũ bị từ chối;
- Identity/Trip unavailable trả `503`;
- empty result vẫn giữ đúng ADR envelope;
- Seat Map snapshot/aisle và dữ liệu layout cũ.
