---
name: audit-day
description: Close out a VietRide timeline day. Independently audits the day's delivered code against the source-of-truth (technical_context_v7 + API contract + BSOT + db-schema) and the Day-N DoD/Review in BE_TIMELINE_VU.md, runs the verification matrix, then writes docs/handoff/day-<N>-checklist.md (DoD result + verification + carry-over for Day N+1). Use at end of day before commit (e.g. /audit-day 3).
---

# Audit & close a backend day

Parametric generalization of `docs/internal/day-1-2-review-prompt.md` — same rigor, only the
day number changes, so end-of-day verification is identical every day (no per-day prose drift).

`$ARGUMENTS` = the timeline day number `N`. If absent, ask which day.

## Method (read-only audit — do NOT fix code here)
Answer two questions; **both** must pass to call the day done:
1. **Truth-correct?** Delivered code matches the source-of-truth, in this conflict order:
   `SU26SE101_VIETRIDE_technical_context_v7.md` (business) > `VietRide_API_Contract_v1.md`
   (API) > `BACKEND_SOURCE_OF_TRUTH.md` (impl conventions/registries) > ADRs > db-schema.
   A file existing but diverging from truth = a bug, not a pass.
2. **DoD met?** Every Day-N **DoD** + **Review** bullet in `BE_TIMELINE_VU.md` is satisfied.

Read `docs/handoff/day-<N>-plan.md` and the Day-N timeline entry first. Verify by opening
files and quoting evidence — do not trust filenames or a worker's self-report.

## Verification matrix (run, record exact result)
Run only what the day touched; record skips with a reason. Typical:
```
dotnet build apps/<svc>/VietRide.<Svc>.sln -c Release
dotnet format apps/<svc>/VietRide.<Svc>.sln --verify-no-changes
dotnet test  apps/<svc>/VietRide.<Svc>.sln          # incl. NetArchTest dependency rules
dotnet ef database update -p apps/<svc>/src/VietRide.<Svc>.Infrastructure -s apps/<svc>/src/VietRide.<Svc>.Api   # from empty DB
```
Plus the `smoke-test` skill (`/health` matrix) and the Day-N "Review" bullet from the timeline.
Also re-confirm the hard invariants (CPM, banned deps, no `Co-Authored-By`, line endings) —
these are hook/CI-enforced, but the checklist records that they held.

## Output — write the checklist
Write `docs/handoff/day-<N>-checklist.md` using `docs/handoff/_TEMPLATE-day-checklist.md`:
DoD result (✅/❌ + evidence per line), tasks completed, changed files, verification table
(exact command + pass/fail), contract/event/schema changes shipped, **known gaps + carry-over
for Day N+1**, notes for next planning. Status = ✅ READY / ⚠️ CLOSED-WITH-GAPS / ❌ BLOCKED.

If a new event/error/convention landed, flag that it must be appended to the BSOT registry +
changelog (§13) — and whether that was done.

## Guardrails
- Read-only: this skill writes only `docs/handoff/day-<N>-checklist.md`. No code edits, no
  commits. If the audit finds gaps, list them as carry-over — fixing is a separate worker task.
- Be honest: if verification failed but the human closes the day anyway, record the failure
  and mark CLOSED-WITH-GAPS. Never claim green when it isn't.
