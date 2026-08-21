# Parcel API — Operator/Admin Web

> Tài liệu sinh từ source code tại ngày 2026-08-21. Đối tượng: AI agent/FE phụ trách Operator/Admin Web. Tài liệu chỉ mô tả contract đang tồn tại trong source; nơi deployment production chưa khớp source được đánh dấu rõ.

## Mục lục

- [1. Nguồn sự thật và deployment](#1-nguồn-sự-thật-và-deployment)
- [2. Base URL, xác thực và headers](#2-base-url-xác-thực-và-headers)
- [3. Quy ước response, lỗi và dữ liệu](#3-quy-ước-response-lỗi-và-dữ-liệu)
- [4. Tổng quan 41 endpoint](#4-tổng-quan-41-endpoint)
- [5. Các read model FE cần dùng](#5-các-read-model-fe-cần-dùng)
- [6. Incident và forwarding](#6-incident-và-forwarding)
- [7. Claim và compensation policy](#7-claim-và-compensation-policy)
- [8. Quản lý Parcel vận hành](#8-quản-lý-parcel-vận-hành)
- [9. Route fare, stats và report](#9-route-fare-stats-và-report)
- [10. Unidentified package và station handoff](#10-unidentified-package-và-station-handoff)
- [11. Flow UI và trách nhiệm của AI agent Operator FE](#11-flow-ui-và-trách-nhiệm-của-ai-agent-operator-fe)
- [12. Đối chiếu source](#12-đối-chiếu-source)

## 1. Nguồn sự thật và deployment

Contract chuẩn của tài liệu này được đối chiếu từ controller, request record, FluentValidation/handler guard, Domain enum, response record, middleware auth/idempotency, Gateway route và local OpenAPI.

| Môi trường | Base URL | Swagger/OpenAPI |
|---|---|---|
| Local qua Gateway | `http://localhost:3000` | `http://localhost:3000/api-specs/parcel` |
| Local gọi thẳng Parcel Service | `http://localhost:5005` | `http://localhost:5005/swagger/v1/swagger.json` |
| Production | `https://api.vietride.online` | UI: `https://api.vietride.online/docs`; spec: `https://api.vietride.online/api-specs/parcel` |
| Staging | ⚠️ TODO: cần xác nhận thêm | Không có base URL staging trong config đã quét |

Local OpenAPI hiện có 75 public operations. Production spec đang cũ hơn source: toàn bộ nhóm Reliability mới gồm incident queue/detail/actions, claim queue/detail/decision, forwarding options, compensation policy, unidentified package và station handoff chưa xuất hiện trên production. Không nối các route này vào production FE cho đến khi deployment spec đã được cập nhật.

## 2. Base URL, xác thực và headers

### 2.1. Token người dùng

- Access token là JWT `RS256`, issuer `vietride-identity`, audience `vietride-api`.
- Gateway đọc `Authorization: Bearer <accessToken>` rồi chuyển user context bằng Internal JWT; FE không gửi `X-Internal-Auth`.
- `.env` hiện cấu hình access-token TTL 15 phút; auth validation cho phép clock skew 5 giây.
- Role dùng trong tài liệu: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.
- Operator token phải có operator scope và `operatorStatus=APPROVED`. Thiếu/không hợp lệ trả `401 AUTH_TOKEN_INVALID`; operator suspended trả `403 OPERATOR_SUSPENDED`.

Khai báo biến shell một lần trước các lệnh curl:

```bash
BASE_URL="http://localhost:3000"
ACCESS_TOKEN="<RS256-access-token>"
IDEMPOTENCY_KEY="$(uuidgen)"
```

Nếu môi trường Windows không có `uuidgen`, dùng một UUID v4 do application tạo và gán trực tiếp cho `IDEMPOTENCY_KEY`.

Lấy token:

```bash
curl -X POST "$BASE_URL/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@operator.vn","password":"YourPassword"}'
```

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresInSeconds": 900,
    "user": {}
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Khi access token hết hạn, gọi đúng một lần:

```http
POST /v1/auth/refresh
Content-Type: application/json

{"refreshToken":"..."}
```

Nếu refresh thất bại, xóa session và đưa người dùng về login; không retry vô hạn.

### 2.2. Headers

| Header | Khi nào | Giá trị |
|---|---|---|
| `Authorization` | Tất cả endpoint trong tài liệu | `Bearer <accessToken>` |
| `Content-Type` | Mutation có body | `application/json` |
| `Idempotency-Key` | Tất cả `POST`, `PUT`, `PATCH` bên dưới | UUID v4, ví dụ `52bc34c4-8052-4c14-8de8-2971730e69ef` |
| `Accept` | Read JSON | `application/json` |

Middleware Parcel bật `requireAllMutations=true`. Thiếu key trả `422 IDEMPOTENCY_KEY_REQUIRED`; key không phải UUID v4 trả `422 VALIDATION_ERROR`; cùng key nhưng request fingerprint khác trả `422 IDEMPOTENCY_KEY_MISMATCH`; request cùng key đang xử lý trả `409 IDEMPOTENCY_REQUEST_PENDING`. Cache kết quả giữ 86.400 giây. Retry cùng thao tác phải giữ nguyên key; thao tác mới phải sinh key mới.

### 2.3. Role matrix

| Nhóm | `OPERATOR_ADMIN` | `OPERATOR_STAFF` |
|---|:---:|:---:|
| Incident read/actions/forward | Có | Có |
| Claim list/detail | Có | Có |
| Claim decision | Có | Không |
| Compensation policy GET | Có | Có |
| Compensation policy PUT | Có | Không |
| Parcel list/detail/operations | Có | Có |
| Override capacity | Có | Chỉ khi JWT có permission `CAN_OVERRIDE_CAPACITY` |
| Route fare read | Có | Có |
| Route fare mutation | Có | Không |
| Parcel stats | Có | Không |
| Reports | Có | Có |
| Unidentified/station | Có | Có |

## 3. Quy ước response, lỗi và dữ liệu

### 3.1. Wrapper JSON

Mọi response JSON thành công dùng `ApiResponse<T>`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": { "traceId": "00-...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Response lỗi:

```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid paging values.",
    "fields": [{ "field": "pageSize", "message": "..." }]
  },
  "meta": { "traceId": "00-...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

`fields` chỉ xuất hiện khi exception có field-level errors. File CSV/XLSX thành công là binary/file response, không bọc `ApiResponse`; lỗi của chúng vẫn dùng wrapper.

| Field wrapper | Kiểu | Ý nghĩa |
|---|---|---|
| `success` | boolean | `true` cho success, `false` cho error |
| `statusCode` | int | HTTP status được lặp lại trong body |
| `message` | string? | Chỉ success model có field optional này; bị bỏ khi null |
| `data` | T | Payload thành công; không có ở error envelope |
| `error.code` | string | Mã UPPER_SNAKE_CASE để FE branch logic |
| `error.message` | string | Thông điệp an toàn từ backend |
| `error.fields` | `{field,message}[]?` | Chi tiết validation; bị bỏ khi null |
| `meta.traceId` | string | Correlation/trace ID gửi cho BE khi support |
| `meta.timestamp` | datetime | Thời điểm tạo envelope; route `/v1` trình bày offset `+07:00` |

### 3.2. Pagination

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalItems": 0,
  "totalPages": 0,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

`page` tối thiểu 1; `pageSize` từ 1 đến 100 ở các list trong tài liệu.

### 3.3. Format và enums

- JSON field dùng `camelCase`.
- `Guid` là UUID dạng `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`.
- `DateTimeOffset` trả theo convention `/v1` với offset `+07:00`; gửi timestamp ISO-8601.
- `DateOnly` dùng `YYYY-MM-DD` và được hiểu theo lịch `Asia/Ho_Chi_Minh` trong report/stats.
- Tiền là JSON integer `int64`, đơn vị VND; không parse bằng floating point.
- `decimal` dùng JSON number.
- Unknown query parameter bị chặn trên endpoint có `AllowedQueryParameters`; các endpoint không gắn filter này vẫn để ASP.NET bỏ qua query dư.

Domain enums tồn tại trong source; dòng `SlaState` là tập string được handler kiểm tra trực tiếp (không phải C# enum):

```text
ParcelStatus = PENDING_OPERATOR_REVIEW | PENDING_PAYMENT | PENDING |
  PENDING_ADDITIONAL_PAYMENT | RESERVED | CHECKED_IN | PENDING_FINAL_PAYMENT |
  READY_TO_LOAD | LOADED | IN_TRANSIT | PENDING_TRANSFER_CONFIRM |
  TRANSFER_ESCALATED | UNLOADED | DELIVERED_PENDING_CONFIRM |
  DELIVERY_CONFIRMED | DELIVERY_REJECTED | RETURN_INITIATED | RETURNED |
  PENDING_OPERATOR_ACTION | CANCELLED | REJECTED | EXPIRED

PendingActionType = CAPACITY_EXCEEDED | RESERVE_FAILED | REFUND_CONFIRMATION |
  CUSTODY_EXCEPTION
ParcelSizeCategory = SMALL | MEDIUM | LARGE | EXTRA_LARGE
ParcelIncidentType = MISSING | WRONG_STOP | DELIVERY_NOT_RECEIVED | PARTIAL_LOSS |
  DAMAGED | SCAN_IDENTITY_MISMATCH | PACKAGE_IDENTITY_MISMATCH |
  UNSCANNED_HANDOFF | MISSING_AFTER_DEPARTURE
ParcelIncidentStatus = OPEN | SEARCHING | FOUND | FORWARDING | RESOLVED | CLOSED |
  ESCALATED | SEARCH_EXPIRED | LOST_CONFIRMED
ParcelClaimStatus = SUBMITTED | UNDER_REVIEW | APPROVED | FUNDING_PENDING | PAID |
  REJECTED | CANCELLED | APPEALED
ParcelCustodyLocationType = ORIGIN_STATION | DESTINATION_STATION | ROUTE_STOP |
  VEHICLE | WAREHOUSE
UnidentifiedParcelPackageStatus = UNIDENTIFIED | MATCHED | FORWARDED | RETURNED
SlaState = ON_TRACK | DUE_SOON | BREACHED | CLOSED
```

### 3.4. Lỗi chung có thể đến trước handler

| HTTP | `error.code` | Nguyên nhân thực tế trong middleware/filter |
|---:|---|---|
| 422 | `IDEMPOTENCY_KEY_REQUIRED` | Mutation thiếu header |
| 422 | `VALIDATION_ERROR` | `Idempotency-Key` không phải UUID v4 |
| 422 | `VALIDATION_ERROR` | Query key dư; JSON malformed; Guid/date/enum sai kiểu; model/handler validation |
| 401 | `AUTH_TOKEN_INVALID` | Token thiếu, hết hạn, chữ ký/issuer/audience sai |
| 403 | `FORBIDDEN` | Sai role, thiếu operator scope hoặc resource khác tenant |
| 403 | `OPERATOR_SUSPENDED` | Operator không ở trạng thái được phép |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Reuse key cho method/path/caller/payload khác |
| 409 | `IDEMPOTENCY_REQUEST_PENDING` | Request cùng key vẫn đang chạy |
| 502 | `UPSTREAM_UNAVAILABLE` | Gateway không kết nối được Parcel service |
| 503 | `UPSTREAM_UNAVAILABLE` hoặc mã dependency cụ thể | Parcel handler không lấy được dữ liệu bắt buộc từ Trip/Identity/Payment |
| 500 | `INTERNAL_ERROR` | Exception không được mapping thành coded exception |

⚠️ Rate-limit config của Gateway là 120 request/phút, nhưng raw proxy middleware được đăng ký trước Nest router. Source hiện không đủ bằng chứng để khẳng định limiter bao phủ các route proxy Parcel; FE không nên dựa vào con số này như contract. Cần xác nhận bằng integration test Gateway nếu muốn hiển thị quota.

## 4. Tổng quan 41 endpoint

| # | Method | Path | Role | Mô tả |
|---:|---|---|---|---|
| 1 | GET | `/v1/operator/parcel-incidents` | Admin/Staff | Incident queue screen-ready |
| 2 | GET | `/v1/operator/parcel-incidents/{incidentId}` | Admin/Staff | Incident detail + tasks + custody |
| 3 | POST | `/v1/operator/parcel-incidents/{incidentId}/assign` | Admin/Staff | Gán search tasks |
| 4 | POST | `/v1/operator/parcel-incidents/{incidentId}/search-scan` | Admin/Staff | Ghi kết quả search task |
| 5 | POST | `/v1/operator/parcel-incidents/{incidentId}/mark-found` | Admin/Staff | Xác nhận tìm thấy |
| 6 | GET | `/v1/operator/parcel-incidents/{incidentId}/forwarding-options` | Admin/Staff | Chuyến forwarding phù hợp |
| 7 | POST | `/v1/operator/parcel-incidents/{incidentId}/forward` | Admin/Staff | Tạo forwarding leg |
| 8 | POST | `/v1/operator/parcel-incidents/{incidentId}/resolve` | Admin/Staff | Kết thúc incident found/forwarding |
| 9 | POST | `/v1/operator/parcel-incidents/{incidentId}/declare-lost` | Admin/Staff | Xác nhận mất sau SLA |
| 10 | GET | `/v1/operator/claims` | Admin/Staff | Claim queue screen-ready |
| 11 | GET | `/v1/operator/claims/{claimId}` | Admin/Staff | Claim detail |
| 12 | POST | `/v1/operator/claims/{claimId}/decision` | Admin | Duyệt/từ chối claim |
| 13 | GET | `/v1/operator/policies/parcel-compensation` | Admin/Staff | Đọc policy bồi thường |
| 14 | PUT | `/v1/operator/policies/parcel-compensation` | Admin | Tạo/cập nhật policy |
| 15 | GET | `/v1/operator/parcels` | Admin/Staff | Danh sách Parcel enrich |
| 16 | GET | `/v1/operator/parcels/{parcelId}` | Admin/Staff | Parcel detail |
| 17 | PATCH | `/v1/operator/parcels/{parcelId}/review` | Admin/Staff | Review đơn |
| 18 | POST | `/v1/operator/parcels/{parcelId}/request-transfer` | Admin/Staff | Yêu cầu chuyển trip |
| 19 | POST | `/v1/operator/parcels/{parcelId}/return` | Admin/Staff | Return parcel |
| 20 | POST | `/v1/operator/parcels/{parcelId}/cancel` | Admin/Staff | Hủy thủ công trước load |
| 21 | POST | `/v1/operator/parcels/{parcelId}/confirm-refund` | Admin/Staff | Xác nhận refund pending |
| 22 | POST | `/v1/operator/parcels/{parcelId}/override-capacity` | Admin/Staff có permission | Override cargo capacity |
| 23 | POST | `/v1/operator/parcels/{parcelId}/confirm-delivery` | Admin/Staff | Manual delivery confirm |
| 24 | POST | `/v1/operator/parcels/{parcelId}/manual-confirm` | Admin/Staff | Alias/manual delivery confirm |
| 25 | POST | `/v1/operator/parcels/{parcelId}/resend-delivery-email` | Admin/Staff | Gửi lại email token |
| 26 | PATCH | `/v1/operator/parcels/{parcelId}/status` | Admin/Staff | Override duy nhất sang RETURNED |
| 27 | GET | `/v1/operator/parcels/reports/summary` | Admin/Staff | Summary vận hành/doanh thu |
| 28 | GET | `/v1/operator/parcels/reports/export` | Admin/Staff | Export CSV summary |
| 29 | POST | `/v1/operator/parcel-route-fares` | Admin | Tạo fare |
| 30 | GET | `/v1/operator/parcel-route-fares` | Admin/Staff | Fare list |
| 31 | GET | `/v1/operator/parcel-route-fares/summary` | Admin/Staff | Coverage theo route |
| 32 | PATCH | `/v1/operator/parcel-route-fares/{routeId}/{sizeCategory}` | Admin | Sửa fare |
| 33 | PUT | `/v1/operator/parcel-route-fares/{routeId}/batch` | Admin | Upsert 1–4 size fare |
| 34 | GET | `/v1/operator/parcel-stats` | Admin | Chart count theo status/route |
| 35 | GET | `/v1/operator/reports/parcels/export` | Admin/Staff | Export XLSX chi tiết |
| 36 | GET | `/v1/operator/unidentified-packages` | Admin/Staff | Queue kiện chưa định danh |
| 37 | GET | `/v1/operator/unidentified-packages/{packageId}` | Admin/Staff | Chi tiết kiện chưa định danh |
| 38 | GET | `/v1/operator/unidentified-packages/{packageId}/match-candidates` | Admin/Staff | Candidate ghép Parcel |
| 39 | POST | `/v1/stations/parcels/unidentified` | Admin/Staff | Đăng ký kiện chưa định danh |
| 40 | POST | `/v1/stations/parcels/unidentified/{packageId}/match` | Admin/Staff | Supervisor xác nhận ghép |
| 41 | POST | `/v1/stations/parcels/{parcelId}/handoff` | Admin/Staff | Ghi station custody handoff |

## 5. Các read model FE cần dùng

Phần này giải thích field một lần; mỗi endpoint bên dưới dẫn đến đúng model tương ứng.

### 5.1. Reliability primitives

| Model | Field và kiểu |
|---|---|
| `ReliabilityParcelSummary` | `parcelId: uuid`, `parcelCode: string`, `status: string`, `description: string?`, `photoUrl: string?`, `quantity: int`, `declaredValueVnd: int64?` |
| `ReliabilityLocation` | `type: string?`, `id: uuid?`, `name: string?`, `orderIndex: int?`, `eta: datetime?` |
| `ReliabilityVehicle` | `vehicleId: uuid`, `licensePlate: string`, `status: string?` |
| `ReliabilityRoute` | `routeId: uuid`, `name: string`, `origin: ReliabilityLocation`, `destination: ReliabilityLocation` |
| `ReliabilityTrip` | `tripId: uuid`, `status: string?`, `departureAt: datetime?`, `eta: datetime?`, `route: ReliabilityRoute?`, `vehicle: ReliabilityVehicle?`, `stops: ReliabilityTripStop[]` |
| `ReliabilityTripStop` | `stopId`, `name`, `orderIndex`, `estimatedArrivalAt`, `status`, `actualArrivalAt?`, `actualDepartureAt?` |
| `ReliabilityCustodySummary` | `lastEventType`, `lastConfirmedLocation`, `lastConfirmedAt`, `currentTripId?`, `currentVehicleId?`, `trackingConfidence`, `hasTrackingGap` |
| `OperatorUserSummary` | `userId?`, `displayName?`, `phone?`, `email?`, `avatarUrl?`, `source?` |
| `ReliabilityIncidentSummary` | `incidentId`, `type`, `status`, `searchDeadline`, `nextUpdateAt?`, `slaState`, `operatorProcessBreach` |
| `ReliabilityClaimSummary` | `claimId`, `status`, `totalAwardVnd`, `decisionDeadline?`, `payoutDeadline?`, `slaState?` |

### 5.2. Incident list item

```json
{
  "incidentId": "00000000-0000-0000-0000-000000000101",
  "parcelId": "00000000-0000-0000-0000-000000000201",
  "operatorId": "00000000-0000-0000-0000-000000000301",
  "type": "WRONG_STOP",
  "status": "SEARCHING",
  "tripId": "00000000-0000-0000-0000-000000000401",
  "lastKnownLocation": "Bến B",
  "searchDeadline": "2026-08-24T10:00:00+07:00",
  "createdAt": "2026-08-21T10:00:00+07:00",
  "operatorProcessBreach": true,
  "parcel": { "parcelId": "00000000-0000-0000-0000-000000000201", "parcelCode": "VR-PCL-20260821-ABCD2345", "status": "PENDING_OPERATOR_ACTION", "description": "Thùng điện tử", "photoUrl": "https://cdn.example/p.jpg", "quantity": 1, "declaredValueVnd": 12000000 },
  "trip": { "tripId": "00000000-0000-0000-0000-000000000401", "status": "IN_PROGRESS", "departureAt": "2026-08-21T08:00:00+07:00", "eta": "2026-08-21T14:00:00+07:00", "route": null, "vehicle": null, "stops": [] },
  "expectedDropoff": { "type": "ROUTE_STOP", "id": "00000000-0000-0000-0000-000000000501", "name": "Bến C", "orderIndex": 3, "eta": "2026-08-21T12:00:00+07:00" },
  "lastCustody": { "lastEventType": "HANDOFF", "lastConfirmedLocation": { "type": "ROUTE_STOP", "id": "00000000-0000-0000-0000-000000000502", "name": "Bến B", "orderIndex": 2, "eta": null }, "lastConfirmedAt": "2026-08-21T10:05:00+07:00", "currentTripId": null, "currentVehicleId": null, "trackingConfidence": "CONFIRMED_SCAN", "hasTrackingGap": false },
  "reporter": { "userId": "00000000-0000-0000-0000-000000000601", "displayName": "Nguyễn A", "phone": "0900000000", "email": null, "avatarUrl": null, "source": "USER" },
  "taskSummary": { "completed": 1, "total": 7, "assignees": [] },
  "claimSummary": null,
  "sla": { "deadline": "2026-08-24T10:00:00+07:00", "remainingMinutes": 4200, "state": "ON_TRACK" },
  "availableActions": ["ASSIGN", "RECORD_SEARCH", "MARK_FOUND"]
}
```

Các field nullable phản ánh upstream enrichment có thể không trả snapshot. FE không gọi N+1 để bù dữ liệu; hiển thị fallback từ field Parcel có sẵn.

### 5.3. Incident detail

`ParcelIncidentDetailResponse` gồm:

- `incident`: item ở §5.2.
- `searchTasks[]`: `taskId`, `incidentId`, `taskType`, `status`, `assigneeId?`, `location?`, `deadline`, `result?`, `completedAt?`, `assignee?`.
- `expectedLocation?`, `resolutionCode?`, `resolutionNote?`, `resolvedAt?`.
- `currentCustody?`: `lastEventType`, `lastLocationType?`, `lastLocationId?`, `lastLocationSnapshot?`, `lastConfirmedAt`, `currentTripId?`, `currentVehicleId?`, `trackingConfidence`. Đây là shape phẳng, khác `ReliabilityCustodySummary.lastConfirmedLocation` ở list.
- `custodyTimeline`: `{items, nextCursor}`; mỗi item có `eventId`, `eventType`, `legId?`, `tripId?`, expected/actual location type/id, `locationSnapshot?`, `vehicleId?`, `actorId?`, `actorRole`, `occurredAt`, `recordedAt`, `source`, `evidenceReferences[]`, `reason?`, `sequence`.
- `claim?`: full claim §5.4.
- `parcel?`, `sender?`, `recipient?`, `trip?`, `expectedDropoff?`, `reporter?`, `forwardingSummary?`, `availableActions?`.
- `forwardingOperation?`: `targetTrip`, `newLeg?`, `cargoTransferStatus`, `nextHandoffAction`; leg có `legId`, `tripId`, `sequence`, `status`, expected origin/destination id/name, `vehicleId?`, `startedAt?`, `endedAt?`.

### 5.4. Claim models

Full `ParcelClaimResponse`:

```json
{
  "claimId": "00000000-0000-0000-0000-000000000701",
  "parcelId": "00000000-0000-0000-0000-000000000201",
  "incidentId": "00000000-0000-0000-0000-000000000101",
  "status": "SUBMITTED",
  "declaredValueVnd": 12000000,
  "provenDirectLossVnd": null,
  "compensationRatePercent": 50,
  "policyCapVnd": 30000000,
  "cargoAwardVnd": 0,
  "freightRefundVnd": 0,
  "totalAwardVnd": 0,
  "policyVersion": 1,
  "beneficiaryUserId": "00000000-0000-0000-0000-000000000801",
  "decisionReason": null,
  "decidedBy": null,
  "decidedAt": null,
  "payoutReferenceId": null,
  "paidAt": null,
  "appealReason": null,
  "appealedByUserId": null,
  "appealedAt": null,
  "evidence": [{ "evidenceId": "00000000-0000-0000-0000-000000000901", "evidenceType": "INVOICE", "reference": "https://cdn.example/invoice.pdf", "note": "Hóa đơn", "uploadedByUserId": "00000000-0000-0000-0000-000000000801", "createdAt": "2026-08-21T11:00:00+07:00" }],
  "parcelSummary": null,
  "incidentSummary": null,
  "policySnapshot": { "version": 1, "compensationRatePercent": 50, "maxCompensationVnd": 30000000, "noProofFallbackMultiplier": 4, "claimWindowDays": 30, "searchSlaHours": 72, "decisionSlaBusinessDays": 7, "payoutSlaBusinessDays": 3 },
  "decisionDeadline": "2026-09-01T11:00:00+07:00",
  "payoutDeadline": null,
  "availableActions": ["DECIDE_CLAIM"]
}
```

Claim list row có `claimId`, `status`, `parcel`, `sender`, `incident?`, `evidenceCount`, `policySnapshot`, `cargoAwardVnd`, `freightRefundVnd`, `totalAwardVnd`, `deadline?`, `slaState?`, `fundingStatus`, `trip?`, `availableActions[]`. Claim detail có `claim`, `parcel`, `incident?`, `currentCustody?`, `trip?`, `expectedDropoff?`, `beneficiary`, `fundingStatus`, `availableActions[]`.

### 5.5. Operator Parcel list/detail

List item field đúng source:

```text
parcelId, parcelCode, status, tripId, senderUserId, recipientName?, recipientPhone?,
estimatedSizeCategory, actualSizeCategory?, estimatedChargeableWeightKg,
actualChargeableWeightKg?, depositRequiredVnd, depositPaidVnd, balanceRequiredVnd,
balancePaidVnd, refundDueVnd, forfeitedDepositVnd, latestCheckInAt?, loadCutoffAt?,
finalPaymentDeadline?, pendingActionType?, pendingActionReason?, photoUrl?, createdAt,
trip, route?, sender, recipient, sizeCategory, description?, estimatedWeightKg,
actualWeightKg?, estimatedVolumeM3, actualVolumeM3?, estimatedTotalPriceVnd,
finalTotalPriceVnd, discountAmountVnd, refundedAmountVnd, updatedAt
```

`trip` có `tripId`, `status?`, `departureAt?`, `arrivalEstimate?`, `vehicle? {vehicleId, licensePlate}`. `route` có `routeId`, `routeName`, `originStationName`, `destinationStationName`. `sender`/`recipient` có `userId?`, `displayName?`, `phone?`.

Detail chứa toàn bộ field list và thêm:

```text
operatorId, recipientUserId?, dropoffStopId?, senderEmail?, recipientEmail?,
checkInPhotoUrls?, deliveryPhotoUrls?, deliveryMethod, depositAmount,
originalDepositAmount, discountAmount, voucherCode?, voucherUsageId?, additionalAmount,
estimatedLengthCm, estimatedWidthCm, estimatedHeightCm, estimatedDimWeightKg,
actualLengthCm?, actualWidthCm?, actualHeightCm?, actualDimWeightKg?,
estimatedGrossPriceVnd, finalGrossPriceVnd, depositPercent, depositPaymentId?,
balancePaymentId?, checkedInAt?, checkedInByUserId?, reweighedAt?, reweighedByUserId?,
pricePerKgVnd, minimumPriceVnd, dimWeightFactor, settlementPolicyVersion,
loadedAt?, loadedByUserId?, unloadedAt?, deliveredPendingConfirmAt?, confirmedAt?,
confirmedByUserId?, rejectedAt?, pendingActionResumeStatus?, rejectionReason?,
cancellationReason?, reviewDecision?, reviewedAt?, reviewedByUserId?,
transferTargetTripId?, transferRequestedAt?, transferConfirmedAt?,
transferConfirmedByUserId?, returnReason?, returnedAt?, returnedByUserId?, statusHistory[]
```

`statusHistory[]` có `status`, `occurredAt`, `actorType`, `actorId?`, `source`, `reason?`.

### 5.6. Mutation response models

```json
{
  "operationalParcel": {
    "parcelId": "00000000-0000-0000-0000-000000000201",
    "parcelCode": "VR-PCL-20260821-ABCD2345",
    "status": "PENDING_TRANSFER_CONFIRM",
    "tripId": "00000000-0000-0000-0000-000000000401",
    "transferTargetTripId": "00000000-0000-0000-0000-000000000402",
    "transferConfirmedAt": null,
    "returnReason": null,
    "returnedAt": null,
    "refundChoice": null,
    "refundAmount": null
  },
  "reviewParcel": { "parcelId": "00000000-0000-0000-0000-000000000201", "parcelCode": "VR-PCL-20260821-ABCD2345", "status": "PENDING_PAYMENT", "depositRequiredVnd": 100000 },
  "manualConfirm": { "parcelId": "00000000-0000-0000-0000-000000000201", "status": "DELIVERY_CONFIRMED", "confirmedAt": "2026-08-21T12:00:00+07:00" },
  "resendEmail": { "parcelId": "00000000-0000-0000-0000-000000000201", "status": "DELIVERED_PENDING_CONFIRM", "expiresAt": "2026-08-22T12:00:00+07:00" }
}
```

Tên `operationalParcel`, `reviewParcel`… ở ví dụ trên chỉ là nhãn minh họa trong tài liệu; `data` thực tế là trực tiếp object tương ứng, không có lớp nhãn đó.

## 6. Incident và forwarding

Quy ước ví dụ JavaScript trong tài liệu:

```js
const BASE_URL = "http://localhost:3000";
const accessToken = "<access-token>";
const newKey = () => crypto.randomUUID();
const auth = { Authorization: `Bearer ${accessToken}` };
```

### 6.1. Danh sách incident

`GET {BASE_URL}/v1/operator/parcel-incidents`

Trả một page đã enrich đủ Parcel, trip/route/vehicle, expected stop, custody, reporter, task/claim/SLA và `availableActions`; FE không gọi detail cho từng row.

| Query | Kiểu | Bắt buộc | Default/rule |
|---|---|:---:|---|
| `status` | string enum | Không | Một `ParcelIncidentStatus` |
| `type` | string enum | Không | Một `ParcelIncidentType` |
| `search` | string | Không | Tối đa 100 ký tự; repository tìm snapshot và Identity tìm user |
| `tripId` | uuid | Không | — |
| `assigneeId` | uuid | Không | — |
| `slaState` | string enum | Không | `ON_TRACK`, `DUE_SOON`, `BREACHED`, `CLOSED` |
| `from` | datetime | Không | Inclusive |
| `to` | datetime | Không | Handler cộng 1 tick để inclusive |
| `page` | int | Không | `1`, tối thiểu 1 |
| `pageSize` | int | Không | `20`, từ 1 đến 100 |

Headers: `Authorization`, `Accept`. Không có body.

```bash
curl "$BASE_URL/v1/operator/parcel-incidents?status=SEARCHING&slaState=ON_TRACK&page=1&pageSize=20" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Accept: application/json"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents?status=SEARCHING&slaState=ON_TRACK&page=1&pageSize=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`; `data` là `PagedResult<IncidentListItem>` (§3.2, §5.2). Lỗi riêng: `422 VALIDATION_ERROR` (enum/search/paging/sla sai), `422 SEARCH_TOO_BROAD` (>1.000 user match), `503 UPSTREAM_UNAVAILABLE` khi Identity search lỗi; cộng lỗi chung §3.4. Trip/Identity batch enrichment không thành công sau query không làm fail list: source trả các display field nullable.

### 6.2. Chi tiết incident

`GET {BASE_URL}/v1/operator/parcel-incidents/{incidentId}`

| Input | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `incidentId` path | uuid | Có | ASP.NET route `guid` |
| `beforeSequence` query | int | Không | Cursor lấy event cũ hơn sequence này |
| `limit` query | int | Không | Default `50`, từ 1 đến 100 |

```bash
curl "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID?limit=50" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}?limit=50`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "incident": {
      "incidentId": "00000000-0000-0000-0000-000000000101",
      "parcelId": "00000000-0000-0000-0000-000000000201",
      "operatorId": "00000000-0000-0000-0000-000000000301",
      "type": "MISSING",
      "status": "SEARCHING",
      "tripId": "00000000-0000-0000-0000-000000000401",
      "lastKnownLocation": null,
      "searchDeadline": "2026-08-24T10:00:00+07:00",
      "createdAt": "2026-08-21T10:00:00+07:00",
      "operatorProcessBreach": false,
      "parcel": { "parcelId": "00000000-0000-0000-0000-000000000201", "parcelCode": "VR-PCL-20260821-ABCD2345", "status": "PENDING_OPERATOR_ACTION", "description": "Thùng điện tử", "photoUrl": null, "quantity": 1, "declaredValueVnd": 12000000 },
      "trip": { "tripId": "00000000-0000-0000-0000-000000000401", "status": null, "departureAt": null, "eta": null, "route": null, "vehicle": null, "stops": [] },
      "expectedDropoff": { "type": "DESTINATION_STATION", "id": null, "name": null, "orderIndex": null, "eta": null },
      "lastCustody": null,
      "reporter": null,
      "taskSummary": null,
      "claimSummary": null,
      "sla": null,
      "availableActions": ["ASSIGN", "RECORD_SEARCH", "MARK_FOUND"]
    },
    "searchTasks": [],
    "expectedLocation": null,
    "resolutionCode": null,
    "resolutionNote": null,
    "resolvedAt": null,
    "currentCustody": null,
    "custodyTimeline": { "items": [], "nextCursor": null },
    "claim": null,
    "parcel": { "parcelId": "00000000-0000-0000-0000-000000000201", "parcelCode": "VR-PCL-20260821-ABCD2345", "status": "PENDING_OPERATOR_ACTION", "description": "Thùng điện tử", "photoUrl": null, "quantity": 1, "declaredValueVnd": 12000000 },
    "sender": { "userId": "00000000-0000-0000-0000-000000000801", "displayName": null, "phone": null, "email": null, "avatarUrl": null, "source": "SENDER" },
    "recipient": { "userId": null, "displayName": "Nguyễn B", "phone": "0900000000", "email": null, "avatarUrl": null, "source": "RECIPIENT" },
    "trip": { "tripId": "00000000-0000-0000-0000-000000000401", "status": null, "departureAt": null, "eta": null, "route": null, "vehicle": null, "stops": [] },
    "expectedDropoff": { "type": "DESTINATION_STATION", "id": null, "name": null, "orderIndex": null, "eta": null },
    "reporter": { "userId": "00000000-0000-0000-0000-000000000601", "displayName": null, "phone": null, "email": null, "avatarUrl": null, "source": "USER" },
    "forwardingSummary": null,
    "availableActions": ["ASSIGN", "RECORD_SEARCH", "MARK_FOUND"],
    "forwardingOperation": null
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Handler luôn dựng `parcel`, `sender`, `recipient`, `trip`, `expectedDropoff`, `reporter` và `availableActions`; nếu Identity/Trip enrichment thiếu thì các display field bên trong nullable hoặc dùng snapshot fallback, không phải toàn bộ object `null`. `currentCustody`, `claim`, `forwardingSummary` và `forwardingOperation` có thể null. Lỗi riêng: `404 PARCEL_INCIDENT_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `422 VALIDATION_ERROR` khi `limit` ngoài 1–100 hoặc `beforeSequence < 1`.

### 6.3. Gán toàn bộ search task đang mở

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/assign`

Body:

```json
{ "assigneeUserId": "00000000-0000-0000-0000-000000000601" }
```

`assigneeUserId` là uuid bắt buộc theo non-nullable request record. Handler gán tất cả task `OPEN`/`IN_PROGRESS`; không kiểm tra assignee có tồn tại trong Identity.

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/assign" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"assigneeUserId\":\"$ASSIGNEE_ID\"}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/assign`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ assigneeUserId })
});
const result = await response.json();
```

Thành công `200`, `data` là incident detail đã cập nhật (§5.3), không cần refetch. Lỗi code trong handler: `404 PARCEL_INCIDENT_NOT_FOUND`, `403 FORBIDDEN`; cộng lỗi chung. ⚠️ `Guid.Empty` chưa có guard application-level; Domain có thể ném exception không coded và thành `500 INTERNAL_ERROR`. FE phải chặn UUID rỗng.

### 6.4. Ghi kết quả search task

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/search-scan`

```json
{
  "taskId": "00000000-0000-0000-0000-000000000111",
  "found": false,
  "result": "Không thấy trong khoang hành lý",
  "evidenceReferences": ["https://cdn.example/check-1.jpg"]
}
```

| Field | Kiểu | Bắt buộc | Rule thực tế |
|---|---|:---:|---|
| `taskId` | uuid | Có | Task phải thuộc incident |
| `found` | boolean | Có | `true` gọi `Complete`; `false` gọi `Fail` |
| `result` | string | Có | Request non-nullable; Domain yêu cầu nội dung hợp lệ nhưng controller chưa có validator |
| `evidenceReferences` | string[]? | Không | Serialize JSON vào task evidence |

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/search-scan" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"taskId\":\"$TASK_ID\",\"found\":false,\"result\":\"Không thấy trên xe\",\"evidenceReferences\":[]}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/search-scan`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ taskId, found: false, result: "Không thấy trên xe", evidenceReferences: [] })
});
const result = await response.json();
```

Thành công `200`, trả incident detail cập nhật. Lỗi riêng: `404 PARCEL_INCIDENT_NOT_FOUND`, `404 PARCEL_SEARCH_TASK_NOT_FOUND`, `409 PARCEL_SEARCH_TASK_MISMATCH`, `403 FORBIDDEN` nếu khác tenant hoặc task gán người khác. ⚠️ Chuỗi `result` rỗng hoặc ghi lại task đã đóng có thể đi qua exception Domain và thành `500 INTERNAL_ERROR`; FE phải bắt buộc nonblank và khóa task sau response thành công.

### 6.5. Xác nhận đã tìm thấy Parcel

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/mark-found`

```json
{
  "actualLocationType": "WAREHOUSE",
  "actualLocationId": "00000000-0000-0000-0000-000000000502",
  "locationSnapshot": "Kho thất lạc bến B",
  "evidenceReferences": ["https://cdn.example/found.jpg"],
  "note": "Đối chiếu đúng tem và cân nặng"
}
```

`actualLocationType` bắt buộc và phải là `ParcelCustodyLocationType`; `actualLocationId` bắt buộc trừ `VEHICLE`; các field còn lại optional. Chỉ incident `OPEN`, `SEARCHING`, `ESCALATED`, `SEARCH_EXPIRED` được mark found.

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/mark-found" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"actualLocationType\":\"WAREHOUSE\",\"actualLocationId\":\"$LOCATION_ID\",\"locationSnapshot\":\"Kho bến B\",\"evidenceReferences\":[],\"note\":\"Đã đối chiếu\"}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/mark-found`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ actualLocationType: "WAREHOUSE", actualLocationId: locationId, locationSnapshot: "Kho bến B", evidenceReferences: [], note: "Đã đối chiếu" })
});
const result = await response.json();
```

Thành công `200`, trả detail cập nhật, tạo custody event `FOUND` và kết thúc các task còn mở theo handler. Lỗi: `404 PARCEL_INCIDENT_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_INCIDENT_INVALID_STATUS`, `422 PARCEL_CUSTODY_LOCATION_REQUIRED`, `422 VALIDATION_ERROR` nếu location type sai.

### 6.6. Lấy forwarding options

`GET {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/forwarding-options?limit=20`

`limit` optional, default 20, từ 1 đến 50. Incident phải `FOUND`, current custody phải có confirmed location. Trip Service chịu trách nhiệm tính route/capacity; Parcel không tự suy luận.

```bash
curl "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/forwarding-options?limit=20" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/forwarding-options?limit=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": [{
    "trip": { "tripId": "00000000-0000-0000-0000-000000000402", "status": "SCHEDULED", "departureAt": "2026-08-22T08:00:00+07:00", "eta": "2026-08-22T12:00:00+07:00", "route": null, "vehicle": null, "stops": [] },
    "route": null,
    "vehicle": null,
    "pickupLocation": { "type": "WAREHOUSE", "id": "00000000-0000-0000-0000-000000000502", "name": "Kho bến B", "orderIndex": null, "eta": "2026-08-22T08:00:00+07:00" },
    "targetDropoff": { "type": "ROUTE_STOP", "id": "00000000-0000-0000-0000-000000000501", "name": "Bến C", "orderIndex": null, "eta": "2026-08-22T12:00:00+07:00" },
    "departureAt": "2026-08-22T08:00:00+07:00",
    "eta": "2026-08-22T12:00:00+07:00",
    "canReserve": true,
    "unavailableReason": null
  }],
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Lỗi: `404 PARCEL_INCIDENT_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_INCIDENT_INVALID_STATUS`, `409 PARCEL_CUSTODY_LOCATION_REQUIRED`, `422 VALIDATION_ERROR`, `503 UPSTREAM_UNAVAILABLE`.

### 6.7. Bắt đầu forwarding

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/forward`

```json
{ "targetTripId": "00000000-0000-0000-0000-000000000402" }
```

Incident phải `FOUND`; target trip phải tồn tại, cùng operator và không `COMPLETED`/`CANCELLED`. Handler chuyển Parcel vào flow transfer confirmation, tạo planned transit leg nếu chưa có và chuyển incident thành `FORWARDING`.

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/forward" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d "{\"targetTripId\":\"$TARGET_TRIP_ID\"}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/forward`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ targetTripId })
});
const result = await response.json();
```

Thành công `200`, trả incident detail với `forwardingSummary`/`forwardingOperation` nếu read model dựng được. Lỗi: `404 PARCEL_INCIDENT_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `404 TRIP_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_INCIDENT_INVALID_STATUS`, `409 INVALID_STATUS`; cộng lỗi chung.

### 6.8. Resolve incident

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/resolve`

```json
{
  "note": "Đã giao lại tại đúng bến",
  "resolutionCode": "DELIVERED_TO_CORRECT_LOCATION"
}
```

`note` optional. `resolutionCode` có default C# `DELIVERED_TO_CORRECT_LOCATION`; nếu gửi `null`/blank thì handler trả validation. Chỉ incident `FOUND` hoặc `FORWARDING`.

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/resolve" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"note":"Đã giao đúng bến","resolutionCode":"DELIVERED_TO_CORRECT_LOCATION"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/resolve`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ note: "Đã giao đúng bến", resolutionCode: "DELIVERED_TO_CORRECT_LOCATION" })
});
const result = await response.json();
```

Thành công `200`, trả detail cập nhật. Lỗi: `404 PARCEL_INCIDENT_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_INCIDENT_INVALID_STATUS`, `422 VALIDATION_ERROR`.

### 6.9. Declare lost sau search SLA

`POST {BASE_URL}/v1/operator/parcel-incidents/{incidentId}/declare-lost`

Controller tái sử dụng `ResolveParcelIncidentRequest`:

```json
{ "note": "Không tìm thấy sau đối soát", "resolutionCode": "DELIVERED_TO_CORRECT_LOCATION" }
```

Handler chỉ dùng `note`, bỏ qua `resolutionCode`; có thể gửi `{ "note": "..." }` và serializer dùng default của request record. Chỉ được gọi khi giờ hiện tại không trước `searchDeadline`; handler tự chuyển `OPEN/SEARCHING → ESCALATED → SEARCH_EXPIRED → LOST_CONFIRMED` trong cùng request khi SLA đã hết.

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-incidents/$INCIDENT_ID/declare-lost" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"note":"Không tìm thấy sau SLA"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-incidents/${incidentId}/declare-lost`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ note: "Không tìm thấy sau SLA" })
});
const result = await response.json();
```

Thành công `200`, detail có status `LOST_CONFIRMED`; không đổi `ParcelStatus` thành `LOST`. Lỗi: `404 PARCEL_INCIDENT_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_SEARCH_SLA_NOT_EXPIRED`, `409 PARCEL_INCIDENT_INVALID_STATUS`.

## 7. Claim và compensation policy

### 7.1. Claim queue

`GET {BASE_URL}/v1/operator/claims`

| Query | Kiểu | Bắt buộc | Rule/default |
|---|---|:---:|---|
| `status` | `ParcelClaimStatus` | Không | Case-insensitive |
| `search` | string | Không | Tối đa 100 ký tự |
| `slaState` | enum | Không | `ON_TRACK`, `DUE_SOON`, `BREACHED`, `CLOSED` |
| `from`, `to` | datetime | Không | `to` được cộng 1 tick |
| `page` | int | Không | 1 |
| `pageSize` | int | Không | 20, 1–100 |

```bash
curl "$BASE_URL/v1/operator/claims?status=SUBMITTED&slaState=ON_TRACK&page=1&pageSize=20" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/claims?status=SUBMITTED&page=1&pageSize=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`, `data` là page claim row §5.4. Ví dụ empty page dùng đúng schema §3.2. `fundingStatus` chỉ có `FUNDING_PENDING`, `READY_FOR_PAYOUT`, `PAID`, `NOT_APPLICABLE`. Lỗi: `422 VALIDATION_ERROR`, `422 SEARCH_TOO_BROAD`, `503 UPSTREAM_UNAVAILABLE`; cộng lỗi chung.

### 7.2. Claim detail

`GET {BASE_URL}/v1/operator/claims/{claimId}`

`claimId` là path uuid bắt buộc. Không có query/body.

```bash
curl "$BASE_URL/v1/operator/claims/$CLAIM_ID" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/claims/${claimId}`, { headers: auth });
const result = await response.json();
```

