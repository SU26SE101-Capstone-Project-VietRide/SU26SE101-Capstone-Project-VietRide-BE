# Handoff FE/Mobile — chất lượng ETA khi chuyển sang Goong

## Kết luận ngắn

FE và Mobile **không đổi endpoint, request hoặc cấu trúc payload**. Thay đổi client duy nhất là chấp
nhận thêm enum additive `ROUTE_BASED` ở các field chất lượng ETA hiện có.

Backend không trả tên provider, API key, snap metadata hoặc traffic metadata. Việc Google Maps SDK
dùng để hiển thị bản đồ và đăng nhập Google OAuth không nằm trong migration này.

## Contract cần hỗ trợ

| Field hiện có | Giá trị hợp lệ sau Day 51 | Ý nghĩa hiển thị |
|---|---|---|
| `plannedEtaQuality` trong Trip detail | `TRAFFIC_AWARE`, `ROUTE_BASED`, `FALLBACK` | Chất lượng planned ETA của Trip/TripStop. |
| `estimateQuality` trong Tracking REST/Socket | `TRAFFIC_AWARE`, `ROUTE_BASED`, `FALLBACK` | Chất lượng ETA realtime cho stop/station/Shuttle. |

Mapping backend:

- `TRAFFIC_AWARE`: dữ liệu Trip lịch sử đã tính bởi Google Routes trước Day 51.
- `ROUTE_BASED`: ETA hiện hành được tính theo Goong Directions; Goong không cung cấp traffic-aware
  departure time trong contract Day 51.
- `FALLBACK`: Route baseline hoặc Local fallback.

Client không được suy luận provider từ enum hoặc hiển thị chữ “Google/Goong” cho người dùng. Đây là
phân loại chất lượng, không phải thông tin nhà cung cấp.

## Ví dụ payload

Trip detail giữ nguyên shape, chỉ có thể nhận giá trị mới:

```json
{
  "tripId": "11111111-1111-4111-8111-111111111111",
  "estimatedArrivalTime": "2026-08-25T18:30:00+07:00",
  "plannedEtaQuality": "ROUTE_BASED"
}
```

Tracking ETA cũng giữ nguyên shape:

```json
{
  "targetKind": "STOP",
  "stopId": "22222222-2222-4222-8222-222222222222",
  "etaMinutes": 25,
  "estimatedArrivalTime": "2026-08-25T17:25:00+07:00",
  "distanceMeters": 18500,
  "estimateQuality": "ROUTE_BASED"
}
```

Các payload Shuttle, `eta:update`, `eta:batch:update` và REST context không thêm field provider, không
đổi tên field và không đổi điều kiện hiển thị hiện hành.

## Việc FE/Mobile cần làm

1. Mở rộng type/enum của cả `plannedEtaQuality` và `estimateQuality` để nhận `ROUTE_BASED`.
2. Map `ROUTE_BASED` vào nhãn trung tính như “Ước tính theo tuyến đường”.
3. Giữ cách hiển thị hiện tại cho `TRAFFIC_AWARE` và `FALLBACK`.
4. Có default branch an toàn cho enum tương lai: hiển thị nhãn ETA trung tính, không crash, không ẩn
   toàn bộ Trip/Tracking card.
5. Không thêm Goong key vào web/mobile bundle và không gọi Goong trực tiếp từ client.

## Không cần thay đổi

- Không đổi URL Gateway hoặc endpoint Trip/Tracking.
- Không đổi query, body, pagination, room Socket.IO hoặc event name.
- Không đổi logic Shuttle hay payload `include=shuttle`.
- Không đổi Google Maps display key, bản đồ nền, marker/polyline hoặc Google OAuth chỉ vì backend đổi
  routing provider.
- Không cần migration dữ liệu phía client.

## Checklist xác nhận client

- [ ] Decode được `ROUTE_BASED` ở Trip detail.
- [ ] Decode được `ROUTE_BASED` ở Tracking REST và Socket.IO.
- [ ] `TRAFFIC_AWARE` lịch sử vẫn hiển thị bình thường.
- [ ] `FALLBACK` vẫn hiển thị bình thường.
- [ ] Enum chưa biết không gây crash.
- [ ] Không có provider/key trong analytics, crash report hoặc UI.
- [ ] Không thay đổi endpoint/payload ngoài enum additive nêu trên.
