# Day 38 — Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 38 — Invoice PDF + PlatformWallet + Settlement
- **Prior checklist**: `docs/handoff/day-37-checklist.md` (`not found`)
- **Plan status**: DRAFT → (reviewer) APPROVED / REVISION-REQUIRED

## Objective

Hoàn thiện luồng hóa đơn subscription của Payment & Wallet và mô hình giữ tiền, ví nội bộ operator, settlement theo từng Trip. Day 38 phải bảo đảm tiền chỉ chuyển từ PlatformWallet sang OperatorWallet một lần, sau cửa sổ hold bảy ngày hoặc do System Admin chủ động settle. Hóa đơn chỉ được phát hành sau subscription payment thành công và Notification chỉ nhận event sau khi PDF đã có signed URL. Kết quả mở đường cho Day 40 report nhưng không triển khai bank withdrawal hoặc e-invoice provider v2.

## Success criteria (DoD — binary, verifiable)

- [ ] Subscription payment `SUCCEEDED` tạo đúng một Invoice PDF đã phát hành, lưu signed URL, và phát event `payment.invoice.issued`; Notification có thể gửi thông báo cho OPERATOR_ADMIN.
- [ ] Một Trip có revenue dương tạo tối đa một `OperatorTripSettlement`; marker chuyển `PENDING_HOLD → ELIGIBLE` sau bảy ngày.
- [ ] Hangfire Monday 09:00 settle toàn bộ marker `ELIGIBLE` bằng một transaction: PlatformWallet DEBIT, OperatorWallet CREDIT, hai ledger immutable và marker `SETTLED` nhất quán.
- [ ] Manual admin settle xử lý đúng `PENDING_HOLD`/`ELIGIBLE`, có `Idempotency-Key`, không thể double-settle; mọi trường hợp net amount không dương bị `CANCELLED` và không credit wallet.
- [ ] `dotnet build apps/payment/VietRide.Payment.sln -c Release`, `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes`, và `dotnet test apps/payment/VietRide.Payment.sln -c Release` đều xanh; migration up/down được kiểm tra trên DB trống.

## Contract changes

- Bổ sung vào `VietRide_API_Contract_v1.md` các response envelope/phân trang và auth cho `GET /v1/operator/invoices`, `GET /v1/operator/invoices/{id}/download`, `GET /v1/operator/wallet`, `GET /v1/operator/wallet/transactions`, `GET /v1/operator/trip-settlements`, `GET /v1/operator/ledger`, `GET /v1/admin/trip-settlements`, `GET /v1/admin/platform-wallet`, và `POST /v1/admin/trip-settlements/{id}/settle`; mutation cuối yêu cầu `Idempotency-Key`.
- Event keys đã được registry: `payment.invoice.issued` → Notification và `payment.trip_settlement.completed` → Notification. Tạo/consume đầy đủ qua Payment outbox; không thêm routing key không có registry.
- EF migration + `db-schema/payment-wallet/schema.sql` đồng bộ Invoice, PlatformWalletTransaction, OperatorWallet, OperatorWalletTransaction, OperatorLedgerEntry, OperatorTripSettlement và enum/index/constraint tương ứng. Gateway route table hiện đã có admin settlement/platform wallet; route prefix cho operator invoices/wallet cần xác nhận ở Task 38.0.
- **Nguồn**: BSOT §5.6, §5.9, §7.3; technical_context_v7 §4.5(e), §4.6; `db-schema/payment-wallet/schema.sql`; `apps/gateway/src/config/routes.ts`.

## Tasks

