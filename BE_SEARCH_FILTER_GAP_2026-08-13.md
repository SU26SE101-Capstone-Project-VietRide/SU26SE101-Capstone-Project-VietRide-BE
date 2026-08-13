# GAP: search & filter mà FE cần nhưng BE chưa đáp ứng

> Ngày rà soát: 2026-08-13
> Phạm vi: toàn bộ màn Admin (SYSTEM_ADMIN) và Manager (OPERATOR_ADMIN / OPERATOR_STAFF)
> có bảng dữ liệu kèm ô tìm kiếm hoặc bộ lọc, trong `vietride/src/pages`.
> File này **chỉ để yêu cầu BE bổ sung**. Không có thay đổi nào trong source BE.
>
> Thay thế `docs/BE_SEARCH_FILTER_AUDIT.md` (bản cũ chỉ soát một phần).

---

## Tóm tắt cho người quyết định

Rà 27 màn có bảng. Kết quả:

| Nhóm | Số màn | Trạng thái |
|---|---|---|
| A — FE gọi server-side đúng, BE đủ tham số | 10 | Không cần làm gì |
| B — BE đã có tham số, FE chưa gắn | 2 | **FE đã sửa xong** trong lần này |
| C — BE thiếu tham số | 9 | **Cần BE bổ sung — nội dung file này** |
| D — lọc client hợp lệ theo thiết kế | 6 | Không cần làm gì |

Trong nhóm C có **4 lỗi đang hiện hữu trên production UI**: người dùng bấm/gõ vào
bộ lọc, UI phản hồi bình thường, nhưng BE bỏ qua tham số nên **danh sách không hề
được lọc**. Không có thông báo lỗi nào. Đây là các mục P0 bên dưới.

---

## P0 — Bộ lọc đang chết im lặng

FE gửi tham số, BE nhận request 200 OK rồi bỏ qua tham số đó. Người dùng tin là đã
lọc nhưng thực chất đang nhìn danh sách chưa lọc.

### P0-1. `GET /v1/operator/driver-schedules` — thiếu `search`, `vehicleTypeId`; `isOneTime` không thuộc domain

Màn: Lịch chạy (`src/pages/Manager/Trips/index.tsx`)

FE đang gửi:

```
page, pageSize, search, isActive, vehicleTypeId
```

FE từng gửi `isOneTime=true` cho ô thống kê, nhưng query này phải được loại bỏ vì lịch v1 luôn lặp theo tuần.

BE nhận (`OperatorDriverSchedulesController.GetAsync`):

```
page, pageSize, routeId, driverUserId, isActive
```

`ListDriverSchedulesHandler` chỉ `Where` theo `RouteId`, `DriverUserId`, `IsActive`.

Hệ quả:

- Ô tìm kiếm lịch chạy **không lọc gì**.
- Dropdown lọc theo loại xe **không lọc gì**.
- Thẻ thống kê "lịch chạy một lần" đang hiển thị **tổng số lịch chạy**, không phải
  số lịch một lần — con số sai, không phải chỉ thiếu.

Đề nghị:

| Tham số | Kiểu | Ngữ nghĩa |
|---|---|---|
| `search` | `string?` | Khớp `ILIKE %...%`, nên `unaccent`. Trường gợi ý: tên tuyến, biển số xe, tên tài xế/phụ xe |
| `vehicleTypeId` | `Guid?` | Lọc theo loại xe của xe được gán cho lịch |
| `isOneTime` | Không hỗ trợ | FE phải bỏ filter/card này; strict query validation trả `422 VALIDATION_ERROR` |

Không bổ sung `isOneTime`; số liệu/card này không có ý nghĩa trong domain lịch lặp hằng tuần.

### P0-2. `GET /v1/operator/vehicles` — thiếu `status`, `vehicleTypeId`

Màn: Phương tiện & ghế (`src/pages/Manager/Vehicles/index.tsx`)

