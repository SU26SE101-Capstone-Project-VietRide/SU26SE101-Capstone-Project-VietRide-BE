# Hướng dẫn chuyển đổi Idempotency cho Frontend và Mobile

> Cập nhật: 23/07/2026
> Đối tượng: Web Frontend, Mobile, QA và các client tích hợp VietRide
> Mức độ ảnh hưởng: Có thay đổi bắt buộc đối với các API mutation được đánh dấu `required`

## 1. Tóm tắt thay đổi

Backend VietRide đã chuẩn hóa idempotency trên toàn hệ thống để một thao tác bị retry không tạo booking, payment, refund, parcel, notification hoặc dữ liệu khác nhiều lần.

Từ phiên bản này:

- Các API `POST`, `PATCH`, `PUT`, `DELETE` được đánh dấu **Bắt buộc** phải gửi header `Idempotency-Key`.
- Giá trị header phải là UUID phiên bản 4, dạng chuẩn 36 ký tự, ví dụ `7f2f819f-ea4b-4330-8a95-f81244fd47d6`.
- Một lần retry của cùng thao tác phải sử dụng lại đúng key và đúng request.
- Một thao tác mới phải tạo key mới.
- Không được dùng cùng key cho endpoint, người dùng, query, body hoặc file khác.
- Route, request body và success response nghiệp vụ không thay đổi.
- Các API đọc bằng `GET` không cần `Idempotency-Key`.
- API được đánh dấu **Miễn** tiếp tục hoạt động không cần header.

Nếu client cũ gọi API bắt buộc nhưng không gửi key, backend trả `422 IDEMPOTENCY_KEY_REQUIRED`.

## 2. Quy tắc lifecycle bắt buộc ở client

### 2.1. Tạo key

Tạo UUID v4 ngay khi người dùng bắt đầu một thao tác mutation, trước lần gửi request đầu tiên.

Web hiện đại có thể dùng:

```ts
const idempotencyKey = globalThis.crypto.randomUUID();
```

Mobile phải dùng API UUID v4 được nền tảng hỗ trợ hoặc thư viện UUID đã có trong ứng dụng. Không tự tạo UUID bằng timestamp, chuỗi ngẫu nhiên yếu, UUID v5, hash payload hoặc chuỗi có prefix.

### 2.2. Retry và thao tác mới

| Trường hợp | Key phải dùng |
|---|---|
| Request timeout, mất mạng hoặc chưa biết kết quả | Dùng lại key cũ và request cũ |
| Người dùng bấm gửi lại do lỗi mạng | Dùng lại key cũ |
| Client tự retry vì lỗi kết nối | Dùng lại key cũ |
| Backend trả kết quả thành công hoặc lỗi nghiệp vụ cuối cùng | Kết thúc operation hiện tại |
| Người dùng chủ động thực hiện một thao tác mới | Tạo key mới |
| Body, query, file, endpoint hoặc user thay đổi | Tạo key mới |

Không đặt `crypto.randomUUID()` trực tiếp bên trong hàm retry, vì mỗi lần retry sẽ trở thành operation mới.

Ví dụ Axios:

```ts
const idempotencyKey = globalThis.crypto.randomUUID();

const submitBooking = () =>
  axios.post('/v1/bookings', payload, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Idempotency-Key': idempotencyKey,
    },
  });

try {
  return await submitBooking();
} catch (error) {
  if (isRetryableNetworkError(error)) {
    return await submitBooking(); // Giữ nguyên key và payload.
  }
  throw error;
}
```

Ví dụ Fetch:

```ts
const idempotencyKey = globalThis.crypto.randomUUID();
const requestBody = JSON.stringify(payload);

const execute = () =>
  fetch('/v1/parcels', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    body: requestBody,
  });
```

### 2.3. Offline queue và khởi động lại ứng dụng

Nếu Mobile có offline queue hoặc cần retry sau khi ứng dụng khởi động lại, phải lưu cùng nhau:

- Idempotency key.
- HTTP method và URL.
- User hiện tại.
- Body/query/file identity bất biến.
- Trạng thái operation.

