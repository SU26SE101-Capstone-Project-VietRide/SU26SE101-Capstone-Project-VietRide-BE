# Bộ định tuyến workflow AI của VietRide

Tài liệu này là hợp đồng vận hành mặc định cho agent làm việc trong backend VietRide. Agent phải
tự áp dụng nó cho task có liên quan đến source code; người dùng không cần phải gõ riêng
`codegraph`, `subagent`, `explorer`, `worker` hoặc `reviewer` để kích hoạt bước phù hợp.

## Luồng mặc định

```text
Yêu cầu
  -> đọc AGENTS.md và stack guide phù hợp
  -> preflight bằng Codegraph nếu task chạm source
  -> phân loại phạm vi và quyết định delegate
  -> plan/review trước
  -> chờ ok/go trước implementation
  -> worker -> targeted verification -> reviewer
  -> audit khi người dùng yêu cầu đóng ngày
```

## Quy tắc Codegraph

- Câu hỏi về kiến trúc, flow, bug hoặc vùng code: gọi `codegraph_explore` trước.
- Đã biết symbol hoặc file cụ thể: gọi `codegraph_node`.
- Hỏi caller, callback registration hoặc blast radius: gọi `codegraph_callers`.
- Chỉ đọc raw source sau Codegraph. Markdown, JSON, TOML, YAML và instruction file có thể đọc
  trực tiếp.
- Nếu project chưa được index hoặc Codegraph không khả dụng, báo rõ giới hạn rồi dùng fallback
  an toàn; không tuyên bố đã dùng Codegraph.

## Quy tắc delegate

| Phạm vi | Delegate mặc định |
|---|---|
| Docs/config-only hoặc thay đổi nhỏ một file đã rõ | Không delegate |
| Flow chưa rõ, bug có blast radius khó đoán | Một `explorer` với câu hỏi cụ thể |
| Hai stack hoặc các service độc lập | Một agent cho mỗi scope không chồng lấn, tối đa hai agent song song |
| Task implementation đã được duyệt | Worker đúng stack, sau đó reviewer độc lập |
| Lập kế hoạch một ngày | `manager` rồi `reviewer` PLAN-REVIEW |

Agent cha chịu trách nhiệm giữ scope, ghép kết quả, kiểm tra diff và hand-back. Không spawn agent
chỉ để đạt số lượng; không để hai agent sửa cùng một path.

## Handoff tối thiểu

Mọi handoff phải có:

- câu hỏi hoặc task id cụ thể;
- kết quả Codegraph và các symbol/file liên quan;
- owned files, forbidden scope và auto-expand scope;
- acceptance criteria và các invariant liên quan;
- lệnh targeted verification chính xác;
- baseline/diff boundary khi review.

Gate `ok/go`, review, audit và quy tắc không tự push/PR vẫn giữ nguyên theo `AGENTS.md`.