### Task 38.0 — Chốt contract còn thiếu và dựng persistence baseline Payment
| Field | Value |
|---|---|
| stack/owner | dotnet + gateway/documentation |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer + nest-reviewer (route table nếu thay đổi) |
| skill | ef-migration |
| owned files (write set) | `VietRide_API_Contract_v1.md`; `apps/gateway/src/config/routes.ts` (chỉ khi prefix operator chưa được route); `apps/payment/src/VietRide.Payment.Domain/Entities/Invoice.cs`; `PlatformWallet.cs`; `PlatformWalletTransaction.cs`; `OperatorWallet.cs`; `OperatorWalletTransaction.cs`; `OperatorLedgerEntry.cs`; `OperatorTripSettlement.cs`; các enum mới trong `apps/payment/src/VietRide.Payment.Domain/Enums/`; repository interfaces trong `apps/payment/src/VietRide.Payment.Application/Abstractions/Repositories/`; `apps/payment/src/VietRide.Payment.Infrastructure/PaymentDbContext.cs`; configurations/repositories tương ứng dưới `apps/payment/src/VietRide.Payment.Infrastructure/Persistence/`; migration mới và `PaymentDbContextModelSnapshot.cs` dưới `apps/payment/src/VietRide.Payment.Infrastructure/Migrations/`; `db-schema/payment-wallet/schema.sql`; persistence unit/integration tests tương ứng dưới `apps/payment/tests/` |
| forbidden scope | Không sửa `.env`, secrets, package versions, `.agents/**`, `.codex/**`, Identity/Trip/Booking/Parcel source, existing migration đã apply, hay Git operations; không thêm cross-DB FK, payout/withdrawal/bank-account flow, e-invoice provider v2. |
| depends on | Không có; là baseline bắt buộc trước 38.1–38.3. |
| invariant flags | `.cs` CRLF, docs/TS LF; MediatR v11; CPM không `Version=`; BIGINT VND đến đơn vị đồng; mọi logical relation Operator/Subscription/Trip không FK DB; `row_version` cho ba wallet/settlement aggregate; ledger immutable, amount dương khi type quyết định chiều; migration Up/Down reversible. |
| acceptance | Contract ghi rõ role, envelope, page/query và errors đã registry; DDL/migration cùng shape, gồm singleton PlatformWallet, natural PK `operator_id`, UNIQUE `(operator_id, trip_id)`, status/consistency checks và index settlement `(status, eligible_at)`; test tạo schema sạch + migration rollback; build/format/test xanh. |
| source citations | technical_context_v7 §4.5(e), §4.6; BSOT §4.4, §5.6, §5.9, §7.3; `db-schema/payment-wallet/schema.sql` and README. |

### Task 38.1 — Ingest revenue, tạo settlement marker và auto-settle tuần
| Field | Value |
|---|---|
| stack/owner | dotnet / Payment & Wallet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files (write set) | Payment event contracts/handlers dưới `apps/payment/src/VietRide.Payment.Application/Events/` và `apps/payment/src/VietRide.Payment.Infrastructure/Messaging/`; Payment/Wallet/settlement commands, queries, handlers, validators và DTOs dưới `apps/payment/src/VietRide.Payment.Application/Features/`; `IPlatformWalletRepository.cs`, `IOperatorWalletRepository.cs`, `IOperatorTripSettlementRepository.cs`, `IOperatorLedgerEntryRepository.cs`; repository implementations dưới `apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories/`; `apps/payment/src/VietRide.Payment.Infrastructure/Jobs/TripSettlementEligibilityJob.cs`; `TripSettlementWeeklySettleJob.cs`; `apps/payment/src/VietRide.Payment.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/payment/src/VietRide.Payment.Api/Program.cs`; unit/integration tests cho handlers/jobs/messaging dưới `apps/payment/tests/` |
| forbidden scope | Không sửa schema/migration từ 38.0 trừ lỗi review được phê duyệt; không sửa Trip/Booking/Parcel producer; không gọi trực tiếp DB service khác; không settle bank, không điều chỉnh wallet sau `SETTLED` tự động, không thêm gateway/API public ở task này. |
| depends on | 38.0 |
| invariant flags | Consume RabbitMQ at-least-once idempotently; `trip.trip.completed`/`trip.trip.disrupted` tạo marker bằng UPSERT `(operator_id, trip_id)` chỉ khi SUM ledger > 0; booking/parcel payment hold làm CREDIT PlatformWallet + audit ledger trong cùng local transaction; daily 02:00 chỉ `PENDING_HOLD → ELIGIBLE`; Monday 09:00 lock trạng thái/row version và commit atomic PlatformWallet DEBIT + OperatorWallet CREDIT + cả hai transaction ledger + settlement; net amount recompute tại settle; balance không âm; emit `payment.trip_settlement.completed` qua outbox sau commit. |
| acceptance | Redelivery của identity approval/payment/trip terminal không tạo duplicate wallet/ledger/marker; late refund trước settle làm giảm net amount; net ≤ 0 chuyển `CANCELLED` không có wallet transaction; thiếu PlatformWallet balance rollback toàn bộ và để settlement chưa `SETTLED`; race manual/weekly có một winner; Hangfire registrations chạy đúng cron và tests xác nhận. |
| source citations | technical_context_v7 §4.6 (flow 1–7, net formula, status lock); BSOT §7.3, §7.4, §4.5 Hangfire; `db-schema/payment-wallet/README.md` “Trip settlement state machine”. |

