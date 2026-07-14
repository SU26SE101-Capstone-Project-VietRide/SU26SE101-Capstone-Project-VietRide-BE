# API Subscription Cho Frontend

Tài liệu này mô tả các API Subscription Day 37 được Gateway công khai. Base URL local: `http://localhost:3000`.

## Quy ước chung

- API trả envelope ADR 0004: `success`, `statusCode`, `data` hoặc `error`, `meta`.
- Access token: `Authorization: Bearer <accessToken>`.
- Mọi `POST` và `PATCH` trong tài liệu này bắt buộc có `Idempotency-Key` là UUID mới cho mỗi thao tác nghiệp vụ. Retry cùng thao tác phải giữ nguyên key.
- Giá tiền là số nguyên VND, không có phần thập phân.
- Không truyền `operatorId` từ FE: backend lấy operator scope từ access token.

Ví dụ lỗi:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "SUBSCRIPTION_PAYMENT_PENDING",
    "message": "An upgrade payment is already pending."
  },
  "meta": {
    "traceId": "...",
    "timestamp": "2026-07-12T08:00:00Z"
  }
}
```

## 1. Xem subscription hiện tại

`GET /v1/operator/subscription`

**Quyền:** `OPERATOR_ADMIN`

Subscription hết hạn vẫn đọc được. FE dùng `status`, `plan`, `usage` và `pendingUpgrade` để render màn hình quản lý gói.

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "subscriptionId": "0d0b3b2c-6a4c-4d21-9e2a-1e745006fc3a",
    "status": "ACTIVE",
    "billingPeriod": "YEARLY",
    "startedAt": "2026-07-12T08:00:00Z",
    "expiresAt": "2027-07-12T08:00:00Z",
    "plan": {
      "planId": "b143713b-3810-4657-b9ca-92db51d7ae9e",
      "name": "Professional",
      "description": "Gói vận hành đầy đủ",
      "pricePerMonth": 120000,
      "pricePerYear": 1200000,
      "limits": {
        "maxVehicles": 10,
        "maxDrivers": 10,
        "maxAssistants": 10,
        "maxOperatorUsers": 10,
        "maxRoutes": 10,
        "maxTripsPerMonth": 1000
      },
      "modules": {
        "enableParcel": true,
        "enableShuttle": true,
        "enableRag": false
      },
      "isActive": true
    },
    "usage": {
      "currentVehicles": 2,
      "currentDrivers": 3,
      "currentAssistants": 1,
      "currentOperatorUsers": 4,
      "currentRoutes": 2,
      "currentTripsThisMonth": 81,
      "lastResetAt": "2026-07-01T00:01:00Z"
    },
    "pendingUpgrade": null
  },
  "meta": { "traceId": "...", "timestamp": "..." }
}
```

### Trạng thái cần xử lý trên FE

| `status` | Hành vi FE |
|---|---|
| `PENDING_APPROVAL` | Hiển thị đang chờ duyệt; chưa cho upgrade. |
| `ACTIVE` | Hiển thị gói và usage; cho phép chọn upgrade. |
| `PENDING_PAYMENT` | Hiển thị payment đang chờ, dùng `pendingUpgrade.paymentId` và không gửi upgrade mới. |
| `EXPIRED` | Vẫn hiển thị dữ liệu; các màn hình tạo/sửa resource có thể nhận `402 SUBSCRIPTION_EXPIRED`. Hiển thị CTA gia hạn/upgrade. |
| `CANCELLED` | Hiển thị subscription đã hủy; liên hệ quản trị nếu cần. |

## 2. Lấy danh sách gói có thể mua

`GET /v1/operator/subscription-plans`

**Quyền:** `OPERATOR_ADMIN`

Chỉ trả các plan đang hoạt động. Mỗi phần tử có cấu trúc `plan` trong API xem subscription.

```json
{
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "planId": "b143713b-3810-4657-b9ca-92db51d7ae9e",
      "name": "Professional",
      "pricePerMonth": 120000,
      "pricePerYear": 1200000,
      "limits": { "maxVehicles": 10, "maxDrivers": 10, "maxAssistants": 10, "maxOperatorUsers": 10, "maxRoutes": 10, "maxTripsPerMonth": 1000 },
      "modules": { "enableParcel": true, "enableShuttle": true, "enableRag": false },
      "isActive": true
    }
  ],
  "meta": { "traceId": "...", "timestamp": "..." }
}
```

FE phải hiển thị giá theo lựa chọn `MONTHLY` hoặc `YEARLY`; tuyệt đối không tự tính hoặc gửi amount lên backend.

## 3. Tạo upgrade và chuyển đến VNPay

`POST /v1/operator/subscription/upgrade`

**Quyền:** `OPERATOR_ADMIN`  
**Header bắt buộc:** `Idempotency-Key: <uuid>`

Request:

```json
{
  "planId": "b143713b-3810-4657-b9ca-92db51d7ae9e",
  "billingPeriod": "YEARLY",
  "returnUrl": "https://app.vietride.vn/operator/subscription/result"
}
```

`billingPeriod` chỉ nhận `MONTHLY` hoặc `YEARLY`.

Response thành công: `202 Accepted`.

```json
{
  "success": true,
  "statusCode": 202,
  "data": {
    "subscriptionId": "0d0b3b2c-6a4c-4d21-9e2a-1e745006fc3a",
    "upgradeAttemptId": "74bfaf48-1b47-4382-a93b-2b956f221817",
    "status": "PENDING_PAYMENT",
    "paymentId": "d8d70d6b-c5b9-4bcd-9004-84a8f0b9991d",
    "amount": 1200000,
    "billingPeriod": "YEARLY",
    "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
    "dueAt": "2026-07-19T08:00:00Z"
  },
  "meta": { "traceId": "...", "timestamp": "..." }
}
```

