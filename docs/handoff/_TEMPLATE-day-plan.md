# Day &lt;N&gt; — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.
> Replace &lt;N&gt; with the timeline day number. Delete this quote block when filling in.

- **Timeline ref**: BE_TIMELINE_VU.md → Day &lt;N&gt; (Jira: SCV-___)
- **Prior checklist**: docs/handoff/day-&lt;N-1&gt;-checklist.md (or `not found`)
- **Plan status**: `DRAFT`
  <!-- Replace the value, do not append statuses. Allowed lifecycle:
  DRAFT | REVISION-REQUIRED | REVIEWER-APPROVED — AWAITING HUMAN | APPROVED -->

## Objective
&lt;=4 sentences: what Day N delivers, why it matters, what it unblocks for Day N+1.

## Success criteria (DoD — binary, verifiable)
- [ ] … (mirror BE_TIMELINE_VU.md Day N **DoD**; each line checkable by a command/test)

## Contract changes
New/changed REST endpoints, event routing keys, DB migration, error codes, Gateway routes.
Cite source (VietRide_API_Contract_v1.md §, BSOT registry §). If none: `No contract changes`.

## Tasks
Ordered, single-responsibility. **Task 0 is always the Day-N "Pre-reqs / architecture
baseline"** if the timeline lists one (e.g. Day 3: MediatR behaviors + NetArchTest + CPM
entries) — feature tasks depend on it.

Per-task verification uses the smallest scope that can falsify acceptance:
- `DOCS`: parse/syntax/citation/registry consistency plus `git diff --check`; no app build.
- `FOCUSED` (default): exact test class/spec/filter, changed-file format/lint, and only the
  smallest compile not already performed by the targeted test.
- `PROJECT`: affected test project or Nx project only, reserved for DI/global middleware,
  DbContext-wide behavior, global fixtures, or project-wide config; include a justification.

Targeted test output must report at least one executed test; zero selected/executed is failure.
Migration lifecycle checks remain task-specific. Full solution/workspace regression is never a
task command: `/audit-day N` owns it after implementation and review are complete.
When `skill` names a feature skill, `verification commands` must explicitly include all of that
skill's applicable mandatory focused checks; the worker must not invent or append hidden checks.

### Task N.0 — &lt;title&gt;
| Field | Value |
|---|---|
| stack/owner | dotnet / nest / cross-cutting |
| implement agent | dotnet-worker / nest-worker / worker |
| review agent | dotnet-reviewer / nest-reviewer / reviewer (or /code-review) |
| skill | scaffold-aggregate / add-endpoint / ef-migration / add-integration-event / (none) |
| owned files (base write set) | concrete paths the worker is expected to edit |
| auto-expand scope | concrete path patterns/categories directly required by acceptance or reviewer findings; only applicable same-feature/service files, affected tests, DI/config, interface/implementation pairs, generated migration files, Gateway routes, or required producer/consumer contracts |
| forbidden scope | hard-stop areas outside the base + auto-expand envelope (always incl. `.env`, secrets, unrelated services, new dependencies, unresolved business/API/schema decisions, destructive operations, git ops) |
| depends on | task ids that must land first |
| parallel-safe | yes / no (yes only when the base + auto-expand envelope is disjoint from every other task) |
| verification tier | `DOCS` / `FOCUSED` / `PROJECT` — choose exactly one; justify `PROJECT` here |
| verification commands | exact targeted commands, paths, and filters; tests execute >0; .NET format uses `--include` changed paths; no full solution/workspace command |
| full regression owner | `audit-day` |
| invariant flags | CRLF/.cs · LF/.ts · CPM no `Version=` · MediatR v11 · BCrypt 12 · Money pass-through/to-the-đồng with `AwayFromZero` rounding (BSOT v1.11.0) · Outbox routing-key · no cross-DB FK · tenant isolation |
| acceptance | observable behavior/outcomes tied to DoD: domain rules · contract · migration result · events · auth/tenant · idempotency as applicable; no commands or generic "build/format/tests pass" wording |
| source citations | technical_context_v7 § / API contract § / db-schema / BSOT § |

### Task N.1 — …
(repeat)

## Dispatch order
1. Task N.0 → … (note which are parallel-safe = disjoint write sets; default serial in one tree)

## Progress tracker
> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task`
> or task completed by `/execute-day`, with the task's review verdict. **Informational only —
> NOT audit evidence.**
> `/audit-day` MUST re-verify every task independently against the SOT; it must never treat a
> ✅ here (or a worker self-report) as proof. A row is bookkeeping, not a passed audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| N.0 | ⬜ todo | — | — | — |
| N.1 | ⬜ todo | — | — | — |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + targeted verification green) · ⚠️ done-with-carryover · ❌ blocked

## Open questions
Ambiguities the `manager` could NOT resolve from the SOT docs — resolve with the human
before dispatch. Do **not** guess.