### Task 38.2 — Phát hành Invoice PDF subscription và thông báo operator
| Field | Value |
|---|---|
| stack/owner | dotnet / Payment & Wallet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files (write set) | Invoice event contracts/consumer dưới `apps/payment/src/VietRide.Payment.Application/Events/`; commands/queries/handlers/validators/DTOs dưới `apps/payment/src/VietRide.Payment.Application/Features/Invoices/`; `IInvoiceRepository.cs`, `IInvoicePdfGenerator.cs`, `IInvoiceStorage.cs` dưới Application abstractions; implementations dưới `apps/payment/src/VietRide.Payment.Infrastructure/Invoices/` và persistence repositories; `apps/payment/src/VietRide.Payment.Infrastructure/Jobs/InvoicePdfRetryJob.cs`; DI registration trong `InfrastructureServiceCollectionExtensions.cs`; Invoice controller/request DTOs dưới `apps/payment/src/VietRide.Payment.Api/Controllers/`; `Program.cs`; `appsettings.json`/`appsettings.Development.json` chỉ cho tên config không-secret nếu cần; unit/integration tests dưới `apps/payment/tests/` |
| forbidden scope | Không sửa Identity subscription state machine hay Payment provider callback flow ngoài consumer cần thiết; không log PDF bytes, signed URL nhạy cảm, token hoặc credential Firebase; không tích hợp VNPT/Misa/Viettel e-invoice, không tạo bank withdrawal; không thêm NuGet/Firebase credential khi chưa chốt Open question. |
| depends on | 38.0; bắt đầu sau khi Open question về PDF/storage được chốt. |
| invariant flags | Chỉ xử lý `payment.subscription.payment_succeeded`/subscription Payment `SUCCEEDED` idempotently; unique Invoice theo payment; số hóa đơn `VR-INV-yyyyMM-XXXXXX` unique và concurrency-safe; DRAFT → ISSUED chỉ sau PDF upload thành công; PDF bao gồm kỳ, plan, amount, VAT note, publisher/buyer; download chỉ owner operator; emit `payment.invoice.issued { invoiceId, operatorId, amount, pdfUrl }` bằng outbox sau ISSUED; signed URL không được persist/return lâu hơn policy storage đã chốt. |
| acceptance | Duplicate event/retry tạo tối đa một Invoice issued; PDF failure giữ trạng thái có thể retry, không phát notification sai; list chỉ trả invoice tenant của caller, download invoice khác tenant không lộ tồn tại; PDF contains required fields; event consumer/notification contract test xanh. |
| source citations | technical_context_v7 §4.5(e) lines Invoice/Flow; BSOT §7.3 `payment.subscription.payment_succeeded`, `payment.invoice.issued`, §7.4; Day 38 timeline. |

