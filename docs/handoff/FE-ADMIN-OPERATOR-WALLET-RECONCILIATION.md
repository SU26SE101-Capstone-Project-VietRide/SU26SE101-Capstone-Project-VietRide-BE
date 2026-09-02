# Phản hồi FE — Đồng bộ đối soát ví Admin và ví nhà xe

## Kết luận ngắn

Backend Payment đã hoàn thiện dữ liệu đối soát cho cả hai màn hình:

- **Admin `PlatformWallet`**: xem một dòng tiền thuộc nhà xe, chuyến, booking/parcel và settlement nào.
- **Nhà xe `OperatorWallet`**: xem số tiền còn được nhận, đang chờ chuyến hoàn tất, đang hold và đã đủ điều kiện settlement.

Đây là thay đổi **additive**: các field cũ vẫn được giữ. FE cần bổ sung phần hiển thị, filter và tải
XLSX; không cần thay luồng ghi tiền hiện tại.

## Trước và sau thay đổi

| Trước | Sau |
|---|---|
| Admin chủ yếu thấy movement và `referenceType` | Có taxonomy nghiệp vụ và `allocations[]` để truy ngược nhà xe/chuyến/chứng từ |
| Khó đối chiếu tổng PlatformWallet với công nợ nhà xe | Có API summary dùng cùng công thức với từng OperatorWallet |
| Nhà xe thấy các amount rời rạc | Có `reconciliation` với công nợ dương theo trạng thái settlement của từng chuyến |
| Ledger nhà xe thiếu ngữ cảnh chuyến | Có `businessGroup`, `operatorEffect` và `trip` |
| Dữ liệu legacy/upstream thiếu dễ bị hiểu nhầm là không có giao dịch | Có `dataCompleteness=PARTIAL` và `missingFields` |
| Chưa có file đối soát tổng hợp | Có XLSX riêng cho Admin và nhà xe |

## Quy ước chung FE phải dùng

- Tất cả số tiền là số nguyên VND. FE chỉ format để hiển thị, không tự làm tròn hoặc tự tính lại.
- Không parse `note` để tìm booking, parcel, chuyến, nhà xe hoặc số tiền.
- Dùng `businessGroup` để nhóm/đặt nhãn nghiệp vụ; dùng `cashFlowPurpose` để giải thích mục đích dòng tiền.
- Dùng ID/code trong response để điều hướng hoặc hiển thị; không suy luận từ chuỗi mô tả.
- `dataCompleteness=PARTIAL` không có nghĩa giao dịch thất bại. Movement tiền vẫn hợp lệ, chỉ thiếu một
  phần dữ liệu truy vết hoặc enrichment.
- Các thời điểm public phải được parse như RFC 3339 và hiển thị theo `Asia/Ho_Chi_Minh`.
- Gateway hiện tại đã proxy các prefix liên quan; FE không cần chờ thêm route Gateway.

## Phần Admin — PlatformWallet

### 1. Danh sách giao dịch

```http
GET /v1/admin/platform-wallet/transactions
```

Quyền: chỉ `SYSTEM_ADMIN`. Role Admin khác phải nhận `403`.

Ngoài query cũ, FE có thể gửi thêm:

| Query | Ý nghĩa |
|---|---|
| `operatorId` | Chỉ lấy movement có allocation thuộc nhà xe này |
| `tripId` | Chỉ lấy movement có allocation thuộc chuyến này |
| `businessGroup` | Lọc theo nhóm nghiệp vụ |
| `cashFlowPurpose` | Lọc theo mục đích dòng tiền |
| `search` | Match thêm prefix của `referenceCode`; vẫn hỗ trợ các điều kiện search cũ |

Filter được BE áp dụng trước count/paging. Một movement có nhiều allocation vẫn chỉ xuất hiện một
lần trong danh sách.

Các field mới trên mỗi item:

```text
businessGroup
cashFlowPurpose
allocations[]
  allocatedAmountVnd
  operator
    operatorId
    name
    logoUrl
    contactPhone
  tripId
  tripCode
  referenceType
  referenceId
  referenceCode
  relatedSettlement
dataCompleteness
missingFields
```

