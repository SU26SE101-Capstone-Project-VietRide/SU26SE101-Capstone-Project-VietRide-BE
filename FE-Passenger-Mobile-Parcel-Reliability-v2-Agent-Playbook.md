# Passenger Mobile — Parcel Reliability v2 FE Agent Playbook

> Tài liệu thực thi dành cho dev/AI agent phụ trách Passenger Mobile. Đây là đặc tả luồng UI và thứ tự gọi API; không được suy diễn thêm nút hoặc state machine từ tên endpoint.

## 1. Phạm vi và nguyên tắc bắt buộc

- Base URL production: `https://api.vietride.online`.
- Các Parcel account API dùng `Authorization: Bearer <accessToken>`. Ba endpoint delivery-token `/v1/parcels/delivery/*` là `[AllowAnonymous]` trong code và xác thực bằng token trong body, không phụ thuộc JWT.
- Mọi mutation có `Idempotency-Key` phải nhận một UUID mới cho một ý định mới. Retry cùng payload phải dùng lại UUID cũ.
- Đọc dữ liệu trong `response.data`; lỗi đọc từ `response.error.code`, `response.error.message`, `response.error.fields`; luôn lưu `response.meta.traceId` khi báo lỗi BE.
- Chỉ render CTA reliability/incident/claim có trong `availableActions`. Không tự suy ra các CTA này từ `status`. Thanh toán là business flow riêng và dùng các field tiền/trạng thái do BE trả.
- Passenger không được nhìn thấy actor nội bộ, search evidence nội bộ hoặc claim của recipient.
- Tracking hiển thị `currentCustody.lastConfirmedLocation` và `lastConfirmedAt` dưới nhãn “Vị trí xác nhận gần nhất”. Không đổi thành “vị trí GPS hiện tại”.
- Không tạo UI cho `custody-scan`, `custody-exception`, `load`, `unload`, reconciliation hoặc forwarding. Đây là API vận hành của crew/operator.

Response chung:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": { "traceId": "..." }
}
```

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "ERROR_CODE",
    "message": "...",
    "fields": null
  },
  "meta": { "traceId": "..." }
}
```

## 2. Các contract FE cũ phải sửa

| Contract mới | Việc phải sửa |
|---|---|
| `POST /v1/parcels` trả thêm `bookingId` | Lưu trực tiếp association; không đoán parcel-only từ màn hình trước đó. |
| `GET /v1/parcels/{parcelId}` trả `bookingId`, `operator`, `trip`, `dropoffLocation`, `compensationPolicySnapshot`, `reliabilitySummary`, `availableActions` | Dùng làm screen model chi tiết, không gọi riêng Trip/Operator để ghép. |
| `GET /v1/parcels/{parcelId}/trace` là screen-ready tracking | Không gọi detail rồi incidents rồi claims để dựng một màn tracking. |
| `searchDeadline` có thể `null` | `null` nghĩa là incident chưa bắt đầu SLA tìm kiếm, thường đang chờ duyệt; không hiện countdown giả. |
| Passenger chỉ report `DELIVERY_NOT_RECEIVED`, `DAMAGED`, `PARTIAL_LOSS` | Bỏ lựa chọn `MISSING`, `WRONG_STOP`, `UNSCANNED_HANDOFF`, `MISSING_AFTER_DEPARTURE`, `PACKAGE_IDENTITY_MISMATCH`. |
| Claim/evidence/appeal mutation trả claim đã cập nhật | Cập nhật local state từ response, không bắt buộc refetch. |
| `claimSummary` chỉ có với sender | Recipient phải chấp nhận field `null`, không coi là lỗi dữ liệu. |

## 3. Các API Passenger được dùng

