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
1. **Truth-correct?** Delivered code matches the source-of-truth, in this conflict order
   (same as AGENTS.md): `SU26SE101_VIETRIDE_technical_context_v7.md` (business) >
   `VietRide_API_Contract_v1.md` (API) > `BACKEND_SOURCE_OF_TRUTH.md` (impl conventions/
   registries) > ADRs > `BE_TIMELINE_VU.md` > db-schema.
   A file existing but diverging from truth = a bug, not a pass.
2. **DoD met?** Every Day-N **DoD** + **Review** bullet in `BE_TIMELINE_VU.md` is satisfied.

Read `docs/handoff/day-<N>-plan.md` and the Day-N timeline entry first. Verify by opening
files and quoting evidence — do not trust filenames, a worker's self-report, **or the plan's
`## Progress tracker` table**. That tracker is orchestrator bookkeeping, NOT audit evidence: a
✅ there means "reviewer approved during the day", not "audit-verified". Re-run the verification
matrix and re-read the code against the SOT regardless of what the tracker says — a task marked
✅ that fails verification here is a ❌, and the checklist records the real result.

## Verification matrix (run EVERYTHING the day touched — every tier must PASS to close)
This skill is self-contained: it does NOT delegate behavioral checks to `/verify`. Run each tier
below for whatever the day touched, record the **exact** result (counts, status codes, real output),
and a skip only with a written reason. **A green `dotnet test` + healthy `/health` is necessary but
NOT sufficient** — the day is only done when the real running app passes the business E2E too. The
day's `docs/handoff/day-<N>-checklist.md` `## Verification run` table is the template for the depth
expected (see Day-3's table: per-solution build/format/test with unit+integration counts, full TS
suite, EF fresh-DB apply/rollback, real containers healthy, health matrix, and a real-app E2E).

**1. Static / deterministic (.NET, per touched solution)** — all must be `0 Warning(s) 0 Error(s)`
and tests green, recording both unit AND integration counts separately:
```
dotnet build  apps/<svc>/VietRide.<Svc>.sln -c Release
dotnet format apps/<svc>/VietRide.<Svc>.sln --verify-no-changes
dotnet test   apps/<svc>/VietRide.<Svc>.sln -c Release      # unit + integration + NetArchTest layering; record e.g. "integration 15/15, unit 69/69"
```
Also run `libs/dotnet/VietRide.Libs.sln` when shared libs changed.

**2. TS / NestJS suite (matches CI)** — when the day touched gateway/workers/shared TS:
```
npx nx run-many -t build --all --exclude="VietRide.*"
npx nx run-many -t lint  --all --exclude="VietRide.*"
npx nx run-many -t test  --all --exclude="VietRide.*" --ci --passWithNoTests
```

**3. EF migration check (only when the day shipped or changed a migration)** — you do NOT need to
rebuild the whole DB from zero every day. A fresh-from-empty apply only replays **DDL** (it does
*not* re-run Day 3/4/5 tests or business logic) and is per-service, so it's cheap — but it's still
wasted work when no migration changed this day. Default: apply the day's new migration onto the
current dev DB, confirm its `Down()` reverses cleanly, then re-apply:
```
dotnet ef database update -p apps/<svc>/src/VietRide.<Svc>.Infrastructure -s apps/<svc>/src/VietRide.<Svc>.Api                  # apply the day's pending migration
dotnet ef database update <PrevMigration> -p apps/<svc>/src/VietRide.<Svc>.Infrastructure -s apps/<svc>/src/VietRide.<Svc>.Api  # run its Down() — must reverse cleanly
dotnet ef database update -p apps/<svc>/src/VietRide.<Svc>.Infrastructure -s apps/<svc>/src/VietRide.<Svc>.Api                  # re-apply
```
Do the stronger fresh-from-empty apply on a throwaway DB (apply full chain → inspect tables/triggers/
extensions → roll back to `0` → drop), like Day-3's temp-DB run, only when migration **history
itself** changed (a migration was squashed/reordered/edited) or at a cluster/milestone audit.

