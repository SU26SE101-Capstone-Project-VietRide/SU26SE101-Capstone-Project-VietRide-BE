# FE Hand-off — Chuẩn hóa doanh thu toàn dự án theo Payment

> Ngày chốt: 2026-08-08
>
> Phạm vi: Dashboard, Revenue Analytics, Platform Report, Booking Stats, Parcel Report và export liên quan đến dòng tiền
>
> Đối tượng sử dụng: Frontend Admin và Frontend Nhà xe
>
> Base URL: gọi qua API Gateway của từng môi trường

## 1. Kết luận cần thống nhất

1. **Payment là nguồn dữ liệu tài chính chuẩn duy nhất** cho các số doanh thu công khai.
2. Booking, Trip và Parcel chỉ tiếp tục sở hữu các số liệu vận hành như số booking, số chuyến và số kiện hàng.
3. FE không tự tính lại doanh thu từ `Booking.totalAmount`, `Parcel.totalPrice`, trạng thái booking/parcel hoặc số tiền hiển thị trong các bảng vận hành.
4. Tất cả số tiền là số nguyên VND và có thể âm. FE không được ép số âm về `0`.
5. Các API `/internal/**` chỉ dành cho giao tiếp giữa service, không được FE gọi và không được Gateway public.
6. Các field revenue là **KPI quản trị**, không phải báo cáo kế toán pháp lý và không đồng nghĩa với tiền mặt khách đã trả hoặc số dư ví.

## 2. Quy tắc doanh thu chuẩn

```text
netTicketRevenueVnd      = các ledger entry BOOKING hợp lệ
netParcelRevenueVnd      = các ledger entry PARCEL hợp lệ
netTransportRevenueVnd   = netTicketRevenueVnd + netParcelRevenueVnd
subscriptionRevenueVnd   = subscription payment SUCCEEDED, ghi nhận theo succeededAt
totalProjectRevenueVnd   = netTransportRevenueVnd + subscriptionRevenueVnd
```

`paidToOperatorsVnd` là dòng tiền settlement độc lập. Field này:

- nằm trong object `settlement`;
- không thuộc công thức revenue;
- không được cộng vào `totalProjectRevenueVnd` hoặc `netTransportRevenueVnd`.

Phần voucher do VietRide tài trợ được ghi nhận vào KPI doanh thu của nhà xe dù không phải tiền mặt hành khách trực tiếp trả. FE nên dùng nhãn “Doanh thu ghi nhận” hoặc “Doanh thu quản trị”, không dùng nhãn “Tiền khách đã trả”.

Refund và reversal được ghi nhận tại kỳ phát sinh ledger. Vì vậy một tháng không có giao dịch bán mới vẫn có thể có doanh thu âm do hoàn tiền của kỳ trước.

## 3. Quy tắc ngày giờ

### 3.1 Dữ liệu FE gửi

FE gửi ngày lịch dạng:

```text
YYYY-MM-DD
```

Ví dụ:

```text
from=2026-07-01&to=2026-07-31
```

`from` và `to` là ngày inclusive theo lịch `Asia/Ho_Chi_Minh`. FE không gửi timestamp và không tự chuyển UTC.

### 3.2 Cách BE truy vấn dữ liệu

BE chuyển khoảng ngày trên thành UTC half-open:

```text
[fromUtc, toUtcExclusive)
```

Ví dụ:

```text
2026-07-01..2026-07-31
→ [2026-06-30T17:00:00Z, 2026-07-31T17:00:00Z)
```

Đây là boundary UTC dùng để query/persist. FE chỉ giữ giá trị ngày người dùng chọn, không hiển thị hai mốc UTC này như khoảng báo cáo.

## 4. Danh sách API FE sử dụng

