# Spec: Lưu tuyến đường thực tế (Path Polyline) cho Route / AlternativeRoute

- **Ngày:** 2026-07-07
- **Trạng thái:** REVIEWED — implementation-ready; bắt buộc hoàn thành Task 0 (SOT/contract sync) trước khi sửa code
- **Phạm vi service:** Trip (thay đổi chính). **Tracking: 0 thay đổi code** — contract nội bộ giữ nguyên.
- **Quyết định nền (đã chốt qua brainstorming):**
  1. **FE lấy polyline** từ Directions API (Google/Goong), hiển thị cho nhà xe xác nhận, rồi gửi **encoded polyline string** (chuẩn Google, precision 5) lên BE. BE không giữ API key map, không gọi Directions.
  2. **Một polyline cho cả tuyến** — cột mới trên bảng `Route`; KHÔNG lưu theo từng chặng (leg).
  3. Làm cho **cả `Route` lẫn `AlternativeRoute`** ngay đợt này (AlternativeRoute lưu sẵn, Tracking chưa dùng — xem Lưu ý L-1).
  4. Endpoint tracking `route-geometry` **decode server-side, trả `points[]` như shape hiện tại** → Tracking không sửa gì.
  5. Validation mức **cơ bản + khớp stop** (mọi stop cách polyline ≤ 500m).

---

## 1. Bối cảnh

### 1.1 Vấn đề

Off-route detection bên Tracking hiện đo khoảng cách GPS tới **đường thẳng nối các stop** vì Trip chỉ trả tọa độ stop:

- `GetTripRouteGeometryTrackingHandler.cs` map thẳng tọa độ `TripStop` → `points[]`.
- Đường thật cong theo quốc lộ/cao tốc → xe chạy đúng tuyến vẫn có thể cách "đường chim bay" giữa 2 stop > 500m → **báo lệch tuyến sai**.
- Route có 0–1 stop trung gian → < 2 points → detection **tắt hẳn** (guard `points.length < 2` ở cả hai phía).

### 1.2 Hiện trạng đã xác minh (code reality)

| Thành phần | Trạng thái | Vị trí |
|---|---|---|
| Nhà xe setup tuyến: tạo route → thêm stops từng cái → alternative routes | ✅ Có | `OperatorRoutesController.cs` (`POST /v1/operator/routes`, `POST {id}/stops`, `DELETE {id}/stops/{stopId}`, `POST {id}/alternative-routes`) |
| `Route` entity (không có trường geometry) | ✅ Có | `apps/trip/.../Domain/Entities/Route.cs` |
| `AlternativeRoute` + `AlternativeRouteStop` (stops replace cả list khi create/update) | ✅ Có | `Domain/Entities/AlternativeRoute.cs`, `Features/AlternativeRoutes/` |
| Endpoint nội bộ `GET /v1/internal/trips/{tripId}/route-geometry` trả `{ tripId, points[], alertRecipientUserIds }` | ✅ Có | `InternalTripsController.cs`, `GetTripRouteGeometryTrackingHandler.cs` |
| Tracking: nearest-segment distance + ngưỡng 500m / 2 phút liên tục + cache TTL | ✅ Có, không sửa | `apps/tracking/src/off-route/off-route.service.ts`, `off-route.constants.ts` |
| `Trip` chỉ tham chiếu `RouteId` — chưa có cơ chế gán trip chạy theo AlternativeRoute | ⚠️ Giới hạn | `Domain/Entities/Trip.cs:14` |
| `Station.Latitude/Longitude` nullable; `Stop` có lat/lng non-null | ✅ Đã xác minh | `Domain/Entities/Station.cs:17-18` |
| Cột polyline trong DB / decoder polyline trong Trip service | ❌ **Thiếu — spec này** | — |

---

## 2. Business Rules

### BR-1 — Nguồn và định dạng polyline

- FE gọi Directions API, cho nhà xe xem/duyệt đường đi trên bản đồ, rồi gửi **encoded polyline string** lên BE.
- Định dạng: thuật toán Encoded Polyline của Google, **precision 5** (mặc định của Google Directions và Goong). KHÔNG nhận precision 6 (Mapbox/OSRM mặc định) — xem Lưu ý L-6.
- BE chỉ validate + lưu nguyên chuỗi; decode khi cần dùng.

### BR-2 — Lưu trữ

