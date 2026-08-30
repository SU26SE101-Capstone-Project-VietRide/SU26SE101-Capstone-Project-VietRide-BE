# Hướng dẫn FE đồng bộ ghế sau khi thay xe

## 1. Quy tắc nghiệp vụ

- Backend luôn ưu tiên giữ đúng số ghế hiện tại của hành khách. Ví dụ `A1 -> A1`, `A2 -> A2`.
- Nếu xe thay thế không có ghế cũ hoặc ghế đó bị vô hiệu hóa, FE phải cho Admin nhà xe chọn một ghế còn sử dụng được từ kết quả preview.
- Hành khách không duyệt việc đổi ghế. Sau khi Admin xác nhận thay xe, hành khách chỉ nhận thông báo.
- `PENDING_CONFIRM` là trạng thái nhân sự trên xe xác nhận việc chuyển/lên xe thực tế; đây không phải trạng thái chờ hành khách duyệt ghế.
- Không tạo Booking, thanh toán hoặc vé bán mới. `Ticket.SeatNumber` giữ ghế lúc phát hành để đối soát; `Passenger.SeatNumber` là ghế vận hành hiện tại.

## 2. API preview

```http
POST /v1/operator/trips/{tripId}/substitute-vehicle/preview
Authorization: Bearer <operator-admin-token>
Content-Type: application/json
```

```json
{
  "replacementVehicleId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
}
```

Response khi giữ được `A1`, `A2`:

```json
{
  "success": true,
  "data": {
    "tripId": "11111111-1111-4111-8111-111111111111",
    "replacementVehicleId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    "previewToken": "64_KY_TU_HEX",
    "passengers": [
      {
        "bookingId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "passengerId": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1",
        "originalSeatNumber": "A1",
        "proposedSeatNumber": "A1",
        "requiresAdminSelection": false,
        "alternativeSeatNumbers": []
      },
      {
        "bookingId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "passengerId": "cccccccc-cccc-4ccc-8ccc-ccccccccccc2",
        "originalSeatNumber": "A2",
        "proposedSeatNumber": "A2",
        "requiresAdminSelection": false,
        "alternativeSeatNumbers": []
      }
    ],
    "availableSeatNumbers": ["A1", "A2", "A3"]
  }
}
```

Response khi thiếu `A2`:

```json
{
  "success": true,
  "data": {
    "tripId": "11111111-1111-4111-8111-111111111111",
    "replacementVehicleId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    "previewToken": "64_KY_TU_HEX",
    "passengers": [
      {
        "bookingId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "passengerId": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1",
        "originalSeatNumber": "A1",
        "proposedSeatNumber": "A1",
        "requiresAdminSelection": false,
        "alternativeSeatNumbers": []
      },
      {
        "bookingId": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "passengerId": "cccccccc-cccc-4ccc-8ccc-ccccccccccc2",
        "originalSeatNumber": "A2",
        "proposedSeatNumber": null,
        "requiresAdminSelection": true,
        "alternativeSeatNumbers": ["A5", "A10"]
      }
    ],
    "availableSeatNumbers": ["A1", "A5", "A10"]
  }
}
```

FE chỉ hiển thị bộ chọn ghế cho các phần tử có `requiresAdminSelection = true`. Không cho chọn trùng ghế đã được giữ hoặc đã chọn cho hành khách khác.

## 3. API xác nhận thay xe