Không lưu key riêng lẻ rồi dùng lại với payload mới. Xóa operation khỏi queue khi đã nhận được kết quả cuối cùng.

## 3. Xử lý response và error code

| HTTP/ack | Error code | Ý nghĩa | Client phải làm gì |
|---:|---|---|---|
| 422 | `IDEMPOTENCY_KEY_REQUIRED` | Endpoint bắt buộc nhưng thiếu key | Sửa client để gửi UUID v4; không retry mù |
| 422 | `VALIDATION_ERROR` | Key không phải UUID v4 hoặc request không hợp lệ | Tạo UUID v4 đúng định dạng; sửa request |
| 422 | `IDEMPOTENCY_KEY_MISMATCH` | Key cũ được dùng với request khác | Dừng retry; tạo key mới chỉ khi đây thực sự là thao tác mới |
| 409 | `IDEMPOTENCY_REQUEST_PENDING` | Request cùng key vẫn đang được xử lý | Chờ ngắn rồi retry cùng key và cùng request |
| 409 | `IDEMPOTENCY_REQUEST_IN_PROGRESS` | Notification operation vẫn đang chạy | Chờ rồi retry cùng key |
| Socket ack | `IDEMPOTENCY_KEY_REUSED` | Cùng định danh GPS nhưng payload khác | Không ghi đè; tạo GPS sample mới với `recordedAt` mới |

Response replay giữ nguyên status và response body đã hoàn thành. Client không nên tự tạo thêm entity khi nhận response replay.

## 4. Auth và idempotency là hai contract độc lập

`Authorization` và `Idempotency-Key` phải được gửi đồng thời trên endpoint cần đăng nhập:

```http
Authorization: Bearer <access-token>
Idempotency-Key: 7f2f819f-ea4b-4330-8a95-f81244fd47d6
```

Không sử dụng access token, refresh token, user ID hoặc device ID làm idempotency key.

Các endpoint login/refresh được miễn vì chúng trả hoặc xoay vòng credential. Các endpoint đăng ký, quên mật khẩu, reset mật khẩu và cập nhật profile vẫn là mutation bắt buộc và phải có key.

## 5. Lưu ý riêng cho upload, SSE và Socket.IO

### 5.1. RAG chat SSE

- Lần gửi `/v1/rag/chat` đầu tiên tạo một UUID v4.
- Nếu stream bị ngắt và client cần gửi lại cùng câu hỏi, dùng lại key và body cũ.
- Không dùng cùng key cho câu hỏi đã chỉnh sửa.

### 5.2. Upload tài liệu RAG

Fingerprint bao gồm metadata và nội dung bytes của file.

- Retry cùng file, metadata và key sẽ replay operation cũ.
- Thay file hoặc metadata phải tạo key mới.
- Không được dùng cùng key cho hai file có cùng tên nhưng nội dung khác.

### 5.3. Tracking Socket.IO

Hai mutation Socket.IO được bảo vệ:

| Event | Định danh operation |
|---|---|
| `gps:update` | `tripId + recordedAt` |
| `shuttle:gps:update` | `shuttleTripId + recordedAt` |

Retry một GPS sample phải giữ nguyên `recordedAt` và toàn bộ payload. GPS sample mới phải có `recordedAt` mới. Nếu cùng định danh nhưng tọa độ hoặc payload khác, backend trả ack `IDEMPOTENCY_KEY_REUSED`.

## 6. Danh sách API chịu ảnh hưởng

Quy ước:

- **Bắt buộc**: client phải gửi UUID v4 trong `Idempotency-Key`.
- **Miễn**: không bắt buộc gửi header.
- Path chứa `{id:guid}` hoặc `:id` là template; client vẫn thay bằng ID thật như hiện tại.
- Route bắt đầu bằng `/internal/` không dành cho Web/Mobile. Nếu client đang gọi trực tiếp các route này, phải chuyển qua public API hoặc Gateway phù hợp.

### 6.1. Identity