- `Route.PathPolyline` : `string?` (nullable, DB `text`).
- `AlternativeRoute.PathPolyline` : `string?` (nullable, DB `text`).
- Nullable = chưa setup → mọi hành vi giữ như hiện tại (fallback BR-6). Không backfill dữ liệu cũ.
- Domain method riêng `SetPathGeometry(string? encodedPolyline)` trên cả hai entity — KHÔNG nhét vào `UpdateDetails`.

### BR-3 — API cho nhà xe

| Endpoint | Body | Quyền |
|---|---|---|
| `PUT /v1/operator/routes/{id}/geometry` | `{ "pathPolyline": "<encoded>" }` hoặc `{ "pathPolyline": null }` để xóa | `OPERATOR_ADMIN` (như các write khác) |
| `PUT /v1/operator/alternative-routes/{id}/geometry` | như trên | `OPERATOR_ADMIN` |

- Gọi **sau khi** đã setup xong stops (validation BR-4 cần stops hiện có).
- Response: `RouteDto` / `AlternativeRouteDto` đã cập nhật và có `pathPolyline`.
- Chỉ DTO dùng cho **detail/mutation response** được mang `pathPolyline`. Endpoint list route/alternative route không trả chuỗi này để tránh payload tăng tới 100 KB cho mỗi item. Nếu code hiện tại dùng chung một DTO cho list và detail, tách lightweight list DTO hoặc projection riêng; không thêm `pathPolyline` vào projection list.
- Ownership check theo `operatorId` như các endpoint operator khác.

### BR-4 — Validation khi set geometry (thứ tự chặn sớm)

1. **Kích thước chuỗi** ≤ 100 KiB (`Encoding.UTF8.GetByteCount(pathPolyline) <= 102_400`) → sai trả `ROUTE_GEOMETRY_TOO_LARGE`. Encoded polyline hợp lệ là ASCII; non-ASCII sẽ fail decode ở bước 2.
2. **Decode được** theo thuật toán polyline chuẩn Google → sai trả `ROUTE_GEOMETRY_INVALID`.
3. **Số điểm decode** trong khoảng [2, 10 000] → sai trả `ROUTE_GEOMETRY_INVALID`.
4. **Tọa độ hợp lệ**: lat ∈ [−90, 90], lng ∈ [−180, 180] → sai trả `ROUTE_GEOMETRY_INVALID`.
5. **Khớp stop**: mọi stop của tuyến (Route → `RouteStop`; AlternativeRoute → `AlternativeRouteStop`; tọa độ lấy từ `Stop`) phải cách polyline ≤ **500 m** (point-to-segment, cùng công thức equirectangular mà Tracking dùng, hằng số `111_320 m/độ vĩ`).
6. **Khớp station**: station có tọa độ cũng phải cách polyline ≤ 500 m; station thiếu một trong hai tọa độ thì bỏ qua. Route kiểm tra origin + destination. AlternativeRoute kiểm tra **origin của parent Route** + destination riêng của AlternativeRoute.
7. Nếu có stop/station lệch, trả `ROUTE_GEOMETRY_STOP_MISMATCH` với `error.fields` ổn định: field `stopIds` chứa các Stop UUID lệch và field `stationIds` chứa các Station UUID lệch; chỉ đưa field có danh sách khác rỗng. Giá trị dùng chuỗi UUID phân cách bằng dấu phẩy để khớp `ValidationError(field, message)` hiện tại; API Contract phải ghi đúng shape này để FE parse thống nhất.
8. KHÔNG yêu cầu số stop tối thiểu — route đi thẳng bến-đến-bến (0 stop trung gian) vẫn set được polyline (đây chính là case detection đang tắt, polyline giúp bật lên).
9. `pathPolyline = null` → bỏ qua toàn bộ, set null (xóa geometry).

### BR-5 — Tự xóa polyline khi hình dạng tuyến thay đổi (safe degradation)

Polyline cũ không còn khớp tuyến → nếu giữ lại sẽ gây báo lệch tuyến sai liên tục. Vì vậy set `PathPolyline = null` khi:

| Thao tác | Áp dụng cho |
|---|---|
| `AddRouteStop` / `RemoveRouteStop` | `Route.PathPolyline` |
| `UpdateRoute` **có đổi** `OriginStationId` hoặc `DestinationStationId` | `Route.PathPolyline` |
| `UpdateAlternativeRoute` có `HasStops=true` (replace stops list) hoặc destination thực sự đổi | `AlternativeRoute.PathPolyline` |

Sau khi bị xóa, hệ thống rơi về fallback BR-6 (an toàn như hiện tại) cho tới khi nhà xe lưu polyline mới. FE cần nhắc nhà xe cập nhật lại đường đi sau khi sửa stops (Lưu ý L-2).

