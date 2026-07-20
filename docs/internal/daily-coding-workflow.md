# Quy trình coding mỗi ngày — Day 3 → hết Sprint 6

> SOP (quy trình chuẩn) cho **một ngày làm việc backend**, dùng pipeline agent của VietRide
> (`/plan-day` → `/implement-task` hoặc `/execute-day` → `/audit-day`).
> Đi kèm `docs/handoff/README.md` (sơ đồ loop) và `BE_TIMELINE_VU.md` (scope từng ngày).
>
> **File này dành cho NGƯỜI đọc và làm theo** — không agent nào đọc/dùng nó. Agent chỉ đọc
> `AGENTS.md`/`CLAUDE.md`, file `agent.md` của nó, các `SKILL.md`, và các doc SOT.

## Một ý tưởng cốt lõi khiến quy trình này hoạt động

Chất lượng code được giữ bởi **các cổng deterministic**, KHÔNG bởi cách bạn viết prompt:
`.githooks/` (CPM, banned deps, cấm `Co-Authored-By`, line endings) + CI matrix + NetArchTest +
`dotnet format` + các skill scaffold. Trong implement/review, task chỉ chạy targeted checks đã
ghi trong plan; `/audit-day` mới là owner duy nhất của full regression matrix mặc định. Nên prompt
mỗi phase chỉ là **một dòng** (`/plan-day N`, rồi `/implement-task X.Y` hoặc `/execute-day N`) —
cấu trúc nằm trong artifact bền vững (`manager.md`, `reviewer.md`, skills, template `docs/handoff/`).
Bạn KHÔNG tự viết hay copy-paste workflow mới mỗi ngày; chính việc đó mới gây drift.

**Bạn (người) giữ đúng 2 cổng:** duyệt plan/chế độ chạy, và ký DoD trước khi close/push ngày.
Task có thể được commit riêng trong quá trình chạy; mọi thứ ở giữa hai cổng là cơ học.

---

## Một ngày FEATURE chuẩn (mặc định — phần lớn Day 3–9, 11–19, 21–28, 31–42)

### Phase 0 — Mở ngày (2 phút)
- `git status` sạch / đúng branch.
- Lướt mục Day N trong `BE_TIMELINE_VU.md` (scope + **DoD** + **Review**) và phần carry-over trong
  `docs/handoff/day-<N-1>-checklist.md`.

### Phase 1 — Plan + cổng  →  `/plan-day N`
Chạy `manager` (sinh `docs/handoff/day-<N>-plan.md`) rồi `reviewer` ở mode PLAN-REVIEW.
- **CỔNG 1 (bạn):** đọc task list + **Open questions**. Giải quyết chỗ mơ hồ dựa trên SOT docs
  (đừng để agent đoán). Xác nhận Task N.0 = "Pre-reqs / architecture baseline" nếu timeline có.
  Duyệt, hoặc trả lại để `manager` patch plan. Sau explicit approval và khi không còn blocking
  question, orchestrator đổi field hiện tại thành đúng `Plan status: APPROVED`; đây là durable
  authorization mà cả hai execution mode kiểm tra.

### Phase 2 — Implement + review + targeted verification

Chọn **một** trong hai chế độ sau khi plan đã được duyệt.

#### Chế độ thủ công — `/implement-task X.Y`

Với từng task theo thứ tự dispatch trong plan, gọi một dòng:
```
/implement-task 3.0   → worker + static reviewer + targeted checks → tracker update → STOP
/implement-task 3.1   → tương tự
...
```
Skill đọc plan, trích task, dispatch `implement agent` (theo trường task) → dispatch
`review agent` (theo trường task) → patch/re-review liên tục khi còn tiến triển → STOP sau một task.
Reviewer review tĩnh và đánh giá evidence; worker chạy exact targeted commands của task. Skill
**không** tự nhảy sang task kế, commit, `/audit-day`, push hoặc tạo PR.

#### Chế độ batch đã được người duyệt — `/execute-day N`

```text
/execute-day 3
```

