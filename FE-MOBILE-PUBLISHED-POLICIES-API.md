# FE/Mobile Handoff — API đọc Policy dành cho người dùng

## 1. Trạng thái

- Backend đã triển khai tại commit `86ad4aaf`.
- FE và Mobile gọi qua API Gateway, không gọi trực tiếp RAG Service.
- Không cần thay đổi database hoặc gửi `X-Internal-Auth` từ client.

## 2. Quyền truy cập

- Yêu cầu access token hợp lệ:

```http
Authorization: Bearer <access_token>
```

- Các role `PASSENGER`, `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`, `OPERATOR_ADMIN` và `SYSTEM_ADMIN` đều đọc được.
- Passenger chưa hoàn tất số điện thoại vẫn đọc được Policy.
- Anonymous hoặc token không hợp lệ nhận `401 AUTH_TOKEN_INVALID` tại Gateway.

## 3. Danh sách Policy

```http
GET /v1/policies
```

### Query

| Field | Bắt buộc | Mô tả |
|---|---:|---|
| `operatorId` | Không | UUID nhà xe. Không truyền thì chỉ lấy Policy nền tảng; có truyền thì lấy chung Policy nền tảng và Policy của nhà xe đó. |
| `category` | Không | Lọc chính xác theo category, ví dụ `REFUND`, `LUGGAGE`. |
| `search` | Không | Tìm trong title, description, content và category. |
| `page` | Không | Mặc định `1`, tối thiểu `1`. |
| `pageSize` | Không | Mặc định `20`, tối đa `100`. |
| `sortBy` | Không | `updatedAt`, `createdAt`, `title` hoặc `version`; mặc định `updatedAt`. |
| `sortDir` | Không | `asc` hoặc `desc`; mặc định `desc`. |

Query không nằm trong allow-list hoặc sai định dạng trả `422 VALIDATION_ERROR`.

### Ví dụ chỉ lấy Policy nền tảng

```http
GET /v1/policies?page=1&pageSize=20
Authorization: Bearer <access_token>
```

### Ví dụ lấy Policy nền tảng và Policy nhà xe

```http
GET /v1/policies?operatorId=44444444-4444-4444-8444-444444444444&page=1&pageSize=20
Authorization: Bearer <access_token>
```

`operatorId` là ID nhà xe, không phải user ID của tài xế. Backend không có Policy riêng cho từng tài xế.

### Response `200`

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "11111111-1111-4111-8111-111111111111",
        "operatorId": null,
        "title": "Chính sách hoàn vé",
        "description": "Quy định hoàn vé áp dụng toàn hệ thống",
        "content": "Nội dung Policy",
        "category": "REFUND",
        "version": 1,
        "createdAt": "2026-08-15T10:00:00.000Z",
        "updatedAt": "2026-08-15T10:00:00.000Z"
      },
      {
        "id": "22222222-2222-4222-8222-222222222222",
        "operatorId": "44444444-4444-4444-8444-444444444444",
        "title": "Quy định hành lý nhà xe",
        "description": "Quy định dành cho hành khách của nhà xe",
        "content": "Mỗi hành khách được mang tối đa 20 kg hành lý.",
        "category": "LUGGAGE",
        "version": 2,
        "createdAt": "2026-08-15T10:00:00.000Z",
        "updatedAt": "2026-08-15T11:00:00.000Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 2,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  },
  "meta": {
    "traceId": "request-id",
    "timestamp": "2026-08-15T11:00:00.000Z"
  }
}
```

Phân biệt nguồn Policy bằng `operatorId`:

- `operatorId = null`: Policy nền tảng.
- `operatorId = <uuid>`: Policy của nhà xe tương ứng.

Backend chỉ trả Policy `FOR_USER`, đang active và chưa soft-delete. Response consumer không có `policyType`, `active`, `createdBy`, audit log hoặc row version.

## 4. Chi tiết Policy

```http
GET /v1/policies/{policyId}
Authorization: Bearer <access_token>
```

Response `200` dùng cùng item shape trong API danh sách, không có wrapper phân trang.

Backend trả `404 POLICY_NOT_FOUND` nếu Policy:

- không tồn tại;
- thuộc loại `FOR_OPERATOR`;
- đang inactive;
- đã soft-delete.

## 5. Cách tích hợp đề xuất

1. Màn hình điều khoản chung gọi `GET /v1/policies` không truyền `operatorId`.
2. Màn hình chi tiết chuyến/nhà xe truyền `operatorId` của nhà xe để lấy cả Policy nền tảng và Policy nhà xe.
3. FE/Mobile có thể nhóm item theo `operatorId` để hiển thị hai section “Chính sách nền tảng” và “Chính sách nhà xe”.
4. Khi user chọn một Policy, gọi endpoint detail hoặc dùng luôn `content` đã có trong list.
5. Không gọi `/v1/admin/policies` hoặc `/v1/operator/policies`; đó là API quản trị.

## 6. Ngoài phạm vi hiện tại

- Backend chưa yêu cầu Passenger bấm đồng ý Policy.
- Booking chưa lưu `policyId`, `version` hoặc thời điểm chấp nhận.
- Policy dạng text không tự động tính phí hủy, hoàn tiền hoặc giới hạn hành lý.
- Policy chưa được tự động đưa vào chatbot RAG.

## 7. Checklist FE/Mobile

- [ ] Gửi access token qua `Authorization`.
- [ ] Dùng đúng `operatorId` của nhà xe, không dùng driver user ID.
- [ ] Xử lý phân trang theo `data` của ApiResponse envelope.
- [ ] Phân biệt Policy nền tảng và nhà xe bằng `operatorId`.
- [ ] Xử lý `401`, `404` và `422` theo error envelope.
- [ ] Không phụ thuộc vào các field quản trị không có trong consumer response.