Lưu ý:
- `UpdateRouteRequest` hiện **không** cho đổi origin/destination station (`UpdateRouteCommand` không nhận 2 field này) — rule này ghi để phòng khi API mở ra sau; hiện tại chỉ cần implement cho Add/RemoveRouteStop và UpdateAlternativeRoute.
- Với AlternativeRoute, `HasStops=true` luôn được coi là thay đổi hình dạng và clear polyline, kể cả danh sách gửi lên giống dữ liệu cũ; tránh query/so sánh sâu không cần thiết. Destination chỉ clear khi UUID mới khác `DestinationStationId` hiện tại. Các thay đổi name/description/distance/duration/isActive không clear geometry.

### BR-6 — Endpoint tracking: ưu tiên polyline, fallback stop

`GetTripRouteGeometryTrackingHandler` bỏ nested `IMediator.Send(GetTripRouteStopsTrackingQuery)` và inject trực tiếp `ITripRepository`, `IRouteRepository`, `ITripStopRepository`, `IStopRepository`. Handler load Trip đúng **một lần**, lấy `trip.RouteId`, rồi load Route đúng một lần:

```
route = load qua trip.RouteId
if route.PathPolyline != null:
    points = decode(route.PathPolyline)      // đường thật, dày đặc
else:
    points = tọa độ TripStop snapshot theo OrderIndex  // logic hiện tại, giữ nguyên
```

- Response shape **không đổi**: `{ tripId, points[], alertRecipientUserIds }`.
- Trip không tồn tại → `TRIP_NOT_FOUND` như hiện tại. Trip tồn tại nhưng Route bị thiếu bất thường cũng map `TRIP_NOT_FOUND` để không mở thêm error code cho internal contract.
- Fallback tiếp tục dùng `TripStop` snapshot, không dùng `RouteStop`, nhằm giữ hành vi của chuyến đã sinh.
- Tracking service không sửa dòng nào; provider/zod schema/cache/detection giữ nguyên.

### BR-7 — AlternativeRoute: lưu trước, dùng sau

Polyline của AlternativeRoute chỉ được **lưu và trả qua DTO**; endpoint tracking CHƯA dùng vì `Trip` chưa có cơ chế "trip này đang chạy theo alternative route X". Khi tính năng đó ra đời, BR-6 mở rộng: chọn polyline theo tuyến đang chạy.

---

## 3. Thiết kế chi tiết

### 3.1 Domain (`VietRide.Trip.Domain`)

- `Route.PathPolyline` + `Route.SetPathGeometry(string?)` — chỉ gán, validation nằm ở Application (cần decode + query stops, không thuộc invariant entity).
- `AlternativeRoute.PathPolyline` + `SetPathGeometry(string?)` — tương tự.

### 3.2 Application (`VietRide.Trip.Application`)

**Utility mới** (đặt tại `Common/Geometry/` trong Application):

- `PolylineCodec.Decode(string) → IReadOnlyList<(double Lat, double Lng)>` — thuật toán chuẩn Google (~30 dòng, không thêm package). Chỉ cần Decode; không cần Encode.
- `GeoDistance.PointToPolylineMeters(point, polyline)` — port công thức point-to-segment equirectangular từ `apps/tracking/src/off-route/off-route.service.ts:149-195` sang C# (xem Lưu ý L-5 về đồng bộ hằng số).

**Features mới:**

- `Features/Routes/SetRouteGeometryCommand + Handler + Validator` — validation BR-4, ownership check, gọi `SetPathGeometry`.
- `Features/AlternativeRoutes/SetAlternativeRouteGeometryCommand + Handler + Validator` — tương tự.

**Features sửa:**

- DTO/mapper detail và mutation response của Route/AlternativeRoute — thêm `PathPolyline`; giữ list projection lightweight theo BR-3.
- `AddRouteStopHandler`, `RemoveRouteStopHandler` — sau khi mutate stop, `route.SetPathGeometry(null)` (BR-5).
- `UpdateAlternativeRouteHandler` — tương tự khi stops/destination đổi (BR-5).
- `GetTripRouteGeometryTrackingHandler` — BR-6; đổi sang repository dependencies trực tiếp, không gọi nested MediatR query.

### 3.3 API (`VietRide.Trip.Api`)

- `OperatorRoutesController`: `PUT {id:guid}/geometry` + request record `SetRouteGeometryRequest`.
- `OperatorAlternativeRoutesController`: `PUT {id:guid}/geometry`.

### 3.4 Infrastructure