Thành công `200`; `data` có đúng các field claim detail §5.4. Lỗi: `404 PARCEL_CLAIM_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`; cộng lỗi chung.

### 7.3. Quyết định claim

`POST {BASE_URL}/v1/operator/claims/{claimId}/decision` — chỉ `OPERATOR_ADMIN`.

```json
{
  "decision": "APPROVE",
  "provenDirectLossVnd": 12000000,
  "reason": "Chứng từ hợp lệ, lỗi vận hành operator"
}
```

| Field | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `decision` | string | Có | Case-insensitive `APPROVE` hoặc `REJECT` |
| `provenDirectLossVnd` | int64? | Không | Dùng khi approve; calculator từ chối giá trị âm bằng `VALIDATION_ERROR` |
| `reason` | string | Có | Nonblank; blank trả `PARCEL_CLAIM_EVIDENCE_REQUIRED` |

Chỉ claim `SUBMITTED` được quyết định. Handler gọi `BeginReview` và approve/reject trong cùng request; không có endpoint chuyển riêng sang `UNDER_REVIEW`.

```bash
curl -X POST "$BASE_URL/v1/operator/claims/$CLAIM_ID/decision" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"decision":"APPROVE","provenDirectLossVnd":12000000,"reason":"Chứng từ hợp lệ"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/claims/${claimId}/decision`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ decision: "APPROVE", provenDirectLossVnd: 12_000_000, reason: "Chứng từ hợp lệ" })
});
const result = await response.json();
```

Thành công `200`, trả claim detail đã cập nhật. Công thức trong code:

```text
assessedLoss = min(provenDirectLossVnd, declaredValueVnd) nếu cả hai có
gross = round(assessedLoss × rate / 100, AwayFromZero)
cargoAward = min(gross, policyCapVnd)
không có provenDirectLossVnd: cargoAward = min(noProofFallbackMultiplier × finalTotalPriceVnd, cap)
freightRefund = max(0, finalTotalPriceVnd - refundedAmountVnd)
totalAward = cargoAward + freightRefund
```

Lỗi: `404 PARCEL_CLAIM_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 PARCEL_CLAIM_ALREADY_DECIDED`, `422 PARCEL_CLAIM_EVIDENCE_REQUIRED`, `422 VALIDATION_ERROR`. Approval phát event; payout/funding status được Payment cập nhật bất đồng bộ, không giả định response approve đã là `PAID`.

### 7.4. Đọc compensation policy

`GET {BASE_URL}/v1/operator/policies/parcel-compensation`

```bash
curl "$BASE_URL/v1/operator/policies/parcel-compensation" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/policies/parcel-compensation`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "00000000-0000-0000-0000-000000000301",
    "compensationRatePercent": 50,
    "maxCompensationVnd": 30000000,
    "noProofFallbackMultiplier": 4,
    "claimWindowDays": 30,
    "searchSlaHours": 72,
    "decisionSlaBusinessDays": 7,
    "payoutSlaBusinessDays": 3,
    "version": 1,
    "belowDefaultAcknowledged": false,
    "platformDefaultPolicy": { "compensationRatePercent": 50, "maxCompensationVnd": 30000000, "noProofFallbackMultiplier": 4, "claimWindowDays": 30, "searchSlaHours": 72, "decisionSlaBusinessDays": 7, "payoutSlaBusinessDays": 3 },
    "isBelowPlatformDefault": false,
    "effectiveForNewParcelsOnly": true,
    "updatedAt": "2026-08-21T12:00:00+07:00",
    "updatedBy": "00000000-0000-0000-0000-000000000601"
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Nếu chưa có row operator, query trả policy mặc định với `version=0`, `updatedAt=null`, `updatedBy=null`. Lỗi riêng chỉ `403 FORBIDDEN`; cộng auth lỗi.

### 7.5. Cập nhật compensation policy

`PUT {BASE_URL}/v1/operator/policies/parcel-compensation` — chỉ `OPERATOR_ADMIN`.

```json
{
  "compensationRatePercent": 50,
  "maxCompensationVnd": 30000000,
  "noProofFallbackMultiplier": 4,
  "claimWindowDays": 30,
  "searchSlaHours": 72,
  "decisionSlaBusinessDays": 7,
  "payoutSlaBusinessDays": 3,
  "belowDefaultAcknowledged": false
}
```

| Field | Kiểu | Default khi bỏ field | Range |
|---|---|---:|---|
| `compensationRatePercent` | int | `0` do non-optional CLR | 1–100 |
| `maxCompensationVnd` | int64 | `0` | > 0 |
| `noProofFallbackMultiplier` | int | 4 | 1–100 |
| `claimWindowDays` | int | 30 | 1–365 |
| `searchSlaHours` | int | 72 | 1–720 |
| `decisionSlaBusinessDays` | int | 7 | 1–90 |
| `payoutSlaBusinessDays` | int | 3 | 1–90 |
| `belowDefaultAcknowledged` | bool | false | Phải true nếu rate < 50 hoặc cap < 30.000.000 |

Hai field đầu về mặt JSON model binding có thể bị bỏ nhưng sẽ thành 0 rồi bị validation; FE phải xem là bắt buộc.

```bash
curl -X PUT "$BASE_URL/v1/operator/policies/parcel-compensation" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"compensationRatePercent":50,"maxCompensationVnd":30000000,"noProofFallbackMultiplier":4,"claimWindowDays":30,"searchSlaHours":72,"decisionSlaBusinessDays":7,"payoutSlaBusinessDays":3,"belowDefaultAcknowledged":false}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/policies/parcel-compensation`, {
  method: "PUT", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ compensationRatePercent: 50, maxCompensationVnd: 30_000_000, noProofFallbackMultiplier: 4, claimWindowDays: 30, searchSlaHours: 72, decisionSlaBusinessDays: 7, payoutSlaBusinessDays: 3, belowDefaultAcknowledged: false })
});
const result = await response.json();
```

