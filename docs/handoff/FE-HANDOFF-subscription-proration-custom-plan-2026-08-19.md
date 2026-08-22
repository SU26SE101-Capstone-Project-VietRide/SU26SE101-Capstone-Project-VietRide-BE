# Handoff FE: nâng cấp gói theo phần thời gian còn lại và Custom Plan

## 1. Phạm vi FE cần làm

FE cần bổ sung bốn nhóm giao diện:

1. Cảnh báo usage gần hoặc đã chạm quota và nút chọn gói phù hợp.
2. Form gửi yêu cầu Custom Plan khi Standard Plan không đáp ứng.
3. Màn hình quote hiển thị giá gói, phần giá trị gói cũ còn lại và số tiền cần trả.
4. Trạng thái thanh toán WALLET/VNPAY, bao gồm retry khi ví thiếu tiền hoặc quote hết hiệu lực.

Không hiển thị commission, phí vượt mức, `annualBillableMonths` hoặc chức năng mua thêm tuyến/chuyến riêng lẻ.

## 2. Đọc trạng thái subscription

```http
GET /v1/operator/subscription
```

FE dùng:

- `status`: trạng thái hiệu lực tại thời điểm response; tại đúng `expiresAt`, BE trả `EXPIRED`.
- `entitlementActive`: nguồn sự thật để bật/tắt chức năng cần subscription.
- `pendingUpgrade`: giao dịch nâng cấp đang chờ, tách riêng khỏi plan đang cấp quyền.
- `activePlan`: plan đang cấp entitlement; khi VNPAY đang chờ, đây vẫn là plan cũ.

Không tự suy ra còn hiệu lực chỉ từ persisted status hoặc từ đồng hồ client.

## 3. Danh sách plan

```http
GET /v1/operator/subscription-plans
```

Response chỉ chứa:

- Standard Plan đang active.
- Custom Plan đang active và thuộc đúng nhà xe hiện tại.

Custom Plan của nhà xe khác không bao giờ xuất hiện. Custom Plan đã deactivate không thể quote hoặc mua lại.

## 4. Luồng Custom Request

### Gửi yêu cầu

```http
POST /v1/operator/subscription/custom-requests
Idempotency-Key: <uuid-v4>
```

Form gồm sáu quota, ba module, kỳ thanh toán mong muốn và ghi chú:

- `maxVehicles`
- `maxDrivers`
- `maxAssistants`
- `maxOperatorUsers`
- `maxRoutes`
- `maxTripsPerMonth`
- `enableParcel`
- `enableShuttle`
- `enableRag`
- `preferredBillingPeriod`: `MONTHLY | YEARLY`
- `note`

Một nhà xe chỉ có một request `PENDING_REVIEW`. Nếu nhận `409 CUSTOM_REQUEST_ALREADY_PENDING`, FE chuyển sang màn hình request hiện tại.

### Đọc lịch sử và chi tiết

```http
GET /v1/operator/subscription/custom-requests
GET /v1/operator/subscription/custom-requests/{requestId}
```

Các trạng thái:

- `PENDING_REVIEW`: đang chờ admin xử lý.
- `APPROVED`: hiển thị `approvedPlanId` và CTA xem/mua private plan.
- `REJECTED`: hiển thị `rejectionReason`.

UUID thuộc nhà xe khác trả `404 RESOURCE_NOT_FOUND`; FE xử lý giống resource không tồn tại.

## 5. Tạo quote nâng cấp

```http
POST /v1/operator/subscription/upgrade/quote
Idempotency-Key: <uuid-v4>
Content-Type: application/json

{
  "planId": "uuid",
  "billingPeriod": "MONTHLY",
  "paymentMethod": "WALLET"
}
```

FE hiển thị nguyên giá trị BE trả về, không tự tính lại:

- `targetCyclePrice`: giá đầy đủ của chu kỳ target.
- `currentCyclePrice`: giá đầy đủ của chu kỳ hiện tại.
- `unusedCredit`: phần giá trị gói cũ còn lại được khấu trừ.
- `proratedTargetAmount`: giá target tương ứng phần thời gian còn lại.
- `amountDue`: số tiền phải trả.
- `prorationApplied`: có áp dụng proration hay không.
- `periodFrom`, `periodTo`: kỳ mà payment này chi trả.
- `dueAt`: hạn cuối được confirm.

FE nên hiển thị breakdown:

```text
Giá target cho thời gian còn lại  250.000 đ
- Giá trị gói cũ còn lại           150.000 đ
= Cần thanh toán                   100.000 đ
```

