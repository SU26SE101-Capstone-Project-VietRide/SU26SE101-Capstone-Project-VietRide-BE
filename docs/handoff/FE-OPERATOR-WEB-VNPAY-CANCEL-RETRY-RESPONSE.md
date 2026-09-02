# Phản hồi FE — Thanh toán lại gói ngay sau khi hủy VNPay trên Manager Web

## 1. Phạm vi

Tài liệu này chỉ áp dụng cho **Operator Manager Web** khi nâng cấp gói bằng VNPay.

- Không liên quan Passenger Mobile.
- Không thay đổi flow Booking hoặc Top-up.
- Backend đã sửa tại commit `f530379b`.

## 2. Backend đã thay đổi gì?

Trước đây, khi người dùng bấm **Hủy thanh toán** trên VNPay, payment vẫn giữ
`PENDING_REDIRECT`. Vì vậy Identity trả `SUBSCRIPTION_PAYMENT_PENDING` và FE phải chờ payment hết
hạn sau 15 phút mới thanh toán lại được.

Hiện tại, khi VNPay trả về query hợp lệ với:

```text
vnp_ResponseCode=24
vnp_TransactionStatus khác 00
```

Backend sẽ:

1. Chuyển payment subscription còn `PENDING_REDIRECT` sang `FAILED` ngay.
2. Phát event `payment.subscription.payment_failed` sang Identity.
3. Cho phép dùng lại upgrade attempt hiện tại qua API `retry-payment`.

Callback bị gọi lại là idempotent, không phát event trùng. Payment đã `SUCCEEDED`, `FAILED` hoặc
`EXPIRED` không bị ghi đè. Kết quả thành công vẫn chỉ được xác nhận bởi VNPay IPN.

## 3. FE Web cần làm

### Bước 1 — Chuyển nguyên query VNPay sang status API

Tại route SPA `/payments/return`, FE phải giữ nguyên toàn bộ `window.location.search`. Không tự
lọc, dựng lại hoặc đổi tên query parameter.

```http
GET /v1/payments/vnpay-return-status?<nguyên-query-VNPay>
```

```ts
const rawQuery = window.location.search;
const response = await api.get(
  `/v1/payments/vnpay-return-status${rawQuery}`,
);

const payment = response.data.data;
```

Khi người dùng hủy, response mẫu:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "txnRef": "VR-SUBSCRIPTION-...",
    "paymentId": "uuid",
    "referenceType": "SUBSCRIPTION",
    "referenceId": "upgrade-attempt-uuid",
    "status": "FAILED"
  }
}
```

Nếu `referenceType === "SUBSCRIPTION"` và `status === "FAILED"`, FE hiển thị **Đã hủy thanh
toán** và tiếp tục bước 2.

### Bước 2 — Poll trạng thái subscription đến khi được retry

Payment đã `FAILED` ngay, nhưng Identity nhận trạng thái qua event bất đồng bộ. FE poll ngắn:

```http
GET /v1/operator/subscription
Authorization: Bearer <operator-token>
```

Chỉ mở nút **Thanh toán lại** khi:

```ts
subscription.pendingUpgrade?.latestPayment?.status === "FAILED" &&
subscription.pendingUpgrade.latestPayment.canRetry === true
```

Response liên quan:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "pendingUpgrade": {
      "upgradeAttemptId": "uuid",
      "dueAt": "2026-09-03T17:15:00+07:00",
      "remainingSeconds": 720,
      "latestPayment": {
        "paymentId": "uuid",
        "status": "FAILED",
        "canRetry": true
      }
    }
  }
}
```

Gợi ý poll mỗi 1 giây, tối đa khoảng 10 giây. Trong lúc chờ, hiển thị trạng thái **Đang cập nhật
giao dịch** và khóa nút để tránh double-click.

### Bước 3 — Gọi API thanh toán lại

API không có request body:

```http
POST /v1/operator/subscription/upgrade/{upgradeAttemptId}/retry-payment
Authorization: Bearer <operator-token>
Idempotency-Key: <UUID>
```

```ts
const idempotencyKey = crypto.randomUUID();

const retryResponse = await api.post(
  `/v1/operator/subscription/upgrade/${upgradeAttemptId}/retry-payment`,
  undefined,
  {
    headers: {
      "Idempotency-Key": idempotencyKey,
    },
  },
);

window.location.assign(retryResponse.data.data.paymentRedirectUrl);
```

Response `202` chứa `paymentId`, `paymentRedirectUrl` và `dueAt` mới nhất. Mỗi lần người dùng chủ
động bấm **Thanh toán lại** phải tạo `Idempotency-Key` mới. Nếu chỉ retry cùng request do lỗi mạng,
phải gửi lại đúng key cũ.

Retry tạo payment/VNPay URL mới nhưng không kéo dài `dueAt` của upgrade attempt.

## 4. Xử lý trạng thái và lỗi

| Trường hợp | FE xử lý |
|---|---|
| Status API trả `FAILED` | Hiển thị đã hủy, poll subscription và mở nút thanh toán lại khi `canRetry=true`. |
| Status API trả `PENDING_REDIRECT` | Hiển thị đang xử lý; poll subscription, không báo thành công sớm. |
| Subscription đã thành công/active | Hiển thị thành công và dừng poll. |
| `409 SUBSCRIPTION_PAYMENT_NOT_RETRYABLE` | Refresh `GET /v1/operator/subscription`; không tự tạo thêm payment. |
| `409 SUBSCRIPTION_UPGRADE_EXPIRED` | Upgrade attempt đã hết hạn; yêu cầu người dùng tạo quote/nâng cấp mới. |
| `422 IDEMPOTENCY_KEY_REQUIRED` | FE chưa gửi `Idempotency-Key`. |
| `503 VNPAY_WEB_DISABLED` | Giữ người dùng tại trang gói và báo kênh VNPay đang tạm khóa. |

FE không được gọi `/v1/payments/vnpay-ipn` và không được tự xác nhận thanh toán thành công dựa trên
query URL của trình duyệt.

## 5. Checklist nghiệm thu FE

- [ ] Route `/payments/return` hoạt động khi redirect trực tiếp và khi hard refresh.
- [ ] FE chuyển nguyên `window.location.search` sang `vnpay-return-status`.
- [ ] Cancel VNPay code `24` hiển thị **Đã hủy thanh toán**.
- [ ] FE poll đến khi `latestPayment.status=FAILED` và `canRetry=true`.
- [ ] Nút **Thanh toán lại** gọi đúng `retry-payment`, không gọi lại API tạo upgrade mới.
- [ ] Retry-payment không gửi body và có `Idempotency-Key` hợp lệ.
- [ ] FE điều hướng đến `paymentRedirectUrl` mới trong response `202`.
- [ ] Không cần chờ đủ 15 phút sau khi người dùng hủy VNPay.
- [ ] Không thay đổi code Mobile cho yêu cầu này.
