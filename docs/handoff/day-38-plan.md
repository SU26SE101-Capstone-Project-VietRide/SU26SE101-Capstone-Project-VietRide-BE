# Day 38 — Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 38 — Invoice PDF + PlatformWallet + Settlement
- **Prior checklist**: `docs/handoff/day-37-checklist.md` — `not found` tại thời điểm lập kế hoạch; tiếp tục theo timeline và trạng thái source hiện tại
- **Plan status**: ✅ APPROVED — user override ngày 2026-07-13 sau khi vá toàn bộ findings của hai vòng PLAN-REVIEW
- **Revision**: Revision 6

## Objective

Hoàn thiện Payment & Wallet cho ba năng lực liên kết: ghi nhận doanh thu booking/parcel vào PlatformWallet và sổ cái operator, settlement theo Trip sau cửa sổ giữ tiền, và thanh toán subscription bằng OperatorWallet. Mỗi subscription payment thành công qua WALLET hoặc VNPay tạo đúng một invoice PDF có luồng retry/recovery hữu hạn và download được ủy quyền. Operator/Admin có API đọc, điều chỉnh và vận hành settlement; Notification gửi đúng người nhận mà không làm lộ PII hoặc signed URL. Ngày chỉ hoàn tất khi real-stack E2E chạy PostgreSQL, Redis, RabbitMQ, API và consumer thật chứng minh đủ các bất biến tiền tệ, idempotency và race.

## Success criteria (DoD — binary, verifiable)

- [ ] Payment lưu `context JSONB` là snapshot server-side bất biến, đủ `operatorId`, `tripId`, allocation và subscription billing data để consumer không lookup chéo DB.
- [ ] Chính các money handler gốc của booking/parcel payment success và refund ghi PlatformWallet movement cùng OperatorLedgerEntry trong một local transaction; Payment không tự consume event outbound của mình để ghi tiền lần hai; không thu platform fee trên doanh thu booking/parcel trong Day 38.
- [ ] `identity.operator.approved` bootstrap đúng một OperatorWallet bằng consumer có durable dedupe; operator đã approved trước Day 38 được backfill bằng Identity Outbox và vẫn có lazy-create an toàn khi money operation đầu tiên đến trước backfill.
- [ ] `trip.trip.completed` và `trip.trip.disrupted` tạo tối đa một `OperatorTripSettlement` cho mỗi `(operator_id, trip_id)`; riêng Payment settlement coi `hasSubstitution` là audit-only và không đổi số tiền.
- [ ] Job eligibility chạy `0 19 * * *` UTC (02:00 ICT) và weekly settlement chạy `0 2 * * 1` UTC (09:00 ICT thứ Hai); settlement cân bằng PlatformWallet DEBIT = OperatorWallet CREDIT = `netAmount`.
- [ ] Thiếu PlatformWallet balance rollback toàn bộ, giữ cùng settlement ở `ELIGIBLE`, retry hằng tuần không giới hạn, có failure metadata và cảnh báo vận hành không spam.
- [ ] OperatorWallet subscription payment atomically DEBIT OperatorWallet + CREDIT PlatformWallet; WALLET và VNPay cùng phát `payment.subscription.payment_succeeded` với một contract canonical.
- [ ] Mỗi subscription payment `SUCCEEDED` tạo tối đa một Invoice với invoice number được cấp phát atomic; PDF thành công chuyển DRAFT → ISSUED và phát `payment.invoice.issued`; năm total attempts bao gồm stale PROCESSING recovery.
- [ ] Invoice download tạo signed Firebase URL mới, TTL 60 phút, không persist signed URL, tenant-isolated và rate limit 10 request/phút/user/invoice.
- [ ] Manual settle/retry/adjust mutations bắt buộc `Idempotency-Key`; replay cùng key trả response gốc, hai key khác nhau chịu CAS/optimistic lock và không double movement.
- [ ] Notification tạo push + in-app + email invoice cho đúng OPERATOR_ADMIN; settlement notification dùng `netAmount`; log không chứa email, phone, full event payload hoặc signed URL.
- [ ] `npm run e2e:day38` chạy isolated real stack với deterministic seed, pass đủ 26 scenario, persistence assertions và cleanup; tất cả build/format/test/lint/migration gates cũng xanh.

## Contract changes

### REST và authorization

- Sửa `POST /v1/operator/subscription/upgrade`: thêm `paymentMethod: VNPAY | WALLET`; `returnUrl` bắt buộc với VNPAY và không nhận với WALLET. WALLET success trả `200` với `status=ACTIVE`, `paymentId`, `invoiceStatus=PENDING`; VNPAY giữ response `202` và redirect. Role `OPERATOR_ADMIN`, bắt buộc `Idempotency-Key`.
- Thêm `POST /v1/driver/trips/{tripId}/complete` đúng technical context §4. Role `DRIVER | ASSISTANT`, caller phải là driver/assistant được gán cho Trip, bắt buộc `Idempotency-Key`, body rỗng; chỉ nhận Trip `IN_PROGRESS`, thành công `200` trả `{ tripId, status: "COMPLETED", completedAt }` trong ADR 0004 và ghi audit actor/role. Replay cùng key trả response gốc; key khác khi Trip đã terminal trả `409 TRIP_ALREADY_TERMINAL`.
- Thêm `GET /v1/operator/invoices`, `GET /v1/operator/invoices/{invoiceId}` và `GET /v1/operator/invoices/{invoiceId}/download`. Role `OPERATOR_ADMIN`. Download chọn duy nhất wire shape `200 ApiResponse<{ downloadUrl, expiresAt }>`: frontend gọi endpoint stable bằng Bearer token, sau đó mới điều hướng/tải từ signed Firebase URL; rate limit 10/phút/user/invoice.
- Thêm `POST /v1/admin/invoices/{invoiceId}/retry`. Role `SYSTEM_ADMIN`, bắt buộc `Idempotency-Key`, thành công `202`; cùng key replay response gốc; key khác thua CAS trả `409 INVOICE_RETRY_ALREADY_PENDING`; ISSUED/CANCELLED hoặc hết năm attempts trả `409 INVOICE_RETRY_NOT_ALLOWED`.
- Thêm `GET /v1/operator/wallet`, `GET /v1/operator/wallet/transactions`, `GET /v1/operator/trip-settlements`, `GET /v1/operator/ledger`. Role `OPERATOR_ADMIN | OPERATOR_STAFF`, operator scope chỉ lấy từ JWT.
- Thêm `GET /v1/admin/trip-settlements`, `GET /v1/admin/platform-wallet`, `GET /v1/admin/platform-wallet/transactions`, `POST /v1/admin/platform-wallet/adjust`, `POST /v1/admin/operators/{operatorId}/wallet/adjust`, `POST /v1/admin/trip-settlements/{settlementId}/settle`. Role `SYSTEM_ADMIN`; mọi POST bắt buộc `Idempotency-Key`.
- List API dùng `page`, `pageSize <= 100`, `sortBy`, `sortDir`, filter contract đã whitelist và ADR 0004 envelope. Gateway bổ sung đúng các prefix operator/admin còn thiếu, không proxy wildcard rộng hơn contract.

### Event registry canonical

- `payment.payment.succeeded`: thêm `method` và trusted `context`; booking group có `allocations[]` để một event tạo nhiều ledger row mà không chia sai doanh thu.
- `payment.payment.refunded`: payload canonical có `paymentId`, `referenceType`, `referenceId`, `amount`, `context` và source event id.
- `payment.subscription.payment_succeeded`: cả WALLET và VNPay phát cùng payload gồm `paymentId`, `upgradeAttemptId`, `operatorId`, `operatorSubscriptionId`, `amount`, `method`, `planName`, `billingPeriod`, `periodFrom`, `periodTo`, buyer snapshot.
- `trip.trip.completed | trip.trip.disrupted`: canonical payload cho Payment gồm `tripId`, `operatorId`, `terminalAt`, `hasSubstitution`; **chỉ logic Payment settlement** coi `hasSubstitution` là audit-only. Quyết định này không thay đổi semantics của Trip substitution, Notification hoặc flow khác.
- `payment.invoice.issued`: `{ invoiceId, invoiceNumber, operatorId, amount, invoiceWebUrl, downloadApiUrl }`; `invoiceWebUrl` là deep-link Operator Web có flow đăng nhập, còn `downloadApiUrl` là protected Gateway endpoint để frontend gọi bằng Bearer token. Không field nào là Firebase signed URL.
- `payment.trip_settlement.completed`: `{ settlementId, tripId, operatorId, netAmount, settlementMethod, settledAt }`; dùng thống nhất `netAmount`, không dùng alias `amount`.

Producer Outbox và business mutation phải commit cùng local transaction; consumer idempotent theo event id. Không có distributed transaction hoặc FK chéo database.

### Persistence và error registry