FE đang gửi `page, pageSize, search, searchIn, status, vehicleTypeId`.
BE nhận `page, pageSize, search, searchIn, sortBy, sortDir`.

Hệ quả: hai dropdown "Trạng thái" và "Loại xe" trên màn phương tiện **không lọc gì**.
FE còn ghi các giá trị này vào query string của URL nên link chia sẻ cũng sai theo.

Đề nghị:

| Tham số | Kiểu | Ngữ nghĩa |
|---|---|---|
| `status` | `string?` | `ACTIVE` / `MAINTENANCE` / `OFF_DUTY` / `RETIRED` |
| `vehicleTypeId` | `Guid?` | Lọc theo loại xe |

### P0-3. `GET /v1/operator/routes` — thiếu `status` (hoặc `isActive`)

Màn: Quản lý tuyến (`src/pages/Manager/RouteManagement/index.tsx`)

FE đang gửi `page, pageSize, search, status`. BE chỉ nhận `page, pageSize, search`.

Hệ quả: dropdown "Tất cả / Hoạt động / Ngưng" trên màn quản lý tuyến **không lọc gì**.

Đề nghị: thêm `isActive: bool?` (đồng bộ với `/v1/admin/stations` và
`/v1/admin/locations` đang dùng `isActive`). FE sẽ đổi sang gửi `isActive`.
`isActive` là cờ riêng; không ánh xạ `false` thành status `INACTIVE`.

### P0-4. `GET /v1/admin/stations` — `sortBy` / `sortDir` bị bỏ qua

Màn: Bến xe hệ thống (`src/pages/Admin/Stations/index.tsx`)

FE gửi `sortBy=updatedAt&sortDir=desc`. `ListAdminStationsHandler` luôn
`OrderBy(x => x.Name)` bất kể tham số.

Hệ quả nhẹ hơn ba mục trên (chỉ sai thứ tự, không sai tập dữ liệu) nhưng admin đang
mong bến vừa sửa nằm đầu danh sách thì không thấy.

Đề nghị: hoặc implement `sortBy`/`sortDir` (`name`, `createdAt`, `updatedAt`),
hoặc bỏ hẳn khỏi contract để FE ngừng gửi. Nói rõ chọn hướng nào.

---

## P1 — Thiếu tham số khiến FE phải tải toàn bộ bảng

Các màn này **hoạt động đúng** nhưng phải tải hết toàn bộ dữ liệu về trình duyệt rồi
lọc bằng JavaScript, vì BE thiếu một phần bộ lọc mà màn cần. FE không thể chuyển
sang server-side một nửa: nếu gắn `search` lên server mà giữ các filter còn lại ở
client thì filter client chỉ còn tác dụng trên đúng trang đang xem — sai hơn hiện tại.

Vì vậy **mỗi mục dưới đây phải bổ sung trọn bộ tham số thì FE mới chuyển được.**

### P1-1. `GET /v1/admin/locations` — thiếu `type`, `parentCode`

Màn: Khu vực hành chính (`src/pages/Admin/Locations/index.tsx`)

Đây là mục nặng nhất về hiệu năng. Danh mục ~3.400 bản ghi, BE cap `pageSize` = 100,
nên **mỗi lần mở màn FE bắn 34 request** (`fetchAllAdminLocations`, chạy song song
theo lô 8) chỉ để dựng được bộ lọc.

BE hiện có: `page, pageSize, search, isActive`
Màn cần thêm: `type`, `parentCode`

Đáng chú ý: endpoint public `GET /v1/locations` **đã có sẵn** `parentCode`, `search`,
`type`. Chỉ cần bê logic đó sang endpoint admin.

| Tham số | Kiểu | Ngữ nghĩa |
|---|---|---|
| `type` | `string?` | `PROVINCE` / `WARD` (giống enum của `/v1/locations`) |
| `parentCode` | `string?` | Lọc con trực tiếp của một tỉnh/thành |

