# Hướng dẫn sửa Operator Web — duyệt báo cáo Custody Exception

> Backend contract áp dụng: commit `c6306059` trở lên, ngày 2026-08-28.

## 1. Mục đích

Tài liệu này dành cho FE Operator Web và AI agent phụ trách Admin/Operator Web. Phạm vi là quy trình mới:

```text
Assistant báo cáo sự cố
  → PENDING_APPROVAL
  → Operator Staff/Admin xem bằng chứng
  → APPROVE hoặc REJECT bằng JWT của chính người duyệt
```

Operator Web **cần cập nhật**. Driver không phải bên duy nhất được duyệt. Backend cho phép cả hai role sau dùng endpoint Operator:

```text
OPERATOR_STAFF
OPERATOR_ADMIN
```

Riêng quyết định claim bồi thường vẫn chỉ dành cho `OPERATOR_ADMIN`; không được dùng rule của custody exception cho claim.

## 2. Những điểm FE phải sửa ngay

1. Incident queue phải đọc `availableActions` của từng row. Khi có `APPROVE`/`REJECT`, hiển thị trạng thái “Chờ duyệt báo cáo”.
2. Khi mở detail, dùng `custodyExceptionApproval` làm nguồn dữ liệu cho location, lý do, ảnh/bằng chứng và người báo cáo.
3. Không yêu cầu người dùng nhập hoặc chọn UUID người duyệt.
4. Không gửi `reviewerUserId`, `reviewedByUserId`, `supervisorApprovalUserId` hoặc `operatorId` trong body.
5. Backend lấy `reviewedByUserId`, `reviewedByRole` và `operatorId` từ JWT của người đang đăng nhập.
6. Mọi decision mutation phải gửi `Idempotency-Key` là UUID.
7. Khi request còn `PENDING_APPROVAL`, không hiển thị SLA tìm kiếm, không cho assign task, mark found, forward, declare lost hoặc mở claim.
8. Sau approve, refetch incident detail một lần để lấy đầy đủ hai search task vừa được backend tạo.
9. Sau reject, đóng incident khỏi queue đang xử lý và không hiển thị custody event thủ công.
10. Nếu hai người duyệt đồng thời, client nhận `PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED` phải refetch detail và hiển thị quyết định đã được ghi nhận, không retry bằng key mới.

## 3. Base URL, auth và response envelope

### Base URL

```text
Production: https://api.vietride.online
Local Gateway: http://localhost:3000
```

FE phải lấy base URL từ cấu hình môi trường hiện có, không hard-code URL production trong source.

### Headers

Read API:

```http
Authorization: Bearer <operatorAccessToken>
Accept: application/json
```

Mutation API:

```http
Authorization: Bearer <operatorAccessToken>
Content-Type: application/json
Idempotency-Key: <uuid-v4>
```

### Success envelope

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "...",
    "timestamp": "2026-08-28T10:00:00+00:00"
  }
}
```

### Error envelope

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED",
    "message": "Custody exception request has already been decided.",
    "fields": []
  },
  "meta": {
    "traceId": "...",
    "timestamp": "2026-08-28T10:00:00+00:00"
  }
}
```

`error.fields` là một mảng nếu có validation field errors. Không parse field này như object key-value.

## 4. API Operator Web cần dùng

| Chức năng | Method | Path |
|---|---|---|
| Danh sách incident | `GET` | `/v1/operator/parcel-incidents` |
| Chi tiết incident và báo cáo chờ duyệt | `GET` | `/v1/operator/parcel-incidents/{incidentId}` |
| Duyệt/từ chối custody exception | `POST` | `/v1/operator/parcel-incidents/{incidentId}/custody-exception-decision` |
| Phân công search task | `POST` | `/v1/operator/parcel-incidents/{incidentId}/assign` |
| Ghi kết quả search task | `POST` | `/v1/operator/parcel-incidents/{incidentId}/search-scan` |
| Đánh dấu tìm thấy | `POST` | `/v1/operator/parcel-incidents/{incidentId}/mark-found` |
| Lấy chuyến forwarding phù hợp | `GET` | `/v1/operator/parcel-incidents/{incidentId}/forwarding-options` |
| Forward hàng về đúng điểm | `POST` | `/v1/operator/parcel-incidents/{incidentId}/forward` |
| Xác nhận mất | `POST` | `/v1/operator/parcel-incidents/{incidentId}/declare-lost` |
| Resolve incident | `POST` | `/v1/operator/parcel-incidents/{incidentId}/resolve` |