| Method | Path | Chính sách |
|---|---|---|
| POST | `/v1/admin/operators` | Bắt buộc |
| POST | `/v1/admin/operators/{operatorId:guid}/approve` | Bắt buộc |
| POST | `/v1/admin/operators/{operatorId:guid}/reject` | Bắt buộc |
| POST | `/v1/admin/operators/{operatorId:guid}/suspend` | Bắt buộc |
| POST | `/v1/admin/subscription-plans` | Bắt buộc |
| PATCH | `/v1/admin/subscription-plans/{planId:guid}` | Bắt buộc |
| POST | `/v1/admin/users` | Bắt buộc |
| POST | `/v1/admin/users/{userId:guid}/lock` | Bắt buộc |
| POST | `/v1/admin/users/{userId:guid}/unlock` | Bắt buộc |
| POST | `/v1/auth/register` | Bắt buộc |
| POST | `/v1/auth/verify-email` | Bắt buộc |
| POST | `/v1/auth/resend-verification-email` | Bắt buộc |
| POST | `/v1/auth/forgot-password` | Bắt buộc |
| POST | `/v1/auth/reset-password` | Bắt buộc |
| POST | `/v1/auth/set-initial-password` | Bắt buộc |
| POST | `/v1/auth/login` | Miễn |
| POST | `/v1/auth/google` | Miễn |
| POST | `/v1/auth/refresh` | Miễn |
| POST | `/v1/auth/logout` | Bắt buộc |
| POST | `/v1/auth/device-token` | Bắt buộc |
| DELETE | `/v1/auth/device-token` | Bắt buộc |
| POST | `/v1/firebase/custom-token` | Miễn |
| POST | `/internal/v1/operators/summaries/batch` | Miễn, internal-only |
| POST | `/internal/v1/operators/{operatorId:guid}/usage/increment` | Bắt buộc, internal-only |
| POST | `/internal/v1/operators/{operatorId:guid}/quota-allocations` | Bắt buộc, internal-only |
| POST | `/internal/v1/operators/{operatorId:guid}/quota-allocations/{allocationId:guid}/release` | Bắt buộc, internal-only |
| POST | `/internal/v1/users/{userId:guid}/device-tokens/deactivate` | Bắt buộc, internal-only |
| PATCH | `/v1/operator/profile` | Bắt buộc |
| POST | `/v1/operators/register` | Bắt buộc |
| POST | `/v1/operator/subscription/upgrade` | Bắt buộc |
| POST | `/v1/operator/subscription/upgrade/{upgradeAttemptId:guid}/retry-payment` | Bắt buộc |
| POST | `/v1/operator/users` | Bắt buộc |
| POST | `/v1/operator/users/{userId:guid}/resend-initial-password` | Bắt buộc |
| POST | `/v1/users/me/complete-profile` | Bắt buộc |

### 6.2. Trip

Tất cả mutation Trip trong bảng dưới đây đều bắt buộc.

