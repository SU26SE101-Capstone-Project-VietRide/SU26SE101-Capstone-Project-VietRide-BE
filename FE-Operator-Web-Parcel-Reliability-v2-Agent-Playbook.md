# Operator Web — Parcel Reliability v2 FE Agent Playbook

> Tài liệu thực thi dành cho dev/AI agent Operator Web. Mục tiêu là xử lý incident/claim theo queue và action BE, không xây một state machine thứ hai ở FE.

## 1. Nguyên tắc bắt buộc

- Base URL production: `https://api.vietride.online`.
- Roles đọc queue: `OPERATOR_ADMIN`, `OPERATOR_STAFF`; claim decision, appeal decision và update policy chỉ `OPERATOR_ADMIN` theo controller.
- `operatorId`, reviewer và actor lấy từ JWT. Không có input `operatorId`, `reviewerUserId`, `supervisorApprovalUserId` cho người dùng nhập.
- Mọi mutation có `Idempotency-Key` dùng UUID; retry cùng ý định giữ nguyên key.
- Mọi list/detail đã là screen-ready read model. Không gọi từng Parcel/Trip/User theo mỗi row.
- Chỉ render CTA có trong `availableActions` và role phù hợp.
- `searchDeadline` nullable: incident chờ duyệt chưa chạy SLA.
- Operator không tự kết luận mất khi client countdown về 0; chỉ gọi declare-lost khi BE trả `DECLARE_LOST`.

## 2. Contract FE cũ phải sửa

| Thay đổi | Migration bắt buộc |
|---|---|
| Incident list là `PagedResult`, có `approvalStatus` filter | Bổ sung tab/filter pending approval; dùng pagination BE. |
| Row đã có parcel/trip/expectedDropoff/lastCustody/reporter/task/claim/SLA/actions | Xóa N+1 calls theo row. |
| Detail có `custodyExceptionApproval`, `forwardingOperation`, timeline cursor và screen data | Mutation trả updated detail; patch màn hình trực tiếp. |
| `searchDeadline` nullable | Pending approval không hiển thị SLA/countdown. |
| Claim có queue/detail độc lập | Không duyệt từng incident để tìm claim. |
| Forwarding có options API | Không tự lọc route/capacity ở browser. |
| Policy response có platform default và cờ below-default | Không hard-code 50%/30 triệu trong UI logic. |
| Trip được chọn cho Parcel/forwarding bắt buộc có Assistant | Hiển thị `unavailableReason`; không cho chọn trip bị loại. |
| Destination arrival không tự mở MISSING | Không tạo local missing event khi thấy Trip tới đích. |

## 3. Incident queue

```http
GET /v1/operator/parcel-incidents?status=&type=&search=&tripId=&assigneeId=&slaState=&approvalStatus=&from=&to=&page=1&pageSize=20
```

`approvalStatus` dùng để tách báo cáo custody đang chờ duyệt. Mỗi row trả:

```text
incidentId, parcelId, operatorId, type, status, tripId
lastKnownLocation, searchDeadline, createdAt, operatorProcessBreach
parcel, trip, expectedDropoff, lastCustody, reporter
taskSummary, claimSummary, sla, availableActions
```

UI không gọi `GET /v1/operator/parcels/{parcelId}`, Trip detail hoặc Identity user cho từng row. Nếu enrichment nullable, vẫn render ID/snapshot có sẵn.

SLA UI:

- `searchDeadline == null`: “Chờ phê duyệt/xác minh”, không quá hạn.
- Có `sla.state`: dùng đúng state BE.
- Không tự đổi incident status khi browser timer hết.

## 4. Incident detail

```http
GET /v1/operator/parcel-incidents/{incidentId}?limit=50
```

Timeline cũ hơn dùng `beforeSequence`/cursor contract hiện tại; không tải toàn bộ history khi mở màn hình.

Detail gồm:

```text
incident, searchTasks
expectedLocation, resolutionCode/note/resolvedAt
currentCustody, custodyTimeline
claim, parcel, sender, recipient, trip, expectedDropoff, reporter
forwardingSummary, availableActions
forwardingOperation, custodyExceptionApproval
```

Operator có thể thấy actor/evidence nội bộ trong detail theo tenant; không copy nguyên object này sang Passenger screen.

## 5. Duyệt custody exception

Assistant report trước; Operator Staff/Admin có thể duyệt theo incident:

```http
POST /v1/operator/parcel-incidents/{incidentId}/custody-exception-decision
Idempotency-Key: <uuid>
```

```json
{
  "decision": "APPROVE",
  "note": "Đã đối soát crew và camera bến"
}
```

`decision`: `APPROVE` hoặc `REJECT`.

- Không truyền reviewer UUID; BE lấy từ JWT.
- Pending: incident `OPEN`, `searchDeadline = null`, chưa có search tasks/custody event được duyệt.
- Approve: ghi manual custody event, chuyển search flow và bắt đầu SLA.
- Reject: đóng báo cáo và khôi phục Parcel về trạng thái trước report.
- Response trả request/incident cập nhật; không cần refetch ngay.

Operator Web và Driver có hai route duyệt khác nhau nhưng cùng nguyên tắc JWT. Không gọi crew route bằng Operator token.

## 6. Search workflow

### 6.1. Assign

Chỉ render khi có `ASSIGN`:

```http
POST /v1/operator/parcel-incidents/{incidentId}/assign
```

```json
{ "assigneeUserId": "uuid" }
```

### 6.2. Ghi kết quả task

Chỉ render khi có `RECORD_SEARCH`:

```http
POST /v1/operator/parcel-incidents/{incidentId}/search-scan
```

```json
{
  "taskId": "uuid",
  "found": false,
  "result": "Đã kiểm tra khoang hành lý, chưa thấy kiện",
  "evidenceReferences": []
}
```

### 6.3. Mark found tại vị trí ngoài xe

Chỉ render khi có `MARK_FOUND`:

```http
POST /v1/operator/parcel-incidents/{incidentId}/mark-found
```

```json
{
  "actualLocationType": "WAREHOUSE",
  "actualLocationId": "uuid-or-null",
  "locationSnapshot": "Kho bến Bình Dương",
  "evidenceReferences": ["https://..."],
  "note": "Đã đối chiếu QR và ảnh kiện"
}
```

Trường hợp tìm thấy ngay trên source vehicle nên để Assistant dùng `confirm-found-on-vehicle`, vì endpoint đó xác minh QR, resolve incident và khôi phục transport state nguyên tử. Operator không giả lập action này bằng mark-found nếu crew có thể xác nhận trên xe.

Tất cả mutation trên trả incident detail cập nhật; thay local state bằng response.

## 7. Wrong station và forwarding

Khi incident ở `FOUND` và có `FORWARD`:

### Bước 1 — lấy phương án

```http
GET /v1/operator/parcel-incidents/{incidentId}/forwarding-options?limit=20
```

BE tự dùng actual location, expected stop, size/weight và operator. Response mỗi option:

```text
trip, route, vehicle
pickupLocation, targetDropoff
departureAt, eta
canReserve, unavailableReason
```

Không tự join Trip API hoặc tự suy luận route/cargo. Nếu Trip service lỗi, endpoint có thể trả `503`; không cho chọn một Trip tùy ý.

### Bước 2 — forward

```http
POST /v1/operator/parcel-incidents/{incidentId}/forward
Idempotency-Key: <uuid>
```

```json
{ "targetTripId": "uuid-from-forwarding-options" }
```

Target Trip phải cùng operator, route/cargo tương thích và có Assistant. Response detail có `forwardingOperation`:

```text
targetTrip
newLeg
cargoTransferStatus
nextHandoffAction
```

UI chuyển sang “Chờ crew chuyến mới xác nhận bàn giao”. Không đánh dấu delivered và không sửa leg cũ. Crew target gọi confirm-transfer bằng QR rồi vận chuyển/unload đúng điểm.

### Nếu chưa có option

Giữ hàng tại kho, tiếp tục search/task hoặc dùng return flow được BE cho phép. Không tạo form nhập targetTripId thủ công.

## 8. Resolve và declare lost

Resolve chỉ khi có `RESOLVE`:

```json
{
  "resolutionCode": "DELIVERED_TO_CORRECT_LOCATION",
  "note": "Đã giao về đúng bến"
}
```

tới `POST /v1/operator/parcel-incidents/{incidentId}/resolve`.

Declare lost chỉ khi có `DECLARE_LOST`:

```json
{
  "resolutionCode": "LOST_CONFIRMED",
  "note": "Đã hoàn tất toàn bộ search task và hết SLA"
}
```

