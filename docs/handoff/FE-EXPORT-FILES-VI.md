# Handoff FE — Toàn bộ file xuất đã dùng tiếng Việt

## Kết luận cho FE

Backend Việt hóa trực tiếp các endpoint tải file hiện tại. FE chỉ cần gửi request, nhận `blob` hoặc
URL tải và dùng filename từ `Content-Disposition`; FE không dịch nội dung file.

FE chỉ phải sửa nếu đang:

- hard-code filename cũ;
- kiểm tra tên sheet;
- parse header/cột XLSX hoặc CSV;
- tự map enum bên trong file tải xuống.

API JSON và enum trong JSON vẫn giữ tiếng Anh. Quyền, tenant, query, MIME và công thức tiền không đổi.

## Quy ước chung XLSX

- Single-sheet: tiêu đề ở dòng 1, kỳ báo cáo ở dòng 2, thời gian xuất ở dòng 3, dòng 4 trống,
  header ở dòng 5; filter/freeze theo dòng 5.
- Multi-sheet: metadata chỉ nằm trong `Tổng quan`; các sheet dữ liệu có header ở dòng 1.
- Ngày giờ: `dd/MM/yyyy HH:mm`, múi giờ `Asia/Ho_Chi_Minh`.
- Tiền: `#,##0 "₫"`; tỷ lệ: `0.00"%"`.
- UUID nằm ở các cột cuối và có nhãn bắt đầu bằng `Mã hệ thống`.
- Giá trị kỹ thuật lạ hiển thị `Không xác định`, không lộ raw code.

## Sáu XLSX nhà xe

| Endpoint | Filename | Sheet | Cột theo thứ tự |
|---|---|---|---|
| `GET /v1/operator/reports/bookings/export` | `bao-cao-dat-ve-{from}-{to}.xlsx` | `Đặt vé` | Mã đặt vé; Tuyến; Điểm đi; Điểm đến; Trạng thái; Số hành khách; Tổng tiền; Thời gian đặt; Thời gian xác nhận; Thời gian hoàn thành; Mã hệ thống đặt vé; Mã hệ thống chuyến |
| `GET /v1/operator/reports/cancellation/export` | `bao-cao-huy-ve-{from}-{to}.xlsx` | `Hủy vé` | Mã đặt vé; Tuyến; Điểm đi; Điểm đến; Trạng thái; Thời gian hủy; Lý do hủy; Tổng tiền; Mã hệ thống đặt vé; Mã hệ thống chuyến |
| `GET /v1/operator/reports/parcels/export` | `bao-cao-buu-kien-{from}-{to}.xlsx` | `Bưu kiện` | Mã bưu kiện; Tuyến; Điểm gửi; Điểm nhận; Biển số xe; Trạng thái; Kích thước; Tổng cước; Tiền cọc; Phụ thu; Hoàn tiền; Thời gian tạo; Thời gian xác nhận; Mã hệ thống bưu kiện; Mã hệ thống chuyến |
| `GET /v1/operator/reports/occupancy/export` | `bao-cao-ty-le-lap-day-{from}-{to}.xlsx` | `Tỷ lệ lấp đầy` | Mã chuyến; Tuyến; Biển số xe; Trạng thái; Thời gian khởi hành; Ghế mở bán; Ghế đã đặt; Tỷ lệ lấp đầy; Mã hệ thống chuyến; Mã hệ thống tuyến |
| `GET /v1/operator/reports/revenue/export` | `bao-cao-doanh-thu-{from}-{to}.xlsx` | `Doanh thu` | Mã tham chiếu; Mã chuyến; Nội dung nghiệp vụ; Nguồn phát sinh; Số tiền; Thời gian; Diễn giải; Mã hệ thống giao dịch; Mã hệ thống tham chiếu; Mã hệ thống chuyến |
| `GET /v1/operator/reports/refunds/export` | `bao-cao-hoan-tien-{from}-{to}.xlsx` | `Hoàn tiền` | Giống báo cáo doanh thu |

`from` và `to` trong filename có dạng `yyyyMMdd`. Nếu query bỏ ngày, BE vẫn trả filename theo kỳ
thực tế đã chuẩn hóa.

## Hai workbook đối soát

### Admin ví nền tảng

```http
GET /v1/admin/platform-wallet/transactions/export
```

- Filename: `doi-soat-vi-nen-tang-{yyyyMMdd}.xlsx`.
- Sheet: `Tổng quan`, `Giao dịch`, `Phân bổ`.
- Metric, loại giao dịch, loại tham chiếu, nhóm nghiệp vụ, mục đích dòng tiền và chủ thể thực hiện
  đều hiển thị tiếng Việt.

### Ví nhà xe

```http
GET /v1/operator/wallet/reconciliation/export?from=2026-09-01&to=2026-09-30
```

- Filename: `doi-soat-vi-nha-xe-20260901-20260930.xlsx`.
- Sheet: `Tổng quan`, `Sổ cái`, `Quyết toán chuyến`, `Biến động ví`.
- Trạng thái, phương thức quyết toán, processing state, loại giao dịch và diễn giải đều là tiếng Việt.

## Hai CSV

### Tổng hợp bưu kiện

```http
GET /v1/operator/parcels/reports/export?from=2026-09-01&to=2026-09-30&format=csv
```

- Filename: `bao-cao-tong-hop-buu-kien-20260901-20260930.csv`.
- Header: `Từ ngày, Đến ngày, Tổng bưu kiện, Đã xếp lên xe, Đã giao, Bị từ chối, Đã hoàn trả,
  Doanh thu gộp, Tiền hoàn, Doanh thu thuần, Mã hệ thống nhà xe`.
- Cột `source` cũ đã bị bỏ.

### Danh sách nhà xe

```http
GET /v1/admin/operators/export
```

- Filename: `danh-sach-nha-xe-{yyyyMMdd}.csv`.
- Header: `Tên nhà xe, Email liên hệ, Số điện thoại liên hệ, Số đăng ký kinh doanh, Mã số thuế,
  Trạng thái đăng ký, Đang hoạt động, Ngày tạo, Ngày duyệt, Ngày tạm ngưng, Mã hệ thống`.
- Boolean là `Có/Không`; trạng thái và ngày giờ là tiếng Việt.

Cả hai CSV dùng UTF-8 BOM, escape dấu phẩy/dấu nháy/xuống dòng và thêm dấu nháy đơn trước giá trị
bắt đầu bằng `=`, `+`, `-`, `@`. Đây là breaking change có chủ ý: consumer parse header tiếng Anh
phải chuyển sang header mới.

## Hóa đơn PDF

```http
GET /v1/operator/invoices/{invoiceId}/download
```

Endpoint vẫn trả signed URL trong ADR-0004. File tại URL có metadata tải xuống
`hoa-don-{invoiceNumber}.pdf`; object path nội bộ vẫn dùng UUID. Nội dung PDF dùng giờ Việt Nam,
đơn vị `VNĐ`, tên hiển thị của gói và kỳ `Hàng tháng/Hàng năm`.

## Checklist FE

- [ ] Dùng filename từ `Content-Disposition`, không tự ghép filename tiếng Anh cũ.
- [ ] Tải response dưới dạng blob; không parse XLSX nếu chỉ phục vụ tải xuống.
- [ ] Nếu có import/preview CSV, đổi sang header tiếng Việt mới và xử lý UTF-8 BOM.
- [ ] Không dịch lại giá trị trong file; BE đã xuất bản copy tiếng Việt.
- [ ] Giữ xử lý lỗi ADR-0004 hiện tại; raw-file success không được parse như JSON.