Ví dụ response rút gọn:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "transactionId": "51c7d480-0187-4d2e-b3d9-b2607ba0d61d",
        "transactionCode": "PWT-20260902-4F8N2KQJ",
        "type": "DEBIT",
        "amount": 1250000,
        "balanceBefore": 98500000,
        "balanceAfter": 97250000,
        "referenceType": "TRIP_SETTLEMENT",
        "referenceId": "6a5dc4c4-f121-49e9-aeed-b780424c493a",
        "businessGroup": "SETTLEMENT",
        "cashFlowPurpose": "OPERATOR_PAYOUT",
        "actorType": "SYSTEM",
        "actor": null,
        "allocations": [
          {
            "allocatedAmountVnd": 1250000,
            "operator": {
              "operatorId": "5be78296-a250-4bd4-a660-b19cd06f971a",
              "name": "Nhà xe Minh Anh",
              "logoUrl": null,
              "contactPhone": "0901234567"
            },
            "tripId": "18c27198-cfa9-4312-a84e-6f21a9930525",
            "tripCode": "TRIP-20260830-M5Q7WV3D",
            "referenceType": "TRIP_SETTLEMENT",
            "referenceId": "6a5dc4c4-f121-49e9-aeed-b780424c493a",
            "referenceCode": "STL-20260902-P9R4TX2W",
            "relatedSettlement": {
              "settlementId": "6a5dc4c4-f121-49e9-aeed-b780424c493a",
              "status": "SETTLED",
              "eligibleAt": "2026-09-01T09:00:00+07:00",
              "settledAt": "2026-09-02T09:10:00+07:00",
              "walletTransactionId": "9bcfe621-9b49-4623-b236-77a2d806d3b2",
              "settlementCode": "STL-20260902-P9R4TX2W",
              "tripCode": "TRIP-20260830-M5Q7WV3D"
            }
          }
        ],
        "dataCompleteness": "COMPLETE",
        "missingFields": []
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 1
  }
}
```

Một payment nhóm có thể có nhiều phần phân bổ:

```json
{
  "transactionId": "uuid-platform-transaction",
  "amount": 500000,
  "referenceType": "BOOKING_PAYMENT_HOLD",
  "businessGroup": "TICKET",
  "cashFlowPurpose": "CUSTOMER_FUNDS_HELD",
  "allocations": [
    {
      "allocatedAmountVnd": 300000,
      "operator": { "operatorId": "uuid-operator-a", "name": "Nhà xe A" },
      "tripId": "uuid-trip-a",
      "tripCode": "TRIP-A",
      "referenceType": "BOOKING",
      "referenceId": "uuid-booking-a",
      "referenceCode": "BKG-A"
    },
    {
      "allocatedAmountVnd": 200000,
      "operator": { "operatorId": "uuid-operator-b", "name": "Nhà xe B" },
      "tripId": "uuid-trip-b",
      "tripCode": "TRIP-B",
      "referenceType": "BOOKING",
      "referenceId": "uuid-booking-b",
      "referenceCode": "BKG-B"
    }
  ]
}
```

FE nên hiển thị movement ở một row và mở rộng/drawer để xem `allocations[]`; không nhân movement
thành nhiều row vì sẽ làm tổng giao dịch bị đếm lặp.

### 2. Taxonomy Admin

| `referenceType` | `businessGroup` | `cashFlowPurpose` | Nhãn gợi ý |
|---|---|---|---|
| `BOOKING_PAYMENT_HOLD` | `TICKET` | `CUSTOMER_FUNDS_HELD` | Tiền vé khách đã giữ |
| `PARCEL_PAYMENT_HOLD`, `PARCEL_ADDITIONAL_PAYMENT_HOLD` | `PARCEL` | `CUSTOMER_FUNDS_HELD` | Tiền gửi hàng đã giữ |
| `BOOKING_REFUND`, `PARCEL_REFUND` | `REFUND` | `CUSTOMER_REFUND` | Hoàn tiền khách |
| `TRIP_SETTLEMENT` | `SETTLEMENT` | `OPERATOR_PAYOUT` | Thanh toán nhà xe |
| `SUBSCRIPTION_PAYMENT` | `SUBSCRIPTION` | `PLATFORM_REVENUE` | Doanh thu gói dịch vụ |
| `PARCEL_COMPENSATION` | `COMPENSATION` | `PARCEL_COMPENSATION_PAYOUT` | Bồi thường kiện hàng |
| `MANUAL_ADJUSTMENT` | `MANUAL_ADJUSTMENT` | `MANUAL_ADJUSTMENT` | Điều chỉnh thủ công |

FE nên map label theo enum nhưng luôn giữ fallback hiển thị raw value để không làm mất dữ liệu khi
BE bổ sung taxonomy mới.

### 3. Tổng quan đối soát Admin

```http
GET /v1/admin/platform-wallet/reconciliation-summary?from=2026-09-01&to=2026-09-30
```

- `from` và `to` phải cùng có hoặc cùng bỏ.
- Nếu bỏ cả hai, BE dùng tháng hiện tại theo ICT.
- Khoảng ngày tối đa 366 ngày, inclusive theo lịch `Asia/Ho_Chi_Minh`.

Ví dụ:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "snapshot": {
      "platformWalletBalanceVnd": 97250000,
      "outstandingOperatorPayableVnd": 24500000,
      "awaitingTripCompletionVnd": 17000000,
      "pendingHoldVnd": 3000000,
      "eligibleForSettlementVnd": 4500000,
      "eligibleOperatorCount": 8,
      "stuckSettlementCount": 2,
      "partialReconciliationTransactionCount": 3
    },
    "period": {
      "from": "2026-09-01",
      "to": "2026-09-30",
      "timezone": "Asia/Ho_Chi_Minh",
      "subscriptionRevenueVnd": 12000000,
      "paidToOperatorsVnd": 38400000
    },
    "calculatedAt": "2026-09-02T10:15:00+07:00"
  }
}
```