- EF configuration: `PathPolyline` cột `text`, nullable, trên `RouteConfiguration` + `AlternativeRouteConfiguration`.
- **Một migration** `AddRoutePathPolyline` cho cả hai bảng.

### 3.5 Error codes

| Code | HTTP | Khi nào |
|---|---|---|
| `ROUTE_GEOMETRY_TOO_LARGE` | 422 | Chuỗi > 100 KB |
| `ROUTE_GEOMETRY_INVALID` | 422 | Decode fail / điểm ∉ [2, 10 000] / tọa độ ngoài range |
| `ROUTE_GEOMETRY_STOP_MISMATCH` | 422 | Stop/station cách polyline > 500 m; `error.fields` dùng `stopIds` và/hoặc `stationIds` |
| `ROUTE_NOT_FOUND` | 404 | Route hoặc AlternativeRoute sai id/khác operator; giữ convention Day-8 hiện tại, **không tạo** `ALTERNATIVE_ROUTE_NOT_FOUND` |

---

## 4. Testing

**Unit (Trip.Application):**

- `PolylineCodec`: decode vector mẫu chuẩn từ Google docs (chuỗi "_p~iF~ps|U_ulLnnqC_mqNvxq`@" — có ký tự backtick trước @ — → 3 điểm (38.5, -120.2), (40.7, -120.95), (43.252, -126.453)); chuỗi rác → exception/fail có kiểm soát.
- `PolylineCodec`: thêm case input bị truncate giữa varint, overflow, ký tự non-ASCII, chỉ decode được 0–1 điểm, và > 10 000 điểm; mọi lỗi map ổn định về `ROUTE_GEOMETRY_INVALID`, không rò exception thành 500.
- `GeoDistance.PointToPolylineMeters`: cùng test vectors với `off-route.service.spec.ts` bên Tracking để hai implementation cho kết quả khớp nhau.
- `SetRouteGeometryHandler`: từng nhánh validation BR-4 (quá size, decode fail, quá điểm, stop lệch, origin/destination station lệch → đúng error code + `stopIds`/`stationIds`); happy path; set null; ownership isolation.
- `SetAlternativeRouteGeometryHandler`: kiểm tra stop, origin station của parent Route, destination station riêng; ownership isolation; dùng `ROUTE_NOT_FOUND` cho id sai/cross-tenant.
- `AddRouteStop/RemoveRouteStop/UpdateAlternativeRoute`: polyline bị clear và persist trong cùng transaction (BR-5); update chỉ name/description không clear; destination giữ nguyên không clear; `HasStops=true` luôn clear.

**Integration (Trip.IntegrationTests):**

- `PUT geometry` → route/alternative-route detail hoặc mutation response trả `pathPolyline`; list response không chứa `pathPolyline`.
- `PUT geometry` với polyline lệch stop → 422 `ROUTE_GEOMETRY_STOP_MISMATCH`.
- Geometry chỉ lệch station → 422 với `stationIds`; geometry lệch cả hai → có cả `stopIds` và `stationIds`.
- Cả hai endpoint: cross-operator → 404 `ROUTE_NOT_FOUND`; `OPERATOR_STAFF` → 403.
- Trip thuộc route có polyline → `GET /v1/internal/trips/{tripId}/route-geometry` trả points decode từ polyline (nhiều hơn số stop).
- Route không có polyline → response giữ nguyên hành vi cũ (cập nhật `InternalTripsEndpointTests` hiện có nếu cần).
- Add stop → polyline cleared → route-geometry fallback về stop points.

---

## 5. Thứ tự triển khai bắt buộc

### Task 0 — SOT/contract sync (phải xong trước code)

- Bump version + changelog `BACKEND_SOURCE_OF_TRUTH.md`; đăng ký đúng 3 code `ROUTE_GEOMETRY_TOO_LARGE`, `ROUTE_GEOMETRY_INVALID`, `ROUTE_GEOMETRY_STOP_MISMATCH` trong §5.9. Giữ `ROUTE_NOT_FOUND` cho cả Route/AlternativeRoute.
- Cập nhật `VietRide_API_Contract_v1.md`: hai PUT endpoint, role, request/response, detail-vs-list DTO, 403/404/422, và exact `error.fields` cho mismatch.
- Cập nhật canonical DDL `db-schema/trip-route-vehicle/schema.sql`: hai cột nullable `path_polyline text`.
- Cập nhật Postman contract cases. Xác minh hai Gateway prefix hiện hữu đã cover endpoint; không sửa Gateway nếu route table đã đúng.

### Task 1 — Domain + persistence