**4. Bring up the REAL environment (not just infra)** — production-like containers, then confirm health:
```
docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"     # every app + infra container healthy/up
```
`--build` rebuilds the app images so the containers run the day's **new code** — keep it when the day
changed app code (the usual case). If no app code changed since the last build, **drop `--build`**
(`... --profile app up -d`) to skip the rebuild and start faster; for infra-only days use
`--profile infra up -d`. Without `--build` after a code change, containers may run **stale** code and
the E2E would verify the wrong build.
Then the `/health` matrix through the Gateway and per service (gateway 3000, identity 5001, trip 5002,
booking 5003, payment 5004, parcel 5005, tracking 3001, notification 3002, rag 3003) — all HTTP `200`.

**5. Real-app business E2E through the Gateway (MANDATORY — this is the gate `/health` can't prove)**
Exercise the day's key flow(s) end-to-end against the running stack via the Gateway (`:3000`, the
real route prefixes in `apps/gateway/src/config/routes.ts`). **Pick ONE execution vehicle, don't do
both:** the timeline mandates a cumulative Postman collection (Day-4 Review, the per-PR convention,
and the external reviewer runs it) — it lives at `docs/api/postman/vietride.postman_collection.json`
with env `docs/api/postman/vietride.local.postman_environment.json`. If the day's flow is covered
there, run that collection against the Docker stack (`npx newman run ... -e ...`) and let it BE this
tier-5 E2E + the deliverable check at once; only fall back to `curl.exe` on PowerShell when no
collection covers the flow yet. Drive the full happy path AND the Day-N "Review" adversarial case,
and confirm the side effects in the DB / Outbox / RabbitMQ — exactly like Day-3's "Final Day-3 auth E2E via Gateway"
(`register 201 → OTP from DB → verify 200 → login 200 → refresh rotated → logout 204 → DB ACTIVE`).
Record the real status codes + observed state; **redact tokens/secrets** in the evidence. If the
day's behavior cannot be driven E2E **because it wasn't run**, that is a ❌ — not a skip. The ONLY
legitimate skip is a flow blocked by a genuinely-unavailable external credential (a real Google ID
token, a third-party sandbox); see the Review-bullet scoring rule below for how such an inherent skip
affects the day's status.

Scope is the day's own flows — the full `dotnet test` + `nx test` suites (tiers 1–2) already re-run
prior days' tests, so they are the regression net for older flows. BUT if the day touched
**shared/cross-cutting code** (Gateway auth/proxy middleware, the shared `ApiResponse` envelope, any
`libs/` shared lib, JWKS), also drive **one** quick E2E of an affected earlier flow — a shared change
can break runtime in ways unit tests don't cover.

**6. Hard invariants** — re-confirm CPM (no `.csproj` `Version=`), banned deps / MediatR v12+,
no `Co-Authored-By`, line endings (`git ls-files --eol`). Hook/CI-enforced, but the checklist records
they held.

Record every row in the checklist's `## Verification run` table with the exact command + PASS/FAIL +
notes. Any tier failing (build, format, unit, integration, TS, migration, container health, E2E,
invariant) means the day is **not** ✅ READY.

### Day-N "Review" bullet — execution-required, and how to score it
Never mark a row PASS unless the command/check was **actually executed** — record the exact command/
tool, target base URL, and the flows covered. The Day-N "Review" bullet is **execution-required**
unless the timeline explicitly says artifact-only. If it mentions curl/Postman/collection/manual API
path/E2E/smoke flow, run it against the Docker/local stack — **artifact validation alone is NOT a
PASS** ("the Postman JSON parses" / "the collection has the requests" proves nothing ran). Split it
into separate rows so artifact ≠ execution:

| `/health` matrix (tier 4)                     | PASS/FAIL      | liveness/readiness only |
| Review artifact validation                    | PASS/FAIL      | collection/spec exists + parses |
| Review execution against Docker/local stack   | PASS/FAIL/SKIP | the actual functional flow ran |

**SKIP vs ❌ — and how it affects status:** a flow that *could* run but wasn't is a ❌. A flow
blocked by a genuinely-unavailable external dependency in dev (a real Google ID token, a third-party
sandbox) is **SKIPPED with a written reason** — but still test the part you CAN (e.g. that a
forged/expired token is rejected, request validation), and skip only the external leg. Such an
**inherent** skip does NOT by itself block ✅ READY, *provided* the testable portion passed and the
limitation is recorded as a known env note. Reserve **⚠️ CLOSED-WITH-GAPS** for an **avoidable**
skip (could have been set up but wasn't) or when the testable portion failed. Record one final
`Day-N Review bullet overall` row reflecting that judgement.

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
