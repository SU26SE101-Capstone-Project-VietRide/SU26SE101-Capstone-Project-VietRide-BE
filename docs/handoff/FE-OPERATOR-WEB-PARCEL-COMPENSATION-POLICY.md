# FE Web + Mobile — Parcel Compensation Financial Policy

## 1. Mục tiêu và phạm vi FE

BE đã chuyển quyết định claim/appeal sang contract proof tường minh và cung cấp preview tiền bồi
thường. Contract mới có hiệu lực ngay; body cũ thiếu `proofStatus` hoặc
`acceptedEvidenceIds` sẽ bị từ chối.

Phân công tích hợp:

- **Operator Web — `OPERATOR_ADMIN`:** đọc claim/appeal, chọn proof/evidence, gọi preview và gửi
  quyết định.
- **Operator Web — `OPERATOR_STAFF`:** chỉ đọc list/detail; không hiển thị nút preview/decision.
- **Passenger Mobile:** không gọi endpoint `/v1/operator/*`; chỉ đọc các field quyết định mới qua
  `GET /v1/parcels/{parcelId}/claims`. Các API submit claim, upload evidence và submit appeal giữ
  nguyên contract.
- **Driver/Assistant Mobile:** không có thay đổi cho hạng mục này.

Tất cả endpoint dưới đây được gọi qua API Gateway. Mọi response FE-facing dùng ADR 0004 envelope;
không đọc payload ở root:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "...",
    "timestamp": "2026-09-03T05:00:00Z"
  }
}
```

Error cũng nằm trong envelope:

```json
{
  "success": false,
  "statusCode": 422,
  "error": {
    "code": "PARCEL_CLAIM_EVIDENCE_REQUIRED",
    "message": "...",
    "fields": []
  },
  "meta": {
    "traceId": "...",
    "timestamp": "2026-09-03T05:00:00Z"
  }
}
```

## 2. Contract proof và evidence

- `proofStatus` có ba giá trị: `VERIFIED`, `UNVERIFIED`, `NO_PROOF`.
- `VERIFIED`: loss phải là số nguyên VND không âm; `acceptedEvidenceIds` phải có ít nhất một UUID,
  không rỗng, không trùng và tất cả ID phải thuộc claim đang xử lý.
- `UNVERIFIED` hoặc `NO_PROOF`: loss bắt buộc là `null` và
  `acceptedEvidenceIds` bắt buộc là mảng rỗng `[]`.
- Claim dùng field `provenDirectLossVnd`; appeal dùng `revisedProvenDirectLossVnd`.
- Không gửi `undefined` hoặc bỏ field: gửi tường minh `null`/`[]` theo ma trận trên.
- Evidence không tồn tại hoặc không thuộc claim trả tenant-masked
  `404 PARCEL_CLAIM_EVIDENCE_NOT_FOUND`.
- Tổ hợp proof/loss/evidence không hợp lệ trả `422 PARCEL_CLAIM_EVIDENCE_REQUIRED`.
- `proofStatus` không thuộc enum trả `422 VALIDATION_ERROR`.
- `proofStatus: null` trên `SUBMITTED|UNDER_REVIEW` nghĩa là chưa có quyết định; hiển thị
  **“Chưa đánh giá”**. Chỉ khi claim/appeal đã ở trạng thái quyết định cuối mà proof vẫn `null` mới
  hiển thị **“Chưa ghi nhận (legacy)”**. Không suy diễn proof từ loss/evidence.
- Giá khai báo chỉ là trần trách nhiệm đã chốt trước chuyến, không phải chứng từ. Không tự chọn
  `VERIFIED` chỉ vì parcel có `declaredValueVnd`.
- Với `UNVERIFIED|NO_PROOF`, fallback mặc định/tối đa là `2x` cước và vẫn bị chặn bởi trách nhiệm
  theo giá khai báo. Nếu khách không khai giá, `cargoAwardVnd=0` và chỉ hoàn phần cước còn lại;
  hiển thị rõ “Không có cơ sở xác định giá trị hàng”.

Nguồn evidence để render checkbox là `data.claim.evidence[]` từ
`GET /v1/operator/claims/{claimId}`. Giá trị gửi lên là `evidence[].evidenceId`, không phải URL
`reference`. Appeal không có API upload evidence riêng; từ `claimId` của appeal, tải detail claim
gốc và chỉ cho chọn evidence của claim đó.

## 3. Operator Web — dữ liệu khởi tạo

Các endpoint read dùng được cho `OPERATOR_ADMIN|OPERATOR_STAFF`:

```http
GET /v1/operator/claims?status=&search=&slaState=&from=&to=&page=1&pageSize=20
GET /v1/operator/claims/{claimId}
GET /v1/operator/claim-appeals?status=&page=1&pageSize=20
GET /v1/operator/claim-appeals/{appealId}
```

List claim trả `data.items[]`; claim detail trả object:

```ts
type OperatorParcelClaimDetail = {
  claim: ParcelClaim;
  parcel: unknown;
  incident: unknown | null;
  currentCustody: unknown | null;
  trip: unknown | null;
  expectedDropoff: unknown | null;
  beneficiary: unknown;
  fundingStatus: "NOT_APPLICABLE" | "READY_FOR_PAYOUT" | "FUNDING_PENDING" | "PAID";
  availableActions: string[];
};
```

Chỉ bật thao tác quyết định khi token có role `OPERATOR_ADMIN` và response có action tương ứng:

- Claim: `availableActions` chứa `DECIDE_CLAIM`.
- Appeal: `availableActions` chứa `DECIDE_APPEAL`.

Không tự suy diễn quyền chỉ từ status.

## 4. Claim preview và decision

Khi `proofStatus`, loss hoặc evidence selection thay đổi, debounce/cancel request cũ rồi gọi lại:

```http
POST /v1/operator/claims/{claimId}/award-preview
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "proofStatus": "VERIFIED",
  "provenDirectLossVnd": 300000,
  "acceptedEvidenceIds": ["<evidence-uuid>"]
}
```

Preview là read-only, chỉ `OPERATOR_ADMIN`, không gửi `Idempotency-Key`. Dữ liệu nằm trong
`response.data`:

```json
{
  "claimId": "<claim-uuid>",
  "appealId": null,
  "proofStatus": "VERIFIED",
  "acceptedEvidenceIds": ["<evidence-uuid>"],
  "calculationBasis": "VERIFIED_LOSS",
  "provenDirectLossVnd": 300000,
  "assessedLossVnd": 300000,
  "declaredLiabilityVnd": 150000,
  "fallbackAmountVnd": null,
  "policySnapshot": {
    "version": 1,
    "compensationRatePercent": 50,
    "maxCompensationVnd": 30000000,
    "noProofFallbackMultiplier": 2,
    "claimWindowDays": 7,
    "searchSlaHours": 72,
    "decisionSlaBusinessDays": 7,
    "payoutSlaBusinessDays": 3
  },
  "cargoAwardVnd": 150000,
  "freightRefundVnd": 150000,
  "totalAwardVnd": 300000,
  "originalTotalAwardVnd": null,
  "supplementaryAwardVnd": null
}
```

Khi admin xác nhận:

```http
POST /v1/operator/claims/{claimId}/decision
Authorization: Bearer <access-token>
Idempotency-Key: <uuid-v4>
Content-Type: application/json