| Màn hình/hành động | Method và path | Ghi chú |
|---|---|---|
| Tìm chuyến gửi hàng | `GET /v1/parcels/available-trips` | Trả trip và quote dùng khi tạo Parcel. |
| Voucher khả dụng | `GET /v1/parcels/vouchers/available` | Gọi sau khi đã chọn trip/size. |
| Tạo Parcel | `POST /v1/parcels` | Có `Idempotency-Key`. |
| Thanh toán cọc | `POST /v1/parcels/{parcelId}/deposit-payment` | Có `Idempotency-Key`. |
| Thanh toán phần còn lại | `POST /v1/parcels/{parcelId}/final-payment` | Chỉ khi BE cho phép. |
| Danh sách đã gửi | `GET /v1/parcels/sent` | Có reliability summary theo item. |
| Danh sách được nhận | `GET /v1/parcels/received` | Recipient view. |
| Chi tiết Parcel | `GET /v1/parcels/{parcelId}` | Screen-ready detail. |
| Tracking | `GET /v1/parcels/{parcelId}/trace` | API chính của màn tracking. |
| Báo sự cố Passenger | `POST /v1/parcels/{parcelId}/incidents` | Chỉ ba loại được phép. |
| Danh sách incident | `GET /v1/parcels/{parcelId}/incidents` | Tracking đã có `incidents[]`; chỉ gọi riêng nếu màn độc lập. |
| Danh sách claim | `GET /v1/parcels/{parcelId}/claims` | Sender/operator có quyền tương ứng. |
| Nộp claim | `POST /v1/parcels/{parcelId}/claims` | Body rỗng, sender-only. |
| Thêm evidence | `POST /v1/parcels/{parcelId}/claims/{claimId}/evidence` | Sender-only. |
| Appeal | `POST /v1/parcels/{parcelId}/claims/{claimId}/appeal` | Chỉ khi `APPEAL` có trong actions. |
| Recipient xác nhận giao | `POST /v1/parcels/delivery/confirm` | Dùng delivery token. |
| Recipient từ chối | `POST /v1/parcels/delivery/reject` | Dùng token và lý do. |
| Hoàn tác từ chối | `POST /v1/parcels/delivery/undo-reject` | Dùng token. |

## 4. Luồng tạo và thanh toán happy case

### Bước 1 — tìm chuyến

```http
GET /v1/parcels/available-trips?originStationId={uuid}&destinationStationId={uuid}&departureDate=2026-09-01&lengthCm=40&widthCm=30&heightCm=20&estimatedWeightKg=5&sizeCategory=MEDIUM&page=1&pageSize=20
```

Validation đáng chú ý: các UUID không rỗng; kích thước và cân nặng lớn hơn `0`; `sizeCategory` nếu có phải là enum hợp lệ; `page >= 1`, `pageSize` từ `1` đến `100`.

Chỉ cho chọn trip được BE trả về. Trip không có Assistant hoặc không đủ cargo không được dùng để tạo Parcel.

### Bước 2 — tạo Parcel

```http
POST /v1/parcels
Authorization: Bearer <passenger-token>
Idempotency-Key: <uuid>
Content-Type: application/json
```

```json
{
  "tripId": "uuid",
  "dropoffStopId": "uuid-or-null",
  "bookingId": "uuid-or-null",
  "itemName": "Laptop",
  "description": "Hộp carton màu nâu",
  "sizeCategory": "MEDIUM",
  "lengthCm": 40,
  "widthCm": 30,
  "heightCm": 20,
  "estimatedWeightKg": 5,
  "photoUrl": "https://owned-firebase-image-url-or-null",
  "recipient": {
    "fullName": "Nguyễn Văn B",
    "phoneNumber": "+84901234567",
    "email": "recipient@example.com"
  },
  "deliveryMethod": "TERMINAL_PICKUP",
  "paymentMethod": "WALLET",
  "voucherCode": null,
  "quoteToken": "token-from-available-trips",
  "declaredValueVnd": 12000000,
  "quantity": 1
}
```

Ràng buộc từ code:

- `recipient.fullName` bắt buộc, tối đa 255 ký tự.
- `recipient.phoneNumber` bắt buộc, tối đa 20 ký tự.
- `recipient.email` optional, tối đa 255 và phải là email hợp lệ nếu có.
- `description` tối đa 2000; `quoteToken` tối đa 16384.
- `estimatedWeightKg`, `lengthCm`, `widthCm`, `heightCm` phải lớn hơn `0`.
- `declaredValueVnd` nếu có phải lớn hơn hoặc bằng `0`; `quantity` từ `1` đến `10000`.
- `deliveryMethod` hiện chỉ nhận `TERMINAL_PICKUP`.
- `paymentMethod` chỉ nhận `WALLET` hoặc `VNPAY`.
- `photoUrl` nếu có phải là Firebase image URL thuộc user theo validator BE.

Response cần lưu: `parcelId`, nullable `bookingId`, `parcelCode`, `status`, toàn bộ giá dự kiến và `compensationPolicy` snapshot. Không lấy policy hiện tại của operator để ghi đè snapshot này.

### Bước 3 — thanh toán cọc

```json
{
  "paymentMethod": "WALLET",
  "paymentReturnMode": null
}
```