### Luồng FE VNPay

1. Người dùng chọn plan và kỳ thanh toán.
2. FE gọi API upgrade với một `Idempotency-Key` mới.
3. Khi nhận `202`, lưu `paymentId` và chuyển trình duyệt tới `paymentRedirectUrl` bằng `window.location.assign(...)`.
4. VNPay xử lý thanh toán và backend nhận IPN. FE không gọi IPN.
5. Khi người dùng quay lại ứng dụng hoặc mở lại màn subscription, FE gọi lại `GET /v1/operator/subscription` cho đến khi `status` là `ACTIVE` và plan đã cập nhật.

Khi network retry, FE gửi lại request body và cùng `Idempotency-Key`; backend trả lại cùng payment thay vì tạo payment mới.

### Lỗi quan trọng

| HTTP | `error.code` | Hành vi FE |
|---|---|---|
| 403 | `FORBIDDEN` | Không có quyền operator admin hoặc token không có operator scope. |
| 404 | `RESOURCE_NOT_FOUND` | Plan/subscription không tồn tại. Refresh danh sách gói. |
| 409 | `SUBSCRIPTION_PAYMENT_PENDING` | Đang có payment khác chờ xử lý; mở trạng thái hiện tại. |
| 409 | `IDEMPOTENCY_KEY_MISMATCH` | Key bị dùng với payload khác; tạo key mới sau khi người dùng xác nhận thao tác mới. |
| 422 | `SUBSCRIPTION_PLAN_INACTIVE` | Plan vừa bị tắt; refresh danh sách gói. |
| 422 | `SUBSCRIPTION_PLAN_NOT_PAYABLE` | Plan không có giá VNPay hợp lệ. |
| 422 | `VALIDATION_ERROR` | Kiểm tra `planId`, `billingPeriod` hoặc header idempotency. |

## 4. Quản trị plan

Các API dưới đây chỉ dành cho màn System Admin.

### Danh sách plan

`GET /v1/admin/subscription-plans?includeInactive=false`

**Quyền:** `SYSTEM_ADMIN`

- `includeInactive=false` là mặc định.
- Response là mảng `SubscriptionPlanDto` như API operator plans.

### Tạo plan

`POST /v1/admin/subscription-plans`

**Quyền:** `SYSTEM_ADMIN`  
**Header bắt buộc:** `Idempotency-Key: <uuid>`

```json
{
  "name": "Business",
  "description": "Gói cho doanh nghiệp vận hành nhiều tuyến",
  "pricePerMonth": 250000,
  "pricePerYear": 2500000,
  "maxVehicles": 20,
  "maxDrivers": 25,
  "maxAssistants": 25,
  "maxOperatorUsers": 15,
  "maxRoutes": 30,
  "maxTripsPerMonth": 3000,
  "enableParcel": true,
  "enableShuttle": true,
  "enableRag": false,
  "isActive": true
}
```

Response: `201 Created`, `data` là plan vừa tạo.

### Cập nhật hoặc tắt plan

`PATCH /v1/admin/subscription-plans/{planId}`

**Quyền:** `SYSTEM_ADMIN`  
**Header bắt buộc:** `Idempotency-Key: <uuid>`

Request có cùng cấu trúc tạo plan. Để tắt plan, gửi `isActive: false`.

- Không có API xóa plan.
- Tắt plan chỉ chặn upgrade mới; không làm thay đổi subscription đang dùng plan đó.
- Response: `200 OK`, `data` là plan sau cập nhật.

## 5. Ảnh hưởng subscription đến các màn hình vận hành

| Điều kiện backend | HTTP/code | Gợi ý FE |
|---|---|---|
| Subscription hết hạn khi tạo/sửa resource thuộc phạm vi kiểm soát | `402 SUBSCRIPTION_EXPIRED` | Hiển thị dialog gia hạn và điều hướng tới subscription. Không retry write tự động. |
| Module Parcel bị tắt khi tạo parcel | `403 SUBSCRIPTION_MODULE_DISABLED` | Không tiếp tục payment/cargo flow; hiển thị yêu cầu nâng cấp plan. |
| Module Shuttle bị tắt khi tạo Vehicle/Route | `403 SUBSCRIPTION_MODULE_DISABLED` | Khóa thao tác tạo và hiển thị CTA nâng cấp. |
| Chạm limit Vehicle/Route/Trip | `422 SUBSCRIPTION_LIMIT_EXCEEDED` | Hiển thị usage hiện tại, giới hạn plan và CTA nâng cấp. |
| Payment upgrade đang chờ | `409 SUBSCRIPTION_PAYMENT_PENDING` | Giữ một trạng thái payment duy nhất; dùng API xem subscription để đồng bộ. |

## Lưu ý tích hợp

- Không gọi hoặc hiển thị các endpoint `/internal/v1/*`; chúng chỉ dành cho service-to-service.
- Không tự gọi `POST /v1/payments/subscription-vnpay-ipn`; endpoint này dành cho VNPay callback.
- Luôn dùng HTTP status thực tế cùng `error.code`; không chỉ kiểm tra `success`.
- Khi `currentTripsThisMonth` reset vào đầu tháng, FE đọc lại API subscription thay vì tự reset cache theo giờ thiết bị.