{
  "decision": "APPROVE",
  "proofStatus": "VERIFIED",
  "provenDirectLossVnd": 300000,
  "acceptedEvidenceIds": ["<evidence-uuid>"],
  "reason": "Chứng từ hợp lệ"
}
```

`decision` là `APPROVE|REJECT`. `REJECT` vẫn phải gửi đủ proof fields theo cùng quy tắc để lưu
audit quyết định. Claim decision trả `OperatorParcelClaimDetail` trong `response.data`; kết quả
claim nằm tại `response.data.claim`, không nằm trực tiếp tại `response.data`.

## 5. Appeal preview và decision

Preview chỉ dành cho phương án tăng bồi thường:

```http
POST /v1/operator/claim-appeals/{appealId}/adjustment-preview
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "proofStatus": "VERIFIED",
  "revisedProvenDirectLossVnd": 500000,
  "acceptedEvidenceIds": ["<claim-evidence-uuid>"]
}
```

Preview không dùng `Idempotency-Key`. Khi admin xác nhận mutation:

```http
POST /v1/operator/claim-appeals/{appealId}/decision
Authorization: Bearer <access-token>
Idempotency-Key: <uuid-v4>
Content-Type: application/json

{
  "decision": "APPROVE_ADJUSTMENT",
  "proofStatus": "VERIFIED",
  "revisedProvenDirectLossVnd": 500000,
  "acceptedEvidenceIds": ["<claim-evidence-uuid>"],
  "reason": "Chấp nhận chứng từ bổ sung"
}
```

`decision` là `UPHOLD|APPROVE_ADJUSTMENT`. `UPHOLD` cũng phải gửi proof fields nhưng không cần gọi
adjustment preview. Mutation appeal trả `ParcelClaimAppeal` trực tiếp tại `response.data`.
`APPROVE_ADJUSTMENT` chỉ hợp lệ khi tổng mới lớn hơn tổng gốc; FE hiển thị
`supplementaryAwardVnd` là khoản chi bổ sung, không cộng lại hoàn cước lần hai.

## 6. Breakdown và quy tắc hiển thị

Preview trả:

- `calculationBasis`: `VERIFIED_LOSS`, `NO_PROOF_FALLBACK` hoặc
  `NO_DECLARATION_FREIGHT_ONLY`.
- `assessedLossVnd`: loss được dùng cho nhánh verified; nullable.
- `declaredLiabilityVnd`: trách nhiệm theo giá khai báo và rate; nullable nếu parcel không khai giá.
- `fallbackAmountVnd`: `freight × multiplier` trước khi áp các trần; chỉ có ở nhánh fallback.
  Với `NO_DECLARATION_FREIGHT_ONLY`, field này vẫn phục vụ audit nhưng không được cộng vào
  `cargoAwardVnd`.
- `policySnapshot`: policy đã đóng băng cho parcel. Trần nằm ở
  `policySnapshot.maxCompensationVnd`; field tương ứng trên claim là `policyCapVnd`.
- `cargoAwardVnd`: bồi thường hàng.
- `freightRefundVnd`: phần cước còn được hoàn sau khi trừ khoản đã hoàn trước đó.
- `totalAwardVnd`: `cargoAwardVnd + freightRefundVnd`.
- Appeal thêm `originalTotalAwardVnd` và `supplementaryAwardVnd`.

Luôn hiển thị riêng bồi thường hàng, hoàn cước và tổng chi. `policyCapVnd` chỉ giới hạn
`cargoAwardVnd`; `totalAwardVnd` có thể cao hơn cap do cộng hoàn cước. FE không tự tính, làm tròn
hoặc gửi bất kỳ award/rate/cap nào. Giá trị VND là số nguyên; chỉ format tiền ở view.

Preview không phải cam kết payout. Mutation tính lại dưới transaction/lock; response mutation là
nguồn cuối cùng để cập nhật cache/store/màn hình. Trong lúc preview hoặc mutation đang chạy, khóa
nút submit. Bỏ qua response preview cũ nếu form đã thay đổi sau khi request được gửi.

## 7. Idempotency và xử lý lỗi

- Preview/read: không gửi `Idempotency-Key`.
- Mutation: tạo một UUID-v4 cho **một thao tác người dùng**; nếu timeout/network retry thì reuse
  đúng key đó. Chỉ tạo key mới khi người dùng bắt đầu một thao tác mới.
- `409 PARCEL_CLAIM_ALREADY_DECIDED` hoặc `PARCEL_CLAIM_APPEAL_ALREADY_DECIDED`: form đã stale;
  đóng form và reload detail.
- `409 PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED`: revised award không tạo delta dương; giữ form để
  admin sửa proof/loss/evidence hoặc chọn `UPHOLD`.
- `404 PARCEL_CLAIM_EVIDENCE_NOT_FOUND`: reload claim detail vì evidence selection đã stale hoặc
  không thuộc claim.
- `422 PARCEL_CLAIM_EVIDENCE_REQUIRED`: đánh dấu nhóm proof/loss/evidence và hiển thị
  `error.message`; không tự sửa payload ngầm.
- `403`: role không đủ hoặc sai operator scope; không retry tự động.

## 8. Trạng thái payout

Claim:

- `APPROVED`: quyết định đã duyệt, đang chờ Payment xử lý; `fundingStatus=READY_FOR_PAYOUT`.
- `FUNDING_PENDING`: đang chờ operator bổ sung nguồn tiền.
- `PAID`: đã chi trả thành công.
- `REJECTED`: không có payout.

Appeal:

- `ADJUSTMENT_APPROVED`: adjustment đã duyệt, đang chờ Payment xử lý.
- `FUNDING_PENDING`: đang chờ nguồn tiền.
- `PAID`: khoản bổ sung đã chi thành công.
- `UPHELD`: giữ nguyên quyết định cũ, không có payout bổ sung.

Đây là luồng bất đồng bộ. Không hiển thị `APPROVED`/`ADJUSTMENT_APPROVED` là đã thanh toán và không
tự chuyển trạng thái khi chưa nhận response/read model mới từ BE. Sau notification, reconnect hoặc
manual refresh, refetch detail/list; không suy ra `PAID` chỉ vì Passenger Wallet đã xuất hiện tiền.
Payment tự chạy retry/reconciliation mỗi 10 phút cho payout thiếu nguồn tiền hoặc payout mới thiếu
completion marker, nhưng FE vẫn chỉ dùng trạng thái canonical từ Parcel read model.

Đối soát không yêu cầu đồng thời cả hai source wallet transaction. Mỗi payout có đúng một nguồn:

- Trước Trip settlement: Admin thấy một PlatformWallet `DEBIT/PARCEL_COMPENSATION`; Operator xem
  khoản giảm trong `OperatorLedgerEntry`, không có OperatorWallet transaction.
- Sau Trip settlement: Operator thấy một OperatorWallet `DEBIT/PARCEL_COMPENSATION` và một
  `OperatorLedgerEntry`; Admin không có PlatformWallet compensation debit cho payout đó.
- Passenger luôn thấy đúng một Wallet `CREDIT/PARCEL_COMPENSATION`.

Nếu cần support đối soát, dùng `claimId`/`appealId` làm payout reference và `parcelId` cho operator
ledger; không kết luận thiếu sổ chỉ vì cả PlatformWallet và OperatorWallet không cùng xuất hiện.

## 9. Passenger Mobile

Mobile tiếp tục gọi:

```http
GET /v1/parcels/{parcelId}/claims
Authorization: Bearer <access-token>
```

`response.data` là mảng claim. Mỗi claim bổ sung:

- `proofStatus: "VERIFIED"|"UNVERIFIED"|"NO_PROOF"|null`.
- `acceptedEvidenceIds: string[]`.
- `cargoAwardVnd`, `freightRefundVnd`, `totalAwardVnd` để hiển thị breakdown.
- `appeal`, nếu có, cũng chứa `proofStatus` và `acceptedEvidenceIds`.

Mobile không gửi `proofStatus`/`acceptedEvidenceIds` khi submit claim, upload evidence hoặc submit
appeal; các field này do Operator Admin quyết định. Với null trước quyết định hiển thị “Chưa đánh
giá”; với null trên quyết định lịch sử hiển thị “Chưa ghi nhận (legacy)”. Với trạng thái payout,
dùng cùng mapping ở mục 8.

## 10. Checklist cho FE/agent

- Parse ADR 0004 envelope và lấy payload từ `response.data`.
- Tách model claim decision detail (`data.claim`) và appeal decision (`data`).
- Gate action bằng role cộng `availableActions`.
- Form luôn gửi đủ ba field proof/loss/evidence, kể cả `REJECT`/`UPHOLD`.
- Preview lại khi input thay đổi; không lưu preview thành kết quả cuối.
- Reuse UUID-v4 khi retry cùng mutation.
- Không tự tính award, không hoàn cước appeal lần hai.
- Phân biệt `proofStatus=null` trước quyết định với `proofStatus=null` của quyết định legacy.
- Operator Staff và mọi mobile role không gọi preview/decision operator-only.