Thành công `200`, trả full policy §7.4, tăng `version`; chỉ có hiệu lực với Parcel mới. Lỗi: `403 FORBIDDEN`, `422 VALIDATION_ERROR`, `422 POLICY_BELOW_DEFAULT_ACK_REQUIRED`; cộng idempotency/auth lỗi.

## 8. Quản lý Parcel vận hành

### 8.1. Danh sách Parcel operator

`GET {BASE_URL}/v1/operator/parcels`

| Query | Kiểu | Bắt buộc | Default/rule |
|---|---|:---:|---|
| `status` | `ParcelStatus` | Không | Case-insensitive |
| `tripId` | uuid | Không | Nếu có phải khác UUID rỗng |
| `pendingActionType` | `PendingActionType` | Không | — |
| `page` | int | Không | 1; >= 1 |
| `pageSize` | int | Không | 20; 1–100 |
| `search` | string | Không | Tối đa 100 ký tự; tìm Parcel snapshot và sender qua Identity |
| `from`, `to` | date | Không | `from <= to`; `to` inclusive theo ngày local |
| `dateField` | string | Không | Default `createdAt`; `createdAt` hoặc `finalPaymentDeadline` |
| `sizeCategory` | `ParcelSizeCategory` | Không | — |
| `routeId` | uuid | Không | — |
| `sortBy` | string | Không | Default `createdAt`; `createdAt` hoặc `finalPaymentDeadline` |
| `sortDir` | string | Không | Default `desc`; case-sensitive `asc` hoặc `desc` |