| Method | Path |
|---|---|
| POST | `/v1/admin/locations` |
| PATCH | `/v1/admin/locations/{id:guid}` |
| DELETE | `/v1/admin/locations/{id:guid}` |
| PATCH | `/v1/admin/stations/{id:guid}` |
| POST | `/v1/admin/stations/{primaryStationId:guid}/merge` |
| DELETE | `/v1/admin/stations/{id:guid}` |
| PATCH | `/v1/admin/stops/{id:guid}` |
| DELETE | `/v1/admin/stops/{id:guid}` |
| POST | `/v1/driver/trips/{tripId}/start` |
| POST | `/v1/driver/trips/{tripId}/complete` |
| POST | `/v1/driver/trips/{tripId}/incident` |
| POST | `/v1/driver/trips/{tripId:guid}/stops/{stopId:guid}/arrive` |
| POST | `/v1/driver/trips/{tripId:guid}/stops/{stopId:guid}/depart` |
| POST | `/v1/driver/trips/{tripId}/stops/{stopId}/depart` |
| POST | `/v1/driver/trips/{tripId:guid}/destination/arrive` |
| POST | `/internal/v1/trips/{tripId:guid}/lock-seats` |
| POST | `/internal/v1/trips/{tripId:guid}/release-seats` |
| POST | `/internal/v1/trips/round-trip/book-seats` |
| POST | `/internal/v1/trips/{tripId:guid}/book-seats` |
| POST | `/internal/v1/trips/{tripId:guid}/cargo/reserve` |
| POST | `/internal/v1/trips/{tripId:guid}/cargo/remeasure` |
| POST | `/internal/v1/trips/{tripId:guid}/cargo/load` |
| POST | `/internal/v1/trips/{tripId:guid}/cargo/release` |
| POST | `/internal/v1/trips/round-trip/lock-seats` |
| PATCH | `/v1/operator/alternative-routes/{id:guid}` |
| PUT | `/v1/operator/alternative-routes/{id:guid}/geometry` |
| DELETE | `/v1/operator/alternative-routes/{id:guid}` |
| POST | `/v1/operator/driver-schedules` |
| PATCH | `/v1/operator/driver-schedules/{id:guid}/activate` |
| PATCH | `/v1/operator/driver-schedules/{id:guid}/crew` |
| PATCH | `/v1/operator/driver-schedules/{id:guid}` |
| POST | `/v1/operator/routes` |
| PATCH | `/v1/operator/routes/{id:guid}` |
| PUT | `/v1/operator/routes/{id:guid}/geometry` |
| POST | `/v1/operator/routes/{id:guid}/stops` |
| DELETE | `/v1/operator/routes/{id:guid}/stops/{stopId:guid}` |
| POST | `/v1/operator/routes/{id:guid}/fare-templates` |
| POST | `/v1/operator/routes/{id:guid}/alternative-routes` |
| POST | `/v1/operator/shuttle-trips` |
| POST | `/v1/operator/stations` |
| PATCH | `/v1/operator/stations/{stationId:guid}` |
| DELETE | `/v1/operator/stations/{stationId:guid}` |
| POST | `/v1/operator/stops` |
| PATCH | `/v1/operator/stops/{id:guid}` |
| DELETE | `/v1/operator/stops/{id:guid}` |
| PATCH | `/v1/operator/trips/{tripId:guid}` |
| POST | `/v1/operator/trips/{tripId:guid}/substitute-vehicle` |
| POST | `/v1/operator/trips/{tripId:guid}/disrupt-no-substitution` |
| POST | `/v1/operator/vehicles` |
| PATCH | `/v1/operator/vehicles/{id:guid}` |

### 6.3. Booking

| Method | Path | Chính sách |
|---|---|---|
| POST | `/v1/admin/campaigns` | Bắt buộc |
| PATCH | `/v1/admin/campaigns/{campaignId:guid}` | Bắt buộc |
| POST | `/v1/admin/campaigns/{campaignId:guid}/activate` | Bắt buộc |
| POST | `/v1/admin/campaigns/{campaignId:guid}/deactivate` | Bắt buộc |
| POST | `/v1/admin/vouchers` | Bắt buộc |
| PATCH | `/v1/admin/vouchers/{id:guid}` | Bắt buộc |
| DELETE | `/v1/admin/vouchers/{id:guid}` | Bắt buộc |
| POST | `/v1/bookings/trips/{tripId:guid}/boarding/passenger/{passengerRecordId:guid}` | Bắt buộc |
| POST | `/v1/bookings/trips/{tripId:guid}/boarding/qr-scan` | Bắt buộc |
| POST | `/v1/bookings` | Bắt buộc |
| POST | `/v1/bookings/round-trip` | Bắt buộc |
| POST | `/v1/bookings/{bookingId:guid}/edit-pickup` | Bắt buộc |
| POST | `/v1/bookings/{bookingId:guid}/edit-dropoff` | Bắt buộc |
| POST | `/v1/bookings/{bookingId:guid}/cancel` | Bắt buộc |
| POST | `/v1/bookings/{bookingId:guid}/pending-action/{actionId:guid}/accept-fallback` | Bắt buộc |
| POST | `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve` | Bắt buộc |
| POST | `/internal/v1/vouchers/validate` | Miễn, internal-only |
| POST | `/internal/v1/vouchers/usages` | Bắt buộc, internal-only |
| DELETE | `/internal/v1/vouchers/usages/by-reference` | Bắt buộc, internal-only |
| POST | `/v1/operator/voucher-consents/{id:guid}/accept` | Bắt buộc |
| POST | `/v1/operator/voucher-consents/{id:guid}/reject` | Bắt buộc |
| POST | `/v1/operator/vouchers` | Bắt buộc |
| PATCH | `/v1/operator/vouchers/{id:guid}` | Bắt buộc |
| DELETE | `/v1/operator/vouchers/{id:guid}` | Bắt buộc |
| POST | `/v1/operator/vouchers/{id:guid}/activate` | Bắt buộc |
| POST | `/v1/operator/vouchers/{id:guid}/deactivate` | Bắt buộc |

