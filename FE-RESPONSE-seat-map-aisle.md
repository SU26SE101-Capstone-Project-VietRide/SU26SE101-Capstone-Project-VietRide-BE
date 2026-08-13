# [BE → FE] Seat Map aisle đã hoàn tất — hướng dẫn tích hợp

> Ngày bàn giao: 2026-08-13
> Phạm vi: Passenger/Driver/Assistant/Operator UI có hiển thị sơ đồ ghế của một Trip
> Trạng thái BE: **ĐÃ IMPLEMENT VÀ E2E PASS**

## 1. Kết luận

FE đã gọi đúng endpoint. Lỗi trước đây nằm ở BE: layout snapshot của Trip có dữ liệu aisle nhưng
response seat-map làm mất field này khi mapping.

BE đã sửa endpoint:

```http
GET /v1/trips/{tripId}/seat-map
Authorization: Bearer <access-token>
```

- Endpoint đi qua Gateway như hiện tại; không có prefix hay service mới cho FE.
- Endpoint **cần user access token**. Không gọi anonymous và không dùng `X-Internal-Auth`.
- Request không nhận query parameter nào.
- Response luôn có `data.aisles` là array; không trả `null` và không bỏ field.
- Aisle và ghế đều lấy từ cùng immutable seat-layout snapshot của Trip.

## 2. Response contract mới

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "f843d12c-d1de-4d9e-940d-72318583e48e",
    "vehicleType": "SLEEPER_BUS",
    "aisles": [
      { "afterCol": 2 }
    ],
    "seats": [
      {
        "seatNumber": "A01",
        "status": "AVAILABLE",
        "type": "SLEEPER_LOWER",
        "row": 1,
        "col": 1,
        "deck": 1,
        "disabledReason": null
      }
    ]
  },
  "meta": {
    "traceId": "request-trace-id",
    "timestamp": "2026-08-13T17:00:00+07:00"
  }
}
```

Ngữ nghĩa:

| Field | Ý nghĩa |
|---|---|
| `aisles` | Danh sách vị trí hành lang của layout đã snapshot cho Trip. |
| `afterCol` | Chèn hành lang ngay sau cột ghế có `col == afterCol`. |
| `[]` | Layout không khai báo hành lang. Không được tự suy luận một hành lang khác. |
| `deck` trên seat | Tầng của ghế; cùng cấu hình `aisles` được áp dụng cho từng deck trong contract v1. |

Ví dụ `aisles: [{ "afterCol": 2 }]` với các cột ghế `1..4`:

```text
col 1 | col 2 | AISLE | col 3 | col 4
```

Nếu có nhiều aisle:

```json
"aisles": [{ "afterCol": 1 }, { "afterCol": 2 }]
```

thì FE chèn spacer sau cả cột 1 và cột 2. Không hard-code xe chỉ có một aisle.

## 3. TypeScript model đề nghị

Trong code mới, nên chuyển `aisles` thành required thay vì optional:

```ts
export type SeatMapAisle = {
  afterCol: number;
};

export type TripSeatMapSeat = {
  seatNumber: string;
  status: 'AVAILABLE' | 'HELD' | 'BOOKED' | 'UNAVAILABLE';
  type: string;
  row: number;
  col: number;
  deck: number;
  disabledReason: string | null;
};

