# docs/handoff — Per-day handoff artifacts

Committed, durable artifacts that drive the **daily agent loop** and survive a session
ending mid-day (or hitting a limit). Two files per day:

- `day-<N>-plan.md` — the dispatchable work breakdown for Day N (produced by the `manager`
  subagent, gated by `reviewer`). The source of truth for *what the workers do today*.
- `day-<N>-checklist.md` — the end-of-day result: DoD status, verification output, changed
  files, and **carry-over** for Day N+1.

> Day N+1 planning reads `day-<N>-checklist.md` as an input. This is the continuity chain —
> a fresh session resumes from the committed plan/checklist, not from chat memory.

> Full step-by-step procedure for one day (phases, variants, spillover, Day 3→50 map):
> `docs/internal/daily-coding-workflow.md`.

## Daily loop (low-ceremony)

```
/plan-day N   -> manager drafts day-<N>-plan.md  ->  reviewer PLAN-REVIEW gate (APPROVE PLAN / REQUEST PLAN CHANGES)
   |                                                            |
   |  (human approves; Plan status = APPROVED)                  v
   +--> choose one human-approved execution mode:
        - /implement-task X.Y  -> implement + static review + targeted verification; stop after one task
        - /execute-day N       -> run the same task loop serially for every remaining task; commit each task
                                  and stop at IMPLEMENTED — AWAITING /audit-day N
                                      |
                                      v
/audit-day N  -> sole default full-regression owner; writes day-<N>-checklist.md
                 (DoD ✅/❌, verification, carry-over) before day close/push
```

Human stays in the loop at exactly **2 gates**: approve the plan (including batch authorization),
and sign DoD before the day is closed/pushed.
Everything else is mechanical. Quality invariants are still enforced deterministically by
`.githooks/`, CI, NetArchTest and `dotnet format` — independent of prompt wording.

Task execution uses the plan's exact `DOCS`, `FOCUSED`, or `PROJECT` checks. It does not run the
full solution/workspace regression matrix by default. `/audit-day N` always runs that matrix for
everything the day touched; it does not reuse or trust worker/reviewer evidence. `/verify` and
`smoke-test` remain available for an intentional troubleshooting or runtime checkpoint, but are
not an extra mandatory gate after every task and never replace the final audit.

## Conventions

- File names: lowercase, `day-<N>-plan.md` / `day-<N>-checklist.md` (N = timeline day number).
- Copy `_TEMPLATE-day-plan.md` / `_TEMPLATE-day-checklist.md`; the skills do this for you.
- Do not paste DDL or full API contract here — cite the source file + section (same rule as BSOT).
- These files ARE committed (unlike SignalDesk where handoff was local) — they are the team's
  audit trail for the capstone.
- **Commit cadence depends on the authorized mode:** `/execute-day` creates one commit per
  approved task; `/implement-task` updates the tracker and stops without committing. Do not push
  during either mode. After implementation (and all batch task commits), run `/audit-day N`, let
  the human sign the DoD, then close/push the day. The `## Progress tracker` remains the explicit
  resume point for manual execution.
- The plan's `## Progress tracker` is **informational only** — `/audit-day` re-verifies every
  task independently against the SOT and never trusts the tracker, task verification evidence,
  or a worker/reviewer self-report.
