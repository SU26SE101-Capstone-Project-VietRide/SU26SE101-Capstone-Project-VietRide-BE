# Day 41 — Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 41 — Operator Excel export backend (Jira: SCV-125)
- **Prior checklist**: `docs/handoff/day-40-checklist.md` — `not found` tại thời điểm lập kế hoạch; không coi platform report Day 40 là dependency đã hoàn tất
- **Plan status**: APPROVED — các quyết định contract được khóa cho mục tiêu Day 41–43; Task 41.0 cập nhật SOT/API contract trước feature implementation

## Objective

Cung cấp sáu báo cáo operator dưới dạng `.xlsx`: booking, parcel, revenue, occupancy, cancellation và refund, luôn giới hạn theo operator trong JWT. Mỗi service chỉ xuất dữ liệu thuộc database mình sở hữu; Gateway chỉ proxy theo route canonical được khóa ở Task 41.0 và không tổng hợp domain data. Dùng một writer ClosedXML dùng chung với response dạng stream-backed file để tránh thêm full byte-array/full row-list buffer, đồng thời giữ tương thích endpoint CSV Parcel hiện hữu. Day 41 hoàn tất khi cả sáu file tải được xuyên Gateway và fixture 10.000 dòng chứng minh không OOM, không lẫn tenant.

## Success criteria (DoD — binary, verifiable)

- [ ] Sáu report type `BOOKING`, `PARCEL`, `REVENUE`, `OCCUPANCY`, `CANCELLATION`, `REFUND` có endpoint canonical đã ghi trong API contract và tải được file XLSX hợp lệ xuyên Gateway.
- [ ] Mỗi query lấy `operatorId` duy nhất từ authenticated claims; test với ít nhất hai operator chứng minh không có row/summary của tenant khác trong file.
- [ ] File thành công có MIME XLSX, `Content-Disposition: attachment`, filename deterministic, workbook mở được bằng ClosedXML/OpenXML và có đúng sheet/header/cell type đã freeze ở Task 41.0; range không có dữ liệu vẫn trả workbook hợp lệ.
- [ ] Booking/Parcel/Payment/Trip chỉ đọc database của chính service; không cross-DB query/FK, không Gateway aggregation và không tạo integration event mới.
- [ ] Export không materialize đồng thời full dataset và full workbook bytes; response dùng seekable temp-file stream, cleanup ở success/error/cancellation/client disconnect.
- [ ] Real-stack fixture 10.000 data rows cho từng report hoàn tất không `OutOfMemoryException`, file không hỏng và process peak memory được ghi vào test artifact.
- [ ] Endpoint CSV Parcel hiện hữu `/v1/operator/parcels/reports/export?format=csv` vẫn pass compatibility tests; không delete/rename hoặc âm thầm đổi content type.
- [ ] Release build, `dotnet format --verify-no-changes`, unit/integration/architecture tests của Booking, Parcel, Payment, Trip và Gateway lint/test/E2E/build đều xanh.

## Contract changes

### Đã xác nhận từ source-of-truth

- Timeline Day 41 yêu cầu ClosedXML, response stream, tenant filter trong mọi query và sáu output report; technical context v7 §4.3/§4.5(d) xác nhận `.xlsx`, các nhóm số liệu doanh thu, vé, parcel, refund, occupancy/cancellation.
- Success response là raw file, không bọc ADR 0004; mọi validation/auth/server error vẫn dùng `ApiResponse` ADR 0004. Đây là read-only GET nên không dùng `Idempotency-Key`.
- `operatorId` không được nhận từ query/body. Existing operator controllers dùng `OPERATOR_ADMIN,OPERATOR_STAFF` và claim scope; role cuối cùng vẫn phải được khóa trong Task 41.0 vì API contract chưa có report endpoint.
- Không có migration/event mới được timeline yêu cầu. Payment dùng `OperatorLedgerEntry` đã có từ Day 38 làm source attribution theo operator/trip; không tạo `payment_attributions` hoặc backfill mới.
- Endpoint CSV Parcel hiện hữu được giữ nguyên để tránh breaking change. XLSX dùng route mới hoặc report type mới theo quyết định OQ-1.

### Chưa có contract canonical — phải khóa trước implementation

