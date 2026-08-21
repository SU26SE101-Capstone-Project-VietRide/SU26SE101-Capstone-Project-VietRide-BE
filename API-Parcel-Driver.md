# Parcel API — Driver/Assistant Mobile

> Tài liệu sinh từ source code tại ngày 2026-08-21. Đối tượng: AI agent/FE phụ trách Mobile Driver. Trong backend, nghiệp vụ Parcel tại xe chủ yếu thuộc role `ASSISTANT`; role `DRIVER` chỉ truy cập nhóm `/v1/crew/parcels/*`.

## Mục lục

- [1. Nguồn sự thật và deployment](#1-nguồn-sự-thật-và-deployment)
- [2. Base URL, auth và headers](#2-base-url-auth-và-headers)
- [3. Response/error chuẩn](#3-responseerror-chuẩn)
- [4. Tổng quan endpoint và role](#4-tổng-quan-endpoint-và-role)
- [5. Screen model dùng chung](#5-screen-model-dùng-chung)
- [6. Manifest và QR scan](#6-manifest-và-qr-scan)
- [7. Check-in, reweigh, load, unload và deliver](#7-check-in-reweigh-load-unload-và-deliver)
- [8. Custody exception, custody scan và reconciliation](#8-custody-exception-custody-scan-và-reconciliation)
- [9. API dùng chung cho Driver/Assistant](#9-api-dùng-chung-cho-driverassistant)
- [10. Flow xử lý đúng/sai QR và giao nhầm stop](#10-flow-xử-lý-đúngsai-qr-và-giao-nhầm-stop)
- [11. Checklist cho AI agent Driver FE](#11-checklist-cho-ai-agent-driver-fe)
- [12. Đối chiếu source](#12-đối-chiếu-source)

## 1. Nguồn sự thật và deployment

Đã đối chiếu `AssistantParcelsController`, `CrewParcelsController`, request records, validators/handlers, Domain enums, Gateway route/auth, common wrapper/idempotency và cả local/production OpenAPI.

Toàn bộ controller Parcel đã được quét. Các route `/internal/v1/*`, health và ping không đưa vào tài liệu Driver vì không phải FE contract; Mobile không được tự tạo/gửi Internal JWT.

Base production Swagger: `https://api.vietride.online/docs`; Parcel spec: `https://api.vietride.online/api-specs/parcel`.

Chênh lệch hiện tại:

- Production chưa có `custody-exception`, `custody-scan`, `reconcile`.
- Production manifest vẫn trả `PagedResult<AssistantTripParcelResponse>` cũ, chưa có `tripContext`, `summary`, `pagination`, custody/incident/actions.
- Production `load` còn trả `MarkParcelLoadedResponse` cũ; local/source trả `AssistantParcelActionResponse`.
- Production `unload` chưa có JSON request body; local/source bắt buộc `parcelCode`, `actualLocation`, `photoUrls` và trả action screen model.

Do đó Driver FE chỉ nối contract mới sau khi BE deploy cùng commit/source này.

## 2. Base URL, auth và headers

| Môi trường | Base URL |
|---|---|
| Local Gateway | `http://localhost:3000` (`GATEWAY_PORT=3000`) |
| Production Gateway | `https://api.vietride.online` |
| Direct Parcel, chỉ debug BE | `http://localhost:5005` |
| Staging | ⚠️ TODO: cần xác nhận thêm |

Khai báo cho các ví dụ curl:

```bash
BASE_URL="http://localhost:3000"
ACCESS_TOKEN="<RS256-access-token>"
IDEMPOTENCY_KEY="$(uuidgen)"
```

Nếu không có `uuidgen`, gán một UUID v4 do app tạo. Sinh key mới cho thao tác mới; retry cùng thao tác giữ nguyên key.

Access token lấy từ `POST /v1/auth/login`, gắn:

```http
Authorization: Bearer <RS256 access token>
```

Gateway verify issuer `vietride-identity`, audience `vietride-api`, algorithm `RS256`, clock tolerance 5 giây. Token cho role operator-side phải có `operatorStatus=APPROVED`; thiếu claim này trả `401 AUTH_TOKEN_INVALID`, suspended trả `403 OPERATOR_SUSPENDED`.

Headers:

| Header | Rule |
|---|---|
| `Authorization` | Bắt buộc tất cả endpoint trong file |
| `Content-Type: application/json` | Request body JSON |
| `Idempotency-Key` | UUID v4 bắt buộc mọi mutation, trừ `POST .../qr-scan` có `[SkipIdempotency]` |
| `X-Internal-Auth` | FE tuyệt đối không gửi; Gateway tự tạo |

Khi token hết hạn, refresh bằng `POST /v1/auth/refresh`, ghi đè cả token pair, rồi retry mutation với **cùng** `Idempotency-Key`.

## 3. Response/error chuẩn

Success:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": { "traceId": "...", "timestamp": "2026-08-21T15:30:00+07:00" }
}
```

Error có `success=false`, `statusCode`, `error{code,message,fields?}`, `meta`. Timestamp public `/v1` ở offset `+07:00`; tiền int64 VND; JSON camelCase.

Ví dụ location mismatch có structured fields:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "PARCEL_CUSTODY_LOCATION_MISMATCH",
    "message": "Actual unload location does not match the parcel drop-off stop.",
    "fields": [
      { "field": "expectedStop", "message": "66666666-6666-4666-8666-666666666666" },
      { "field": "actualStop", "message": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" },
      { "field": "requiredAction", "message": "KEEP_ON_VEHICLE_OR_REPORT_CUSTODY_EXCEPTION" }
    ]
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T15:30:00+07:00" }
}
```

Lỗi chung: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`, `OPERATOR_SUSPENDED`; `422 VALIDATION_ERROR`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`; `409 IDEMPOTENCY_REQUEST_PENDING`; `502 UPSTREAM_UNAVAILABLE` khi Gateway không nối được Parcel; `503 UPSTREAM_UNAVAILABLE`/mã dependency cụ thể khi Parcel handler không nối được service khác; `500 INTERNAL_ERROR`. Rate limit cấu hình 120 request/60 giây/IP, nhưng ⚠️ TODO cần test xác nhận raw proxy có đi qua `ThrottlerGuard` hay không.

## 4. Tổng quan endpoint và role

| Method | Path | Role hiệu lực | Mô tả | Prod contract mới |
|---|---|---|---|---|
| GET | `/v1/assistant/trips/{tripId}/parcels` | `ASSISTANT` | Manifest screen-ready | Chưa |
| POST | `/v1/assistant/trips/{tripId}/parcels/qr-scan` | `ASSISTANT` | Resolve QR, không mutate | Một phần |
| POST | `/v1/assistant/parcels/{parcelId}/check-in` | `ASSISTANT` | Check-in | Response cũ |
| POST | `/v1/assistant/parcels/{parcelId}/reweigh` | `ASSISTANT` | Cân/đo thực tế | Có |
| POST | `/v1/assistant/parcels/{parcelId}/load` | `ASSISTANT` | Load lên xe | Response cũ |
| POST | `/v1/assistant/parcels/{parcelId}/unload` | `ASSISTANT` | Dỡ đúng stop | Chưa body mới |
| POST | `/v1/assistant/parcels/{parcelId}/deliver` | `ASSISTANT` | Bàn giao cho người nhận | Response cũ |
| POST | `/v1/assistant/parcels/{parcelId}/confirm-delivery` | `ASSISTANT` | Manual confirm | Có |
| POST | `/v1/assistant/parcels/{parcelId}/custody-exception` | `ASSISTANT` | Báo sai bến/không scan | **Chưa có** |
| POST | `/v1/assistant/parcels/{parcelId}/custody-scan` | `ASSISTANT` | Ghi custody scan | **Chưa có** |
| POST | `/v1/assistant/trips/{tripId}/stops/{stopId}/reconcile` | `ASSISTANT` | Đối soát stop | **Chưa có** |
| POST | `/v1/crew/parcels/{parcelId}/confirm-transfer` | `DRIVER`,`ASSISTANT` | Nhận transfer/forwarding | Có |
| POST | `/v1/crew/parcels/{parcelId}/manual-confirm` | `DRIVER`,`ASSISTANT` | Manual confirm | Có |
| POST | `/v1/crew/parcels/{parcelId}/resend-delivery-email` | `DRIVER`,`ASSISTANT` | Gửi lại link nhận hàng | Có |

Lưu ý Gateway route `/v1/assistant/parcels` không khai `requiredRoles`, nhưng Parcel controller vẫn `[Authorize(Roles="ASSISTANT")]`; hiệu lực cuối cùng vẫn chỉ `ASSISTANT`.

## 5. Screen model dùng chung

### 5.1. `AssistantParcelActionResponse`

Các mutation `qr-scan`, `check-in`, `load`, `unload`, `deliver`, `custody-scan`, `custody-exception` trả cùng shape để FE cập nhật card, không refetch manifest:

```json
{
  "parcelState": {
    "parcelId": "77777777-7777-4777-8777-777777777777",
    "parcelCode": "VR-PCL-20260821-ABCD2345",
    "status": "LOADED",
    "dropoffLocation": { "type": "ROUTE_STOP", "id": "66666666-6666-4666-8666-666666666666", "name": "Stop C", "orderIndex": 2, "eta": "2026-08-22T10:00:00+07:00" },
    "paymentState": { "depositRequiredVnd": 36000, "depositPaidVnd": 36000, "balanceRequiredVnd": 84000, "balancePaidVnd": 84000, "finalPaymentDeadline": null, "isFullyPaid": true },
    "identityCheckHints": { "photoUrl": null, "description": "Laptop màu bạc", "expectedWeightKg": 2.5, "actualWeightKg": 2.6, "expectedLengthCm": 35, "expectedWidthCm": 25, "expectedHeightCm": 8, "actualLengthCm": 35, "actualWidthCm": 25, "actualHeightCm": 8 }
  },
  "currentCustody": {
    "lastEventType": "LOADED",
    "lastConfirmedLocation": { "type": "ORIGIN_STATION", "id": null, "name": "Bến A", "orderIndex": null, "eta": null },
    "lastConfirmedAt": "2026-08-22T07:20:00+07:00",
    "currentTripId": "11111111-1111-4111-8111-111111111111",
    "currentVehicleId": null,
    "trackingConfidence": "CONFIRMED_SCAN",
    "hasTrackingGap": false
  },
  "activeIncident": null,
  "createdCustodyEvent": {
    "eventId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    "eventType": "LOADED",
    "actualLocationType": "ORIGIN_STATION",
    "actualLocationId": null,
    "locationSnapshot": "Bến A",
    "occurredAt": "2026-08-22T07:20:00+07:00",
    "sequence": 3
  },
  "availableActions": ["CUSTODY_SCAN", "CUSTODY_EXCEPTION"],
  "warning": null
}
```

`activeIncident` nếu có: `incidentId`, `type`, `status`, `searchDeadline`, `nextUpdateAt`, `slaState`, `operatorProcessBreach`. FE chỉ enable CTA từ `availableActions`.

### 5.2. Các response khác

- `ReweighParcelResponse`: `parcelId`, `parcelCode`, `status`, `actualSizeCategory`, `actualChargeableWeightKg`, `finalGrossPriceVnd`, `discountAmountVnd`, `finalTotalPriceVnd`, `depositPaidVnd`, `balanceRequiredVnd`, `refundDueVnd`, `finalPaymentDeadline`.
- `ManualConfirmDeliveryResponse`: `parcelId`, `status`, `confirmedAt`.
- `ResendDeliveryEmailResponse`: `parcelId`, `status`, `expiresAt`.
- `OperationalParcelResponse`: `parcelId`, `parcelCode`, `status`, `tripId`, `transferTargetTripId`, `transferConfirmedAt`, `returnReason`, `returnedAt`, `refundChoice`, `refundAmount`.

Biến dùng trong ví dụ fetch:

```js
const BASE_URL = "http://localhost:3000";
const accessToken = "<RS256-access-token>";
const tripId = "<trip-uuid>";
const parcelId = "<parcel-uuid>";
```

## 6. Manifest và QR scan

### 6.1. Manifest screen-ready

`GET {BASE_URL}/v1/assistant/trips/{tripId}/parcels`

Headers: `Authorization`. Path `tripId`: UUID.

| Query | Type | Optional | Rule |
|---|---|---:|---|
| `stopId` | UUID | Có | Filter stop |
| `status` | string | Có | phải là một `ParcelStatus` |
| `hasException` | boolean | Có | Filter active incident |
| `search` | string | Có | tối đa 100 ký tự |
| `page` | integer | Có | mặc định 1, `>=1` |
| `pageSize` | integer | Có | mặc định 20, `1..100` |

Chỉ assistant được Trip Service xác nhận là assigned assistant của trip và cùng operator.

Success `200`: wrapper theo §3; object `data` đầy đủ là:

```json
{
  "tripContext": {
    "trip": {
      "tripId": "11111111-1111-4111-8111-111111111111",
      "status": "IN_PROGRESS",
      "departureAt": "2026-08-22T08:00:00+07:00",
      "eta": "2026-08-22T12:00:00+07:00",
      "route": { "routeId": "22222222-2222-4222-8222-222222222222", "name": "A - B", "origin": { "type": "ORIGIN_STATION", "id": "44444444-4444-4444-8444-444444444444", "name": "Bến A", "orderIndex": null, "eta": null }, "destination": { "type": "DESTINATION_STATION", "id": "55555555-5555-4555-8555-555555555555", "name": "Bến B", "orderIndex": null, "eta": null } },
      "vehicle": { "vehicleId": "cccccccc-cccc-4ccc-8ccc-cccccccccccc", "licensePlate": "51B-123.45", "status": "ACTIVE" },
      "stops": []
    },
    "currentOperationalLocation": { "location": { "type": "ROUTE_STOP", "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "name": "Stop B", "orderIndex": 1, "eta": "2026-08-22T09:00:00+07:00" }, "status": "ARRIVED", "arrivedAt": "2026-08-22T09:01:00+07:00", "departedAt": null },
    "orderedStops": [{ "stopId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "name": "Stop B", "orderIndex": 1, "estimatedArrivalAt": "2026-08-22T09:00:00+07:00", "status": "ARRIVED", "actualArrivalAt": "2026-08-22T09:01:00+07:00", "actualDepartureAt": null }]
  },
  "summary": { "total": 1, "checkedIn": 1, "loaded": 1, "expectedAtCurrentStop": 0, "unloaded": 0, "exceptionCount": 0, "unresolvedCount": 0 },
  "items": [{
    "parcelId": "77777777-7777-4777-8777-777777777777",
    "parcelCode": "VR-PCL-20260821-ABCD2345",
    "status": "IN_TRANSIT",
    "recipientName": "Nguyễn Văn B",
    "recipientPhone": "0901234567",
    "dropoffStopId": "66666666-6666-4666-8666-666666666666",
    "sizeCategory": "SMALL",
    "estimatedSizeCategory": "SMALL",
    "actualSizeCategory": "SMALL",
    "estimatedWeightKg": 2.5,
    "actualWeightKg": 2.6,
    "balanceRequiredVnd": 84000,
    "balancePaidVnd": 84000,
    "finalPaymentDeadline": null,
    "description": "Laptop màu bạc",
    "photoUrl": null,
    "dropoffLocation": { "type": "ROUTE_STOP", "id": "66666666-6666-4666-8666-666666666666", "name": "Stop C", "orderIndex": 2, "eta": null },
    "currentCustody": null,
    "activeIncident": null,
    "paymentState": { "depositRequiredVnd": 36000, "depositPaidVnd": 36000, "balanceRequiredVnd": 84000, "balancePaidVnd": 84000, "finalPaymentDeadline": null, "isFullyPaid": true },
    "identityCheckHints": { "photoUrl": null, "description": "Laptop màu bạc", "expectedWeightKg": 2.5, "actualWeightKg": 2.6, "expectedLengthCm": 35, "expectedWidthCm": 25, "expectedHeightCm": 8, "actualLengthCm": 35, "actualWidthCm": 25, "actualHeightCm": 8 },
    "availableActions": ["CUSTODY_SCAN", "UNLOAD", "CUSTODY_EXCEPTION"]
  }],
  "pagination": { "page": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false }
}
```

Lỗi: `403 FORBIDDEN`; `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE`/`UPSTREAM_UNAVAILABLE` tùy service client outcome.

```bash
curl -G "$BASE_URL/v1/assistant/trips/$TRIP_ID/parcels" -H "Authorization: Bearer $ACCESS_TOKEN" --data-urlencode "stopId=$STOP_ID" --data-urlencode "page=1" --data-urlencode "pageSize=20"
```

```js
const manifest = await fetch(`${BASE_URL}/v1/assistant/trips/${tripId}/parcels?${new URLSearchParams({ stopId, page: '1', pageSize: '20' })}`, { headers: { Authorization: `Bearer ${accessToken}` } }).then(r => r.json());
```

### 6.2. QR scan không mutate

`POST /v1/assistant/trips/{tripId}/parcels/qr-scan`

Body:

```json
{ "parcelCode": "VR-PCL-20260821-ABCD2345" }
```

Regex code chấp nhận:

```regex
^(?:VR-PCL-\d{8}-[A-HJ-NP-Z2-9]{8}|VRP-\d{8}-[A-Z0-9]{8})$
```

Không gửi `Idempotency-Key`; endpoint là query dù dùng POST. Success `200`: `AssistantParcelActionResponse`, `createdCustodyEvent=null`. Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND` nếu code không thuộc trip/operator; `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/assistant/trips/$TRIP_ID/parcels/qr-scan" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" --data '{"parcelCode":"VR-PCL-20260821-ABCD2345"}'
```

```js
const scanned = await fetch(`${BASE_URL}/v1/assistant/trips/${tripId}/parcels/qr-scan`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ parcelCode }) }).then(r => r.json());
```

## 7. Check-in, reweigh, load, unload và deliver

### 7.1. Check-in

`POST /v1/assistant/parcels/{parcelId}/check-in`

```json
{ "tripId": "11111111-1111-4111-8111-111111111111", "parcelCode": "VR-PCL-20260821-ABCD2345", "photoUrls": [] }
```

`tripId`, `parcelId`, `parcelCode` không rỗng. `photoUrls` tối đa 3; mỗi URL phải là Firebase evidence URL sở hữu bởi operator/actor/parcel theo prefix được backend tính. Chỉ assigned assistant. Parcel phải ở status cho phép handler (`RESERVED` theo transition repository), còn hạn check-in.

Success `200`: action response. Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 INVALID_STATUS`, `PARCEL_CHECK_IN_CLOSED`, `RACE_LOST`; `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/check-in" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"tripId\":\"$TRIP_ID\",\"parcelCode\":\"$PARCEL_CODE\",\"photoUrls\":[]}"
```

```js
const checkedIn = await mutate(`/v1/assistant/parcels/${parcelId}/check-in`, { tripId, parcelCode, photoUrls: [] });
```

### 7.2. Reweigh

`POST /v1/assistant/parcels/{parcelId}/reweigh`

```json
{ "actualLengthCm": 35, "actualWidthCm": 25, "actualHeightCm": 8, "actualWeightKg": 2.6 }
```

Tất cả number `>0`. Chỉ assigned assistant; Parcel phải `CHECKED_IN`; chưa qua `loadCutoffAt`. Backend tính size/chargeable weight/giá/balance/refund và remeasure cargo.

Success `200`: `ReweighParcelResponse` mục 5.2. Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 INVALID_STATUS`, `PARCEL_LOAD_CUTOFF_PASSED`, `TRIP_CARGO_STATE_INVALID`, `RACE_LOST`; `503 TRIP_NOT_FOUND`, `TRIP_SERVICE_UNAVAILABLE`. Khi cargo vượt sức chứa, handler không throw ngay: transition có thể đưa Parcel vào pending operator action và response status phản ánh state đó.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/reweigh" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"actualLengthCm":35,"actualWidthCm":25,"actualHeightCm":8,"actualWeightKg":2.6}'
```

```js
const reweighed = await mutate(`/v1/assistant/parcels/${parcelId}/reweigh`, { actualLengthCm: 35, actualWidthCm: 25, actualHeightCm: 8, actualWeightKg: 2.6 });
```

### 7.3. Load

`POST /v1/assistant/parcels/{parcelId}/load`

```json
{ "tripId": "11111111-1111-4111-8111-111111111111", "parcelCode": "VR-PCL-20260821-ABCD2345" }
```

DTO disallow unknown JSON fields. Parcel phải đúng operator/trip/code, assigned assistant và status `READY_TO_LOAD`. Success action response; custody append `LOADED` tại `ORIGIN_STATION`.

Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 INVALID_STATUS`; `422 VALIDATION_ERROR`/idempotency; `503 TRIP_NOT_FOUND`, `TRIP_CARGO_CAPACITY_EXCEEDED`, `TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/load" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"tripId\":\"$TRIP_ID\",\"parcelCode\":\"$PARCEL_CODE\"}"
```

```js
const loaded = await mutate(`/v1/assistant/parcels/${parcelId}/load`, { tripId, parcelCode });
```

### 7.4. Unload đúng location

`POST /v1/assistant/parcels/{parcelId}/unload`

```json
{
  "parcelCode": "VR-PCL-20260821-ABCD2345",
  "actualLocation": { "kind": "ROUTE_STOP", "id": "66666666-6666-4666-8666-666666666666" },
  "photoUrls": []
}
```

Nếu Parcel không có `dropoffStopId`, `kind` phải `DESTINATION_STATION` và `id` phải bằng trip destination station. Nếu có stop, `kind=ROUTE_STOP`, ID đúng `dropoffStopId`; stop cho dropoff; operational current stop đúng, status `ARRIVED`, chưa `actualDepartureAt`. Parcel phải `IN_TRANSIT`, QR bắt buộc và đúng, assistant assigned.

Success action response; cargo release và custody `UNLOADED` trong cùng transaction.

Lỗi riêng đầy đủ từ handler:

- 403 `FORBIDDEN`.
- 404 `PARCEL_NOT_FOUND`, `TRIP_NOT_FOUND`.
- 409 `SCAN_IDENTITY_MISMATCH`, `INVALID_STATUS`, `PARCEL_CUSTODY_LOCATION_MISMATCH`.
- 422 `PARCEL_SCAN_REQUIRED`, `PARCEL_CUSTODY_LOCATION_REQUIRED`, `DROP_OFF_STOP_NOT_FOUND`, `DROP_OFF_STOP_NOT_ALLOWED`, `DROP_OFF_STOP_NOT_ARRIVED`, `DESTINATION_TERMINAL_NOT_ARRIVED`.
- 503 `TRIP_SERVICE_UNAVAILABLE`, `TRIP_CARGO_CAPACITY_EXCEEDED`.

Khi mismatch, **không** cập nhật status, **không** release cargo. FE đọc `error.fields.requiredAction`, không cho “force unload” qua endpoint này.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/unload" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"parcelCode\":\"$PARCEL_CODE\",\"actualLocation\":{\"kind\":\"ROUTE_STOP\",\"id\":\"$STOP_ID\"},\"photoUrls\":[]}"
```

```js
const unloaded = await mutate(`/v1/assistant/parcels/${parcelId}/unload`, { parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: stopId }, photoUrls: [] });
```

### 7.5. Deliver

`POST /v1/assistant/parcels/{parcelId}/deliver`

Body optional; nếu gửi:

```json
{ "photoUrls": [] }
```

Tối đa 3 Firebase evidence URLs với owned prefix. Chỉ assigned assistant; Parcel phải `UNLOADED`. Handler chuyển `DELIVERED_PENDING_CONFIRM`, append custody `HANDOFF`, revoke token cũ và gửi delivery link mới nếu `recipientEmail` có giá trị.

Success action response. Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 INVALID_STATUS`; `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE` và delivery-email dependency errors.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/deliver" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"photoUrls":[]}'
```

```js
const delivered = await mutate(`/v1/assistant/parcels/${parcelId}/deliver`, { photoUrls: [] });
```

### 7.6. Assistant manual confirm

`POST /v1/assistant/parcels/{parcelId}/confirm-delivery`

Body hỗ trợ alias cũ nhưng FE chỉ nên gửi `confirmNote`:

```json
{ "confirmNote": "Đã đối chiếu giấy tờ người nhận", "note": null }
```

`ResolveNote()` chọn `confirmNote`, sau đó `note`, sau đó empty. Validator yêu cầu resolved note không blank và tối đa 500 ký tự. Parcel phải `DELIVERED_PENDING_CONFIRM`; same actor + same note replay được trả success.

Success `{parcelId,status,confirmedAt}`. Lỗi: `400 PARCEL_NOT_PENDING_CONFIRM`; `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 RESOURCE_CONFLICT`; `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/confirm-delivery" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"confirmNote":"Đã đối chiếu giấy tờ người nhận","note":null}'
```

```js
const confirmed = await mutate(`/v1/assistant/parcels/${parcelId}/confirm-delivery`, { confirmNote: 'Đã đối chiếu giấy tờ người nhận', note: null });
```

Helper `mutate` dùng cho các ví dụ trên:

```js
async function mutate(path, body) {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
    body: JSON.stringify(body),
  });
  return response.json();
}
```

## 8. Custody exception, custody scan và reconciliation

### 8.1. Custody exception — local/source only

`POST /v1/assistant/parcels/{parcelId}/custody-exception`

DTO disallow unknown fields.

```json
{
  "incidentType": "WRONG_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  "locationSnapshot": "Stop B",
  "temporaryExceptionTag": null,
  "description": "Đã dỡ nhầm tại Stop B",
  "observedWeightKg": 2.6,
  "evidenceUrls": ["https://<firebase-owned-evidence-url>"],
  "reason": "QR mismatch được phát hiện sau khi dỡ",
  "supervisorApprovalUserId": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
}
```

Rules: incident/location type phải là domain enum; `reason` bắt buộc, tối đa 1000; `description` tối đa 2000; temporary tag tối đa 100; weight nếu có `>0`; với actor role `ASSISTANT`, `supervisorApprovalUserId` bắt buộc. Handler xác nhận assignment, append `MANUAL_CUSTODY_EXCEPTION`, mở incident `SEARCHING`, tạo hai search tasks và đặt Parcel pending operator action.

Success action response có `activeIncident`, latest event và warning cố định. Lỗi: `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 PARCEL_INCIDENT_ALREADY_OPEN`; `422 VALIDATION_ERROR`; dependency errors.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/custody-exception" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"incidentType\":\"WRONG_STOP\",\"actualLocationType\":\"ROUTE_STOP\",\"actualLocationId\":\"$ACTUAL_STOP_ID\",\"locationSnapshot\":\"Stop B\",\"temporaryExceptionTag\":null,\"description\":\"Đã dỡ nhầm\",\"observedWeightKg\":2.6,\"evidenceUrls\":[],\"reason\":\"Dỡ nhầm stop\",\"supervisorApprovalUserId\":\"$SUPERVISOR_ID\"}"
```

```js
const exception = await mutate(`/v1/assistant/parcels/${parcelId}/custody-exception`, exceptionBody);
```

### 8.2. Custody scan — local/source only

`POST /v1/assistant/parcels/{parcelId}/custody-scan`

```json
{
  "parcelCode": "VR-PCL-20260821-ABCD2345",
  "eventType": "ARRIVED_AT_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  "locationSnapshot": "Stop B",
  "evidenceReferences": [],
  "reason": null
}
```

DTO disallow unknown fields. Direct scan chỉ cho event `ACCEPTED`, `ARRIVED_AT_STOP`, `HANDOFF`, `RETURNED_TO_STATION`; location type thuộc enum; ID bắt buộc trừ `VEHICLE`; QR đúng Parcel; assigned assistant.

Success action response. Lỗi: `404 PARCEL_NOT_FOUND`; `403 FORBIDDEN`; `409 SCAN_IDENTITY_MISMATCH`; `422 VALIDATION_ERROR`, `PARCEL_CUSTODY_LOCATION_REQUIRED`.

```bash
curl -X POST "$BASE_URL/v1/assistant/parcels/$PARCEL_ID/custody-scan" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"parcelCode\":\"$PARCEL_CODE\",\"eventType\":\"ARRIVED_AT_STOP\",\"actualLocationType\":\"ROUTE_STOP\",\"actualLocationId\":\"$STOP_ID\",\"locationSnapshot\":\"Stop B\",\"evidenceReferences\":[],\"reason\":null}"
```

```js
const custody = await mutate(`/v1/assistant/parcels/${parcelId}/custody-scan`, custodyBody);
```

### 8.3. Stop reconciliation — local/source only

`POST /v1/assistant/trips/{tripId}/stops/{stopId}/reconcile`

```json
{
  "scannedParcelIds": ["77777777-7777-4777-8777-777777777777"],
  "manualExceptionParcelIds": [],
  "departureOverrideReason": null,
  "supervisorApprovalUserId": null
}
```

Null arrays được chuyển thành empty. IDs khai báo phải thuộc expected manifest và đã có matching custody event tại đúng trip/stop. Trip operational location phải đúng stop, `ARRIVED`, chưa departed. Nếu unresolved, handler mở `UNSCANNED_HANDOFF` và search tasks. `canDepart=true` chỉ khi không unresolved hoặc có đồng thời nonblank override reason và supervisor ID.

Success `200`:

```json
{
  "expectedCount": 2,
  "scannedCount": 1,
  "manualExceptionCount": 0,
  "unresolvedParcels": [{
    "parcelId": "99999999-9999-4999-8999-999999999999",
    "parcelCode": "VR-PCL-20260821-EFGH5678",
    "photoUrl": null,
    "expectedDropoff": { "type": "ROUTE_STOP", "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "name": "Stop B", "orderIndex": 1, "eta": "2026-08-22T09:00:00+07:00" },
    "lastCustody": null,
    "incidentId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    "incidentType": "UNSCANNED_HANDOFF",
    "reason": "No verified unload or manual custody event exists for this stop.",
    "recommendedAction": "SEARCH_VEHICLE_OR_STATION"
  }],
  "canDepart": false,
  "requiresSupervisorApproval": true,
  "unresolvedParcelIds": ["99999999-9999-4999-8999-999999999999"]
}
```

`unresolvedParcelIds` là compatibility read-only alias; FE mới dùng `unresolvedParcels`.

Lỗi: `403 FORBIDDEN`; `404 STOP_NOT_FOUND`; `409 TRIP_SERVICE_UNAVAILABLE`, `PARCEL_CUSTODY_LOCATION_MISMATCH`, `PARCEL_CUSTODY_EVENT_NOT_FOUND`; idempotency errors.

```bash
curl -X POST "$BASE_URL/v1/assistant/trips/$TRIP_ID/stops/$STOP_ID/reconcile" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"scannedParcelIds":[],"manualExceptionParcelIds":[],"departureOverrideReason":null,"supervisorApprovalUserId":null}'
```

```js
const reconciliation = await mutate(`/v1/assistant/trips/${tripId}/stops/${stopId}/reconcile`, { scannedParcelIds, manualExceptionParcelIds, departureOverrideReason: null, supervisorApprovalUserId: null });
```

## 9. API dùng chung cho Driver/Assistant

### 9.1. Confirm transfer/forwarding

`POST /v1/crew/parcels/{parcelId}/confirm-transfer`

```json
{ "parcelCode": "VR-PCL-20260821-ABCD2345" }
```

Role `DRIVER` hoặc `ASSISTANT`, nhưng phải được Trip Service xác nhận assigned crew của **target trip** và cùng operator. Parcel phải `PENDING_TRANSFER_CONFIRM`; parcel code/target khớp; confirmation window là 30 phút. Handler transfer cargo, complete state, append `FORWARDED_OUT` trên leg cũ, start leg mới, append `FORWARDED_IN`.

Success `OperationalParcelResponse`. Lỗi thực tế: `404 PARCEL_NOT_FOUND`, `TRIP_NOT_FOUND`, `PARCEL_CARGO_NOT_FOUND`; `403 FORBIDDEN`; `409 PARCEL_NOT_TRANSFERABLE`, `PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED`, `TRIP_CARGO_TRANSFER_CONFLICT`; `422 TRIP_CARGO_CAPACITY_EXCEEDED`; `503 TRIP_SERVICE_UNAVAILABLE`.

```bash
curl -X POST "$BASE_URL/v1/crew/parcels/$PARCEL_ID/confirm-transfer" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data "{\"parcelCode\":\"$PARCEL_CODE\"}"
```

```js
const transfer = await mutate(`/v1/crew/parcels/${parcelId}/confirm-transfer`, { parcelCode });
```

### 9.2. Crew manual confirm

`POST /v1/crew/parcels/{parcelId}/manual-confirm`

Body/validation/response/error giống mục 7.6; role `DRIVER` hoặc `ASSISTANT` và phải assigned crew. Dùng `confirmNote`, tối đa 500.

```bash
curl -X POST "$BASE_URL/v1/crew/parcels/$PARCEL_ID/manual-confirm" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" --data '{"confirmNote":"Đã đối chiếu người nhận","note":null}'
```

```js
const confirmed = await mutate(`/v1/crew/parcels/${parcelId}/manual-confirm`, { confirmNote: 'Đã đối chiếu người nhận', note: null });
```

### 9.3. Resend delivery email

`POST /v1/crew/parcels/{parcelId}/resend-delivery-email`

Không có body. Parcel phải cùng operator, caller assigned crew, status phù hợp delivery confirmation, có recipient email. Success `{parcelId,status,expiresAt}`.

Lỗi: `400 PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED`, `400 PARCEL_NOT_PENDING_CONFIRM`; `403 FORBIDDEN`; `404 PARCEL_NOT_FOUND`; `409 RESOURCE_CONFLICT`; `422 PARCEL_RECIPIENT_EMAIL_REQUIRED`, `422 VALIDATION_ERROR`; `503 TRIP_SERVICE_UNAVAILABLE` hoặc email dependency.

```bash
curl -X POST "$BASE_URL/v1/crew/parcels/$PARCEL_ID/resend-delivery-email" -H "Authorization: Bearer $ACCESS_TOKEN" -H "Idempotency-Key: $(uuidgen)" --data ''
```

```js
const resent = await fetch(`${BASE_URL}/v1/crew/parcels/${parcelId}/resend-delivery-email`, { method: 'POST', headers: { Authorization: `Bearer ${accessToken}`, 'Idempotency-Key': crypto.randomUUID() } }).then(r => r.json());
```

## 10. Flow xử lý đúng/sai QR và giao nhầm stop

### Happy path tại stop

1. Load manifest một lần; lấy `currentOperationalLocation`, `orderedStops`, card actions.
2. QR scan để resolve card; scan không mutate.
3. Chỉ gọi `unload` nếu `availableActions` có `UNLOAD` và UI hiển thị expected stop trùng current stop.
4. Dùng response mutation cập nhật card/store, không refetch manifest.
5. Trước departure gọi `reconcile`; chỉ cho UI complete stop khi `canDepart=true`.

### QR sai hoặc stop sai

- `SCAN_IDENTITY_MISMATCH`: giữ hàng, mở identity hints; không thử parcel ID khác tự động.
- `PARCEL_CUSTODY_LOCATION_MISMATCH`: hiển thị expected/actual stop và `requiredAction`. Nếu hàng còn trên xe, giữ trên xe. Nếu đã dỡ vật lý, supervisor phê duyệt rồi gọi custody-exception `WRONG_STOP`.
- QR đúng nhưng kiện vật lý không khớp ảnh/cân nặng: không gọi unload/deliver; gọi custody-exception `PACKAGE_IDENTITY_MISMATCH`.
- Không đọc được QR: không gọi unload thường. Gọi custody-exception với temporary tag/evidence/supervisor. Nếu không biết Parcel ID thì workflow đăng ký unidentified package thuộc Operator/Station API, không nằm trong role Assistant hiện tại.
- Reconciliation unresolved không đồng nghĩa lost ngay; incident/search diễn ra trước `LOST_CONFIRMED`.

## 11. Checklist cho AI agent Driver FE

- Tách route guard theo role: `ASSISTANT` có toàn bộ operational flow; `DRIVER` chỉ có `/v1/crew/parcels/*` trong Parcel service.
- Dùng manifest screen model mới, không N+1 gọi detail/trace cho từng card.
- Sau mutation, merge `parcelState`, `currentCustody`, `activeIncident`, `createdCustodyEvent`, `availableActions`, `warning` vào state.
- Không tự suy diễn action từ `status`; backend là nguồn truth qua `availableActions`.
- Show ảnh/mô tả/cân nặng/kích thước từ `identityCheckHints` trước load/unload.
- Luôn scan QR cho normal unload; không cung cấp nút bỏ qua scan.
- Parse structured `error.fields`; không hiển thị dữ liệu của parcel ngoài tenant/trip vì backend chủ động trả not-found/forbidden.
- Giữ UUID v4 idempotency key khi retry cùng thao tác; tạo key mới cho thao tác mới.
- Feature-flag contract mới trên production cho đến khi deployment spec có action responses và ba Reliability routes.
- Contract/E2E FE tối thiểu: manifest one-call, QR invalid format, load response merge, old ARRIVED-but-DEPARTED unload rejection, wrong-stop exception, no-scan flow, reconcile unresolved, transfer confirm, manual confirm/token email.

## 12. Đối chiếu source

- Đủ 14 public Driver/Assistant operations trong local OpenAPI.
- Method/path/role đối chiếu cả Gateway và controller.
- Body fields đối chiếu request records, `JsonUnmappedMemberHandling.Disallow` và validators/manual guards.
- Response fields đối chiếu local OpenAPI components và response records.
- Error code đối chiếu handlers, dependency exception mapping và common filters.
- Production spec được so sánh trực tiếp; response contract cũ/missing routes đã ghi ở mục 1.

⚠️ TODO: `RegisterUnidentifiedPackage` hiện chỉ dành cho `OPERATOR_ADMIN,OPERATOR_STAFF`, không có endpoint tương đương cho `ASSISTANT`. Mobile Assistant cần phối hợp Operator Web/Station khi gặp kiện hoàn toàn không xác định.