- `payments.context JSONB NOT NULL DEFAULT '{}'::jsonb`; dữ liệu do server tạo, immutable sau khi Payment được tạo.
- Bổ sung/đồng bộ Invoice, PlatformWallet, PlatformWalletTransaction, OperatorWallet, OperatorWalletTransaction, OperatorLedgerEntry và OperatorTripSettlement vào EF migration cùng canonical DDL. Giữ lại PlatformWallet code/migration đang tồn tại, chỉ mở rộng, không scaffold lại hoặc làm mất dữ liệu.
- Invoice: `UNIQUE(payment_id)`, `UNIQUE(invoice_number)`, stable `pdf_url`, `storage_object_path`, `pdf_generation_status=PENDING|PROCESSING|FAILED|COMPLETED`, `pdf_generation_attempts`, `pdf_generation_started_at`, `pdf_generation_next_retry_at`, `pdf_generation_last_error`, timestamps và consistency checks.
- `invoice_number_counters(period_key CHAR(6) PRIMARY KEY, last_value BIGINT NOT NULL)` cấp số bằng một câu lệnh PostgreSQL `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` trong cùng transaction tạo Invoice. Format `VR-INV-yyyyMM-XXXXXX`, `XXXXXX` bắt đầu từ `000001` mỗi tháng; vượt `999999` fail toàn transaction với `INVOICE_NUMBER_EXHAUSTED`, không tái sử dụng số đã commit.
- Settlement là một entity/row duy nhất trong toàn bộ `PENDING_HOLD → ELIGIBLE → SETTLED|CANCELLED`; thêm failure metadata gồm `settlement_failure_count`, `last_settlement_failure_at`, `active_failure_code`, `failure_resolved_at` và index phục vụ stuck query.
- OperatorWallet transaction ratify `reference_type=SUBSCRIPTION_PAYMENT`, `reference_id=payment_id`; partial unique index chính xác là `(operator_id, type, reference_type, reference_id) WHERE reference_type='SUBSCRIPTION_PAYMENT'`. PlatformWallet transaction tương ứng dùng partial unique `(type, reference_type, reference_id) WHERE reference_type='SUBSCRIPTION_PAYMENT'`. Cả hai khớp tên cột hiện hữu và chặn double debit/credit.
- Ledger dedupe: `UNIQUE(source_event_id, entry_type, reference_id)`; Invoice `UNIQUE(payment_id)` và `UNIQUE(invoice_number)`; settlement `UNIQUE(operator_id, trip_id)`; OperatorWallet `UNIQUE(operator_id)`; processed-event marker unique theo consumer + event id.
- Error codes: `INVOICE_NOT_FOUND`, `INVOICE_NUMBER_EXHAUSTED`, `INVOICE_PDF_GENERATION_FAILED`, `INVOICE_RETRY_ALREADY_PENDING`, `INVOICE_RETRY_NOT_ALLOWED`, `TRIP_ALREADY_TERMINAL`, `TRIP_SETTLEMENT_NOT_FOUND`, `TRIP_SETTLEMENT_ALREADY_SETTLED`, `PLATFORM_WALLET_INSUFFICIENT_BALANCE`, `WALLET_INSUFFICIENT_BALANCE`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_MISMATCH`, `RATE_LIMIT_EXCEEDED`.

### Các quyết định Revision 6 đã được người dùng phê chuẩn

- Money là BIGINT VND đến đơn vị đồng theo BSOT v1.11.0; không áp dụng quick-reference floor 1.000 đã cũ cho ledger/settlement. Validation bội số 1.000 chỉ giữ ở endpoint nào contract hiện hành yêu cầu riêng.
- Không có platform fee cho booking/parcel settlement. Subscription fee là sản phẩm SaaS riêng mà operator trả VietRide nên vẫn CREDIT PlatformWallet; không mâu thuẫn với quy tắc trên. Dòng Review của timeline về trừ fee % là stale so với technical context §4.6 và quyết định Revision 6.
- Admin invoice retry: cùng `Idempotency-Key` replay nguyên response `202`; hai key khác nhau race bằng CAS, loser `409 INVOICE_RETRY_ALREADY_PENDING`; ISSUED/CANCELLED hoặc đã dùng đủ năm attempts trả `409 INVOICE_RETRY_NOT_ALLOWED`.
- Invoice download rate limit cố định `10 request/phút/user/invoice`. Firebase object path là `invoices/{operatorId}/{invoiceId}.pdf`; DB và event chỉ lưu/phát stable VietRide endpoint. Mỗi authenticated download trả signed URL mới TTL 60 phút và không persist.
- Settlement thiếu PlatformWallet balance giữ nguyên row `ELIGIBLE`, retry hàng tuần vô hạn và lưu `settlement_failure_count`, `last_settlement_failure_at`, `active_failure_code`, `failure_resolved_at`. Severity HIGH khi failure count `>=3` **HOẶC** stuck `>21 ngày`; settlement đã SETTLED/CANCELLED hoặc đã recovery không xuất hiện trong stuck filter. Recovery giữ historical count/time, clear active error và set resolved time.
- QuestPDF Community chỉ dùng sau khi Task 38.0 ghi bằng chứng tổ chức đủ điều kiện license. Fallback đã được người dùng duyệt trước là NuGet `PDFsharp-MigraDoc` (MIT); `Google.Cloud.Storage.V1` cũng được người dùng phê duyệt cho Firebase/GCS adapter. Không dùng dependency thương mại và không chờ đến Task 38.7 mới quyết định provider.
- Cấu hình không chứa secret: `InvoicePdf:Provider`, `InvoicePdf:MaxAttempts=5`, `InvoicePdf:StaleAfterMinutes=15`, `InvoicePdf:ReconciliationCron=*/5 * * * *`, `InvoiceStorage:Bucket`, `InvoiceStorage:StableBaseUrl`, `InvoiceStorage:SignedUrlTtlMinutes=60`, `OperatorWeb:InvoiceDetailBaseUrl`. Firebase dùng Application Default Credentials qua `GOOGLE_APPLICATION_CREDENTIALS`/workload identity của môi trường triển khai; `.env.example` chỉ có placeholder và compose chỉ map credential file ngoài repo read-only, không commit JSON credential/path thật/token vào appsettings hoặc docs.
- PDF dùng Noto Sans Regular/Bold được bundle trong Payment image cùng OFL-1.1 license và custom font resolver; không phụ thuộc font cài trên host. Linux container integration test render chuỗi tiếng Việt có dấu, kiểm tra text extraction/glyph không tofu và PDF không rỗng.

### Rollout tương thích dữ liệu trước Day 38

1. Phase A deploy migration additive và code dual-write context; `payments.context` vẫn có default `{}` cho row cũ. Callback không được từ chối IPN đã xác thực chỉ vì row legacy thiếu context: vẫn hoàn tất Payment/PlatformWallet movement hiện hành, gắn `context_reconciliation_required=true` và chưa tạo ledger/Invoice nếu thiếu trusted facts.
2. `Day38PaymentContextBackfillJob` hydrate context cho PENDING_REDIRECT/SUCCEEDED legacy qua internal HTTP có auth đến owner service theo `reference_type/reference_id`; không query chéo DB. Booking/Parcel/Identity trả trusted historical snapshot. Job idempotent, có dry-run/count/error report và không overwrite context khác `{}`.
3. Với SUCCEEDED legacy, `Day38RevenueLedgerBackfillJob` chỉ bổ sung OperatorLedgerEntry còn thiếu, không credit PlatformWallet lần hai. `Day38InvoiceBackfillJob` tạo Invoice còn thiếu cho subscription SUCCEEDED qua `UNIQUE(payment_id)`. Mọi backfill có durable source marker và transaction cục bộ.
4. Phase B chỉ bật `PaymentContext:Required=true`, terminal settlement consumers và invoice reconciliation sau khi readiness query xác nhận không còn PENDING_REDIRECT callback có context rỗng và mọi SUCCEEDED legacy trong phạm vi đã backfill hoặc được quarantine có runbook. Rollback tắt enforcement/consumers, không rollback schema hoặc xóa dữ liệu.
5. Upgrade fixtures gồm DB hiện hữu có PENDING_REDIRECT trước deploy, callback đến trong Phase A, SUCCEEDED booking/parcel chưa có ledger và SUCCEEDED subscription chưa có Invoice. Không callback hợp lệ nào bị mất tiền hoặc trả lỗi chỉ do rollout.

## Tasks

### Task 38.0 — Chốt Contract/SOT và architecture baseline

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md` chỉ các registry/error/persistence mục Day 38 và exact Trip terminal dependency seam; `db-schema/payment-wallet/README.md`; contract fixture/schema dưới `libs/shared/contracts/src/events/`; tài liệu cấu hình/rollout/runbook Day 38 dưới `docs/` |
| forbidden scope | Không viết implementation; không sửa `.agents/**`, `.codex/**`, `.claude/**`, `.env`, secret, generated client, migration hoặc business code; ngoài Invoice/OperatorWallet/Settlement chỉ được đổi exact Trip terminal dependency seam đã liệt kê, không mở rộng Trip business scope khác; không thêm e-invoice provider/bank withdrawal/platform fee; không git ops. |
| depends on | Không có. Đây là baseline bắt buộc trước mọi task feature. |
| invariant flags | Docs/TS LF; tiếng Việt có dấu trong `docs/`; ADR 0004; mutations có `Idempotency-Key`; money BIGINT đến đồng; Outbox routing key canonical; no cross-DB FK; tenant isolation; no commercial dependency; stable URL không chứa signed token. |
| acceptance | Contract ghi đủ request/response/status/error/auth/pagination, gồm complete Trip và invoice download wire shape duy nhất; event payload canonical khớp producer/consumer; money-handler ownership, wallet bootstrap/backfill, phased legacy rollout, state machine, invoice-number primitive, cron UTC/ICT, dedupe keys, retry/backoff/race result và no-platform-fee được freeze; ghi bằng chứng QuestPDF eligibility hoặc chốt `PDFsharp-MigraDoc`, đồng thời ghi approval `Google.Cloud.Storage.V1`; contract tests/JSON schemas pass. |
| source citations | `BE_TIMELINE_VU.md` Day 38; technical context v7 §4.5(e), §4.6, §5; BSOT §5.6, §7.3, §7.4, §8.9 và Hangfire registry; API contract “Operator/Admin Management”; `db-schema/payment-wallet/README.md`. |

