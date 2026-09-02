# Operator Web — Alternative Route geometry read response

**Ngày handoff:** 2026-09-02

**Trạng thái:** `RESOLVED_BE — verified local`

**Phạm vi:** Trip service / Operator Alternative Routes

## Kết luận

Backend bổ sung read-detail endpoint để Operator Web tải lại geometry đã lưu sau reload/remount:

```http
GET /v1/operator/alternative-routes/{alternativeRouteId}
Authorization: Bearer <userAccessToken>
```

FE luôn gọi qua Gateway. Cả `OPERATOR_ADMIN` và `OPERATOR_STAFF` đều được đọc tài nguyên thuộc
operator của mình.

## Response contract

Response `200` dùng ADR 0004 envelope và trả `AlternativeRouteDto` đầy đủ:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "id": "11111111-1111-1111-1111-111111111111",
    "routeId": "22222222-2222-2222-2222-222222222222",
    "name": "Tuyến tránh quốc lộ",
    "description": "Dùng khi tuyến chính bị ùn tắc",
    "destinationStationId": "33333333-3333-3333-3333-333333333333",
    "totalDistanceKm": 40.6,
    "estimatedDurationMinutes": 90,
    "pathPolyline": "encoded-google-polyline-precision-5",
    "isActive": true,
    "stops": [
      {
        "alternativeRouteId": "11111111-1111-1111-1111-111111111111",
        "stopId": "44444444-4444-4444-4444-444444444444",
        "orderIndex": 1,
        "estimatedDurationFromOriginMinutes": 35,
        "distanceFromOriginKm": 15.2,
        "createdAt": "2026-09-02T14:00:00+07:00",
        "updatedAt": "2026-09-02T14:00:00+07:00"
      }
    ],
    "createdAt": "2026-09-02T14:00:00+07:00",
    "updatedAt": "2026-09-02T14:00:00+07:00"
  },
  "meta": {
    "traceId": "request-id",
    "timestamp": "2026-09-02T14:00:00+07:00"
  }
}
```

- `pathPolyline` là đúng chuỗi Google encoded polyline precision-5 đã persist.
- `pathPolyline: null` nghĩa là tuyến chưa có geometry hoặc geometry đã bị invalidated hợp lệ;
  Backend không tự dựng đường thay thế.
- Stops được sort theo `orderIndex ASC`, sau đó `stopId ASC`.
- AlternativeRoute inactive vẫn đọc được với `isActive: false`.
- `message` trong success envelope là optional; FE không được phụ thuộc vào field này.

Errors:

| Trường hợp | HTTP | `error.code` |
|---|---:|---|
| Không tồn tại hoặc thuộc operator khác | 404 | `ROUTE_NOT_FOUND` |
| Role hợp lệ nhưng thiếu operator scope | 403 | `FORBIDDEN` |
| UUID trên path sai định dạng | 404 | Không dispatch vào endpoint |

## Luồng FE cần tích hợp

1. Dùng `GET /v1/operator/routes/{routeId}/alternative-routes` để tải list metadata và ID.
2. Với mỗi alternative route cần vẽ, gọi GET detail theo ID; có thể chạy song song với giới hạn phù
   hợp thay vì tải geometry trong list.
3. Key loading/result/error state bằng `alternativeRouteId`. Khi người dùng đổi lựa chọn nhanh, hủy
   request cũ hoặc bỏ response nếu ID không còn là ID đang cần hiển thị.
4. Nếu detail trả `pathPolyline != null`, decode và vẽ chính chuỗi đó; không chạy Directions lại và
   không nối waypoint để ghi đè đường đã lưu.
5. Chỉ dùng routing suggestion khi detail trả `pathPolyline == null`. Suggestion không trở thành
   source of truth cho tới khi người dùng lưu thành công qua `PUT .../geometry`.
6. Khi nhận `404 ROUTE_NOT_FOUND`, bỏ cache detail tương ứng và refetch list. Khi nhận `403
   FORBIDDEN`, xử lý lại auth/operator scope; không retry bằng mutation endpoint.

`localStorage` hoặc `sessionStorage` chỉ được dùng làm cache UX. Server detail response vẫn là source
of truth sau reload, đổi trình duyệt hoặc khi tài khoản khác cập nhật dữ liệu.

## Những phần Backend giữ nguyên

- List tiếp tục trả `AlternativeRouteListItemDto` không có `pathPolyline`.
- `AlternativeRouteDto`, mapper, repository, entity và cột `path_polyline` không đổi.
- GET detail mới không cần `Idempotency-Key`; không có migration, event, dependency hoặc cache dùng
  chung mới. Các mutation giữ nguyên policy idempotency hiện hữu.
- Gateway prefix `/v1/operator/alternative-routes` hiện hữu tiếp tục forward sang Trip service với
  role `OPERATOR_ADMIN|OPERATOR_STAFF`; không thêm route config mới.
- POST/PATCH/PUT/DELETE và geometry validation hiện hữu không đổi.

## Verification Backend

- Trip unit tests: `19/19` passed.
- Trip PostgreSQL integration tests: `3/3` passed.
- Gateway focused route test: `1/1` passed (`66` test khác bị skip bởi filter).
- Scoped `dotnet format --verify-no-changes` và `git diff --check`: passed.

Chưa có PR, commit hoặc môi trường deploy trong phạm vi handoff local này.
