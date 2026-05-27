# CLAUDE.md — VietRide Backend (SU26SE101 Capstone)

The project rules, invariants, conventions, commands and directory map live in `AGENTS.md`
(shared with OpenCode / Codex CLI). Imported below — read it first:

@AGENTS.md

---

## Claude Code-specific tooling in this repo

These are Claude Code features (other tools ignore them). The **hard invariants** are
additionally enforced by git hooks in `.githooks/`, so enforcement does not depend on Claude.

**Hooks & settings**

- `.claude/settings.json` — shared hooks + read-only command allowlist (committed).
- `.claude/hooks/pre-guard.mjs` (PreToolUse) — blocks: `Co-Authored-By` / `--no-verify` commits; `Version=` on a `.csproj` `<PackageReference>` (CPM); banned deps in `Directory.Packages.props`/`package.json` (AutoMapper, OpenTelemetry/Prometheus/Grafana/Tempo/Loki, MediatR v12+).
- `.claude/hooks/format-on-edit.mjs` (PostToolUse) — formats only the edited file (`dotnet format --include` for `.cs`, `prettier --write` for TS/JSON/MD/YAML). Best-effort, never blocks.

**Skills** (`.claude/skills/`) — invoke via `/<name>`

- `scaffold-aggregate` — new .NET aggregate across all 4 Clean Architecture layers (BSOT §3.2/§3.5).
- `add-endpoint` — .NET controller (→ MediatR) + ProblemDetails + idempotency + Gateway route.
- `ef-migration` — per-service EF migration via `IDesignTimeDbContextFactory`.
- `add-integration-event` — Outbox event + routing key `<svc>.<aggregate>.<verb_past>`.
- `smoke-test` — bring up the stack + `/health` matrix.

**Subagents** (`.claude/agents/`) — a backend "department"

- `manager` (read-only planner) — turns a timeline day/feature into a dispatchable task list. It plans; the main thread dispatches.
- `worker` — general/cross-cutting implementation (infra, docs, scripts, db-schema, CI).
- `reviewer` — general cross-cutting + seams review (read-only).
- `dotnet-worker` / `dotnet-reviewer` — implement / review .NET service code.
- `nest-worker` / `nest-reviewer` — implement / review NestJS + TS code.
- (Workers edit code; reviewers are read-only. Built-in `/code-review`, `/security-review`, `/verify`, `/run` are NOT duplicated.)
