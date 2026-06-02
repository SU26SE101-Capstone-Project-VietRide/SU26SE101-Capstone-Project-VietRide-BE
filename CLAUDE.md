# CLAUDE.md — VietRide Backend (SU26SE101 Capstone)

The project rules, invariants, conventions, commands and directory map live in `AGENTS.md`
(shared with OpenCode / Codex CLI). Imported below — read it first:

@AGENTS.md

> **The Vietnamese-reporting rule and the worktree policy now live in `AGENTS.md`** (shared with
> OpenCode, imported above) — report to the human in Vietnamese; git worktrees are forbidden.
> Claude Code reads them via the import; this file only adds the Claude-Code-specific *enforcement*
> and tooling below.

---

## Claude Code-specific tooling in this repo

These are Claude Code features (other tools ignore them). The **hard invariants** are
additionally enforced by git hooks in `.githooks/`, so enforcement does not depend on Claude.

**Worktree policy — Claude Code enforcement** (the policy itself is in `AGENTS.md`: git worktrees
are forbidden, an absolute block)

- If a worktree is ever genuinely needed, the human must **temporarily disable/adjust the hook**
  (remove the tool from the matcher in `.claude/settings.json`, or comment out the worktree branch
  in `pre-guard.mjs`) first, then restore it.
- **Enforced in `.claude/hooks/pre-guard.mjs` (PreToolUse), not just by instruction** — blocked
  (exit 2) regardless of model: (a) any tool call carrying `isolation:"worktree"`; (b) the
  `EnterWorktree`/`ExitWorktree` tool; (c) a raw `git worktree add` via Bash (`list`/`remove`/
  `prune` stay allowed for cleanup); (d) a `Workflow` script whose text requests
  `isolation:"worktree"`. This is the net for non-Claude gateway models (e.g. a `.claude-*`
  profile pointing at a non-Anthropic model) that ignore the soft rule and spawn one worktree per
  subagent. The settings matcher includes `Task`/`Agent`/`Workflow`/`EnterWorktree`/`ExitWorktree`.

**Hooks & settings**

- `.claude/settings.json` — shared hooks + read-only command allowlist (committed).
- `.claude/hooks/pre-guard.mjs` (PreToolUse) — blocks: `Co-Authored-By` / `--no-verify` commits; `Version=` on a `.csproj` `<PackageReference>` (CPM); banned deps in `Directory.Packages.props`/`package.json` (AutoMapper, OpenTelemetry/Prometheus/Grafana/Tempo/Loki, MediatR v12+); the same banned deps installed via the CLI (`dotnet add package` / `npm install`); and worktree creation — `isolation:"worktree"` dispatch, `EnterWorktree`/`ExitWorktree`, `git worktree add`, or a `Workflow` script requesting a worktree (worktree policy above).
- `.claude/hooks/format-on-edit.mjs` (PostToolUse) — formats only the edited file (`dotnet format --include` for `.cs`, `prettier --write` for TS/JSON/MD/YAML). Best-effort, never blocks.

**Skills** (`.claude/skills/`) — invoke via `/<name>`

- `plan-day` — start a backend day: `manager` drafts `docs/handoff/day-<N>-plan.md`, then `reviewer` PLAN-REVIEW gate.
- `implement-task` — execute ONE task from an approved plan: dispatches the task's `implement agent` + `review agent`, loops once on REQUEST CHANGES, stops for human `/verify`. Per-task granularity (no `/implement-day`).
- `review-task` — review ONE task implemented by a cheaper model (a gateway-profile session) so the authoritative review runs on strong Claude: dispatches only the task's `review agent` on the diff + updates the Progress tracker. The implement-task Step 2+3 split out so reviewer ≠ implementer by model; on REQUEST CHANGES it STOPS for the profile session to patch (re-review = re-invoke), does NOT dispatch an internal worker. Third invocation still failing → escalate to human.
- `audit-day` — close a backend day: audit code vs SOT + run verification matrix → `docs/handoff/day-<N>-checklist.md`.
- `scaffold-aggregate` — new .NET aggregate across all 4 Clean Architecture layers (BSOT §3.2/§3.5).
- `add-endpoint` — .NET controller (→ MediatR) + ApiResponse envelope (ADR 0004) + idempotency + Gateway route.
- `ef-migration` — per-service EF migration via `IDesignTimeDbContextFactory`.
- `add-integration-event` — Outbox event + routing key `<svc>.<aggregate>.<verb_past>`.
- `smoke-test` — bring up the stack + `/health` matrix.

**Daily loop** — `/plan-day N` (plan + PLAN-REVIEW gate) → human approves + resolves open Qs →
`/implement-task X.Y` per task (worker + review per task, serial; the skill stops after each so
the human runs `/verify`) → `/audit-day N` (DoD + verification → checklist) → commit.
Plan/checklist are committed handoff artifacts in `docs/handoff/` (see its README) — they survive
a session ending mid-day, and Day N+1 planning reads Day N's checklist. There is intentionally
NO `/implement-day` skill — per-task granularity is the gate.
**Split-model variant** — when a task is implemented by a cheaper model (a gateway-profile Claude
Code session) and you want the authoritative review on strong Claude, run `/review-task X.Y` in a
strong-Claude session to keep the review gate + tracker bookkeeping; `/verify` is then batched at
cluster boundaries. (If the same model implements and reviews, just use `/implement-task` — it
already bundles the review.)

**Subagents** (`.claude/agents/`) — a backend "department"

- `manager` (read-only planner) — turns a timeline day/feature into a dispatchable task list. It plans; the main thread dispatches.
- `worker` — general/cross-cutting implementation (infra, docs, scripts, db-schema, CI).
- `reviewer` — general cross-cutting + seams review (read-only).
- `dotnet-worker` / `dotnet-reviewer` — implement / review .NET service code.
- `nest-worker` / `nest-reviewer` — implement / review NestJS + TS code.
- (Workers edit code; reviewers are read-only. Built-in `/code-review`, `/security-review`, `/verify`, `/run` are NOT duplicated.)
