---
name: implement-task
description: Implement ONE task from an approved VietRide day plan. Reads docs/handoff/day-<N>-plan.md, extracts the named task (e.g. 3.0), dispatches the task's `implement agent` (dotnet-worker / nest-worker / worker) restricted to the task's `owned files`, then dispatches the task's `review agent` (dotnet-reviewer / nest-reviewer / reviewer) on the resulting diff. Loops once on REQUEST CHANGES, then stops for the human to /verify and move to the next task. Per-task granularity by design — NOT a per-day auto-loop. Use after /plan-day produced an APPROVED plan and the human resolved any open questions (e.g. /implement-task 3.0).
---

# Implement one task from an approved day plan

Single low-ceremony entry point for executing **one** task. The task id is the only input —
the durable artifacts (the plan, the agent `.md` files, the skills) supply the structure, so
there is no per-task free-form prompt to drift.

`$ARGUMENTS` = the task id from the plan (e.g. `3.0`, `3.2`). Derive `N` = the day from the id
prefix (`3.0` → Day 3, plan at `docs/handoff/day-3-plan.md`). If absent or malformed, ask.

> **Granularity by design.** This skill executes exactly ONE task per invocation and stops.
> It does NOT chain to the next task, does NOT auto-run `/verify`, does NOT auto-/audit-day.
> A per-day auto-loop is deliberately not provided: code edits touch build/tests/git history
> (asymmetric failure cost vs a plan-text edit), workers need per-task verify before the next
> task starts, and inter-task discovery may require a plan patch. See
> `docs/internal/daily-coding-workflow.md` Phase 2.

## Preconditions (verify before Step 1)
- `docs/handoff/day-<N>-plan.md` exists AND its `Plan status` shows ✅ APPROVED. If not →
  STOP, tell the human to run `/plan-day N` first.
- The plan's **Open questions** list is empty OR all questions blocking this task's scope
  have been resolved by the human. If not → STOP, ask the human to resolve.
- The task's `depends on` predecessors are all completed (commit log or task tracker shows
  them done). If not → STOP, ask the human to confirm.

## Step 1 — Dispatch the implement agent
Open `docs/handoff/day-<N>-plan.md`, locate the task section (e.g. `### Task 3.0 — …`). Read
its fields: `implement agent`, `owned files`, `forbidden scope`, `skill`, `acceptance`,
`invariant flags`, `source citations`.

Dispatch the named subagent via the Agent tool. Prompt template (verbatim, only `<N>` / `<X.Y>`
substituted):

> Implement Task `<X.Y>` from `docs/handoff/day-<N>-plan.md`. Read **only** that task section
> plus the files it cites in `source citations` and lists in `owned files`. Edit **only** files
> in `owned files`; treat `forbidden scope` as a hard fence (STOP and report if you need to
> touch anything outside). Use the `skill` field if it names one. Meet every line of `acceptance`
> and honor every `invariant flag`. Before reporting done, run the build/format/test commands
> implied by the acceptance for this task's scope. Report: files changed, commands run + results,
> any deviation from acceptance, follow-ups.

## Step 2 — Dispatch the review agent on the diff
Dispatch the subagent named in the task's `review agent` field (DIFF-REVIEW mode is the default
for `reviewer` / `dotnet-reviewer` / `nest-reviewer`). Prompt template:

> Review the diff for Task `<X.Y>` (Day `<N>`) against the acceptance criteria + invariant flags
> + source citations in `docs/handoff/day-<N>-plan.md`. Standard DIFF-REVIEW. End with the
> task-level verdict: **APPROVE** / **REQUEST CHANGES** (with file:line + concrete fix per
> finding).

- If **APPROVE** → go to Step 3.
- If **REQUEST CHANGES** → relay findings to the same implement agent (fresh dispatch) for a
  patch round (restricted to the same owned files); re-run Step 2 once. If reviewer still says
  REQUEST CHANGES after one patch round → STOP, escalate to the human (do not chain further
  rounds without human input — repeated failures usually mean the plan is wrong, not the worker).

## Step 3 — Hand back to the human
First, **update the plan's `## Progress tracker` table** for this task: set Status, Review
verdict, Date, and a one-line Note (e.g. patch rounds, spawned ADRs, carry-over). This is the
ONE allowed main-thread edit to `day-<N>-plan.md` — it is status bookkeeping, NOT a scope/
acceptance edit (the "do NOT patch the plan" guardrail below still holds for everything else).
Mark ✅ done only when the reviewer APPROVED; if the human has not `/verify`'d yet, note that.

Then report: task id, files changed, commands run + results, review verdict, **next task id
from the plan's Dispatch order** (so the human knows what `/implement-task` to run next, after
they `/verify` this one). Then stop.

The human then:
1. Runs `/verify` (real app behavior — not just unit tests) + checks the Day-N "Review" bullet
   for any cross-task acceptance specific to that ticket.
2. Decides whether to continue with the next task or pause.
3. After the last task: runs `/audit-day N` to close the day.

## Guardrails
- One task per invocation. **No auto-chaining to the next task.** No auto-/verify, no
  auto-/audit-day. The per-task stop is the design — it's where the human reviews real diff
  + behavior before compounding risk.
- **Dispatch prompts are the templates above, verbatim, with only `<N>` and `<X.Y>` substituted.**
  Do NOT enrich with task-specific context ("Task X.Y does A, B, C…") — that information lives
  in the plan, which the worker is required to read. Enrichment re-introduces per-task prose
  drift (same anti-pattern as the `/plan-day` guardrail).
- **If implement or review agent fails (session limit, error, timeout):** (1) retry the same
  agent fresh first. (2) If retry also fails, STOP and escalate to the human. (3) Do NOT edit
  source files directly from the main thread to "finish" the task, and do NOT patch the plan
  directly to "make the test pass". Direct main-thread patches lose the agent's `*.md`
  discipline (Clean Architecture layering, naming, CRLF, invariants) — empirical evidence
  from `/plan-day` Day 3 (2026-05-28): direct patches invented a column absent from schema
  and wrote a wrong HTTP status code. The cost asymmetry is worse here because the artifact
  is code, not text.
- Worker may NOT add a new package, create a branch, commit, push, open a PR, or run
  `--no-verify`. Those are human-approved actions per `worker.md` / `dotnet-worker.md` /
  `nest-worker.md` always-on invariants.
- The plan is the authoritative scope. If a worker thinks the plan is wrong, it must STOP and
  report — not silently expand scope. A wrong plan is fixed by re-running `/plan-day N`'s
  patch loop (manager → reviewer), not by the worker.
