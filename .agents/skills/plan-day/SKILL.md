---
name: plan-day
description: Produce the dispatchable work breakdown for a VietRide timeline day with behavioral acceptance, explicit scope envelopes, and per-task targeted verification. Runs the `manager` subagent to draft the durable day plan, then runs `reviewer` as a PLAN-REVIEW gate before any worker is dispatched. Use at the start of each backend day (e.g. /plan-day 3).
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
> Every task must include `owned files`, `auto-expand scope`, `forbidden scope`, `invariant
> flags`, behavioral `acceptance`, `verification tier` (`DOCS`, `FOCUSED`, or `PROJECT`), exact
> `verification commands`, `full regression owner: audit-day`, and `source citations`. Choose
> the smallest sufficient tier; `PROJECT` requires a concrete justification. Targeted tests must
> name an exact project/spec/filter and execute at least one test. Do not assign full-solution or
> full-workspace verification to a task, and do not put vague build/format/tests wording in
> acceptance. When `skill` names a feature skill, include all of that skill's mandatory focused
> checks as exact commands; never leave them implicit for the worker. Make the Day-N
> "Pre-reqs / architecture baseline" Task N.0 with dependency edges
> if the timeline lists one. List ambiguities under Open questions — do not guess.

## Step 2 — PLAN-REVIEW gate (reviewer)
Dispatch the **`reviewer`** subagent (`subagent_type: reviewer`) in **PLAN-REVIEW mode**:

> PLAN-REVIEW docs/handoff/day-<N>-plan.md (review the PLAN, not a diff). Apply your
> "Plan-review mode" checklist. REQUEST PLAN CHANGES if any task lacks its verification tier,
> exact targeted commands, auto-expand scope, or
> `full regression owner: audit-day`; if acceptance contains generic build/format/tests wording;
> if a test-bearing task lacks an exact project/spec/filter or can pass with zero tests; if
> `PROJECT` lacks justification; or if any task runs a full solution/workspace regression.
> Also REQUEST PLAN CHANGES when a named feature skill's applicable mandatory focused checks are
> absent from the task's exact verification commands.
> End with **APPROVE PLAN** or **REQUEST PLAN CHANGES**.

- If **REQUEST PLAN CHANGES** → relay findings, have `manager` patch `day-<N>-plan.md`, re-run Step 2.
- If **APPROVE PLAN** → update only the plan-status line to
  `REVIEWER-APPROVED — AWAITING HUMAN`, surface the plan's **Open questions**, and stop for the
  human gate. Reviewer approval is not implementation authorization.

## Step 3 — Hand back to the human
Report: plan file path, task count, dispatch order, parallel-safe tasks, and open questions. Ask
the human to approve the plan after reviewing it.

After explicit human approval:

- If an answer changes scope, acceptance, commands, or citations, have `manager` patch the plan
  and repeat PLAN-REVIEW before asking again.
- If all blocking questions are resolved and no plan content changes, update only the status line
  to `APPROVED`. This one-line bookkeeping edit is the durable authorization required by task
  execution.

**Do not dispatch workers from this skill** unless the human separately asked for execution.
After status is `APPROVED`, the human can use `/implement-task X.Y` or `/execute-day N`. Both use
targeted verification. Close the day with `/audit-day N`, which alone owns the full
solution/workspace regression matrix.

## Guardrails
- Read-only planning: this skill produces only `docs/handoff/day-<N>-plan.md`. The orchestrator's
  only direct edits are the status transitions described above; all substantive plan patches go
  back through manager + PLAN-REVIEW. No code edits, branches/commits, or package installs.
- The plan is a design, not permission to implement. Quality invariants remain enforced by
  `.githooks/` + CI + NetArchTest regardless — the gate here only catches a *bad plan* early.
- Per-task verification must use the smallest scope that can falsify behavioral acceptance.
  `FOCUSED` is the default, `PROJECT` is exceptional and justified, and full regression is
  always deferred to `audit-day`.
- **Dispatch prompt is the template above, verbatim, with only `N` substituted.** Do NOT enrich
  the prompt with day-specific scope ("Day N covers X, Y, Z…") — that information already lives
  in `BE_TIMELINE_VU.md` which `manager.md` is required to read. Enrichment re-introduces the
  per-day prose drift this pipeline exists to prevent. If `manager` consistently misses scope,
  fix `manager.md` or the timeline, not the daily prompt.
- **If `manager` fails mid-draft or mid-patch (session limit, error, timeout):** (1) retry with
  a fresh `manager` dispatch FIRST. (2) If retry also fails, escalate to the human. (3) Do NOT
  patch `day-<N>-plan.md` directly from the main thread except the exact status transition above.
  Substantive direct patches lose
  `manager.md`'s READ-then-DISCOVER + no-invent + cite-source guardrails — empirical evidence
  (Day 3, 2026-05-28): main-thread direct patch invented a `LockedUntil` column absent from
  `schema.sql` and wrote HTTP 423 contradicting BSOT §5.9's 403, both caught by reviewer at the
  cost of a second patch+review cycle.