Xong hai tham số này FE sẽ bỏ `fetchAllAdminLocations`, còn đúng 1 request/trang.

### P1-2. `GET /v1/admin/stations` — thiếu `supportsShuttle`; `search` chưa phủ `address_street`

Màn: Bến xe hệ thống (`src/pages/Admin/Stations/index.tsx`)

BE hiện có `search` (unaccent trên `name`, `city`, `ward`) và `isActive` — tốt.
Nhưng màn còn một dropdown thứ ba là "Loại xe: Trung chuyển / Không trung chuyển",
lọc theo `supportsShuttle`, mà BE không có.

Ngoài ra ô tìm kiếm phía FE đang khớp cả `slug` và `addressStreet`; BE chỉ khớp
`name`, `city`, `ward`. Chuyển sang server-side mà không bổ sung thì mất khả năng
tìm theo địa chỉ đường.

| Tham số | Kiểu | Ngữ nghĩa |
|---|---|---|
| `supportsShuttle` | `bool?` | Lọc bến có/không hỗ trợ trung chuyển |
| (mở rộng `search`) | — | Thêm `address_street` và `slug` vào mệnh đề `unaccent ILIKE` |

Thêm một điểm chặn nữa: màn có 4 thẻ thống kê (tổng / đang hoạt động / ngưng /
trung chuyển) đếm trên **toàn bộ** bến, và panel gộp bến cần danh sách đầy đủ để
chọn bến đích. Nếu muốn FE bỏ hẳn tải-toàn-bộ thì cần thêm:

```
GET /v1/admin/stations/summary
-> { total, active, inactive, supportsShuttle }
```

Không có endpoint summary thì FE vẫn phải tải hết để đếm, dù đã có đủ filter.

### P1-3. `GET /v1/admin/vouchers` — thiếu `search`, `service`

Màn: Voucher hệ thống (`src/pages/Admin/Vouchers/index.tsx`)

`ListVouchersRequest` hiện có: `fundingType`, `isActive`, `page`, `pageSize`,
`sortBy`, `sortDir`.

Màn cần thêm:

| Tham số | Kiểu | Ngữ nghĩa |
|---|---|---|
| `search` | `string?` | Khớp `code` hoặc `name` |
| `service` | `string?` | `BOOKING` / `PARCEL` — màn có tab tách voucher vé và voucher hàng |

### P1-4. `GET /v1/operator/vouchers` — thiếu `search`, `service`

Màn: Voucher nhà xe (`src/pages/Manager/Vouchers/index.tsx`)

`ListOperatorVouchersRequest` hiện chỉ có `isActive`, `page`, `pageSize`, `sortBy`,
`sortDir`. Cần đúng `search` + `service` như P1-3 để hai màn đồng bộ contract.

### P1-5. `GET /v1/admin/trip-settlements` — thiếu `search`

Màn: Ví & đối soát nền tảng (`src/pages/Admin/WalletSettlement/index.tsx`)

Đây là trường hợp tệ nhất trong nhóm: màn **đã phân trang server-side** nhưng ô tìm
kiếm lại lọc trên mảng `records` của **đúng trang đang xem**. Gõ mã chuyến nằm ở
trang 3 trong khi đang ở trang 1 thì bảng trả về rỗng, trông như không có dữ liệu.

BE hiện có: `page, pageSize, operatorId, status, tripId, stuckOnly, severity, from, to, sortBy, sortDir`

Cần thêm `search: string?`, khớp trên: `tripCode`, `operatorName`, `settlementId`,
`activeFailureCode` (đúng các trường FE đang lọc ở client).

Endpoint tương ứng bên operator là `GET /v1/operator/trip-settlements` **đã có sẵn
`search` và `dateField`**. Bê nguyên logic đó sang bản admin là xong.

### P1-6. `GET /v1/admin/platform-wallet/transactions` — thiếu `search`