```http
POST /v1/operator/trips/{tripId}/substitute-vehicle
Authorization: Bearer <operator-admin-token>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Khi giữ được toàn bộ ghế, FE có thể gửi `seatAssignments` rỗng. Khi thiếu `A2` và Admin chọn `A5`, gửi:

```json
{
  "replacementVehicleId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  "estimatedRecoveryDepartureAt": "2026-08-30T09:16:00Z",
  "reason": "Xe cũ gặp sự cố",
  "incidentId": "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
  "notifyPassengers": true,
  "replacementCrew": {
    "driverId": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1",
    "assistantId": "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee2"
  },
  "previewToken": "64_KY_TU_HEX",
  "seatAssignments": [
    {
      "passengerId": "cccccccc-cccc-4ccc-8ccc-ccccccccccc2",
      "newSeatNumber": "A5"
    }
  ]
}
```

FE phải dùng đúng `previewToken` mới nhất. Backend kiểm tra lại trạng thái chuyến, xe, danh sách hành khách, layout ghế, ghế trùng và ghế không khả dụng trước khi tạo chuyến thay thế. Nếu confirm lỗi thì chuyến cũ và Booking chưa bị chuyển.

## 4. Ý nghĩa field và nguồn hiển thị

| Field | Ý nghĩa | FE sử dụng |
|---|---|---|
| `originalSeatNumber` | Ghế vận hành trên chuyến cũ | Hiển thị trong màn preview/đối chiếu |
| `newSeatNumber` | Ghế được gán trên chuyến thay thế | Gửi khi Admin chọn ghế thay thế |
| `Passenger.SeatNumber` | Ghế vận hành hiện tại | Nguồn ghế cho Driver, Assistant, Passenger và Nhà xe |
| `Ticket.SeatNumber` | Ghế tại thời điểm vé được phát hành | Chỉ dùng audit/lịch sử vé, không dùng làm ghế hiện tại |

Các API manifest, quét QR, chi tiết Booking nhà xe và lịch sử Booking hành khách đều trả `seatNumber` theo ghế vận hành hiện tại. FE không tự kết hợp với ghế cũ trong vé.

## 5. Error code

| Error code | Cách xử lý trên FE |
|---|---|
| `REPLACEMENT_SEAT_ASSIGNMENT_REQUIRED` | Quay lại/giữ màn preview và yêu cầu Admin chọn đủ ghế còn thiếu |
| `REPLACEMENT_SEAT_NOT_AVAILABLE` | Báo ghế vừa chọn không còn hợp lệ; gọi preview lại |
| `REPLACEMENT_SEAT_PREVIEW_STALE` | Bỏ token và danh sách lựa chọn cũ; gọi preview lại |
| `REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS` | Chặn xác nhận và yêu cầu chọn xe khác vì không đủ tổng số ghế |
| `TRIP_NOT_SUBSTITUTABLE` | Thông báo trạng thái chuyến đã thay đổi, tải lại chi tiết chuyến |

## 6. Refresh và cache

Sau khi API confirm thành công hoặc client nhận thông báo/event `booking.booking.transferred`:

1. Vô hiệu cache theo `oldTripId`, `newTripId` và `bookingId`.
2. Tải lại seat map của chuyến thay thế.
3. Driver/Assistant tải lại manifest; màn quét QR dùng response mới nhất của API scan.
4. Nhà xe tải lại Booking detail.
5. Passenger tải lại Booking history/detail và thông tin chuyến.

Không lấy `originalSeatNumber` trong event để ghi đè ghế đang hiển thị. Ghế hiện tại phải lấy lại từ response API.

## 7. Checklist regression FE

- [ ] Xe mới có `A1`, `A2`: preview đề xuất `A1`, `A2`; confirm không xuất hiện `A10`.
- [ ] Xe mới thiếu `A2`: chỉ Passenger của `A2` hiện bộ chọn; Admin chọn `A5` và confirm được.
- [ ] Không cho hai Passenger chọn cùng một ghế.
- [ ] Token stale hoặc ghế vừa bị chiếm: FE gọi preview lại.
- [ ] Driver manifest và Assistant manifest hiển thị cùng ghế.
- [ ] Kết quả quét QR hiển thị cùng ghế với manifest.
- [ ] Passenger history/detail hiển thị cùng ghế với Nhà xe Booking detail.
- [ ] Passenger không có nút duyệt đổi ghế.
- [ ] `PENDING_CONFIRM` chỉ xuất hiện trong luồng xác nhận chuyển/lên xe.
