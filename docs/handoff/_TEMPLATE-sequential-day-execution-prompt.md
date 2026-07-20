# Compatibility launcher — sequential execution of an approved day plan

The canonical workflow lives in `.agents/skills/execute-day/SKILL.md`. This file intentionally
contains no second copy of that workflow, so the reusable prompt cannot drift from the skill.

## Usage

Replace `<DAY_N>` with the timeline day number. In a harness that exposes project skills, invoke:

```text
/execute-day <DAY_N>
```

If slash commands are unavailable, paste this compatibility prompt into a new orchestrator session:

```text
Run the human-approved sequential batch for Day <DAY_N>. Read and follow
`.agents/skills/execute-day/SKILL.md` in full with `$ARGUMENTS = <DAY_N>`; treat that skill as the
only workflow authority. Do not reconstruct the old batch procedure from chat history or this
launcher. Finish at `IMPLEMENTED — AWAITING /audit-day <DAY_N>`; do not auto-audit, push, or create
a PR.
```
