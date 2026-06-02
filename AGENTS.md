# AGENTS.md — VietRide Backend (SU26SE101 Capstone)

> Shared agent guide for **any** coding tool (Claude Code, OpenCode, Codex CLI, …).
> Claude Code reads this via `@AGENTS.md` in `CLAUDE.md`; OpenCode and Codex CLI read
> `AGENTS.md` natively. The **hard invariants** below are additionally enforced by git
> hooks in `.githooks/` (tool-agnostic), so they hold even for manual commits.

Polyglot Nx 22 monorepo: 5 .NET 8 services (Clean Architecture) + API Gateway + 3 workers (NestJS),
6 .NET shared libs + 6 TS shared libs. Infra: Postgres 16 + Redis 7 + RabbitMQ 3.13.

## Communication — report to the human in Vietnamese

The skills/subagents in this repo are authored in English, but the human (BE lead, Vũ) works in
Vietnamese. **The orchestrator (main thread, whichever harness) MUST write its conversational
replies and hand-back reports to the human in Vietnamese by default** — including the summaries
after `/plan-day`, `/implement-task`, `/review-task`, `/audit-day`, `/verify`, and any general
Q&A. Do NOT make the human ask for a translation as a second step.

Scope — Vietnamese applies ONLY to the orchestrator's chat/report to the human. It does NOT
change repo artifacts, which keep their established language:
- **Subagent dispatch prompts** stay the verbatim English skill templates (translating them would
  break the "verbatim, only `<N>`/`<X.Y>` substituted" guardrail).
- **Committed artifacts** (`day-<N>-plan.md` / `day-<N>-checklist.md`, code, comments, commit
  messages, ADRs, BSOT changelog rows) keep their existing convention — English for code/plan,
  Vietnamese where the doc is already Vietnamese (e.g. BSOT prose).
- Technical identifiers (error codes, file paths, type names, commands) stay verbatim.

If a future human prefers another language, override this section locally.

## Source-of-truth hierarchy (when in conflict, higher wins)

1. `SU26SE101_VIETRIDE_technical_context_v7.md` — business rules, flows, enums, status machines (canonical for **business/domain**)
2. `VietRide_API_Contract_v1.md` — controller/DTO request-response shape
3. `BACKEND_SOURCE_OF_TRUTH.md` (BSOT) — canonical for **backend implementation**: project structure, conventions, error/event/job registry
4. `docs/adr/*.md` — accepted architecture decisions
5. `BE_TIMELINE_VU.md` — daily sprint plan (Day 3 = Identity: User + Auth)
6. `db-schema/<service>/schema.sql` + `README.md` — canonical DDL per service

> Rule: if BSOT contradicts `technical_context_v7`, BSOT is wrong — fix BSOT, bump its version (§13 changelog).

> **Working method (all agents/tools, before writing any code):** READ the SOT sections your
> task cites (above hierarchy) — extract the exact columns/enums/endpoints/status-rules, don't
> skim. Then DISCOVER the current repo state (existing patterns to mirror, migrations, stubs).
> **Never invent** a column/enum/endpoint/rule — if a needed fact is missing or ambiguous, STOP
> and ask, don't guess. Code-quality philosophy is **BSOT §3.2.3 (.NET) / §3.3.1 (NestJS)** —
> SOLID/clean-code as *balance, not dogma* (judgment over premature fragmentation; size numbers
> are review guidelines, not CI limits). (Claude Code agents restate this in `.claude/agents/*.md`.)

## Hard invariants (DO NOT violate — enforced by `.githooks/`)

- **Commits MUST NOT contain a `Co-Authored-By` trailer.** Capstone rule: contribution is attributed to the
  member only — no AI/co-author trailer. Never use `--no-verify`. (Enforced by `.githooks/commit-msg`.)
- **Line endings (per `.gitattributes`):** `.cs/.csproj/.sln/.props/.targets` = **CRLF**; `.ts/.tsx/.js/.json/.yml/.yaml/.md/.sh` = **LF**. Wrong EOL → .NET CI format check goes red.
- **Central Package Management is ON.** A `.csproj` `<PackageReference>` MUST NOT carry a `Version=` attribute. Versions live only in `Directory.Packages.props` as `<PackageVersion>`. (Enforced by `.githooks/pre-commit`.)
- **Observability v1 = Sentry + UptimeRobot + Serilog/Winston only** (BSOT §9.13). Do NOT add OpenTelemetry / Prometheus / Grafana / Tempo / Loki — they were removed deliberately. (Enforced by `.githooks/pre-commit`.)
- **Banned deps:** AutoMapper (use Mapster or manual mapping). **MediatR pinned v11.x** (v12+ commercial — do NOT upgrade). No commercial/paid deps.
- **Do not add a new .NET or TS dependency without explicit approval.**
- **Git worktrees are forbidden in this repo.** Do not create or enter a worktree — no
  `git worktree add`, and no agent/subagent worktree isolation. Dispatch subagents in the current
  working tree; if parallel work could conflict, STOP and ask the human. Worktree creation is
  blocked *before the tool runs* by a per-harness pre-tool guard: `.claude/hooks/pre-guard.mjs`
  (Claude Code — committed, every clone has it) and `.opencode/plugins/pre-guard.js` (OpenCode —
  local-only / git-ignored, so each OpenCode user must install that mirror; without it the rule is
  instruction-only for them). Git has no commit-time hook for worktree creation, so `.githooks/*`
  do NOT block it — those enforce the other invariants (no `Co-Authored-By`, CPM, banned deps) at
  commit time. A genuinely-needed worktree requires temporarily disabling the active guard.
  (`git worktree list/remove/prune` stay allowed for cleanup.) Rationale: many agent worktrees
  obscure which checkout holds the real diff and make later merges/cleanup error-prone.