Tài liệu này mô tả chi tiết ba API đầu. Các action tìm kiếm/forwarding chỉ được mở sau khi approval thành công và phải dựa trên `availableActions` backend trả về.

## 5. Danh sách incident

```http
GET /v1/operator/parcel-incidents?page=1&pageSize=20
Authorization: Bearer <operatorAccessToken>
```

### Query params

| Param | Kiểu | Bắt buộc | Mặc định/ràng buộc |
|---|---|---:|---|
| `status` | string | Không | Enum incident status, không phân biệt hoa/thường |
| `type` | string | Không | Enum incident type, không phân biệt hoa/thường |
| `search` | string | Không | Tối đa 100 ký tự |
| `tripId` | UUID | Không | Lọc theo trip |
| `assigneeId` | UUID | Không | Lọc theo người được giao search task |
| `slaState` | string | Không | `NOT_STARTED`, `ON_TRACK`, `DUE_SOON`, `BREACHED`, `CLOSED` |
| `approvalStatus` | string | Không | `PENDING_APPROVAL`, `APPROVED`, `REJECTED`, `CANCELLED` |
| `from` | ISO-8601 datetime | Không | Thời điểm bắt đầu |
| `to` | ISO-8601 datetime | Không | Thời điểm kết thúc, được backend xử lý inclusive |
| `page` | integer | Không | Mặc định `1`, phải `>= 1` |
| `pageSize` | integer | Không | Mặc định `20`, từ `1` đến `100` |

Incident status hiện có:

```text
OPEN
SEARCHING
FOUND
FORWARDING
RESOLVED
CLOSED
ESCALATED
SEARCH_EXPIRED
LOST_CONFIRMED
```

Incident type hiện có:

```text
MISSING
WRONG_STOP
DELIVERY_NOT_RECEIVED
PARTIAL_LOSS
DAMAGED
SCAN_IDENTITY_MISMATCH
PACKAGE_IDENTITY_MISMATCH
UNSCANNED_HANDOFF
MISSING_AFTER_DEPARTURE
```

### Pagination response

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

### Nhận biết row chờ duyệt

Một custody exception đang chờ duyệt vẫn có incident status `OPEN`, nhưng backend trả:

```json
{
  "incidentId": "42f15a34-6a50-4a0a-a79c-5f90c819a12e",
  "parcelId": "cb1f063d-e5e8-437f-9d63-10382775935b",
  "type": "WRONG_STOP",
  "status": "OPEN",
  "searchDeadline": null,
  "taskSummary": {
    "completed": 0,
    "total": 0,
    "assignees": []
  },
  "sla": null,
  "availableActions": [
    "APPROVE",
    "REJECT"
  ]
}
```

FE phải xác định pending approval bằng `availableActions`, không chỉ bằng `status === "OPEN"`.

Quy tắc hiển thị:

| Dữ liệu | UI |
|---|---|
| Có `APPROVE` hoặc `REJECT` | Badge “Chờ duyệt báo cáo” và CTA mở detail |
| `searchDeadline = null` | Không hiện countdown |
| `sla = null` | Không tự tính SLA |
| `taskSummary.total = 0` | Không hiển thị “chưa hoàn thành task” như một lỗi |