Chỉ 13 query key trên được phép; key dư trả `422 VALIDATION_ERROR` với item tương ứng trong `error.fields`.

```bash
curl "$BASE_URL/v1/operator/parcels?status=PENDING_OPERATOR_ACTION&pendingActionType=CUSTODY_EXCEPTION&page=1&pageSize=20&sortBy=createdAt&sortDir=desc" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const qs = new URLSearchParams({ status: "PENDING_OPERATOR_ACTION", pendingActionType: "CUSTODY_EXCEPTION", page: "1", pageSize: "20" });
const response = await fetch(`${BASE_URL}/v1/operator/parcels?${qs}`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": { "items": [], "page": 1, "pageSize": 20, "totalItems": 0, "totalPages": 0, "hasNextPage": false, "hasPreviousPage": false },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Mỗi item có toàn bộ field §5.5. Endpoint batch-enrich Trip và Identity một lần/page. Lỗi: `400 INVALID_SORT_FIELD`, `422 VALIDATION_ERROR`, `422 SEARCH_TOO_BROAD`, `503 UPSTREAM_UNAVAILABLE`; cộng lỗi chung.

### 8.2. Chi tiết Parcel operator

`GET {BASE_URL}/v1/operator/parcels/{parcelId}`

`parcelId` path uuid bắt buộc; không query/body.

```bash
curl "$BASE_URL/v1/operator/parcels/$PARCEL_ID" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}`, { headers: auth });
const result = await response.json();
```

