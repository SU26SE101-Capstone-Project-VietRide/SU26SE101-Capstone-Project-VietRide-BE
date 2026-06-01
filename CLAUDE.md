# CLAUDE.md — VietRide Backend (SU26SE101 Capstone)

The project rules, invariants, conventions, commands and directory map live in `AGENTS.md`
(shared with OpenCode / Codex CLI). Imported below — read it first:

@AGENTS.md

---

## Communication — orchestrator → human (REPORT IN VIETNAMESE)

The skills/subagents in this repo are authored in English, but the human (BE lead, Vũ) works
in Vietnamese. **The main thread (orchestrator) MUST write its conversational replies and
hand-back reports to the human in Vietnamese by default** — including the summaries it produces
after `/plan-day`, `/implement-task`, `/audit-day`, `/verify`, and any general Q&A. Do NOT make
the human ask for a translation as a second step.

Scope — Vietnamese applies ONLY to the orchestrator's chat/report to the human. It does NOT
change repo artifacts, which keep their established language:
- **Subagent dispatch prompts** stay the verbatim English skill templates (translating them
  would break the "verbatim, only `<N>`/`<X.Y>` substituted" guardrail).
- **Committed artifacts** (`day-<N>-plan.md` / `day-<N>-checklist.md`, code, comments, commit
  messages, ADRs, BSOT changelog rows) keep their existing convention — English for code/plan,
  Vietnamese where the doc is already Vietnamese (e.g. BSOT prose).
- Technical identifiers (error codes, file paths, type names, commands) stay verbatim.

If a future human prefers another language, override this section (or scope it personally via
`CLAUDE.local.md`).

---

## Claude Code-specific tooling in this repo

These are Claude Code features (other tools ignore them). The **hard invariants** are
additionally enforced by git hooks in `.githooks/`, so enforcement does not depend on Claude.

**Hooks & settings**

- `.claude/settings.json` — shared hooks + read-only command allowlist (committed).
- `.claude/hooks/pre-guard.mjs` (PreToolUse) — blocks: `Co-Authored-By` / `--no-verify` commits; `Version=` on a `.csproj` `<PackageReference>` (CPM); banned deps in `Directory.Packages.props`/`package.json` (AutoMapper, OpenTelemetry/Prometheus/Grafana/Tempo/Loki, MediatR v12+); and the same banned deps installed via the CLI (`dotnet add package` / `npm install`).
- `.claude/hooks/format-on-edit.mjs` (PostToolUse) — formats only the edited file (`dotnet format --include` for `.cs`, `prettier --write` for TS/JSON/MD/YAML). Best-effort, never blocks.

**Skills** (`.claude/skills/`) — invoke via `/<name>`

- `plan-day` — start a backend day: `manager` drafts `docs/handoff/day-<N>-plan.md`, then `reviewer` PLAN-REVIEW gate.
- `implement-task` — execute ONE task from an approved plan: dispatches the task's `implement agent` + `review agent`, loops once on REQUEST CHANGES, stops for human `/verify`. Per-task granularity (no `/implement-day`).
- `audit-day` — close a backend day: audit code vs SOT + run verification matrix → `docs/handoff/day-<N>-checklist.md`.
- `scaffold-aggregate` — new .NET aggregate across all 4 Clean Architecture layers (BSOT §3.2/§3.5).
- `add-endpoint` — .NET controller (→ MediatR) + ProblemDetails + idempotency + Gateway route.
- `ef-migration` — per-service EF migration via `IDesignTimeDbContextFactory`.
- `add-integration-event` — Outbox event + routing key `<svc>.<aggregate>.<verb_past>`.
- `smoke-test` — bring up the stack + `/health` matrix.

**Daily loop** — `/plan-day N` (plan + PLAN-REVIEW gate) → human approves + resolves open Qs →
`/implement-task X.Y` per task (worker + review per task, serial; the skill stops after each so
the human runs `/verify`) → `/audit-day N` (DoD + verification → checklist) → commit.
Plan/checklist are committed handoff artifacts in `docs/handoff/` (see its README) — they survive
a session ending mid-day, and Day N+1 planning reads Day N's checklist. There is intentionally
NO `/implement-day` skill — per-task granularity is the gate.

**Subagents** (`.claude/agents/`) — a backend "department"

- `manager` (read-only planner) — turns a timeline day/feature into a dispatchable task list. It plans; the main thread dispatches.
- `worker` — general/cross-cutting implementation (infra, docs, scripts, db-schema, CI).
- `reviewer` — general cross-cutting + seams review (read-only).
- `dotnet-worker` / `dotnet-reviewer` — implement / review .NET service code.
- `nest-worker` / `nest-reviewer` — implement / review NestJS + TS code.
- (Workers edit code; reviewers are read-only. Built-in `/code-review`, `/security-review`, `/verify`, `/run` are NOT duplicated.)