Pending approval bị loại khỏi các filter SLA `ON_TRACK`, `DUE_SOON`, `BREACHED`. Tab “Chờ duyệt
báo cáo” gọi trực tiếp
`GET /v1/operator/parcel-incidents?approvalStatus=PENDING_APPROVAL&page=1&pageSize=20` và vẫn dùng
`availableActions` để quyết định CTA. Không cần tải toàn bộ queue rồi lọc ở FE.

Backend hiện chưa phát một push event riêng cho “pending custody approval”. Operator Web phải dùng cơ chế refresh/polling sẵn có của màn hình; backend không quy định một polling interval cố định.

### Search thực tế

`search` có thể khớp các dữ liệu backend đang hỗ trợ như parcel code, UUID incident/parcel/trip, last-known location, recipient, vehicle plate, route và sender được Identity Service tìm thấy. Không gọi API Identity hoặc Trip theo từng row; list response đã enrich dữ liệu khi upstream khả dụng.

## 6. Chi tiết incident và báo cáo chờ duyệt

```http
GET /v1/operator/parcel-incidents/{incidentId}?limit=50
Authorization: Bearer <operatorAccessToken>
```

Query timeline:

| Param | Kiểu | Bắt buộc | Ràng buộc |
|---|---|---:|---|
| `beforeSequence` | integer | Không | Nếu có, phải `>= 1` |
| `limit` | integer | Không | Mặc định `50`, từ `1` đến `100` |

Response detail có các nhóm dữ liệu sau:

```text
incident
searchTasks[]
expectedLocation
resolutionCode
resolutionNote
resolvedAt
currentCustody
custodyTimeline
claim
parcel
sender
recipient
trip
expectedDropoff
reporter
forwardingSummary
availableActions
forwardingOperation
custodyExceptionApproval
```

### Nguồn dữ liệu approval panel

Chỉ `custodyExceptionApproval` có toàn bộ dữ liệu báo cáo. Queue row không chứa evidence chi tiết.

```json
{
  "requestId": "5e0a2a19-ce8d-4f90-97dc-5446cd517d7b",
  "parcelId": "cb1f063d-e5e8-437f-9d63-10382775935b",
  "incidentId": "42f15a34-6a50-4a0a-a79c-5f90c819a12e",
  "incidentType": "WRONG_STOP",
  "incidentStatus": "OPEN",
  "status": "PENDING_APPROVAL",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "3ce01b86-713a-4c44-bc65-6e6f2ef4640a",
  "locationSnapshot": "Bến xe Miền Đông",
  "temporaryExceptionTag": null,
  "description": "Kiện được phát hiện ở bến không đúng điểm trả",
  "observedWeightKg": 5.5,
  "evidenceReferences": [
    "https://example.com/wrong-stop-photo.jpg"
  ],
  "reason": "Assistant báo kiện đã bị dỡ ngoài luồng chuẩn",
  "reportedByUserId": "1fbd103a-1ac3-4bda-a253-c53f54676644",
  "reportedByRole": "ASSISTANT",
  "reportedAt": "2026-08-28T10:00:00+00:00",
  "reviewedByUserId": null,
  "reviewedAt": null,
  "reviewedByRole": null,
  "reviewNote": null,
  "approvedCustodyEventId": null,
  "searchDeadline": null,
  "availableActions": [
    "APPROVE",
    "REJECT"
  ]
}
```

### Approval panel phải hiển thị

- `incidentType`;
- `actualLocationType`, `actualLocationId`, `locationSnapshot`;
- `description`, `observedWeightKg`, `temporaryExceptionTag`;
- `reason`;
- toàn bộ `evidenceReferences`;
- `reportedByUserId`, `reportedByRole`, `reportedAt`;
- parcel, trip, expected dropoff và current custody từ detail response để đối chiếu.

Không được lấy `actualLocationId` làm UUID người báo cáo hoặc UUID người duyệt. Đây là ID location.

### Timeline cursor

`custodyTimeline.items` trả tối đa `limit` event mới nhất. Nếu `custodyTimeline.nextCursor` khác `null`, gọi tiếp:

```http
GET /v1/operator/parcel-incidents/{incidentId}?beforeSequence={nextCursor}&limit=50
```

Không nối trùng event khi tải thêm timeline; dùng `eventId` hoặc `sequence` làm key.

## 7. Approve hoặc reject custody exception

```http
POST /v1/operator/parcel-incidents/{incidentId}/custody-exception-decision
Authorization: Bearer <operatorAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

### Body chính xác

Approve:

```json
{
  "decision": "APPROVE",
  "note": "Đã đối chiếu ảnh, manifest và xác nhận vị trí báo cáo"
}
```

Reject:

```json
{
  "decision": "REJECT",
  "note": "Ảnh/camera cho thấy kiện vẫn nằm trên xe"
}
```

Validation thực tế:

| Field | Kiểu | Bắt buộc | Rule |
|---|---|---:|---|
| `decision` | string | Có | Backend normalize trim + uppercase; chỉ nhận `APPROVE` hoặc `REJECT` |
| `note` | string/null | Không | Tối đa 2000 ký tự |

Body dùng strict JSON contract. Field lạ bị từ chối. Không gửi:

```text
reviewerUserId
reviewedByUserId
supervisorApprovalUserId
operatorId
requestId
```

### Response approve

HTTP `200`; `data` là approval response, không phải full incident detail:

```json
{
  "status": "APPROVED",
  "incidentStatus": "SEARCHING",
  "reviewedByUserId": "<user-id-from-jwt>",
  "reviewedByRole": "OPERATOR_STAFF",
  "reviewedAt": "2026-08-28T10:05:00+00:00",
  "reviewNote": "Đã đối chiếu ảnh, manifest và xác nhận vị trí báo cáo",
  "approvedCustodyEventId": "a347c925-128c-4566-9219-dc08783a6536",
  "searchDeadline": "2026-08-31T10:05:00+00:00",
  "availableActions": [
    "CONTINUE_SEARCH"
  ]
}
```

Approve làm backend thực hiện nguyên tử các bước nghiệp vụ:

1. Ghi custody event `MANUAL_CUSTODY_EXCEPTION`.
2. Đánh dấu `operatorProcessBreach` cho incident.
3. Chuyển incident sang `SEARCHING`.
4. Bắt đầu SLA theo policy snapshot của Parcel.
5. Tạo hai search task mặc định: `MANIFEST_RECONCILIATION` và `VEHICLE_SWEEP`.
6. Phát integration event `parcel.incident.opened`.

Sau approve, FE nên invalidate/refetch:

```text
GET /v1/operator/parcel-incidents/{incidentId}
GET /v1/operator/parcel-incidents (row trong queue)
```

Detail refetch là cần thiết nếu màn hình muốn có full `searchTasks[]`, vì decision response không chứa task list.

### Response reject

HTTP `200`:

```json
{
  "status": "REJECTED",
  "incidentStatus": "RESOLVED",
  "reviewedByUserId": "<user-id-from-jwt>",
  "reviewedByRole": "OPERATOR_ADMIN",
  "reviewedAt": "2026-08-28T10:05:00+00:00",
  "reviewNote": "Ảnh/camera cho thấy kiện vẫn nằm trên xe",
  "approvedCustodyEventId": null,
  "availableActions": []
}
```

Reject không tạo custody event. Backend resolve incident, hủy task còn mở nếu có và khôi phục Parcel khỏi `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION` nếu trạng thái vẫn hợp lệ.

`searchDeadline` trong response reject có thể còn giá trị audit của incident, nhưng không còn hiệu lực vì `incidentStatus = RESOLVED`. FE không hiển thị countdown cho incident đã resolve.

## 8. State machine UI

```text
PENDING_APPROVAL
  incidentStatus = OPEN
  searchDeadline = null
  availableActions = [APPROVE, REJECT]
      ├─ APPROVE
      │    status = APPROVED
      │    incidentStatus = SEARCHING
      │    approvedCustodyEventId != null
      │    searchDeadline != null
      │    → mở workflow search/forwarding
      └─ REJECT
           status = REJECTED
           incidentStatus = RESOLVED
           approvedCustodyEventId = null
           → đóng workflow
