---
name: execute-day
description: Execute every remaining task in an approved VietRide day plan sequentially in one human-authorized batch. Use after /plan-day when the human wants continuous implement-review-patch-commit loops with targeted per-task verification, automatic in-scope file expansion, and no full regression run until /audit-day.
---

# Execute an approved day sequentially

`$ARGUMENTS` = Day number `N`. If absent or malformed, ask for it. Read
`docs/handoff/day-<N>-plan.md`; never hard-code task ids in the invocation.

This is the explicit batch counterpart to `/implement-task`. It overrides only:

- one task per invocation;
- the manual mode's no-commit hand-back (this authorized batch commits each approved task);
- the human `/verify` stop between tasks; and
- exact-file stops inside the authorized scope envelope defined below.

All SOT, dependency, package, secret, destructive-operation, unrelated-change, git hygiene,
and hard-invariant guardrails remain in force. Never use a worktree.

## Phase 0 — Preflight and run manifest

Before dispatching a worker:

1. Read `AGENTS.md`, applicable nested instructions, this skill,
   `.agents/skills/implement-task/SKILL.md`, the approved Day-N plan, and its referenced prior
   checklist when present.
2. Require the normalized `Plan status` field value to be exactly `APPROVED` (not merely a line
   containing that word), no unresolved blocking question/SOT conflict, and satisfied task
   dependencies. Stop when any condition fails; do not guess or patch the plan.
3. Inspect and preserve the baseline with `git status --short`, `git diff --stat`,
   `git diff --check`, and `git log --oneline -10`. Never stage, edit, or revert unrelated work.
4. Build the ordered manifest from `Dispatch order` plus `Progress tracker`. Skip only immutable
   or already-done tasks whose current-day acceptance does not require another action.
5. For every task record: implement/review agents, skill, dependencies, baseline owned files,
   auto-expand scope, forbidden scope, verification tier, exact verification commands, and full
   regression owner. Report a concise manifest, then start immediately.

### Legacy plan compatibility

For an approved plan without the new verification fields:

- map docs/contracts-only work to `DOCS`;
- map generic `build`, `format`, `tests pass`, or `NetArchTest green` wording to `FOCUSED`;
- map DI/global middleware/DbContext-wide behavior/test infrastructure to `PROJECT`;
- defer solution-wide/workspace-wide regression commands to `/audit-day N`; and
- retain focused migration and business/runtime checks explicitly required by the task.

Record each deferred full-regression item in the manifest and final hand-back. Do not claim it
passed during this batch.

## Targeted verification policy

`/audit-day N` is the sole default owner of full touched-solution and full TS-workspace
regression. This batch uses only:

- `DOCS`: parse/syntax/citation/registry checks plus `git diff --check`;
- `FOCUSED` (default): exact affected test class/spec, changed-file format/lint, and the smallest
  compile target needed; or
- `PROJECT`: the affected test project or Nx project when DI, global middleware/filter,
  DbContext-wide behavior, shared fixture, or test configuration changed.

For .NET, prefer `dotnet test <test.csproj> --filter FullyQualifiedName~<ClassOrNamespace>` and
confirm at least one test executed. A targeted test compiles its referenced production projects;
do not add a redundant solution build. When no applicable test project references the changed
code, build only the smallest affected `.csproj`. Check changed C# files with
`dotnet format <sln> --verify-no-changes --include <paths>`.

For NestJS/TS, run the affected spec(s) or the affected Nx project, lint changed files, and build
only the affected Nx project when production source changed. Never use `run-many --all` here.

Migration tasks still generate with the design-time factory and run the task-required
apply/Down/reapply/pending-model checks. These are focused schema checks, not full regression.

Let implementers read complete targeted-command output. Do not introduce quiet wrappers or hide
logs. The saving comes from smaller commands, not truncated evidence.

## Per-task loop

For each manifest task, strictly in order:

1. Read only its plan block, cited SOT sections, and the existing patterns needed for that task.
   Record an explicit cumulative task-diff boundary before editing: baseline commit/pre-task
   working-tree state plus the exact authorized paths. If an intended path already has unrelated
   changes that cannot be separated, stop under the overlap rule.
