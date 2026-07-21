---
name: review-task
description: Review ONE task X.Y from an approved VietRide day plan when a cheaper gateway-profile session produced the implementation and strong Codex must provide the authoritative static review. Dispatches the named review agent, assesses targeted evidence, updates the tracker, and stops with findings for the external implementing session to patch. Use after a profile-model session implements a task, for example /review-task 7.3.
---

# Review a task implemented by a cheaper model, on strong Codex

The counterpart to `/implement-task` for the **split-model** workflow: the implementation was
produced by a cheaper/weaker model (a gateway-profile Codex session) and you want the
authoritative review to run on strong Codex. This skill performs the part you split off —
`/implement-task`'s **Step 2 (review)** and **Step 3 (bookkeeping + hand back)** — keyed off the
task id, so there is no per-task free-form prompt to drift.

`$ARGUMENTS` = the task id `X.Y` from the plan (any day). Derive `N` = the day from the id prefix
(`X` → Day `X`, plan at `docs/handoff/day-<N>-plan.md`; e.g. `7.3` → Day 7,
`docs/handoff/day-7-plan.md`). If absent or malformed, ask.

> **Why this exists.** `/implement-task` bundles implement → review → bookkeeping, all under ONE
> session/model — so its review runs on whatever (possibly cheap) model implemented the task. When
> you want a strong-Codex review of a diff a profile-model session produced, you split: the
> profile session implements, then a strong-Codex session runs `/review-task`. A cheaper model
> does NOT reliably carry this repo's agent `.md` discipline (Clean Architecture layering, naming,
> CRLF, invariants), so the strong-model review gate is where drift is caught. Built-in
> `/code-review` / `/review` do NOT know the task's `acceptance`, its designated `review agent`, or
> the Progress tracker — this skill does. See `docs/internal/daily-coding-workflow.md`.

> **Granularity by design.** One task per invocation, then stop — same contract as
> `/implement-task`. No auto-chaining, no auto-/verify, no auto-/audit-day.

## Preconditions (verify before Step 1)
- `docs/handoff/day-<N>-plan.md` exists AND its normalized `Plan status` value is exactly
  `APPROVED`. If not →
  STOP, tell the human to run `/plan-day N` first.