### 6.4. Payment

| Method | Path | Chính sách |
|---|---|---|
| POST | `/v1/admin/trip-settlements/{settlementId:guid}/settle` | Bắt buộc |
| POST | `/v1/admin/platform-wallet/adjust` | Bắt buộc |
| POST | `/v1/admin/operators/{operatorId:guid}/wallet/adjust` | Bắt buộc |
| POST | `/v1/admin/invoices/{invoiceId:guid}/retry` | Bắt buộc |
| POST | `/internal/e2e/day38/jobs/{jobName}` | Bắt buộc, test/internal-only |
| POST | `/internal/v1/payments/batch-charge` | Bắt buộc, internal-only |
| POST | `/internal/v1/payments/charge` | Bắt buộc, internal-only |
| POST | `/internal/v1/payments/subscription` | Bắt buộc, internal-only |
| POST | `/internal/v1/payments/{paymentId:guid}/expire-subscription` | Bắt buộc, internal-only |
| POST | `/internal/v1/wallet/refund` | Bắt buộc, internal-only |
| POST | `/v1/payments/vnpay-ipn` | Miễn, callback từ VNPay |
| POST | `/v1/payments/vnpay-topup-ipn` | Miễn, callback từ VNPay |
| POST | `/v1/payments/subscription-vnpay-ipn` | Miễn, callback từ VNPay |
| POST | `/v1/wallet/top-up` | Bắt buộc |

FE/Mobile không được tự gọi các endpoint VNPay IPN. Client chỉ khởi tạo luồng thanh toán qua public API tương ứng.

### 6.5. Parcel

Tất cả mutation Parcel trong bảng dưới đây đều bắt buộc.

| Method | Path |
|---|---|
| POST | `/v1/assistant/parcels/{parcelId:guid}/load` |
| POST | `/v1/assistant/parcels/{parcelId:guid}/reweigh` |
| POST | `/v1/assistant/parcels/{parcelId:guid}/confirm-delivery` |
| POST | `/v1/assistant/parcels/{parcelId:guid}/unload` |
| POST | `/v1/assistant/parcels/{parcelId:guid}/deliver` |
| POST | `/internal/v1/parcels/{parcelId:guid}/mark-loaded` |
| POST | `/internal/v1/parcels/{parcelId:guid}/confirm-transfer` |
| POST | `/v1/operator/parcel-route-fares` |
| PATCH | `/v1/operator/parcel-route-fares/{routeId:guid}/{sizeCategory}` |
| PATCH | `/v1/operator/parcels/{parcelId:guid}/review` |
| POST | `/v1/operator/parcels/{parcelId:guid}/request-transfer` |
| POST | `/v1/operator/parcels/{parcelId:guid}/return` |
| POST | `/v1/operator/parcels/{parcelId:guid}/cancel` |
| POST | `/v1/operator/parcels/{parcelId:guid}/confirm-refund` |
| POST | `/v1/operator/parcels/{parcelId:guid}/override-capacity` |
| POST | `/v1/operator/parcels/{parcelId:guid}/confirm-delivery` |
| PATCH | `/v1/operator/parcels/{parcelId:guid}/status` |
| POST | `/v1/parcels/delivery/confirm` |
| POST | `/v1/parcels/delivery/reject` |
| POST | `/v1/parcels/delivery/undo-reject` |
| POST | `/v1/parcels` |

### 6.6. Notification