### Task 38.3 — API đọc ví/settlement và manual admin settlement
| Field | Value |
|---|---|
| stack/owner | dotnet + gateway nếu 38.0 phát hiện thiếu route |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer + nest-reviewer (route table nếu thay đổi) |
| skill | add-endpoint |
| owned files (write set) | `apps/payment/src/VietRide.Payment.Application/Features/OperatorWallets/`; `OperatorTripSettlements/`; `OperatorLedger/`; `PlatformWallets/`; `apps/payment/src/VietRide.Payment.Api/Controllers/OperatorWalletController.cs`; `OperatorTripSettlementsController.cs`; `AdminTripSettlementsController.cs`; `AdminPlatformWalletController.cs`; request DTOs dưới `Controllers/Requests/`; repository query methods dưới Application/Infrastructure persistence; integration/unit endpoint tests dưới `apps/payment/tests/`; `apps/gateway/src/config/routes.ts` chỉ nếu Task 38.0 chứng minh thiếu prefix. |
| forbidden scope | Không thêm public write endpoint cho operator; không cho OPERATOR_ADMIN tự bypass hold; không đổi API contract đã chốt ở 38.0; không sửa invoice/PDF logic hay scheduler; không expose cross-tenant data/ledger notes có dữ liệu nhạy cảm. |
| depends on | 38.0, 38.1; invoice read endpoints của 38.2 có thể chạy song song sau 38.0 khi contract/storage đã chốt. |
| invariant flags | `[Authorize]` role/tier đúng; caller operatorId lấy từ trusted JWT claim và luôn query tenant-scoped; list dùng `QueryOptions` pageSize ≤100; response dùng ADR 0004 ApiResponse; manual settle requires Idempotency-Key 24h and `SYSTEM_ADMIN`; only `PENDING_HOLD`/`ELIGIBLE` can settle, guard status + row version; errors are `TRIP_SETTLEMENT_NOT_FOUND`, `TRIP_SETTLEMENT_ALREADY_SETTLED`, `PLATFORM_WALLET_INSUFFICIENT_BALANCE`. |
| acceptance | Operator không đọc được wallet/ledger/settlement/invoice của operator khác; admin settle early có `ADMIN_MANUAL`, admin user id và audit metadata; replay same idempotency key trả original result; concurrent/repeated requests không double debit/credit; auth, validation, tenant isolation, pagination and ApiResponse integration tests xanh. |
| source citations | technical_context_v7 §4.5(e), §4.6 operator/system-admin endpoints; BSOT §5.6, §5.9, §7.2, §7.3; `apps/gateway/src/config/routes.ts`. |

## Dispatch order

1. Task 38.0 → contract/persistence baseline. Nó khóa schema và response shape trước implementation; không có task nào khác được dispatch trước.
2. Task 38.1 → revenue ingestion, marker và Hangfire settlement, sau 38.0.
3. Task 38.2 → Invoice PDF/storage/event, sau 38.0 và sau khi human trả lời Open question provider/license. Có thể song song với 38.1 vì write set độc lập sau baseline, trừ `Program.cs`/DI phải merge tuần tự.
4. Task 38.3 → public/admin reads và manual settle sau 38.1; phần invoice read có thể ghép vào 38.2 hoặc chỉ dispatch sau 38.2 để tránh controller overlap.

## Progress tracker

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 38.0 | todo | — | — | — |
| 38.1 | todo | — | — | — |
| 38.2 | todo | — | — | Chờ quyết định PDF/Storage. |
| 38.3 | todo | — | — | — |

Legend: `todo` · `in progress` · `done` (reviewer APPROVED + human `/verify`) · `done-with-carryover` · `blocked`

## Open questions

1. `VietRide_API_Contract_v1.md` hiện chưa định nghĩa request/response chi tiết, pagination fields, lỗi authorization, hoặc Gateway prefix cho các endpoint Invoice/OperatorWallet/Settlement đã có trong technical context và BSOT. Cần chốt contract trước Task 38.0; không suy đoán DTO công khai.
2. SOT yêu cầu QuestPDF (hoặc tương đương) và Firebase Storage signed URL, nhưng repo chưa xác nhận package/license của PDF library, Firebase client/package, bucket, credential delivery hoặc TTL signed URL. `AGENTS_DOTNET.md` cấm thêm NuGet dependency khi chưa có phê duyệt; cần quyết định provider/library và cấu hình vận hành trước Task 38.2.
3. technical_context_v7 ghi `payment.subscription.payment_succeeded` cho Invoice flow nhưng Day 37 timeline nói endpoint subscription pay Wallet/VNPay và contract hiện không mô tả payload đủ để Invoice hydrate operator/plan/period. Cần xác nhận canonical event payload hoặc internal lookup contract cho `operatorId`, `operatorSubscriptionId`, plan name và period trước Task 38.2.
