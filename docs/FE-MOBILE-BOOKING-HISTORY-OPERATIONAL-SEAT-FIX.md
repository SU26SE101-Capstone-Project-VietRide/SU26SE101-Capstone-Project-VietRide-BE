# Phản hồi FE/Mobile: Booking History trả đúng ghế vận hành

## Kết luận

Backend đã sửa API Booking History để trả đúng ghế vận hành hiện tại từ `Passenger.SeatNumber`.

FE/Mobile không cần thay đổi contract nếu đang đọc `tickets[].seatNumber`.

## Nguyên nhân trước khi sửa

Khi đặt vé, Backend lưu ghế vào cả hai trường:

- `Passenger.SeatNumber`: ghế vận hành hiện tại của hành khách.
- `Ticket.SeatNumber`: ghế tại thời điểm xuất vé, được giữ nguyên để audit/lịch sử.

Booking History đã có logic lấy ghế từ `Passenger.SeatNumber`, nhưng repository chỉ tải `Tickets` mà không tải `Passengers`. Vì vậy danh sách `booking.Passengers` rỗng và API trả `seatNumber = null` dù dữ liệu ghế vẫn tồn tại trong database.

Mobile nhận `null` nên hiển thị "Đang chờ xếp ghế".

## Thay đổi Backend

Repository của Booking History hiện tải đồng thời:

- `Tickets`.
- `Passengers`.
- `ShuttleIntents` khi request yêu cầu lịch sử Shuttle.

Query tiếp tục dùng `AsSplitQuery()` để tránh nhân bản dữ liệu khi tải nhiều collection.

Không có thay đổi schema, migration, DTO hoặc đường dẫn API.

## Quy tắc ghế

| Trường hợp | `Ticket.SeatNumber` | `Passenger.SeatNumber` | `tickets[].seatNumber` trả về |
|---|---|---|---|
| Đặt ghế `A01`, chưa thay xe | `A01` | `A01` | `A01` |
| Đặt ghế `A01`, sau thay xe chuyển sang `A10` | `A01` | `A10` | `A10` |
| Hành khách thực sự chưa được xếp ghế | Có thể là ghế cũ hoặc `null` | `null` | `null` |

Backend không fallback sang `Ticket.SeatNumber`, vì fallback có thể làm ứng dụng hiển thị ghế cũ sau khi hành khách đã được chuyển sang xe mới.

## Ví dụ response sau khi sửa

Hành khách đặt ghế `A01`, sau thay xe được chuyển sang `A10`:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "bookingCode": "VR-20260830-ABCDEFGH",
        "tickets": [
          {
            "ticketCode": "VT-20260830-ABCDEFGH",
            "seatNumber": "A10",
            "status": "ISSUED",
            "paidAmount": 350000
          }
        ]
      }
    ]
  }
}
```

`Ticket.SeatNumber` trong database vẫn là `A01`; API trả `A10` vì đó là `Passenger.SeatNumber` hiện tại.

## Hướng dẫn FE/Mobile

- Tiếp tục hiển thị `tickets[].seatNumber` trong Booking History.
- Chỉ hiển thị "Đang chờ xếp ghế" khi `seatNumber` thực sự là `null` hoặc chuỗi rỗng.
- Không tự lấy ghế từ dữ liệu vé cũ hoặc cache cũ.
- Sau khi nhận event chuyển booking/thay xe, invalidate hoặc refetch Booking History để nhận ghế vận hành mới.

## Checklist regression FE/Mobile

- Vé chưa thay xe hiển thị đúng ghế đã đặt.
- Vé đã thay xe hiển thị ghế mới từ `Passenger.SeatNumber`.
- Không hiển thị "Đang chờ xếp ghế" khi API trả số ghế.
- Refresh màn hình không quay lại ghế cũ.
- Booking có và không có Shuttle Request đều hiển thị đúng ghế.
- Pagination và tổng số booking không thay đổi.

## Kết quả kiểm thử Backend

- Booking solution build Release: đạt.
- Booking History unit tests: `16/16` đạt.
- Booking History PostgreSQL integration regression: `1/1` đạt.
- Regression xác nhận `Ticket.SeatNumber = A01` và `Passenger.SeatNumber = A10` thì History trả `A10` ở cả query có và không có Shuttle Request.
