# Quy trình coding mỗi ngày — Day 3 → hết Sprint 6

> SOP (quy trình chuẩn) cho **một ngày làm việc backend**, dùng pipeline agent của VietRide
> (`/plan-day` → workers → review → `/verify` → `/audit-day`).
> Đi kèm `docs/handoff/README.md` (sơ đồ loop) và `BE_TIMELINE_VU.md` (scope từng ngày).
>
> **File này dành cho NGƯỜI đọc và làm theo** — không agent nào đọc/dùng nó. Agent chỉ đọc
> `AGENTS.md`/`CLAUDE.md`, file `agent.md` của nó, các `SKILL.md`, và các doc SOT.

## Một ý tưởng cốt lõi khiến quy trình này hoạt động

Chất lượng code được giữ bởi **các cổng deterministic**, KHÔNG bởi cách bạn viết prompt:
`.githooks/` (CPM, banned deps, cấm `Co-Authored-By`, line endings) + CI matrix + NetArchTest +
`dotnet format` + các skill scaffold. Nên prompt mỗi ngày chỉ là **một dòng** (`/plan-day N`) —
cấu trúc nằm trong artifact bền vững (`manager.md`, `reviewer.md`, skills, template `docs/handoff/`).
Bạn KHÔNG tự viết hay copy-paste prompt mới mỗi ngày; chính việc đó mới gây drift.

**Bạn (người) giữ đúng 2 cổng:** duyệt plan, và ký DoD trước khi commit. Mọi thứ ở giữa là cơ học.

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
  Duyệt, hoặc trả lại để `manager` patch plan.

### Phase 2 — Implement, mỗi lần một task → `/implement-task X.Y`
Với từng task theo thứ tự dispatch trong plan, **một dòng**:
```
/implement-task 3.0   → worker + reviewer trên task đó → STOP
/implement-task 3.1   → tương tự
...
```
Skill đọc plan, trích task, dispatch `implement agent` (theo trường task) → dispatch
`review agent` (theo trường task) → loop **một** vòng nếu REQUEST CHANGES → STOP báo cáo.
**Không** tự nhảy sang task kế, **không** tự `/verify`, **không** tự `/audit-day`.

Quy tắc khi vận hành:
- Một task xong rồi mới gọi task kế (serial). Chỉ chạy song song khi plan đánh dấu
  parallel-safe (write set disjoint).
- **Không tạo `/implement-day`** để auto-chain cả ngày — chi phí sai ở code lớn hơn ở plan;
  per-task stop là điểm bạn `/verify` thật trước khi tích luỹ rủi ro.
- Sau khi skill stop APPROVE: bạn `/verify` hành vi (chạy app, hit endpoint — không chỉ unit
  test) + đi qua bullet "Review" Day-N của timeline cho task đó nếu có; rồi mới `/implement-task`
  task kế.
- Nếu task thiếu chi tiết hoặc worker thấy plan sai → STOP, bắt `manager` patch plan qua Cổng 1
  (không viết side-prompt, không tự sửa code ngoài owned files).
- **Một session cho một task (hoặc một layer)** với ngày nặng. Plan đã commit là điểm resume
  nếu session bị ngắt giữa chừng.

### Phase 3 — Verify ngày
- `/verify` hành vi mới (chạy app, gọi endpoint — không chỉ unit test).
- skill `smoke-test` → ma trận `/health` (Gateway proxy + Internal-JWT roundtrip).
- Đi qua bullet **Review** của Day N trong timeline (vd "tamper Internal JWT → 401",
  "2 booking cùng ghế → chỉ 1 thắng").

### Phase 4 — Đóng ngày  →  `/audit-day N`
Tự audit code đã giao vs SOT + chạy verification matrix, ghi `docs/handoff/day-<N>-checklist.md`
(DoD ✅/❌, bảng verification, carry-over).
- **CỔNG 2 (bạn):** đọc checklist. Nếu có event/error/convention mới phát sinh, xác nhận đã append
  vào BSOT registry + changelog (§13).
- Commit (không `Co-Authored-By`; không bao giờ `--no-verify`). Push branch; Friday EoD → mở PR.

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
`/verify` + `smoke-test`, nhẹ worker. Vẫn đóng bằng `/audit-day N`.

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
| Implement một task | `dotnet-worker` / `nest-worker` / `worker` |
| Review diff | `/code-review` hoặc `dotnet-reviewer` / `nest-reviewer` |
| Verify hành vi | `/verify` + `smoke-test` |
| Đóng ngày | `audit-day` (read-only) → checklist; **bạn** ký DoD + commit |

Worker sửa code; planner/reviewer/auditor read-only. Hooks chặn vi phạm CPM/banned-dep/commit-trailer
bất kể ai chạy.

---

## Thói quen hằng ngày (từ BE_TIMELINE_VU.md §Cross-cutting)

- Sáng: chọn Jira subtask kế, set In Progress.
- Mỗi endpoint mới: ≥1 happy-path + ≥1 error-case test; annotation Swashbuckle; **thêm route Gateway**
  trong `apps/gateway/src/config/routes.ts` (FE luôn gọi qua Gateway).
- Mỗi migration: có `Down()` reversible; không sửa migration đã merge.
- Cuối ngày: commit + push + ghi note Jira. Friday EoD: mở PR, tag Tuyên nếu đổi NestJS contract.

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

Mỗi ngày, dù kiểu nào: `/plan-day N` → (cổng) → workers + reviews → `/verify` → `/audit-day N` →
(cổng) → commit. Kiểu ngày chỉ đổi *task là gì*, không đổi *vòng lặp*.