tới `POST /v1/operator/parcel-incidents/{incidentId}/declare-lost`.

Không tự gọi declare-lost chỉ vì:

- Trip tới destination;
- GPS sai/thiếu;
- parcel chưa scan ở một stop;
- incident còn pending approval;
- countdown FE vừa về 0 nhưng BE chưa trả action.

`LOST_CONFIRMED` nằm ở Incident, không thêm/giả lập `ParcelStatus = LOST`.

## 9. Stop departure approval cho Operator

Khi Assistant reconcile stop còn unresolved và xin override, Operator đọc:

```http
GET /v1/operator/parcel-stop-departure-approvals/{requestId}
```

Quyết định:

```http
POST /v1/operator/parcel-stop-departure-approvals/{requestId}/decision
Idempotency-Key: <uuid>
```

```json
{
  "decision": "APPROVE",
  "note": "Cho phép rời stop sau khi đã mở search task"
}
```

Không nhập reviewer ID. Approval này chỉ cho Trip rời điểm theo clearance; không có nghĩa Parcel đã mất và không được tự tạo claim.

## 10. Claim queue và detail

### Queue

```http
GET /v1/operator/claims?status=&search=&slaState=&from=&to=&page=1&pageSize=20
```

Mỗi row có parcel, sender, incident, evidence count, policy snapshot, award, deadline, funding status, trip và actions. Không scan incident queue để dựng claim queue.

### Detail

```http
GET /v1/operator/claims/{claimId}
```

Response có `claim`, `parcel`, `incident`, `currentCustody`, `trip`, `expectedDropoff`, `beneficiary`, `fundingStatus`, `availableActions`.

### Decision — chỉ OPERATOR_ADMIN

```http
POST /v1/operator/claims/{claimId}/decision
Idempotency-Key: <uuid>
```

Approve:

```json
{
  "decision": "APPROVE",
  "provenDirectLossVnd": 12000000,
  "reason": "Hóa đơn và ảnh kiện hợp lệ"
}
```

Reject:

```json
{
  "decision": "REJECT",
  "provenDirectLossVnd": null,
  "reason": "Chứng từ không khớp kiện hàng"
}
```

Không cho Operator Staff thấy nút quyết định nếu role không phải Admin, kể cả action/data tồn tại do cache cũ.

## 11. Công thức bồi thường và funding

Policy snapshot của Parcel/Claim là nguồn sự thật:

```text
assessedLoss = min(provenDirectLossVnd, declaredValueVnd nếu có)
grossCompensation = assessedLoss × compensationRatePercent / 100
cargoAwardVnd = min(grossCompensation, policyCapVnd)
totalAwardVnd = cargoAwardVnd + freightRefundVnd
```

Không có chứng từ:

```text
fallback cargo award = min(noProofFallbackMultiplier × parcel freight, policy cap)
```

`noProofFallbackMultiplier = 4` nghĩa là khi không có chứng từ hợp lệ, phần bồi thường hàng được tính theo tối đa bốn lần cước Parcel trước khi áp cap; không phải “đền gấp bốn giá trị hàng”.

FE không gửi `cargoAwardVnd`, `freightRefundVnd`, `totalAwardVnd`; BE tính và snapshot. `FUNDING_PENDING` nghĩa operator chưa đủ nguồn, VietRide không ứng trước; hiển thị trạng thái chờ nguồn, không đánh dấu paid.

## 12. Appeal queue

```http
GET /v1/operator/claim-appeals?status=&page=1&pageSize=20
GET /v1/operator/claim-appeals/{appealId}
```

Decision chỉ OPERATOR_ADMIN:

```http
POST /v1/operator/claim-appeals/{appealId}/decision
Idempotency-Key: <uuid>
```

```json
{
  "decision": "APPROVE_ADJUSTMENT",
  "revisedProvenDirectLossVnd": 15000000,
  "reason": "Chứng từ bổ sung hợp lệ"
}
```

`decision` chỉ `UPHOLD` hoặc `APPROVE_ADJUSTMENT`; `revisedProvenDirectLossVnd` nếu có phải `>= 0`; reason bắt buộc, tối đa 2000.

## 13. Compensation policy

Đọc:

```http
GET /v1/operator/policies/parcel-compensation
```

Update chỉ Admin:

```http
PUT /v1/operator/policies/parcel-compensation
Idempotency-Key: <uuid>
```

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

Response có `platformDefaultPolicy`, `isBelowPlatformDefault`, `effectiveForNewParcelsOnly`, `updatedAt`, `updatedBy`. Nếu below default, UI yêu cầu acknowledgement theo response/rule BE. Policy mới chỉ áp dụng Parcel mới; không đổi snapshot Parcel/Claim cũ.

## 14. Unidentified packages

Queue:

```http
GET /v1/operator/unidentified-packages
GET /v1/operator/unidentified-packages/{packageId}
GET /v1/operator/unidentified-packages/{packageId}/match-candidates
```

Candidates cung cấp dữ liệu đối chiếu; BE không tự match. Sau khi supervisor chọn đúng Parcel:

```http
POST /v1/stations/parcels/unidentified/{packageId}/match
Idempotency-Key: <uuid>
```

```json
{ "parcelId": "confirmed-parcel-uuid" }
```

Không tạo nút “Tự động ghép tốt nhất”. Phải có màn so sánh ảnh, mô tả, cân, trip và expected stop trước khi xác nhận.

## 15. Các API Parcel operation khác trên Operator Web

Các endpoint `/v1/operator/parcels` vẫn phục vụ quản lý đơn, review, transfer/return/cancel/refund/capacity/manual delivery. Không dùng chúng để thay incident workflow:

- `request-transfer` là operational recovery được BE cho phép, không thay `/parcel-incidents/{id}/forward` khi xử lý wrong station.
- `PATCH /status` không được dùng để giả lập LOST/FOUND/SEARCHING.
- `confirm-delivery`/`manual-confirm` không được dùng để đóng incident nếu kiện chưa thực sự giao.
- Luôn ưu tiên `availableActions` ở incident/detail để chọn đúng mutation.

## 16. CTA mapping bắt buộc

| Action | CTA | Endpoint |
|---|---|---|
| `ASSIGN` | Giao người xử lý | `/assign` |
| `RECORD_SEARCH` | Cập nhật kết quả tìm kiếm | `/search-scan` |
| `MARK_FOUND` | Xác nhận tìm thấy tại vị trí | `/mark-found` |
| `FORWARD` | Chọn chuyến chuyển tiếp | GET options rồi POST forward |
| `RESOLVE` | Đóng sự cố đã xử lý | `/resolve` |
| `DECLARE_LOST` | Xác nhận thất lạc | `/declare-lost` |
| `DECIDE_CLAIM` | Duyệt/từ chối claim | claim decision, Admin-only |

Pending custody approval lấy từ `custodyExceptionApproval`/filter, dùng custody exception decision. Không suy diễn nó từ action search.

## 17. Error/upstream handling

- `401`: refresh auth rồi retry một lần.
- `403`: sai role/operator; không cho user chọn tenant khác.
- `404`: resource tenant khác có thể bị che; quay queue.
- `409`: state đã đổi hoặc action chưa hợp lệ; thay màn hình bằng một detail GET mới.
- `422`: validation/policy acknowledgement/body sai; map `error.fields`.
- `503 UPSTREAM_UNAVAILABLE`: giữ Parcel read data, cho retry. Riêng forwarding options không được fallback tự chọn Trip.
- Mutation timeout: retry với cùng `Idempotency-Key`, không sinh key mới.

## 18. Checklist giao cho AI agent Operator Web

- [ ] Dùng incident/claim queues screen-ready, loại bỏ N+1 calls.
- [ ] Thêm filter/tab `approvalStatus` và xử lý nullable deadline.
- [ ] Không còn reviewer/supervisor/operator UUID trong body.
- [ ] Chỉ Admin thấy claim/appeal decision và policy update.
- [ ] Forwarding bắt buộc GET options trước POST forward.
- [ ] Không tự tính award hoặc tự đổi funding state.
- [ ] Không tự declare lost theo GPS, destination arrival hoặc timer FE.
- [ ] Không dùng generic parcel status API để giả incident states.
- [ ] Candidate unidentified package luôn cần supervisor xác nhận.
- [ ] Patch detail từ mutation response; refetch chỉ khi conflict/stale state.
- [ ] Test cross-tenant 404/403, pending approval, found-on-vehicle handoff, wrong-station forwarding, search expiry, claim 12 triệu, cap 30 triệu, no-proof và funding pending.