- Thêm properties/domain methods, EF configuration và một migration `AddRoutePathPolyline` có `Down()` đảo ngược được.

### Task 2 — Geometry primitives

- Implement decoder precision-5 và point-to-polyline distance không thêm dependency; unit-test toàn bộ malformed/limit vectors.

### Task 3 — Route geometry use case

- Command/handler/validator, tenant ownership, stop + origin/destination station validation, detail response và tests.

### Task 4 — AlternativeRoute geometry use case

- Command/handler/validator, parent Route origin + alternative destination/stops validation, `ROUTE_NOT_FOUND` convention và tests.

### Task 5 — Safe invalidation

- Clear geometry trong Add/RemoveRouteStop và UpdateAlternativeRoute theo BR-5, cùng UnitOfWork transaction; tests cho clear/preserve.

### Task 6 — Tracking projection/fallback

- Refactor handler theo BR-6, giữ nguyên internal response contract và Tracking code; integration tests polyline/fallback/not-found.

### Task 7 — Verification

- `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes`
- `dotnet build apps/trip/VietRide.Trip.sln -c Release`
- `dotnet test apps/trip/VietRide.Trip.sln`
- Verify migration SQL/rollback, Postman cases và Gateway forwarding qua port 3000.

---

## 6. LƯU Ý — các điểm cần chú ý / việc ngoài phạm vi code đợt này

- **L-1 — AlternativeRoute polyline chưa có tác dụng với Tracking.** Lưu sẵn theo yêu cầu, nhưng off-route detection chỉ dùng polyline của Route chính cho tới khi có tính năng gán trip chạy theo AlternativeRoute (cần thêm field kiểu `Trip.ActiveAlternativeRouteId` + flow điều hành — NGOÀI phạm vi spec này).
- **L-2 — FE có trách nhiệm:** (a) gọi Directions API và cho nhà xe duyệt đường; (b) gửi lại polyline **sau mỗi lần sửa stops** vì BE tự xóa (BR-5) — nên hiển thị cảnh báo "tuyến chưa có đường đi thật" khi `pathPolyline == null`; (c) xử lý 422 `ROUTE_GEOMETRY_STOP_MISMATCH`, parse `stopIds`/`stationIds` và highlight điểm bị lệch.
- **L-3 — Polyline áp dụng ngay cho trip đang chạy:** endpoint tracking đọc polyline hiện tại của Route (không snapshot theo trip). Nhà xe đổi polyline giữa chừng → trip đang chạy dùng đường mới sau khi cache Tracking hết TTL (`TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS`). Chấp nhận được cho off-route detection; ghi ra để không bất ngờ.
- **L-4 — Độ trễ cache Tracking:** mọi thay đổi geometry (set/clear) chỉ có hiệu lực với Tracking sau khi cache TTL hết hạn — không có cơ chế invalidate chủ động. Chấp nhận trong phạm vi này.
- **L-5 — Hằng số 500 m và công thức khoảng cách bị lặp ở 2 service** (Tracking TS `OFF_ROUTE_DISTANCE_THRESHOLD_METERS` ↔ Trip C# ngưỡng validate khớp stop). Đổi bên này phải đổi bên kia thủ công — ghi comment tham chiếu chéo ở cả hai chỗ.
- **L-6 — Precision encoding:** BE giả định precision 5 (Google/Goong). Nếu FE dùng Mapbox/OSRM (mặc định precision 6), tọa độ decode sẽ sai lệch 10× → validation khớp stop sẽ chặn được đa số case, nhưng FE phải chốt dùng encoder precision 5.
- **L-7 — SOT/hạ tầng:** đã nâng thành Task 0 bắt buộc, không còn là cleanup sau code. Hai Gateway prefix `/v1/operator/routes` và `/v1/operator/alternative-routes` hiện đã tồn tại; implementation chỉ cần regression verify forwarding/role, không thêm route mới.
- **L-8 — Không dùng PostGIS:** tính toán khoảng cách bằng app code (đồng bộ với cách Tracking làm). Nếu sau này cần query không gian (tìm tuyến gần điểm X) mới cân nhắc PostGIS.
- **L-9 — Kích thước/CPU:** tuyến dài decode ra tối đa 10 000 điểm; Tracking quét tuyến tính mọi segment mỗi GPS update. Không khẳng định chi phí micro-giây khi chưa benchmark. Giới hạn 10 000 là safety cap v1; theo dõi latency/CPU thực tế và chỉ bổ sung simplify (Douglas-Peucker) hoặc hạ cap nếu profiling cho thấy cần thiết.
