# Runbook chuẩn hóa doanh thu theo Payment

## 1. Mục đích và phạm vi

Runbook này dùng để triển khai contract doanh thu thống nhất cho Payment, Booking và Parcel.
Payment là nguồn sự thật tài chính; Booking/Trip/Parcel chỉ sở hữu số liệu vận hành. Tài liệu này
không cho phép tự động chạy migration, backfill hoặc deploy trên production.

Không nằm trong phạm vi: frontend implementation, role/permission, Gateway route, commission,
platform fee, subscription refund/proration, wallet/settlement redesign và báo cáo kế toán pháp lý.

> Revenue là KPI quản trị. VietRide-funded voucher credit là quyền lợi doanh thu của nhà xe, không
> phải tiền mặt hành khách đã trả. `paidToOperatorsVnd` là settlement cash-flow độc lập, không được
> cộng vào revenue.

## 2. Invariant phải giữ

```text
netTicketRevenueVnd    = canonical BOOKING ledger entries
netParcelRevenueVnd    = canonical PARCEL ledger entries
netTransportRevenueVnd = netTicketRevenueVnd + netParcelRevenueVnd
subscriptionRevenueVnd = SUBSCRIPTION payment SUCCEEDED
totalProjectRevenueVnd = netTransportRevenueVnd + subscriptionRevenueVnd
```

Input public là ngày theo lịch `Asia/Ho_Chi_Minh`, nhưng mọi DB filter và internal range phải là
UTC half-open `[fromUtc,toUtcExclusive)`. Ví dụ `2026-07-01..2026-07-31` được chuẩn hóa thành
`[2026-06-30T17:00:00Z, 2026-07-31T17:00:00Z)`. Không query persistence bằng timestamp Asia/Ho_Chi_Minh.

Canonical ledger chỉ nhận:

- Booking revenue/refund/VietRide-funded credit có reference `BOOKING`;
- Parcel revenue/refund/VietRide-funded credit có reference `PARCEL`;
- typed `VIETRIDE_FUNDED_VOUCHER_REVERSAL` đúng reference.

Manual, generic entitlement, legacy unclassified, operator-funded audit và mọi reference khác bị
loại. `note` chỉ để đọc audit, không phải predicate tài chính.

## 3. Chuẩn bị trước deploy

1. Xác nhận deploy window đồng bộ Payment → Booking → Parcel → FE và bật maintenance cho màn hình
   reporting. Nếu không thể deploy đồng bộ, dừng và thiết kế API v2; không triển khai contract
   breaking nửa chừng.
2. Chụp backup theo quy trình DB hiện hành và xác nhận có người chịu trách nhiệm rollback.
3. Xác nhận không còn Payment instance/worker cũ sau bước deploy typed writer.
4. Ghi lại số lượng và tổng tiền ở audit query trước mỗi bước; không chỉ nhìn exit code migration.
5. Không đưa Internal JWT, connection string hoặc payload tài chính vào ticket/log.

## 4. Migration ba bước

### A — Expand

Target migration:
`20260807085712_AddOperatorLedgerAdjustmentReason`.

Migration tạo enum và cột nullable `adjustment_reason`, chưa bật CHECK. Sau khi apply A:

1. Deploy Payment writer mới.
2. Drain toàn bộ instance/worker cũ.
3. Quan sát count `ADJUSTMENT` null; count có thể còn từ lịch sử nhưng không được tiếp tục tăng sau
   khi writer mới đã ổn định.

```sql
SELECT count(*) AS adjustment_without_reason
FROM vietride_payment.operator_ledger_entries
WHERE entry_type = 'ADJUSTMENT' AND adjustment_reason IS NULL;
```

Nếu count tăng sau khi drain, dừng rollout vì vẫn còn writer cũ hoặc writer mới sai.

### B — Classify

Target migration:
`20260807085846_ClassifyOperatorLedgerAdjustments`.

Đây là lần duy nhất migration dùng `note` lịch sử để phân loại. Runtime sau migration không được
match chuỗi note. Chạy audit ngay sau B:

```sql
SELECT adjustment_reason, reference_type, count(*) AS row_count, sum(amount) AS amount_vnd
FROM vietride_payment.operator_ledger_entries
WHERE entry_type = 'ADJUSTMENT'
GROUP BY adjustment_reason, reference_type
ORDER BY adjustment_reason, reference_type;

SELECT
  count(*) FILTER (
    WHERE entry_type = 'ADJUSTMENT' AND adjustment_reason IS NULL
  ) AS adjustment_reason_null_count,
  count(*) FILTER (
    WHERE entry_type <> 'ADJUSTMENT' AND adjustment_reason IS NOT NULL
  ) AS non_adjustment_reason_count,
  count(*) FILTER (
    WHERE adjustment_reason = 'LEGACY_UNCLASSIFIED'
  ) AS legacy_unclassified_count
FROM vietride_payment.operator_ledger_entries;
```

