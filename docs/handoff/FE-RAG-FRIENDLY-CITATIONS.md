# Handoff Mobile: nguồn tham khảo thân thiện của RAG

## Thay đổi contract

Sự kiện SSE `done` của `POST /v1/rag/chat` không còn trả `citedChunkIds`. Trường này là UUID
nội bộ của chunk, chỉ phục vụ audit và feedback ở backend.

Payload mới:

```json
{
  "conversationId": "11111111-1111-1111-1111-111111111111",
  "userMessageId": "22222222-2222-2222-2222-222222222222",
  "assistantMessageId": "33333333-3333-3333-3333-333333333333",
  "citations": [
    {
      "title": "Cẩm nang VietRide dành cho tài xế",
      "section": "Hoàn tất chuyến"
    }
  ]
}
```

- `citations` luôn là mảng.
- `title` luôn là chuỗi có nội dung.
- `section` là chuỗi hoặc `null`.
- Backend loại trùng theo cặp `title + section`.
- Backend không trả `chunkId`, `documentId` hoặc identifier nội bộ khác.

## Mobile cần thay đổi

1. Ngừng đọc `done.data.citedChunkIds`.
2. Xóa giao diện `Nguồn: <UUID>`.
3. Đọc `done.data.citations` và hiển thị `title — section` khi người dùng mở danh sách nguồn.
4. Nếu `section=null`, chỉ hiển thị `title`.
5. Nếu `citations=[]`, ẩn toàn bộ khu vực nguồn.
6. Trong giai đoạn Mobile có thể gặp backend cũ, nếu chỉ nhận `citedChunkIds` thì ẩn nguồn hoặc
   hiện số lượng chung; tuyệt đối không hiện giá trị UUID.

Ví dụ trình bày:

```text
Nguồn tham khảo
• Cẩm nang VietRide dành cho tài xế — Hoàn tất chuyến
```

Nội dung trả lời của model không tự thêm mục “Nguồn”; Mobile chỉ dựng khu vực này từ metadata
`citations` của sự kiện `done`.