### Task 38.1 — Booking/Parcel payment context seams và canonical success/refund events

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment, Booking/Parcel integration seams |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files (write set) | Payment request/context DTOs, booking/parcel create/confirm/refund command handlers và outbound event classes dưới `apps/payment/src/VietRide.Payment.Application/`; internal Payment request DTOs/controllers; narrowly scoped Booking/Parcel payment request builders và trusted legacy snapshot internal endpoints/clients dưới `apps/booking/` và `apps/parcel/`; `Day38PaymentContextBackfillJob`, options/readiness query và tests. Không sở hữu `Payment.cs`, EF configuration hoặc migration của Task 38.3. |
| forbidden scope | Không tạo wallet/ledger/settlement/invoice entity; không sửa DB/migration; không lookup Booking/Parcel/Identity sau success event; không nhận operator/trip/amount snapshot từ client public; không đổi Trip/Notification/Gateway; không `.env`, secrets, other services hoặc git ops. |
| depends on | 38.3. Dependency tuần tự này loại bỏ write-set/compile overlap quanh `Payment.cs` và `payments.context`. |
| invariant flags | Context booking/parcel do owning service dựng từ trusted snapshot và immutable; booking group allocations cộng đúng paid economics; WALLET/VNPay xuất cùng shape; Outbox cùng transaction; outbound event chỉ là integration fact, Payment không bind consumer vào event success/refund của chính mình để ghi tiền; event id stable khi retry publish; no cross-DB transaction/FK; .NET CRLF; MediatR v11; CPM không `Version=`. |
| acceptance | Payment create lưu context cho booking single/round-trip/group và parcel; success/refund event chứa đủ allocation/operator/trip data; redelivery không đổi context; request mới thiếu/malformed context bị reject trước money mutation. Legacy PENDING_REDIRECT callback trong Phase A vẫn settle theo semantics cũ và đánh dấu reconciliation thay vì fail; backfill internal HTTP hydrate đúng context, không overwrite, có dry-run/readiness/quarantine. Unit + contract + upgrade-fixture tests cover WALLET/VNPay, multi-allocation và callback tạo trước deploy. Subscription context thuộc 38.6. |
| source citations | technical context v7 §4.5(e), §4.6 flow 1–3, §6.5; BSOT §7.3 `payment.payment.succeeded`, `payment.subscription.payment_succeeded`; API contract internal Payment và subscription upgrade; source hiện tại `Payment.cs`, `PaymentSucceededIntegrationEvent.cs`, `SubscriptionPaymentSucceededIntegrationEvent.cs`. |

### Task 38.2 — Trip terminal events end-to-end

| Field | Value |
|---|---|
| stack/owner | dotnet — Trip; shared TS contract only where consumer schema is shared |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files (write set) | `CompleteTripCommand`, handler, response và terminal events dưới `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/`; `DriverController.cs` và complete endpoint tests; `AutoCompletedFallbackJob` và registration để dùng chung terminal service; existing disrupt/substitute handlers chỉ để phát canonical event; Trip audit/outbox tests dưới `apps/trip/tests/`; `libs/shared/contracts/src/events/trip-completed.event.ts` và related event tests nếu canonical shared contract vẫn được dùng |
| forbidden scope | Không sửa trip status business rules ngoài terminal emitter; không tạo settlement trong Trip; không gọi Payment HTTP; không sửa Payment schema/API, Notification/Gateway, `.env`, secrets hoặc git ops. |
| depends on | 38.0 |
| invariant flags | `POST /v1/driver/trips/{tripId}/complete`, role `DRIVER|ASSISTANT`, caller assignment check, explicit `Idempotency-Key`, thin controller→MediatR; manual completion ghi `completedByUserId` + audit `TRIP_COMPLETED_MANUAL`; fallback ETA+30 phút dùng cùng atomic terminal/outbox path; `trip.trip.completed` và `trip.trip.disrupted` có `tripId`, `operatorId`, `terminalAt`, `hasSubstitution`; một logical transition không enqueue trùng; audit-only interpretation chỉ thuộc Payment settlement; .cs CRLF, .ts LF. |
| acceptance | Complete endpoint body rỗng trả ADR 0004 `200` với trip/status/completedAt; chỉ assigned DRIVER/ASSISTANT và Trip IN_PROGRESS được complete; same-key replay response gốc, different-key terminal conflict `409 TRIP_ALREADY_TERMINAL`; unassigned/other role fail không mutate. Manual và `AutoCompletedFallbackJob` đều atomically COMPLETED + canonical Outbox, duplicate/manual-vs-fallback race chỉ một event; DISRUPTED thật cũng publish canonical event; disrupted có/không substitution đều đúng payload; contract test loại bỏ TS schema cũ `fareVnd/driverId/passengerId`; Trip build/format/test xanh. |
| source citations | technical context v7 §4.6 bước 4, §8 Trip terminal; BSOT §7.3 `trip.trip.completed`; source `DisruptNoSubstitutionCommandHandler.cs`; `libs/shared/contracts/src/events/trip-completed.event.ts`. |

### Task 38.3 — Payment persistence cho Invoice, wallet, ledger và settlement

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment persistence |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | scaffold-aggregate + ef-migration |
| owned files (write set) | `Payment.cs`, entities/enums dưới `apps/payment/src/VietRide.Payment.Domain/`; repository abstractions; `PaymentDbContext.cs`; EF configurations/repositories; migration mới + model snapshot dưới `apps/payment/src/VietRide.Payment.Infrastructure/Migrations/`; `db-schema/payment-wallet/schema.sql`; Payment persistence tests. Không sửa payment command handlers/context builders của Task 38.1 hoặc 38.6. |
| forbidden scope | Không re-scaffold/drop PlatformWallet đã có; không sửa prior applied migration; không business handler/job/API; không hard FK đến Identity/Trip/Booking/Parcel; không seed output E2E; không `.env`, secrets, other services hoặc git ops. |
| depends on | 38.0 |
| invariant flags | Migration Up/Down/reapply; DDL và EF model đồng bộ; money BIGINT; ledger/transaction immutable; wallet balance CHECK >= 0; row_version optimistic lock; unique invoice payment/number, monthly counter key, settlement operator-trip, wallet operator, wallet transaction reference và ledger source triple; `SUBSCRIPTION_PAYMENT` dùng `reference_id=payment_id`; Invoice/settlement consistency CHECK; logical cross-DB refs only. |
| acceptance | Fresh DB migration, upgrade từ Day-37 fixture, down và reapply pass; `payments.context` generic JSONB round-trip và immutable cho write mới nhưng giữ được `{}` legacy để phased backfill; schema có `context_reconciliation_required`, `invoice_number_counters`, PDF attempts/next-retry/status, settlement failure metadata/index, durable processed-event marker và exact partial unique indexes theo tên cột thật; migration ratify enum/check `SUBSCRIPTION_PAYMENT` không làm mất existing wallet/payment data; atomic counter concurrency test trả dãy unique trong tháng và reset theo period key. |
| source citations | technical context v7 §4.5(e) Invoice entity, §4.6 five entities/state machine; BSOT §3.5, §8.9; `db-schema/payment-wallet/schema.sql` và README; source hiện tại `PlatformWallet.cs`, `PlatformWalletTransaction.cs`. |

