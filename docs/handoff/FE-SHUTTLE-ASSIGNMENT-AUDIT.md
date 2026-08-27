# Handoff FE — Audit người gán xe/tài xế Shuttle

## Mục tiêu

Thẻ Shuttle cần hiển thị đúng người thực hiện lần gán hoặc đổi xe/tài xế gần nhất.
Trang chi tiết có thể mở lịch sử điều phối đầy đủ. Backend không triển khai thay đổi FE trong
task này.

## API danh sách

`GET /v1/operator/shuttle-trips` giữ nguyên các trường cũ, bao gồm `createdBy`, và bổ sung
`latestAssignment` (nullable):

```json
{
  "shuttleTripId": "uuid",
  "createdBy": "uuid",
  "driver": { "id": "uuid", "displayName": "Lê Văn An" },
  "vehicle": { "id": "uuid", "licensePlate": "51A-123.45" },
  "latestAssignment": {
    "action": "REASSIGNED",
    "assignedAt": "2026-08-27T15:30:00+07:00",
    "assignedBy": {
      "userId": "uuid",
      "displayName": "Trần Minh Bình",
      "role": "OPERATOR_ADMIN"
    },
    "reason": "Xe cũ gặp sự cố"
  }
}
```

`action` chỉ có `INITIAL_ASSIGNED` và `REASSIGNED`. `assignedAt` là thời điểm thao tác; FE format
theo múi giờ `Asia/Ho_Chi_Minh`.

### Trạng thái legacy

ShuttleTrip cũ không có bản ghi audit sẽ trả `latestAssignment: null` và không có item trong
history. Khi đó hiển thị đúng chuỗi **“Chưa có dữ liệu người gán”**. Tuyệt đối không dùng
`createdBy` làm fallback, vì `createdBy` chỉ là người tạo ShuttleTrip và không chứng minh người
đã gán xe/tài xế hiện tại.

## API lịch sử điều phối

`GET /v1/operator/shuttle-trips/{shuttleTripId}/assignment-history?page=1&pageSize=20`

Quyền: `OPERATOR_ADMIN`, `OPERATOR_STAFF`. Tenant lấy từ JWT. ShuttleTrip không tồn tại hoặc
khác tenant đều trả `404 SHUTTLE_TRIP_NOT_FOUND`. Kết quả là `PagedResult`, sắp xếp mới nhất
trước (`assignedAt DESC`), hỗ trợ tải thêm bằng `page`/`pageSize` (tối đa 100).

Ví dụ một item:

```json
{
  "id": "uuid",
  "action": "REASSIGNED",
  "assignedAt": "2026-08-27T15:30:00+07:00",
  "assignedBy": {
    "userId": "uuid",
    "displayName": "Trần Minh Bình",
    "role": "OPERATOR_ADMIN"
  },
  "reason": "Xe cũ gặp sự cố",
  "previousDriver": { "id": "uuid", "displayName": "Lê Văn An" },
  "currentDriver": { "id": "uuid", "displayName": "Phạm Quốc Huy" },
  "previousVehicle": { "id": "uuid", "licensePlate": "51A-123.45" },
  "currentVehicle": { "id": "uuid", "licensePlate": "51B-678.90" }
}
```

Với `INITIAL_ASSIGNED`, `previousDriver` và `previousVehicle` là `null`; `reason` thường là
`null`. Với `REASSIGNED`, `reason` luôn có giá trị; các snapshot before/after là dữ liệu cố định
tại thời điểm thao tác, không lookup lại để sửa lịch sử.

## Quy tắc hiển thị

- `INITIAL_ASSIGNED`: **“Gán bởi {assignedBy.displayName} · {giờ}”**.
- `REASSIGNED`: **“Đổi xe/tài xế bởi {assignedBy.displayName} · {giờ}”**; card rút gọn lý do,
  drawer chi tiết hiển thị đầy đủ.
- `latestAssignment == null`: **“Chưa có dữ liệu người gán”**.
- Drawer **“Lịch sử điều phối”** lazy-load endpoint history khi mở, hiển thị mới nhất trước và
  có nút tải thêm khi `hasNextPage = true`.
- Nếu tên actor thiếu do dữ liệu lịch sử không hợp lệ, giữ `userId` để nhận diện; không đổi
  sang `createdBy`.

## Sau mutation

`PATCH /v1/operator/shuttle-trips/{shuttleTripId}/assignment` vẫn giữ request/response hiện tại
và bắt buộc `Idempotency-Key`. Backend chỉ ghi audit/event khi xe hoặc tài xế thực sự thay đổi;
request lặp cùng assignment không tạo dòng mới. Sau khi mutation thành công, FE invalidate/refetch
cache card danh sách và cache history của ShuttleTrip. Gateway không cần route mới vì prefix GET
`/v1/operator/shuttle-trips` đã proxy toàn bộ subpath sang Trip.
