# Phản hồi BE — thông tin người đặt khi crew chọn ghế

> Gửi: Mobile crew app (Driver/Assistant)
> Từ: Backend
> Ngày phản hồi: 30/08/2026
> Trạng thái: Đã triển khai và kiểm thử Docker E2E thu gọn
> Endpoint liên quan: `GET /v1/bookings/trips/{tripId}/manifest` và
> `POST /v1/bookings/trips/{tripId}/boarding/qr-scan`

## 1. Kết luận

Backend đã bổ sung dữ liệu để Driver/Assistant chạm vào ghế và xem người liên hệ
của Booking. Tuy nhiên, hệ thống hiện không thu thập danh tính riêng của từng người
ngồi từng ghế, nên contract chính xác là:

```text
buyerName     string|null   Tên người đặt/người liên hệ của Booking
buyerPhone    string|null   SĐT người đặt, giữ nguyên E.164
```

Không sử dụng tên `passengerName`/`passengerPhone`, vì các tên đó có thể khiến crew
hiểu nhầm đây là danh tính đã được xác minh của người thực tế ngồi ghế. Nếu một
Booking có nhiều ghế thì các ghế đó nhận cùng `buyerName` và `buyerPhone`.

## 2. Contract manifest đã cập nhật

`GET /v1/bookings/trips/{tripId}/manifest` trả thêm ba field nullable trên mỗi item:

```text
pickupPointName   string|null   Tên điểm đón để hiển thị
buyerName         string|null   Tên người đặt Booking
buyerPhone        string|null   SĐT người đặt theo E.164
```

