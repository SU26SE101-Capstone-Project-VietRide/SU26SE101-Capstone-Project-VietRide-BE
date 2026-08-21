# Parcel API — Passenger Mobile

> Tài liệu sinh từ source code tại ngày 2026-08-21. Đối tượng sử dụng: FE/AI agent phụ trách Passenger Mobile. Tên field, method, path, validation và error code trong tài liệu giữ nguyên như code.

## Mục lục

- [1. Nguồn sự thật và trạng thái triển khai](#1-nguồn-sự-thật-và-trạng-thái-triển-khai)
- [2. Base URL và xác thực](#2-base-url-và-xác-thực)
- [3. Quy ước request/response](#3-quy-ước-requestresponse)
- [4. Tổng quan endpoint](#4-tổng-quan-endpoint)
- [5. Kiểu dữ liệu dùng chung](#5-kiểu-dữ-liệu-dùng-chung)
- [6. Tra cứu, tạo và thanh toán Parcel](#6-tra-cứu-tạo-và-thanh-toán-parcel)
- [7. Danh sách, chi tiết và tracking](#7-danh-sách-chi-tiết-và-tracking)
- [8. Incident, claim và evidence](#8-incident-claim-và-evidence)
- [9. Xác nhận hoặc từ chối nhận hàng bằng delivery token](#9-xác-nhận-hoặc-từ-chối-nhận-hàng-bằng-delivery-token)
- [10. Flow tích hợp cho Passenger Mobile](#10-flow-tích-hợp-cho-passenger-mobile)
- [11. Checklist cho AI agent Passenger FE](#11-checklist-cho-ai-agent-passenger-fe)
- [12. Đối chiếu source](#12-đối-chiếu-source)

## 1. Nguồn sự thật và trạng thái triển khai

Nguồn được đối chiếu:

- `apps/parcel/src/VietRide.Parcel.Api/Controllers/*.cs` và `Controllers/Requests/*.cs`.
- Command/query/validator/handler trong `apps/parcel/src/VietRide.Parcel.Application/Features`.
- Entity/enum trong `apps/parcel/src/VietRide.Parcel.Domain`.
- Gateway route/auth trong `apps/gateway/src/config/routes.ts` và `apps/gateway/src/proxy/proxy.middleware.ts`.
- Wrapper/error/idempotency trong `libs/dotnet/VietRide.Shared.Web` và `VietRide.Shared.Kernel`.
- OpenAPI local: `http://localhost:3000/api-specs/parcel`.
- Swagger production do người dùng cung cấp: `https://api.vietride.online/docs`, spec Parcel thực tế ở `https://api.vietride.online/api-specs/parcel`.

Toàn bộ controller Parcel đã được quét. Các route `/internal/v1/*`, `/health`, `/v1/parcel/health` và `/v1/ping` không đưa vào tài liệu Passenger vì chúng không phải FE contract; `/internal/v1/*` yêu cầu Internal JWT do service/Gateway phát hành, Mobile không được gọi.

Nhóm Reliability trong source local chưa có trong spec production đã đối chiếu. Riêng với Passenger, các path sau **đã có trong source/local nhưng chưa deploy production**:

```text
GET  /v1/parcels/{parcelId}/trace
POST /v1/parcels/{parcelId}/incidents
GET  /v1/parcels/{parcelId}/incidents
POST /v1/parcels/{parcelId}/claims
GET  /v1/parcels/{parcelId}/claims
POST /v1/parcels/{parcelId}/claims/{claimId}/evidence
POST /v1/parcels/{parcelId}/claims/{claimId}/appeal
```

FE gọi các path này trên production hiện có thể nhận `404 ROUTE_NOT_FOUND`. Đây là chênh lệch deployment, không phải contract local.

## 2. Base URL và xác thực

### 2.1. Base URL

| Môi trường | Base URL | Nguồn |
|---|---|---|
| Local qua Gateway | `http://localhost:3000` | `.env`: `GATEWAY_PORT=3000` |
| Production qua Gateway | `https://api.vietride.online` | URL Swagger/deployment hiện tại |
| Parcel service trực tiếp, chỉ dùng debug BE local | `http://localhost:5005` | `.env`: `PARCEL_BASE_URL`, `PARCEL_PORT` |
| Staging | ⚠️ TODO: cần xác nhận thêm | Không có public staging base URL trong config đã quét |

FE luôn gọi Gateway, không gọi port `5005` và không tự gắn `X-Internal-Auth`.

Ví dụ thiết lập biến:

```bash
BASE_URL="http://localhost:3000"
ACCESS_TOKEN="<RS256 access token>"
```

```js
const BASE_URL = 'http://localhost:3000';
const accessToken = '<RS256 access token>';
```

### 2.2. Lấy và refresh token

Access token lấy qua `POST /v1/auth/login`:

```json
{
  "email": "passenger@example.com",
  "password": "your-password"
}
```

Login yêu cầu `email` không rỗng, đúng email và `password` không rỗng. `data` trả `accessToken`, `refreshToken`, `expiresInSeconds`, `user`. Access token có issuer `vietride-identity`, audience `vietride-api`, thuật toán `RS256`; Gateway cho sai lệch clock tối đa 5 giây. TTL mặc định trong `.env` là 15 phút.

Gắn token vào mọi endpoint có auth:

```http
Authorization: Bearer <accessToken>
```

Khi nhận `401 AUTH_TOKEN_INVALID`, gọi `POST /v1/auth/refresh` một lần:

```json
{ "refreshToken": "<refreshToken>" }
```

Refresh token được rotate. FE phải ghi đè **cả access token và refresh token** bằng pair mới. Nếu refresh cũng trả 401 thì xóa session và đưa người dùng về login. Khi retry mutation sau refresh, giữ nguyên `Idempotency-Key` của logical operation cũ.

### 2.3. Gate riêng của Passenger

Gateway từ chối bằng `403 AUTH_PHONE_REQUIRED` nếu token role `PASSENGER` có claim `hasPhone` khác `true`. Parcel endpoints không nằm trong whitelist hoàn tất hồ sơ.

## 3. Quy ước request/response

### 3.1. Headers

| Header | Khi nào bắt buộc | Giá trị |
|---|---|---|
| `Authorization` | Mọi endpoint trong tài liệu trừ ba delivery-token endpoint | `Bearer <accessToken>` |
| `Content-Type` | Request có JSON body | `application/json` |
| `Idempotency-Key` | Mọi `POST`, `PUT`, `PATCH`, `DELETE` của Parcel service | UUID v4 dạng chuẩn 36 ký tự, ví dụ `4d87fe36-e3b7-45dc-9aca-c1f17a33f08d` |
| `X-Request-Id` | Optional | Correlation ID; Gateway tự tạo nếu FE không gửi |

Một `Idempotency-Key` chỉ được reuse khi method, path, caller và payload không đổi. Middleware cache response 86.400 giây. Dùng cùng key cho payload khác trả `422 IDEMPOTENCY_KEY_MISMATCH`; request cùng key còn chạy trả `409 IDEMPOTENCY_REQUEST_PENDING`.

### 3.2. Success envelope

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "f228b934-8cff-4b91-9619-55ac395f29d6",
    "timestamp": "2026-08-21T15:30:00.0000000+07:00"
  }
}
```

`message` chỉ xuất hiện nếu controller/handler truyền message; các endpoint liệt kê ở đây không dựa vào field này.

### 3.3. Error envelope

```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "fields": [
      { "field": "pageSize", "message": "'Page Size' must be between 1 and 100." }
    ]
  },
  "meta": {
    "traceId": "f228b934-8cff-4b91-9619-55ac395f29d6",
    "timestamp": "2026-08-21T15:30:00.0000000+07:00"
  }
}
```

`error.fields` bị bỏ khỏi JSON khi không có field error.

Lỗi chung có thể xảy ra trước handler:

| HTTP | `error.code` | Nguyên nhân thực tế trong code |
|---:|---|---|
| 401 | `AUTH_TOKEN_INVALID` | Thiếu/sai/hết hạn access token |
| 403 | `FORBIDDEN` | Role không hợp lệ |
| 403 | `AUTH_PHONE_REQUIRED` | Passenger chưa hoàn tất phone profile |
| 404 | `ROUTE_NOT_FOUND` | Gateway chưa đăng ký path; hiện xảy ra ở production với Reliability routes chưa deploy |
| 409 | `IDEMPOTENCY_REQUEST_PENDING` | Request cùng key vẫn đang xử lý |
| 422 | `IDEMPOTENCY_KEY_REQUIRED` hoặc `VALIDATION_ERROR` | Thiếu key hoặc key không phải UUID v4; action filter cũ có thể dùng `VALIDATION_ERROR` |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Reuse key cho request khác |
| 429 | `RATE_LIMITED` | Global Nest throttler định nghĩa 120 request/60 giây/IP; ⚠️ TODO: cần xác nhận bằng deployment test việc guard có áp dụng cho raw proxy route Parcel hay không |
| 502 | `UPSTREAM_UNAVAILABLE` | Gateway không kết nối được Parcel service |
| 500 | `INTERNAL_ERROR` | Exception không được map riêng; không hiển thị nội dung exception ra FE |

### 3.4. Format dữ liệu

- JSON dùng `camelCase`.
- Timestamp trả qua public `/v1` dùng RFC 3339/ISO 8601 với offset Việt Nam `+07:00`.
- Input `from`/`to` của history là RFC 3339 string; input DateOnly dùng `YYYY-MM-DD`.
- `Guid` dùng UUID dạng canonical.
- Tiền là JSON integer `int64`, đơn vị VND, không phải số thập phân.
- Kích thước/trọng lượng là JSON number.
- `PagedResult<T>` có đúng các field: `items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNextPage`, `hasPreviousPage`.

## 4. Tổng quan endpoint

| Method | Path | Auth | Mô tả | Production 2026-08-21 |
|---|---|---|---|---|
| GET | `/v1/parcels/available-trips` | PASSENGER | Tìm chuyến nhận Parcel và quote | Có |
| POST | `/v1/parcels` | PASSENGER | Tạo Parcel | Có |
| POST | `/v1/parcels/{parcelId}/deposit-payment` | PASSENGER | Bắt đầu trả cọc | Có |
| POST | `/v1/parcels/{parcelId}/final-payment` | PASSENGER | Bắt đầu trả phần còn lại | Có |
| GET | `/v1/parcels/vouchers/available` | PASSENGER | Voucher áp dụng được | Có |
| GET | `/v1/parcels/received` | PASSENGER | Parcel nhận | Có |
| GET | `/v1/parcels/sent` | PASSENGER | Parcel đã gửi | Có |
| GET | `/v1/passenger/history` | PASSENGER | History ticket/parcel hợp nhất | Có |
| GET | `/v1/parcels/{parcelId}` | user liên quan/operator | Chi tiết screen-ready | Có |
| GET | `/v1/parcels/{parcelId}/trace` | user liên quan/operator | Tracking screen-ready | **Chưa có** |
| POST | `/v1/parcels/{parcelId}/incidents` | user liên quan/operator | Báo sự cố | **Chưa có** |
| GET | `/v1/parcels/{parcelId}/incidents` | user liên quan/operator | Danh sách incident | **Chưa có** |
| GET | `/v1/parcels/{parcelId}/claims` | sender/operator | Danh sách claim | **Chưa có** |
| POST | `/v1/parcels/{parcelId}/claims` | PASSENGER sender | Nộp claim | **Chưa có** |
| POST | `/v1/parcels/{parcelId}/claims/{claimId}/evidence` | PASSENGER beneficiary | Thêm evidence | **Chưa có** |
| POST | `/v1/parcels/{parcelId}/claims/{claimId}/appeal` | PASSENGER sender | Appeal claim | **Chưa có** |
| POST | `/v1/parcels/delivery/confirm` | anonymous token | Xác nhận đã nhận | Có |
| POST | `/v1/parcels/delivery/reject` | anonymous token | Báo chưa nhận/từ chối | Có |
| POST | `/v1/parcels/delivery/undo-reject` | anonymous token | Hoàn tác reject | Có |

## 5. Kiểu dữ liệu dùng chung

### 5.1. `ReliabilityLocationResponse`

| Field | Type | Nullable | Ý nghĩa |
|---|---|---:|---|
| `type` | string | Có | Loại location do read model trả |
| `id` | UUID | Có | ID station/stop/location |
| `name` | string | Có | Tên hiển thị; có thể null khi enrich không khả dụng |
| `orderIndex` | integer | Có | Thứ tự stop |
| `eta` | date-time | Có | ETA location |

### 5.2. Reliability summary

`reliability`/`reliabilitySummary`:

| Field | Type | Nullable | Ý nghĩa |
|---|---|---:|---|
| `currentCustody` | object | Có | Custody xác nhận gần nhất |
| `activeIncident` | object | Có | Incident chưa đóng |
| `claim` | object | Có | Claim summary; recipient không được nhận claim |
| `nextUpdateAt` | date-time | Có | Mốc cập nhật kế tiếp |
| `availableActions` | string[] | Không | Action backend cho phép |

`currentCustody` có `lastEventType`, `lastConfirmedLocation`, `lastConfirmedAt`, `currentTripId`, `currentVehicleId`, `trackingConfidence`, `hasTrackingGap`. `trackingConfidence` theo enum `CONFIRMED_SCAN`, `MANUAL_EXCEPTION`, `INFERRED_FROM_MANIFEST`, `UNKNOWN`.

`activeIncident` có `incidentId`, `type`, `status`, `searchDeadline`, `nextUpdateAt`, `slaState`, `operatorProcessBreach`.

`claim` có `claimId`, `status`, `totalAwardVnd`, `decisionDeadline`, `payoutDeadline`, `slaState`.

### 5.3. `ParcelClaimResponse`

| Field | Type | Nullable |
|---|---|---:|
| `claimId`, `parcelId`, `incidentId`, `beneficiaryUserId` | UUID | Không |
| `status` | string | Không |
| `declaredValueVnd`, `provenDirectLossVnd` | int64 | Có |
| `compensationRatePercent`, `policyVersion` | integer | Không |
| `policyCapVnd`, `cargoAwardVnd`, `freightRefundVnd`, `totalAwardVnd` | int64 | Không |
| `decisionReason` | string | Có |
| `decidedBy`, `payoutReferenceId`, `appealedByUserId` | UUID | Có |
| `decidedAt`, `paidAt`, `appealedAt`, `decisionDeadline`, `payoutDeadline` | date-time | Có |
| `appealReason` | string | Có |
| `evidence` | `ParcelClaimEvidenceResponse[]` | Không |
| `parcelSummary`, `incidentSummary`, `policySnapshot` | object | Có |
| `availableActions` | string[] | Không |

Mỗi evidence có `evidenceId`, `evidenceType`, `reference`, `note`, `uploadedByUserId`, `createdAt`.

### 5.4. Enum dùng bởi Passenger

```text
ParcelSizeCategory: SMALL | MEDIUM | LARGE | EXTRA_LARGE
ParcelStatus: PENDING_OPERATOR_REVIEW | PENDING_PAYMENT | PENDING |
  PENDING_ADDITIONAL_PAYMENT | RESERVED | CHECKED_IN | PENDING_FINAL_PAYMENT |
  READY_TO_LOAD | LOADED | IN_TRANSIT | PENDING_TRANSFER_CONFIRM |
  TRANSFER_ESCALATED | UNLOADED | DELIVERED_PENDING_CONFIRM |
  DELIVERY_CONFIRMED | DELIVERY_REJECTED | RETURN_INITIATED | RETURNED |
  PENDING_OPERATOR_ACTION | CANCELLED | REJECTED | EXPIRED
IncidentType: MISSING | WRONG_STOP | DELIVERY_NOT_RECEIVED | PARTIAL_LOSS |
  DAMAGED | SCAN_IDENTITY_MISMATCH | PACKAGE_IDENTITY_MISMATCH |
  UNSCANNED_HANDOFF | MISSING_AFTER_DEPARTURE
IncidentStatus: OPEN | SEARCHING | FOUND | FORWARDING | RESOLVED | CLOSED |
  ESCALATED | SEARCH_EXPIRED | LOST_CONFIRMED
ClaimStatus: SUBMITTED | UNDER_REVIEW | APPROVED | FUNDING_PENDING | PAID |
  REJECTED | CANCELLED | APPEALED
```

## 6. Tra cứu, tạo và thanh toán Parcel

### 6.1. Tìm chuyến nhận Parcel

`GET {BASE_URL}/v1/parcels/available-trips`

Headers: `Authorization`.

| Query | Type | Bắt buộc | Validation/default |
|---|---|---:|---|
| `originStationId` | UUID | Có | khác empty GUID |
| `destinationStationId` | UUID | Có | khác empty GUID |
| `departureDate` | date | Có | `YYYY-MM-DD`, khác default |
| `lengthCm`, `widthCm`, `heightCm`, `estimatedWeightKg` | number | Có | `> 0` |
| `sizeCategory` | string | Không | Nếu có phải thuộc `ParcelSizeCategory`; server vẫn tự derive category từ chargeable weight |
| `page` | integer | Không | mặc định 1, `>= 1` |
| `pageSize` | integer | Không | mặc định 20, `1..100` |

Success `200`: `data` là `PagedResult<AvailableTripResponse>`; mỗi item có đầy đủ `tripId`, `routeId`, `status`, `operatorId`, `operatorName`, `originStation{id,name}`, `destinationStation{id,name}`, `departureDateTime`, `estimatedArrivalTime`, `estimatedPriceVnd`, `depositPercent`, `estimatedDepositVnd`, `quoteToken`, `quoteExpiresAt`, `estimatedSizeCategory`, `estimatedGrossPriceVnd`, `estimatedDiscountVnd`.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [{
      "tripId": "11111111-1111-4111-8111-111111111111",
      "routeId": "22222222-2222-4222-8222-222222222222",
      "status": "SCHEDULED",
      "operatorId": "33333333-3333-4333-8333-333333333333",
      "operatorName": "VietRide Operator",
      "originStation": { "id": "44444444-4444-4444-8444-444444444444", "name": "Bến A" },
      "destinationStation": { "id": "55555555-5555-4555-8555-555555555555", "name": "Bến B" },
      "departureDateTime": "2026-08-22T08:00:00+07:00",
      "estimatedArrivalTime": "2026-08-22T12:00:00+07:00",
      "estimatedPriceVnd": 120000,
      "depositPercent": 30,
      "estimatedDepositVnd": 36000,
      "quoteToken": "<opaque-quote-token>",
      "quoteExpiresAt": "2026-08-21T16:00:00+07:00",
      "estimatedSizeCategory": "SMALL",
      "estimatedGrossPriceVnd": 120000,
      "estimatedDiscountVnd": 0
    }],
    "page": 1,
    "pageSize": 20,
    "totalItems": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T15:30:00+07:00" }
}
```

Lỗi riêng: `422 VALIDATION_ERROR`; `404 OPERATOR_NOT_FOUND`; `503 TRIP_SEARCH_UNAVAILABLE`, `OPERATOR_LOOKUP_UNAVAILABLE` hoặc `UPSTREAM_UNAVAILABLE`.

```bash
curl -G "$BASE_URL/v1/parcels/available-trips" -H "Authorization: Bearer $ACCESS_TOKEN" \
  --data-urlencode "originStationId=44444444-4444-4444-8444-444444444444" \
  --data-urlencode "destinationStationId=55555555-5555-4555-8555-555555555555" \
  --data-urlencode "departureDate=2026-08-22" --data-urlencode "lengthCm=30" \
  --data-urlencode "widthCm=20" --data-urlencode "heightCm=15" \
  --data-urlencode "estimatedWeightKg=5" --data-urlencode "page=1" --data-urlencode "pageSize=20"
```

```js
const q = new URLSearchParams({ originStationId: '44444444-4444-4444-8444-444444444444', destinationStationId: '55555555-5555-4555-8555-555555555555', departureDate: '2026-08-22', lengthCm: '30', widthCm: '20', heightCm: '15', estimatedWeightKg: '5' });
const result = await fetch(`${BASE_URL}/v1/parcels/available-trips?${q}`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 6.2. Tạo Parcel

`POST {BASE_URL}/v1/parcels`

Headers: `Authorization`, `Content-Type`, `Idempotency-Key`.

| Body field | Type | Bắt buộc | Validation/behavior |
|---|---|---:|---|
| `tripId` | UUID | Có | khác empty; trip phải `SCHEDULED` |
| `dropoffStopId` | UUID | Không | nếu có phải thuộc trip và `allowDropoff=true` |
| `bookingId` | UUID | Không | phải thuộc sender, cùng trip, booking `CONFIRMED`, còn active ticket |
| `itemName` | string | Không | nếu có được ghép vào đầu `description`; code không đặt max riêng |
| `description` | string | Không | tối đa 2000 ký tự |
| `sizeCategory` | enum string | Có | hợp lệ; quote token phải khớp; giá thực tế derive từ kích thước/trọng lượng |
| `lengthCm`, `widthCm`, `heightCm`, `estimatedWeightKg` | number | Có | `> 0` |
| `photoUrl` | string | Không | rỗng hoặc Firebase URL sở hữu bởi sender dưới prefix `parcels/{senderUserId}/` |
| `recipient.fullName` | string | Có | không rỗng, tối đa 255 |
| `recipient.phoneNumber` | string | Có | không rỗng, tối đa 20; sau đó được `PhoneNumber.Normalize` |
| `recipient.email` | string | Không | tối đa 255, email hợp lệ |
| `deliveryMethod` | string | Có | chỉ `TERMINAL_PICKUP` |
| `paymentMethod` | string | Có | `VNPAY` hoặc `WALLET` |
| `voucherCode` | string | Không | phải applicable nếu có |
| `quoteToken` | string | Không | tối đa 16.384; nếu có phải khớp sender/trip/route/operator/stations/cargo/category và chưa hết hạn |
| `declaredValueVnd` | int64 | Không | `>= 0` |
| `quantity` | integer | Không | mặc định 1, `1..10000` |

```json
{
  "tripId": "11111111-1111-4111-8111-111111111111",
  "dropoffStopId": "66666666-6666-4666-8666-666666666666",
  "bookingId": null,
  "itemName": "Laptop",
  "description": "Máy màu bạc, serial SN123",
  "sizeCategory": "SMALL",
  "lengthCm": 35,
  "widthCm": 25,
  "heightCm": 8,
  "estimatedWeightKg": 2.5,
  "photoUrl": null,
  "recipient": {
    "fullName": "Nguyễn Văn B",
    "phoneNumber": "0901234567",
    "email": "recipient@example.com"
  },
  "deliveryMethod": "TERMINAL_PICKUP",
  "paymentMethod": "WALLET",
  "voucherCode": null,
  "quoteToken": "<opaque-quote-token>",
  "declaredValueVnd": 12000000,
  "quantity": 1
}
```

Success `201`:

```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "parcelId": "77777777-7777-4777-8777-777777777777",
    "parcelCode": "VR-PCL-20260821-ABCD2345",
    "status": "PENDING_PAYMENT",
    "estimatedSizeCategory": "SMALL",
    "estimatedGrossPriceVnd": 120000,
    "discountAmountVnd": 0,
    "estimatedTotalPriceVnd": 120000,
    "depositPercent": 30,
    "depositRequiredVnd": 36000,
    "depositPaidVnd": 0,
    "voucherCode": null,
    "settlementPolicyVersion": 2,
    "compensationPolicy": {
      "version": 1,
      "compensationRatePercent": 50,
      "maxCompensationVnd": 30000000,
      "noProofFallbackMultiplier": 4,
      "claimWindowDays": 30,
      "searchSlaHours": 72,
      "decisionSlaBusinessDays": 7,
      "payoutSlaBusinessDays": 3
    }
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T15:30:00+07:00" }
}
```

Lỗi riêng:

- 403: `USER_FORBIDDEN`, `USER_NOT_PASSENGER`, `USER_INACTIVE`, `BOOKING_NOT_OWNED_BY_SENDER`, hoặc lỗi subscription được upstream trả.
- 404: `USER_NOT_FOUND`, `BOOKING_NOT_FOUND`, `TRIP_NOT_FOUND`.
- 409: `BOOKING_NOT_FOR_THIS_TRIP`, `BOOKING_NOT_ATTACHABLE`, `TRIP_NOT_ACCEPTING_PARCEL`, `PARCEL_CHECK_IN_CLOSED`, `PARCEL_CODE_COLLISION`, `PARCEL_QUOTE_INVALID`, `PARCEL_QUOTE_EXPIRED`, `PARCEL_QUOTE_MISMATCH`, `PARCEL_QUOTE_STALE`.
- 422: `VALIDATION_ERROR`, `DROP_OFF_STOP_NOT_FOUND`, `DROP_OFF_STOP_NOT_ALLOWED`, `INVALID_DELIVERY_METHOD`, `INVALID_SIZE_CATEGORY`, `FARE_NOT_CONFIGURED`, `VOUCHER_NOT_APPLICABLE`.
- 503: `UPSTREAM_UNAVAILABLE`, `BOOKING_SERVICE_UNAVAILABLE`, `TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/parcels" -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" -H "Idempotency-Key: 4d87fe36-e3b7-45dc-9aca-c1f17a33f08d" \
  --data '{"tripId":"11111111-1111-4111-8111-111111111111","dropoffStopId":"66666666-6666-4666-8666-666666666666","bookingId":null,"itemName":"Laptop","description":"Máy màu bạc, serial SN123","sizeCategory":"SMALL","lengthCm":35,"widthCm":25,"heightCm":8,"estimatedWeightKg":2.5,"photoUrl":null,"recipient":{"fullName":"Nguyễn Văn B","phoneNumber":"0901234567","email":"recipient@example.com"},"deliveryMethod":"TERMINAL_PICKUP","paymentMethod":"WALLET","voucherCode":null,"quoteToken":"<opaque-quote-token>","declaredValueVnd":12000000,"quantity":1}'
```

```js
const response = await fetch(`${BASE_URL}/v1/parcels`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(createParcelBody) });
const result = await response.json();
```

### 6.3. Bắt đầu deposit/final payment

```text
POST /v1/parcels/{parcelId}/deposit-payment
POST /v1/parcels/{parcelId}/final-payment
```

Path `parcelId`: UUID bắt buộc. Headers: `Authorization`, `Content-Type`, `Idempotency-Key`.

Body dùng chung:

| Field | Type | Bắt buộc | Rule |
|---|---|---:|---|
| `paymentMethod` | string | Có | `WALLET` hoặc `VNPAY` |
| `paymentReturnMode` | string | Không | Với `WALLET`, handler không kiểm tra giá trị. Với `VNPAY`, bắt buộc có và phải đúng `MOBILE_SDK`; thiếu trả `426 MOBILE_APP_UPDATE_REQUIRED`, giá trị khác trả `422 PAYMENT_RETURN_MODE_INVALID` |

```json
{ "paymentMethod": "WALLET", "paymentReturnMode": null }
```

Deposit success `200` có `parcelId`, `status`, `depositPaymentId`, `depositRequiredVnd`, `depositPaidVnd`, `paymentDueAt`, `paymentRedirectUrl`, `paymentReturnMode`, `vnpaySdk{tmnCode,scheme,isSandbox}`.

Final success `200` có `parcelId`, `status`, `balancePaymentId`, `balanceRequiredVnd`, `balancePaidVnd`, `finalPaymentDeadline`, `paymentRedirectUrl`, `paymentReturnMode`, `vnpaySdk`.

Ví dụ `data` cho deposit bằng wallet:

```json
{
  "parcelId": "77777777-7777-4777-8777-777777777777",
  "status": "PENDING_PAYMENT",
  "depositPaymentId": "12121212-1212-4121-8121-121212121212",
  "depositRequiredVnd": 36000,
  "depositPaidVnd": 0,
  "paymentDueAt": "2026-08-22T07:00:00+07:00",
  "paymentRedirectUrl": null,
  "paymentReturnMode": null,
  "vnpaySdk": null
}
```

Ví dụ `data` cho final payment bằng wallet:

```json
{
  "parcelId": "77777777-7777-4777-8777-777777777777",
  "status": "PENDING_FINAL_PAYMENT",
  "balancePaymentId": "13131313-1313-4131-8131-131313131313",
  "balanceRequiredVnd": 84000,
  "balancePaidVnd": 0,
  "finalPaymentDeadline": "2026-08-22T07:15:00+07:00",
  "paymentRedirectUrl": null,
  "paymentReturnMode": null,
  "vnpaySdk": null
}
```

Lỗi deposit theo mapping thực tế:

- `403 FORBIDDEN`.
- `404 PARCEL_NOT_FOUND`.
- `409 INVALID_STATUS`, `PAYMENT_ALREADY_STARTED`, `PARCEL_CHECK_IN_CLOSED`, `TRIP_CARGO_CAPACITY_EXCEEDED`, `RACE_LOST`.
- `422 VALIDATION_ERROR`, `INSUFFICIENT_FUNDS`, `VOUCHER_NOT_APPLICABLE`, `PAYMENT_RETURN_MODE_INVALID` và lỗi idempotency `IDEMPOTENCY_KEY_REQUIRED`/`IDEMPOTENCY_KEY_MISMATCH`.
- `426 MOBILE_APP_UPDATE_REQUIRED` khi chọn `VNPAY` nhưng không gửi `paymentReturnMode`.
- `503 BOOKING_SERVICE_UNAVAILABLE`, `TRIP_SERVICE_UNAVAILABLE`, `TRIP_NOT_FOUND`, `PAYMENT_SERVICE_ERROR`, `VNPAY_MOBILE_SDK_DISABLED` hoặc lỗi dependency chung.

Lỗi final theo mapping thực tế:

- `403 FORBIDDEN`.
- `404 PARCEL_NOT_FOUND`.
- `409 INVALID_STATUS`, `PAYMENT_ALREADY_STARTED`, `FINAL_PAYMENT_DEADLINE_PASSED`, `BALANCE_ALREADY_PAID`, `RACE_LOST`.
- `422 VALIDATION_ERROR`, `INSUFFICIENT_FUNDS`, `PAYMENT_RETURN_MODE_INVALID` và lỗi idempotency.
- `426 MOBILE_APP_UPDATE_REQUIRED`.
- `503 PAYMENT_SERVICE_ERROR`, `VNPAY_MOBILE_SDK_DISABLED` hoặc lỗi dependency chung.

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/deposit-payment" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"paymentMethod":"WALLET","paymentReturnMode":null}'
```

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/final-payment" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"paymentMethod":"WALLET","paymentReturnMode":null}'
```

```js
const pay = (kind) => fetch(`${BASE_URL}/v1/parcels/${parcelId}/${kind}-payment`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ paymentMethod: 'WALLET', paymentReturnMode: null }) }).then(r => r.json());
const depositPayment = await pay('deposit');
const finalPayment = await pay('final');
```

### 6.4. Voucher khả dụng

`GET {BASE_URL}/v1/parcels/vouchers/available`

Query: `tripId` UUID và `sizeCategory` string được controller nhận như non-null; `paymentMethod`, `orderAmount` int64, `quoteToken` optional. Nếu có `quoteToken`, token phải khớp user/trip/route/operator/stations, `sizeCategory` và `orderAmount`.

Success `200`: array item gồm `id`, `code`, `name`, `type`, `value`, `minOrderAmount`, `maxDiscountAmount`, `discountAmount`, `applicableServices`, `applicablePaymentMethods`, `validUntil`.

```json
{
  "success": true,
  "statusCode": 200,
  "data": [{
    "id": "14141414-1414-4141-8141-141414141414",
    "code": "PARCEL10",
    "name": "Giảm giá Parcel",
    "type": "PERCENTAGE",
    "value": 10,
    "minOrderAmount": 100000,
    "maxDiscountAmount": 30000,
    "discountAmount": 12000,
    "applicableServices": ["PARCEL"],
    "applicablePaymentMethods": ["WALLET"],
    "validUntil": "2026-08-31T23:59:59+07:00"
  }],
  "meta": { "traceId": "...", "timestamp": "2026-08-21T15:30:00+07:00" }
}
```

Lỗi riêng: `422 INVALID_SIZE_CATEGORY`; `404 TRIP_NOT_FOUND`; `409 PARCEL_QUOTE_INVALID`, `PARCEL_QUOTE_EXPIRED`, `PARCEL_QUOTE_MISMATCH`, `PARCEL_QUOTE_STALE`; `503 TRIP_SERVICE_UNAVAILABLE`. Không có validator riêng buộc `tripId` khác empty hoặc `sizeCategory` không rỗng ngoài logic handler.

```bash
curl -G "$BASE_URL/v1/parcels/vouchers/available" -H "Authorization: Bearer $ACCESS_TOKEN" --data-urlencode "tripId=$TRIP_ID" --data-urlencode "sizeCategory=SMALL" --data-urlencode "paymentMethod=WALLET" --data-urlencode "quoteToken=$QUOTE_TOKEN"
```

```js
const vouchers = await fetch(`${BASE_URL}/v1/parcels/vouchers/available?${new URLSearchParams({ tripId, sizeCategory: 'SMALL', paymentMethod: 'WALLET', quoteToken })}`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

## 7. Danh sách, chi tiết và tracking

### 7.1. Parcel đã nhận

`GET /v1/parcels/received?page=1&pageSize=20`

`page >= 1`, `pageSize=1..100`. Success `200` PagedResult; item có `parcelId`, `parcelCode`, `status`, `originStation{id,name}`, `destinationStation{id,name}`, `eta`, `senderUserId`, `recipientName`, `sizeCategory`, `createdAt`, `operatorId`, `tripId`, `operator{operatorId,name,logoUrl,contactPhone}`, `dropoffLocation`, `reliability`. Recipient không nhận claim summary trong `reliability`.

Lỗi riêng: `422 VALIDATION_ERROR`; controller khai báo `503` khi enrich/service không khả dụng. Read model hiện degrade nullable display fields thay vì luôn fail.

```bash
curl "$BASE_URL/v1/parcels/received?page=1&pageSize=20" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const received = await fetch(`${BASE_URL}/v1/parcels/received?page=1&pageSize=20`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 7.2. Parcel đã gửi

`GET /v1/parcels/sent`

| Query | Type | Optional | Rule |
|---|---|---:|---|
| `status` | string | Có | một `ParcelStatus` |
| `from`, `to` | RFC 3339 string | Có | hợp lệ; nếu có cả hai thì bắt buộc `from < to` |
| `page` | integer | Có | mặc định 1, `>=1` |
| `pageSize` | integer | Có | mặc định 20, `1..100` |

Success PagedResult item có `parcelId`, `parcelCode`, `tripId`, `status`, `createdAt`, `totalAmount`, `originName`, `destinationName`, `departureDateTime`, `estimatedArrivalTime`, `bookingId`, `recipientName`, `sizeCategory`, `photoUrl`, `deliveryMethod`, `operator`, `dropoffLocation`, `reliability`. Sender có thể thấy `reliability.claim`.

Lỗi riêng: `422 VALIDATION_ERROR` cho paging/status/date.

```bash
curl -G "$BASE_URL/v1/parcels/sent" -H "Authorization: Bearer $ACCESS_TOKEN" --data-urlencode "status=IN_TRANSIT" --data-urlencode "page=1" --data-urlencode "pageSize=20"
```

```js
const sent = await fetch(`${BASE_URL}/v1/parcels/sent?${new URLSearchParams({ status: 'IN_TRANSIT', page: '1', pageSize: '20' })}`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 7.3. Passenger history hợp nhất

`GET /v1/passenger/history`

`type` thực tế bắt buộc về nghiệp vụ dù controller cho null: chỉ `TICKET` hoặc `PARCEL`; null được chuyển thành empty và validator trả 422. `status` phải phù hợp type. `from`, `to`, `page`, `pageSize` cùng rule như sent list.

Success PagedResult item: `type`, `id`, `code`, `tripId`, `status`, `createdAt`, `totalAmount`, `originName`, `destinationName`, `departureDateTime`, `estimatedArrivalTime`, `ticket`, `parcel`, `paymentRedirectUrl`, `trackingTarget{kind,stopId,stationId}`. `ticket` hoặc `parcel` có thể null theo `type`.

Lỗi: `422 VALIDATION_ERROR`; `502 UPSTREAM_UNAVAILABLE` khi Booking history upstream lỗi.

```bash
curl -G "$BASE_URL/v1/passenger/history" -H "Authorization: Bearer $ACCESS_TOKEN" --data-urlencode "type=PARCEL" --data-urlencode "page=1" --data-urlencode "pageSize=20"
```

```js
const history = await fetch(`${BASE_URL}/v1/passenger/history?type=PARCEL&page=1&pageSize=20`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 7.4. Parcel detail screen-ready

`GET /v1/parcels/{parcelId}`

Path: `parcelId` UUID. Auth: sender, linked recipient hoặc user có `operatorId` đúng Parcel. Success `200` trả mọi field dưới đây; nullable field vẫn có thể xuất hiện với `null`:

```json
{
  "parcelId": "77777777-7777-4777-8777-777777777777",
  "parcelCode": "VR-PCL-20260821-ABCD2345",
  "status": "IN_TRANSIT",
  "senderUserId": "88888888-8888-4888-8888-888888888888",
  "recipientUserId": "99999999-9999-4999-8999-999999999999",
  "recipientName": "Nguyễn Văn B",
  "recipientPhone": "0901234567",
  "operatorId": "33333333-3333-4333-8333-333333333333",
  "tripId": "11111111-1111-4111-8111-111111111111",
  "dropoffStopId": "66666666-6666-4666-8666-666666666666",
  "description": "Laptop\nMáy màu bạc, serial SN123",
  "quantity": 1,
  "photoUrl": null,
  "checkInPhotoUrls": [],
  "deliveryPhotoUrls": [],
  "sizeCategory": "SMALL",
  "estimatedWeightKg": 2.5,
  "actualWeightKg": 2.6,
  "deliveryMethod": "TERMINAL_PICKUP",
  "depositAmount": 36000,
  "originalDepositAmount": 36000,
  "discountAmount": 0,
  "voucherCode": null,
  "voucherUsageId": null,
  "additionalAmount": 0,
  "estimatedSizeCategory": "SMALL",
  "actualSizeCategory": "SMALL",
  "estimatedLengthCm": 35,
  "estimatedWidthCm": 25,
  "estimatedHeightCm": 8,
  "estimatedVolumeM3": 0.007,
  "estimatedDimWeightKg": 1.4,
  "estimatedChargeableWeightKg": 2.5,
  "actualLengthCm": 35,
  "actualWidthCm": 25,
  "actualHeightCm": 8,
  "actualVolumeM3": 0.007,
  "actualDimWeightKg": 1.4,
  "actualChargeableWeightKg": 2.6,
  "estimatedGrossPriceVnd": 120000,
  "finalGrossPriceVnd": 120000,
  "discountAmountVnd": 0,
  "estimatedTotalPriceVnd": 120000,
  "finalTotalPriceVnd": 120000,
  "depositPercent": 30,
  "depositRequiredVnd": 36000,
  "depositPaidVnd": 36000,
  "balanceRequiredVnd": 84000,
  "balancePaidVnd": 84000,
  "refundDueVnd": 0,
  "refundedAmountVnd": 0,
  "forfeitedDepositVnd": 0,
  "depositPaymentId": null,
  "balancePaymentId": null,
  "loadCutoffAt": "2026-08-22T07:30:00+07:00",
  "latestCheckInAt": "2026-08-22T07:00:00+07:00",
  "checkedInAt": "2026-08-22T06:30:00+07:00",
  "checkedInByUserId": null,
  "reweighedAt": "2026-08-22T06:35:00+07:00",
  "reweighedByUserId": null,
  "finalPaymentDeadline": "2026-08-22T07:15:00+07:00",
  "pricePerKgVnd": 0,
  "minimumPriceVnd": 120000,
  "dimWeightFactor": 5000,
  "settlementPolicyVersion": 2,
  "createdAt": "2026-08-21T15:30:00+07:00",
  "loadedAt": "2026-08-22T07:20:00+07:00",
  "unloadedAt": null,
  "deliveredPendingConfirmAt": null,
  "confirmedAt": null,
  "rejectedAt": null,
  "originStationName": "Bến A",
  "destinationStationName": "Bến B",
  "eta": "2026-08-22T12:00:00+07:00",
  "operator": { "operatorId": "33333333-3333-4333-8333-333333333333", "name": "VietRide Operator", "logoUrl": null, "contactPhone": null },
  "trip": {
    "tripId": "11111111-1111-4111-8111-111111111111",
    "status": "IN_PROGRESS",
    "departureAt": "2026-08-22T08:00:00+07:00",
    "eta": "2026-08-22T12:00:00+07:00",
    "route": {
      "routeId": "22222222-2222-4222-8222-222222222222",
      "name": "Bến A - Bến B",
      "origin": { "type": "ORIGIN_STATION", "id": "44444444-4444-4444-8444-444444444444", "name": "Bến A", "orderIndex": null, "eta": null },
      "destination": { "type": "DESTINATION_STATION", "id": "55555555-5555-4555-8555-555555555555", "name": "Bến B", "orderIndex": null, "eta": null }
    },
    "vehicle": { "vehicleId": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee", "licensePlate": "51B-123.45", "status": "ACTIVE" },
    "stops": []
  },
  "dropoffLocation": { "type": "ROUTE_STOP", "id": "66666666-6666-4666-8666-666666666666", "name": "Stop C", "orderIndex": 2, "eta": "2026-08-22T10:00:00+07:00" },
  "compensationPolicySnapshot": { "version": 1, "compensationRatePercent": 50, "maxCompensationVnd": 30000000, "noProofFallbackMultiplier": 4, "claimWindowDays": 30, "searchSlaHours": 72, "decisionSlaBusinessDays": 7, "payoutSlaBusinessDays": 3 },
  "reliabilitySummary": {
    "currentCustody": null,
    "activeIncident": null,
    "claim": null,
    "nextUpdateAt": null,
    "availableActions": ["REPORT_INCIDENT"]
  },
  "availableActions": ["REPORT_INCIDENT"]
}
```

`trip` có `tripId`, `status`, `departureAt`, `eta`, `route{routeId,name,origin,destination}`, `vehicle{vehicleId,licensePlate,status}`, `stops[]`. Nếu Trip batch lookup không thành công, handler vẫn trả object `trip` từ snapshot Parcel: `tripId` giữ nguyên, các display field có thể null/rỗng và `stops=[]`; handler không trả `trip=null`. Trong runtime DI hiện tại, `reliabilitySummary` và `availableActions` được screen-model service dựng theo mục 5.2; ví dụ `IN_TRANSIT` chưa có incident cho sender trả `REPORT_INCIDENT`.

Lỗi riêng: `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`.

```bash
curl "$BASE_URL/v1/parcels/$PARCEL_ID" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const detail = await fetch(`${BASE_URL}/v1/parcels/${parcelId}`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 7.5. Trace screen-ready — local/source only

`GET /v1/parcels/{parcelId}/trace?cursor=<optional>&limit=50`

`limit` phải `1..100`; `cursor` là chuỗi opaque do `timeline.nextCursor` trả, không tự parse/chế. Auth giống detail. Recipient được trace nhưng handler ẩn claim summary; sender/operator phù hợp có thể thấy.

Success `200`:

```json
{
  "parcelId": "77777777-7777-4777-8777-777777777777",
  "parcelCode": "VR-PCL-20260821-ABCD2345",
  "parcelStatus": "PENDING_OPERATOR_ACTION",
  "parcelSummary": { "parcelId": "77777777-7777-4777-8777-777777777777", "parcelCode": "VR-PCL-20260821-ABCD2345", "status": "PENDING_OPERATOR_ACTION", "description": "Laptop", "photoUrl": null, "quantity": 1, "declaredValueVnd": 12000000 },
  "operator": { "operatorId": "33333333-3333-4333-8333-333333333333", "name": "VietRide Operator", "logoUrl": null, "contactPhone": null },
  "trip": {
    "tripId": "11111111-1111-4111-8111-111111111111",
    "status": "IN_PROGRESS",
    "departureAt": "2026-08-22T08:00:00+07:00",
    "eta": "2026-08-22T12:00:00+07:00",
    "route": {
      "routeId": "22222222-2222-4222-8222-222222222222",
      "name": "Bến A - Bến B",
      "origin": { "type": "ORIGIN_STATION", "id": "44444444-4444-4444-8444-444444444444", "name": "Bến A", "orderIndex": null, "eta": null },
      "destination": { "type": "DESTINATION_STATION", "id": "55555555-5555-4555-8555-555555555555", "name": "Bến B", "orderIndex": null, "eta": null }
    },
    "vehicle": { "vehicleId": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee", "licensePlate": "51B-123.45", "status": "ACTIVE" },
    "stops": []
  },
  "dropoffLocation": { "type": "ROUTE_STOP", "id": "66666666-6666-4666-8666-666666666666", "name": "Stop C", "orderIndex": 2, "eta": null },
  "currentCustody": { "lastEventType": "MANUAL_CUSTODY_EXCEPTION", "lastLocationType": "ROUTE_STOP", "lastLocationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "lastLocationSnapshot": "Stop B", "lastConfirmedAt": "2026-08-22T09:00:00+07:00", "currentTripId": "11111111-1111-4111-8111-111111111111", "currentVehicleId": null, "trackingConfidence": "MANUAL_EXCEPTION" },
  "activeIncident": { "incidentId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "type": "WRONG_STOP", "status": "SEARCHING", "searchDeadline": "2026-08-25T09:00:00+07:00", "nextUpdateAt": "2026-08-22T09:30:00+07:00", "slaState": "ON_TRACK", "operatorProcessBreach": true },
  "forwardingTrip": null,
  "claimSummary": null,
  "availableActions": [],
  "timeline": { "items": [{ "eventId": "cccccccc-cccc-4ccc-8ccc-cccccccccccc", "eventType": "MANUAL_CUSTODY_EXCEPTION", "tripId": "11111111-1111-4111-8111-111111111111", "expectedLocationType": "ROUTE_STOP", "expectedLocationId": "66666666-6666-4666-8666-666666666666", "actualLocationType": "ROUTE_STOP", "actualLocationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "locationSnapshot": "Stop B", "occurredAt": "2026-08-22T09:00:00+07:00", "actorRole": "ASSISTANT", "source": "CUSTODY_EXCEPTION", "reason": null, "sequence": 4 }], "nextCursor": null },
  "incidents": [{ "incidentId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "type": "WRONG_STOP", "status": "SEARCHING", "lastKnownLocation": "Stop B", "searchDeadline": "2026-08-25T09:00:00+07:00", "createdAt": "2026-08-22T09:00:00+07:00", "resolvedAt": null, "operatorProcessBreach": true }],
  "nextUpdateAt": "2026-08-22T09:30:00+07:00"
}
```

Lỗi riêng: `422 VALIDATION_ERROR` (limit/cursor); `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`.

Trong source hiện tại, mapper timeline luôn gán `reason=null` dù custody entity có thể có reason; FE không được kỳ vọng lý do điều tra từ endpoint public này.

Lưu ý contract: `reliability.currentCustody` ở list/detail dùng object lồng `lastConfirmedLocation`; riêng top-level `currentCustody` của `/trace` dùng ba field phẳng `lastLocationType`, `lastLocationId`, `lastLocationSnapshot`. FE không được dùng chung một DTO cho hai shape này nếu chưa có adapter.

```bash
curl "$BASE_URL/v1/parcels/$PARCEL_ID/trace?limit=50" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const trace = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/trace?limit=50`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

## 8. Incident, claim và evidence

### 8.1. Báo incident — local/source only

`POST /v1/parcels/{parcelId}/incidents`

Body:

| Field | Type | Bắt buộc | Rule |
|---|---|---:|---|
| `incidentType` | string | Có | một `IncidentType` |
| `description` | string | Không | tối đa 2000 |
| `evidenceUrls` | string[] | Không | Handler lưu JSON; không có validator URL/count tại endpoint này |

```json
{ "incidentType": "DELIVERY_NOT_RECEIVED", "description": "Ứng dụng báo đã giao nhưng tôi chưa nhận", "evidenceUrls": [] }
```

Success `201`: `{incidentId, parcelId, incidentType, status, searchDeadline}`. Lỗi: `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`; `422 VALIDATION_ERROR`; `409 PARCEL_INCIDENT_ALREADY_OPEN`.

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/incidents" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"incidentType":"DELIVERY_NOT_RECEIVED","description":"Chưa nhận được hàng","evidenceUrls":[]}'
```

```js
const incident = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/incidents`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ incidentType: 'DELIVERY_NOT_RECEIVED', description: 'Chưa nhận được hàng', evidenceUrls: [] }) }).then(r => r.json());
```

### 8.2. Lấy incidents — local/source only

`GET /v1/parcels/{parcelId}/incidents`

Success `200`: array item `{incidentId,type,status,lastKnownLocation,searchDeadline,createdAt,resolvedAt,operatorProcessBreach}`. Endpoint tái sử dụng Trace handler nên cùng authorization và có thể trả `422 VALIDATION_ERROR`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN` dù controller chỉ khai báo 403/404.

```bash
curl "$BASE_URL/v1/parcels/$PARCEL_ID/incidents" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const incidents = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/incidents`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 8.3. Lấy claims — local/source only

`GET /v1/parcels/{parcelId}/claims`

Chỉ sender hoặc operator cùng tenant được xem; linked recipient không được xem claim. Success `200`: `ParcelClaimResponse[]` theo mục 5.3. Lỗi: `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`.

```bash
curl "$BASE_URL/v1/parcels/$PARCEL_ID/claims" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const claims = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/claims`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 8.4. Nộp claim — local/source only

`POST /v1/parcels/{parcelId}/claims`

Body phải để trống. Headers có `Idempotency-Key`. Chỉ sender. Incident phải ở `LOST_CONFIRMED`, chưa quá `claimWindowDays` snapshot và chưa có claim.

Success `201`: full `ParcelClaimResponse`. Lỗi: `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`; `409 PARCEL_CLAIM_WINDOW_NOT_OPEN`, `PARCEL_INCIDENT_CLAIM_WINDOW_EXPIRED`, `PARCEL_CLAIM_ALREADY_EXISTS`; idempotency errors.

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/claims" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Idempotency-Key: $(uuidgen)" --data ''
```

```js
const claim = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/claims`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Idempotency-Key': crypto.randomUUID() } }).then(r => r.json());
```

### 8.5. Thêm evidence — local/source only

`POST /v1/parcels/{parcelId}/claims/{claimId}/evidence`

```json
{ "evidenceType": "INVOICE", "reference": "https://storage.example/evidence/invoice.pdf", "note": "Hóa đơn mua hàng" }
```

`evidenceType` và `reference` là string non-null trong DTO nhưng endpoint không có FluentValidator. Domain yêu cầu chúng không rỗng; hiện `ArgumentException` từ blank value bị global mapper đổi thành `500 INTERNAL_ERROR`, không phải 422. FE phải validate non-blank. `note` optional, được trim.

Success `201` trả **full claim đã cập nhật cùng toàn bộ evidence metadata**, không chỉ evidence mới. Lỗi: `404 PARCEL_CLAIM_NOT_FOUND`, `PARCEL_NOT_FOUND`; `403 FORBIDDEN`; `409 PARCEL_CLAIM_ALREADY_DECIDED`; lỗi chung/idempotency.

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/claims/$CLAIM_ID/evidence" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"evidenceType":"INVOICE","reference":"https://storage.example/evidence/invoice.pdf","note":"Hóa đơn mua hàng"}'
```

```js
const updatedClaim = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/claims/${claimId}/evidence`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ evidenceType: 'INVOICE', reference: evidenceUrl, note: 'Hóa đơn mua hàng' }) }).then(r => r.json());
```

### 8.6. Appeal claim — local/source only

`POST /v1/parcels/{parcelId}/claims/{claimId}/appeal`

Body `{ "reason": "..." }`; `reason` phải không rỗng. Chỉ sender; chỉ claim `PAID` hoặc `REJECTED`.

Success `200` trả full claim, `status=APPEALED`, cập nhật appeal fields và `availableActions`. Lỗi: `422 VALIDATION_ERROR`; `404 PARCEL_NOT_FOUND`, `PARCEL_CLAIM_NOT_FOUND`; `403 FORBIDDEN`; `409 PARCEL_CLAIM_APPEAL_NOT_ALLOWED`.

```bash
curl -X POST "$BASE_URL/v1/parcels/$PARCEL_ID/claims/$CLAIM_ID/appeal" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"reason":"Chứng từ mới cần được xem xét"}'
```

```js
const appealed = await fetch(`${BASE_URL}/v1/parcels/${parcelId}/claims/${claimId}/appeal`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ reason: 'Chứng từ mới cần được xem xét' }) }).then(r => r.json());
```

## 9. Xác nhận hoặc từ chối nhận hàng bằng delivery token

Ba endpoint này `[AllowAnonymous]`; không gửi access token. `token` là UUID raw nhận từ delivery link/email, không phải Parcel QR.

### 9.1. Confirm

`POST /v1/parcels/delivery/confirm`

Body `{ "token": "dddddddd-dddd-4ddd-8ddd-dddddddddddd" }`. Success `200`: `{parcelId,status,confirmedAt}`.

Lỗi: `400 PARCEL_DELIVERY_TOKEN_INVALID`, `PARCEL_DELIVERY_TOKEN_EXPIRED`, `PARCEL_DELIVERY_TOKEN_REVOKED`, `PARCEL_NOT_PENDING_CONFIRM`; `409 RACE_LOST`; `429 RATE_LIMITED`; validation/idempotency.

```bash
curl -X POST "$BASE_URL/v1/parcels/delivery/confirm" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"token":"dddddddd-dddd-4ddd-8ddd-dddddddddddd"}'
```

```js
const confirmed = await fetch(`${BASE_URL}/v1/parcels/delivery/confirm`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ token }) }).then(r => r.json());
```

### 9.2. Reject

`POST /v1/parcels/delivery/reject`

Body `{ "token": "...", "rejectionReason": "Chưa nhận được kiện hàng" }`. Domain/handler yêu cầu reason hợp lệ; success `200`: `{parcelId,status,rejectedAt,canUndoUntil}`.

Lỗi token giống confirm; thêm `422 VALIDATION_ERROR` cho rejection reason rỗng hoặc quá 500 ký tự; `400 PARCEL_NOT_PENDING_CONFIRM`; `409 RACE_LOST`; `429 RATE_LIMITED`.

```bash
curl -X POST "$BASE_URL/v1/parcels/delivery/reject" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"token":"dddddddd-dddd-4ddd-8ddd-dddddddddddd","rejectionReason":"Chưa nhận được kiện hàng"}'
```

```js
const rejected = await fetch(`${BASE_URL}/v1/parcels/delivery/reject`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ token, rejectionReason: 'Chưa nhận được kiện hàng' }) }).then(r => r.json());
```

### 9.3. Undo reject

`POST /v1/parcels/delivery/undo-reject`

Body `{ "token": "..." }`. Success `200`: `{parcelId,status,undoneAt}`.

Lỗi: `400 PARCEL_DELIVERY_TOKEN_INVALID`, `PARCEL_DELIVERY_TOKEN_EXPIRED`, `PARCEL_DELIVERY_TOKEN_REVOKED`, `PARCEL_NOT_DELIVERY_REJECTED`, `PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED`; `409 RACE_LOST`; `429 RATE_LIMITED`.

```bash
curl -X POST "$BASE_URL/v1/parcels/delivery/undo-reject" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"token":"dddddddd-dddd-4ddd-8ddd-dddddddddddd"}'
```

```js
const undone = await fetch(`${BASE_URL}/v1/parcels/delivery/undo-reject`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ token }) }).then(r => r.json());
```

## 10. Flow tích hợp cho Passenger Mobile

### 10.1. Gửi hàng happy path

1. Gọi `available-trips`; giữ `tripId`, `quoteToken`, `quoteExpiresAt`, quote tiền.
2. Có thể gọi `vouchers/available` bằng cùng `quoteToken`.
3. Gọi `POST /v1/parcels` với quote token và một UUID v4 mới.
4. Nếu status `PENDING_PAYMENT`, gọi deposit payment. Với `VNPAY`, dùng `paymentRedirectUrl`/`vnpaySdk` đúng response; với `WALLET`, cập nhật response trực tiếp.
5. Poll/refetch detail ở lifecycle boundary hoặc nhận notification. Không polling detail sau mọi action.
6. Khi cần final payment, gọi final-payment theo `availableActions`/status/deadline.

### 10.2. Tracking và thất lạc

1. Danh sách `sent`/`received` đã có reliability summary; không gọi trace từng row.
2. Mở screen tracking bằng **một** call `/trace`.
3. Trên `/trace`, render `currentCustody.lastLocationSnapshot` (kèm `lastLocationType`/`lastLocationId`) là “vị trí xác nhận gần nhất”. Trên list/detail, field tương ứng là `reliability.currentCustody.lastConfirmedLocation`. Top-level trace không có `hasTrackingGap`; coi `trackingConfidence !== "CONFIRMED_SCAN"` là tracking gap. List/detail mới có `hasTrackingGap` trực tiếp.
4. Chỉ render CTA từ `availableActions`; không tự suy ra từ status.
5. Recipient có thể report incident nhưng không được xem/nộp claim. Sender mới là beneficiary.
6. Chỉ khi incident `LOST_CONFIRMED` và backend trả `SUBMIT_CLAIM` mới gọi submit claim.

### 10.3. Error handling bắt buộc

- 401: single-flight refresh, retry một lần.
- 403 `AUTH_PHONE_REQUIRED`: điều hướng complete profile.
- 409 idempotency pending: chờ ngắn rồi retry **cùng key**.
- 422 field errors: map theo `error.fields[].field`; không parse message.
- 404 `ROUTE_NOT_FOUND` trên Reliability production: feature flag/tắt UI cho đến khi deploy.
- 502/503: giữ dữ liệu screen cũ, cho retry; nullable enrich field không đồng nghĩa Parcel không tồn tại.

## 11. Checklist cho AI agent Passenger FE

Agent phụ trách Passenger Mobile phải:

- Tạo typed models đúng field trong mục 5–9; không đổi `parcelId` thành `id` hoặc tự camel/snake lại.
- Dùng một API `/trace` cho tracking screen; danh sách dùng embedded `reliability`.
- Phân quyền UI sender/recipient bằng `availableActions`, không chỉ dựa vào JWT role.
- Không hiển thị claim cho recipient; backend cũng chặn nhưng FE phải tránh lộ state cache giữa account.
- Lưu idempotency key theo logical mutation cho tới khi có terminal response; retry network/refresh dùng lại key.
- Không gửi `X-Internal-Auth`; không log access token, refresh token, quote token, delivery token hoặc evidence signed URL.
- Xử lý production feature availability vì 7 Reliability path Passenger chưa deploy ở thời điểm audit.
- Viết contract tests tối thiểu cho: sent/received pagination, one-call trace, report incident, submit claim chỉ sau lost, evidence mutation trả full claim, appeal, token expiry và recipient claim privacy.

## 12. Đối chiếu source

Checklist rà soát đã thực hiện:

- 19 public Passenger operations trong local OpenAPI được map vào tài liệu; `/v1/ping` không phải product API và không được Mobile sử dụng.
- Method/path/role so với `ParcelsController`, `ParcelDeliveryController`, `PassengerHistoryController`.
- Request field so với toàn bộ record trong `Controllers/Requests` và FluentValidation/manual validation tương ứng.
- Response field so với local OpenAPI components và response records.
- Error code so với các handler liên quan và global Gateway/.NET filters.
- Production spec được diff với local spec; 7 Passenger Reliability paths thiếu production đã đánh dấu.

⚠️ TODO: rate limit 120/phút được cấu hình bằng `ThrottlerGuard`, nhưng Parcel traffic đi qua raw proxy middleware đăng ký trước Nest router. Cần một deployment test riêng để xác nhận guard có thực sự bao phủ proxy requests trước khi FE dựa vào con số này.
