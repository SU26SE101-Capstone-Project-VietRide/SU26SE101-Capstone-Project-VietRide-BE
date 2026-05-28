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
   |  (plan approved)                                           v
   +--> dispatch each task to dotnet-worker / nest-worker / worker (one task at a time)
            |
            v
        /code-review (or dotnet-reviewer / nest-reviewer) on the diff per task
            |
            v
        /verify + smoke-test + the Day-N "Review" bullet from BE_TIMELINE_VU.md
            |
            v
/audit-day N  -> writes day-<N>-checklist.md (DoD ✅, verification, carry-over)
```

Human stays in the loop at exactly **2 gates**: approve the plan, and sign DoD before commit.
Everything else is mechanical. Quality invariants are still enforced deterministically by
`.githooks/`, CI, NetArchTest and `dotnet format` — independent of prompt wording.

## Conventions

- File names: lowercase, `day-<N>-plan.md` / `day-<N>-checklist.md` (N = timeline day number).
- Copy `_TEMPLATE-day-plan.md` / `_TEMPLATE-day-checklist.md`; the skills do this for you.
- Do not paste DDL or full API contract here — cite the source file + section (same rule as BSOT).
- These files ARE committed (unlike SignalDesk where handoff was local) — they are the team's
  audit trail for the capstone.
