# Phản hồi Backend — Theo dõi xe trung chuyển phía nhà xe

- **File yêu cầu FE:** `FE-REQUEST-shuttle-operator-tracking.md`
- **Ngày phản hồi:** 2026-08-15
- **Service:** Trip + Tracking
- **Trạng thái:** Đã xử lý trên source; cần deploy và smoke test lại trên môi trường đích
- **Commit Gap A:** `8b0d0835` — `feat(tracking): expose operator shuttle context`
- **Commit Gap B:** `b1b4d7af` — `feat(tracking): include shuttles in operator fleet`

## Kết luận cho FE

Backend đã xử lý cả hai gap trong yêu cầu:

1. Operator cùng nhà xe đã có endpoint riêng để đọc toàn bộ điểm đón và bến của một Shuttle Trip.
2. `fleet-latest` đã hỗ trợ opt-in xe trung chuyển, trả chung trong một response có field phân loại
   `kind: "TRIP" | "SHUTTLE"`.

Các thay đổi đã được commit nhưng **chưa thể hiện rằng môi trường production/staging đã deploy**. FE chỉ
nên chuyển trạng thái sang “đã sẵn sàng trên môi trường” sau khi image chứa hai commit trên được deploy
và các API được smoke test qua Gateway.

## Trạng thái từng gap

| Gap FE báo | Kết quả Backend | FE cần làm |
|---|---|---|
| Không đọc được `stops` của Shuttle Trip | **Đã thêm** `operator-context` | Gọi endpoint khi mở panel/bản đồ Shuttle và dùng `stops` làm source of truth |
| Shuttle không có trong `fleet-latest` | **Đã thêm** opt-in `include=shuttle` | Gọi fleet API với param mới và branch item theo `kind` |
| ETA chỉ có `nextPickupOrder` | Đã có dữ liệu đối chiếu trong `operator-context.stops` | Tìm stop có `pickupOrder === nextPickupOrder` để hiển thị địa chỉ |
| Thiếu pin bến và điểm đón | Đã trả tọa độ stop cùng `station` | Vẽ marker từ response Backend; không ghép từ pending request |
| FE phải gọi N+1 `latest` cho Shuttle | Không còn cần thiết trên màn hình fleet | Bỏ fallback N+1 sau khi môi trường đã deploy Gap B |

## 1. GAP A — Operator Shuttle context

### Endpoint

```http
GET /v1/tracking/shuttle-trips/{shuttleTripId}/operator-context
Authorization: Bearer <OPERATOR_ADMIN hoặc OPERATOR_STAFF access token>
```

Quyền truy cập:

- `OPERATOR_ADMIN` và `OPERATOR_STAFF` chỉ được đọc Shuttle Trip thuộc đúng `operatorId` trong JWT.
- Operator khác nhà xe nhận `403 TRACKING_ACCESS_DENIED`.
- Passenger/Driver không được dùng endpoint này.
- Passenger tiếp tục dùng endpoint riêng hiện có:

  ```http
  GET /v1/tracking/shuttle-trips/{shuttleTripId}/passenger-context
  ```

Response luôn đặt:

```http
Cache-Control: private, no-store
```

Lý do: response có địa chỉ phục vụ của hành khách và không được cache dùng chung.

### Response mẫu

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "shuttleTripId": "36000000-0000-4000-8000-000000000001",
    "mainTripId": "36000000-0000-4000-8000-000000000101",
    "direction": "INBOUND_TO_STATION",
    "status": "IN_PROGRESS",
    "stops": [
      {
        "pickupOrder": 1,
        "bookingId": "36000000-0000-4000-8000-000000000201",
        "latitude": 10.7731,
        "longitude": 106.7032,
        "status": "PENDING",
        "isStation": false,
        "serviceAddress": "123 Nguyễn Huệ, Quận 1, TP.HCM",
        "serviceOrder": 1,
        "roadDistanceMeters": 9500
      },
      {
        "pickupOrder": 2,
        "bookingId": "36000000-0000-4000-8000-000000000202",
        "latitude": 10.7901,
        "longitude": 106.6802,
        "status": "PICKED_UP",
        "isStation": false,
        "serviceAddress": "45 Điện Biên Phủ, Bình Thạnh, TP.HCM",
        "serviceOrder": 2,
        "roadDistanceMeters": 5200
      },
      {
        "pickupOrder": 3,
        "bookingId": null,
        "latitude": 10.8100,
        "longitude": 106.6300,
        "status": "PENDING",
        "isStation": true,
        "serviceAddress": "Bến xe Miền Đông",
        "serviceOrder": 3
      }
    ],
    "station": {
      "stationId": "36000000-0000-4000-8000-000000000301",
      "name": "Bến xe Miền Đông",
      "latitude": 10.8100,
      "longitude": 106.6300,
      "pickupOrder": 3
    }
  },
  "meta": {
    "traceId": "req-123",
    "timestamp": "2026-08-15T22:00:00+07:00"
  }
}
```

### TypeScript đề nghị

```ts
type ShuttleDirection =
  | "INBOUND_TO_STATION"
  | "OUTBOUND_FROM_STATION";