Skill tự tạo manifest từ plan có `Plan status: APPROVED` sau human gate, rồi chạy tuần tự mọi task
còn lại bằng cùng loop
implement → static review → targeted verification → patch/re-review. Nó được tự mở rộng file trong
scope envelope đã duyệt, commit riêng từng task, và kết thúc ở
`IMPLEMENTED — AWAITING /audit-day N`. Nó không auto-audit, push hoặc tạo PR. File
`docs/handoff/_TEMPLATE-sequential-day-execution-prompt.md` chỉ là compatibility launcher tới
skill này, không chứa một bản workflow thứ hai.

Quy tắc khi vận hành:
- Mặc định serial. Chế độ thủ công dừng sau một task; batch chỉ chạy tuần tự và không dùng worktree.
- Task dùng tier `DOCS`, `FOCUSED`, hoặc `PROJECT` cùng exact commands trong plan. Không tự nâng một
  task nhỏ thành full solution/workspace build, format, lint hoặc test.
- Targeted test đã compile production project liên quan thì không build solution thêm lần nữa.
  Sau patch chỉ chạy lại checks bị invalidated; patch docs/comment không rerun code tests.
- Reviewer không tự chạy build/test/lint/format. Evidence thiếu hoặc stale trở thành finding kèm
  command để worker chạy, rồi reviewer đọc kết quả đầy đủ khi re-review.
- Chỉ STOP để `manager` patch plan qua Cổng 1 khi thiếu quyết định business/API/schema, có SOT
  conflict, hoặc fix phải ra ngoài `owned files` + `auto-expand scope` envelope. File phụ trợ nằm
  trong envelope được tự thêm và ghi vào scope ledger, không cần hỏi lại.
- `/verify` hoặc `smoke-test` có thể dùng khi chủ động cần troubleshooting/runtime checkpoint,
  nhưng không phải một full-regression gate bắt buộc sau mỗi task và không thay `/audit-day`.

### Phase 3 — Kết thúc implementation

Khi mọi task đã có reviewer `APPROVE` và targeted evidence green, implementation đã hoàn tất. Batch
mode còn yêu cầu từng task commit thành công; manual mode giữ tiến độ trong tracker và không auto-
commit. Trạng thái ngày là `IMPLEMENTED — AWAITING /audit-day N`, chưa phải `READY`; full regression,
Docker health và Day-N business E2E chưa được suy ra từ targeted evidence.

### Phase 4 — Đóng ngày  →  `/audit-day N`
Sau implementation (và sau các task commits trong batch mode), trước khi close/push, tự audit code
đã giao vs SOT + chạy full verification matrix cho mọi solution/workspace mà ngày đã chạm, ghi
`docs/handoff/day-<N>-checklist.md`
(DoD ✅/❌, bảng verification, carry-over). Đây là owner duy nhất của full regression mặc định;
audit luôn chạy lại matrix và không tin/reuse targeted evidence, progress tracker hay self-report.
- **CỔNG 2 (bạn):** đọc checklist. Nếu có event/error/convention mới phát sinh, xác nhận đã append
  vào BSOT registry + changelog (§13).
- Nếu audit phát hiện lỗi, tạo remediation task riêng rồi audit lại. Khi checklist đạt yêu cầu,
  close/push branch; Friday EoD → mở PR. Không commit có `Co-Authored-By`; không dùng `--no-verify`.

> **Định nghĩa "ngày xong"** = mọi dòng **DoD** của Day N đều ✅ trong checklist, và build +
> `dotnet format --verify-no-changes` + tests (gồm NetArchTest) + migration-up đều xanh.

---

## Các biến thể của ngày

**Ngày nặng pre-req (Day 3).** Day 3 phải dựng architecture baseline *trước* mọi feature:
MediatR pipeline behaviors (`ValidationBehavior`/`LoggingBehavior`/`TransactionBehavior`), test
`NetArchTest` (dependency direction), và CPM `<PackageVersion>`. `/plan-day 3` phải ra các thứ này
thành **Task 3.0** với mọi feature task phụ thuộc vào nó. Đừng để worker nhảy thẳng vào
`/auth/register` trước khi Task 3.0 được APPROVE — đó là cổng khiến "CI-enforced layering" thành thật
cho cả phần còn lại của dự án.

**Ngày buffer / integration (Day 20, 29).** Không feature mới — bug sweep + wire E2E. Vẫn dùng
`/plan-day N` nhưng task kiểu "fix X", "wire Notification consumer", "update Postman". Nặng
targeted integration checks, nhẹ worker; full runtime/E2E matrix vẫn thuộc `/audit-day N`.