### Task 38.4 — Atomic revenue ledger, PlatformWallet hold/refund và OperatorWallet bootstrap

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment money handlers/messaging; Identity backfill emitter |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files (write set) | Exact Payment money handlers `Features/Internal/Payments/ChargePayment/ChargePaymentCommandHandler.cs` (BOOKING/PARCEL/PARCEL_ADDITIONAL WALLET), `Features/Internal/Payments/BatchChargePayment/BatchChargePaymentCommandHandler.cs` (booking group WALLET), `Features/Payments/ConfirmBookingPayment/ConfirmBookingPaymentCommandHandler.cs` (BOOKING/PARCEL/PARCEL_ADDITIONAL VNPay), và `Features/Internal/Wallets/RefundToWallet/RefundToWalletCommandHandler.cs` (BOOKING_REFUND/PARCEL_REFUND); `MarkPaymentRefunded` chỉ để status follow-up, không sở hữu tiền; shared atomic ledger/wallet service; `Day38RevenueLedgerBackfillJob`; Payment consumer + registration cho `identity.operator.approved`; durable processed-event methods thuộc 38.3; `OperatorApprovedIntegrationEvent` thêm payload `eventId`; Identity `operator_wallet_backfill_markers` entity/config/repository/migration + canonical DDL, `OperatorWalletBackfillJob`/Outbox emitter và registration; Payment/Identity unit, upgrade-fixture và PostgreSQL integration tests |
| forbidden scope | Không tạo Payment consumer cho `payment.payment.succeeded` hoặc `payment.payment.refunded`; không move PlatformWallet sau khi money handler đã commit; không tạo settlement từ non-terminal event; không credit OperatorWallet ngoài bootstrap balance 0; không áp platform fee; không direct/cross-DB lookup; không sửa public API, Invoice PDF, Trip, Gateway/Notification; không `.env`, secrets hoặc git ops. |
| depends on | 38.1, 38.3. Không phụ thuộc 38.2; Trip terminal producer chỉ được 38.5 consume. |
| invariant flags | Mỗi original money command tạo stable local operation/source-event id và mỗi allocation tạo đúng một ledger row qua unique `(source_event_id, entry_type, reference_id)`; Payment status/wallet transaction/PlatformWallet update/PlatformWallet transaction/ledger rows/outbound Outbox commit trong cùng Payment transaction; outbound event không được self-consume; refund direction đúng. Approval payload có `eventId`; Identity marker persist một stable eventId và Outbox row trong cùng transaction, không sửa shared Outbox API; Payment chỉ mark processed sau wallet commit. `UNIQUE(operator_id)` + processed-event unique; no distributed transaction. |
| acceptance | WALLET booking đi qua ChargePayment, group qua BatchChargePayment, VNPay qua ConfirmBookingPayment, parcel qua exact charge/confirm handler và refund qua RefundToWallet; mọi path ghi hold/refund + ledger ngay trong transaction tiền gốc. `MarkPaymentRefunded` không debit lần hai. Voucher economics đúng; replay không nhân rows/balance; insert/outbox failure rollback; không bind self-consumer success/refund. Legacy SUCCEEDED backfill chỉ tạo ledger thiếu bằng durable source id, không move PlatformWallet. New approval tạo wallet 0 một lần; backfill marker `(operator_id PK, event_id UNIQUE)` persist stable eventId + enqueue approval payload trong cùng Identity transaction, retry reuse marker; Payment dedupe theo payload eventId. Lazy-create chỉ trong trusted money transaction; GET không tạo row. PostgreSQL upgrade/race tests xanh. |
| source citations | technical context v7 §4.6 bước 1–3 và net formula; §6.5 Wallet; BSOT §7.3 payment events; `db-schema/payment-wallet/README.md` “Wallet model v1” và “OperatorLedgerEntry”. |

### Task 38.5 — Settlement engine, scheduler và vận hành lỗi kẹt

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment settlement/jobs |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | Trip terminal consumers; settlement commands/services; `TripSettlementEligibilityFlagJob`, `TripSettlementWeeklyAutoSettleJob`; Hangfire registration trong Payment Infrastructure/Api; stuck-settlement query/alert service; Redis dedupe integration; settlement runbook dưới `docs/runbooks/`; unit/PostgreSQL integration/race tests |
| forbidden scope | Không bank withdrawal/payout; không settle cancelled trip; không reset historical failure count; không sửa Invoice/subscription payment/public controllers; không filter earned revenue theo current subscription status; không `.env`, secrets, other services hoặc git ops. |
| depends on | 38.2, 38.4 |
| invariant flags | Một settlement row xuyên suốt state machine; terminal UPSERT `(operator_id,trip_id)` khi net > 0; terminalAt+7d; daily cron `0 19 * * *` UTC; weekly `0 2 * * 1` UTC; settle recompute net; CAS status+rowVersion; Platform DEBIT + Operator CREDIT + two transactions + outbox atomic; no negative balance. |
| acceptance | Net <= 0 chuyển CANCELLED không movement; manual/weekly chỉ một winner; same manual Idempotency-Key replay response gốc, weekly loser no-op, different manual key loser `409 TRIP_SETTLEMENT_ALREADY_SETTLED`; insufficient balance rollback, giữ ELIGIBLE và retry các tuần sau không giới hạn; mỗi failure tăng count, set last/active error; HIGH khi count >=3 **HOẶC** stuck >21 ngày; stuck filter chỉ gồm ELIGIBLE có active failure; Redis `payment:settlement_insufficient:{settlementId}` giới hạn alert 24h; success giữ historical count/time, clear active error, set `failure_resolved_at`, biến mất khỏi stuck filter; integration race lặp nhiều lần trên PostgreSQL thật. Đây là acceptance theo quyết định người dùng đã phê chuẩn ở Revision 6. |
| source citations | technical context v7 §4.6 bước 4–7, status transition lock/errors; BSOT §4.5 Hangfire, §8.9; payment schema README “Trip settlement state machine”; Revision 6 failure/recovery decisions. |

### Task 38.6 — Thanh toán subscription bằng OperatorWallet

| Field | Value |
|---|---|
| stack/owner | dotnet — Identity + Payment subscription seam |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint + add-integration-event |
| owned files (write set) | subscription upgrade DTO/handler/internal clients dưới `apps/identity/`; `CreateSubscriptionPayment` request/command và Payment context builder cho trusted subscription billing/buyer snapshot; internal OperatorWallet subscription charge endpoint/command dưới `apps/payment/`; `ConfirmSubscriptionPayment` canonical success producer cho VNPay và cùng producer path cho WALLET; narrowly scoped Gateway contract tests nếu cần; Identity/Payment unit/integration tests. Không sửa `Payment.cs`/EF/migration thuộc 38.3. |
| forbidden scope | Không direct DB access giữa Identity và Payment; không distributed transaction; không debit PassengerWallet; không tạo Invoice trực tiếp trong Identity; không thay VNPay callback semantics; không bank withdrawal; không `.env`, secrets hoặc git ops. |
| depends on | 38.4 |
| invariant flags | Identity dựng server-side subscription/buyer snapshot; Payment nhận và persist snapshot vào immutable `payments.context`; Payment local transaction atomically OperatorWallet DEBIT + OperatorWalletTransaction `SUBSCRIPTION_PAYMENT/reference_id=payment_id` + PlatformWallet CREDIT + matching platform transaction + Payment SUCCEEDED + Outbox; unique reference chặn double movement; balance không âm; WALLET/VNPay cùng event; Idempotency-Key 24h semantics. |
| acceptance | WALLET đủ tiền activate qua existing eventual subscription event và trả response contract; payment context round-trip đủ plan/period/buyer cho cả WALLET/VNPay; thiếu tiền trả `402 WALLET_INSUFFICIENT_BALANCE` không đổi subscription/wallet; replay same key không double debit; key mismatch reject; concurrent charges chỉ một success nhờ payment/reference dedupe; cross-tenant operator không dùng wallet khác; VNPay flow regression xanh. |
| source citations | API contract `POST /v1/operator/subscription/upgrade` Day 37 carry-over; technical context v7 §4.5 subscription payment, §4.6 PlatformWallet `SUBSCRIPTION_PAYMENT`; BSOT §5.6 idempotency, §7.3 event registry; source `CreateSubscriptionPayment`, `ConfirmSubscriptionPayment`. |