| Màn hình/chức năng | API | Role | Service sở hữu public facade |
|---|---|---|---|
| Admin Dashboard | `GET /v1/admin/dashboard/summary` | `SYSTEM_ADMIN` | Booking |
| Admin Revenue Analytics | `GET /v1/admin/revenue/analytics` | `SYSTEM_ADMIN` | Payment |
| Admin Platform Report | `GET /v1/admin/reports/platform` | `SYSTEM_ADMIN` | Booking |
| Operator Revenue Analytics | `GET /v1/operator/revenue/analytics` | `OPERATOR_ADMIN` | Payment |
| Operator Parcel Report Summary | `GET /v1/operator/parcels/reports/summary` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` | Parcel, money lấy từ Payment |
| Operator Parcel Report CSV cũ | `GET /v1/operator/parcels/reports/export` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` | Parcel, money lấy từ Payment |
| Operator Revenue XLSX | `GET /v1/operator/reports/revenue/export` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` | Payment |
| Operator Refund XLSX | `GET /v1/operator/reports/refunds/export` | `OPERATOR_ADMIN`, `OPERATOR_STAFF` | Payment |

Mọi request JSON thành công dùng envelope chung:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "req-abc123",
    "timestamp": "2026-08-08T08:00:00Z"
  }
}
```

Các API download file trả raw binary/file response, không bọc `ApiResponse` khi thành công.

## 5. Chi tiết từng API

### 5.1 Admin Dashboard Summary

```http
GET /v1/admin/dashboard/summary?from=2026-07-01&to=2026-07-31
Authorization: Bearer <SYSTEM_ADMIN_TOKEN>
```

Quy tắc:

- `from` và `to` bắt buộc;
- khoảng ngày từ 1 đến 366 ngày inclusive;
- kỳ so sánh là khoảng có cùng số ngày, nằm ngay trước `from`;
- năm field doanh thu lấy từ Payment;
- Booking chỉ cung cấp count booking, Identity cung cấp user/operator metrics.

Response `data`:

```json
{
  "period": {
    "from": "2026-07-01",
    "to": "2026-07-31",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "totalProjectRevenueVnd": {
    "currentValue": 55000000,
    "previousValue": 50000000,
    "changePercent": 10.0,
    "trend": "UP"
  },
  "netTransportRevenueVnd": {
    "currentValue": 51200000,
    "previousValue": 48000000,
    "changePercent": 6.67,
    "trend": "UP"
  },
  "netTicketRevenueVnd": {
    "currentValue": 48000000,
    "previousValue": 45000000,
    "changePercent": 6.67,
    "trend": "UP"
  },
  "netParcelRevenueVnd": {
    "currentValue": 3200000,
    "previousValue": 3000000,
    "changePercent": 6.67,
    "trend": "UP"
  },
  "subscriptionRevenueVnd": {
    "currentValue": 3800000,
    "previousValue": 2000000,
    "changePercent": 90.0,
    "trend": "UP"
  },
  "activeOperators": {
    "currentValue": 12,
    "previousValue": 10,
    "changePercent": 20.0,
    "trend": "UP"
  },
  "activeUsers": {
    "currentValue": 500,
    "previousValue": 450,
    "changePercent": 11.11,
    "trend": "UP"
  },
  "bookings": {
    "currentValue": 120,
    "previousValue": 100,
    "changePercent": 20.0,
    "trend": "UP"
  },
  "userDistribution": [
    { "role": "PASSENGER", "count": 450 }
  ],
  "operatorStatusDistribution": [
    { "status": "APPROVED", "count": 12, "percent": 80.0 }
  ]
}
```

Field cũ `totalRevenue` không còn dùng. FE phải chuyển sang năm comparison field mới ở trên.

### 5.2 Admin Revenue Analytics

```http
GET /v1/admin/revenue/analytics?from=2026-01-01&to=2026-12-31&groupBy=month&top=5
Authorization: Bearer <SYSTEM_ADMIN_TOKEN>
```

Quy tắc:

- `from` và `to` bắt buộc, định dạng `YYYY-MM-DD`;
- khoảng ngày từ 1 đến 366 ngày inclusive;
- `groupBy` bắt buộc và phải đúng `month`;
- `top` mặc định `5`, BE clamp trong khoảng `1..20`;
- `monthly` zero-fill các tháng không có dữ liệu;
- `topOperators[].revenueVnd` là net transport revenue, không phải payout.

Response `data`:

```json
{
  "period": {
    "from": "2026-01-01",
    "to": "2026-12-31",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "summary": {
    "revenue": {
      "totalProjectRevenueVnd": {
        "currentValue": 55000000,
        "previousValue": 50000000,
        "changePercent": 10.0,
        "trend": "UP"
      },
      "netTransportRevenueVnd": {
        "currentValue": 51200000,
        "previousValue": 48000000,
        "changePercent": 6.67,
        "trend": "UP"
      },
      "netTicketRevenueVnd": {
        "currentValue": 48000000,
        "previousValue": 45000000,
        "changePercent": 6.67,
        "trend": "UP"
      },
      "netParcelRevenueVnd": {
        "currentValue": 3200000,
        "previousValue": 3000000,
        "changePercent": 6.67,
        "trend": "UP"
      },
      "subscriptionRevenueVnd": {
        "currentValue": 3800000,
        "previousValue": 2000000,
        "changePercent": 90.0,
        "trend": "UP"
      }
    },
    "settlement": {
      "paidToOperatorsVnd": {
        "currentValue": 40000000,
        "previousValue": 35000000,
        "changePercent": 14.29,
        "trend": "UP"
      }
    }
  },
  "monthly": [
    {
      "month": "2026-01",
      "revenue": {
        "totalProjectRevenueVnd": 5000000,
        "netTransportRevenueVnd": 4700000,
        "netTicketRevenueVnd": 4300000,
        "netParcelRevenueVnd": 400000,
        "subscriptionRevenueVnd": 300000
      },
      "settlement": {
        "paidToOperatorsVnd": 3900000
      }
    }
  ],
  "topOperators": [
    {
      "rank": 1,
      "operatorId": "00000000-0000-4000-8000-000000000001",
      "operatorName": "Nhà xe A",
      "logoUrl": null,
      "revenueVnd": 12000000,
      "vehicleCount": 10
    }
  ],
  "generatedAt": "2026-08-08T08:00:00Z"
}
```

Không cộng `summary.settlement.paidToOperatorsVnd` vào bất kỳ tổng revenue nào.

### 5.3 Admin Platform Report

```http
GET /v1/admin/reports/platform?from=2026-07-01&to=2026-07-31
Authorization: Bearer <SYSTEM_ADMIN_TOKEN>
```

API này dùng để hiển thị count vận hành và net transport revenue theo từng nhà xe. Nó không chứa subscription revenue và không chứa settlement.

Response `data`:

```json
{
  "period": {
    "from": "2026-07-01",
    "to": "2026-07-31",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "totals": {
    "completedBookingCount": 120,
    "completedTripCount": 36,
    "deliveredParcelCount": 18,
    "netTicketRevenueVnd": 48000000,
    "netParcelRevenueVnd": 3200000,
    "netTransportRevenueVnd": 51200000
  },
  "byOperator": [
    {
      "operatorId": "00000000-0000-4000-8000-000000000001",
      "operatorName": "Nhà xe A",
      "completedBookingCount": 120,
      "completedTripCount": 36,
      "deliveredParcelCount": 18,
      "netTicketRevenueVnd": 48000000,
      "netParcelRevenueVnd": 3200000,
      "netTransportRevenueVnd": 51200000
    }
  ],
  "generatedAt": "2026-08-08T08:00:00Z"
}
```

`operatorName` có thể `null`. `byOperator` được sắp xếp theo `netTransportRevenueVnd` giảm dần rồi theo `operatorId`.

### 5.4 Operator Revenue Analytics

API lấy `operatorId` từ JWT. FE không gửi `operatorId` trong query hoặc body.

#### Chế độ tháng

```http
GET /v1/operator/revenue/analytics?month=2026-07
Authorization: Bearer <OPERATOR_ADMIN_TOKEN>
```

- summary so sánh tháng chọn với tháng liền trước;
- `monthly` trả rolling 12 tháng kết thúc ở tháng được chọn;
- có field `routePerformance`.

#### Chế độ năm

```http
GET /v1/operator/revenue/analytics?year=2026&groupBy=month
Authorization: Bearer <OPERATOR_ADMIN_TOKEN>
```

- summary so sánh cả năm với năm lịch trước;
- `monthly` trả đủ tháng `01..12`, zero-fill tháng không có dữ liệu;
- response omit hoàn toàn `routePerformance`;
- không gửi đồng thời `month` và `year`.

Response tháng `data`:

```json
{
  "period": {
    "month": "2026-07",
    "year": null,
    "groupBy": "month",
    "from": "2026-07-01",
    "to": "2026-07-31",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "summary": {
    "netRevenueVnd": {
      "currentValue": 10000000,
      "previousValue": 8000000,
      "changePercent": 25.0,
      "trend": "UP"
    },
    "netTicketRevenueVnd": {
      "currentValue": 9000000,
      "previousValue": 7000000,
      "changePercent": 28.57,
      "trend": "UP"
    },
    "netParcelRevenueVnd": {
      "currentValue": 1000000,
      "previousValue": 1000000,
      "changePercent": 0,
      "trend": "FLAT"
    },
    "averageNetRevenuePerTripVnd": {
      "currentValue": 2000000,
      "previousValue": 1600000,
      "changePercent": 25.0,
      "trend": "UP"
    }
  },
  "monthly": [
    {
      "month": "2026-07",
      "netRevenueVnd": 10000000,
      "netTicketRevenueVnd": 9000000,
      "netParcelRevenueVnd": 1000000,
      "tripCount": 5
    }
  ],
  "routePerformance": [
    {
      "routeId": "00000000-0000-4000-8000-000000000002",
      "routeName": "TP.HCM - Đà Lạt",
      "originName": "TP.HCM",
      "destinationName": "Đà Lạt",
      "tripCount": 5,
      "completedTripCount": 4,
      "bookingCount": 100,
      "parcelCount": 15,
      "netRevenueVnd": 10000000,
      "completionRatePercent": 80.0
    }
  ],
  "generatedAt": "2026-08-08T08:00:00Z"
}
```

### 5.5 Operator Parcel Report Summary

```http
GET /v1/operator/parcels/reports/summary?from=2026-07-01&to=2026-07-31
Authorization: Bearer <OPERATOR_TOKEN>
```

Response `data`:

```json
{
  "operatorId": "00000000-0000-4000-8000-000000000001",
  "from": "2026-07-01",
  "to": "2026-07-31",
  "totalParcels": 100,
  "totalLoaded": 90,
  "totalDelivered": 80,
  "totalRejected": 2,
  "totalReturned": 3,
  "grossParcelRevenueVnd": 12000000,
  "parcelRefundsVnd": -2000000,
  "netParcelRevenueVnd": 10000000,
  "source": "ParcelStats"
}
```

Quy tắc:

```text
netParcelRevenueVnd = grossParcelRevenueVnd + parcelRefundsVnd
```

`parcelRefundsVnd` là số signed và thường âm. `source` chỉ mô tả nguồn của các count:

- `ParcelStats`; hoặc
- `ParcelsFallback`.

Money luôn lấy từ Payment, không phụ thuộc giá trị của `source`.

Hai field cũ `totalRevenue` và `totalRefunded` đã bị bỏ hoàn toàn, không có alias.

### 5.6 Export file

#### Revenue XLSX

```http
GET /v1/operator/reports/revenue/export?from=2026-07-01&to=2026-07-31
```

#### Refund XLSX

```http
GET /v1/operator/reports/refunds/export?from=2026-07-01&to=2026-07-31
```

Hai API trên:

- nhận `from`, `to` là ngày Việt Nam (`Asia/Ho_Chi_Minh`) inclusive;
- mặc định khoảng gần nhất nếu FE không truyền;
- tối đa 92 ngày inclusive;
- thành công trả XLSX raw binary;
- range rỗng vẫn trả file XLSX hợp lệ;
- lỗi trả JSON theo `ApiResponse`.

FE cần đọc filename từ `Content-Disposition` và tải response dưới dạng `blob`.

Ví dụ:

```ts
const response = await api.get('/v1/operator/reports/revenue/export', {
  params: { from: '2026-07-01', to: '2026-07-31' },
  responseType: 'blob',
});
```

Revenue XLSX dùng canonical signed Payment ledger. Refund XLSX chỉ chứa `BOOKING_REFUND` và `PARCEL_REFUND`.

#### Legacy Parcel CSV

```http
GET /v1/operator/parcels/reports/export?from=2026-07-01&to=2026-07-31&format=csv
```

Header mới:

```text
operatorId,from,to,totalParcels,totalLoaded,totalDelivered,totalRejected,totalReturned,grossParcelRevenueVnd,parcelRefundsVnd,netParcelRevenueVnd,source
```

FE nên luôn gửi rõ `from` và `to` cho CSV cũ để tránh phụ thuộc khoảng mặc định.

## 6. Các API stats không còn chứa tiền

### Operator Booking Stats

```http
GET /v1/operator/booking-stats?from=2026-07-01&to=2026-07-31&groupBy=date
```

Role: `OPERATOR_ADMIN`, `OPERATOR_STAFF`.

`groupBy` hỗ trợ `date|month`. Với `month`, `from` và `to` bắt buộc; `date` trong mỗi bucket là ngày đầu tiên của tháng Việt Nam (`Asia/Ho_Chi_Minh`) và các tháng không có dữ liệu vẫn được zero-fill.

Chỉ dùng cho count booking:

```json
{
  "items": [
    {
      "operatorId": "00000000-0000-4000-8000-000000000001",
      "date": "2026-07-01",
      "totalBookings": 120,
      "totalCancellations": 4,
      "totalNoShows": 2,
      "totalPartialNoShows": 1,
      "totalCompleted": 113
    }
  ],
  "totalBookings": 120
}
```

### Admin Booking Stats Aggregate

```http
GET /v1/admin/booking-stats/aggregate?from=2026-07-01&to=2026-07-31&groupBy=operator
```

Role: `SYSTEM_ADMIN`.

`groupBy` hỗ trợ `operator|date|month`. Với `month`, `from` và `to` bắt buộc, bucket dùng ngày đầu tiên của tháng Việt Nam (`Asia/Ho_Chi_Minh`) và được zero-fill.

Field tiền `totalRevenue` trong item hoặc totals đã bị bỏ hoàn toàn.

### Operator Parcel Stats

```http
GET /v1/operator/parcel-stats?from=2026-07-01&to=2026-07-31&groupBy=status
```

API này chỉ thống kê count theo status/route, không phải API tài chính. Không dùng response của API này để suy ra doanh thu.

## 7. Breaking field mapping bắt buộc sửa ở FE

| Surface | Field cũ | Field mới/hành vi mới |
|---|---|---|
| Admin Dashboard | `totalRevenue` | `totalProjectRevenueVnd`, `netTransportRevenueVnd`, `netTicketRevenueVnd`, `netParcelRevenueVnd`, `subscriptionRevenueVnd` |
| BookingStats Admin/Operator | `totalRevenue` trong item/totals | Bỏ hoàn toàn, không có alias |
| Platform Report | `bookingRevenueVnd` | `netTicketRevenueVnd` |
| Platform Report | `parcelRevenueVnd` | `netParcelRevenueVnd` |
| Platform Report | `netRevenueVnd` | `netTransportRevenueVnd` |
| Admin Revenue Analytics | `grossRevenueVnd`, `platformRevenueVnd` | `summary.revenue.*` |
| Admin Revenue Analytics | top-level `paidToOperatorsVnd` | `summary.settlement.paidToOperatorsVnd` |
| Operator Revenue Analytics | `totalRevenueVnd` | `netRevenueVnd` |
| Operator Revenue Analytics | `ticketRevenueVnd` | `netTicketRevenueVnd` |
| Operator Revenue Analytics | `parcelRevenueVnd` | `netParcelRevenueVnd` |
| Operator Revenue Analytics | `averageRevenuePerTripVnd` | `averageNetRevenuePerTripVnd` |
| Operator route/month item | `revenueVnd` | `netRevenueVnd` |
| Parcel summary/CSV | `totalRevenue` | `grossParcelRevenueVnd` và `netParcelRevenueVnd` |
| Parcel summary/CSV | `totalRefunded` | signed `parcelRefundsVnd` |

Không giữ fallback đọc field cũ. FE cần đổi contract đồng bộ khi deploy.

## 8. Kiểu dữ liệu TypeScript đề xuất