| Method | Path | Chính sách |
|---|---|---|
| POST | `/internal/v1/emails` | Bắt buộc, internal-only |
| POST | `/v1/notifications/:notificationId/read` | Bắt buộc |
| POST | `/v1/operator/notifications` | Bắt buộc |

### 6.7. RAG

| Method | Path | Chính sách |
|---|---|---|
| POST | `/v1/rag/chat` | Bắt buộc |
| POST | `/v1/rag/messages/:messageId/feedback` | Bắt buộc |
| POST | `/v1/rag/documents` | Bắt buộc |
| PUT | `/v1/rag/documents/:documentId/approve` | Bắt buộc |
| PATCH | `/v1/admin/rag-config/:key` | Bắt buộc |
| POST | `/v1/admin/rag-config/:key/rollback` | Bắt buộc |
| POST | `/v1/admin/rag-config/reload` | Miễn |

## 7. Cách triển khai ở FE/Mobile

### 7.1. Không nên làm

- Không tạo key mới trong Axios response/error interceptor mỗi lần retry.
- Không dùng một global key cho mọi request.
- Không dùng key cũ khi user sửa form rồi gửi lại.
- Không dùng request ID, trace ID hoặc access token làm idempotency key.
- Không tự gọi endpoint `/internal/` từ Web/Mobile.
- Không coi `409 pending` là thất bại cuối cùng và tạo operation mới ngay.

### 7.2. Nên làm

- Tạo một helper `createIdempotentOperation()` dùng chung.
- Gắn key vào model/state của đúng thao tác.
- Disable nút submit khi request đang chạy, nhưng vẫn giữ idempotency để chống double tap, timeout và retry.
- Với offline queue, lưu key cùng immutable request snapshot.
- Log key ở mức debug có kiểm soát để QA đối chiếu, nhưng không log token hoặc dữ liệu nhạy cảm.
- Dùng Swagger Gateway `/docs` để kiểm tra endpoint nào có `Idempotency-Key` bắt buộc.

## 8. Checklist kiểm thử trước khi phát hành client

Với mỗi mutation FE/Mobile đang sử dụng:

- [ ] Request đầu tiên có UUID v4 trong `Idempotency-Key`.
- [ ] Retry cùng key và cùng payload không tạo side effect lần hai.
- [ ] Cùng key nhưng payload khác nhận lỗi mismatch.
- [ ] Thao tác mới tạo key mới.
- [ ] Mất mạng sau khi bấm submit vẫn retry bằng key cũ.
- [ ] Double tap không tạo booking/payment/parcel trùng.
- [ ] API miễn hoạt động bình thường khi không có key.
- [ ] Login, Google login và refresh không bị gắn logic replay credential.
- [ ] RAG SSE reconnect giữ key và body cũ.
- [ ] RAG upload retry giữ cùng file bytes và metadata.
- [ ] GPS retry giữ `recordedAt`; sample mới có `recordedAt` mới.
- [ ] Client không gọi route `/internal/`.

## 9. Rollout đề xuất

1. Cập nhật HTTP mutation wrapper dùng chung ở FE và Mobile.
2. Đối chiếu toàn bộ endpoint client đang gọi với bảng tại mục 6.
3. Bổ sung retry test cho Booking, Payment, Parcel, Subscription và RAG.
4. Phát hành client có idempotency trước hoặc đồng thời với backend strict enforcement.
5. Theo dõi tỷ lệ `IDEMPOTENCY_KEY_REQUIRED`, `VALIDATION_ERROR` và `IDEMPOTENCY_KEY_MISMATCH` sau release.

## 10. Nguồn sự thật

- Inventory máy đọc: `tests/dotnet/idempotency-endpoint-inventory.json`.
- Swagger/OpenAPI Gateway: `/docs`.
- Header canonical: `Idempotency-Key`.
- Định dạng canonical: UUID v4.
- TTL replay backend: 24 giờ.

Khi inventory và tài liệu này lệch nhau, inventory/runtime/Swagger là nguồn quyết định. Tài liệu phải được cập nhật cùng pull request thay đổi endpoint hoặc policy idempotency.