### Task 38.7 — Invoice PDF, Firebase storage, reconciliation và admin retry

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment Invoice |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | Invoice consumer/features/controllers và `Day38InvoiceBackfillJob`; invoice-number counter repository implementation sử dụng primitive 38.3; `IInvoicePdfGenerator`, `IInvoiceStorage` và implementations; PDF template; bundled `NotoSans-Regular.ttf`, `NotoSans-Bold.ttf`, OFL-1.1 license và font resolver; Firebase adapter; `InvoicePdfGenerationJob`, `InvoicePdfReconciliationJob`; Payment DI/options; `apps/payment/src/VietRide.Payment.Api/appsettings.json`, root `.env.example`, `infra/docker/docker-compose.yml` và `.prod.yml` chỉ cho non-secret bucket/ADC/workload-identity mapping; `Directory.Packages.props` và Payment Infrastructure csproj cho provider đã chốt (`QuestPDF` nếu eligible, nếu không `PDFsharp-MigraDoc`) cùng `Google.Cloud.Storage.V1`; Payment/Linux-container/upgrade-fixture tests. Không ghi credential thật vào file. |
| forbidden scope | Không e-invoice provider; không persist signed URL/token; không public unauthenticated object; không retry quá năm attempts; không log PDF buyer email/full payload; không sửa settlement/Trip/Notification/Gateway routes; không hardcode Firebase secret; không git ops. |
| depends on | 38.6; 38.0 license/provider decision phải hoàn tất |
| invariant flags | `UNIQUE(payment_id)` + `UNIQUE(invoice_number)`; monthly counter UPSERT và Invoice insert cùng transaction tạo `VR-INV-yyyyMM-XXXXXX`; DRAFT trước render, ISSUED chỉ sau upload; object path canonical; CAS PENDING→PROCESSING increment attempts **trước** render; max 5 total; stale PROCESSING >15 phút đã tiêu một attempt và reconciliation không increment lần hai; outbox after ISSUED; config/options không chứa secret. |
| acceptance | WALLET/VNPay success đi chung pipeline; duplicate event tạo một Invoice và counter không tăng do replay; legacy subscription SUCCEEDED backfill tạo đúng Invoice còn thiếu mà không credit PlatformWallet lần hai. Lifecycle freeze: create `DRAFT/PENDING/attempts=0`; worker CAS `PENDING→PROCESSING` và increment; success upload rồi atomic `COMPLETED + ISSUED + Outbox`; failure khi attempts<5 chuyển `FAILED`, set `next_retry_at` theo backoff `[1,5,15,30]` phút; failure ở attempt 5 chuyển terminal `FAILED` với `next_retry_at=NULL`. Reconciliation cron mỗi 5 phút CAS due `FAILED→PENDING` rồi enqueue; stale PROCESSING chuyển FAILED/due hoặc terminal theo attempts nhưng không increment. Admin retry chỉ nhận retryable FAILED có attempts<5 và chưa pending: CAS `FAILED→PENDING`, clear scheduled `next_retry_at`, enqueue; request không tự tăng attempt, lần claim PROCESSING kế tiếp mới tăng. Cùng admin idempotency key replay original 202; two different keys race, one 202 one `409 INVOICE_RETRY_ALREADY_PENDING`; PENDING/PROCESSING bởi job khác cũng trả already-pending; ISSUED/CANCELLED/attempts=5 trả `INVOICE_RETRY_NOT_ALLOWED`. Authorized download trả duy nhất `200 ApiResponse<{downloadUrl,expiresAt}>`, signed URL mới TTL 60m, rate limit 10/min/user/invoice. Config bind thành công trong dev/prod compose mà không chứa secret; Noto Sans resolver render ổn định tiếng Việt trong Linux container; PDF đủ invoice number, period, plan, amount, VAT note, publisher/buyer. |
| source citations | technical context v7 §4.5(e); BSOT Invoice rules, Hangfire `InvoicePdfRetryJob`, event registry; API contract operator invoice endpoints; Revision 6 retry/stale/signed URL decisions. |

### Task 38.8 — Operator/Admin APIs và Gateway routes

| Field | Value |
|---|---|
| stack/owner | dotnet Payment + NestJS Gateway |
| implement agent | worker |
| review agent | reviewer |
| skill | add-endpoint |
| owned files (write set) | Payment queries/commands/controllers/validators cho wallet, transactions, ledger, settlements, adjustments và invoice list/detail; `apps/gateway/src/config/routes.ts` gồm route Payment và driver Trip complete; Gateway route tests; Payment endpoint tests; API/Postman cumulative collection và environment wrapper. Invoice admin-retry/download Payment controllers thuộc 38.7, task này chỉ expose route qua Gateway. |
| forbidden scope | Không business logic trong controller/Gateway; không operator write ngoài subscription payment; không expose notes/PII không cần thiết; không wildcard route; không thay settlement/invoice job internals; không `.env`, secrets, other services hoặc git ops. |
| depends on | 38.5, 38.6, 38.7 |
| invariant flags | Thin controller→MediatR; ADR 0004; exact roles; operatorId từ claims; SYSTEM_ADMIN global only; mutations require Idempotency-Key explicit validation; pagination <=100; optimistic lock; audit actor/action/metadata; .cs CRLF, .ts/.json LF. |
| acceptance | Tất cả endpoint contract, gồm Trip complete và invoice download `200` authenticated response, accessible qua Gateway; event có `invoiceWebUrl` cho Operator Web và `downloadApiUrl` protected qua Gateway để frontend gửi Bearer rồi nhận signed URL; operator A không đọc B và cross-tenant 404/403 không leak; wallet/platform adjustment atomic, negative guard kể cả concurrent DEBIT; same key replay; settlement stuck filter chỉ lấy unresolved ELIGIBLE và severity HIGH dùng count>=3 **HOẶC** age>21d; exact errors/status; Swagger và Gateway tests xanh; Postman Day 38 folder dùng cumulative collection hiện có. Các filter/rate/retry rules giữ nguyên quyết định người dùng Revision 6. |
| source citations | technical context v7 §4.6 Operator/System Admin endpoints và errors; API contract Operator/Admin Management; BSOT endpoint registry §5.2/§5.6; `apps/gateway/src/config/routes.ts`. |

### Task 38.9 — Notification consumers, email và PII-safe observability

| Field | Value |
|---|---|
| stack/owner | nest — Notification |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | vietride-nest-event |
| owned files (write set) | Notification event constants/schemas/mapper/consumer; notification enum/schema/migration nếu cần type riêng; email template/delivery adapter; processed-event persistence; redaction tests/config; Notification unit/E2E tests |
| forbidden scope | Không lookup Payment DB; không fetch signed Firebase URL; không log email/full payload/download token; không reuse `SUBSCRIPTION_APPROVED` cho Invoice; không production noop provider; không sửa Payment/Gateway; không `.env`, secret, generated Prisma output thủ công hoặc git ops. |
| depends on | 38.7 contract/event output; có thể implement song song 38.8 sau 38.7 |
| invariant flags | RabbitMQ processed marker chỉ sau DB/enqueue success; processing lock NX TTL; idempotent side effects; dedicated INVOICE_ISSUED type; settlement parses `netAmount`; recipient là active OPERATOR_ADMIN của tenant; email/push/in-app dùng `invoiceWebUrl`, không dùng protected `downloadApiUrl` làm href; pino redaction ở logger level; LF; Prisma migration deployable. |
| acceptance | Invoice event tạo một in-app, một push và một email/eligible admin với Operator Web invoice-detail deep-link; click flow yêu cầu đăng nhập rồi frontend gọi protected `downloadApiUrl` bằng Bearer để lấy signed URL. Không email nào link trực tiếp protected API hoặc signed URL. Settlement event tạo đúng notification dùng netAmount; replay không duplicate; transient provider error requeue; log capture test chứng minh không có email/full payload/signed URL; malformed event không poison loop; lint/test/e2e/build và Prisma validate/generate xanh. |
| source citations | technical context v7 §4.5(e) bước 4 và §4.6 settlement event; BSOT §7.3/§7.4; source `parcel-subscription-operator-notification.mapper.ts` hiện reuse SUBSCRIPTION_APPROVED và đọc `amount`; Nest event-handling guide. |

### Task 38.10 — Real-stack black-box E2E và verification gate

| Field | Value |
|---|---|
| stack/owner | cross-cutting QA/infra |
| implement agent | worker |
| review agent | reviewer |
| skill | smoke-test + vietride-postman-api-test-plan |
| owned files (write set) | `infra/docker/docker-compose.day38-e2e.yml`; `scripts/run-day38-invoice-settlement-e2e.mjs`; root `package.json` script only; cumulative Postman collection/environment; E2E-only config/provider fixtures; narrowly scoped real-PostgreSQL race test projects |
| forbidden scope | Không mock DB/HTTP service/in-memory repository cho acceptance; không dùng hoặc xóa dev DB; không seed output cần test; không production credentials; không log JWT/private key/signed URL; không sửa feature behavior để làm test pass; không `.env`, secrets hoặc git ops. |
| depends on | 38.8, 38.9 |
| invariant flags | Isolated compose project; Postgres/Redis/RabbitMQ và service thật; deterministic `38000000-...` UUID; API qua Gateway; finite poll timeout; psql read-only assertions; Outbox→RabbitMQ→consumer evidence; cleanup `down -v` trong finally. |
| acceptance | `npm run e2e:day38` exit 0, đủ 26 scenario bên dưới; summary in từng gate PASS; direct persistence assertions chứng minh ledger balanced/dedupe/race/retry; stack luôn cleanup kể cả fail; toàn bộ verification matrix cuối plan xanh. |
| source citations | Day 38 timeline DoD/Review; technical context v7 §4.5(e), §4.6; BSOT §3.5, §4.5, §7.4; API contract; mẫu real-stack Day 36 acceptance được người dùng yêu cầu. |