Thành công `200`; `data` là full detail §5.5. Lỗi explicit: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `503 UPSTREAM_UNAVAILABLE`; cộng lỗi chung. Repository/filter tenant không trả dữ liệu operator khác.

### 8.3. Review Parcel

`PATCH {BASE_URL}/v1/operator/parcels/{parcelId}/review`

```json
{ "decision": "APPROVED", "reason": null }
```

- `decision` bắt buộc, validator chỉ nhận đúng uppercase `APPROVED` hoặc `REJECTED`.
- `reason` optional khi approve, bắt buộc nonempty khi reject.
- Chỉ Parcel `PENDING_OPERATOR_REVIEW`; review lại bị conflict.

```bash
curl -X PATCH "$BASE_URL/v1/operator/parcels/$PARCEL_ID/review" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"decision":"APPROVED","reason":null}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/review`, {
  method: "PATCH", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ decision: "APPROVED", reason: null })
});
const result = await response.json();
```

Thành công `200`, `data` là `ReviewParcelResponse` §5.6. Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 ALREADY_REVIEWED`, `409 INVALID_STATUS`, `409 RACE_LOST`, `422 INVALID_DECISION`, `422 FARE_NOT_CONFIGURED`, `422 VALIDATION_ERROR`, `503 UPSTREAM_UNAVAILABLE`.

### 8.4. Yêu cầu transfer sang trip khác

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/request-transfer`

```json
{
  "targetTripId": "00000000-0000-0000-0000-000000000402",
  "reason": "Chuyển chuyến để giao lại đúng bến"
}
```

`targetTripId` uuid bắt buộc và khác current trip. `reason` nullable theo DTO nhưng handler bắt buộc sau trim từ 1 đến 500 ký tự. Parcel status phải `PENDING_OPERATOR_ACTION`, `LOADED` hoặc `IN_TRANSIT`; target trip phải cùng operator.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/request-transfer" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"targetTripId\":\"$TARGET_TRIP_ID\",\"reason\":\"Chuyển chuyến để giao đúng bến\"}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/request-transfer`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ targetTripId, reason: "Chuyển chuyến để giao đúng bến" })
});
const result = await response.json();
```

Thành công `200`, `data` là `OperationalParcelResponse`. Lỗi: `404 PARCEL_NOT_FOUND`, `404 TRIP_NOT_FOUND`, `403 FORBIDDEN`, `409 INVALID_STATUS`, `409 INVALID_TRANSFER_TARGET`, `409 RACE_LOST`, `409 PARCEL_CARGO_RECOVERY_IN_PROGRESS`, `409 TRIP_CARGO_TRANSFER_CONFLICT`, `422 VALIDATION_ERROR`, `503 TRIP_SERVICE_UNAVAILABLE`.

### 8.5. Return Parcel

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/return`

```json
{ "returnReason": "Không có chuyến forwarding phù hợp" }
```

`returnReason` bắt buộc sau trim, 1–500 ký tự. Chỉ status `PENDING_OPERATOR_ACTION` hoặc `TRANSFER_ESCALATED`.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/return" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"returnReason":"Không có chuyến phù hợp"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/return`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ returnReason: "Không có chuyến phù hợp" })
});
const result = await response.json();
```

Thành công `200`, `OperationalParcelResponse`. Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 INVALID_STATUS`, `409 PARCEL_CARGO_RECOVERY_IN_PROGRESS`, `409 TRIP_CARGO_TRANSFER_CONFLICT`, `422 VALIDATION_ERROR`; cargo recovery resume còn có thể trả `404 TRIP_NOT_FOUND`, `404 PARCEL_CARGO_NOT_FOUND`, `409 TRIP_CARGO_CAPACITY_EXCEEDED`, `503 TRIP_SERVICE_UNAVAILABLE`.

### 8.6. Hủy Parcel thủ công trước load

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/cancel`

```json
{ "reason": "Khách yêu cầu hủy", "refundChoice": "POLICY" }
```

- `reason`: 1–500 ký tự sau trim.
- `refundChoice`: optional; default `POLICY`; nhận `FULL`, `POLICY`, `NO` và alias `FULL_REFUND`, `POLICY_REFUND`, `NO_REFUND`.
- Chỉ trạng thái được classifier xem là pre-load; các trạng thái khác trả `INVALID_STATUS`.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/cancel" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"reason":"Khách yêu cầu hủy","refundChoice":"POLICY"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/cancel`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ reason: "Khách yêu cầu hủy", refundChoice: "POLICY" })
});
const result = await response.json();
```

Thành công `200`, `OperationalParcelResponse` có `refundChoice`, `refundAmount`. Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 INVALID_STATUS`, `409 RACE_LOST`, `422 INVALID_REFUND_CHOICE`, `422 VALIDATION_ERROR`, `503 UPSTREAM_UNAVAILABLE`, `503 TRIP_SERVICE_UNAVAILABLE`, `503 TRIP_CARGO_TRANSFER_CONFLICT` theo mapping dependency hiện tại.

