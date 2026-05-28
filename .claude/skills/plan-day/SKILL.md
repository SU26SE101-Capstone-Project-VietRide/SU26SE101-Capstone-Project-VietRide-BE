---
name: plan-day
description: Produce the dispatchable work breakdown for a VietRide timeline day. Runs the `manager` subagent to draft docs/handoff/day-<N>-plan.md from BE_TIMELINE_VU.md + BSOT + API contract + db-schema, then runs `reviewer` as a PLAN-REVIEW gate before any worker is dispatched. Use at the start of each backend day (e.g. /plan-day 3).
---

# Plan a backend day

Single low-ceremony entry point for the daily loop. The day number is the only input —
the durable templates (`manager.md`, `reviewer.md`, this skill, `docs/handoff/`) supply the
structure, so there is no per-day free-form prompt to drift.

`$ARGUMENTS` = the timeline day number `N` (e.g. `3`). If absent, ask which day.

> The `manager` reads the source-of-truth docs (timeline, BSOT, API contract, db-schema,
> technical_context_v7) per its own definition — `manager.md` §"Inputs you always consult" +
> §"Method — READ then DISCOVER". This skill does not re-list them (single source of truth);
> if `manager` skips reading, that is a `manager.md` bug, not a skill gap.

## Step 1 — Draft the plan (manager)
Dispatch the **`manager`** subagent (Agent tool, `subagent_type: manager`) with:

> Plan Day N for VietRide per BE_TIMELINE_VU.md. Read the prior checklist
> `docs/handoff/day-<N-1>-checklist.md` first (record `not found` and continue if absent).
> Follow your Output format exactly, and **write the result to
> `docs/handoff/day-<N>-plan.md`** using `docs/handoff/_TEMPLATE-day-plan.md` as the shape.
> Every task must include `owned files`, `forbidden scope`, `invariant flags`, `acceptance`,
> and `source citations`. Make the Day-N "Pre-reqs / architecture baseline" Task N.0 with
> dependency edges if the timeline lists one. List ambiguities under Open questions — do not guess.

## Step 2 — PLAN-REVIEW gate (reviewer)
Dispatch the **`reviewer`** subagent (`subagent_type: reviewer`) in **PLAN-REVIEW mode**:

> PLAN-REVIEW docs/handoff/day-<N>-plan.md (review the PLAN, not a diff). Apply your
> "Plan-review mode" checklist and end with **APPROVE PLAN** or **REQUEST PLAN CHANGES**.

- If **REQUEST PLAN CHANGES** → relay findings, have `manager` patch `day-<N>-plan.md`, re-run Step 2.
- If **APPROVE PLAN** → surface the plan's **Open questions** (if any) to the human, then stop.

## Step 3 — Hand back to the human
Report: plan file path, task count, dispatch order, parallel-safe tasks, and open questions.
**Do not dispatch workers from this skill** — the human approves, then dispatches each task
to `dotnet-worker` / `nest-worker` / `worker` (one at a time). After each task, review the
diff (`/code-review` or the stack reviewer) and `/verify`. Close the day with `/audit-day N`.

## Guardrails
- Read-only planning: this skill produces only `docs/handoff/day-<N>-plan.md`. No code edits,
  no branches/commits, no package installs.
- The plan is a design, not permission to implement. Quality invariants remain enforced by
  `.githooks/` + CI + NetArchTest regardless — the gate here only catches a *bad plan* early.
