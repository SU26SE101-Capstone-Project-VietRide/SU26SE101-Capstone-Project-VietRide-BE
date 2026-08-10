# Hướng dẫn tích hợp VNPay mới cho Web và Mobile

## 1. Phạm vi thay đổi

Luồng VNPay được tách theo kênh sử dụng:

| Kênh | Nghiệp vụ | Return URL | Cách quay lại client |
|---|---|---|---|
| Manager Web | Nâng cấp gói Operator | `VNPAY_WEB_RETURN_URL` | VNPay đưa trình duyệt về route SPA `/payments/return`. |
| Passenger Mobile | Booking, Top-up, Parcel | `VNPAY_MOBILE_SDK_RETURN_URL` | Callback trả URI `merchantbackapp`; VNPay SDK gọi `PaymentBack` trực tiếp về app. |

Các nguyên tắc bắt buộc:

- Web không gửi `returnUrl`; Backend tự chọn mode `OPERATOR_WEB`.
- Mobile phải gửi `paymentReturnMode=MOBILE_SDK`.
- Mobile không mở browser bridge và không dùng `vietride://payments/return`.
- Web return, Mobile SDK return và status API chỉ đọc. Chỉ VNPay IPN được phép cập nhật trạng thái
  thanh toán.
- Không đánh dấu thanh toán thành công chỉ dựa vào URL return hoặc kết quả `PaymentBack` của SDK.

## 2. Manager Web cần sửa

### 2.1. Tạo giao dịch nâng cấp gói

Gọi:

```http
POST /v1/operator/subscription/upgrade
Authorization: Bearer <operator-token>
Idempotency-Key: <uuid>
Content-Type: application/json
```

Request mới:

```json
{
  "planId": "b143713b-3810-4657-b9ca-92db51d7ae9e",
  "billingPeriod": "YEARLY",
  "paymentMethod": "VNPAY"
}
```

Không gửi field sau nữa:

```json
{
  "returnUrl": "https://app.vietride.online/payments/return"
}
```

Response thành công là `202 Accepted`. FE cần lấy `data.paymentRedirectUrl`:

```json
{
  "success": true,
  "statusCode": 202,
  "data": {
    "upgradeAttemptId": "uuid",
    "paymentId": "uuid",
    "status": "PENDING_PAYMENT",
    "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
    "dueAt": "2026-08-10T10:15:00Z"
  }
}
```

Chuyển toàn bộ trang sang VNPay:

```ts
window.location.assign(response.data.data.paymentRedirectUrl);
```

Khi retry do lỗi mạng, gửi lại đúng request body và cùng `Idempotency-Key`. Chỉ tạo key mới khi
người dùng chủ động tạo một thao tác thanh toán mới.

### 2.2. Xử lý route `/payments/return`

FE Web phải khai báo route SPA:

```text
/payments/return
```

Khi VNPay đưa browser về route này, chuyển nguyên `window.location.search` sang status API. Không
tự dựng lại, lọc hoặc đổi tên các query parameter VNPay:

```http
GET /v1/payments/vnpay-return-status?<nguyên-query-VNPay>
```

Endpoint này là public; chữ ký trong query VNPay xác thực request.

```ts
const rawQuery = window.location.search;
const result = await api.get(`/v1/payments/vnpay-return-status${rawQuery}`);
const payment = result.data.data;
```

