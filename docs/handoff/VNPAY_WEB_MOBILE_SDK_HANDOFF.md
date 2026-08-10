# Handoff VNPay Web và Mobile SDK

## Kết quả sau thay đổi

- Manager Web nâng cấp gói: VNPay trả về `VNPAY_WEB_RETURN_URL`, mặc định là
  `https://app.vietride.online/payments/return`.
- Passenger Mobile thanh toán Booking, Top-up hoặc Parcel: VNPay trả về callback kỹ thuật
  `VNPAY_MOBILE_SDK_RETURN_URL`; callback xác thực dữ liệu rồi trả URI `merchantbackapp`
  chuẩn để VNPay SDK trả `PaymentBack` trực tiếp cho app, không qua browser bridge.
- Gateway không còn trang bridge `/payments/return` và không mở
  `vietride://payments/return` trong luồng thanh toán.
- Chỉ IPN được phép thay đổi trạng thái thanh toán. Web return, Mobile SDK return và status API đều
  chỉ đọc.

## Việc Manager Web cần làm

1. Khi gọi `POST /v1/operator/subscription/upgrade`, chỉ gửi `planId`, `billingPeriod` và
   `paymentMethod`; bỏ `returnUrl`.
2. Chuyển trình duyệt tới `paymentRedirectUrl` trong response.
3. Khai báo route SPA `/payments/return`.
4. Tại route này, dùng nguyên bộ query VNPay để gọi
   `GET /v1/payments/vnpay-return-status?...` và hiển thị trạng thái đã lưu.
5. Không coi query return là nguồn xác nhận thanh toán; trạng thái có thể còn pending nếu return đến
   trước IPN.

## Việc Passenger Mobile cần làm

Với Booking, round-trip Booking, Top-up, Parcel deposit và Parcel final payment dùng VNPay, request
phải có:

```json
{
  "paymentMethod": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Top-up vẫn dùng tên field `method`:

```json
{
  "amount": 500000,
  "method": "VNPAY",
  "paymentReturnMode": "MOBILE_SDK"
}
```

Response VNPay có dạng chung:

```json
{
  "paymentId": "uuid",
  "paymentRedirectUrl": "https://sandbox.vnpayment.vn/...",
  "paymentReturnMode": "MOBILE_SDK",
  "vnpaySdk": {
    "tmnCode": "merchant-code",
    "scheme": "vietride",
    "isSandbox": true
  }
}
```

Top-up dùng `topUpRequestId` thay cho `paymentId`; đây cũng là `sessionId`.

Ứng dụng phải:

1. Lưu bền `sessionId` trước khi mở VNPay SDK.
2. Mở SDK bằng `paymentRedirectUrl`, `tmnCode`, `scheme` và môi trường từ `isSandbox`.
3. Khi SDK trả quyền điều khiển về app, gọi có JWT Passenger:
   `GET /v1/payments/sessions/{sessionId}`.
4. Poll có giới hạn/backoff khi nhận `PENDING`; dừng tại
   `SUCCEEDED`, `FAILED`, `EXPIRED` hoặc `REFUNDED`.
5. Không tự đánh dấu thành công chỉ vì SDK trả nhánh success; IPN có thể chưa đến.

App cũ không gửi mode nhận `426 MOBILE_APP_UPDATE_REQUIRED`. Mode sai nhận
`422 PAYMENT_RETURN_MODE_INVALID`. Khi kênh Mobile chưa bật, API trả
`503 VNPAY_MOBILE_SDK_DISABLED` và không fallback sang bridge.

## ENV Payment cần cấu hình

```dotenv
VNPAY_TMN_CODE=<giá trị do VNPay cấp>
VNPAY_HASH_SECRET=<bí mật do VNPay cấp>
VNPAY_BASE_URL=<URL sandbox hoặc production>
VNPAY_IPN_URL=https://api.vietride.online/v1/payments/vnpay-ipn
VNPAY_WEB_RETURN_URL=https://app.vietride.online/payments/return
VNPAY_MOBILE_SDK_RETURN_URL=https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return
VNPAY_SDK_SCHEME=vietride
VNPAY_IS_SANDBOX=false
VNPAY_WEB_ENABLED=false
VNPAY_MOBILE_SDK_ENABLED=false
VNPAY_PAYMENT_TIMEOUT_MINUTES=15
```

Triển khai code và migration với hai feature flag vẫn `false`. Phát hành Mobile có
`paymentReturnMode=MOBILE_SDK` trước; chờ các session VNPay legacy hết cửa sổ thanh toán
(tối đa 15 phút) hoặc xác nhận không còn row pending có `vnpay_return_mode IS NULL`.
Sau khi xác minh sandbox/device và Web return, bật riêng từng kênh. Không cấu hình lại
`VNPAY_RETURN_URL` hoặc `APP_DEEP_LINK`.

Hạ tầng deep-link chung của ứng dụng vẫn giữ:

```dotenv
ANDROID_PACKAGE=com.vietride.passenger
DEEPLINK_APP_SCHEME=vietride
DEEPLINK_ANDROID_SHA256_FINGERPRINTS=<release-fingerprints>
```