```ts
type Trend = 'UP' | 'DOWN' | 'FLAT';

interface Comparison {
  currentValue: number;
  previousValue: number;
  changePercent: number | null;
  trend: Trend;
}

interface ReportPeriod {
  from: string; // YYYY-MM-DD
  to: string; // YYYY-MM-DD
  timezone: 'Asia/Ho_Chi_Minh';
}

interface ApiResponse<T> {
  success: true;
  statusCode: number;
  data: T;
  meta: {
    traceId: string;
    timestamp: string;
  };
}
```

API hiện trả VND dưới dạng JSON number. FE không tự làm tròn theo số thập phân và không chuyển qua kiểu floating-point trung gian khi cộng/tách số tiền. Khi format:

```ts
const formatVnd = (value: number) =>
  new Intl.NumberFormat('vi-VN', {
    style: 'currency',
    currency: 'VND',
    maximumFractionDigits: 0,
  }).format(value);
```

## 9. Cách hiển thị comparison

| Trường hợp | `changePercent` | `trend` | UI đề xuất |
|---|---:|---|---|
| previous = 0, current = 0 | `0` | `FLAT` | `0%` |
| previous = 0, current > 0 | `null` | `UP` | Hiển thị `—` hoặc “Mới”, không hiển thị `0%` |
| previous = 0, current < 0 | `null` | `DOWN` | Hiển thị `—`, vẫn giữ số tiền âm |
| previous != 0 | số phần trăm | `UP`, `DOWN` hoặc `FLAT` | Hiển thị theo giá trị BE trả |

FE không tự tính lại `changePercent`.

## 10. Cache và trạng thái loading

- Payment dùng cache read-through tối đa 60 giây cho analytics và revenue summary.
- Sau giao dịch mới, Dashboard/Analytics/Parcel Report có thể chậm cập nhật tối đa 60 giây.
- `generatedAt` là UTC và có thể phản ánh thời điểm cache được tạo.
- Không coi độ trễ dưới 60 giây là lỗi đồng bộ dữ liệu.
- Khi backend trả `503`, FE không lấy một response cũ ở client để giả làm dữ liệu hiện tại nếu không hiển thị rõ trạng thái stale.

## 11. Error handling

| HTTP | Ý nghĩa | FE xử lý |
|---:|---|---|
| `401` | Token thiếu/hết hạn/không hợp lệ | Chạy flow refresh/login hiện hành |
| `403 FORBIDDEN` | Sai role hoặc thiếu operator scope | Hiển thị không có quyền, không retry |
| `422 VALIDATION_ERROR` | Sai format/range/mode query | Hiển thị lỗi filter ngày hoặc mode |
| `422 REPORT_RANGE_INVALID` | Khoảng export không hợp lệ hoặc quá 92 ngày | Yêu cầu chọn lại khoảng ngày |
| `500 REPORT_VALUE_OVERFLOW` | Tổng vượt giới hạn backend | Báo lỗi hệ thống, không tự tính thay |
| `503 UPSTREAM_UNAVAILABLE` | Payment hoặc source phụ thuộc không dùng được | Hiển thị “Dữ liệu tài chính tạm thời không khả dụng”, cho phép retry |

Backend fail-closed: không trả partial total và không fallback sang BookingStats/ParcelStats money.

## 12. Checklist FE trước khi merge

- [ ] Tất cả request đi qua Gateway và dùng đúng role.
- [ ] Không có code gọi `/internal/**`.
- [ ] Date picker gửi `YYYY-MM-DD`, không tự đổi thành timestamp UTC.
- [ ] Đã bỏ toàn bộ field cũ trong bảng breaking mapping.
- [ ] Không lấy doanh thu từ BookingStats hoặc ParcelStats.
- [ ] Không cộng `paidToOperatorsVnd` vào revenue.
- [ ] Không clamp doanh thu/refund âm về `0`.
- [ ] `changePercent=null` được hiển thị khác `0%`.
- [ ] Year mode của Operator Analytics xử lý trường hợp `routePerformance` bị omit.
- [ ] Download XLSX/CSV dùng `blob` và đọc filename từ `Content-Disposition`.
- [ ] UI có thông báo phù hợp cho độ trễ cache tối đa 60 giây.
- [ ] UI xử lý `503 UPSTREAM_UNAVAILABLE` và không dựng số tiền fallback từ dữ liệu vận hành.