### 8.7. Xác nhận refund pending

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/confirm-refund`

```json
{ "reason": "Đã kiểm tra số tiền hoàn" }
```

`reason` optional, handler chỉ trim để ghi event; không có length validation. Parcel phải `PENDING_OPERATOR_ACTION` với `pendingActionType=REFUND_CONFIRMATION` và refund amount > 0.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/confirm-refund" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"reason":"Đã kiểm tra"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/confirm-refund`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ reason: "Đã kiểm tra" })
});
const result = await response.json();
```

Thành công `200`, `OperationalParcelResponse`. Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 INVALID_PENDING_ACTION`, `409 INVALID_REFUND_AMOUNT`, `409 RACE_LOST`; cộng lỗi chung.

### 8.8. Override cargo capacity

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/override-capacity`

```json
{ "reason": "Supervisor phê duyệt vượt tải còn an toàn" }
```

Role admin luôn được gọi; staff cần permission `CAN_OVERRIDE_CAPACITY`. `reason` bắt buộc nonblank. Parcel phải `PENDING_OPERATOR_ACTION` với `CAPACITY_EXCEEDED` hoặc `RESERVE_FAILED`.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/override-capacity" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"reason":"Supervisor phê duyệt"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/override-capacity`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ reason: "Supervisor phê duyệt" })
});
const result = await response.json();
```

Thành công `200`, `OperationalParcelResponse`. Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 INVALID_PENDING_ACTION`, `409 RACE_LOST`, `409 TRIP_CARGO_CAPACITY_EXCEEDED`, `422 VALIDATION_ERROR`, `503 TRIP_NOT_FOUND`, `503 TRIP_SERVICE_UNAVAILABLE`.

### 8.9. Manual confirm delivery

Có hai public path cùng gọi một command:

- `POST {BASE_URL}/v1/operator/parcels/{parcelId}/confirm-delivery`
- `POST {BASE_URL}/v1/operator/parcels/{parcelId}/manual-confirm`

Body chung:

```json
{ "confirmNote": "Đã đối chiếu giấy tờ", "note": null }
```

`confirmNote` và `note` đều nullable ở request DTO. `ResolveNote()` ưu tiên `confirmNote`, rồi `note`, cuối cùng chuỗi rỗng; sau đó `ManualConfirmDeliveryCommandValidator` bắt buộc resolved note nonblank và tối đa 500 ký tự. Parcel phải `DELIVERED_PENDING_CONFIRM`; retry idempotent về nghiệp vụ chỉ khi cùng actor và cùng note.

`confirm-delivery` curl/fetch:

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/confirm-delivery" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"confirmNote":"Đã đối chiếu giấy tờ","note":null}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/confirm-delivery`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ confirmNote: "Đã đối chiếu giấy tờ", note: null })
});
const result = await response.json();
```

`manual-confirm` curl/fetch:

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/manual-confirm" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"note":"Đã đối chiếu giấy tờ"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/manual-confirm`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ note: "Đã đối chiếu giấy tờ" })
});
const result = await response.json();
```

Cả hai thành công `200`, `data` là `{parcelId,status,confirmedAt}`. Lỗi: `400 PARCEL_NOT_PENDING_CONFIRM`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 RESOURCE_CONFLICT`, `422 VALIDATION_ERROR`; `manual-confirm` Swagger còn khai báo `503`, nhưng với Operator role handler không gọi Trip authorization. Lỗi chung vẫn áp dụng.

### 8.10. Gửi lại delivery confirmation email

`POST {BASE_URL}/v1/operator/parcels/{parcelId}/resend-delivery-email`

Không body. Parcel phải `DELIVERED_PENDING_CONFIRM`, hoặc `DELIVERY_REJECTED` chưa quá cửa sổ undo 15 phút; phải có `recipientEmail` và active delivery token. Token mới hết hạn sau 48 giờ.

```bash
curl -X POST "$BASE_URL/v1/operator/parcels/$PARCEL_ID/resend-delivery-email" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Idempotency-Key: $IDEMPOTENCY_KEY"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/resend-delivery-email`, {
  method: "POST", headers: { ...auth, "Idempotency-Key": newKey() }
});
const result = await response.json();
```

Thành công `200`, `data={parcelId,status:"DELIVERED_PENDING_CONFIRM",expiresAt}`. Lỗi: `400 PARCEL_NOT_PENDING_CONFIRM`, `400 PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 RESOURCE_CONFLICT`, `422 PARCEL_RECIPIENT_EMAIL_REQUIRED`; email dependency có thể thành `503` theo client mapping.

### 8.11. Status override duy nhất sang RETURNED

`PATCH {BASE_URL}/v1/operator/parcels/{parcelId}/status`

```json
{ "targetStatus": "RETURNED", "reason": "Hoàn trả theo biên bản" }
```

`targetStatus` chỉ hỗ trợ case-insensitive `RETURNED`. Command chuyển tiếp sang Return flow, vì vậy `reason` thực tế bắt buộc 1–500 ký tự và current status phải `PENDING_OPERATOR_ACTION` hoặc `TRANSFER_ESCALATED`.

```bash
curl -X PATCH "$BASE_URL/v1/operator/parcels/$PARCEL_ID/status" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d '{"targetStatus":"RETURNED","reason":"Hoàn trả theo biên bản"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/${parcelId}/status`, {
  method: "PATCH", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ targetStatus: "RETURNED", reason: "Hoàn trả theo biên bản" })
});
const result = await response.json();
```

Thành công `200`, `OperationalParcelResponse`. Lỗi trực tiếp: `409 INVALID_TRANSITION`, `422 VALIDATION_ERROR`; cộng toàn bộ lỗi Return ở §8.5.

### 8.12. Report summary

`GET {BASE_URL}/v1/operator/parcels/reports/summary`

`from`, `to` là optional `YYYY-MM-DD`. Default `to=today` theo `Asia/Ho_Chi_Minh`; default `from=to-30 ngày` (inclusive 31 calendar dates theo code).

```bash
curl "$BASE_URL/v1/operator/parcels/reports/summary?from=2026-08-01&to=2026-08-21" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/reports/summary?from=2026-08-01&to=2026-08-21`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "00000000-0000-0000-0000-000000000301",
    "from": "2026-08-01",
    "to": "2026-08-21",
    "totalParcels": 20,
    "totalLoaded": 18,
    "totalDelivered": 15,
    "totalRejected": 1,
    "totalReturned": 2,
    "grossParcelRevenueVnd": 4000000,
    "parcelRefundsVnd": 200000,
    "netParcelRevenueVnd": 3800000,
    "source": "ParcelStats"
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

`source` trong code là `ParcelStats` nếu có daily stats, ngược lại `ParcelsFallback`. Payment revenue dependency lỗi trả `503 UPSTREAM_UNAVAILABLE`. ⚠️ `from > to` ném raw `ArgumentException`, hiện được global filter đổi thành `500 INTERNAL_ERROR`, dù đây nên là 422; FE phải validate trước khi gọi.

### 8.13. Export CSV summary

`GET {BASE_URL}/v1/operator/parcels/reports/export`

Query `from`, `to` như §8.12; `format` optional, chỉ nhận case-insensitive `csv`. Thành công trả `text/csv`, filename `parcel-report-yyyyMMdd-yyyyMMdd.csv`, không có JSON wrapper.

```bash
curl "$BASE_URL/v1/operator/parcels/reports/export?from=2026-08-01&to=2026-08-21&format=csv" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -o parcel-report.csv
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcels/reports/export?from=2026-08-01&to=2026-08-21&format=csv`, { headers: auth });
if (!response.ok) throw await response.json();
const blob = await response.blob();
```

CSV có header:

```csv
operatorId,from,to,totalParcels,totalLoaded,totalDelivered,totalRejected,totalReturned,grossParcelRevenueVnd,parcelRefundsVnd,netParcelRevenueVnd,source
```

Lỗi upstream `503 UPSTREAM_UNAVAILABLE`. ⚠️ `format` khác `csv` hoặc `from > to` ném raw `ArgumentException` và hiện trả `500 INTERNAL_ERROR`; FE chỉ gửi `csv` và range hợp lệ.

## 9. Route fare, stats và report

### 9.1. Tạo route fare

`POST {BASE_URL}/v1/operator/parcel-route-fares` — chỉ `OPERATOR_ADMIN`.

```json
{
  "routeId": "00000000-0000-0000-0000-000000001001",
  "sizeCategory": "MEDIUM",
  "priceVnd": 150000,
  "effectiveFrom": "2026-08-22T00:00:00+07:00",
  "effectiveUntil": null
}
```

| Field | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `routeId` | uuid | Có | Non-empty; Trip Service xác minh route |
| `sizeCategory` | enum | Có | `ParcelSizeCategory`, case-insensitive |
| `priceVnd` | int64 | Có | FluentValidation >= 1.000 |
| `effectiveFrom` | datetime | Có | Non-default |
| `effectiveUntil` | datetime? | Không | Phải sau `effectiveFrom` |

