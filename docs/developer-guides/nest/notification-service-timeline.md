# Notification Service — tiến độ production

> Tài liệu này ghi trạng thái triển khai và tiêu chí nghiệm thu của Notification Service.
> Nguồn nghiệp vụ và wire contract vẫn là technical context, API contract và BSOT.

## Nguyên tắc

- Notification là NestJS consumer-only; không có integration Outbox.
- RabbitMQ vietride.events là biên nhận sự kiện; mọi consumer dùng idempotency, manual ACK, retry và DLQ.
- PostgreSQL schema vietride_notification lưu lịch sử in-app và trạng thái delivery.
- BullMQ quản lý retry nội bộ cho FCM và email.
- Public API xác thực User Access Token RS256 của Identity.
- Internal HTTP dùng X-Internal-Auth với Internal JWT.
- Nội dung hệ thống phải là tiếng Việt Unicode đầy đủ dấu; nội dung do operator nhập được giữ nguyên.
- Observability v1 chỉ gồm pino, Sentry và UptimeRobot.

## Tiến độ phase

- [x] Phase 1 — Nền tảng NestJS, Prisma, Redis, RabbitMQ và health/readiness.
- [x] Phase 2 — REST auth, danh sách thông báo và đánh dấu đã đọc.
- [x] Phase 3 — Notification write core và database dedupe.
- [x] Phase 4 — Booking/Payment core consumers.
- [x] Phase 5 — Trip/Tracking alert consumers.
- [x] Phase 6 — Parcel/Subscription/Operator consumers.
- [x] Phase 7 — FCM delivery pipeline.
- [x] Phase 8 — Email delivery pipeline.
- [x] Phase 9 — Reliability, retention và observability nền.
- [x] Phase 10 — Hoàn thiện coverage v1, Unicode, recipient routing và final acceptance.
- [x] Phase 11 — Nội dung không lộ UUID và điều hướng ngữ nghĩa cho FE.

## Public API hiện hành

- GET /v1/notifications: owner đọc lịch sử, hỗ trợ unreadOnly và phân trang.
- POST /v1/notifications/{notificationId}/read: owner-only, trả 204.
- POST /v1/operator/notifications: OPERATOR_ADMIN hoặc OPERATOR_STAFF, tenant-scoped, yêu cầu Idempotency-Key; title/body là nội dung do operator nhập nên không được tự sửa.
- Các endpoint internal recipient/snapshot không có Gateway route.

## Phase 11 — nội dung thân thiện và điều hướng chuẩn

- `GET /v1/notifications` bổ sung `action={type,params}` nhưng giữ nguyên `data`, `userId` và
  `deepLink` cũ để tương thích ngược.
- REST inbox và FCM dùng chung resolver thuần túy; dữ liệu thiếu hoặc sai định dạng trả action
  `NONE` và không làm hỏng lượt đọc danh sách.
- Action chỉ bao phủ Booking, Trip/Tracking, Parcel, Wallet, Subscription và Shuttle. Các nhóm
  chưa có màn hình FE được xác nhận trả `NONE`.
- Nội dung hệ thống ưu tiên mã/tên hiển thị; khi thiếu snapshot phải dùng cụm từ nghiệp vụ chung,
  không đưa UUID vào `title` hoặc `body`. Nội dung announcement do operator nhập được giữ nguyên.
- Không migration, không backfill, không sửa producer và không gọi service khác trong đường đọc
  `GET /v1/notifications`.
- Quy tắc Phase 11 thay thế fallback ID của Phase 10 đối với thông báo hệ thống tạo mới; ID vẫn
  được giữ nguyên trong metadata phục vụ điều hướng và xử lý nghiệp vụ.

## Phase 10 — phạm vi bắt buộc

### Contract và routing

- Canonical Subscription keys dùng identity.subscription.*.
- Giữ identity.subscription.usage_warning và phát đúng một lần khi crossing 80% theo resource/kỳ.
- Bổ sung identity.operator.registration_submitted, payment.wallet.debited và booking.voucher.consent_requested.
- Không có rag.document.approved Notification event; RAG ingest_requested là local work item.
- Trip route change gửi crew và affected passengers; schedule change từ Trip chỉ gửi crew.
- Parcel dùng recipient policy riêng cho từng routing key, không fan-out blanket sender + recipient.
- Parcel Settlement v2 hỗ trợ exact legacy/v2 `auto_rejected` payload và ba fact sender-only:
  `parcel.parcel.review_approved`, `parcel.parcel.final_payment_requested`,
  `parcel.parcel.settlement_recovered`.
- Review timeout dùng `parcel.parcel.cancelled`; check-in/final-payment timeout dùng
  `parcel.parcel.auto_rejected` với reason và số cọc bị giữ.

### Nội dung

- Tất cả title, body, email subject, text và HTML do hệ thống tạo phải có dấu đầy đủ.
- Placeholder động như ID, mã vé, biển số, tên tuyến và thời gian phải được giữ nguyên.
- Không backfill dữ liệu lịch sử; database môi trường sẽ được clear/reset.

### Recipient resolution

- Booking cung cấp raw trip notification-recipient projection.
- Trip snapshot cung cấp crew và stop context.
- Parcel snapshot ADR envelope cung cấp status, sender, registered recipient, operator và trip;
  terminal rows vẫn resolve được và dependency failure luôn fail closed.
- Identity cung cấp operator recipients, System Admin recipients và device tokens.
- Timeout, 401/403, 5xx hoặc response malformed không được coi là danh sách recipient rỗng hợp lệ.

### Delivery reliability

- Persist thành công nhưng queue add thất bại phải phục hồi bằng replay/reconciliation trên cùng database row.
- FCM phải kiểm tra blacklist ngay trước mỗi lần gửi.
- Email OTP dùng durable dedupe key; SENDING quá lease được reclaim theo at-least-once policy.
- BullMQ xử lý rõ các trạng thái absent, waiting, delayed, active, failed và completed.
- Khi hết RabbitMQ retry, chỉ ACK original sau khi DLQ publish được broker confirm.
- Sentry và log không được chứa JWT, FCM token, email, signed URL hoặc raw payload nhạy cảm.

## Kiểm thử

- Unit/snapshot test bao phủ toàn bộ mapper, template, routing key và payload schema.
- Component E2E bao phủ consumer idempotency, repository, BullMQ worker và controller.
- Real-stack E2E chỉ chọn các biên rủi ro cao: passenger route/delay, crew/operator fan-out,
  Parcel review + settlement timeout/recovery, producer mới, retry→DLQ, DB→queue recovery,
  Gateway Unicode và message redelivery.

## Verification bắt buộc

- npx nx run notification:lint
- npx nx run notification:test -- --runInBand
- npx nx run notification:test:e2e -- --runInBand
- npx nx run notification:build
- npx nx run notification-e2e:e2e
- npx nx run gateway-e2e:e2e
- git diff --check

## Điều kiện hoàn thành

- Mọi event Notification trong registry được phân loại implemented, intentionally no-notification hoặc reserved/out-of-scope.
- Không thiếu producer/consumer/resolver bắt buộc của v1.
- Không gửi sai người nhận, không mất hoặc tạo trùng thông báo khi replay.
- Gateway trả nguyên vẹn Unicode đã persist.
- Targeted verification và E2E chọn lọc đều xanh.
