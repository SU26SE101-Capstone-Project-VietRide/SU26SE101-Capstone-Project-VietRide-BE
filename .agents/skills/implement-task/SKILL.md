---
name: implement-task
description: Implement and review ONE task from an approved VietRide day plan. Uses the task verification tier and exact targeted commands, allows documented in-scope supporting-file expansion, and keeps the implementer patching until a separate static reviewer approves. Stops after the task; full regression belongs to /audit-day.
---

# Implement one approved task

`$ARGUMENTS` is task id `<N>.<X>` (for example `22.3`). Derive the plan path as
`docs/handoff/day-<N>-plan.md`. If the argument is absent or malformed, ask for it.

This skill executes exactly one task. `/execute-day N` uses the same task loop for every
remaining task in a human-authorized batch.

## Preconditions and baseline

Before dispatching an agent:

1. Read `AGENTS.md`, applicable nested instructions, and the complete task block in the plan.
2. Require the normalized `Plan status` field value to be exactly `APPROVED`, resolved
   task-blocking questions, and completed dependencies.
3. Record `git status --short`, the current task diff boundary, and unrelated work to preserve.
4. Extract: implement/review agents, skill, owned files, forbidden scope, acceptance, invariant
   flags, source citations, auto-expand scope, verification tier, verification commands, and
   full regression owner.

Do not patch acceptance or scope to make an implementation pass. A genuine missing business,
API, or schema decision is a stop condition.

### Legacy plan compatibility

When an approved older plan lacks verification fields:

- use `DOCS` for docs/contracts/registry-only work;
- interpret generic `build`, `format`, `tests pass`, or `NetArchTest green` as `FOCUSED`;
- use `PROJECT` for DI, global middleware/filter, DbContext-wide behavior, shared fixtures, or
  project test configuration;
- derive the smallest exact commands from the task's acceptance and affected files; and
- record any solution/workspace-wide command as `deferred to /audit-day N`.

Never infer that a generic legacy acceptance requires full solution or workspace regression.

## Targeted verification policy

`full regression owner` must be `audit-day`. Per-task tiers are:

- `DOCS`: parse/syntax/citation/registry consistency plus `git diff --check`; no application
  build.
- `FOCUSED` (default): exact affected test class/spec, changed-file format/lint, and only the
  smallest compile target required.
- `PROJECT`: affected test project or Nx project for cross-cutting project behavior; never the
  full solution/workspace by default.

For .NET, prefer
`dotnet test <test.csproj> --filter FullyQualifiedName~<ClassOrNamespace>` and verify that at
least one test executed. That command compiles referenced production projects, so do not add a
redundant solution build. If no relevant test project references the code, build the smallest
affected `.csproj`. Format only changed C# paths with
`dotnet format <sln> --verify-no-changes --include <changed paths>`.

For NestJS/TS, run affected specs or the affected Nx project, lint changed files, and build only
the affected Nx project when production source changed. Do not use `run-many --all`.

Migration tasks retain their required generate/apply/Down/reapply/pending-model checks. These
are task-specific checks, not full regression. Agents may read the complete targeted output;
do not add quiet wrappers or log summarizers.

## Step 1 - Implement and verify once

Start the task's named implementer. Reuse the session for patches when continuation is
available; otherwise use a fresh agent with a compact handoff. Dispatch this template with only
`<N>` and `<X.Y>` substituted:

> Implement Task `<X.Y>` from `docs/handoff/day-<N>-plan.md`. Read the complete task block and
> its cited SOT sections, then discover only the existing patterns needed for the task. Satisfy
> behavior-focused acceptance and invariant flags. Follow the task's named `skill` in full when
> it is not `(none)`; its mandatory focused checks must already be present in the plan's exact
> commands, so do not append hidden checks. Treat `owned files` as the baseline write
> set, `forbidden scope` as a hard fence, and `auto-expand scope` as the authorized supporting
> file envelope. Record each expansion with path, reason, and acceptance/citation. Run exactly
> the task's targeted `verification commands` for its `verification tier`; confirm a selected
> test command executes at least one test. Do not run full solution/workspace regression. Read
> and report complete command results, files changed, scope expansions, deviations, and blockers.

