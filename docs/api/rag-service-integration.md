# Tài liệu tích hợp API RAG_SERVICE cho FE/Mobile

## Service: RAG_SERVICE

Tài liệu này mô tả cách FE/mobile tích hợp RAG service qua Gateway. Các endpoint user-facing dùng `Authorization: Bearer <access_token>`. Header `X-Internal-Auth` chỉ dùng nội bộ từ Gateway sang RAG service, FE/mobile không tự truyền header này.

## 1. Danh sách role

| Role | Mô tả theo code |
|---|---|
| `PASSENGER` | Người dùng hành khách; khi chat chỉ truy xuất knowledge `PUBLIC`. |
| `DRIVER` | Role thuộc nhóm operator-scoped; khi chat cần claim `operatorId`. |
| `ASSISTANT` | Role thuộc nhóm operator-scoped trong RAG enum; khi chat cần claim `operatorId`. |
| `OPERATOR_STAFF` | Nhân sự operator; khi chat cần claim `operatorId`. |
| `OPERATOR_ADMIN` | Admin operator; khi chat cần claim `operatorId`. |
| `SYSTEM_ADMIN` | Admin hệ thống; được upload/approve document, audit feedback, quản trị runtime config. |

## 2. Auth cho FE/Mobile

FE/mobile gọi qua Gateway bằng:

```http
Authorization: Bearer <access_token>
```

Gateway sẽ tự ký và bơm header nội bộ xuống RAG:

```http
X-Internal-Auth: Bearer <internal_jwt>
```

FE/mobile không nhập `X-Internal-Auth` trong Swagger và không hardcode internal JWT. Nếu gọi trực tiếp vào RAG service không qua Gateway trong môi trường nội bộ, request vẫn cần `X-Internal-Auth`, nhưng đó không phải luồng tích hợp FE/mobile.

## 3. Danh sách endpoint

| Method | Path | Mô tả | Auth/Role |
|---|---|---|---|
| `GET` | `/health` | Liveness probe, kiểm tra RAG service còn sống. | Public. |
| `GET` | `/ready` | Readiness probe, kiểm tra Prisma, Redis, RabbitMQ, Cloudinary, OpenRouter. | Public. |
| `POST` | `/v1/rag/chat` | Chat với RAG knowledge base bằng SSE streaming. | `PASSENGER`, `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`, `OPERATOR_ADMIN`, `SYSTEM_ADMIN`. |
| `POST` | `/v1/rag/messages/:messageId/feedback` | Tạo/cập nhật feedback cho assistant message. | Caller phải là owner của message/conversation. |
| `GET` | `/v1/rag/feedback` | Admin audit danh sách feedback. | `SYSTEM_ADMIN`. |
| `POST` | `/v1/rag/documents` | Upload knowledge document, auto-approve và request ingest. | `SYSTEM_ADMIN`. |
| `PUT` | `/v1/rag/documents/:documentId/approve` | Approve pending knowledge document để ingest. | `SYSTEM_ADMIN`. |
| `GET` | `/v1/admin/rag-config` | List runtime config keys. | `SYSTEM_ADMIN`. |
| `POST` | `/v1/admin/rag-config/reload` | Reload runtime config cache thủ công. | `SYSTEM_ADMIN`. |
| `GET` | `/v1/admin/rag-config/:key` | Xem chi tiết một config key kèm history. | `SYSTEM_ADMIN`. |
| `PATCH` | `/v1/admin/rag-config/:key` | Cập nhật một config key. | `SYSTEM_ADMIN`. |
| `GET` | `/v1/admin/rag-config/:key/history` | Xem lịch sử thay đổi của một config key. | `SYSTEM_ADMIN`. |
| `POST` | `/v1/admin/rag-config/:key/rollback` | Rollback config key về một history entry. | `SYSTEM_ADMIN`. |

## 4. Endpoint chi tiết

### POST `/v1/rag/chat`

- **Dùng để**: gửi câu hỏi tới RAG và nhận câu trả lời dạng SSE.
- **Header**:

```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

- **Body mẫu**:

```json
{
  "message": "Tôi muốn hỏi chính sách hoàn tiền của VietRide"
}
```

- **Body tiếp tục conversation**:

```json
{
  "conversationId": "11111111-1111-1111-1111-111111111111",
  "message": "Vậy thời gian hoàn tiền là bao lâu?"
}
```

- **Body dành cho `SYSTEM_ADMIN` scope theo operator**:

```json
{
  "message": "Tóm tắt chính sách vận hành của nhà xe này",
  "operatorId": "22222222-2222-2222-2222-222222222222"
}
```

- **Response thành công `200`**:

```text
event: token
data: {"content":"Chính"}

event: done
data: {"conversationId":"11111111-1111-1111-1111-111111111111","userMessageId":"33333333-3333-3333-3333-333333333333","assistantMessageId":"44444444-4444-4444-4444-444444444444","citedChunkIds":[]}
```

- **Lỗi thường gặp**: `VALIDATION_FAILED`, `INSUFFICIENT_ROLE`, `RAG_OPERATOR_SCOPE_REQUIRED`, `RAG_OPERATOR_SCOPE_FORBIDDEN`, `RAG_CONVERSATION_NOT_FOUND`, `RAG_CONVERSATION_FORBIDDEN`, `RAG_RATE_LIMIT_EXCEEDED`, `RAG_PROVIDER_UNAVAILABLE`.

### POST `/v1/rag/messages/:messageId/feedback`

- **Dùng để**: đánh giá câu trả lời của assistant.
- **Header**:

```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

- **Path param**: `messageId` là UUID của assistant message, lấy từ SSE `done.assistantMessageId`.
- **Body mẫu**:

```json
{
  "rating": 1
}
```

`rating` chỉ nhận `1` hoặc `-1`.

- **Response thành công `201`**:

```json
{
  "success": true,
  "statusCode": 201,
  "data": {
    "id": "66666666-6666-6666-6666-666666666666",
    "messageId": "44444444-4444-4444-4444-444444444444",
    "conversationId": "11111111-1111-1111-1111-111111111111",
    "userId": "77777777-7777-7777-7777-777777777777",
    "rating": 1,
    "queryRewritten": null,
    "chunkIds": [],
    "responseLength": 128,
    "createdAt": "2026-07-05T10:00:00.000Z",
    "updatedAt": "2026-07-05T10:00:00.000Z"
  },
  "meta": {
    "traceId": "req_01HZY7B9Q6Y8Y4J4XJ4Z6X9YQ8",
    "timestamp": "2026-07-05T10:00:00.000Z"
  }
}
```

- **Lỗi thường gặp**: `VALIDATION_FAILED`, `INSUFFICIENT_ROLE`, `RAG_FEEDBACK_FORBIDDEN`, `RAG_MESSAGE_NOT_FOUND`, `RAG_FEEDBACK_ASSISTANT_ONLY`.

### GET `/v1/rag/feedback`

- **Dùng để**: `SYSTEM_ADMIN` xem feedback để audit.
- **Header**:

```http
Authorization: Bearer <system_admin_access_token>
```

- **Query params**:
  - `page`: optional, default `1`.
  - `pageSize`: optional, default `20`, max `100`.
  - `sortBy`: optional, `createdAt` hoặc `rating`, default `createdAt`.
  - `sortDir`: optional, `asc` hoặc `desc`, default `desc`.

- **Ví dụ**:

```bash
curl -X GET "https://api.example.com/v1/rag/feedback?page=1&pageSize=20&sortBy=createdAt&sortDir=desc" \
  -H "Authorization: Bearer <system_admin_access_token>"
```

- **Lỗi thường gặp**: `VALIDATION_FAILED`, `INSUFFICIENT_ROLE`, `RAG_ADMIN_REQUIRED`.

### POST `/v1/rag/documents`

- **Dùng để**: `SYSTEM_ADMIN` upload knowledge document. Endpoint này auto-approve và tạo ingest request.
- **Header**:

```http
Authorization: Bearer <system_admin_access_token>
Content-Type: multipart/form-data
```