Gọi `POST /v1/parcels/{parcelId}/deposit-payment`. Với `VNPAY`, điều hướng theo response payment; sau callback/web return phải đọc lại Parcel detail cho trạng thái nguồn sự thật. Không tự đánh dấu đã thanh toán chỉ vì WebView đóng.

### Bước 4 — sau khi crew cân lại

Đọc `GET /v1/parcels/{parcelId}`. `availableActions` hiện là reliability actions, không chứa payment action. Chỉ hiện CTA thanh toán tiếp theo business status hiện tại và khi `balanceRequiredVnd > balancePaidVnd`; nếu endpoint trả conflict thì tải lại detail và không tự ép trạng thái. Gọi:

```json
{
  "paymentMethod": "WALLET",
  "paymentReturnMode": null
}
```

tới `POST /v1/parcels/{parcelId}/final-payment`.

Không cho FE tự tính size cuối, cước cuối hoặc số tiền còn lại từ kích thước. Dùng `actualSizeCategory`, `finalTotalPriceVnd`, `balanceRequiredVnd`, `balancePaidVnd` do BE trả.

## 5. Màn danh sách và chi tiết

- `GET /v1/parcels/sent?status=&from=&to=&page=1&pageSize=20` dành cho sender.
- `GET /v1/parcels/received?page=1&pageSize=20` dành cho recipient đã liên kết.
- `from`/`to` của sent phải là RFC3339 nếu có; `pageSize` tối đa 100.
- Mỗi card dùng reliability data trong item. Không gọi trace cho từng row.
- Khi mở detail, gọi đúng một `GET /v1/parcels/{parcelId}`.
- `bookingId == null` nghĩa là parcel-only; có UUID nghĩa là Parcel gắn Booking.

## 6. Màn tracking — đúng một request chính

```http
GET /v1/parcels/{parcelId}/trace?limit=50
```

Các field phải dùng:

- `parcelSummary`: card cơ bản.
- `operator`, `trip`, `dropoffLocation`: tên hiển thị đã enrich.
- `currentCustody`: vị trí xác nhận gần nhất; `trackingConfidence`; `hasTrackingGap` ở summary tương ứng.
- `activeIncident`: incident đang hoạt động; `searchDeadline` có thể null.
- `forwardingTrip`: chỉ có khi hàng được chuyển chuyến.
- `claimSummary`: chỉ sender; recipient nhận null.
- `availableActions`: nguồn sự thật CTA.
- `timeline.items`: 50 custody event mới nhất; `timeline.nextCursor` để tải trang cũ.
- `incidents`: lịch sử incident đã được lọc cho Passenger.
- `nextUpdateAt`: mốc cập nhật tiếp theo; không tự tạo countdown nếu null.

Không hiển thị `actorId`, ghi chú nội bộ hay evidence điều tra ngay cả khi client model cũ từng có các field đó.

## 7. Báo sự cố Passenger

Chỉ render nút “Báo sự cố” khi `REPORT_INCIDENT` có trong `availableActions`.

```json
{
  "incidentType": "DELIVERY_NOT_RECEIVED",
  "description": "Ứng dụng báo đã giao nhưng tôi chưa nhận được kiện",
  "evidenceUrls": []
}
```

Giá trị hợp lệ cho Passenger UI:

- `DELIVERY_NOT_RECEIVED`
- `DAMAGED`
- `PARTIAL_LOSS`

Không gửi các incident vận hành. BE trả `422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE` nếu client cũ vẫn gửi `MISSING` hoặc `WRONG_STOP`.

Sau HTTP `201`, thay `activeIncident`, `availableActions`, `nextUpdateAt` từ response. Không tạo local incident giả. Nếu `searchDeadline == null`, hiển thị “Chờ nhà xe xác minh”, không hiển thị “còn 72 giờ”.

## 8. Claim và bồi thường

### Điều kiện UI

- Chỉ sender thấy claim.
- Chỉ hiện “Yêu cầu bồi thường” khi có `SUBMIT_CLAIM`.
- Chỉ hiện “Bổ sung chứng từ” khi có `ADD_EVIDENCE`.
- Chỉ hiện “Khiếu nại quyết định” khi có `APPEAL`.
- Recipient không được tự đổi beneficiary.

### Nộp claim

```http
POST /v1/parcels/{parcelId}/claims
Authorization: Bearer <sender-token>
Idempotency-Key: <uuid>
```