export type TripSeatMapData = {
  tripId: string;
  vehicleType: string;
  aisles: SeatMapAisle[];
  seats: TripSeatMapSeat[];
};
```

Trong giai đoạn FE còn phải chạy đồng thời với một deployment BE cũ, có thể normalize ở API
adapter:

```ts
function normalizeSeatMap(data: TripSeatMapData): TripSeatMapData {
  return {
    ...data,
    aisles: Array.isArray(data.aisles) ? data.aisles : [],
  };
}
```

Fallback này chỉ biến field thiếu thành `[]`; **không được tự chèn aisle 2|2**.

## 4. Cách dựng grid đúng

1. Chia `seats` theo `deck`, sau đó theo `row`.
2. Sắp xếp ghế trong một row theo `col` tăng dần.
3. Tạo `Set<number>` từ `aisles.map(x => x.afterCol)`.
4. Sau khi render cột `col`, nếu `aisleSet.has(col)` thì render một aisle spacer.
5. Không tạo seat giả có `seatNumber: null`; aisle là layout spacer, không phải ghế nghiệp vụ.
6. Khi click/đổi trạng thái ghế, vẫn dùng `seatNumber` của seat thật như trước.

Ví dụ:

```tsx
const aisleAfter = new Set(seatMap.aisles.map((x) => x.afterCol));

return columns.map((col) => (
  <Fragment key={col}>
    <SeatColumn col={col} seats={seatsByColumn.get(col) ?? []} />
    {aisleAfter.has(col) && (
      <div aria-hidden className="seat-map__aisle" />
    )}
  </Fragment>
));
```

Nếu UI hiện tại cần một cell placeholder để dùng CSS grid, có thể tạo view-model riêng như
`{ kind: 'AISLE' }`; không đưa placeholder đó trở lại API/domain model và không gửi nó lên BE.

## 5. Snapshot — điểm FE không được nối vòng qua Vehicle API

FE chỉ gọi:

```http
GET /v1/trips/{tripId}/seat-map
```

Không gọi thêm `GET /v1/operator/vehicles/{vehicleId}` để lấy aisle. Lý do:

- Vehicle layout là template hiện tại của xe.
- Trip giữ immutable snapshot tại thời điểm chuyến được tạo.
- Nhà xe có thể sửa template Vehicle sau đó nhưng sơ đồ của Trip cũ phải giữ nguyên.
- Chỉ các flow swap/substitute vehicle đã được BE quy định mới thay đổi snapshot của Trip.

Ghép `seats` từ Trip với `aisles` từ Vehicle có thể tạo một sơ đồ không tồn tại trong thực tế.

## 6. Xử lý lỗi

| HTTP | `error.code` | FE xử lý |
|---|---|---|
| `401` | `AUTH_TOKEN_INVALID`/`UNAUTHORIZED` | Chạy flow refresh/login hiện có; không retry anonymous. |
| `404` | `TRIP_NOT_FOUND` | Hiển thị Trip không còn tồn tại/không truy cập được. |
| `422` | `VALIDATION_ERROR` | Kiểm tra `tripId` và đảm bảo không gửi query key. |

Seat-map không nhận `deck`, `vehicleId`, `includeAisles`, `layoutVersion` hoặc query tùy ý. Ví dụ
sau là sai và sẽ bị strict-query validation từ chối:

```http
GET /v1/trips/{tripId}/seat-map?includeAisles=true
```

## 7. Việc FE cần làm

- [ ] Đảm bảo request có user `Authorization: Bearer ...`.
- [ ] Đổi model `aisles` thành array và normalize tối đa về `[]`.
- [ ] Gỡ heuristic tự chèn aisle 2|2 trong `ApiSeatGrid`/role screen.
- [ ] Render theo mọi phần tử `afterCol`, không hard-code một aisle.
- [ ] Không fetch Vehicle để bổ sung layout cho Trip.
- [ ] Test ít nhất: bus 2|2, limousine 2|1, nhiều aisle, nhiều deck và `aisles: []`.
- [ ] Test Trip đã tạo trước khi Vehicle template bị sửa vẫn hiển thị layout snapshot cũ.

## 8. Xác nhận từ BE

BE đã kiểm thử bằng unit/integration test và E2E dữ liệu PostgreSQL thật:

- camelCase và PascalCase của dữ liệu layout cũ đều đọc được;
- `aisles` lấy từ Trip snapshot;
- sửa Vehicle layout sau khi snapshot không làm đổi aisle của Trip;
- response luôn có array `aisles`.