Ý nghĩa hiển thị:

- `platformWalletBalanceVnd`: số dư PlatformWallet hiện tại.
- `outstandingOperatorPayableVnd`: tổng công nợ dương còn phải trả cho tất cả nhà xe.
- `awaitingTripCompletionVnd`: công nợ của trip chưa có settlement marker.
- `pendingHoldVnd`: công nợ đang trong thời gian hold.
- `eligibleForSettlementVnd`: công nợ đã đủ điều kiện settlement.
- `partialReconciliationTransactionCount`: số movement chưa đủ dữ liệu truy vết; nên có cảnh báo
  kiểm tra dữ liệu thay vì trừ khỏi tổng.
- `subscriptionRevenueVnd` và `paidToOperatorsVnd` là số theo kỳ đã chọn; các field trong `snapshot`
  là snapshot hiện tại.

FE không tự cộng lại các row đang có trên trang để tạo summary vì list có paging và có thể có
movement nhiều allocation.

### 4. Export Admin

```http
GET /v1/admin/platform-wallet/transactions/export
```

Endpoint nhận cùng filter với list nhưng không nhận paging, trả file XLSX gồm đúng ba sheet:

- `Tổng quan`
- `Giao dịch`
- `Phân bổ`

Tên file là `doi-soat-vi-nen-tang-{yyyyMMdd}.xlsx`. FE gửi access token, nhận response dạng
file/blob và lấy tên file từ header; không parse hoặc tự dịch nội dung workbook. Nếu enrichment
bắt buộc chưa đầy đủ, BE trả `503 UPSTREAM_UNAVAILABLE`; FE phải hiển thị lỗi và cho tải lại, không
tự xuất file từ dữ liệu list đang paging.

## Phần nhà xe — OperatorWallet

### 1. Tổng quan ví nhà xe

```http
GET /v1/operator/wallet
```

Quyền: `OPERATOR_ADMIN | OPERATOR_STAFF`. Tenant lấy hoàn toàn từ JWT.

Field cũ vẫn giữ nguyên. Object mới:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "operatorId": "5be78296-a250-4bd4-a660-b19cd06f971a",
    "balance": 1250000,
    "currency": "VND",
    "awaitingTripCompletionAmount": 1700000,
    "pendingHoldAmount": 300000,
    "eligibleAmount": 450000,
    "lifetimeSettledAmount": 3500000,
    "withdrawalSupported": false,
    "reconciliation": {
      "outstandingPayableVnd": 2450000,
      "awaitingTripCompletionPayableVnd": 1700000,
      "pendingHoldPayableVnd": 300000,
      "eligibleForSettlementVnd": 450000
    },
    "calculatedAt": "2026-09-02T10:15:00+07:00"
  }
}
```

Cần phân biệt:

- `balance`: tiền đã được credit vào OperatorWallet sau settlement.
- `outstandingPayableVnd`: tổng tiền dương nền tảng còn phải trả cho nhà xe.
- Ba field con payable cộng lại bằng `outstandingPayableVnd`.
- Một trip có net entitlement âm hoặc bằng 0 không làm tăng payable.
- Trip `SETTLED` hoặc `CANCELLED` không nằm trong outstanding.
- `withdrawalSupported=false`: FE không hiển thị CTA rút tiền.

FE phải dùng trực tiếp object `reconciliation`, không tự tính payable từ các ledger row tải được.

### 2. Ledger nhà xe

```http
GET /v1/operator/ledger
```

Mỗi item có thêm:

```text
businessGroup
operatorEffect
trip
  tripId
  tripCode
  departureAt
  routeName
  originName
  destinationName
