# Phản hồi FE/Mobile — cập nhật enum chất lượng ETA

## Phần cần sửa

FE và Mobile cần bổ sung giá trị `ROUTE_BASED` vào type/enum của hai field chất lượng ETA hiện có:

- `plannedEtaQuality` trong dữ liệu Trip và TripStop.
- `estimateQuality` trong Tracking REST và các sự kiện Socket.IO.

Các giá trị hợp lệ sau khi cập nhật:

```text
TRAFFIC_AWARE
ROUTE_BASED
FALLBACK
```

Nếu UI hiển thị nhãn chất lượng ETA, map `ROUTE_BASED` thành nhãn trung tính:

```text
Ước tính theo tuyến đường
```

## Ví dụ cập nhật type

```ts
type EtaQuality = 'TRAFFIC_AWARE' | 'ROUTE_BASED' | 'FALLBACK';
```

Với `switch-case` hoặc parser enum strict, phải bổ sung nhánh `ROUTE_BASED`. Đồng thời nên có nhánh mặc định
an toàn để giá trị enum mới trong tương lai không làm crash hoặc ẩn toàn bộ nội dung ETA.

## Không cần sửa

- Không đổi endpoint, request hoặc query.
- Không đổi tên field hay cấu trúc response.
- Không đổi tên sự kiện, room hoặc cách kết nối Socket.IO.
- Không cần thêm API key vào FE/Mobile.
- Không cần migration dữ liệu phía client.

## Checklist xác nhận

- [ ] Trip và TripStop decode được `plannedEtaQuality=ROUTE_BASED`.
- [ ] Tracking REST decode được `estimateQuality=ROUTE_BASED`.
- [ ] Tracking Socket.IO decode được `estimateQuality=ROUTE_BASED`.
- [ ] `TRAFFIC_AWARE` và `FALLBACK` vẫn hiển thị bình thường.
- [ ] Giá trị enum chưa biết không làm ứng dụng crash.