## Real-stack E2E acceptance

### Harness

Thêm:

```text
infra/docker/docker-compose.day38-e2e.yml
scripts/run-day38-invoice-settlement-e2e.mjs
npm run e2e:day38
```

Mặc định harness dùng project name riêng, chạy `docker compose down -v` trước run, `up -d --build`, chờ `/health` và `/ready`, migrate qua startup/deploy command thật, seed prerequisites bằng `docker exec ... psql`, mint JWT development ngắn hạn và gọi REST qua Gateway. Các event đi qua Outbox/RabbitMQ thật; async consumer được poll với timeout hữu hạn. `finally` luôn `docker compose down -v`; optional `DAY38_E2E_USE_DEV_STACK=1` chỉ dành cho developer, CI/default không dùng và tuyệt đối không xóa volume dev.

Services thật: PostgreSQL, Redis, RabbitMQ, Identity, Trip, Booking, Parcel, Payment, Notification và Gateway. Firebase/email/push dùng local test provider adapter tại boundary ngoài hệ thống, nhưng không mock Payment/Identity/Trip/Notification HTTP, DB, repository hoặc RabbitMQ.

### Deterministic seed

- UUID prefix `38000000-...` cho mọi fixture; seed idempotent.
- Identity: SYSTEM_ADMIN A/B; Operator A approved mới với OPERATOR_ADMIN + STAFF; Operator B và admin của B để cross-tenant; ít nhất một operator đã approved trước Day 38 để test backfill/lazy-create; subscription plans và upgrade attempt prerequisites; buyer/publisher tax snapshot không chứa production PII.
- Trip: assigned driver và assistant; Trip A IN_PROGRESS cho manual complete, Trip F IN_PROGRESS quá ETA+30 phút cho fallback, Trip B DISRUPTED no substitution, Trip C DISRUPTED with substitution, Trip D không có revenue, Trip E cho race, các terminal/eligible timestamps cố định.
- Booking/Parcel: booking single, booking group hai leg/operator-trip allocation, VietRide-funded/operator-funded voucher, parcel paid/refunded; chỉ seed prerequisite không có public creation path, output ledger/payment/settlement phải tạo qua API/event thật.
- Payment: singleton PlatformWallet; OperatorWallet A/B với balance phù hợp; payments phải tạo qua internal/public flow thật; không seed invoice, ledger, settlement, transaction hoặc processed-event output.
- Notification: active device/test provider và template prerequisite; không seed notification output.
- Clock/time-warp chỉ bằng E2E config hoặc update timestamp trong isolated DB; không sleep bảy ngày và không thay production clock.

### 26 black-box scenarios

**E2E-01 — Bootstrap và schema invariants**

Chạy cả fresh DB và Day-37 upgrade fixture. Replay `identity.operator.approved`; assert payload `eventId`, consumer durable marker và đúng một OperatorWallet balance 0, một PlatformWallet singleton, migrations applied, exact partial unique indexes hiện hữu và replay không duplicate. Chạy Identity backfill hai lần cho operator approved cũ; marker giữ stable eventId dù Outbox row dùng id riêng. Race backfill với money-operation lazy-create vẫn đúng một wallet/operator, GET thuần đọc không tự tạo wallet.

**E2E-02 — Booking WALLET hold và trusted context**

Tạo/charge booking qua Gateway bằng PassengerWallet; assert Payment SUCCEEDED có immutable context, PassengerWallet DEBIT, PlatformWallet CREDIT và BOOKING_REVENUE ledger đúng số tiền trong cùng transaction của original handler. Publish/replay outbound success event và chứng minh Payment không self-consume để CREDIT PlatformWallet lần hai.

**E2E-03 — Booking VNPay callback idempotency**

Tạo VNPay payment và gọi callback thành công hai lần; assert một hold transaction, một ledger entry, một published success event và PlatformWallet chỉ tăng một lần. Upgrade fixture tạo PENDING_REDIRECT trước Phase A với context `{}` rồi callback sau deploy: callback vẫn success/move tiền đúng một lần, row được đánh reconciliation; context backfill qua internal HTTP rồi revenue backfill chỉ thêm ledger, không credit PlatformWallet lần hai. Phase B readiness chỉ pass sau khi row được xử lý/quarantine.

**E2E-04 — Booking group multi-allocation**

Thanh toán booking group có nhiều allocation; assert một payment event tạo đúng ledger row cho từng `(operator, trip, reference)` theo context, tổng allocation khớp economics và unique triple cho phép nhiều reference nhưng chặn replay.

**E2E-05 — Parcel revenue và voucher economics**

Thanh toán parcel và booking có hai funding types; assert PARCEL_REVENUE, VOUCHER_VIETRIDE_FUNDED_CREDIT dương, VOUCHER_OPERATOR_FUNDED_AUDIT bằng 0; không có platform fee deduction.

**E2E-06 — Refund trước settlement**

Refund booking/parcel qua flow thật; assert PassengerWallet CREDIT, PlatformWallet DEBIT, negative refund ledger, no OperatorWallet movement và replay không duplicate.

**E2E-07 — Trip COMPLETED tạo marker**

Hoàn tất Trip A qua `POST /v1/driver/trips/{tripId}/complete` qua Gateway với assigned DRIVER/ASSISTANT token và Idempotency-Key; assert `200` ADR 0004, assignment/IN_PROGRESS guard, completedBy/audit và Trip + Outbox atomic. Same-key replay response gốc, different-key terminal request trả `409 TRIP_ALREADY_TERMINAL`, unassigned caller bị từ chối không mutate. Trigger fallback cho Trip F và race manual-vs-fallback; mỗi Trip chỉ có một canonical event. Payment tạo đúng một settlement row PENDING_HOLD/Trip, `eligibleAt=terminalAt+7d`; replay event không tạo row mới.

**E2E-08 — DISRUPTED, substitution và zero revenue**

Disrupt Trip B/C với `hasSubstitution=false/true`; Payment settlement áp cùng formula cho cả hai. Trip D net <=0 không tạo marker. Assert field được lưu audit nhưng không đổi net/status **trong Payment settlement**; không đưa assertion audit-only này sang behavior substitution khác của Trip/Notification.

**E2E-09 — Invoice transient failure rồi recovery**

Subscription VNPay success tạo DRAFT; test storage fail một attempt rồi thành công; assert attempt increments, object path canonical, ISSUED một lần, PDF hợp lệ và một `payment.invoice.issued`.

**E2E-10 — Eligibility cron UTC/ICT**

Time-warp PENDING_HOLD quanh boundary; chạy job; chỉ row `eligible_at <= now` thành ELIGIBLE. Re-run no-op; kiểm tra cron `0 19 * * *` UTC tương ứng 02:00 ICT.

**E2E-11 — Weekly auto-settlement balanced**

Chạy Monday job trên ELIGIBLE; assert PlatformWallet DEBIT = OperatorWallet CREDIT = recomputed net, hai transaction cùng reference settlement, status SETTLED/AUTO_WEEKLY và một completed event.

**E2E-12 — Manual settle vs weekly race**

Chạy POST manual và weekly job đồng thời, lặp trên PostgreSQL thật. Một winner duy nhất; manual loser trả `409 TRIP_SETTLEMENT_ALREADY_SETTLED`, weekly loser no-op; không double debit/credit/outbox.

**E2E-13 — PlatformWallet insufficient balance và retry vô hạn**

Làm PlatformWallet thiếu balance; weekly attempt rollback toàn bộ và settlement vẫn ELIGIBLE, failure count/time/active error tăng. Re-run đến count 3 hoặc time-warp >21 ngày cho HIGH theo OR; alert Redis tối đa một/24h. Nạp đủ balance rồi retry success: giữ history, clear active error, set resolved time, không còn trong stuck filter.

**E2E-14 — Wallet/platform adjustments và concurrent negative guard**

Gọi admin CREDIT/DEBIT và chạy hai DEBIT đồng thời; balance không âm, một loser trả `WALLET_INSUFFICIENT_BALANCE`/platform equivalent, audit và transaction atomic; replay same idempotency key không nhân movement.

**E2E-15 — Late refund trước settle và net <= 0**

