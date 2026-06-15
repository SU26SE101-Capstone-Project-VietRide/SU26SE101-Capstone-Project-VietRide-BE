# Day &lt;N&gt; — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.
> Replace &lt;N&gt; with the timeline day number. Delete this quote block when filling in.

- **Timeline ref**: BE_TIMELINE_VU.md → Day &lt;N&gt; (Jira: SCV-___)
- **Prior checklist**: docs/handoff/day-&lt;N-1&gt;-checklist.md (or `not found`)
- **Plan status**: DRAFT → (reviewer) APPROVED / REVISION-REQUIRED

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

### Task N.0 — &lt;title&gt;
| Field | Value |
|---|---|
| stack/owner | dotnet / nest / cross-cutting |
| implement agent | dotnet-worker / nest-worker / worker |
| review agent | dotnet-reviewer / nest-reviewer / reviewer (or /code-review) |
| skill | scaffold-aggregate / add-endpoint / ef-migration / add-integration-event / (none) |
| owned files (write set) | concrete paths the worker MAY edit |
| forbidden scope | paths/areas the worker MUST NOT touch (always incl. `.env`, secrets, other services, git ops) |
| depends on | task ids that must land first |
| invariant flags | CRLF/.cs · LF/.ts · CPM no `Version=` · MediatR v11 · BCrypt 12 · Money to-the-đồng (no floor-1000, BSOT v1.11.0) · Outbox routing-key · no cross-DB FK · tenant isolation |
| acceptance | tied to DoD: build · `dotnet format` · tests (incl. NetArchTest) · migration up · contract · events · auth/tenant · idempotency |
| source citations | technical_context_v7 § / API contract § / db-schema / BSOT § |

### Task N.1 — …
(repeat)

## Dispatch order
1. Task N.0 → … (note which are parallel-safe = disjoint write sets; default serial in one tree)

## Progress tracker
> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task`
> (Step 3) with the task's review verdict. **Informational only — NOT audit evidence.**
> `/audit-day` MUST re-verify every task independently against the SOT; it must never treat a
> ✅ here (or a worker self-report) as proof. A row is bookkeeping, not a passed audit.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| N.0 | ⬜ todo | — | — | — |
| N.1 | ⬜ todo | — | — | — |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Open questions
Ambiguities the `manager` could NOT resolve from the SOT docs — resolve with the human
before dispatch. Do **not** guess.
