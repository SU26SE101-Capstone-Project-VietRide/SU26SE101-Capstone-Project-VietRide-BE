# Bộ tri thức trò chuyện VietRide

Thư mục này chứa một nguồn canonical để kiểm chứng và năm tài liệu duy nhất được phép upload cho
trợ lý theo vai trò. Không upload đồng thời tài liệu canonical, demo hoặc CSKH legacy vì các chunk
trùng/chồng phạm vi có thể làm giảm chất lượng truy xuất.

## Bộ canonical được Git theo dõi

Năm tài liệu role-specific trong bảng dưới đây, manifest này, coverage matrix và regression JSON
là bộ canonical. Mọi thay đổi nghiệp vụ phải cập nhật đúng tài liệu role bị ảnh hưởng và case
regression tương ứng.

Khi behavior thay đổi, kiểm tra theo thứ tự: runtime implementation và test, API contract,
technical context, schema/migration, rồi mới cập nhật bộ canonical.
Phần chưa được source xác nhận phải được ghi là chưa đủ thông tin, không chuyển thành lời hứa cho
người dùng.

`vietride-user-chat-knowledge-base.md` là bản audit local lịch sử đang được `.gitignore`; có thể
dùng để tham khảo nhưng không phải completion gate và không được upload production.

## Năm tài liệu upload canonical

| File | Access | Category | Audience roles |
|---|---|---|---|
| `vietride-passenger-chat-knowledge-base.md` | `PUBLIC` | `CUSTOMER_SUPPORT` | `PASSENGER` |
| `vietride-driver-chat-knowledge-base.md` | `OPERATOR` | `OPERATOR_POLICY` | `DRIVER` |
| `vietride-assistant-chat-knowledge-base.md` | `OPERATOR` | `OPERATOR_POLICY` | `ASSISTANT` |
| `vietride-operator-chat-knowledge-base.md` | `OPERATOR` | `OPERATOR_POLICY` | `OPERATOR_STAFF`, `OPERATOR_ADMIN` |
| `vietride-system-admin-chat-knowledge-base.md` | `ADMIN` | `PLATFORM_ADMIN` | `SYSTEM_ADMIN` |

`OPERATOR_STAFF` và `OPERATOR_ADMIN` là một nhóm tri thức duy nhất có tên “Nhà xe”. Nội dung
không chia quyền, không hỏi lại enum role và không tạo hai bản tài liệu khác nhau.

Để dùng chung toàn nền tảng, để trống Operator. Chỉ gắn `operatorId` khi tài liệu được viết riêng
cho đúng một nhà xe. Document type là `GUIDE`, language là `vi`.

## Tài liệu không upload cùng bộ canonical

- `vietride-cskh-role-knowledge-base.txt`: legacy.
- `vietride-*-demo-knowledge-base.txt`: dữ liệu demo.
- `vietride-user-chat-knowledge-base.md`: nguồn audit local, không được Git theo dõi.

Trước rollout, archive các phiên bản cũ đang `APPROVED`, upload năm file trên, chờ ingest
`COMPLETED`, rồi mới chạy regression theo role. Không xóa bản cũ khỏi database nếu cần audit.

## Citation công khai

Chunk UUID được lưu nội bộ trong `RagMessage.citedChunkIds` để audit và feedback nhưng không được
trả cho client. Sự kiện SSE `done` chỉ trả `citations` gồm `title` và nullable `section`. Mobile
không render UUID, document ID hoặc mục `Nguồn: <mã>`. Nếu chưa nhận contract mới, Mobile ẩn nguồn
hoặc chỉ hiện số lượng nguồn tham khảo.

## Kiểm tra

Chạy:

```powershell
node scripts/validate-rag-knowledge-base.mjs
npx jest --config apps/rag/jest.config.cts apps/rag/src/chat/chat.service.spec.ts --runInBand
npx jest --config apps/rag/jest.e2e.config.cts apps/rag/src/chat/chat.e2e-spec.ts --runInBand
```

Validator kiểm metadata, phạm vi năm nhóm, mô hình Nhà xe gộp, coverage matrix, câu hỏi gợi ý,
ít nhất 185 biến thể regression và quy tắc không lộ identifier nội bộ.
