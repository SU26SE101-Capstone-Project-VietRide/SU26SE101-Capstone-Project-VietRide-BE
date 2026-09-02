# Phản hồi FE — hiển thị cốp cũ sau khi chuyển hàng sang xe thay thế

## Kết luận

Backend đã giữ nguyên API hiện tại và bổ sung hai trường lịch sử để FE vẫn hiển thị được lượng hàng từng nằm trong cốp của chuyến cũ sau khi hàng đã được chuyển sang xe thay thế.

FE cần đọc thêm:

- `historicalLoadedWeightKg`: tổng khối lượng hàng từng được chất lên chuyến, tính bằng kg.
- `historicalLoadedVolumeM3`: tổng thể tích hàng từng được chất lên chuyến, tính bằng m³.

Đây là thay đổi cộng thêm, không làm thay đổi hoặc xóa các trường cũ.

## API

```http
GET /v1/operator/trips/{tripId}/cargo-capacity
```

Quyền truy cập: `OPERATOR_ADMIN` hoặc `OPERATOR_STAFF` thuộc đúng nhà xe của chuyến.

Không có query parameter hoặc request body mới.

## Response mẫu của chuyến cũ sau khi chuyển hàng

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "tripId": "uuid-trip-cu",
    "reservedWeightKg": 0,
    "reservedVolumeM3": 0,
    "loadedWeightKg": 0,
    "loadedVolumeM3": 0,
    "maxCargoWeightKg": 500,
    "maxCargoVolumeM3": 5,
    "availableWeightKg": 500,
    "availableVolumeM3": 5,
    "percentFull": 0,
    "historicalLoadedWeightKg": 12.5,
    "historicalLoadedVolumeM3": 0.2
  },
  "meta": {
    "traceId": "request-id",
    "timestamp": "2026-09-03T04:00:00Z"
  }
}
```

Trong ví dụ này:

- `loadedWeightKg = 0` và `loadedVolumeM3 = 0`: hiện tại cốp chuyến cũ không còn hàng.
- `historicalLoadedWeightKg = 12.5` và `historicalLoadedVolumeM3 = 0.2`: trước đó cốp chuyến cũ đã chở lượng hàng này.

## Cách FE sử dụng

- Thông tin cốp hiện tại tiếp tục dùng `loadedWeightKg`, `loadedVolumeM3`, `availableWeightKg`, `availableVolumeM3` và `percentFull`.
- Khi cần hiển thị cốp cũ sau chuyển xe, dùng `historicalLoadedWeightKg` và `historicalLoadedVolumeM3`.
- Không dùng hai trường lịch sử để tính dung lượng còn trống hoặc phần trăm đầy hiện tại.
- Hàng mới chỉ được giữ chỗ nhưng chưa từng được chất lên xe không được tính vào hai trường lịch sử.

## Kết quả theo từng thời điểm

| Trạng thái | `loadedWeightKg` | `historicalLoadedWeightKg` |
|---|---:|---:|
| Chuyến cũ sau khi chuyển 12.5 kg sang xe mới | `0` | `12.5` |
| Chuyến mới đang chở 12.5 kg | `12.5` | `12.5` |
| Chuyến mới sau khi dỡ 12.5 kg | `0` | `12.5` |

Hai trường thể tích có hành vi tương tự.

## Lỗi giữ nguyên

- `403 FORBIDDEN`: chuyến không thuộc nhà xe đang đăng nhập.
- `404 TRIP_NOT_FOUND`: không tìm thấy chuyến.

## Phạm vi thay đổi

- Không có endpoint mới.
- Không thay đổi luồng scan, chất hàng, dỡ hàng hoặc chuyển hàng.
- Không yêu cầu FE thay đổi request hiện tại.
- FE chỉ cần bổ sung mapping hai trường lịch sử nếu muốn hiển thị cốp cũ.