type ShuttleTripStatus =
  | "SCHEDULED"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "CANCELLED"
  | string;

type ShuttleStopStatus =
  | "PENDING"
  | "PICKED_UP"
  | "DELIVERED"
  | "NO_SHOW"
  | "CANCELLED"
  | string;

type OperatorShuttleTrackingStop = {
  pickupOrder: number;
  bookingId: string | null;
  latitude: number;
  longitude: number;
  status: ShuttleStopStatus;
  isStation: boolean;
  serviceAddress?: string;
  serviceOrder?: number;
  roadDistanceMeters?: number;
};

type OperatorShuttleStation = {
  stationId: string;
  name: string;
  latitude: number;
  longitude: number;
  pickupOrder: number;
};

type OperatorShuttleContext = {
  shuttleTripId: string;
  mainTripId: string;
  direction: ShuttleDirection;
  status: ShuttleTripStatus;
  stops: OperatorShuttleTrackingStop[];
  station: OperatorShuttleStation | null;
};
```

Backend không trả các field nội bộ sau:

- `isOwnPickup`
- `roadDistanceSnapshotMeters`
- `passengerUserId`
- Tên hoặc số điện thoại hành khách

FE không cần thêm request sang Identity để lấy tên hành khách. Với màn hình tracking, địa chỉ, thứ tự,
tọa độ và trạng thái stop là dữ liệu đủ dùng.

### Cách ghép ETA với điểm đón

Khi nhận REST/socket ETA:

```json
{
  "nextPickupOrder": 1,
  "etaMinutes": 12
}
```

FE đối chiếu trực tiếp:

```ts
const nextStop = context.stops.find(
  (stop) => stop.pickupOrder === eta.nextPickupOrder,
);

const nextPickupLabel =
  nextStop?.serviceAddress ?? `Điểm số ${eta.nextPickupOrder}`;
```

Không đối chiếu bằng index mảng vì `pickupOrder` là business order và có thể không trùng array index.

### Error code Gap A

| HTTP | Error code | Ý nghĩa |
|---|---|---|
| 400 | `VALIDATION_FAILED` | `shuttleTripId` không phải UUID hợp lệ |
| 401 | `UNAUTHORIZED` | Thiếu hoặc sai access token |
| 403 | `TRACKING_ACCESS_DENIED` | Sai role hoặc Shuttle không thuộc nhà xe |
| 404 | `SHUTTLE_TRIP_NOT_FOUND` | Shuttle Trip không tồn tại |
| 503 | `TRACKING_AUTH_UNAVAILABLE` | Không xác minh được quyền từ Trip |
| 503 | `TRACKING_CONTEXT_UNAVAILABLE` | Context stop/station không đủ hoặc không hợp lệ |

## 2. GAP B — Shuttle trong operator fleet-latest

### Endpoint

```http
GET /v1/tracking/operator/fleet-latest?include=shuttle&status=IN_PROGRESS
Authorization: Bearer <OPERATOR_ADMIN hoặc OPERATOR_STAFF access token>
```

Quy tắc query:

- `include=shuttle` là opt-in; đây là giá trị `include` duy nhất được hỗ trợ.
- Không truyền `include=shuttle`: chỉ trả main Trip như trước.
- Khi `include=shuttle`, Shuttle chỉ được thêm nếu `status` không truyền hoặc bằng `IN_PROGRESS`.
- `status` khác `IN_PROGRESS`: chỉ trả main Trip theo filter, không trả Shuttle.
- `include` khác `shuttle`: `400 VALIDATION_FAILED`.

### Response mẫu

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "kind": "TRIP",
        "tripId": "36000000-0000-4000-8000-000000000401",
        "latitude": 10.51,
        "longitude": 106.12,
        "speedKmh": 47.5,
        "headingDeg": 215,
        "recordedAt": "2026-08-15T14:59:58.000Z",
        "status": "IN_PROGRESS"
      },
      {
        "kind": "SHUTTLE",
        "shuttleTripId": "36000000-0000-4000-8000-000000000001",
        "mainTripId": "36000000-0000-4000-8000-000000000101",
        "latitude": 10.76,
        "longitude": 106.66,
        "speedKmh": 24,
        "headingDeg": 120,
        "recordedAt": "2026-08-15T14:59:59.000Z",
        "status": "IN_PROGRESS"
      }
    ],
    "generatedAt": "2026-08-15T15:00:00.000Z"
  },
  "meta": {
    "traceId": "req-456",
    "timestamp": "2026-08-15T22:00:00+07:00"
  }
}
```