Không dùng giá plan hiện tại để tính lại quote vì BE đã snapshot giá và làm tròn đến đồng.

Các lỗi cần map:

- `409 SUBSCRIPTION_UPGRADE_ALREADY_ACTIVE`: đã có quote/payment đang hoạt động.
- `409 SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE`: plan vừa ngừng bán; tải lại danh sách plan.
- `422 SUBSCRIPTION_UPGRADE_TARGET_LIMIT_BELOW_USAGE`: quota target thấp hơn usage hiện tại.
- `422 SUBSCRIPTION_UPGRADE_AMOUNT_NOT_PAYABLE`: target không tạo ra số tiền nâng cấp hợp lệ.
- `404 RESOURCE_NOT_FOUND`: plan không tồn tại hoặc private plan không thuộc nhà xe.

## 6. Confirm payment

```http
POST /v1/operator/subscription/upgrade/{upgradeAttemptId}/payment
Idempotency-Key: <uuid-v4 mới>
```

Kết quả:

- `200`: WALLET thành công.
- `202`: VNPAY đã tạo redirect; mở `paymentRedirectUrl`.
- `402 WALLET_INSUFFICIENT_BALANCE`: số dư không đủ.

Khi nhận 402:

1. Giữ `upgradeAttemptId` hiện tại.
2. Cho người dùng nạp thêm tiền.
3. Gọi lại endpoint confirm trước `dueAt` bằng `Idempotency-Key` mới.

Không retry bằng key cũ vì response 402 của key đó được replay trong 24 giờ.

Các lỗi sau quote:

- `409 SUBSCRIPTION_UPGRADE_QUOTE_STALE`: plan nguồn hoặc usage đã đổi; bỏ quote cũ và tạo quote mới.
- `409 SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE`: target đã ngừng bán; tải lại plan.
- `409 SUBSCRIPTION_UPGRADE_EXPIRED`: đã quá `dueAt`; tạo quote mới.
- `404 RESOURCE_NOT_FOUND`: attempt/private plan không thuộc nhà xe hiện tại.

Trong lúc VNPAY pending, FE vẫn hiển thị plan cũ là plan đang cấp quyền. Target plan chỉ trở thành entitlement source sau khi payment thành công.

## 7. Admin Custom Request

```http
GET  /v1/admin/subscription-plans/custom-requests
GET  /v1/admin/subscription-plans/custom-requests/{requestId}
POST /v1/admin/subscription-plans/custom-requests/{requestId}/approve
POST /v1/admin/subscription-plans/custom-requests/{requestId}/reject
```

Hai API GET admin trả thêm tên nhà xe ngay cạnh ID:

```json
{
  "requestId": "uuid",
  "operatorId": "uuid",
  "operatorName": "Nhà xe Phương Trang",
  "requestedLimits": {},
  "requestedModules": {},
  "status": "PENDING_REVIEW"
}
```

`operatorName` luôn có giá trị, kể cả khi nhà xe đã bị soft-delete. Admin FE render trực tiếp field này và không gọi thêm API lấy chi tiết nhà xe cho từng dòng.

Approve nhập độc lập `pricePerMonth` và `pricePerYear`; ít nhất một giá phải lớn hơn 0. Admin nhập quota/module cuối cùng, không có công thức auto-pricing.

Nếu quota duyệt thấp hơn usage đang có, BE trả:

```text
422 CUSTOM_PLAN_LIMIT_BELOW_CURRENT_USAGE
```

`error.fields` chỉ rõ từng quota vi phạm. FE gắn lỗi vào đúng input và hiển thị granted limit so với current usage từ message.

Reject bắt buộc có lý do. Request terminal không được approve/reject lại.

Custom Plan sau khi tạo là immutable. Màn hình admin chỉ cho phép deactivate; không cho sửa giá, quota, module, owner hoặc reactivate.

## 8. Idempotency

- Mọi POST/PATCH phải gửi UUID v4 ở header `Idempotency-Key`.
- Retry do timeout mạng với cùng payload dùng lại cùng key.
- Retry WALLET sau response 402 dùng key mới.
- Không dùng cùng key cho payload hoặc endpoint khác.

## 9. Rollout tương thích

Trong giai đoạn rolling deployment, FE ưu tiên `entitlementActive`. Nếu field chưa có từ instance cũ, FE có thể tạm fallback theo contract cũ; xóa fallback sau khi BE Release B hoàn tất và toàn bộ instance đã đồng bộ.