Cùng màn, cùng bệnh với P1-5: phân trang server nhưng tìm kiếm client trên một trang.

BE hiện có: `page, pageSize, type, referenceType, from, to, sortBy, sortDir`

Cần thêm `search: string?` khớp `transactionId`, `referenceType`, `note`,
`actor.displayName`.

Bản operator `GET /v1/operator/wallet/transactions` đã có `search` + `dateField`.

### P1-7. `GET /v1/operator/parcel-route-fares` — thiếu `search`

Màn: Hàng hoá → bảng giá theo tuyến (`src/pages/Manager/Parcels/index.tsx`)

BE hiện có `routeId`, `sizeCategory`, `page`, `pageSize`. FE tải toàn bộ bảng giá rồi
lọc client theo tên tuyến. Cần `search: string?` khớp tên tuyến / tên bến.

Mức độ ưu tiên thấp nhất trong nhóm P1 vì số bản ghi nhỏ.

---

## P2 — Đề xuất mở rộng, không phải lỗi

### P2-1. `GET /v1/operator/bookings` — không tìm được theo tên hành khách

Màn: Đặt vé (`src/pages/Manager/Bookings/index.tsx`)

`ListOperatorBookingsRequest` có `PassengerPhone` và `BookingCode` nhưng không có ô
tìm kiếm tổng quát. FE phải đoán ý người dùng bằng regex: chuỗi ≥ 7 chữ số thì coi là
số điện thoại, còn lại coi là mã vé (`isPhoneSearch`).

Hệ quả: nhân viên gõ **tên hành khách** thì không ra kết quả nào, mà cũng không có
thông báo giải thích tại sao.

Đề nghị thêm `search: string?` khớp `passengerName`, `passengerPhone`, `bookingCode`
— FE sẽ bỏ heuristic đoán mò.

### P2-2. `/v1/admin/policies` đã có `search`, `category`, `active` nhưng FE chưa dùng

Không phải gap của BE — ghi ở đây để khỏi quên. `ListPoliciesQuerySchema` đã hỗ trợ
`policyType`, `category`, `active`, `search`, `sortBy`, `sortDir`; màn
`src/pages/Admin/Policies.tsx` mới chỉ gửi `policyType`. Khi nào cần ô tìm kiếm cho
màn chính sách thì FE gắn được ngay, BE không phải làm gì.

Lưu ý cho FE khi gắn: schema này khai `.strict()`, gửi key lạ sẽ bị **400** chứ không
bị bỏ qua âm thầm như các endpoint khác.

---

## Ghi chú về đặt tên tham số

Contract hiện không nhất quán, FE phải nhớ từng ngoại lệ. Đề nghị chuẩn hoá dần:

| Ngữ nghĩa | Tên đang dùng ở các endpoint khác nhau |
|---|---|
| Từ khoá tìm kiếm | `search` (hầu hết) · **`q`** (`/v1/rag/documents`) |
| Lọc bật/tắt | `isActive` (trip, locations, stations) · `active` (rag policies) · `status` (identity, booking) |

Riêng `/v1/rag/documents` dùng `q`: FE vừa xử lý bằng cách đổi tên ngay tại tầng API
(`getRagDocuments` trong `src/api/vietride.ts`) nên không cần BE đổi. Nhưng nếu BE
định chuẩn hoá thì báo trước để FE gỡ đoạn map đó.

Điểm nguy hiểm chung: **các endpoint .NET bind `[FromQuery]` sẽ bỏ qua tham số lạ mà
không báo lỗi.** Đó là lý do 4 bộ lọc ở mục P0 chết im lặng suốt mà không ai phát
hiện. Nếu BE có thể trả `422` khi nhận query param không nằm trong contract thì các
lệch kiểu này sẽ lộ ra ngay từ lần gọi đầu tiên.

---

## Phụ lục — các màn không cần BE làm gì

