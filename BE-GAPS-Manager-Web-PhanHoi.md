# Phản hồi Backend Gaps — Manager Web

**Ngày cập nhật:** 2026-08-06
**Trạng thái:** Đã implement và xác minh trên local stack
**Service owner:** Trip service (`apps/trip`) và Gateway (`apps/gateway`)
**Phạm vi:** Phương tiện, capacity, seat-map theo Trip, disable/enable ghế, shuttle history, pending shuttle và Gateway RBAC.

## Mục lục

- [Phạm vi và trạng thái tổng quát](#phạm-vi-và-trạng-thái-tổng-quát)
- [Base URL](#base-url)
- [Xác thực và header chung](#xác-thực-và-header-chung)
- [Response envelope](#response-envelope)
- [Các gap đã xử lý](#các-gap-đã-xử-lý)
- [Chi tiết API](#chi-tiết-api)
- [Mã lỗi](#mã-lỗi)
- [Bằng chứng kiểm thử](#bằng-chứng-kiểm-thử)
- [Giới hạn kiểm tra](#giới-hạn-kiểm-tra)
- [Kết luận](#kết-luận)

## Phạm vi và trạng thái tổng quát

| Khu vực | Nội dung đã xử lý | Trạng thái |
|---|---|---|
| Trip layout | Lưu immutable seat-layout snapshot trên Trip; seat-map đọc snapshot của Trip | Đã hoàn tất |
| Vehicle capacity | Bổ sung `usablePassengerCapacity`; không tính ghế disabled và `DRIVER_AREA` | Đã hoàn tất |
| Seat validation | Seat number duplicate được kiểm tra không phân biệt hoa thường | Đã hoàn tất |
| Trip seat mutation | Disable/enable seat, idempotency, RBAC, audit và lock transaction | Đã hoàn tất |
| Shuttle history | Filter ngày/status, phân trang và batch profile lookup | Đã hoàn tất |
| Pending shuttle | Group theo `mainTripId + direction`, enrich `passengers[]`, batch query | Đã hoàn tất |
| Gateway routing | Route matcher hỗ trợ HTTP method để tách GET/POST và RBAC đúng | Đã hoàn tất |
| Contract/schema | Cập nhật DTO, DDL, EF migration/model snapshot và route tests | Đã hoàn tất |
| Gap không cần API mới | Không tạo endpoint mới ngoài phạm vi đã chốt | Đã giữ nguyên |

## Base URL

Frontend/Manager Web gọi public API qua Gateway.

| Thành phần | Local URL | Trách nhiệm |
|---|---:|---|
| Gateway | `http://localhost:3000` | Verify user JWT, RBAC, mint internal JWT và proxy API |
| Trip direct | `http://localhost:5002` | Vehicle, trip, seat và shuttle |
| Identity | `http://localhost:5001` | Operator/driver/passenger profile |

## Xác thực và header chung

| Loại request | Yêu cầu |
|---|---|
| Request qua Gateway | `Authorization: Bearer <user_access_token>` |
| Operator read | Role `OPERATOR_ADMIN` hoặc `OPERATOR_STAFF` |
| Operator mutation | Role `OPERATOR_ADMIN` |
| Seat disable/enable | Bắt buộc `Idempotency-Key` |
| Correlation | Có thể gửi `X-Request-Id`; request ID được dùng cho trace và audit |

Idempotency replay dùng lại kết quả đã lưu. Dùng cùng một key với body khác sẽ bị từ chối theo middleware hiện tại.

## Response envelope

Các endpoint public qua Trip được wrap bằng `ApiResponse` theo ADR 0004:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "req-123",
    "timestamp": "2026-08-06T10:00:00.0000000Z"
  }
}
```

Response lỗi:

```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "fields": []
  },
  "meta": {
    "traceId": "req-123",
    "timestamp": "2026-08-06T10:00:00.0000000Z"
  }
}
```

## Các gap đã xử lý

### 1. Immutable seat-layout snapshot trên Trip

Trip có thêm cột:

```sql
trips.seat_layout_snapshot_json JSONB NOT NULL
```

Migration backfill từ `vehicles.seat_layout_json`, sau đó enforce `NOT NULL`. Snapshot được ghi khi tạo Trip và chỉ thay đổi trong các flow đã được chấp thuận:

- Vehicle substitution.
- Vehicle swap.

Các flow edit Trip và driver schedule `ALL_PENDING` tiếp tục đi qua vehicle swap service để không bỏ sót snapshot. Seat-map lấy vị trí và loại ghế từ snapshot của Trip, nên sửa layout template của Vehicle sau khi Trip được tạo không làm thay đổi seat-map Trip đó.

### 2. Chuẩn hóa `usablePassengerCapacity`

```text
usablePassengerCapacity =
  count(seat where Disabled == false and Type != DRIVER_AREA)
```

`Vehicle.TotalSeats` vẫn giữ để tương thích API cũ. API bổ sung `usablePassengerCapacity` và không lưu thêm capacity column.

Metric dùng chung tại vehicle mapper, driver schedule projection, trip generation và shuttle dispatch. Khi dispatch:

```text
selectedPassengerCount > usablePassengerCapacity
    => 409 SHUTTLE_CAPACITY_EXCEEDED
```

### 3. Case-insensitive seat validation

Validator dùng `StringComparer.OrdinalIgnoreCase`:

- `A1` và `a1`: bị từ chối vì duplicate.
- `A1` và `A01`: vẫn là hai mã khác nhau.
- `TripSeat.Create` tiếp tục normalize seat number về uppercase.

### 4. Disable/enable ghế theo Trip

```http
POST /v1/operator/trips/{tripId}/seats/{seatNumber}/disable
POST /v1/operator/trips/{tripId}/seats/{seatNumber}/enable
```

Quyền: chỉ `OPERATOR_ADMIN`. Cả hai endpoint bắt buộc `Idempotency-Key` và trả `ApiResponse<TripSeatMapDto>` với seat-map mới nhất.

Disable body:

```json
{
  "reason": "Ghế hỏng nội thất"
}
```

Enable không cần body.

| Trạng thái trước | Thao tác | Trạng thái sau | Kết quả |
|---|---|---|---|
| `AVAILABLE` | Disable | `UNAVAILABLE` | Thành công nếu reason hợp lệ |
| `UNAVAILABLE` | Enable | `AVAILABLE` | Thành công và xóa reason |
| `HELD`/`BOOKED` | Disable | Không đổi | `409 TRIP_SEAT_IN_USE` |
| `AVAILABLE` | Enable | Không đổi | `409 TRIP_SEAT_STATE_CONFLICT` |
| `DRIVER_AREA` | Disable/enable | Không có TripSeat | `404 TRIP_SEAT_NOT_FOUND` |

Seat response có thêm field nullable:

```json
{
  "seatNumber": "A1",
  "status": "UNAVAILABLE",
  "type": "STANDARD",
  "row": 1,
  "col": 1,
  "deck": 1,
  "disabledReason": "Ghế hỏng nội thất"
}
```

Handler xác định operator ownership trước, acquire row lock bằng `SELECT ... FOR UPDATE`, rồi transition domain và ghi audit trong cùng transaction. Không phát RabbitMQ event cho disable/enable.

Audit action:

- `TRIP_SEAT_DISABLED`.
- `TRIP_SEAT_ENABLED`.

Metadata audit không lưu raw `Idempotency-Key`:

```json
{
  "seatNumber": "A1",
  "beforeStatus": "AVAILABLE",
  "afterStatus": "UNAVAILABLE",
  "reason": "Ghế hỏng nội thất",
  "requestId": "req-123"
}
```

### 5. Operator shuttle history

```http
GET /v1/operator/shuttle-trips
```

Quyền đọc: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

| Query | Mô tả |
|---|---|
| `page` | Mặc định `1` |
| `pageSize` | Mặc định `20`, giới hạn `1..100` |
| `from`/`to` | `YYYY-MM-DD`, diễn giải theo ngày Việt Nam (`Asia/Ho_Chi_Minh`); `to` bao gồm cả ngày |
| `status` | Danh sách `SCHEDULED,IN_PROGRESS,COMPLETED,CANCELLED`, phân tách bằng dấu phẩy |

Không truyền status lấy toàn bộ status, bao gồm `CANCELLED`. Sort mặc định là `scheduledDepartureTime DESC`, sau đó `shuttleTripId DESC`. Status sai trả `422 VALIDATION_ERROR`.

Response dùng `PagedResult<OperatorShuttleTripListItemDto>`:

```json
{
  "items": [
    {
      "shuttleTripId": "uuid",
      "mainTripId": "uuid",
      "direction": "INBOUND_TO_STATION",
      "status": "COMPLETED",
      "scheduledDepartureTime": "2026-08-06T01:00:00Z",
      "scheduledEndTime": "2026-08-06T02:00:00Z",
      "actualDepartureTime": "2026-08-06T01:05:00Z",
      "completedAt": "2026-08-06T02:03:00Z",
      "vehicle": { "id": "uuid", "licensePlate": "51B-123.45" },
      "driver": { "id": "uuid", "displayName": "Nguyễn Văn A", "phone": "0900000000" },
      "passengerCount": 5,
      "stopCount": 3
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 100,
  "totalPages": 5,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

Query join `ShuttleTrip` với `Vehicle`, count passenger/stop trong SQL projection và gọi Identity một lần cho toàn bộ driver của page. Tenant filter dùng `operatorId`.

### 6. Pending shuttle enrichment và batch query

```http
GET /v1/operator/shuttle-requests?page=1&pageSize=20
```

Quyền đọc: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Grouping giữ theo `mainTripId + direction`.

`bookingGroups[]` có passenger lồng bên trong:

```json
{
  "bookingId": "uuid",
  "passengerCount": 2,
  "pickupAddress": "12 Nguyễn Huệ, Quận 1",
  "pickupLat": 10.1,
  "pickupLng": 106.2,
  "distanceToStationMeters": 1200,
  "roadDistanceMeters": 1400,
  "requestedAt": "2026-08-06T00:30:00Z",
  "passengers": [
    {
      "passengerUserId": "uuid",
      "displayName": "Nguyễn Văn A",
      "phone": "0900000000",
      "ticketIds": ["uuid"]
    }
  ]
}
```

Quy ước:

- Một booking có thể có nhiều passenger; ticket aggregate theo `PassengerUserId` vào `ticketIds`.
- Profile thiếu trả `displayName: null`, `phone: null`.
- Identity transport failure trả `503 UPSTREAM_UNAVAILABLE`.
- Không gọi Booking API N+1; dùng `ShuttlePassenger.PassengerUserId` và `TicketId`.
- PII chỉ trả cho operator roles.

Pending giữ contract riêng `items/page/pageSize/totalItems`; `totalItems` là số group, không phải số booking/passenger:

```json
{
  "items": [
    {
      "mainTripId": "uuid",
      "direction": "INBOUND_TO_STATION",
      "departureDateTime": "2026-08-06T03:00:00Z",
      "hardCutoffAt": "2026-08-06T02:00:00Z",
      "stationId": "uuid",
      "stationName": "Bến xe Miền Đông",
      "pendingPassengerCount": 2,
      "bookingGroups": [],
      "suggestedBookingOrder": ["uuid"]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1
}
```

Flow mới: count group ở DB, `Skip/Take` ở DB, batch load Trip/Route/Station và manifests của page, group trong memory, gọi Identity một lần rồi project theo `suggestedBookingOrder`.

### 7. Gateway method-aware routing

`ProxyRoute` hỗ trợ matcher theo HTTP method:

| Method | Path | Role |
|---|---|---|
| `GET` | `/v1/operator/shuttle-trips` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` |
| `POST` | `/v1/operator/shuttle-trips` | `OPERATOR_ADMIN` |
| `POST` | `/v1/operator/trips/{tripId}/seats/{seatNumber}/disable` | `OPERATOR_ADMIN` |
| `POST` | `/v1/operator/trips/{tripId}/seats/{seatNumber}/enable` | `OPERATOR_ADMIN` |
| `POST` | `/v1/operator/shuttle-trips/{shuttleTripId}/cancel` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` |

Route cũ không khai báo method vẫn giữ behavior match theo path.

### 8. DDL, migration và contract

Đã cập nhật entity/configuration cho `SeatLayoutSnapshotJson`, migration `AddTripSeatLayoutSnapshot`, EF model snapshot, `db-schema/trip-route-vehicle/schema.sql`, DTO vehicle/seat-map/shuttle, Swagger annotations, Gateway route table/matcher/tests và API contract/source-of-truth liên quan.

## Chi tiết API

### GET `/v1/operator/vehicles`

Quyền: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Vehicle response giữ field cũ và bổ sung:

```json
{
  "totalSeats": 3,
  "usablePassengerCapacity": 2
}
```

`usablePassengerCapacity` là giá trị dùng cho passenger/shuttle; `totalSeats` giữ cho compatibility.

### GET `/v1/trips/{tripId}/seat-map`

Quyền: user đã đăng nhập. Response `data`:

```json
{
  "tripId": "uuid",
  "vehicleType": "MINIVAN",
  "seats": [
    {
      "seatNumber": "A1",
      "status": "AVAILABLE",
      "type": "STANDARD",
      "row": 1,
      "col": 1,
      "deck": 1,
      "disabledReason": null
    }
  ]
}
```

Layout đọc từ `Trip.SeatLayoutSnapshotJson`; status và `disabledReason` đọc từ `TripSeat`.

### POST disable/enable TripSeat

```bash
curl -X POST "http://localhost:3000/v1/operator/trips/<tripId>/seats/A1/disable" \
  -H "Authorization: Bearer <operator_admin_token>" \
  -H "Idempotency-Key: <uuid-v4>" \
  -H "Content-Type: application/json" \
  -d '{"reason":"Ghế hỏng nội thất"}'
```

Enable dùng cùng URL suffix `/enable`, giữ `Authorization` và `Idempotency-Key`, không cần body. Cả hai trả `200` và seat-map mới nhất.

### GET shuttle history và pending requests

```bash
curl "http://localhost:3000/v1/operator/shuttle-trips?page=1&pageSize=20&from=2026-08-01&to=2026-08-31&status=COMPLETED,CANCELLED" \
  -H "Authorization: Bearer <operator_token>"

curl "http://localhost:3000/v1/operator/shuttle-requests?page=1&pageSize=20" \
  -H "Authorization: Bearer <operator_token>"
```

### POST `/v1/operator/shuttle-trips`

Quyền: `OPERATOR_ADMIN`. Endpoint tạo shuttle hiện có; capacity check đã chuyển sang `usablePassengerCapacity` và giữ error code `SHUTTLE_CAPACITY_EXCEEDED`.

## Mã lỗi

| HTTP | Error code | Trường hợp |
|---:|---|---|
| 400 | `VALIDATION_ERROR` | JSON/type binding không hợp lệ |
| 403 | `FORBIDDEN` | Role hoặc operator scope không được phép |
| 404 | `TRIP_NOT_FOUND` | Trip không tồn tại hoặc không thuộc operator scope |
| 404 | `TRIP_SEAT_NOT_FOUND` | Seat không có trong TripSeat hoặc không được phép lộ tồn tại |
| 409 | `TRIP_SEAT_IN_USE` | Seat đang `HELD` hoặc `BOOKED` |
| 409 | `TRIP_SEAT_STATE_CONFLICT` | Transition enable/disable không hợp lệ |
| 409 | `SHUTTLE_CAPACITY_EXCEEDED` | Passenger vượt usable capacity |
| 422 | `VALIDATION_ERROR` | Reason thiếu/sai format hoặc status filter sai |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Cùng key nhưng body khác request ban đầu |
| 503 | `UPSTREAM_UNAVAILABLE` | Identity không truy cập được khi enrich profile |

Trip khác tenant được xử lý qua operator ownership check để không lộ tồn tại của Trip/seat.

## Bằng chứng kiểm thử

### Verification tự động sau merge

```text
npx nx test gateway --testPathPatterns=routes.spec.ts --runInBand
2 suites passed, 61 tests passed

dotnet build apps/trip/VietRide.Trip.sln -c Release --no-restore
Build succeeded, 0 warnings, 0 errors
```

### E2E qua Gateway → Identity → Trip

| Flow | Kết quả |
|---|---|
| Vehicle trả `totalSeats=3`, `usablePassengerCapacity=2` với 1 `DRIVER_AREA` | Pass |
| Shuttle history staff read, filter ngày Việt Nam (`Asia/Ho_Chi_Minh`)/status, gồm `COMPLETED` và `CANCELLED` | Pass |
| `passengerCount`, `stopCount`, driver profile | Pass |
| Status history không hợp lệ trả `422` | Pass |
| Pending group và nested `bookingGroups[].passengers[]` | Pass |
| Một booking nhiều passenger, ticket aggregate | Pass |
| Admin disable, response seat-map và `disabledReason` | Pass |
| Idempotency replay | Pass |
| Staff mutation `403`, seat `HELD` `409`, `DRIVER_AREA` `404` | Pass |
| Enable xóa `disabledReason` và audit `TRIP_SEAT_DISABLED` | Pass |
| Sửa Vehicle layout không đổi Trip seat-map | Pass |
| Shuttle vượt capacity trả `409 SHUTTLE_CAPACITY_EXCEEDED` | Pass |
| Cleanup fixture, DB residue `0`, Gateway/Trip/Identity health | Pass |

### Gateway route test

Đã kiểm tra GET shuttle history chọn route cho admin/staff, POST create shuttle và seat mutation chỉ chọn route admin, cancel route cho admin/staff, và route cũ không đổi behavior.

## Giới hạn kiểm tra

Các mục sau chưa chạy qua HTTP thật trong vòng E2E này:

- Concurrent race test với hai request disable cùng một seat.
- Benchmark/N+1 query measurement cho pending shuttle ở page size `20` và `100`.
- Case-insensitive duplicate validation qua vehicle creation API; unit test validator đã pass.

Đây là kiểm tra bổ sung, không phải lỗi phát hiện trong các smoke/E2E flow đã chạy. Migration và thay đổi code mới được xác minh trên local/build/test; cần chạy migration và smoke lại trong môi trường deploy trước production.

## Kết luận

Các gap Backend phục vụ Manager Web trong phạm vi Trip và Gateway đã được implement, cập nhật contract/schema và xác minh qua focused test, build và real-stack E2E. Các flow chính đã đáp ứng RBAC, idempotency, tenant isolation, immutable Trip seat layout, usable capacity và shuttle response enrichment theo contract hiện tại.