```bash
curl -X POST "$BASE_URL/v1/operator/parcel-route-fares" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"routeId\":\"$ROUTE_ID\",\"sizeCategory\":\"MEDIUM\",\"priceVnd\":150000,\"effectiveFrom\":\"2026-08-22T00:00:00+07:00\",\"effectiveUntil\":null}"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-route-fares`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ routeId, sizeCategory: "MEDIUM", priceVnd: 150000, effectiveFrom: "2026-08-22T00:00:00+07:00", effectiveUntil: null })
});
const result = await response.json();
```

Thành công `201`:

```json
{
  "success": true,
  "statusCode": 201,
  "data": { "routeId": "00000000-0000-0000-0000-000000001001", "sizeCategory": "MEDIUM", "operatorId": "00000000-0000-0000-0000-000000000301", "priceVnd": 150000, "effectiveFrom": "2026-08-22T00:00:00+07:00", "effectiveUntil": null, "createdAt": "2026-08-21T12:00:00+07:00", "updatedAt": "2026-08-21T12:00:00+07:00" },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Lỗi: `404 ROUTE_NOT_FOUND`, `409 FARE_ALREADY_EXISTS`, `422 INVALID_SIZE_CATEGORY`, `422 VALIDATION_ERROR`, `503 ROUTE_OWNERSHIP_UNVERIFIABLE`; cộng auth/idempotency.

### 9.2. Danh sách route fare

`GET {BASE_URL}/v1/operator/parcel-route-fares`

| Query | Kiểu | Bắt buộc | Default/rule |
|---|---|:---:|---|
| `routeId` | uuid | Không | — |
| `sizeCategory` | enum | Không | — |
| `page` | int | Không | 1 |
| `pageSize` | int | Không | 20, 1–100 |
| `search` | string | Không | <=100; Trip Service tìm route IDs |
| `sortBy` | string | Không | `effectiveFrom`; chỉ `priceVnd`, `effectiveFrom` |
| `sortDir` | string | Không | `desc`; case-insensitive `asc`, `desc` |
| `effectiveAt` | date | Không | Anchor theo ngày local |
| `status` | string | Không | `ACTIVE`, `SCHEDULED`, `EXPIRED`; nếu chỉ có `effectiveAt`, status mặc định `ACTIVE` |

Chỉ các query trên được phép.

```bash
curl "$BASE_URL/v1/operator/parcel-route-fares?status=ACTIVE&page=1&pageSize=20&sortBy=effectiveFrom&sortDir=desc" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-route-fares?status=ACTIVE&page=1&pageSize=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`, `PagedResult<ParcelRouteFareResponse>`; item field đúng ví dụ §9.1. Lỗi: `400 INVALID_SORT_FIELD`, `422 INVALID_SIZE_CATEGORY`, `422 VALIDATION_ERROR`, `503 UPSTREAM_UNAVAILABLE`; cộng lỗi chung.

### 9.3. Fare coverage summary

`GET {BASE_URL}/v1/operator/parcel-route-fares/summary`

Không chấp nhận query key nào (`AllowedQueryParameters` rỗng).

```bash
curl "$BASE_URL/v1/operator/parcel-route-fares/summary" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-route-fares/summary`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": [{ "routeId": "00000000-0000-0000-0000-000000001001", "configuredSizeCategories": ["SMALL", "MEDIUM"], "hasActiveWindow": true, "hasScheduledWindow": false }],
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Không có lỗi domain explicit; query dư trả `422 VALIDATION_ERROR`, cộng auth/forbidden.

### 9.4. Cập nhật một route fare

`PATCH {BASE_URL}/v1/operator/parcel-route-fares/{routeId}/{sizeCategory}` — chỉ `OPERATOR_ADMIN`.

Path: `routeId` uuid, `sizeCategory` string enum; đều bắt buộc.

```json
{
  "priceVnd": 175000,
  "effectiveFrom": null,
  "effectiveUntil": "2026-12-31T23:59:59+07:00"
}
```

Mỗi body field optional nhưng ít nhất một field phải có giá trị khác null. `priceVnd` nếu có >=1.000. Effective window sau khi merge phải có end sau start. Vì DTO không phân biệt omitted với explicit `null`, endpoint hiện không thể xóa một `effectiveUntil` đang có bằng cách gửi `null`.

```bash
curl -X PATCH "$BASE_URL/v1/operator/parcel-route-fares/$ROUTE_ID/MEDIUM" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"priceVnd":175000,"effectiveFrom":null,"effectiveUntil":"2026-12-31T23:59:59+07:00"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-route-fares/${routeId}/MEDIUM`, {
  method: "PATCH", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ priceVnd: 175000, effectiveUntil: "2026-12-31T23:59:59+07:00" })
});
const result = await response.json();
```

Thành công `200`, `ParcelRouteFareResponse`. Lỗi: `404 ROUTE_NOT_FOUND`, `404 FARE_NOT_FOUND`, `422 INVALID_SIZE_CATEGORY`, `422 VALIDATION_ERROR`, `503 ROUTE_OWNERSHIP_UNVERIFIABLE`; cộng lỗi chung.

### 9.5. Batch upsert route fares

`PUT {BASE_URL}/v1/operator/parcel-route-fares/{routeId}/batch` — chỉ `OPERATOR_ADMIN`.

```json
{
  "effectiveFrom": "2026-08-22T00:00:00+07:00",
  "effectiveUntil": null,
  "items": [
    { "sizeCategory": "SMALL", "priceVnd": 100000 },
    { "sizeCategory": "MEDIUM", "priceVnd": 150000 }
  ]
}
```

- `routeId`: path uuid non-empty.
- `effectiveFrom`: body datetime bắt buộc/non-default.
- `effectiveUntil`: optional, phải sau `effectiveFrom`.
- `items`: bắt buộc 1–4; không null item; `sizeCategory` nonblank, thuộc enum và unique case-insensitive; `priceVnd` > 0. Lưu ý batch chỉ yêu cầu >0, khác create/update yêu cầu >=1.000.

```bash
curl -X PUT "$BASE_URL/v1/operator/parcel-route-fares/$ROUTE_ID/batch" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"effectiveFrom":"2026-08-22T00:00:00+07:00","effectiveUntil":null,"items":[{"sizeCategory":"SMALL","priceVnd":100000},{"sizeCategory":"MEDIUM","priceVnd":150000}]}'
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-route-fares/${routeId}/batch`, {
  method: "PUT", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ effectiveFrom: "2026-08-22T00:00:00+07:00", effectiveUntil: null, items: [{ sizeCategory: "SMALL", priceVnd: 100000 }, { sizeCategory: "MEDIUM", priceVnd: 150000 }] })
});
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "routeId": "00000000-0000-0000-0000-000000001001",
    "items": [{ "sizeCategory": "SMALL", "priceVnd": 100000, "effectiveFrom": "2026-08-22T00:00:00+07:00", "effectiveUntil": null, "created": true }]
  },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Lỗi: `404 ROUTE_NOT_FOUND`, `422 INVALID_SIZE_CATEGORY`, `422 VALIDATION_ERROR`, `503 ROUTE_OWNERSHIP_UNVERIFIABLE`; controller cũng khai báo 409 nhưng handler không ném coded conflict trực tiếp.

### 9.6. Parcel stats cho chart

`GET {BASE_URL}/v1/operator/parcel-stats` — chỉ `OPERATOR_ADMIN`.

| Query | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `from` | date | Có | — |
| `to` | date | Có | `from <= to`; range inclusive <=366 ngày |
| `groupBy` | string | Có | Case-insensitive `status` hoặc `route` |
| `limit` | int? | Không | Default 10; `Math.Clamp` về 1–100, áp dụng route limit |

```bash
curl "$BASE_URL/v1/operator/parcel-stats?from=2026-08-01&to=2026-08-21&groupBy=status" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/parcel-stats?from=2026-08-01&to=2026-08-21&groupBy=status`, { headers: auth });
const result = await response.json();
```

Thành công group status:

```json
{ "success": true, "statusCode": 200, "data": { "items": [{ "key": "DELIVERY_CONFIRMED", "count": 15 }], "totalParcels": 20 }, "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" } }
```

Group route item thay bằng `{routeId,routeName,parcelCount}`; các field null bị `[JsonIgnore(WhenWritingNull)]` nên không xuất hiện. Lỗi: `422 VALIDATION_ERROR` cho thiếu/sai date/group/range; cộng auth/forbidden.

### 9.7. Export XLSX Parcel detail

`GET {BASE_URL}/v1/operator/reports/parcels/export`

`from`, `to` optional date. Default là 30 ngày inclusive kết thúc hôm nay; range bắt buộc từ 1 đến 92 ngày theo lịch `Asia/Ho_Chi_Minh`.

```bash
curl "$BASE_URL/v1/operator/reports/parcels/export?from=2026-08-01&to=2026-08-21" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -o parcels-report.xlsx
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/reports/parcels/export?from=2026-08-01&to=2026-08-21`, { headers: auth });
if (!response.ok) throw await response.json();
const blob = await response.blob();
```

Thành công `200`, content type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, filename `parcels-report-yyyyMMdd-yyyyMMdd.xlsx`, không có JSON wrapper. Sheet `Parcels` có đúng columns:

```text
parcel_id, parcel_code, trip_id, status, size_category, total_price_vnd,
deposit_amount_vnd, additional_amount_vnd, refund_amount_vnd,
created_at_asia_ho_chi_minh, confirmed_at_asia_ho_chi_minh
```

Lỗi: `422 REPORT_RANGE_INVALID` nếu range ngoài 1–92 ngày hoặc đảo chiều; `403 FORBIDDEN`; cộng auth lỗi.

## 10. Unidentified package và station handoff

### 10.1. Danh sách kiện chưa định danh

`GET {BASE_URL}/v1/operator/unidentified-packages`

| Query | Kiểu | Bắt buộc | Default/rule |
|---|---|:---:|---|
| `status` | enum | Không | `UNIDENTIFIED`, `MATCHED`, `FORWARDED`, `RETURNED` |
| `search` | string | Không | Source không đặt max length ở handler này |
| `tripId` | uuid | Không | — |
| `page` | int | Không | 1 |
| `pageSize` | int | Không | 20, 1–100 |

```bash
curl "$BASE_URL/v1/operator/unidentified-packages?status=UNIDENTIFIED&page=1&pageSize=20" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/unidentified-packages?status=UNIDENTIFIED&page=1&pageSize=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`, page item:

```json
{
  "packageId": "00000000-0000-0000-0000-000000002001",
  "temporaryExceptionTag": "TMP-BEN-B-001",
  "operatorId": "00000000-0000-0000-0000-000000000301",
  "status": "UNIDENTIFIED",
  "locationType": "WAREHOUSE",
  "locationId": "00000000-0000-0000-0000-000000000502",
  "matchedParcelId": null,
  "createdAt": "2026-08-21T10:00:00+07:00",
  "tripId": "00000000-0000-0000-0000-000000000401",
  "locationSnapshot": "Kho bến B",
  "description": "Thùng carton nâu",
  "observedWeightKg": 4.2,
  "evidenceReferences": ["https://cdn.example/unidentified.jpg"],
  "createdByUserId": "00000000-0000-0000-0000-000000000601",
  "matchedAt": null,
  "matchedByUserId": null,
  "trip": null,
  "matchedParcel": null,
  "availableActions": ["VIEW_MATCH_CANDIDATES", "MATCH"]
}
```

Trip enrichment lỗi sẽ để `trip=null`, không fail list. Lỗi: `422 VALIDATION_ERROR` cho status/paging; cộng auth/forbidden.

### 10.2. Chi tiết kiện chưa định danh

`GET {BASE_URL}/v1/operator/unidentified-packages/{packageId}`

`packageId` path uuid bắt buộc.

```bash
curl "$BASE_URL/v1/operator/unidentified-packages/$PACKAGE_ID" -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/unidentified-packages/${packageId}`, { headers: auth });
const result = await response.json();
```

Thành công `200`, `data` cùng full item §10.1; khi đã match, `matchedParcel` có `ReliabilityParcelSummary` nếu Parcel còn tồn tại. Lỗi: `404 UNIDENTIFIED_PACKAGE_NOT_FOUND`, `403 FORBIDDEN`; Trip enrichment lỗi chỉ trả `trip=null`.

### 10.3. Danh sách Parcel candidate để ghép

`GET {BASE_URL}/v1/operator/unidentified-packages/{packageId}/match-candidates?limit=20`

`limit` default 20, từ 1 đến 50. Nếu package status không còn `UNIDENTIFIED`, response thành công là array rỗng. API chỉ đề xuất, không tự match.

```bash
curl "$BASE_URL/v1/operator/unidentified-packages/$PACKAGE_ID/match-candidates?limit=20" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

```js
const response = await fetch(`${BASE_URL}/v1/operator/unidentified-packages/${packageId}/match-candidates?limit=20`, { headers: auth });
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": [{
    "parcelId": "00000000-0000-0000-0000-000000000201",
    "parcelCode": "VR-PCL-20260821-ABCD2345",
    "trip": { "tripId": "00000000-0000-0000-0000-000000000401", "status": null, "departureAt": null, "eta": null, "route": null, "vehicle": null, "stops": [] },
    "photoUrl": "https://cdn.example/p.jpg",
    "description": "Thùng carton nâu",
    "weightKg": 4.1,
    "expectedDropoff": { "type": "ROUTE_STOP", "id": "00000000-0000-0000-0000-000000000501", "name": null, "orderIndex": null, "eta": null },
    "matchReasons": ["SAME_TRIP_MANIFEST", "WEIGHT_WITHIN_TOLERANCE", "DESCRIPTION_SIMILAR"]
  }],
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

`WEIGHT_WITHIN_TOLERANCE` được thêm khi package có observed weight; handler không tự tính/kiểm tra tolerance ở bước map, candidate set đã do repository lọc. Lỗi: `404 UNIDENTIFIED_PACKAGE_NOT_FOUND`, `403 FORBIDDEN`, `422 VALIDATION_ERROR`.

### 10.4. Đăng ký kiện chưa định danh tại station

`POST {BASE_URL}/v1/stations/parcels/unidentified`

```json
{
  "temporaryExceptionTag": "TMP-BEN-B-001",
  "tripId": "00000000-0000-0000-0000-000000000401",
  "locationType": "WAREHOUSE",
  "locationId": "00000000-0000-0000-0000-000000000502",
  "locationSnapshot": "Kho bến B",
  "description": "Thùng carton nâu, tem rách",
  "observedWeightKg": 4.2,
  "evidenceReferences": ["https://cdn.example/unidentified.jpg"]
}
```

