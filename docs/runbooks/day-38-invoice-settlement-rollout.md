# Runbook Day 38 — Invoice, OperatorWallet và Settlement

## Phạm vi

Runbook này khóa quy trình triển khai Day 38 theo hai pha, cách xử lý dữ liệu trước Day 38 và vận hành các settlement/Invoice bị kẹt. Không bao gồm bank withdrawal hoặc nhà cung cấp hóa đơn điện tử.

## Cấu hình bắt buộc

| Khóa | Giá trị/chính sách |
|---|---|
| `PaymentContext:Required` | `false` ở Phase A, `true` sau readiness gate Phase B |
| `InvoicePdf:Provider` | `QuestPDF` khi có bằng chứng đủ điều kiện Community; nếu không dùng `PDFsharp-MigraDoc` |
| `InvoicePdf:MaxAttempts` | `5` |
| `InvoicePdf:StaleAfterMinutes` | `15` |
| `InvoicePdf:ReconciliationCron` | `*/5 * * * *` UTC |
| `InvoiceStorage:Bucket` | Tên bucket không chứa secret |
| `InvoiceStorage:StableBaseUrl` | Protected Gateway base URL |
| `InvoiceStorage:SignedUrlTtlMinutes` | `60` |
| `OperatorWeb:InvoiceDetailBaseUrl` | Operator Web invoice-detail deep-link |

`Google.Cloud.Storage.V1`, `PDFsharp-MigraDoc` và phương án QuestPDF có điều kiện đã được người dùng phê duyệt ngày 2026-07-13. Firebase/GCS dùng Application Default Credentials qua workload identity hoặc `GOOGLE_APPLICATION_CREDENTIALS` trỏ tới file nằm ngoài repo. Không commit JSON credential, private key, token hoặc signed URL.

Quy trình cài secret, mount Payment container, validate và rotate key trên VPS được mô tả tại `docs/runbooks/firebase-invoice-storage-deployment.md`.

PDF bundle Noto Sans Regular/Bold cùng OFL-1.1 và custom font resolver. Container verification phải render được chuỗi tiếng Việt có dấu, không tofu, không phụ thuộc font của host.

## Phase A — Schema additive và dual-write

1. Backup theo quy trình môi trường và ghi nhận migration hiện tại.
2. Apply migration additive; không drop/rename dữ liệu Day 37.
3. Deploy code tạo Payment mới với trusted context nhưng giữ `PaymentContext:Required=false`.
4. Callback VNPay hợp lệ cho PENDING_REDIRECT legacy có context `{}` vẫn hoàn tất Payment/PlatformWallet movement hiện hành, set `context_reconciliation_required=true` và chưa tạo ledger/Invoice thiếu dữ kiện.
5. Chưa bật terminal settlement consumer hoặc Invoice reconciliation trên dữ liệu chưa backfill.

## Backfill và readiness

Chạy dry-run trước, sau đó chạy theo thứ tự:

1. `OperatorWalletBackfillJob`: Identity tạo/reuse marker `(operator_id,event_id)` và Outbox approval trong cùng transaction. Payment dedupe theo payload eventId.
2. `Day38PaymentContextBackfillJob`: hydrate context qua internal HTTP có auth tới Booking/Parcel/Identity; không query chéo DB, không overwrite context khác `{}`.
3. `Day38RevenueLedgerBackfillJob`: chỉ thêm ledger còn thiếu với durable source marker; không CREDIT/DEBIT PlatformWallet lần hai.
4. `Day38InvoiceBackfillJob`: tạo Invoice còn thiếu cho subscription SUCCEEDED qua `UNIQUE(payment_id)`; không credit subscription revenue lần hai.

Readiness chỉ đạt khi:

- Không còn PENDING_REDIRECT có context `{}` ngoài row quarantine đã có ticket/runbook.
- Mọi SUCCEEDED booking/parcel trong phạm vi có trusted context và ledger hoặc quarantine marker.
- Mọi SUCCEEDED subscription trong phạm vi có trusted context và Invoice hoặc quarantine marker.
- Không duplicate wallet, movement, ledger, Invoice hoặc Outbox khi chạy lại toàn bộ job.

## Phase B — Bật enforcement

1. Set `PaymentContext:Required=true`.
2. Bật Trip terminal settlement consumer.
3. Bật Invoice reconciliation mỗi 5 phút.
4. Bật eligibility `0 19 * * *` UTC và weekly settlement `0 2 * * 1` UTC.
5. Theo dõi structured log/Sentry, Outbox backlog, RabbitMQ DLQ và readiness trong ít nhất một chu kỳ job.

Rollback chỉ tắt enforcement/consumer/job và quay application image. Không rollback schema, không xóa context/ledger/Invoice đã commit và không replay movement tiền.

## Settlement thiếu số dư

- Transaction phải rollback toàn bộ và giữ settlement `ELIGIBLE`.
- Tăng failure count, set thời điểm/error active; retry weekly không giới hạn.
- HIGH khi count từ 3 trở lên **hoặc** stuck quá 21 ngày.
- Redis key `payment:settlement_insufficient:{settlementId}` giới hạn cảnh báo một lần/24 giờ.
- Ops dashboard phải lọc unresolved ELIGIBLE. Khi recovery thành công: giữ lịch sử, clear active error, set resolved time và loại khỏi stuck filter.

## Invoice bị lỗi

- Claim PROCESSING mới tăng attempt; stale PROCESSING không được tăng lần hai.
- Backoff sau attempts 1..4: 1, 5, 15, 30 phút. Attempt 5 terminal FAILED.
- Admin retry không reset attempt; cùng Idempotency-Key replay response cũ, hai key khác nhau dùng CAS.
- Email chỉ chứa `invoiceWebUrl`. Operator Web đăng nhập rồi gọi protected `downloadApiUrl` để nhận signed URL TTL 60 phút.

## Email có kết quả gửi bất định

- Worker chuyển delivery từ `PENDING/RETRYING` sang `SENDING` bằng compare-and-set trước khi gọi provider.
- Nếu provider trả thành công nhưng cập nhật `SENT` lỗi, giữ `SENDING`, phát cảnh báo Sentry/log chỉ chứa `emailDeliveryId` và tuyệt đối không tự gửi lại.
- Ops đối chiếu `email_deliveries.status='SENDING'` với SendGrid activity bằng `emailDeliveryId`/message metadata. Chỉ chuyển `SENT` khi có bằng chứng provider; không reset về `PENDING` để tránh gửi trùng.
- `SENDING` quá 15 phút là trạng thái cần can thiệp, không phải retry tự động.

## Bằng chứng đóng rollout

- Migration fresh/up/down/reapply và upgrade từ fixture Day 37.
- Phase-A callback legacy thành công, context/ledger backfill không double movement.
- OperatorWallet backfill/lazy-create race giữ một row/operator.
- PDF Unicode Linux container pass và config bind không chứa secret.
- `npm run e2e:day38` in `legacy-upgrade-backfill PASS` và toàn bộ gate còn lại PASS.