Điều kiện dừng: bất kỳ count nào trong query thứ hai khác 0. Không tự đổi unknown thành recognized;
phải điều tra source row và có quyết định nghiệp vụ riêng.

### C — Enforce

Target migration:
`20260807085947_EnforceOperatorLedgerAdjustmentReason`.

Chỉ apply C khi audit B sạch. C tạo presence/semantic CHECK và ba partial index cho canonical
ledger `created_at`, subscription `succeeded_at`, settlement `settled_at`. Sau C, thử insert sai chỉ
trong môi trường kiểm thử; không tạo probe data trên production.

Nếu C fail, giữ maintenance, không deploy Booking/Parcel/FE mới, sửa dữ liệu theo quyết định được
duyệt rồi chạy lại. Không drop enum/cột khi typed writer đang chạy.

## 5. Parcel voucher reversal backfill

Source ID được sinh xác định:

```text
UUID(first 16 bytes of SHA256(
  "parcel-refund-voucher-adjustment:"
  + originalParcelRefundSourceEventId
  + ":allocation:"
  + parcelId
))
```

Endpoint chỉ dùng Internal JWT và không qua Gateway:

```text
POST /internal/v1/revenue/backfills/parcel-voucher-reversals?dryRun=true
X-Internal-Auth: Bearer <internal-jwt>
```

Dry-run phải trả và được lưu vào biên bản review:
`scannedRefundCount`, `candidateCount`, `skippedExistingCount`, `legacyUnclassifiedCount`,
`totalAdjustmentVnd`, `appliedCount`.

Điều kiện trước apply:

- `legacyUnclassifiedCount = 0`;
- `appliedCount = 0` ở dry-run;
- danh sách candidate và `totalAdjustmentVnd` đã được người chịu trách nhiệm tài chính duyệt;
- `totalAdjustmentVnd <= 0`.

Sau khi được duyệt, gọi cùng endpoint với `dryRun=false`. Chạy lại dry-run; kết quả bắt buộc
`candidateCount=0` và `appliedCount=0`. Nếu không đạt, giữ maintenance và điều tra idempotency/source
ID; không chạy lặp mù.

## 6. Circuit breaker và cache

Payment financial client ở Booking/Parcel có timeout tổng 5 giây, tối đa một retry GET transient,
circuit mở sau 5 failed operations, giữ open 30 giây và chỉ cho một half-open probe.

- Closed: request đi qua; success reset failure count.
- Open: trả ngay `503 UPSTREAM_UNAVAILABLE`, không gọi Payment, không dùng local money fallback.
- Half-open: một probe; success đóng circuit, failure mở lại 30 giây.

Khi Payment degrade:

1. Kiểm tra health/log của Payment và lỗi upstream trước; không sửa số liệu ở Booking/Parcel.
2. Giữ reporting maintenance nếu cả Admin/Operator visibility bị mù.
3. Sau khi Payment hồi phục, chờ half-open probe thành công và đối soát cùng một range.
4. Cache Payment/Booking financial tối đa 60 giây; support/QA phải chấp nhận độ trễ này. Không dùng
   stale cache khi query Payment lỗi. Parcel không có cache full response thứ hai.

## 7. Thứ tự rollout và rollback

Thứ tự bắt buộc:

1. Apply Migration A.
2. Deploy typed Payment writers; drain instance/worker cũ.
3. Apply Migration B; chạy classification audit và backfill dry-run.
4. Dừng nếu null/legacy unknown khác 0 hoặc backfill chưa được duyệt.
5. Đưa FE reporting vào maintenance.
6. Apply Migration C.
7. Deploy Payment API/policy/cache mới.
8. Apply Parcel backfill đã duyệt; post-check `candidateCount=0`.
9. Deploy Booking.
10. Deploy Parcel.
11. Deploy FE contract breaking mới.
12. Chạy smoke reconciliation, sau đó mở maintenance.

Rollback decision:

- Trước C: có thể rollback application về writer tương thích A, nhưng giữ cột/enum; không hạ schema
  khi còn instance typed.
- Sau C nhưng trước FE: giữ maintenance, rollback Booking/Parcel trước, sau đó Payment API nếu cần;
  không rollback migration B/C cho đến khi xác nhận mọi writer tương thích.