Sau marker nhưng trước settle, tạo refund; weekly recompute số mới. Case net <=0 chuyển CANCELLED, không wallet transaction/event; late refund không dùng frozen net cũ.

**E2E-16 — OperatorWallet subscription payment**

Upgrade bằng WALLET đủ tiền; assert OperatorWallet DEBIT và PlatformWallet CREDIT đều có `reference_type=SUBSCRIPTION_PAYMENT`, `reference_id=payment_id`, unique reference không duplicate; Payment SUCCEEDED, Identity subscription active qua event và Invoice pipeline được trigger; không có platform fee interpretation trên trip revenue.

**E2E-17 — Subscription auth, validation và insufficient balance**

Test missing/mismatch Idempotency-Key, OPERATOR_STAFF/passenger forbidden, invalid paymentMethod/returnUrl, cross-tenant và insufficient balance; assert exact ADR 0004 status/error và không có partial Payment/wallet/subscription change.

**E2E-18 — Subscription idempotency và concurrent charge**

Replay cùng key/payload trả response gốc; cùng key khác payload trả mismatch; hai key concurrent cho cùng attempt chỉ một debit/payment success/invoice trigger.

**E2E-19 — WALLET và VNPay dùng chung invoice trigger**

Tạo một success mỗi method; assert cùng routing key/schema/consumer, mỗi payment một Invoice, không có branch-specific lookup hoặc duplicate PlatformWallet subscription credit.

**E2E-20 — Invoice unique và PDF content**

Replay subscription success; assert `UNIQUE(payment_id)` và counter không tăng do replay. Upgrade fixture có subscription SUCCEEDED trước Day 38 nhưng chưa Invoice; context/invoice backfill tạo đúng một Invoice và không credit PlatformWallet lần hai. Tạo nhiều invoice đồng thời qua hai boundary tháng cố định; assert PostgreSQL counter UPSERT cấp `VR-INV-yyyyMM-000001...` unique, reset theo `period_key`, counter + Invoice commit cùng transaction và `INVOICE_NUMBER_EXHAUSTED` rollback ở 999999. Linux container dùng bundled Noto Sans render tiếng Việt không tofu, PDF chứa period/plan/amount/VAT/publisher/buyer và không chứa secret/token.

**E2E-21 — Invoice năm attempts và stale PROCESSING**

Ép render/upload fail và stale >15 phút nhiều lần; assert trạng thái `PENDING→PROCESSING→FAILED`, backoff `1/5/15/30` phút qua `next_retry_at`, reconciliation 5 phút chỉ requeue khi due; mỗi CAS vào PROCESSING increment một attempt, stale recovery không cộng lần hai; attempt 5 giữ FAILED terminal với `next_retry_at=NULL`, không loop vô hạn và không phát issued event.

**E2E-22 — Admin invoice retry idempotency và race**

FAILED còn attempts và chưa pending: manual retry CAS sang PENDING/enqueue nhưng chưa tăng attempt; worker claim PROCESSING mới tăng. Cùng Idempotency-Key replay original `202`; hai admin/two keys concurrent, một enqueue và loser `409 INVOICE_RETRY_ALREADY_PENDING`; auto job đã PENDING/PROCESSING cũng trả already-pending; ISSUED/CANCELLED/max-attempt trả `409 INVOICE_RETRY_NOT_ALLOWED`.

**E2E-23 — Invoice download authorization, TTL và rate limit**

Frontend dùng Bearer gọi stable Gateway endpoint và nhận `200 ApiResponse<{downloadUrl,expiresAt}>`; owner gọi hai lần nhận signed URL mới với TTL <=60m, không chấp nhận redirect/raw URL wire shape. DB/event chỉ có stable endpoint/object path. Operator B không biết invoice A; request thứ 11 trong một phút/user/invoice trả 429; log không chứa signed query.

**E2E-24 — Operator/Admin read APIs và tenant isolation**

Qua Gateway, kiểm tra wallet, history, pending summaries, settlements, ledger, invoices, admin platform/stuck lists; pagination/filter/sort đúng; Operator A/B cách ly; role/status/error envelope đúng.

**E2E-25 — Notification, email và PII redaction**

Poll Notification DB/test providers: mỗi Invoice tạo đúng một in-app/push/email cho active OPERATOR_ADMIN với `invoiceWebUrl`. Email click vào Operator Web login/detail flow, không trỏ protected `downloadApiUrl` hoặc signed URL; frontend authenticated mới gọi download API. Settlement tạo WALLET_CREDITED dùng `netAmount`. Replay không duplicate; captured structured logs không có email, full event payload hoặc signed URL.

**E2E-26 — End-to-end reconciliation và replay audit**

Replay toàn bộ producer events/jobs và ba backfill job một lần nữa; assert không side effect mới. Đối chiếu trực tiếp tổng PlatformWallet movements, OperatorWallet movements, ledger net, settlements và Invoice. Mọi legacy payment thuộc phạm vi có context hoặc quarantine marker/runbook; không còn readiness gap trước Phase B. Mọi Outbox PUBLISHED, consumer marker hiện hữu, RabbitMQ queue không poison, không row mixed/partial.

### Direct persistence assertions

Sau mỗi scenario, harness dùng psql read-only và Redis CLI assertions:

- Payment DB: `payments` gồm context/reconciliation flag, `wallets`, `wallet_transactions`, `platform_wallets`, `platform_wallet_transactions`, `operator_wallets`, `operator_wallet_transactions`, `operator_ledger_entries`, `operator_trip_settlements`, `invoices`, `invoice_number_counters`, durable processed/backfill markers, `outbox_events`, Hangfire state. Đối chiếu exact partial unique index, `SUBSCRIPTION_PAYMENT/payment_id`, invoice counter/number, legacy readiness/quarantine và chứng minh không có self-consumed money movement.
- Identity DB: subscription/upgrade attempt terminal state, `operator_wallet_backfill_markers(operator_id,event_id)`, approval payload eventId, Outbox và consumer result.
- Trip DB: terminal status, substitution audit và Outbox row.
- Notification DB: notifications, delivery/email rows, processed-event/idempotency markers.
- Redis: endpoint idempotency records, download rate-limit key, insufficient-balance alert dedupe key; không log value nhạy cảm.
- RabbitMQ correctness được chứng minh bằng producer Outbox PUBLISHED + consumer DB side effect + replay không nhân side effect; HTTP 2xx đơn lẻ không được coi là E2E pass.

### Postman cumulative collection

Thêm folder Day 38 vào collection duy nhất hiện có, không tạo collection riêng. Folder cover Trip complete, subscription WALLET/VNPay, invoice list/detail/authenticated-download-200/retry, operator wallet/transactions/ledger/settlements, admin platform wallet/adjust/stuck/manual settle, idempotency replay, auth/tenant/validation/rate-limit errors. Runtime IDs/tokens lấy từ deterministic seed qua local wrapper; async DB assertions, backfill/lazy-create race, monthly invoice counter, five-attempt loop và concurrency race nằm trong `run-day38-invoice-settlement-e2e.mjs`.

### Verification gate

Task 38.10 chỉ pass khi `npm run e2e:day38` exit code 0 và summary có:

```text
seed/bootstrap PASS
legacy-upgrade-backfill PASS
payment-context PASS
platform-hold-ledger PASS
trip-terminal-marker PASS
eligibility-weekly-settlement PASS
insufficient-balance-recovery PASS
operator-wallet-subscription PASS
invoice-pdf-retry PASS
operator-admin-api PASS
notification-email-redaction PASS
race-idempotency PASS
database-reconciliation PASS
cleanup PASS
```

Ngoài black-box E2E phải chạy:

```text
dotnet build/format/test apps/payment/VietRide.Payment.sln
dotnet build/format/test apps/identity/VietRide.Identity.sln
dotnet build/format/test apps/trip/VietRide.Trip.sln
dotnet build/format/test apps/booking/VietRide.Booking.sln
dotnet build/format/test apps/parcel/VietRide.Parcel.sln
npx nx run gateway:lint && gateway:test && gateway:test:e2e && gateway:build
npx prisma validate --schema=apps/notification/prisma/schema.prisma
npx nx run notification:generate && notification:lint && notification:test && notification:test:e2e && notification:build
Payment migration up/down/reapply trên PostgreSQL thật
Day-37 Payment/Identity upgrade fixture + Phase-A callback/backfill/readiness PASS
Payment PDF Linux-container Unicode font/config binding PASS
Notification Prisma migration deploy trên schema fresh và existing-baseline fixture
```

## Dispatch order