- Timeline liệt kê năm route tách (`bookings/parcels/revenue/occupancy/cancellation`) nhưng DoD yêu cầu thêm refund; technical context lại mô tả một route generic `GET /v1/operator/reports/export?reportType=...&format=xlsx`; API contract hiện không có route nào trong hai shape này.
- SOT chưa quy định exact columns/sheets, filename, default/max range, timezone boundary, date anchor từng report, row cap/error code, occupancy formula hoặc revenue/refund inclusion rules.
- Task 41.0 chỉ được chạy sau khi OQ-1 đến OQ-6 có human decision; task này ghi exact request/response/error/column/metric contract vào `VietRide_API_Contract_v1.md` và registry liên quan trước khi worker feature được dispatch.

## Tasks

### Task 41.0 — Chốt contract và architecture baseline cho sáu XLSX

| Field | Value |
|---|---|
| stack/owner | cross-cutting — docs/contract |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md` chỉ report/error/ownership registry Day 41; `docs/handoff/day-41-plan.md` để thay các TBD/OQ bằng quyết định đã duyệt; contract fixtures dưới `docs/api/` nếu repo đang dùng fixture đó |
| forbidden scope | Không viết implementation, migration hoặc generated code; không sửa service/Gateway/package files; không đổi endpoint CSV Parcel hiện hữu; không thêm bảng/event/reporting service; không sửa `.agents/**`, `.codex/**`, `.claude/**`, `.env`, secret hoặc git ops. |
| depends on | Human resolution cho OQ-1..OQ-6. Timeline không ghi dependency cứng vào Day 40; Day 38 ledger/context hiện hữu là source runtime cần verify, không được redesign trong task này. |
| invariant flags | Docs/JSON LF; tiếng Việt đầy đủ dấu trong `docs/`; ADR 0004 cho error nhưng raw XLSX cho success; role + tenant claim explicit; GET không idempotency; Money BIGINT đến đơn vị đồng, không floor 1.000 (BSOT v1.11.0); UTC storage và range timezone explicit; no cross-DB FK/query; no new event; no commercial dependency. |
| acceptance | Contract freeze đủ sáu route/report type, owner service, auth, query defaults/limits, inclusive/exclusive boundary, date anchor, exact workbook/sheet/column/cell type, filename/MIME, empty result, metrics, row-limit behavior và error codes; ghi rõ legacy CSV compatibility; ghi explicit approval + exact CPM version cho ClosedXML; contract không còn TBD mâu thuẫn timeline/technical context và reviewer trả APPROVE PLAN. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 417–423 và standing items dòng 507–510; technical context v7 §4.3 dòng 571–572, §4.5(d) dòng 898–907; API contract hiện không có operator report export; BSOT §1.2 service ownership, §3.2 Clean Architecture, §5.9 error registry, §9.4 timezone, §9.5 money; source hiện hữu `OperatorParcelsController.cs`. |

### Task 41.1 — Shared XLSX writer, stream lifecycle và 10k harness

| Field | Value |
|---|---|
| stack/owner | dotnet — shared reporting infrastructure |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) |
| owned files (write set) | `Directory.Packages.props` chỉ ClosedXML version đã duyệt; reporting-neutral abstractions/types mới dưới `libs/dotnet/VietRide.Shared.Application/Reporting/`; project mới `libs/dotnet/VietRide.Shared.Reporting/**` chứa ClosedXML implementation; `libs/dotnet/VietRide.Libs.sln`; shared writer unit/integration/performance tests và test artifacts config liên quan |
| forbidden scope | Không sửa domain service, Gateway, business query, DB/schema/migration, API contract hoặc CSV Parcel; không đưa ClosedXML reference vào Domain/Application service projects; không `MemoryStream`/`byte[]` cho cả workbook; không package `Version=` trong `.csproj`; không thêm library khác ngoài ClosedXML đã được duyệt; không `.env`, secrets hoặc git ops. |
| depends on | 41.0 |
| invariant flags | `.cs/.csproj/.sln/.props` CRLF; CPM không `Version=`; MediatR v11 không đổi; Application chỉ biết workbook/row/cell abstraction trung lập; ClosedXML nằm trong shared infrastructure project; async/cancellation propagation; temp path không lộ trong response/log; no commercial dependency. |
| acceptance | `IExcelReportWriter` nhận workbook spec + async row source, tạo typed string/integer/decimal/date/datetime cells theo contract, freeze/filter/style tối thiểu đã khóa và save vào seekable temp `FileStream` với delete-on-close; caller nhận stream ở position 0 và ASP.NET disposal xóa file. Tests cover valid/empty workbook, Unicode tiếng Việt, exception giữa lúc write, cancellation và disposal cleanup. Harness 10.000 rows mở lại workbook, assert row count/cell types, không tạo full output `byte[]`, không giữ duplicate full row list và ghi peak memory; shared solution build/format/test xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 418, 420, 423; technical context v7 §4.5(d) dòng 900–907; `AGENTS_DOTNET.md` Clean Architecture/layer rules, CPM/no paid deps, async cancellation; `Directory.Packages.props`; ClosedXML explicit approval/version từ Task 41.0. |

### Task 41.2 — Booking và cancellation XLSX

| Field | Value |
|---|---|
| stack/owner | dotnet — Booking |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | report queries/projections/validators mới dưới `apps/booking/src/VietRide.Booking.Application/Features/Reports/**`; narrowly scoped reporting read abstraction/implementation dưới Booking Application/Infrastructure nếu existing `IBookingRepository` không hỗ trợ async projection; Booking report controller dưới `apps/booking/src/VietRide.Booking.Api/Controllers/`; Booking DI/project references chỉ cho Shared.Reporting; Booking unit/integration/architecture tests |
| forbidden scope | Không sửa booking create/payment/refund lifecycle, BookingStats consumers/schema/migration, Payment/Parcel/Trip/Identity/Gateway, shared writer internals, API contract, events hoặc CSV Parcel; không query foreign DB/internal service để enrich; không xuất column/PII ngoài contract; không `.env`, secrets hoặc git ops. |
| depends on | 41.1 |
| invariant flags | Thin controller → MediatR; role và `operatorId` từ JWT theo 41.0; every predicate includes tenant scope; `AsNoTracking` + server-side projection + async row enumeration; raw XLSX success/ADR 0004 error; UTC/date boundary theo contract; Money `long`; cancellation token đến EF/writer/response; .NET CRLF. |
| acceptance | Hai report BOOKING và CANCELLATION trả workbook đúng exact contract, deterministic order/tie-breaker, empty range hợp lệ và tenant A không chứa booking tenant B. Date/status/revenue/refund semantics đúng Task 41.0; cap/range+1 behavior và coded validation đúng contract. Tests cover happy, empty, invalid range, unauthorized/role, missing operator claim, boundary timestamps, multiple statuses, 10k projection và writer failure/cancellation cleanup; Booking build/format/unit/integration/architecture xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 419, 421–423; technical context v7 §4.3 booking monitor và §4.5(d) vé bán/refund/tỷ lệ hủy; `db-schema/booking/schema.sql` `bookings`, `tickets`, `booking_stats`; BSOT §4.2 Booking inventory, §8.1 booking lifecycle; source `Booking.cs`, `BookingStats.cs`, `OperatorBookingsController.cs`. |

### Task 41.3 — Parcel XLSX và bảo toàn CSV legacy

| Field | Value |
|---|---|
| stack/owner | dotnet — Parcel |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | `apps/parcel/src/VietRide.Parcel.Application/Features/Parcels/Reports/**`; report read methods trong `IParcelRepository`/`ParcelRepository` nếu cần; `OperatorParcelsController.cs` hoặc controller report canonical mới theo Task 41.0; Parcel DI/project references chỉ cho Shared.Reporting; Parcel report unit/integration/compatibility tests |
| forbidden scope | Không đổi behavior/content type/filename của CSV endpoint hiện hữu ngoài annotation deprecated nếu Task 41.0 yêu cầu; không sửa parcel lifecycle/payment/capacity/stats consumer/schema/migration, service khác, Gateway, shared writer internals hoặc API contract; không dùng `ParcelsFallback` của summary hiện hữu làm canonical revenue/refund nếu metric contract chọn Payment ledger; không cross-DB enrich; không `.env`, secrets hoặc git ops. |
| depends on | 41.1 |
| invariant flags | Thin controller → MediatR; OPERATOR claim scope; `AsNoTracking` projection; tenant predicate trước range/status; raw XLSX success; legacy CSV byte-for-byte/semantic compatibility test; Money `long`; no PII ngoài frozen columns; cancellation cleanup; .NET CRLF. |
| acceptance | PARCEL workbook đúng exact columns/metrics/date anchor của Task 41.0, deterministic order, valid empty result và không lẫn tenant; existing CSV route vẫn trả `text/csv` với contract cũ. Tests cover parcel statuses, initial/additional amounts theo contract, boundary dates, cap/range error, auth/tenant, 10k rows và client cancellation; Parcel build/format/unit/integration/architecture xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 419, 421–423; technical context v7 §4.5(d) parcel count/revenue; `db-schema/parcel/schema.sql` `parcels`/`parcel_stats`; BSOT §4.2 Parcel inventory; source `OperatorParcelsController.cs`, `ExportParcelReportQueryHandler.cs`, `ParcelReportQuerySupport.cs`, `ParcelStats.cs`. |

### Task 41.4 — Revenue và refund XLSX từ Payment operator ledger

| Field | Value |
|---|---|
| stack/owner | dotnet — Payment |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | report queries/projections/validators mới dưới `apps/payment/src/VietRide.Payment.Application/Features/Reports/**`; narrowly scoped read methods trong `IOperatorLedgerEntryRepository`/implementation; Payment operator report controller; Payment DI/project references chỉ cho Shared.Reporting; Payment unit/integration/architecture tests |
| forbidden scope | Không thêm `payment_attributions`, backfill, schema/migration hoặc event; không sửa Payment charge/VNPay/refund/wallet/settlement/invoice lifecycle; không đọc Booking/Parcel/Trip DB hoặc gọi service khác để enrich; không dùng mutable Payment status làm refund ledger thay thế; không sửa Gateway/shared writer/API contract; không `.env`, secrets hoặc git ops. |
| depends on | 41.1 và Day 38 ledger/context implementation hiện hữu phải pass focused readiness tests |
| invariant flags | `OperatorLedgerEntry.OperatorId` là tenant key; signed `long` amount giữ đến đồng; revenue/refund inclusion và date anchor đúng 41.0; immutable ledger, read-only `AsNoTracking`; deterministic order; raw XLSX success/ADR 0004 error; cancellation propagation; .NET CRLF. |
| acceptance | REVENUE và REFUND workbook chỉ lấy ledger entry types đã freeze trong Task 41.0, giữ đúng dấu/display amount contract, support booking group/multi-allocation, parcel additional revenue, partial/multiple refund mà không duplicate. Tests chứng minh operator A/B isolation, trip/reference grouping, voucher-funded cases, exact boundary, empty/cap/range behavior, 10k rows và no cross-service call; Payment build/format/unit/integration/architecture xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 419, 422–423; technical context v7 §4.5(d) revenue/refund và §4.6 operator ledger; BSOT §4.2 Payment inventory, §7.3 payment events, §8.4 Payment, Day 38 ledger invariants; `db-schema/payment-wallet/schema.sql` `operator_ledger_entries`; source `OperatorLedgerEntry.cs`, `OperatorLedgerEntryType.cs`, `OperatorLedgerEntryRepository.cs`. |

### Task 41.5 — Occupancy XLSX từ Trip/TripSeat

| Field | Value |
|---|---|
| stack/owner | dotnet — Trip |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files (write set) | report query/projection/validator mới dưới `apps/trip/src/VietRide.Trip.Application/Features/Reports/**`; narrowly scoped Trip/TripSeat reporting read abstraction/implementation; Trip operator report controller; Trip DI/project references chỉ cho Shared.Reporting; Trip unit/integration/architecture tests |
| forbidden scope | Không sửa Trip lifecycle, seat lock/disable/enable semantics, generation/jobs, schema/migration, Booking/Payment/Parcel/Identity/Gateway, shared writer hoặc API contract; không suy occupancy từ Booking DB/BookingStats bằng cross-service call; không `.env`, secrets hoặc git ops. |
| depends on | 41.1 |
| invariant flags | Tenant predicate on `Trip.OperatorId`; occupancy numerator/denominator/status/range đúng 41.0; `TripSeat.UNAVAILABLE` không được tự diễn giải khi chưa freeze formula; server-side aggregate hoặc bounded projection, `AsNoTracking`; deterministic order; decimal display/rounding theo contract, không float persistence; cancellation propagation; .NET CRLF. |
| acceptance | OCCUPANCY workbook trả per-trip/route/period shape đúng Task 41.0; tests cover zero sellable seats, AVAILABLE/HELD/BOOKED/UNAVAILABLE seats, scheduled/completed/cancelled/disrupted trips theo inclusion rule, range boundary, tenant isolation, empty/cap behavior và 10k rows; không gọi Booking service/DB; Trip build/format/unit/integration/architecture xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 419, 421–423; technical context v7 §4.3 dashboard/seat-disable và §4.5(d) occupancy; `db-schema/trip-route-vehicle/schema.sql` `trips`, `trip_seats`; BSOT §4.2 Trip inventory, §8.2 Trip lifecycle; source `Trip.cs`, `TripSeat.cs`, `TripSeatRepository.cs`. |

### Task 41.6 — Gateway routing và real-stack XLSX acceptance

| Field | Value |
|---|---|
| stack/owner | nest — Gateway + cross-service E2E |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files (write set) | `apps/gateway/src/config/routes.ts`, `routes.spec.ts` và Gateway E2E tests; Day 41 folder trong cumulative `docs/api/postman/vietride.postman_collection.json`; `scripts/run-day41-excel-export-e2e.mjs`; `package.json` chỉ script `e2e:day41`; isolated Day 41 compose/Dockerfile fixtures dưới `infra/docker/` nếu test harness cần |
| forbidden scope | Không sửa downstream business/report implementation, DB schema/migration, shared writer, API contract, unrelated Gateway auth/routes hoặc Postman folders khác; không buffer/transform XLSX body trong Gateway; không tạo wildcard route rộng che prefix hiện hữu; không thêm TS dependency; không `.env`, secrets, generated output hoặc git ops. |
| depends on | 41.2, 41.3, 41.4, 41.5 |
| invariant flags | TS/JSON/YAML/MD LF; exact/longest-prefix route ownership theo 41.0; user JWT validation + downstream Internal JWT existing pattern; success binary passthrough giữ MIME/Content-Disposition/content length/chunking; error ADR 0004 passthrough; no response logging/body buffering; cancellation/client abort propagated; tenant test dùng token thật của hai operator. |
| acceptance | Route matrix gửi từng canonical XLSX path đến đúng Booking/Parcel/Payment/Trip owner và không làm đổi route CSV Parcel; auth/role/missing claim/validation errors đúng contract. `npm run e2e:day41` dựng isolated real stack/seed hai operator, tải và mở đủ sáu workbook, assert sheets/headers/cell types/range/tenant, seed 10.000 rows cho từng report, ghi file size/duration/peak service memory, không OOM và cleanup temp files khi success/error/client abort. Postman cumulative folder có happy/auth/validation cho sáu report; Gateway lint/test/E2E/build và toàn bộ downstream Release verification xanh. |
| source citations | `BE_TIMELINE_VU.md` Day 41 dòng 419–423 và standing item dòng 509; `AGENTS_NESTJS.md` Gateway config-driven proxy, Internal JWT, script verify/E2E; BSOT §1.2 Gateway/service ownership, §3.3 Gateway route table; source `apps/gateway/src/config/routes.ts` và `routes.spec.ts`. |

## Dispatch order

1. Resolve OQ-1..OQ-6 với human, sau đó Task 41.0 freeze contract/metrics/dependency version và qua PLAN-REVIEW.
2. Task 41.1 tạo shared XLSX writer + lifecycle/performance harness.
3. Tasks 41.2, 41.3, 41.4 và 41.5 parallel-safe sau 41.1 vì write set thuộc bốn service khác nhau; trong một worktree vẫn ưu tiên merge tuần tự để tránh project-reference/solution drift.
4. Task 41.6 chạy cuối sau khi cả sáu downstream export tồn tại.

```text
human OQ → 41.0 → 41.1 ─┬→ 41.2 ─┐
                         ├→ 41.3 ─┤
                         ├→ 41.4 ─┼→ 41.6
                         └→ 41.5 ─┘
```

## Progress tracker

> Orchestrator bookkeeping — main thread cập nhật sau mỗi `/implement-task`. Bảng này chỉ mang tính thông tin; `/audit-day` phải xác minh độc lập theo SOT và verification gate.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 41.0 | ✅ done | APPROVED (full audit) | 2026-07-20 | Contract, owner, range, workbook và error registry đã khóa trong API contract/BSOT. |
| 41.1 | ✅ done | APPROVED (full audit) | 2026-07-20 | Shared writer, typed cells, temp-file cleanup và harness 10.000 dòng đã pass. |
| 41.2 | ✅ done | APPROVED (full audit) | 2026-07-20 | Booking/cancellation XLSX, tenant/range và cancellation propagation đã pass. |
| 41.3 | ✅ done | APPROVED (full audit) | 2026-07-20 | Parcel XLSX và compatibility test exact CSV legacy đã pass. |
| 41.4 | ✅ done | APPROVED (full audit) | 2026-07-20 | Revenue/refund XLSX từ immutable operator ledger đã pass. |
| 41.5 | ✅ done | APPROVED (full audit) | 2026-07-20 | Occupancy XLSX từ Trip/TripSeat đã pass. |
| 41.6 | ✅ done | APPROVED (full audit) | 2026-07-20 | Gateway/Postman và isolated real-stack E2E sáu XLSX + tenant + abort cleanup đã pass. |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Quyết định contract đã khóa

- Dùng sáu route tách: `GET /v1/operator/reports/bookings/export`, `/parcels/export`, `/revenue/export`, `/occupancy/export`, `/cancellation/export`, `/refunds/export`; Gateway proxy từng route đến service sở hữu dữ liệu. Không dùng facade generic.
- Cả `OPERATOR_ADMIN` và `OPERATOR_STAFF` được truy cập; `operatorId` chỉ lấy từ JWT, không nhận từ query/body và mọi query có predicate tenant.
- `from`/`to` là ngày ICT inclusive, mặc định 30 ngày inclusive, tối đa 92 ngày; chuyển thành UTC `[from,to)` và lỗi là `422 REPORT_RANGE_INVALID`.
- Workbook dùng sheet tương ứng với report, header ổn định bằng tiếng Anh ASCII, không có PII hành khách/người gửi/người nhận. Các cột định danh chỉ là UUID/code nghiệp vụ, metric BIGINT và timestamp/date typed cell.
- Booking lấy `created_at`; cancellation lấy `cancelled_at`; parcel lấy `created_at`; revenue/refund lấy `OperatorLedgerEntry` immutable theo `created_at` và các loại `BOOKING_REVENUE`, `PARCEL_REVENUE`, `BOOKING_REFUND`, `PARCEL_REFUND`, `ADJUSTMENT` theo contract. `BOOKING_GROUP` dùng allocation/context hiện hữu, không tạo attribution table.
- ClosedXML `0.105.0` là dependency đã được chấp thuận. Writer dùng temp `FileStream` delete-on-close, không tạo full output `byte[]` hoặc full row list. Row cap chỉ được thêm sau benchmark và phải có error code contract; mốc bắt buộc là 10.000 dòng/report không OOM.
- Success trả raw XLSX với MIME và `Content-Disposition`; validation/auth/server errors dùng ADR 0004. Endpoint CSV Parcel hiện hữu giữ nguyên behavior.

## Open questions đã đóng

Các OQ-1..OQ-6 cũ bên dưới được giữ làm lịch sử của bản draft; quyết định ở mục trên là SOT hiện hành và không còn là blocker.

1. **OQ-1 — Route shape/owner**: dùng sáu route tách `GET /v1/operator/reports/{bookings|parcels|revenue|occupancy|cancellation|refunds}/export` và Gateway route từng prefix đến service owner (khuyến nghị, giữ no cross-DB), hay route generic technical-context `GET /v1/operator/reports/export?reportType=...&format=xlsx` với một facade? Nếu chọn facade, service owner và internal contracts phải được chỉ định rõ.
2. **OQ-2 — Authorization**: cho cả `OPERATOR_ADMIN|OPERATOR_STAFF` như operator report/read endpoint hiện hữu (khuyến nghị), hay chỉ `OPERATOR_ADMIN`?
3. **OQ-3 — Range**: `from/to` là ngày ICT inclusive hay UTC; default bao nhiêu ngày; giới hạn tối đa bao nhiêu ngày; vượt giới hạn dùng error code/status nào? Đề xuất cần human duyệt: ICT, mặc định 30 ngày inclusive, tối đa 92 ngày, `422 REPORT_RANGE_INVALID`.
4. **OQ-4 — Workbook wire contract**: exact sheet names, filenames, ngôn ngữ header và exact columns/cell types của từng report chưa được SOT mô tả. Cần bảng sáu report được business/FE duyệt; đặc biệt xác nhận có/không PII người gửi/nhận/hành khách.
5. **OQ-5 — Metric semantics**: cần khóa date anchor và inclusion: booking theo `confirmed_at` hay `created_at`; cancellation theo `cancelled_at`; parcel theo `created_at` hay lifecycle event date; revenue/refund dùng những `OperatorLedgerEntryType` nào; occupancy numerator/denominator/status, xử lý `HELD`/`UNAVAILABLE`/cancelled trip và cách làm tròn.
6. **OQ-6 — Dependency/size guard**: xác nhận explicit approval cho NuGet ClosedXML và version pin; có row cap ngoài max date range hay không, cap bao nhiêu và coded error nào. Timeline chỉ yêu cầu 10.000 rows không OOM, chưa cho phép manager tự đặt 100.000 rows hoặc package version.