| Field | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `temporaryExceptionTag` | string | Có | Nonblank |
| `tripId` | uuid? | Không | — |
| `locationType` | enum | Có | `ParcelCustodyLocationType` |
| `locationId` | uuid | Có | Non-empty, kể cả location type `VEHICLE` ở entity này |
| `locationSnapshot` | string? | Không | Trim; blank thành null |
| `description` | string | Có | Nonblank |
| `observedWeightKg` | decimal? | Không | Nếu có phải >0 |
| `evidenceReferences` | string[] | Có về nghiệp vụ | Ít nhất 1 phần tử; DTO nullable nhưng handler chuyển null thành empty rồi validation fail |

Không có URL-format validation cho evidence string.

```bash
curl -X POST "$BASE_URL/v1/stations/parcels/unidentified" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{\"temporaryExceptionTag\":\"TMP-BEN-B-001\",\"tripId\":\"$TRIP_ID\",\"locationType\":\"WAREHOUSE\",\"locationId\":\"$LOCATION_ID\",\"locationSnapshot\":\"Kho bến B\",\"description\":\"Thùng carton nâu\",\"observedWeightKg\":4.2,\"evidenceReferences\":[\"https://cdn.example/unidentified.jpg\"]}"
```

```js
const response = await fetch(`${BASE_URL}/v1/stations/parcels/unidentified`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ temporaryExceptionTag: "TMP-BEN-B-001", tripId, locationType: "WAREHOUSE", locationId, locationSnapshot: "Kho bến B", description: "Thùng carton nâu", observedWeightKg: 4.2, evidenceReferences: ["https://cdn.example/unidentified.jpg"] })
});
const result = await response.json();
```

Thành công `201`, `data` là full package §10.1 (enrichment `trip`/`matchedParcel` null ở response tạo). Lỗi riêng `422 VALIDATION_ERROR`; cộng auth/idempotency.

### 10.5. Supervisor xác nhận ghép package với Parcel

`POST {BASE_URL}/v1/stations/parcels/unidentified/{packageId}/match`

```json
{ "parcelId": "00000000-0000-0000-0000-000000000201" }
```

`packageId` và `parcelId` là uuid bắt buộc. Package và Parcel phải cùng operator. Thành công set status `MATCHED`, tạo custody event `IDENTIFIED_MANUALLY` tại location của package. Backend không tự gọi endpoint này từ candidate list.

```bash
curl -X POST "$BASE_URL/v1/stations/parcels/unidentified/$PACKAGE_ID/match" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" -d "{\"parcelId\":\"$PARCEL_ID\"}"
```

```js
const response = await fetch(`${BASE_URL}/v1/stations/parcels/unidentified/${packageId}/match`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ parcelId })
});
const result = await response.json();
```

Thành công `200`, package có `matchedParcelId`, `matchedAt`, `matchedByUserId`, `status=MATCHED`; mapper trực tiếp sau mutation hiện vẫn trả `trip=null`, `matchedParcel=null`, `availableActions=[]`. Lỗi: `404 UNIDENTIFIED_PACKAGE_NOT_FOUND`, `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`. ⚠️ Match lại package không còn `UNIDENTIFIED` ném raw `InvalidOperationException`, hiện trả `500 INTERNAL_ERROR`; FE phải ẩn action theo `availableActions`/status và chống double submit.

### 10.6. Station custody handoff

`POST {BASE_URL}/v1/stations/parcels/{parcelId}/handoff`

Request dùng `[JsonUnmappedMemberHandling.Disallow]`: field lạ bị từ chối.

```json
{
  "parcelCode": "VR-PCL-20260821-ABCD2345",
  "eventType": "HANDOFF",
  "actualLocationType": "WAREHOUSE",
  "actualLocationId": "00000000-0000-0000-0000-000000000502",
  "locationSnapshot": "Kho bến B",
  "evidenceReferences": ["https://cdn.example/handoff.jpg"],
  "reason": "Nhận vào khu lưu giữ an toàn"
}
```

| Field | Kiểu | Bắt buộc | Rule |
|---|---|:---:|---|
| `parcelCode` | string | Có | Trim, so case-insensitive với Parcel |
| `eventType` | string | Có | Controller chỉ nhận `HANDOFF` hoặc `RETURNED_TO_STATION` |
| `actualLocationType` | enum | Có | `ParcelCustodyLocationType` |
| `actualLocationId` | uuid? | Có trừ `VEHICLE` | — |
| `locationSnapshot` | string? | Không | — |
| `evidenceReferences` | string[]? | Không | — |
| `reason` | string? | Không | — |

```bash
curl -X POST "$BASE_URL/v1/stations/parcels/$PARCEL_ID/handoff" \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d '{"parcelCode":"VR-PCL-20260821-ABCD2345","eventType":"HANDOFF","actualLocationType":"WAREHOUSE","actualLocationId":"00000000-0000-0000-0000-000000000502","locationSnapshot":"Kho bến B","evidenceReferences":[],"reason":"Nhận giữ"}'
```

```js
const response = await fetch(`${BASE_URL}/v1/stations/parcels/${parcelId}/handoff`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json", "Idempotency-Key": newKey() },
  body: JSON.stringify({ parcelCode: "VR-PCL-20260821-ABCD2345", eventType: "HANDOFF", actualLocationType: "WAREHOUSE", actualLocationId: locationId, locationSnapshot: "Kho bến B", evidenceReferences: [], reason: "Nhận giữ" })
});
const result = await response.json();
```

Thành công `200`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": { "custodyEventId": "00000000-0000-0000-0000-000000002101", "parcelId": "00000000-0000-0000-0000-000000000201", "eventType": "HANDOFF", "actualLocationType": "WAREHOUSE", "actualLocationId": "00000000-0000-0000-0000-000000000502", "occurredAt": "2026-08-21T12:00:00+07:00", "sequence": 8 },
  "meta": { "traceId": "...", "timestamp": "2026-08-21T12:00:00+07:00" }
}
```

Lỗi: `404 PARCEL_NOT_FOUND`, `403 FORBIDDEN`, `409 SCAN_IDENTITY_MISMATCH` với `error.fields.requiredAction=VERIFY_PARCEL_IDENTITY`, `422 PARCEL_CUSTODY_LOCATION_REQUIRED`, `422 VALIDATION_ERROR`. Retry cùng custody idempotency key trả lại event đã tồn tại; source không có error code duplicate riêng.

## 11. Flow UI và trách nhiệm của AI agent Operator FE

### 11.1. Incident → search → found → forwarding

1. Incident queue gọi duy nhất `GET /v1/operator/parcel-incidents`; render trực tiếp `parcel`, `trip`, `expectedDropoff`, `lastCustody`, `taskSummary`, `sla`, `availableActions`.
2. Detail gọi một lần và dùng `searchTasks`, `custodyTimeline`, user/trip summaries; khi load history cũ gửi `beforeSequence` từ sequence thấp nhất và giữ `limit<=100`.
3. Chỉ hiển thị mutation theo `availableActions`; không tự dựng state machine.
4. `ASSIGN` → assign; `RECORD_SEARCH` → search-scan; mọi mutation trả detail mới để replace cache, không refetch.
5. `MARK_FOUND` bắt buộc actual location/evidence từ UI.
6. Sau `FOUND`, gọi forwarding-options một lần; disable option `canReserve=false` và hiển thị `unavailableReason`.
7. Forward xong chờ crew confirm transfer theo Driver app; Operator detail theo dõi `forwardingOperation.cargoTransferStatus` và `nextHandoffAction`.
8. Chỉ resolve khi backend trả `RESOLVE`. Nếu search hết SLA, backend mới trả/cho phép `DECLARE_LOST`; không gọi lost ngay khi thiếu scan.

### 11.2. Lost → claim → payout

1. `LOST_CONFIRMED` chỉ làm claim eligible cho sender; Operator không tự tạo claim thay sender.
2. Claim queue là screen-ready; không đi từ từng incident sang claim detail.
3. Staff xem claim/evidence; chỉ admin thấy/được gọi decision theo route guard UI.
4. Luôn render policy snapshot trong claim, không dùng policy current để tính claim cũ.
5. FE có thể preview công thức nhưng số tiền response BE là source of truth.
6. Sau approve, trạng thái có thể là `APPROVED` rồi `FUNDING_PENDING` hoặc `PAID` qua event; không hiện “đã trả” chỉ vì decision thành công.

### 11.3. Không quét/không xác định được kiện

1. Tạo temporary tag và bắt buộc ít nhất một evidence photo trước station register.
2. Dùng list/detail/candidates; candidate chỉ là gợi ý.
3. Supervisor đối chiếu ảnh, mô tả, cân nặng, trip và expected stop rồi mới gọi match.
4. Khi match thành công, cập nhật row từ response nhưng nếu cần enrichment `matchedParcel`, gọi detail một lần; mutation mapper hiện trả null enrichment.
5. Không retry match bằng key mới khi status đã `MATCHED`; current handler sẽ 500.
6. Sau nhận giữ/chuyển station, ghi `HANDOFF`/`RETURNED_TO_STATION`; không sửa custody history cũ.

### 11.4. Checklist bắt buộc cho AI agent Operator FE

- Tạo ba route/module UI độc lập: Parcel operations, Reliability incidents/claims, unidentified/station; chia permission admin/staff đúng §2.3.
- Dùng screen-ready list contracts, không N+1 `GET detail` cho từng row.
- Giữ nguyên `camelCase`, VND integer và ISO timestamp có offset; dùng thư viện/kiểu số không làm mất chính xác int64.
- Mọi mutation sinh UUID v4 và giữ key khi retry cùng payload.
- Centralize `ApiResponse` parsing; với CSV/XLSX phải parse `blob`, chỉ parse JSON khi `!response.ok`.
- Centralize 401 refresh single-flight; 403 không refresh lặp.
- Hiển thị `error.fields`, đặc biệt `requiredAction`; không hiển thị raw message như business truth nếu đã có `code`.
- Không hiển thị PII/evidence ngoài operator tenant; không đưa access token, evidence URL hoặc delivery token vào log/analytics.
- Dùng `availableActions` làm nguồn quyền/state ở incident, claim, unidentified. Route role guard vẫn cần kiểm tra ở client để UX tốt nhưng backend là enforcement cuối.
- Feature flag Reliability khi production spec chưa có các route §1.
- Contract tests tối thiểu: incident one-call page, detail cursor, assign/search mutation no-refetch, mark-found, forwarding options, forward, lost-before/after-SLA, claim approve/reject, policy below-default acknowledge, unidentified candidate/match, handoff QR mismatch, report blob download, cross-tenant 403/404.

## 12. Đối chiếu source

Đã rà soát:

- Đủ 41 public operations dành cho `OPERATOR_ADMIN`/`OPERATOR_STAFF` trong Parcel local OpenAPI/controller; không đưa internal endpoints hoặc public delivery-token endpoints vào tài liệu Operator.
- Method/path/role khớp controller và Gateway route; mutation khớp idempotency middleware.
- Query keys/default/range khớp controller và handler, kể cả `AllowedQueryParameters`.
- Body field giữ đúng casing/request record; validation khớp FluentValidation, handler guard và Domain constructor.
- Response field khớp response records/read-model mapper; file endpoints được tách khỏi `ApiResponse`.
- Error code khớp coded exceptions; raw Domain/`ArgumentException` đang thành `500 INTERNAL_ERROR` đã được đánh dấu thay vì đoán mã 4xx.
- Production OpenAPI đã được so sánh với local; nhóm Reliability chưa deploy đã cảnh báo ở §1.

⚠️ TODO: cần xác nhận bằng Gateway integration test liệu Throttler 120 request/phút có thực sự áp dụng cho raw proxy route Parcel hay không.
