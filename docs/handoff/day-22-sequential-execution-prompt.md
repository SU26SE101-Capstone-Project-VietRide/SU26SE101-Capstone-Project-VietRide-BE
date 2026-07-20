# Day 22 — Sequential full-day execution prompt

## Usage

Paste everything under **Prompt** into a new orchestrator session from the VietRide repository root.
The orchestrator must discover the executable task IDs and dispatch order from
`docs/handoff/day-22-plan.md`; the task list is deliberately not duplicated here.

## Prompt

```text
Chạy tuần tự toàn bộ executable tasks của Day 22 từ `docs/handoff/day-22-plan.md`, theo đúng mục `Dispatch order` và `Progress tracker` trong plan.

Đây là human-approved sequential batch mode. Dùng đúng workflow của `/implement-task` cho từng task, nhưng tôi explicit override guardrail "one task per invocation" để chạy lần lượt toàn bộ task còn phải thực hiện trong cùng session.

Không chạy song song, kể cả khi plan đánh dấu một số task parallel-safe. Không dùng git worktree. Không auto-run `/audit-day 22`. Không push. Không tạo PR.

## Phase 0 — Preflight và lập run manifest

Trước khi dispatch bất kỳ worker nào:

1. Đọc đầy đủ:
   - `AGENTS.md` và mọi nested `AGENTS.md` áp dụng cho owned files.
   - `.agents/skills/implement-task/SKILL.md`.
   - `docs/handoff/day-22-plan.md`.
   - Prior-day checklist được plan tham chiếu, nếu tồn tại.

2. Xác nhận plan đủ điều kiện triển khai:
   - Plan đã qua `APPROVE PLAN`.
   - Không còn unresolved Open questions.
   - Không có SOT conflict hoặc blocker được ghi ngay trong plan.
   - Nếu một trong các điều kiện này không đạt: DỪNG và báo tôi; không tự sửa plan hoặc đoán quyết định.

3. Tự tạo run manifest từ plan, không dùng danh sách task hard-code từ prompt:
   - Lấy task IDs và thứ tự tuyệt đối từ `Dispatch order`.
   - Đối chiếu `Progress tracker`.
   - Bỏ qua task được plan đánh dấu immutable/already-done hoặc tracker đã xác nhận done, trừ khi plan yêu cầu verify lại.
   - Không bỏ qua task todo/in-progress/done-with-carryover nếu dependency hoặc acceptance của Day hiện tại vẫn yêu cầu nó.
   - Với mỗi task, ghi ra: task ID, implement agent, review agent, skill, depends on, owned files, forbidden scope và verification bắt buộc.
   - Nếu `Dispatch order`, task block và `Progress tracker` mâu thuẫn: DỪNG và báo tôi.

4. Trước khi giao worker, expand mọi shorthand trong owned files như `.../`, `**`, `{A,B,C}`, “new files under...”, “affected tests” thành repo-relative paths cụ thể dựa trên repo state hiện tại. Không mở rộng sang concern ngoài task.

5. Inspect baseline repo state:
   - `git status --short`
   - `git diff --stat`
   - `git diff --check`
   - `git log --oneline -10`
   Ghi nhận và bảo toàn mọi unrelated user change. Không stage, sửa hoặc hoàn nguyên chúng.

Sau preflight, báo run manifest ngắn gọn rồi bắt đầu task đầu tiên ngay; không dừng để xin xác nhận lại nếu plan đã APPROVE và không có blocker.

## Workflow bắt buộc cho từng task

Thực hiện từng task theo run manifest, tuyệt đối tuần tự:

1. Đọc lại nguyên block của task trong `day-22-plan.md`, gồm:
   - implement agent
   - review agent
   - skill
   - owned files
   - forbidden scope
   - depends on
   - invariant flags
   - acceptance
   - source citations

2. Đọc chính xác các SOT sections và existing-code patterns mà task citations yêu cầu trước khi sửa code. Không invent column, enum, endpoint, error code, event, DTO field hoặc business rule.

3. Dispatch đúng implement agent, restricted vào owned files của task. Prompt cho worker phải chứa nguyên acceptance/invariants/citations của task và expanded write set. Worker không được commit.

4. Sau implementation, inspect resulting diff và dispatch đúng review agent trên diff đó. Reviewer phải đối chiếu:
   - acceptance của task
   - invariant flags
   - forbidden scope
   - source citations
   - hard invariants trong `AGENTS.md`

5. Nếu reviewer `REQUEST CHANGES`:
   - Dispatch fresh implement agent cùng loại để patch đúng findings.
   - Vẫn restricted trong owned files của task.
   - Review lại sau patch.
   - Lặp đến khi `APPROVE`; không giới hạn patch round khi findings là lỗi implementation có thể sửa trong approved scope.

6. Nếu implement/review agent fail do timeout, session limit hoặc tool error:
   - Retry bằng fresh agent đúng 1 lần cho lần failure đó.
   - Nếu retry vẫn fail: DỪNG và báo tôi, kèm task ID, bước fail và state của working tree.

7. Khi reviewer `APPROVE`, chạy verification theo acceptance và mức rủi ro của task. Tối thiểu:
   - Targeted tests cho code vừa đổi.
   - Build/lint/format của solution/project bị ảnh hưởng.
   - `git diff --check`.
   - Migration task: generate bằng design-time factory, apply, rollback/Down, reapply và kiểm tra model drift theo plan.
   - Gateway/NestJS task: targeted specs cùng Nx test/lint/build phù hợp CI.
   - Contract/docs-only task: kiểm tra citations, registry/changelog/version consistency, JSON/Markdown parse nếu liên quan.
   - Postman/Newman task: chạy đúng local harness được plan chỉ định và xác nhận cleanup cả success/failure path.

8. Nếu verification fail do implementation và vẫn trong owned scope: patch → review lại → verify lại. Không commit khi reviewer chưa APPROVE hoặc verification bắt buộc chưa green.

9. Khi task đã APPROVE và verification green:
   - Update đúng row của task trong `Progress tracker` của `day-22-plan.md`.
   - Inspect `git status`, `git diff`, `git diff --check`, `git log --oneline -10`.
   - Stage chỉ intended files của task và tracker update.
   - Commit riêng: đúng 1 commit cho task.
   - Không có `Co-Authored-By`.
   - Không dùng `--no-verify`.
   - Không amend/rewrite commit trước trừ khi tôi yêu cầu.
   - Chỉ chuyển sang task kế tiếp sau khi commit thành công.

10. Shared files phải được xử lý cộng dồn:
    - Task sau chỉ append/extend phần thuộc scope của mình.
    - Không rewrite hoặc regress code đã APPROVE/commit từ task trước.
    - Nếu task sau cần thay đổi immutable contract/baseline của task trước: coi là contract drift và DỪNG.

## Stop conditions

Chỉ dừng batch và báo tôi khi gặp ít nhất một điều sau:

- Fix yêu cầu thay đổi plan, API Contract, BSOT, ADR hoặc canonical schema ngoài write set/decision đã được phê duyệt.
- Cần quyết định business/architecture quan trọng chưa có trong SOT hoặc plan.
- Có Open question hoặc SOT conflict mới.
- Cần thêm dependency/package mới mà chưa được explicit approval.
- Cần sửa file ngoài owned files và không thể giải quyết bằng existing seam trong plan.
- Migration không thể generate/apply/down/reapply sau khi đã kiểm tra design-time factory và DB environment.
- Implementer và reviewer lặp lại cùng một bất đồng không hội tụ.
- Fresh-agent retry vẫn fail.
- Phát hiện unrelated user changes overlap trực tiếp với task và không thể bảo toàn an toàn.

Ngoài các trường hợp trên, tiếp tục patch/review/verify; không tự dừng chỉ vì reviewer yêu cầu sửa implementation.

## Hard invariants áp dụng suốt batch

- Source-of-truth hierarchy và task citations luôn thắng reminder hoặc suy đoán của agent.
- Clean Architecture dependency direction; controller chỉ gọi `MediatR.Send`.
- Tenant scope lấy từ authenticated JWT claim, không nhận tenant/operator ID từ client nếu plan không cho phép.
- Cross-service DB FK bị cấm; logical FK only.
- ADR 0004 `ApiResponse<T>` và canonical error registry.
- Mutation tuân theo Idempotency-Key đúng BSOT; read-only endpoint không tự thêm Idempotency-Key.
- `.cs/.csproj/.sln/.props/.targets` dùng CRLF; `.ts/.tsx/.js/.json/.yml/.yaml/.md/.sh/.sql/.mjs` dùng LF theo `.gitattributes`.
- Central Package Management: không đặt `Version=` trên `.csproj` PackageReference.
- Không thêm dependency mới nếu chưa được explicit approval.
- Không AutoMapper; không upgrade MediatR khỏi v11; không thêm OpenTelemetry/Prometheus/Grafana/Tempo/Loki.
- Không dùng git worktree.
- Không stage/commit unrelated user changes.
- Không `--no-verify`; không `Co-Authored-By`.
- Không push hoặc tạo PR.

## Docker và external processes

Nếu task cần Docker cho migration, integration test, Postman/Newman hoặc local stack:

- Kiểm tra trạng thái container trước khi thay đổi.
- Được phép start/run container thuộc repo khi cần cho acceptance.
- Không xóa volume/data hoặc reset môi trường nếu plan không explicit cho phép.
- Chỉ stop process/container do session này tự start; bảo toàn environment có sẵn của user.
- Fixture setup/cleanup phải dùng đúng mechanism trong task plan và cleanup trong failure path.

## Final hand-back

Sau khi task cuối cùng APPROVE, verification green và commit thành công:

- Báo danh sách task đã hoàn thành và commit hash tương ứng.
- Tóm tắt verification matrix đã chạy và kết quả.
- Báo rõ skipped immutable/already-done tasks.
- Báo carry-over/known gaps nếu có.
- Xác nhận working tree còn thay đổi nào và phân biệt intended/unrelated.
- Không tự chạy `/audit-day 22`.
- Không push.
- Không tạo PR.
```