- There is a diff to review (working tree changes, or commits since the task's predecessor).
  If `git status` / `git diff` shows nothing for this task's `owned files` → STOP, ask the
  human whether the profile-model session actually ran / where the change landed.
- The task's `depends on` predecessors are all completed. If not → STOP, ask the human to confirm.

## Step 1 — Identify the diff under review
Open `docs/handoff/day-<N>-plan.md`, locate the task section (`### Task <X.Y> — …`). Read
its fields: `review agent`, `owned files`, `auto-expand scope`, `forbidden scope`, `acceptance`,
`invariant flags`, `source citations`, `verification tier`, `verification commands`, and
`full regression owner`.

Determine the diff for this task: prefer the working-tree diff (uncommitted) if present; else
the commit(s) the profile-model session made for this task (vs the predecessor task's tip). Confirm
the changed paths fall within the task's base write set or documented auto-expand envelope. Require
a path/reason/acceptance-or-finding ledger entry for every expansion. Flag forbidden-scope edits
and changes outside both sets as scope leaks regardless of the review agent's verdict.

Collect the implementing session's exact targeted commands, exits/results, non-zero selected-test
counts, and complete command output. Pass it verbatim to the reviewer, or pass a concrete
transcript/artifact locator the reviewer can read. The plan defines commands but does not contain
runtime evidence. A summary, quiet wrapper, or unlocatable claim is missing evidence.

Pass the reviewer the explicit baseline/commit range or path-scoped cumulative task diff, exact
changed paths, and scope ledger determined above. It must ignore unrelated pre-existing diffs and
re-review every patch from the same task baseline.

## Step 2 — Dispatch the review agent on the diff
Dispatch the subagent named in the task's `review agent` field (DIFF-REVIEW mode is the default
for `reviewer` / `dotnet-reviewer` / `nest-reviewer`). Keep the instruction template unchanged
except for `<N>` / `<X.Y>`, then append the required mechanical handoff block from Guardrails:

> Statically review only the supplied cumulative diff boundary for Task `<X.Y>` (Day `<N>`) against the acceptance
> criteria, invariant flags, source citations, scope-expansion ledger, and the complete targeted
> verification evidence supplied with this handoff. The plan defines commands but does not contain
> runtime output. Ignore unrelated pre-existing changes outside the supplied baseline/range and
> exact task paths. The implementation was produced by a
> cheaper model that does NOT reliably carry this repo's agent discipline — apply extra scrutiny
> to invented columns/enums/endpoints, wrong HTTP status codes, Clean Architecture layering,
> naming, CRLF/CPM, and banned-dep invariants. **Ignore any verdict already recorded in the plan's
> `## Progress tracker` — it may be a weaker model's self-review; form your own verdict purely from
> the diff against the task spec, do not treat a prior APPROVE as evidence.** Read the task section
> for spec, NOT the tracker. You may read complete targeted logs but must not execute build, test,
> lint, format, migration, or other application verification. Missing/stale/zero-test evidence is
> a finding with the exact targeted command the implementing session must run; do not request full
> solution/workspace regression because `/audit-day` owns it. Standard DIFF-REVIEW.
> End with the task-level verdict: **APPROVE** / **REQUEST CHANGES** (with file:line + concrete fix
> per finding).

- If **APPROVE** and targeted evidence is green → go to Step 3.
- If **REQUEST CHANGES** → go to Step 3 (record the verdict), then **STOP and hand the findings
  back to the human** to relay to the profile-model session for a patch round. Do NOT dispatch an
  internal worker to patch — implementation is intentionally split onto the profile session in this
  workflow. Re-review after it patches = the human re-invokes `/review-task <X.Y>` (that is the
  "one loop"). If a third invocation still REQUESTs CHANGES → **STOP and escalate to the human**;
  do not chain further rounds.

## Step 3 — Update the tracker + hand back to the human
First, **update the plan's `## Progress tracker` table** for this task: set Status, Review
verdict, Date, and a one-line Note (e.g. "impl: <profile model>", patch rounds, scope leaks found,
spawned ADRs). This is the ONE allowed main-thread edit to `day-<N>-plan.md` — status bookkeeping
only, NOT a scope/acceptance edit. **This skill's verdict is authoritative and OVERWRITES any
prior verdict in the row — including a profile-model `/implement-task`'s self-review APPROVE.**
On REQUEST CHANGES, set the verdict to REQUEST CHANGES (do NOT leave a stale APPROVE) with a Note
like "strong-Codex re-review overrides profile-model APPROVE"; it stays REQUEST CHANGES until a
re-invocation APPROVES. Mark ✅ done only when THIS skill's reviewer APPROVED; if the human has not
`/verify`'d yet, note that.

Then report: task id, reviewed diff source (working tree vs commit range), files changed, scope
expansions, targeted evidence assessed, review verdict + findings (if any), and the **next task id
from the plan's Dispatch order**. Then stop.

The human then:
1. On APPROVE: runs `/verify` (real app behavior) at the appropriate milestone — in the
   split-model workflow, `/verify` is batched at cluster boundaries, not every task.
2. On REQUEST CHANGES: relays findings to the profile-model session, then re-invokes
   `/review-task <X.Y>`.
3. After the last task of the day: runs `/audit-day N` to close the day.

## Guardrails
- One task per invocation. **No auto-chaining, no auto-/verify, no auto-/audit-day.** Same stop
  contract as `/implement-task`.
- **Read-only on source.** This skill NEVER edits source files — not to "finish" a finding, not
  to patch a nit. The only write it performs is the Progress tracker bookkeeping in Step 3. If a
  finding needs a code change, it goes back to the profile-model session. Direct main-thread
  patches lose the agent `*.md` discipline — the exact failure mode documented in
  `/implement-task`'s guardrails (invented column, wrong HTTP status on 2026-05-28).
- **Dispatch instructions are the template above, with only `<N>` and `<X.Y>` substituted.**
  Append exactly one mechanical handoff block containing the task baseline/range or path-scoped
  diff locator, exact changed paths, scope ledger, exact command results/non-zero counts, and the
  complete targeted log or readable locator. Do not add task-specific implementation prose; it
  lives in the plan.
- **Scope leak is a finding.** Any edit outside both the task's base `owned files` and documented
  `auto-expand scope` (or any edit in `forbidden scope`) is REQUEST CHANGES-worthy even if the
  correctness verdict is APPROVE. An allowed expansion without a ledger reason is also a finding.
- The plan is the authoritative scope. If the review surfaces that the plan itself is wrong, STOP
  and tell the human to re-run `/plan-day N`'s patch loop — do not paper over it in review.