### TypeScript đề nghị

```ts
type TripFleetLatestItem = {
  kind: "TRIP";
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
  status: OperatorTripStatus;
};

type ShuttleFleetLatestItem = {
  kind: "SHUTTLE";
  shuttleTripId: string;
  mainTripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
  status: "IN_PROGRESS";
};

type OperatorFleetLatestItem =
  | TripFleetLatestItem
  | ShuttleFleetLatestItem;

type OperatorFleetLatestData = {
  items: OperatorFleetLatestItem[];
  generatedAt: string;
};
```

FE nên branch bằng `kind`:

```ts
for (const item of response.data.items) {
  if (item.kind === "SHUTTLE") {
    renderShuttleMarker({
      key: `shuttle:${item.shuttleTripId}`,
      shuttleTripId: item.shuttleTripId,
      mainTripId: item.mainTripId,
      latitude: item.latitude,
      longitude: item.longitude,
      headingDeg: item.headingDeg,
    });
    continue;
  }

  renderTripMarker({
    key: `trip:${item.tripId}`,
    tripId: item.tripId,
    latitude: item.latitude,
    longitude: item.longitude,
    headingDeg: item.headingDeg,
  });
}
```

Các invariant FE cần giữ:

- Không đọc `tripId` trên item `SHUTTLE`.
- Không dùng chung một key UUID trần cho Trip và Shuttle; nên prefix `trip:` hoặc `shuttle:`.
- Không map trạng thái Shuttle sang vocabulary của main Trip.
- Main Trip item luôn có thêm field additive `kind: "TRIP"`, kể cả khi không opt-in Shuttle.
- Shuttle GPS gốc dùng field `heading`; fleet response đã chuẩn hóa thành `headingDeg` cho FE.

### TTL và trạng thái mất tín hiệu

Main Trip và Shuttle latest GPS đều dùng TTL **300 giây**.

- Redis key đã hết TTL hoặc chưa có GPS: item không xuất hiện trong `items`.
- Shuttle `COMPLETED`, `CANCELLED` hoặc `SCHEDULED`: không được đưa vào active Shuttle projection.
- FE có thể tiếp tục đánh dấu `lost` cho marker đang giữ local khi `recordedAt` cũ hơn 300 giây, nhưng
  lần refetch tiếp theo Backend sẽ không còn trả item đó.

### Error code Gap B

| HTTP | Error code | Ý nghĩa |
|---|---|---|
| 400 | `VALIDATION_FAILED` | `status` hoặc `include` không hợp lệ |
| 401 | `UNAUTHORIZED` | Thiếu hoặc sai access token |
| 403 | `FORBIDDEN` | Principal không phải Operator hoặc thiếu `operatorId` |
| 503 | `TRACKING_FLEET_UNAVAILABLE` | Trip projection, Shuttle projection hoặc Redis không khả dụng |

## Những phần không thay đổi