dataCompleteness
missingFields
```

Ví dụ:

```json
{
  "ledgerEntryId": "4511ed90-2f35-4b2b-884e-9d8aa0b19f26",
  "tripId": "18c27198-cfa9-4312-a84e-6f21a9930525",
  "entryType": "BOOKING_REVENUE",
  "amount": 500000,
  "referenceType": "BOOKING",
  "referenceId": "ec854087-c26f-4607-80df-44489428d8de",
  "referenceCode": "BKG-20260902-8Q2K",
  "businessGroup": "TICKET",
  "operatorEffect": "INCREASES_ENTITLEMENT",
  "affectsRevenue": true,
  "affectsSettlement": true,
  "trip": {
    "tripId": "18c27198-cfa9-4312-a84e-6f21a9930525",
    "tripCode": "TRIP-20260830-M5Q7WV3D",
    "departureAt": "2026-08-30T08:00:00+07:00",
    "routeName": "TP.HCM - Đà Lạt",
    "originName": "Bến xe Miền Đông",
    "destinationName": "Bến xe Đà Lạt"
  },
  "dataCompleteness": "COMPLETE",
  "missingFields": []
}
```

Mapping `operatorEffect`:

| Effect | Ý nghĩa UI |
|---|---|
| `INCREASES_ENTITLEMENT` | Tăng khoản nhà xe được nhận |
| `DECREASES_ENTITLEMENT` | Giảm khoản nhà xe được nhận |
| `AUDIT_ONLY` | Chỉ để truy vết, không cộng/trừ entitlement lần nữa |
| `INCREASES_WALLET_BALANCE` | Điều chỉnh làm tăng số dư ví |
| `DECREASES_WALLET_BALANCE` | Điều chỉnh làm giảm số dư ví |

Doanh thu và voucher do VietRide tài trợ tăng entitlement. Refund và negative adjustment được ghi
nhận làm giảm entitlement. Voucher do nhà xe tài trợ là `AUDIT_ONLY`; số tiền nằm ở
`operatorFundedVoucherAmount`, không được trừ thêm lần hai. Compensation dùng effect theo dấu của
canonical `amount`.

### 3. Giao dịch OperatorWallet

```http
GET /v1/operator/wallet/transactions
```

Field cũ vẫn giữ; FE dùng thêm `businessGroup` và `cashFlowPurpose`:

| `referenceType` | `businessGroup` | `cashFlowPurpose` |
|---|---|---|
| `TRIP_SETTLEMENT` | `SETTLEMENT` | `OPERATOR_PAYOUT_RECEIVED` |
| `SUBSCRIPTION_PAYMENT` | `SUBSCRIPTION` | `PLATFORM_SERVICE_PAYMENT` |
| `ADJUSTMENT` | `MANUAL_ADJUSTMENT` | `MANUAL_ADJUSTMENT` |
| `PARCEL_COMPENSATION` | `COMPENSATION` | `PARCEL_COMPENSATION_PAYOUT` |

`amount` cũ luôn dương. Khi cần thể hiện chiều tiền, dùng `signedAmount`: `CREDIT` dương, `DEBIT`
âm. Settlement có `relatedSettlement`; subscription luôn có `relatedSettlement=null`.

### 4. Trip settlements

```http
GET /v1/operator/trip-settlements
```

Endpoint này giữ nguyên contract hiện tại và tiếp tục là nguồn chi tiết cho breakdown, processing
state, Trip enrichment, settlement code và retry information. FE không cần thay endpoint này để
dùng reconciliation mới.

### 5. Export đối soát nhà xe

```http
GET /v1/operator/wallet/reconciliation/export?from=2026-09-01&to=2026-09-30
```

- Không gửi `operatorId`; BE lấy tenant từ JWT.
- `from/to` cùng có hoặc cùng bỏ, tối đa 366 ngày theo lịch ICT.
- Response là XLSX gồm đúng bốn sheet: `Tổng quan`, `Sổ cái`, `Quyết toán chuyến`,
  `Biến động ví`.
- Filename, sheet/header và giá trị hiển thị đã được Việt hóa tại BE; FE không dịch blob.
- Nếu Trip enrichment bắt buộc thiếu, BE trả `503 UPSTREAM_UNAVAILABLE`; FE hiển thị lỗi và cho thử lại.

## Cách xử lý dữ liệu `PARTIAL`

Ví dụ item legacy hoặc lỗi enrichment:

```json
{
  "transactionId": "uuid",
  "amount": 250000,
  "allocations": [
    {
      "allocatedAmountVnd": 250000,
      "operator": null,
      "tripId": "uuid-trip",
      "tripCode": null,
      "referenceType": "BOOKING",
      "referenceId": "uuid-booking",
      "referenceCode": null,
      "relatedSettlement": null
    }
  ],
  "dataCompleteness": "PARTIAL",
  "missingFields": [
    "allocations.operator",
    "allocations.tripCode",
    "allocations.referenceCode"
  ]
}
```

Quy tắc render:

- Vẫn hiển thị row và số tiền; không ẩn row `PARTIAL`.
- Hiển thị badge **“Thiếu dữ liệu truy vết”** và tooltip/drawer dựa trên `missingFields`.
- `operator=null`: hiển thị **“Chưa xác định nhà xe”**, không gán nhà xe hiện tại làm fallback.
- `trip=null` hoặc `tripCode=null`: hiển thị **“Chưa có thông tin chuyến”**, không suy ra từ booking code.
- Giá trị nullable hiển thị `—`; không hiển thị chữ `null`.
- List là fail-soft và vẫn có thể trả `200`; export là fail-closed và có thể trả `503`.

## Auth và giới hạn dữ liệu

- Admin PlatformWallet chỉ dành cho `SYSTEM_ADMIN`.
- API nhà xe nhận tenant từ JWT; FE không gửi và không cho người dùng sửa `operatorId`.
- Nhà xe không được xem PlatformWallet hoặc dữ liệu của nhà xe khác.
- Thiếu/sai tenant phải nhận `403`; FE xử lý như lỗi quyền, không retry vô hạn.
- Không có commission/platform fee mới trong thay đổi này.

## Checklist FE Admin

- [ ] Bổ sung filter `operatorId`, `tripId`, `businessGroup`, `cashFlowPurpose`.
- [ ] Hiển thị nhãn theo `businessGroup`/`cashFlowPurpose` và có fallback raw enum.
- [ ] Render một transaction thành một row; xem nhiều `allocations[]` trong expand/drawer.
- [ ] Hiển thị nhà xe, trip code, reference code và settlement code từ allocation.
- [ ] Thêm các card từ `reconciliation-summary`; không cộng lại list để tính card.
- [ ] Phân biệt rõ snapshot hiện tại và số liệu theo kỳ.
- [ ] Hiển thị cảnh báo khi `partialReconciliationTransactionCount > 0`.
- [ ] Hỗ trợ badge/tooltip cho `PARTIAL` và `missingFields`.
- [ ] Tải XLSX từ endpoint export và xử lý `503 UPSTREAM_UNAVAILABLE`.
- [ ] Ẩn/chặn màn hình với role khác `SYSTEM_ADMIN`.

## Checklist FE nhà xe

- [ ] Hiển thị bốn số trong `reconciliation` và giữ các field cũ trong thời gian chuyển đổi.
- [ ] Phân biệt số dư đã settlement với công nợ còn phải trả.
- [ ] Không hiển thị nút rút tiền khi `withdrawalSupported=false`.
- [ ] Bổ sung `businessGroup`, `operatorEffect` và thông tin `trip` cho ledger.
- [ ] Dùng `signedAmount` để thể hiện chiều giao dịch OperatorWallet.
- [ ] Không tính voucher nhà xe tài trợ hai lần.
- [ ] Xử lý `trip=null`, `PARTIAL` và `missingFields` theo quy tắc ở trên.
- [ ] Export không gửi `operatorId`; nhận XLSX và xử lý `503`.
- [ ] Không cho phép điều hướng sang dữ liệu PlatformWallet hoặc tenant khác.

## Xác nhận phạm vi

- BE Payment đã cung cấp dữ liệu/API nêu trên.
- FE cần thay đổi phần hiển thị, filter và export để tận dụng dữ liệu mới.
- Không cần sửa Gateway route.
- Không thay đổi Identity.
- Không thay công thức settlement/refund, không tạo commission row và không thêm platform fee.