2. Start one implementer session and one separate reviewer session for the task. Reuse each
   session for patch/re-review when the harness supports continuation; otherwise dispatch fresh
   with a compact handoff containing task id, findings, cumulative diff boundary, and evidence.
3. Tell the implementer to follow the task's named feature skill when present, satisfy the task,
   remain inside the scope envelope, run the exact targeted checks once, confirm non-zero selected
   tests, and report complete command results. Mandatory skill checks must already be explicit in
   those plan commands; do not append hidden verification.
4. Before review, confirm targeted evidence is present and green. Pass the reviewer the explicit
   task baseline/commit range or path-scoped cumulative diff, exact changed paths, scope ledger,
   every exact command, exit/result, non-zero test count, and the complete targeted output
   verbatim in the handoff (or concrete readable diff/transcript locators). The plan contains
   commands, not runtime evidence. Never replace output with a summary or quiet wrapper, and do
   not rerun it from the orchestrator.
5. Tell the reviewer to inspect only the supplied cumulative task boundary against acceptance,
   invariants, citations, forbidden scope, scope-expansion ledger, and evidence; ignore unrelated
   pre-existing diffs outside that boundary. Review is static: the reviewer may read complete
   targeted logs but must not run build/test/lint/format.
6. On `REQUEST CHANGES`, send findings to the implementer, patch, and rerun only invalidated
   checks. Pass every new complete output/result and changed-path update into the reviewer handoff,
   then re-review from the same task baseline. Continue while each round makes concrete progress.
7. On `APPROVE` with green targeted evidence, update the tracker row, inspect the intended diff,
   stage only task files plus tracker bookkeeping, and create one task commit. Never amend an
   earlier commit, use `--no-verify`, add `Co-Authored-By`, push, or open a PR.

If a post-commit batch task exposes an earlier task defect, patch it through the responsible
worker/reviewer loop and create a separate corrective commit; do not rewrite history.

## Automatic scope expansion

Treat plan `owned files` as the baseline write set. Without asking the human, add a file to the
task scope when it is necessary for existing acceptance and is one of:

- a new file under the same feature/service/layer;
- an affected unit/integration/spec file;
- DI/config registration for the implemented type;
- an interface and its implementation pair;
- generated migration/designer/model snapshot files;
- a Gateway route already required by the approved endpoint; or
- a producer/consumer shared contract already named by acceptance/citations.

Record `path`, `reason`, and supporting acceptance/citation or reviewer finding in a scope ledger.
Do not auto-expand into a new service/concern, an unapproved API/business/schema decision, package
files for a new dependency, secrets, destructive data operations, or overlapping unrelated work.

## Verification invalidation after a patch

- Docs/comment-only patch: rerun parse/static checks only.
- Production or test logic patch: rerun the corresponding focused test/filter.
- DI/global/shared-contract/test-fixture patch: rerun the affected `PROJECT` check.
- Migration/model patch: rerun the required migration checks.
- Changed-file format/lint and `git diff --check`: rerun for every changed relevant file.

Never rerun a green unrelated check merely because another file changed.

## Stop conditions

Stop and report the task, current diff, evidence, and exact blocker only when:

- a required decision is absent from or conflicts across the SOT;
- a new dependency/package or destructive/secret-bearing action needs approval;
- the fix leaves the authorized concern/service envelope;
- unrelated user work overlaps and cannot be preserved safely;
- migration checks cannot complete after environment/design-time diagnosis;
- the same finding survives two consecutive rounds with no addressing diff or the agents cite an
  irreconcilable SOT interpretation; or
- an implement/review tool failure still fails after one fresh retry.

Do not stop merely because a reviewer requests another in-scope implementation patch or an
auto-allowed supporting file.

## Final hand-back

After the last task is committed, report tasks and commit hashes, targeted checks, scope
expansions, deferred full-regression items, carry-over, and remaining intended/unrelated work.
End with exactly the day-level state:

`IMPLEMENTED — AWAITING /audit-day <N>`

Do not call the day READY, auto-run `/audit-day`, push, or create a PR.