Body rỗng. BE chỉ cho nộp khi incident đạt điều kiện, ví dụ `LOST_CONFIRMED`; FE không tự chuyển mất hàng sau 72 giờ.

### Thêm evidence

```json
{
  "evidenceType": "INVOICE",
  "reference": "https://owned-evidence-reference",
  "note": "Hóa đơn mua hàng"
}
```

Response HTTP `201` là toàn bộ claim đã cập nhật, gồm `evidence[]`, policy snapshot, award/deadline/actions.

### Appeal

```json
{
  "reason": "Giá trị thiệt hại được duyệt chưa khớp hóa đơn"
}
```

Gọi `POST /v1/parcels/{parcelId}/claims/{claimId}/appeal` với idempotency UUID. Không cho appeal nếu `APPEAL` không có trong actions.

### Cách hiển thị công thức

FE chỉ trình bày dữ liệu BE đã snapshot:

```text
assessedLoss = min(provenDirectLossVnd, declaredValueVnd nếu có)
cargoAwardVnd = min(assessedLoss × compensationRatePercent / 100, policyCapVnd)
totalAwardVnd = cargoAwardVnd + freightRefundVnd
```

Không có chứng từ dùng `noProofFallbackMultiplier × parcelFreight`, vẫn chịu cap. FE không tự quyết định có/không chứng từ và không tự tính payout để ghi đè `totalAwardVnd`.

## 9. Delivery recipient

- Crew chuyển Parcel sang trạng thái chờ xác nhận bằng API crew; Passenger không gọi API crew.
- Recipient dùng token nhận qua kênh giao hàng để gọi `POST /v1/parcels/delivery/confirm` với `{ "token": "uuid" }` và `Idempotency-Key`.
- Từ chối: `{ "token": "uuid", "rejectionReason": "..." }` tới `/reject`, có `Idempotency-Key`.
- Hoàn tác: `{ "token": "uuid" }` tới `/undo-reject`, có `Idempotency-Key`.
- Token hết hạn/sai: hiển thị lỗi BE; không cho nhập `parcelId` thay token và không fallback sang endpoint operator.

## 10. Quy tắc UI chống sai luồng

| Dữ liệu BE | UI đúng | UI cấm |
|---|---|---|
| `currentCustody == null` | “Chưa có vị trí xác nhận” | Gán vị trí xe từ GPS. |
| `trackingConfidence = MANUAL_EXCEPTION` | Nhãn “Đang xác minh” | Hiển thị như scan chắc chắn. |
| `searchDeadline == null` | “Chờ duyệt/xác minh” | Countdown 72 giờ. |
| `forwardingTrip != null` | Hiển thị chuyến chuyển tiếp/ETA mới | Sửa lịch sử trip cũ. |
| Không có `SUBMIT_CLAIM` | Ẩn/disable CTA và giải thích trạng thái | Vẫn gửi API vì đã đủ 72 giờ trên client. |
| Recipient view | Tracking + report incident hợp lệ | Claim, evidence nội bộ, beneficiary controls. |

## 11. Error handling tối thiểu

- `401`: access token hết hạn/không hợp lệ; dùng auth refresh flow chung, retry tối đa một lần.
- `403`: sai role/không sở hữu Parcel; không retry vòng lặp.
- `404`: không tìm thấy hoặc tenant/access bị che; quay về danh sách.
- `409`: state thay đổi hoặc mutation không hợp lệ; giữ `traceId`, tải lại đúng screen model một lần.
- `422`: body/query/incident type sai; map `error.fields` vào form.
- `503`: upstream tạm lỗi; cho retry có kiểm soát. Với mutation phải giữ nguyên `Idempotency-Key`.

## 12. Checklist giao cho AI agent Passenger

- [ ] Xóa mọi client-side state transition Parcel/Incident/Claim.
- [ ] Dùng `availableActions` cho toàn bộ CTA reliability.
- [ ] Tracking chỉ một request chính tới `/trace`.
- [ ] Không tạo bất kỳ nút custody/crew/operator nào.
- [ ] Chấp nhận nullable `bookingId`, `searchDeadline`, `claimSummary`, `forwardingTrip`, `currentCustody`.
- [ ] Passenger incident enum chỉ còn ba loại được phép.
- [ ] Payment lấy số tiền từ BE, không tự tính theo cân/kích thước.
- [ ] Retry mutation giữ idempotency UUID cũ; ý định mới sinh UUID mới.
- [ ] Log `meta.traceId` trong crash/error report.
- [ ] Test sender và recipient bằng hai JWT khác nhau.