- **Multipart fields**:
  - `file`: required, binary.
  - `title`: required, string, max 500.
  - `description`: optional, string.
  - `accessLevel`: required, `PUBLIC`, `OPERATOR`, hoặc `ADMIN`.
  - `operatorId`: optional, UUID.
  - `category`: required, `CUSTOMER_SUPPORT`, `OPERATOR_POLICY`, hoặc `PLATFORM_ADMIN`.
  - `documentType`: required, `FAQ`, `POLICY`, `SOP`, `GUIDE`, hoặc `TERMS`.
  - `audienceRoles`: optional, comma-separated string hoặc JSON array string.
  - `language`: optional, chỉ nhận `vi`, default `vi`.

- **Ví dụ**:

```bash
curl -X POST "https://api.example.com/v1/rag/documents" \
  -H "Authorization: Bearer <system_admin_access_token>" \
  -F "file=@./refund-policy.md;type=text/markdown" \
  -F "title=Chính sách hoàn tiền" \
  -F "description=Tài liệu hỗ trợ khách hàng về hoàn tiền" \
  -F "accessLevel=PUBLIC" \
  -F "category=CUSTOMER_SUPPORT" \
  -F "documentType=POLICY" \
  -F "audienceRoles=PASSENGER" \
  -F "language=vi"
```

- **Lỗi thường gặp**: `VALIDATION_FAILED`, `RAG_DOCUMENT_FILE_REQUIRED`, `RAG_DOCUMENT_FILE_INVALID_SIZE`, `RAG_DOCUMENT_FILE_INVALID_TYPE`, `RAG_DOCUMENT_TAXONOMY_INVALID`, `INSUFFICIENT_ROLE`, `SERVICE_UNAVAILABLE`.

### PUT `/v1/rag/documents/:documentId/approve`

- **Dùng để**: approve document đang `PENDING_REVIEW`.
- **Header**:

```http
Authorization: Bearer <system_admin_access_token>
```

- **Path param**: `documentId` là UUID.
- **Lỗi thường gặp**: `VALIDATION_FAILED`, `INSUFFICIENT_ROLE`, `RAG_DOCUMENT_NOT_FOUND`, `RAG_DOCUMENT_STATUS_CONFLICT`.

### `/v1/admin/rag-config/*`

- **Dùng để**: `SYSTEM_ADMIN` quản trị runtime config của RAG, ví dụ prompt, text fallback, allowed file extensions, max file size.
- **Header**:

```http
Authorization: Bearer <system_admin_access_token>
```

| Method | Path | Body |
|---|---|---|
| `GET` | `/v1/admin/rag-config` | Không có. |
| `POST` | `/v1/admin/rag-config/reload` | Không có. |
| `GET` | `/v1/admin/rag-config/:key` | Không có. |
| `PATCH` | `/v1/admin/rag-config/:key` | `{ "value": "...", "reason": "..." }` |
| `GET` | `/v1/admin/rag-config/:key/history` | Không có. |
| `POST` | `/v1/admin/rag-config/:key/rollback` | `{ "historyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }` |

Ví dụ update config:

```bash
curl -X PATCH "https://api.example.com/v1/admin/rag-config/chat.no_context_text" \
  -H "Authorization: Bearer <system_admin_access_token>" \
  -H "Content-Type: application/json" \
  -d '{"value":"Không tìm thấy ngữ cảnh phù hợp.","reason":"Cập nhật nội dung hiển thị"}'
```

## 5. Luồng tích hợp

1. User gửi chat bằng `POST /v1/rag/chat`.
2. Client đọc SSE `token` để render streaming.
3. Khi nhận `done`, lưu `conversationId` và `assistantMessageId`.
4. Nếu user đánh giá câu trả lời, gọi `POST /v1/rag/messages/:assistantMessageId/feedback`.
5. Admin upload tài liệu bằng `POST /v1/rag/documents`; ingest worker xử lý async trước khi tài liệu được retrieve trong chat.

## 6. Khác biệt Web FE vs Mobile

Không có khác biệt, cả 2 nền tảng gọi giống nhau qua Gateway:

```http
Authorization: Bearer <access_token>
```

Không tìm thấy code yêu cầu `device-id`, cookie riêng cho web, hoặc endpoint riêng cho mobile trong RAG service.