```

Không dùng riêng `incident.status` để quyết định CTA. Nguồn quyết định cuối cùng là `availableActions` từ backend.

Ví dụ resolver:

```ts
function getCustodyApprovalUi(detail: ParcelIncidentDetail) {
  const approval = detail.custodyExceptionApproval;
  const actions = new Set(detail.availableActions ?? []);

  if (!approval) return { kind: 'NONE' as const };

  if (
    approval.status === 'PENDING_APPROVAL' &&
    actions.has('APPROVE') &&
    actions.has('REJECT')
  ) {
    return { kind: 'REVIEW_REQUIRED' as const, approval };
  }

  if (approval.status === 'APPROVED') {
    return { kind: 'APPROVED' as const, approval };
  }

  return { kind: 'CLOSED' as const, approval };
}
```

## 9. Error handling bắt buộc

| HTTP | `error.code` | Khi nào | FE xử lý |
|---:|---|---|---|
| 401 | auth error chung | Token thiếu/hết hạn/không hợp lệ | Dùng flow refresh/login hiện có; không retry mutation với token cũ |
| 403 | `FORBIDDEN` | Thiếu operator scope, sai tenant hoặc role không được phép | Đóng action, báo không có quyền; không thử endpoint Driver/Assistant |
| 404 | `PARCEL_INCIDENT_NOT_FOUND` | Incident không tồn tại | Đóng detail và refetch queue |
| 404 | `PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND` | Không có approval request cho incident hoặc tenant-mask | Refetch detail; không dựng approval giả |
| 409 | `PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED` | Người khác đã approve/reject | Refetch detail và hiển thị reviewer/decision thật |
| 409 | `PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED` | Gọi search/found/forward/lost khi request chưa duyệt | Đưa người dùng về approval panel |
| 409 | `INVALID_STATUS` | Parcel không còn ở trạng thái chờ phù hợp khi reject/transition | Refetch detail; không retry tự động |
| 422 | `VALIDATION_ERROR` | Decision/note/query/body không hợp lệ hoặc body có field lạ | Map `error.fields`; giữ form để người dùng sửa |
| 503 | `UPSTREAM_UNAVAILABLE` hoặc dependency code | Upstream cần cho query/action không khả dụng | Cho retry có kiểm soát; giữ `traceId` để support |

Các action incident khác còn có các code riêng như `PARCEL_INCIDENT_INVALID_STATUS`, `PARCEL_SEARCH_TASK_NOT_FOUND`, `PARCEL_SEARCH_TASK_MISMATCH`. FE phải hiển thị `error.message` và refetch khi lỗi cho biết state đã thay đổi.

## 10. Idempotency và xử lý hai người duyệt đồng thời

Quy tắc key:

```text
Một click nghiệp vụ mới → một UUID mới
Retry cùng request do timeout/mất mạng → dùng lại UUID cũ
Đổi từ APPROVE sang REJECT → đây là thao tác mới, dùng UUID mới
```

Không tạo UUID mới sau mỗi lần retry network, vì có thể làm FE gửi trùng nghiệp vụ.

Ví dụ helper:

```ts
type PendingDecision = {
  incidentId: string;
  decision: 'APPROVE' | 'REJECT';
  note: string | null;
  idempotencyKey: string;
};

function createPendingDecision(
  incidentId: string,
  decision: 'APPROVE' | 'REJECT',
  note: string | null,
): PendingDecision {
  return {
    incidentId,
    decision,
    note,
    idempotencyKey: crypto.randomUUID(),
  };
}
```

Nếu nhận `PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED`, không replay bằng UUID khác. Refetch detail để lấy `reviewedByUserId`, `reviewedByRole`, `reviewedAt` và `reviewNote` đã được lưu.

## 11. TypeScript contract tối thiểu

```ts
export type ApiMeta = {
  traceId: string;
  timestamp?: string;
};

