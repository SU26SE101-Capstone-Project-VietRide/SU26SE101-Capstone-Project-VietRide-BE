# [FE → BE] Đề xuất: bổ sung lối đi (aisles) vào response seat-map của chuyến

> Ngày gửi: 2026-08-12 · Từ: FE Driver/Assistant app · Trạng thái: **CHỜ BE PHẢN HỒI**

---

## 1. Vấn đề

Màn "Đón khách" của phụ xe vẽ sơ đồ ghế từ **`GET /v1/trips/{tripId}/seat-map`**.
Response hiện tại chỉ có mảng `seats[]` với `row/col` liền nhau:

```jsonc
// Thực tế trả về cho xe BUS_40 (4 cột, col 1..4, không có ô khuyết)
{ "tripId": "...", "vehicleType": "BUS",
  "seats": [{ "seatNumber": "A1", "status": "AVAILABLE", "type": "STANDARD",
              "row": 1, "col": 1, "deck": 1 }, /* ... A2..A40, col 1..4 kín */] }
```

Trong khi đó **dữ liệu lối đi đã tồn tại phía BE** — `seatLayoutJson` của Vehicle
(operator API `GET /v1/operator/vehicles/{id}`) có sẵn:

```jsonc
"seatLayoutJson": { "rows": 10, "cols": 4, "decks": 1,
  "aisles": [{ "afterCol": 2 }],   // ← lối đi giữa xe, FE cần đúng field này
  "seats": [ /* ... */ ] }
```

Nhưng field `aisles` **không được đưa vào `TripSeatMapDto`**, nên app phụ xe vẽ
sơ đồ ghế thành lưới 4 cột dính liền — không thấy hành lang, không giống lòng xe
thật, phụ xe khó đối chiếu ghế trên sơ đồ với ghế vật lý (ghế cửa sổ/ghế lối đi).

## 2. Hiện trạng FE (đã sẵn sàng nhận)

- Type `SeatMapData` phía FE đã khai `aisles?: { afterCol: number }[] | null`
  (optional) — BE trả thêm field là FE render ngay, **không cần đổi gì thêm**,
  thiếu field cũng không vỡ.
- FE đã render được: ô `seatNumber: null` / `type: "AISLE"` / ô khuyết trong lưới
  đều vẽ thành dải hành lang.
- Tạm thời FE đang dùng **heuristic**: lưới 4 cột kín ghế → tự chèn hành lang
  giữa (2|2). Heuristic này **sai với xe khác** (limousine 2|1, giường nằm
  1|1|1 hai lối đi, ghế 2|3…) nên chỉ là giải pháp chờ.

## 3. Đề xuất

Thêm `aisles` từ cùng immutable `Trip.seatLayoutSnapshotJson` đang dùng để dựng ghế
vào response `GET /v1/trips/{tripId}/seat-map`:

```jsonc
{ "tripId": "...", "vehicleType": "BUS",
  "aisles": [{ "afterCol": 2 }],   // NEW — [] nếu layout không khai
  "seats": [ /* giữ nguyên */ ] }
```

- Ngữ nghĩa `afterCol`: hành lang nằm **sau cột `col == afterCol`** (theo đúng
  cách `seatLayoutJson` đang dùng), áp dụng cho mọi deck.
- Nếu về sau có xe lối đi khác nhau theo tầng, có thể mở rộng
  `{ "afterCol": 2, "deck": 1 }` — FE sẽ theo.
- Field mới là **additive** và response luôn trả array → không breaking với client cũ.

## 4. Khi BE xong

FE sẽ gỡ heuristic 2|2 (đánh dấu sẵn trong `ApiSeatGrid`,
`src/features/operations/role-screens.tsx`) và render thuần theo `aisles`.

## 5. Phản hồi BE — 2026-08-13

Trạng thái: **ĐÃ IMPLEMENT**.

- FE gọi đúng endpoint; đây là gap mapping phía BE.
- `aisles` được lấy từ cùng immutable `Trip.seatLayoutSnapshotJson` đang dựng `seats`, không lấy
  độc lập từ template Vehicle hiện tại. Vì vậy sửa layout Vehicle không làm đổi sơ đồ chuyến cũ.
- Response luôn có `aisles`; layout không khai báo trả `[]`, không trả `null` và không bỏ field.
- FE có thể gỡ heuristic 2|2 sau khi nhận bản BE chứa thay đổi này.