Nhóm A (FE gọi server-side, BE đã đủ tham số):

| Màn | Endpoint | Tham số đang dùng |
|---|---|---|
| Admin → Người dùng | `/v1/admin/users` | `search`, `role`, `status`, `sortBy`, `sortDir` |
| Admin → Chính sách | `/v1/admin/policies` | `policyType` |
| Admin → Báo cáo / Tổng quan | `/v1/admin/reports/platform`, `/v1/admin/dashboard/summary` | `from`, `to` |
| Manager → Nhân sự | `/v1/operator/users` | `search`, `role`, `status` |
| Manager → Bến nhà xe | `/v1/operator/stations` | `search` |
| Manager → Đặt vé | `/v1/operator/bookings` | `bookingCode`/`passengerPhone`, `status` |
| Manager → Sự cố | `/v1/operator/incidents` | `category`, `status`, `from`, `to`, `tripId` |
| Manager → Hàng hoá (hàng chờ) | `/v1/operator/parcels` | `status`, `pendingActionType`, `tripId` |
| Manager → Ví (3 tab) | `/v1/operator/wallet/transactions`, `/trip-settlements`, `/ledger` | `search`, `dateField`, `from`, `to`, `type`, `status` |

Nhóm D (lọc ở client là đúng thiết kế, không cần đổi):

- **Trung tâm vận hành** (`Manager/Operations`) — bản đồ realtime, dữ liệu là model
  suy ra từ GPS + chuyến, không phân trang.
- **Sidebar chọn tuyến** (`Manager/Routes/RouteListSidebar`) — picker trên tập tuyến
  của chính nhà xe, đã cache session cho workspace bản đồ.
- Các ô chọn bến/điểm dừng trong luồng tạo tuyến — picker, không phải bảng dữ liệu.
- **Điều phối xe trung chuyển** (`Manager/Dispatch`) — chỉ có phân trang, không có
  ô tìm kiếm.
- **Gói dịch vụ** Admin/Manager — danh sách ngắn cố định, không có UI lọc.

---

## Phản hồi và contract chốt từ BE — 2026-08-13

Trạng thái: **ĐÃ IMPLEMENT PHÍA BE**. FE không nối nhầm service/prefix, nhưng trước thay đổi này đã
gửi một số query ngoài API Contract.

- DriverSchedule: hỗ trợ `search`, `vehicleTypeId`; **không có `isOneTime`** vì lịch v1 luôn lặp
  theo tuần. FE phải bỏ card/query `isOneTime`.
- Vehicle: hỗ trợ riêng `status=ACTIVE|MAINTENANCE|OFF_DUTY|RETIRED`, `vehicleTypeId`, `isActive`.
  Không tồn tại status `INACTIVE`.
- Route dùng `isActive`, không dùng `status`.
- Admin Location hỗ trợ toàn bộ type `PROVINCE|MUNICIPALITY|WARD|COMMUNE|SPECIAL_ZONE` và
  `parentCode`.
- Admin Station hỗ trợ `supportsShuttle`, extended search, sort whitelist và endpoint `/summary`.
- Voucher Admin/Operator hỗ trợ `search` và `service=BOOKING|PARCEL`.
- Operator Booking `search` tìm booking code, buyer display name và buyer phone snapshot; không
  thêm passenger-name PII.
- Admin settlement/platform transaction hỗ trợ server-side `search` trước paging.
- Parcel fare tìm Route/Station qua Internal Trip API; không denormalize tên vào Parcel DB.
- RAG Policy BE đã hỗ trợ sẵn `search`, `category`, `active`; FE chỉ cần bind đúng query.
- Các GET endpoint được sửa dùng strict query allow-list. Key lạ trả `422 VALIDATION_ERROR` với
  `error.fields`; vì vậy FE phải gỡ các key cũ thay vì tiếp tục dựa vào việc BE bỏ qua âm thầm.