Ví dụ response:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "passengerRecordId": "c0000000-0000-4000-8000-000000000001",
        "ticketId": "d0000000-0000-4000-8000-000000000001",
        "ticketCode": "VT-20260830-E2EABCDE",
        "seatNumber": "A10",
        "bookingCode": "VR-20260830-E2EABCDE",
        "pickupStop": "a0000000-0000-4000-8000-000000000001",
        "boardingStatus": "PENDING",
        "pickupPointName": "Ngã tư Hàng Xanh",
        "buyerName": "Nguyễn Văn E2E",
        "buyerPhone": "+84888151546"
      }
    ]
  },
  "meta": {
    "traceId": "...",
    "timestamp": "..."
  }
}
```

Manifest bao gồm Booking ở trạng thái `CONFIRMED`, `PARTIAL_NO_SHOW` hoặc `NO_SHOW`,
nhưng vẫn chỉ lấy Ticket `ISSUED` hoặc `USED`.

## 3. Quyền truy cập và bảo vệ thông tin liên hệ

- Caller phải có role `DRIVER` hoặc `ASSISTANT`.
- JWT `sub` phải đúng Driver/Assistant được phân công cho Trip; caller khác nhận
  `403 FORBIDDEN`.
- `buyerName` và `buyerPhone` chỉ có giá trị khi Trip ở `BOARDING` hoặc
  `IN_PROGRESS`.
- Với `SCHEDULED`, `COMPLETED`, `CANCELLED`, snapshot thiếu hoặc snapshot đã
  redacted, hai field contact trả `null`.
- Phone được trả nguyên định dạng E.164 để Mobile tự format khi hiển thị/gọi điện.
- Manifest và QR response đặt `Cache-Control: private, no-store`.
- Backend không ghi buyer PII vào log hoặc integration event.

BE không trả số che một phần và không thêm endpoint gọi điện riêng ở phiên bản này.
Quyền truy cập theo assignment và cửa sổ trạng thái Trip là lớp bảo vệ bắt buộc.

## 4. QR scan

`POST /v1/bookings/trips/{tripId}/boarding/qr-scan` trả thêm:

```text
bookingCode   string
buyerName     string|null
buyerPhone    string|null
```

Quy tắc contact giống manifest. QR scan chỉ đọc dữ liệu, không tick boarding và
không yêu cầu header `Idempotency-Key`.

- Gửi `ticketCode`: trả đúng một item nếu ticket hợp lệ.
- Gửi legacy `bookingCode`: có thể trả nhiều item thuộc cùng Booking.
- Mutation xác nhận lên xe vẫn dùng endpoint boarding-passenger riêng.

## 5. Giải đáp các câu hỏi của Mobile

### 5.1 Có ràng buộc bảo mật nào không?

Có. Chỉ crew được phân công và chỉ trong `BOARDING|IN_PROGRESS` mới nhận contact.
Ngoài cửa sổ này backend vẫn trả item để giữ contract nhưng contact là `null`.
Mobile không được lưu contact vào local cache lâu dài, analytics, crash report hoặc
log debug.

### 5.2 QR scan có trả tên/số không?

Có. QR scan trả `bookingCode`, `buyerName`, `buyerPhone` theo cùng policy với
manifest. Đây là thông tin người đặt Booking, không phải danh tính riêng của người
đang cầm vé.

### 5.3 `pickupStop` là tên hay UUID?

`pickupStop` là UUID nullable và được giữ nguyên để tương thích. Mobile không được
hiển thị trực tiếp field này. Hãy hiển thị `pickupPointName`:

- Pickup tại Stop: tên Stop.
- `pickupStop = null`: tên bến đầu của Trip.
- `pickupPointName = null`: fallback UI như `Điểm đón đang cập nhật`, không hiển thị
  UUID.

### 5.4 Ghế `BOOKED` nhưng không có manifest nghĩa là gì?

Seat map là trạng thái ghế phía Trip, còn manifest chỉ chứa Booking/Ticket đủ điều
kiện vận hành. Chênh lệch có thể xuất hiện trong lúc thanh toán/phát hành vé hoặc
đồng bộ trạng thái.

Mobile không được suy diễn contact từ ghế khác và không cho tick boarding khi ghế
không có item manifest. Trạng thái UI đề nghị:

```text
Đã đặt — đang đồng bộ thông tin vé
```

Không nên ghi `Đã đặt (chưa có vé)` vì Mobile không đủ dữ liệu để kết luận vé chưa
tồn tại hay sẽ không được phát hành.

### 5.5 Có field danh sách ghế đi cùng nhóm không?

Không thêm field mới. `bookingCode` tiếp tục là khóa group. Mobile có thể gom các
item manifest có cùng `bookingCode`; backend không trả thêm danh sách ghế lặp lại
trên từng item.

## 6. Mobile cần sửa gì

### 6.1 Cập nhật model

```ts
type CrewManifestItem = {
  passengerRecordId: string;
  ticketId: string;
  ticketCode: string;
  seatNumber: string;
  bookingCode: string;
  pickupStop: string | null;
  boardingStatus: string;
  pickupPointName: string | null;
  buyerName: string | null;
  buyerPhone: string | null;
};
```

QR passenger item cũng cần thêm `bookingCode`, `buyerName`, `buyerPhone`.

### 6.2 Ghép seat map với manifest

- Tiếp tục map theo `seatNumber`.
- Chỉ mở thao tác boarding khi tìm thấy item manifest tương ứng.
- Ghế `BOOKED` không có item manifest dùng trạng thái đồng bộ nêu trên.
- Không gán contact của cùng `bookingCode` cho một ghế không tồn tại trong manifest.

### 6.3 Hiển thị bottom sheet

- Nhãn nên là `Người đặt` hoặc `Người liên hệ`, không phải `Hành khách`.
- Hiển thị `buyerName` khi khác `null`.
- Chỉ hiện nút gọi khi `buyerPhone` khác `null`; có thể format E.164 sang nội địa ở
  UI nhưng giữ giá trị gốc khi mở dialer.
- Hiển thị `pickupPointName`, không hiển thị `pickupStop` UUID.
- Tiếp tục dùng `passengerRecordId` cho mutation xác nhận lên xe.

### 6.4 Xử lý dữ liệu nullable và client cũ

Các field mới là additive và nullable. Mobile phải xử lý được `null`, đặc biệt khi
Trip chưa vào giai đoạn vận hành hoặc trong rolling deployment. Client cũ vẫn hoạt
động vì không field cũ nào bị đổi tên hoặc xóa.

## 7. Những gì Backend không thay đổi

- Không thêm PII riêng cho Passenger/ghế.
- Không migration hoặc thay đổi database schema.
- Không thêm dependency, endpoint, Gateway route hoặc integration event.
- Không thay đổi endpoint tick boarding.
- Không thay đổi `pickupStop` UUID và `bookingCode` grouping key.

## 8. Bằng chứng kiểm thử

Backend đã chạy E2E thu gọn bằng container thật với luồng:

```text
Booking API → Booking PostgreSQL → Trip API → Trip PostgreSQL
```

Các case đã pass:

- Manifest lấy đúng `buyerName`, `buyerPhone`, `pickupPointName` từ dữ liệu thật.
- Hai ghế cùng Booking trả cùng contact.
- QR theo ticket trả một item; QR theo booking code trả nhiều item.
- Caller không được phân công nhận `403 FORBIDDEN`.
- `BOARDING` có contact; `SCHEDULED` redacted contact về `null`.
- Manifest/QR có `private, no-store`.
- Không tìm thấy buyer PII đã seed trong log Trip/Booking.

Trong lần E2E đầu, QR read-only bị middleware yêu cầu `Idempotency-Key`. Backend đã
sửa endpoint thành read-only idempotency-skip và chạy lại thành công: QR trả `200`
mà không cần header này.

## 9. Thứ tự rollout đề nghị

1. Deploy Trip service để internal snapshot có Stop `name`.
2. Deploy Booking service để trả các field manifest/QR mới.
3. Mobile cập nhật model nullable và UI theo hướng dẫn trên.

Booking xử lý được Trip version cũ chưa có Stop `name`, nên rolling deployment không
làm endpoint lỗi; trong khoảng chuyển tiếp `pickupPointName` có thể là `null`.