- Sau FE: contract không có alias cũ, vì vậy phải rollback đồng bộ FE + Booking + Parcel + Payment.
  Không chạy mixed version.
- Backfill là append-only; không xóa entry bằng rollback tự động. Reversal chỉ được thực hiện bằng
  adjustment mới sau quyết định tài chính riêng.

## 8. Smoke reconciliation

Với cùng UTC range đã chuẩn hóa:

```text
Admin Dashboard revenue
= Admin Revenue Analytics summary.revenue
= Platform Report transport totals + subscription revenue
= SUM(Admin Analytics monthly)
```

Với cùng operator/range:

```text
Operator Analytics netParcelRevenueVnd
= Parcel report netParcelRevenueVnd
= canonical Revenue export sum cho PARCEL
```

Kiểm tra thêm:

- `totalProject = netTransport + subscription`;
- `netTransport = netTicket + netParcel`;
- `paidToOperatorsVnd` không tham gia hai công thức;
- summary bằng sum monthly;
- Parcel CSV bằng Parcel summary;
- refund-only month có thể âm;
- previous=0/current khác 0 có `changePercent=null`;
- Payment unavailable trả 503 và không có data/financial fallback.

## 9. Ma trận acceptance và bằng chứng tự động

| Trường hợp | Bằng chứng chính |
|---|---|
| Một tháng, nhiều tháng, tháng trống, previous year trống | Payment Admin/Operator Revenue handler tests |
| Refund-only, VietRide/operator voucher, Booking/Parcel reversal, manual/generic/legacy | `RevenueAnalyticsRepositoryTests.PostgreSqlCoreUsesCanonicalSourcesClassificationVietnamBoundariesAndOneSqlPerRead` |
| Asia/Ho_Chi_Minh calendar → UTC half-open | `RevenueAnalyticsCoreTests.AdminRange_UsesInclusiveVietnamBoundariesAndEqualPreviousPeriod`; Booking Platform Report range tests; Parcel Payment client query test |
| Cache TTL/expiry | `RevenueAnalyticsCacheTests`; internal summary cache test; Booking Platform Report cache tests |
| Overflow | Payment repository + Booking Platform Report overflow tests |
| Dashboard dùng Payment, BookingStats không money | Booking Admin Dashboard/BookingStats unit và endpoint integration tests |
| Parcel summary/CSV dùng Payment, 503 không fallback | Parcel report unit/integration tests |
| Retry/circuit/half-open | Booking và Parcel Payment reporting client tests |

Gate cuối là full build/format/test của Payment, Booking và Parcel cùng `git diff --check`.

## 10. Performance gate trên staging

Chỉ chạy trên staging có dữ liệu đại diện. Với query summary 366 ngày, capture cả trước/sau:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT operator_id, reference_type, sum(amount)
FROM vietride_payment.operator_ledger_entries
WHERE created_at >= :from_utc
  AND created_at < :to_utc
  AND (
    (reference_type = 'BOOKING' AND (
      entry_type IN ('BOOKING_REVENUE','BOOKING_REFUND','VOUCHER_VIETRIDE_FUNDED_CREDIT')
      OR (entry_type = 'ADJUSTMENT' AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL')
    ))
    OR
    (reference_type = 'PARCEL' AND (
      entry_type IN ('PARCEL_REVENUE','PARCEL_REFUND','VOUCHER_VIETRIDE_FUNDED_CREDIT')
      OR (entry_type = 'ADJUSTMENT' AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL')
    ))
  )
GROUP BY operator_id, reference_type;
```

Lưu plan, actual time, rows, shared/local buffers và index được dùng. Gate đạt khi p95 API summary
dưới 1 giây với tải/dữ liệu đại diện và export vẫn streaming. Local database ít dữ liệu không được
dùng để tuyên bố gate này đạt; nếu không có staging representative data phải ghi rõ
`PERFORMANCE_STAGING_BLOCKED`.

## 11. Bằng chứng verification ngày 2026-08-07

- Payment: build 0 warning/error, format sạch, unit 231/231, integration 108/108.
- Booking: build 0 warning/error, format sạch, unit 607/607, integration 245/245.
- Parcel: build 0 warning/error, format sạch, unit 460/460, integration 86/86.
- Tổng: 1.737 test pass, 0 fail, 0 skip; `git diff --check` sạch.
- Production migration/backfill/deploy: chưa chạy, đúng phạm vi hand-off.
- Performance: `PERFORMANCE_STAGING_BLOCKED` — workspace hiện tại không có dataset và traffic
  staging đại diện để capture `EXPLAIN (ANALYZE, BUFFERS)` trước/sau hoặc chứng minh p95 dưới 1 giây.
