# Day &lt;N&gt; — Final checklist

> Produced by `/audit-day N` AFTER all tasks are done and verification ran.
> Honest record: if verification failed but the day was closed, say so. Don't claim green.

- **Timeline ref**: BE_TIMELINE_VU.md → Day &lt;N&gt; (Jira: SCV-___)
- **Plan**: docs/handoff/day-&lt;N&gt;-plan.md
- **Status**: ✅ READY / ⚠️ CLOSED-WITH-GAPS / ❌ BLOCKED

## DoD result
- [ ] … (each Day-N success-criterion line, ✅/❌ with one-line evidence)

## Tasks completed
- Task N.0 — &lt;title&gt; — ✅ / ⚠️ / ❌
- …

## Changed files
- path — what changed

## Verification run
| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/<svc>/VietRide.<Svc>.sln -c Release` | pass/fail | |
| `dotnet format … --verify-no-changes` | pass/fail | |
| `dotnet test …` (incl. NetArchTest) | pass/fail | |
| `dotnet ef database update` (from empty DB) | pass/fail | |
| `/health` matrix (smoke-test) | pass/fail | |
| Day-N "Review" bullet from timeline | pass/fail | |

## Contract / event / schema changes shipped
Endpoints, routing keys, migrations, error codes. `none` if no change.
**Cross-check**: if a new event/error/convention landed, it MUST be appended to the BSOT
registry + changelog (§13).

## Known gaps & carry-over for Day N+1
- gap / unfinished item → handled how, when
- preconditions Day N+1 needs from Day N

## Notes for Day N+1 planning
Anything the next `/plan-day` should know that isn't obvious from the repo or this checklist.