1. Task 38.0 khóa contract, state machine, retry/race và dependency/license baseline.
2. Task 38.2 (Trip terminal) và Task 38.3 (Payment persistence) bắt đầu song song sau 38.0. Nhánh Trip 38.2 không chặn revenue context/ledger.
3. Task 38.1 chạy sau 38.3 để dùng `payments.context` đã chốt; Task 38.4 chạy sau 38.1 + 38.3 và không phụ thuộc 38.2.
4. Task 38.6 chạy sau 38.4. Task 38.5 chỉ bắt đầu khi cả 38.2 và 38.4 hoàn tất vì đây mới là điểm Trip terminal consumer cần producer thật. Merge tuần tự thay đổi chung ở `Program.cs`, DI và Hangfire registration.
5. Task 38.7 sau 38.6 để cả WALLET/VNPay đã có canonical trigger.
6. Tasks 38.8 và 38.9 parallel-safe sau 38.7; API/Gateway và Notification có write set tách biệt.
7. Task 38.10 sau 38.8 + 38.9; đây là real-stack acceptance cuối.

```text
38.0 ─┬→ 38.2 ───────────────┬→ 38.5 ───────────┐
      └→ 38.3 → 38.1 → 38.4 ┘                   ├→ 38.8 ─┐
                              └→ 38.6 → 38.7 ───┘        ├→ 38.10
                                              └→ 38.9 ───┘
```

## Progress tracker

> Orchestrator bookkeeping — main thread cập nhật sau mỗi `/implement-task`. Bảng này chỉ mang tính thông tin; `/audit-day` phải xác minh độc lập theo SOT và verification gate.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 38.0 | ✅ done | APPROVED (user override + main-thread contract review) | 2026-07-13 | Contract/SOT/runbook/Zod schemas; `contracts:build` và 18 contract tests pass. |
| 38.1 | ✅ done | APPROVED (user override + main-thread integration review) | 2026-07-13 | Trusted context cho Booking/Parcel single, round-trip/group và refund; WALLET/VNPay canonical events; legacy callback quarantine; internal snapshot backfill dry-run/readiness. Payment 73 unit + 22 integration, Booking 324 unit + 5 focused HTTP integration, Parcel 153 unit + 18 integration pass; full Booking DB suite chờ PostgreSQL local. |
| 38.2 | ✅ done | APPROVED (user override + main-thread terminal-race review) | 2026-07-13 | Driver/assistant complete endpoint, PostgreSQL row lock, canonical completed/disrupted events, ETA+30 fallback dùng chung handler, manual/fallback audit fields. Trip build/format, 235 unit và focused 422 endpoint test pass; mutation replay/race dùng Redis/PostgreSQL thật thuộc 38.10. |
| 38.3 | ✅ done | APPROVED (user override + main-thread persistence review) | 2026-07-13 | Build/format, 70 unit + 22 integration pass; PostgreSQL fresh/up/down/reapply, legacy preservation, JSONB round-trip và concurrent counter pass; isolated container cleaned. |
| 38.4 | ✅ done | APPROVED (user override + main-thread money-path review) | 2026-07-13 | Atomic revenue/refund ledger gắn trực tiếp vào charge/batch/VNPay/refund; batch credit PlatformWallet; voucher economics; deterministic legacy ledger backfill; approval eventId + durable Identity backfill marker + OperatorWallet consumer dedupe. Payment 76 unit + 22 integration, Identity 243 unit pass; migration model-drift check pass, PostgreSQL up/down/race thuộc 38.10. |
| 38.5 | ✅ done | APPROVED (user override + main-thread settlement review) | 2026-07-13 | Terminal consumers + processed-event dedupe; PENDING_HOLD→ELIGIBLE/CANCELLED→SETTLED state machine; daily/weekly UTC jobs; CAS row lock, atomic Platform debit/Operator credit/outbox; failure history/resolution, Redis 24h alert và HIGH dùng OR. Payment 78 unit pass; repeated PostgreSQL race thuộc 38.10. |
| 38.6 | ✅ done | APPROVED (user override + main-thread subscription money-path review) | 2026-07-14 | WALLET/VNPay trusted subscription snapshot và canonical success event; atomic OperatorWallet debit + PlatformWallet credit + outbox; method-specific response/validation và replay checks. Payment 83 unit, Identity 249 unit, cả hai build sạch. |
| 38.7 | ✅ done | APPROVED (worker verification + main-thread lifecycle review) | 2026-07-14 | Invoice consumer/backfill, atomic numbering, PDFsharp + bundled Noto Sans/OFL, private GCS storage, 5-attempt retry/reconciliation, admin retry và signed download. Bổ sung attempt-token CAS cho stale-worker race và test-host JWT config. Payment build sạch; 85 unit, 25 integration, Linux PDF/font và dev/prod compose config pass. Live Firebase/PostgreSQL races thuộc 38.10. |
| 38.8 | ✅ done | APPROVED (main-thread API/tenant/concurrency review) | 2026-07-14 | Operator wallet/transactions/ledger/settlement và invoice list/detail; admin settlement/platform/operator adjustments; claim-only tenant scope, pagination/filter/sort, HIGH OR age/count, optimistic negative guard và structured audit. Gateway explicit routes gồm dynamic operator-wallet mutation và Trip complete; cumulative Postman Day 38 có 15 requests/replay. Payment build, 85 unit, 30 integration; Gateway lint, 146 tests, build pass. Repeated PostgreSQL races thuộc 38.10. |
| 38.9 | ✅ done | APPROVED (main-thread consumer/PII review + real-stack verification) | 2026-07-14 | Invoice issued và settlement completed fan-out đúng OPERATOR_ADMIN; push, in-app, email dùng durable dedupe; logger redaction chặn email/full payload/signed URL. Notification lint pass với 2 warning có sẵn, 114 unit + 14 E2E và build pass. |
| 38.10 | ✅ done | APPROVED (independent Day-38 audit) | 2026-07-14 | `npm run e2e:day38` isolated real stack pass 26/26 và đủ 13 business gate, gồm Phase-A legacy callback/hydration/backfill/readiness, PostgreSQL/Redis/RabbitMQ side effects, races, PDF, notification và cleanup. Payment 85 unit + 31 integration; toàn bộ Release build/format/test, EF/Prisma migration và hard-invariant gates xanh. |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Business invariants

- Một Payment giữ một trusted immutable context; consumer không hydrate business facts bằng cross-service DB lookup.
- Original Payment money handler là nơi duy nhất ghi PlatformWallet movement và OperatorLedger cho success/refund; integration event outbound không được Payment tự consume để lặp money movement.
- Một event/allocation chỉ tạo một ledger entry; một payment subscription chỉ có một Invoice; một operator-trip chỉ có một settlement row.
- Mỗi approved operator có đúng một OperatorWallet; approval consumer có durable dedupe, backfill phát qua Identity Outbox, lazy-create chỉ xảy ra trong trusted money mutation và GET không tạo dữ liệu.
- OperatorTripSettlement marker và settlement là cùng một entity/row, không tạo row mới khi đổi trạng thái hoặc retry.
- OperatorWallet chỉ credit bởi settlement/admin adjustment và có thể debit bởi subscription/admin adjustment; subscription dùng `SUBSCRIPTION_PAYMENT/reference_id=payment_id`; không bank withdrawal trong v1.
- Booking/parcel revenue không trừ platform fee. Subscription payment là doanh thu SaaS của VietRide và CREDIT PlatformWallet.
- Riêng Payment settlement coi `hasSubstitution` là audit metadata; COMPLETED và DISRUPTED dùng cùng settlement economics. Không suy rộng quy tắc này sang domain khác.
- Settlement không freeze net tại marker creation; luôn recompute từ eligible ledger tại settle time.
- Không wallet balance nào âm; mọi balance update và transaction row tương ứng commit atomic trong Payment DB.
- Invoice number dùng monthly counter PostgreSQL atomic trong cùng transaction tạo Invoice; unique payment/number và counter primitive là hàng rào cuối cho concurrency.
- Invoice không ISSUED trước khi PDF upload thành công; không phát notification cho DRAFT/FAILED. Mỗi lần claim PROCESSING mới tiêu một trong năm attempts; retry/reconciliation không cấp attempt miễn phí.
- Signed URL chỉ sinh sau authorization; stable endpoint trả `200 ApiResponse` cho frontend đã gửi Bearer token, TTL 60 phút, không persist/log/event payload và rate limit 10/phút/user/invoice.
- At-least-once delivery và concurrent requests không được tạo double money movement, invoice, settlement, outbox hoặc notification.
- Không distributed transaction, không cross-database FK, không mock persistence trong Task 38.10.

## Open questions

Không còn ambiguity nghiệp vụ chặn dispatch trong Revision 6. Task 38.0 vẫn phải ghi bằng chứng kiểm tra eligibility của QuestPDF Community theo annual gross revenue của tổ chức; nếu không đủ điều kiện thì bắt buộc dùng fallback `PDFsharp-MigraDoc` MIT đã được duyệt trước, không mở lại thiết kế Invoice.