export type ApiSuccess<T> = {
  success: true;
  statusCode: number;
  message?: string;
  data: T;
  meta: ApiMeta;
};

export type ApiError = {
  success: false;
  statusCode: number;
  error: {
    code: string;
    message: string;
    fields?: Array<{ field: string; message: string }>;
  };
  meta: ApiMeta;
};

export type CustodyExceptionApprovalStatus =
  | 'PENDING_APPROVAL'
  | 'APPROVED'
  | 'REJECTED'
  | 'CANCELLED';

export type CustodyExceptionApproval = {
  requestId: string;
  parcelId: string;
  incidentId: string;
  incidentType: string;
  incidentStatus: string;
  status: CustodyExceptionApprovalStatus;
  actualLocationType: string;
  actualLocationId: string | null;
  locationSnapshot: string | null;
  temporaryExceptionTag: string | null;
  description: string | null;
  observedWeightKg: number | null;
  evidenceReferences: string[];
  reason: string;
  reportedByUserId: string;
  reportedByRole: string;
  reportedAt: string;
  reviewedByUserId: string | null;
  reviewedAt: string | null;
  reviewedByRole: string | null;
  reviewNote: string | null;
  approvedCustodyEventId: string | null;
  searchDeadline: string | null;
  availableActions: string[];
};

export type DecideCustodyExceptionBody = {
  decision: 'APPROVE' | 'REJECT';
  note: string | null;
};
```

Không đổi field sang snake_case. Không thêm reviewer UUID vào `DecideCustodyExceptionBody`.

## 12. Ví dụ API client

```ts
export async function decideCustodyException(
  baseUrl: string,
  accessToken: string,
  incidentId: string,
  body: DecideCustodyExceptionBody,
  idempotencyKey: string,
): Promise<CustodyExceptionApproval> {
  const response = await fetch(
    `${baseUrl}/v1/operator/parcel-incidents/${incidentId}/custody-exception-decision`,
    {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(body),
    },
  );

  const envelope = (await response.json()) as
    | ApiSuccess<CustodyExceptionApproval>
    | ApiError;

  if (!response.ok || !envelope.success) {
    throw envelope;
  }

  return envelope.data;
}
```

Mutation state đề xuất:

```ts
async function submitDecision(pending: PendingDecision) {
  try {
    const approval = await decideCustodyException(
      API_BASE_URL,
      accessToken,
      pending.incidentId,
      { decision: pending.decision, note: pending.note },
      pending.idempotencyKey,
    );

    updateApprovalInStore(approval);
    await refetchIncidentDetail(pending.incidentId);
    invalidateIncidentQueue();
  } catch (error) {
    if (isApiCode(error, 'PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED')) {
      await refetchIncidentDetail(pending.incidentId);
      return;
    }

    throw error;
  }
}
```

## 13. Luồng sau khi tìm thấy hàng ở bến sai

Approval không phải bước cuối. Sau khi approve và search xác nhận hàng đang ở bến sai:

```text
APPROVE report
  → SEARCHING
  → hoàn thành search task
  → mark-found
  → lấy forwarding-options
  → chọn trip phù hợp
  → forward
  → theo dõi handoff trên leg mới
  → giao tại expectedDropoff