**Ngày demo-prep (Day 10, 30, 44–50).** Chủ yếu seed data, Postman/demo script, dry-run, tune
perf/index. `/plan-day` vẫn ra task list, nhưng DoD là "demo xanh" / "scenario chạy hết" thay vì
endpoint mới. Day 50 còn: update BSOT với khác biệt ACTUAL-vs-DESIGNED + viết retro.

**Ngày aggregate mới (phần lớn ngày Trip/Booking/Payment/Parcel).** Task dẫn đầu gần như luôn là
`ef-migration` (schema) → rồi `scaffold-aggregate` (4 layer) → rồi `add-endpoint` mỗi route → rồi
`add-integration-event` cho lifecycle event. Plan sắp thứ tự này theo dependency.

---

## Vai trò (ai làm gì)

| Bước | Người/Agent |
|---|---|
| Plan ngày | `manager` (read-only) qua `/plan-day` |
| Gate plan | `reviewer` PLAN-REVIEW, rồi **bạn** |
| Implement task | `dotnet-worker` / `nest-worker` / `worker` qua `/implement-task` hoặc `/execute-day` |
| Review + task gate | `reviewer` / `dotnet-reviewer` / `nest-reviewer` (static) + worker targeted checks |
| Đóng ngày | `audit-day` → independent full matrix + checklist; **bạn** ký DoD trước close/push |

Worker sửa code; planner/reviewer read-only, còn auditor chỉ viết checklist và không sửa code.
Hooks chặn vi phạm CPM/banned-dep/commit-trailer bất kể ai chạy.

---

## Thói quen hằng ngày (từ BE_TIMELINE_VU.md §Cross-cutting)

- Sáng: chọn Jira subtask kế, set In Progress.
- Mỗi endpoint mới: ≥1 happy-path + ≥1 error-case test; annotation Swashbuckle; **thêm route Gateway**
  trong `apps/gateway/src/config/routes.ts` (FE luôn gọi qua Gateway).
- Mỗi migration: có `Down()` reversible; không sửa migration đã merge.
- Cuối ngày: sau task commits và `/audit-day`, push + ghi note Jira. Friday EoD: mở PR, tag Tuyên
  nếu đổi NestJS contract.

---

## Khi một ngày bị trễ (spillover)

Theo `BE_TIMELINE_VU.md` §Spillover. Ghi phần chưa xong thành **carry-over** trong
`day-<N>-checklist.md`; `/plan-day N+1` đọc nó và đưa vào plan kế như một dependency.
**Hard-stop (không được cắt):** Identity, Outbox baseline, Booking core, Payment+VNPay IPN, Trip
lifecycle automation. Còn lại có thể đàm phán cho demo.

---

## Bản đồ nhanh — ngày nào kiểu gì (Day 3 → 50)

| Sprint | Days | Kiểu |
|---|---|---|
| S2 Foundation | 3 | **Nặng pre-req** (baseline) + Identity auth |
| | 4–9 | Feature day (Identity → Operator → Trip/Route/Vehicle) |
| | 10 | Outbox/idempotency baseline + **demo-prep** |
| S3 Booking+Payment | 11–19 | Feature day (search, seat-lock, voucher, payment, cancel, manifest) |
| | 20 | **Buffer + demo-prep** |
| S4 Trip ops + Parcel | 21–28 | Feature day (lifecycle, schedule change, parcel) |
| | 29 | **Buffer (integration)** · 30 **demo-prep** |
| S5 Disruption + Subscription | 31–40 | Feature day (substitution, shuttle, subscription, invoice/settlement) |
| S6 Reporting + Polish | 41–43 | Feature day (exports, platform reports, reliability) |
| | 44–50 | **Demo-prep / E2E rehearsal / dry-runs / capstone day** |

Mỗi ngày, dù kiểu nào: `/plan-day N` → (cổng) → `/implement-task X.Y` thủ công **hoặc**
`/execute-day N` batch (task commits) → `/audit-day N` full regression → (cổng) → close/push.
Kiểu ngày chỉ đổi *task là gì*, không đổi *vòng lặp*.