- Socket contract giữ nguyên:
  - `joinShuttleTracking`
  - `shuttle:gps:update`
  - `shuttle:eta:update`
- REST fallback giữ nguyên:
  - `GET /v1/tracking/shuttle-trips/{shuttleTripId}/latest`
  - `GET /v1/tracking/shuttle-trips/{shuttleTripId}/eta`
- Không có Gateway route mới; `/v1/tracking/*` đã được proxy sẵn.
- Không thay đổi request tạo/hủy Shuttle Trip.
- Không thêm tên hành khách vào tracking context.

## Checklist cập nhật FE

- [ ] Thêm client method gọi `operator-context` với Operator access token.
- [ ] Khi chọn Shuttle, nạp `operator-context` song song với `latest` và `eta` ban đầu.
- [ ] Vẽ toàn bộ marker từ `context.stops`, dùng `isStation` để phân biệt bến.
- [ ] Đối chiếu ETA bằng `pickupOrder`, không dùng array index.
- [ ] Hiển thị `serviceAddress` cho “điểm đón kế tiếp”; có fallback khi field vắng.
- [ ] Xử lý `station: null` mà không làm crash bản đồ.
- [ ] Đổi fleet request thành `fleet-latest?include=shuttle` hoặc
      `fleet-latest?include=shuttle&status=IN_PROGRESS`.
- [ ] Khai báo fleet item dưới dạng discriminated union theo `kind`.
- [ ] Dùng `shuttleTripId` cho Shuttle và `tripId` cho main Trip.
- [ ] Bỏ N+1 request Shuttle `latest` trên màn hình fleet sau khi deploy Gap B.
- [ ] Giữ REST `latest`/`eta` riêng khi mở panel chi tiết Shuttle.
- [ ] Map `403 TRACKING_ACCESS_DENIED`, `403 FORBIDDEN` và `503 TRACKING_FLEET_UNAVAILABLE`.
- [ ] Refetch và xác nhận marker biến mất sau TTL hoặc sau khi Shuttle hoàn thành.

## Bằng chứng xác minh Backend

| Gate | Kết quả |
|---|---|
| Gap A Shuttle service unit | **12/12 passed** |
| Gap A controller E2E | **8/8 passed** |
| Gap B Trip handler unit | **1/1 passed** |
| Gap B internal endpoint integration | **2/2 passed** |
| Gap B Tracking aggregation/provider unit | **6/6 passed** |
| Gap B operator fleet controller E2E | **3/3 passed** |
| Tracking lint | Passed, 0 error |
| Tracking build | Passed |
| Scoped .NET formatter và CRLF | Passed |
| `git diff --check` trước commit | Passed |

Hai kiểm tra runtime chưa được xác nhận trong phiên implementation:

- PostgreSQL projection integration test compile thành công nhưng không chạy được vì local PostgreSQL
  `127.0.0.1:5432` không hoạt động.
- Live smoke scripts chưa chạy vì workspace không có Gateway/Tracking URL, Operator/Passenger token và
  Shuttle fixture IDs.

Vì vậy, FE/QA cần thực hiện smoke test sau deploy trước khi xác nhận hoàn tất trên môi trường thật.

## Checklist retest sau deploy

- [ ] Xác nhận image deploy chứa commit `8b0d0835` và `b1b4d7af` hoặc commit hậu duệ.
- [ ] Operator cùng nhà xe gọi `operator-context` nhận `200`, đủ passenger stops và station.
- [ ] Operator khác nhà xe gọi cùng Shuttle ID nhận `403 TRACKING_ACCESS_DENIED`.
- [ ] Passenger gọi `operator-context` nhận `403`; `passenger-context` cũ vẫn hoạt động.
- [ ] Fleet có hai main Trip và một active Shuttle có GPS trả ba item với đúng một `kind: "SHUTTLE"`.
- [ ] Không truyền `include=shuttle` thì response không chứa item Shuttle.
- [ ] `include=bus` trả `400 VALIDATION_FAILED`.
- [ ] Other operator không thấy Shuttle của nhà xe đang kiểm thử.
- [ ] Shuttle hoàn thành hoặc latest GPS hết TTL không còn trong fleet response.
- [ ] `nextPickupOrder` hiển thị đúng `serviceAddress` từ operator context.