```

Operator Web không tự suy luận chuyến phù hợp. Phải gọi:

```http
GET /v1/operator/parcel-incidents/{incidentId}/forwarding-options?limit=20
```

Sau đó dùng `targetTripId` được chọn để gọi endpoint `forward`. Chỉ render các CTA này khi `availableActions` của detail cho phép. Không sửa leg cũ trên FE và không biến `lastKnownLocation` thành điểm giao mới.

## 14. Checklist nghiệm thu Operator Web

- [ ] Login bằng `OPERATOR_STAFF` xem được queue và detail thuộc operator của mình.
- [ ] Login bằng `OPERATOR_ADMIN` xem và quyết định được custody exception.
- [ ] Operator khác không xem/duyệt được incident ngoài tenant.
- [ ] Queue nhận biết pending bằng `availableActions = [APPROVE, REJECT]`.
- [ ] Pending không hiển thị countdown SLA khi `searchDeadline`/`sla` là `null`.
- [ ] Detail hiển thị đúng location, reason, weight, description và evidence từ `custodyExceptionApproval`.
- [ ] Form không có field UUID người duyệt.
- [ ] Request body chỉ gửi `decision` và `note`.
- [ ] Mutation có `Idempotency-Key` UUID.
- [ ] Approve trả reviewer từ JWT, chuyển incident sang `SEARCHING` và có deadline.
- [ ] Sau approve, detail refetch có hai search task mặc định.
- [ ] Reject chuyển incident sang `RESOLVED` và không có `approvedCustodyEventId`.
- [ ] Không cho search/mark-found/forward/declare-lost trước approval.
- [ ] Hai browser approve đồng thời: browser thua refetch khi nhận `PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED`.
- [ ] Token hết hạn đi qua auth refresh/login flow chung, không mất idempotency key của mutation đang retry.
- [ ] Timeline phân trang bằng `nextCursor`, không nối trùng event.
- [ ] Tìm thấy ở bến sai tiếp tục được xử lý bằng forwarding options và forward, không dừng ở mark-found.
- [ ] Claim decision chỉ hiện cho `OPERATOR_ADMIN`, không nhầm với quyền duyệt custody exception của `OPERATOR_STAFF`.

## 15. Phân công cho AI agent của FE Operator Web

### API/store

1. Thêm `CustodyExceptionApproval` và `DecideCustodyExceptionBody` đúng camelCase tại mục 11.
2. Thêm API client cho decision endpoint; bắt buộc truyền access token và idempotency key.
3. Mở rộng incident detail model bằng `custodyExceptionApproval`.
4. Giữ `availableActions`, `searchDeadline` và `sla` nullable đúng response.
5. Không thêm reviewer/operator UUID vào request model.

### Incident queue

1. Thêm badge/filter client-side “Chờ duyệt báo cáo” dựa trên `availableActions`.
2. Không xếp pending approval vào overdue chỉ vì incident đã tạo lâu.
3. Dùng metadata pagination backend; không gọi detail cho từng row.
4. Click row mới gọi một detail request để lấy evidence.

### Incident detail

1. Thêm approval panel từ `custodyExceptionApproval`.
2. Hiển thị parcel/trip/expected dropoff/current custody cạnh actual reported location để reviewer đối chiếu.
3. Render CTA từ `availableActions` thay vì tự dựng theo status.
4. Approve/reject cần confirmation và note; backend cho phép note null nhưng audit UI nên khuyến khích nhập lý do rõ ràng.
5. Sau approve refetch detail để lấy task; sau reject đóng/refresh row.

### Error/concurrency

1. Map các code tại mục 9.
2. Lưu idempotency key cùng mutation pending để retry đúng request.
3. Với `ALREADY_DECIDED`, refetch thay vì báo lỗi chung hoặc gửi lại.
4. Hiển thị `meta.traceId` trong khu vực kỹ thuật/support khi có lỗi upstream.

### Forwarding continuation

1. Sau `FOUND`, lấy `forwarding-options` thay vì yêu cầu người dùng tự nhập trip ID.
2. Chỉ cho chọn trip backend trả về và hiển thị `unavailableReason` nếu option không reserve được.
3. Sau forward, cập nhật từ incident detail response và theo dõi `forwardingOperation`/leg mới.

Không bổ sung GraphQL, generic `include`, join client-side theo từng row hoặc một state machine riêng khác với `availableActions` của backend.