If the plan is legacy, include only the derived tier and exact targeted commands in the compact
handoff; do not add task-specific implementation prose.

Before review, require green targeted evidence or an explicit environment blocker. Capture the
explicit baseline/commit range or path-scoped cumulative task diff, exact changed paths, scope
ledger, every exact command, exit/result, non-zero test count, and complete targeted output from
the worker. Pass them verbatim in the reviewer handoff, or pass concrete readable diff/transcript
locators. The plan contains commands, not runtime evidence. Do not summarize/truncate output,
create a quiet wrapper, or rerun worker verification from the orchestrator.

## Step 2 - Static review and continuous patching

Start the separate named reviewer. Reuse that reviewer session for re-review when possible.
Dispatch:

> Statically review only the supplied cumulative diff boundary for Task `<X.Y>` against its acceptance, invariant
> flags, source citations, forbidden scope, scope-expansion ledger, and the targeted verification
> evidence supplied with this handoff (the plan defines commands but does not contain runtime
> output). Ignore unrelated pre-existing diffs outside the supplied baseline/range and exact task
> paths. Read the complete targeted logs, but do not
> execute build, test, lint, or format. If evidence is missing or inadequate, return a finding
> with the exact command the implementer must run. End with **APPROVE** or **REQUEST CHANGES**;
> every finding needs file:line and a concrete fix.

On `REQUEST CHANGES`:

1. Return the findings to the implementer.
2. Patch within the authorized concern and update the scope ledger.
3. Rerun only invalidated checks:
   - docs/comment-only patch: parse/static checks, changed-file format if applicable, and
     `git diff --check`;
   - production/test patch: its focused test/filter;
   - DI/global/shared-contract/fixture patch: the affected `PROJECT` check;
   - migration/model patch: required migration checks.
4. Pass the new complete command output/result and changed-path update to the reviewer and
   re-review from the same task baseline.

Continue while each round makes concrete progress. There is no arbitrary one-round limit. Stop
after the same finding survives two consecutive rounds with no addressing diff, or for a hard
blocker listed below.

## Automatic scope expansion

Do not ask the human merely to add a necessary file already inside the task concern. The worker
may add and edit:

- a new file in the same feature/service/layer;
- affected unit/integration/spec files;
- DI/config registration for the implemented type;
- an interface and implementation pair;
- generated migration/designer/model snapshot files;
- a Gateway route required by the approved endpoint; or
- a producer/consumer shared contract named by acceptance or citations.

Each addition goes into the scope ledger. Stop instead of expanding to a new concern/service,
new dependency, unapproved API/business/schema behavior, secrets, destructive data work, or
unrelated overlapping changes.

## Step 3 - Tracker and hand-back

After `APPROVE` and green targeted evidence, update only the task row in `## Progress tracker`:
Status, Review verdict, Date, and a concise note with patch rounds or scope expansion. Do not
change task acceptance or plan scope.

Report task id, cumulative files changed, scope expansions, exact targeted commands/results,
review verdict, deferred full-regression items, and the next task id. Then stop. Do not chain,
commit, push, create a PR, run `/audit-day`, or claim the day is ready.

The human may inspect behavior or continue with the next `/implement-task`. After all tasks,
`/audit-day N` runs the full touched-solution/workspace regression before day close and push.

## Hard stop conditions

Stop with the current diff, evidence, and exact blocker only for:

- a missing or conflicting SOT decision;
- a new dependency/package, secret-bearing action, or destructive operation;
- work outside the approved concern/service envelope;
- unrelated user changes that cannot be preserved safely;
- migration/environment failure after focused diagnosis;
- the same finding receiving no addressing diff for two consecutive rounds; or
- an implement/review agent failure after one fresh retry.

Never create a worktree, add a package, create a branch, commit, push, open a PR, use
`--no-verify`, or add a `Co-Authored-By` trailer. Do not stop merely because the reviewer asks
for another in-scope patch or an auto-authorized supporting file.