## Domain conventions

- **Internal JWT** (Gateway → service): HS256, issuer `vietride-gateway`, audience `vietride-internal`, header `X-Internal-Auth: Bearer <jwt>`, TTL 120s, secret env `INTERNAL_JWT_SECRET`.
- **User Access Token**: RS256, JWKS from Identity, issuer `vietride-identity`, audience `vietride-api`.
- **Password hashing**: BCrypt.Net-Next, cost 12 (BSOT §2.1).
- **Money**: BIGINT (VND), floor to 1000 before persisting (`Money.FromRaw`). No decimals.
- **Persistence**: EF Core, one DbContext per service, snake_case schema, soft-delete (`deleted_at timestamptz` only, partial unique index `WHERE deleted_at IS NULL`, see ADR 0003), `is_active` is a **separate** activation flag (not part of soft-delete — entities that need enable/disable implement `IActivatable`; `User` has no `is_active`, it uses its `status` enum), Outbox pattern. EF migrations run WITHOUT booting the host via per-service `IDesignTimeDbContextFactory`.
- **Messaging**: RabbitMQ topic exchange `vietride.events`, routing key `<svc>.<aggregate>.<verb_past>` (e.g. `identity.user.created`).
- **Responses/errors**: ADR 0004 `ApiResponse<T>` envelope — success `{success,statusCode,data,meta}`, error `{success:false,statusCode,error:{code,message,fields?},meta}`; `error.code` is UPPER_SNAKE_CASE from BSOT §5.9. `application/problem+json` (RFC 7807) is dropped as of ADR 0004 (2026-06-01).
- **Cross-DB FK is forbidden at DB layer** — logical FK only, enforced via HTTP/event (see `db-schema/_global/cross-service-references.md`).
- **Clean Architecture dependency direction** (CI-enforced via NetArchTest): Domain → (nothing); Application → Domain; Infrastructure → Domain+Application; Api → Application+Infrastructure. Controller calls `MediatR.Send`, never a service directly.
- **Naming** (BSOT §3.5, fixed): `<Verb><Aggregate>Command/Query/Handler/Validator`, `I<Aggregate>Repository`, `<Aggregate>Service`. One class per file.

## Build / test / lint commands

```bash
# Build everything (.NET + TS)
npx nx run-many -t build --all

# TS / NestJS only (matches CI — .NET is excluded)
npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests
npx nx run-many -t lint --all --exclude="VietRide.*"

# .NET per solution (6 solutions)
dotnet build <sln> -c Release
dotnet test  <sln>
dotnet format <sln> --verify-no-changes   # CI lint for .NET; must report no changes
```

Six solutions: `libs/dotnet/VietRide.Libs.sln` + `apps/{identity,trip,booking,payment,parcel}/VietRide.<Svc>.sln`.

EF migration (no host boot needed):
```bash
dotnet ef migrations add <Name> -p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api -o Migrations
```

CI: separate jobs — `lint`/`test-ts`/`build-ts` run Nx with `--exclude="VietRide.*"` (TS only); `build-dotnet` matrix runs restore → build → `dotnet format --verify-no-changes` → test per solution. Do not change anything that breaks these.

## Directory map

```
apps/{identity,trip,booking,payment,parcel}/   .NET 8 services (each = own .sln, 4 layers + 2 test projects)
apps/{gateway,tracking,notification,rag}/       NestJS (gateway = proxy + JWT; rest = workers)
  apps/gateway/src/config/routes.ts             route table /v1/* -> downstream service
libs/dotnet/VietRide.Shared.{Kernel,Application,Persistence,Messaging,Http,Web}/   .NET shared libs
libs/shared/{contracts,nest-common,nest-config,nest-persistence,nest-rabbitmq,nest-redis}/   TS shared libs
infra/docker/docker-compose.yml                 Postgres + Redis + RabbitMQ + PgBouncer + 9 app containers
db-schema/<service>/                            canonical DDL (do not move)
docs/{adr,api,runbooks,deliverables}/           all dev + generated docs (lowercase docs/ only)
```

Service ports (local): gateway 3000, identity 5001, trip 5002, booking 5003, payment 5004, parcel 5005, tracking 3001, notification 3002, rag 3003.

## Git hooks (tool-agnostic enforcement)

`.githooks/` holds the portable guard that runs on every commit regardless of which AI CLI
(or no CLI) created it. One-time per clone: `git config core.hooksPath .githooks` (auto-run by
the `prepare` npm script on `npm install`). Hooks:
- `commit-msg` — rejects a `Co-Authored-By` trailer.
- `pre-commit` — rejects `Version=` on a `.csproj` `<PackageReference>` (CPM) and banned deps
  (AutoMapper, OpenTelemetry/Prometheus/Grafana/Tempo/Loki, MediatR v12+) in staged
  `Directory.Packages.props` / `*.csproj` / `package.json`.

> `--no-verify` bypasses git hooks by design and cannot be blocked at the git layer — do not use it.