Response mẫu:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "vnPayTxnRef": "VR-SUBSCRIPTION-001",
    "paymentId": "uuid",
    "referenceType": "SUBSCRIPTION",
    "referenceId": "uuid",
    "status": "PENDING_REDIRECT"
  }
}
```

Nếu payment vẫn pending vì IPN chưa đến, FE hiển thị trạng thái đang xử lý và poll có giới hạn:

```http
GET /v1/operator/subscription
Authorization: Bearer <operator-token>
```

FE chỉ hiển thị thành công khi trạng thái backend đã chuyển sang trạng thái thành công tương ứng.
Không gọi `/v1/payments/vnpay-ipn` từ FE.

### 2.3. Lỗi Web cần xử lý

| HTTP | `error.code` | Hành vi đề xuất |
|---|---|---|
| 401 | `PAYMENT_SIGNATURE_INVALID` | Hiển thị kết quả không hợp lệ; không tự xác nhận thanh toán. |
| 404 | `PAYMENT_NOT_FOUND` | Hiển thị không tìm thấy giao dịch và cho phép quay về trang gói. |
| 409 | `SUBSCRIPTION_PAYMENT_PENDING` | Mở trạng thái payment hiện tại thay vì tạo payment mới. |
| 422 | `PAYMENT_AMOUNT_INVALID` | Hiển thị giao dịch không hợp lệ; không retry tự động. |
| 503 | `VNPAY_WEB_DISABLED` | Giữ người dùng tại trang gói và thông báo kênh đang tạm khóa. |

## 3. Passenger Mobile cần sửa

### 3.1. Request bắt buộc theo endpoint

| Nghiệp vụ | Endpoint | Field session trong response |
|---|---|---|
| Booking một chiều | `POST /v1/bookings` | `paymentId` |
| Booking khứ hồi | `POST /v1/bookings/round-trip` | `paymentId` |
| Top-up | `POST /v1/wallet/top-up` | `topUpRequestId` |
| Parcel đặt cọc | `POST /v1/parcels/{parcelId}/deposit-payment` | `depositPaymentId` |
| Parcel thanh toán cuối | `POST /v1/parcels/{parcelId}/final-payment` | `balancePaymentId` |

Các request mutation trên phải gửi JWT Passenger và `Idempotency-Key`. Khi retry cùng một thao tác
do lỗi mạng, Mobile dùng lại cùng key và cùng request body.

Booking và Parcel dùng:

```json
{
  "paymentMethod": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Top-up dùng tên field `method`:

```json
{
  "amount": 500000,
  "method": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Với `paymentMethod=WALLET`, không gửi `paymentReturnMode`.

### 3.2. Đọc response và mở VNPay SDK

Response VNPay có metadata chung:

```json
{
  "paymentId": "uuid",
  "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
  "paymentReturnMode": "MOBILE_SDK",
  "vnpaySdk": {
    "tmnCode": "merchant-code",
    "scheme": "vietride",
    "isSandbox": true
  }
}
```

Mobile phải dùng đúng dữ liệu backend trả về, không hardcode merchant hoặc môi trường:

1. Chọn `sessionId` theo bảng endpoint ở trên.
2. Lưu bền `sessionId` trước khi mở SDK để có thể khôi phục sau khi app bị kill/restart.
3. Mở VNPay SDK bằng `paymentRedirectUrl`, `vnpaySdk.tmnCode`, `vnpaySdk.scheme` và
   `vnpaySdk.isSandbox`.
4. Khi SDK gọi `PaymentBack`, quay về màn hình trạng thái giao dịch.
5. Gọi status API bằng JWT của Passenger; không tự kết luận từ nhánh success/cancel của SDK.

Chỉ mở SDK khi response có đủ `sessionId`, `paymentRedirectUrl` và `vnpaySdk`. Trường hợp Parcel
có số tiền cần thu bằng `0` có thể hoàn tất nghiệp vụ mà không tạo payment hoặc mở VNPay SDK.

Pseudo-code:

```ts
const data = response.data.data;
const sessionId =
  data.paymentId ??
  data.topUpRequestId ??
  data.depositPaymentId ??
  data.balancePaymentId;

await secureStorage.set("pendingVnPaySessionId", sessionId);

await vnPaySdk.open({
  paymentUrl: data.paymentRedirectUrl,
  tmnCode: data.vnpaySdk.tmnCode,
  scheme: data.vnpaySdk.scheme,
  isSandbox: data.vnpaySdk.isSandbox
});

// Sau PaymentBack: đọc trạng thái thật từ backend.
await getPaymentSessionStatus(sessionId);
```

Tên hàm mở SDK trong ví dụ chỉ mang tính minh họa; Mobile dùng API tương ứng của thư viện VNPay đang
tích hợp.

### 3.3. Poll trạng thái thanh toán

Gọi:

```http
GET /v1/payments/sessions/{sessionId}
Authorization: Bearer <passenger-token>
```

Response:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "sessionId": "uuid",
    "status": "PENDING"
  }
}
```

Các trạng thái chuẩn hóa:

| Status | Hành vi Mobile |
|---|---|
| `PENDING` | Hiển thị đang xử lý và poll có giới hạn/backoff. |
| `SUCCEEDED` | Hiển thị thành công, xóa session đã lưu và refresh nghiệp vụ. |
| `FAILED` | Hiển thị thất bại, xóa session đã lưu và cho phép thử lại nếu nghiệp vụ còn hạn. |
| `EXPIRED` | Hiển thị hết hạn và xóa session đã lưu. |
| `REFUNDED` | Hiển thị đã hoàn tiền và refresh dữ liệu liên quan. |

API chỉ trả session thuộc đúng Passenger đang đăng nhập. Session không tồn tại hoặc không thuộc
người dùng hiện tại đều trả `404`.

### 3.4. Lỗi Mobile cần xử lý

| HTTP | `error.code` | Ý nghĩa |
|---|---|---|
| 426 | `MOBILE_APP_UPDATE_REQUIRED` | App chưa gửi `paymentReturnMode`; bắt buộc cập nhật app. |
| 422 | `PAYMENT_RETURN_MODE_INVALID` | Mode khác `MOBILE_SDK`. |
| 422 | `PAYMENT_DEADLINE_PASSED` | Cửa sổ thanh toán nghiệp vụ đã hết. |
| 503 | `VNPAY_MOBILE_SDK_DISABLED` | Kênh Mobile đang tắt; không fallback sang browser bridge. |

## 4. Những phần phải xóa khỏi client

Web:

- Xóa field `returnUrl` khỏi request nâng cấp subscription.
- Không hardcode URL return trong FE.

Mobile:

- Không mở `vietride://payments/return` sau thanh toán.
- Không mở route bridge `/payments/return` trong WebView hoặc browser.
- Không gọi trực tiếp `/v1/payments/vnpay-mobile-sdk-return`; đây là callback giữa VNPay và Backend.
- Không parse query VNPay để tự xác nhận thanh toán.

Backend không còn dùng:

```dotenv
VNPAY_RETURN_URL
VNPAY_PAYMENT_URL
APP_DEEP_LINK
```

Hạ tầng Android App Links dùng chung vẫn giữ, nhưng không tham gia VNPay return:

```dotenv
ANDROID_PACKAGE=com.vietride.passenger
DEEPLINK_APP_SCHEME=vietride
DEEPLINK_ANDROID_SHA256_FINGERPRINTS=<release-fingerprints>
```

## 5. Cấu hình môi trường liên quan đến client

```dotenv
VNPAY_WEB_RETURN_URL=https://app.vietride.online/payments/return
VNPAY_MOBILE_SDK_RETURN_URL=https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return
VNPAY_SDK_SCHEME=vietride
VNPAY_IS_SANDBOX=<true nếu dùng sandbox; false nếu dùng production>
VNPAY_WEB_ENABLED=<true sau khi Web đã sẵn sàng>
VNPAY_MOBILE_SDK_ENABLED=<true sau khi Mobile đã sẵn sàng>
```

`VNPAY_IS_SANDBOX` phải khớp với credential và `VNPAY_BASE_URL`. Mobile không hardcode giá trị này;
luôn dùng `vnpaySdk.isSandbox` từ response.

## 6. Checklist nghiệm thu

### Manager Web

- [ ] Request upgrade không còn `returnUrl`.
- [ ] FE dùng `paymentRedirectUrl` từ response.
- [ ] Route `/payments/return` hoạt động cả khi điều hướng trực tiếp và hard refresh.
- [ ] FE chuyển nguyên query VNPay sang `/v1/payments/vnpay-return-status`.
- [ ] Return đến trước IPN hiển thị trạng thái đang xử lý, không báo thành công sớm.
- [ ] Retry mạng dùng lại cùng `Idempotency-Key` và cùng body.

### Passenger Mobile

- [ ] Mọi request VNPay gửi `paymentReturnMode=MOBILE_SDK`.
- [ ] App lưu `sessionId` trước khi mở VNPay SDK.
- [ ] App dùng đủ `paymentRedirectUrl`, `tmnCode`, `scheme`, `isSandbox` từ response.
- [ ] VNPay SDK gọi `PaymentBack` trực tiếp về app, không mở browser bridge.
- [ ] Sau `PaymentBack`, app poll `/v1/payments/sessions/{sessionId}` bằng JWT Passenger.
- [ ] App chỉ báo thành công khi status API trả `SUCCEEDED`.
- [ ] App khôi phục được session đang chờ sau khi bị kill/restart.
- [ ] Đã kiểm thử success, cancel, fail, timeout và IPN đến chậm trên thiết bị thật.
