# Trade-off đã biết của RAG service

Tài liệu này ghi lại các quyết định có chủ đích trong bản capstone/MVP. Các điểm dưới đây không được xem là bug khẩn cấp, nhưng cần được hiểu đúng nếu service được nâng cấp lên production thật.

## `tokenCount` là số từ theo khoảng trắng

Hiện tại chunker đếm token bằng cách tách nội dung theo khoảng trắng. Đây là số từ gần đúng, không phải tokenizer thật của model LLM.

Lý do chấp nhận ở MVP:

- Dễ hiểu, không thêm dependency.
- Đủ ổn với tài liệu ngắn và context budget bảo thủ.

Khi nâng cấp:

- Dùng tokenizer thật của model hoặc hệ số an toàn.
- Nếu vẫn dùng word count, nên nhân hệ số khoảng 1.5-2 lần khi tính context budget.

## Embed và insert chunk đang tuần tự

Ingest hiện embed từng chunk và insert từng chunk trong transaction.

Lý do chấp nhận ở MVP:

- Tài liệu RAG dự kiến nhỏ.
- Logic dễ kiểm thử, dễ debug.

Khi dữ liệu lớn hơn:

- Dùng batch embedding nếu provider hỗ trợ input array.
- Dùng bulk insert để giảm round-trip DB và thời gian transaction.

## TTL lock ingest phải lớn hơn thời gian xử lý tài liệu lớn nhất

Redis lock `RAG_INGEST_LOCK_TTL_SECONDS` cần lớn hơn thời gian xử lý tài liệu lớn nhất trong môi trường vận hành.

Lý do rủi ro hiện tại thấp:

- DB state `ingestStatus` vẫn chặn phần lớn double-processing.
- Redis lock là lớp bảo vệ thêm.

Khi chạy nhiều node hoặc tài liệu lớn:

- Đo thời gian ingest p95/p99.
- Tăng TTL hoặc chuyển sang cơ chế heartbeat/extend lock.

## Retrieved context đang nằm trong system prompt

Context retrieved hiện được ghép vào system prompt, kèm guard rằng context là nội dung không đáng tin cậy và không được làm theo instruction trong tài liệu.

Lý do chấp nhận ở MVP:

- Tài liệu phải được `SYSTEM_ADMIN` duyệt trước khi ingest.
- Prompt có guard chống prompt injection từ retrieved context.

Khi mở rộng nguồn tài liệu:

- Cân nhắc đưa context sang message có trust thấp hơn.
- Tăng kiểm duyệt tài liệu hoặc thêm bước sanitize context.

## `SYSTEM_ADMIN` không tự động thấy chunk theo operator cụ thể

`SYSTEM_ADMIN` có access level rộng hơn, nhưng không tự động truy xuất tài liệu gắn `operator_id` cụ thể nếu request không có scope operator rõ ràng.

Lý do:

- Đây là default-deny cho tenant isolation.
- Tránh admin chat vô tình trộn dữ liệu riêng của nhiều nhà xe.

Cơ chế hiện tại:

- Admin gửi `operatorId` trong body `POST /v1/rag/chat` để chọn scope operator cụ thể.
- Nếu không gửi `operatorId`, admin chỉ thấy tài liệu global (`operator_id IS NULL`).
- Admin audit feedback qua `GET /v1/rag/feedback` (read-only, không POST feedback thay user khác).

## `audienceRoles` kiểm soát role được phép xem tài liệu

Trường `audienceRoles` trên `knowledge_documents` cho phép giới hạn tài liệu chỉ hiển thị cho một số role cụ thể.

Cách hoạt động:

- `audienceRoles` rỗng (`[]`): tài liệu hiển thị cho mọi caller có `accessLevel` phù hợp.
- `audienceRoles` chứa danh sách role (vd `['DRIVER', 'OPERATOR_STAFF']`): chỉ caller có role trong danh sách mới retrieve được tài liệu đó.
- `SYSTEM_ADMIN` bỏ qua filter `audienceRoles` — admin thấy tất cả tài liệu trong tenant scope đã chọn.

Triển khai: GIN index trên `knowledge_documents.audience_roles` được khuyến nghị cho production performance.
