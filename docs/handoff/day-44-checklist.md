# Day 44 — Final checklist

> Produced by `/audit-day 44` after an independent SOT/DoD review and a fresh verification run.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 44 (Jira: SCV-133)
- **Plan**: `docs/handoff/day-44-plan.md`
- **Audited delivery**: merge commit `4be707b1a37df6f75e4b7e44aae24f106bfabfae`; Day 44 implementation range `3523feb0..30fb670c`
- **Status**: ✅ READY

## DoD result

- [x] ✅ Runtime guard and deterministic manifest — `npm run seed:demo -- --start-date=<YYYY-MM-DD>` rejects Production, a missing runtime `DEMO_SEED_ACCOUNT_PASSWORD`, and a non-future ICT date before planning any write (`scripts/seed-dev-data.ts:144-157`); the manifest uses `schemaVersion: 1`, namespace `day44-v1`, timezone `Asia/Ho_Chi_Minh`, and fixed UUIDs only. The manifest artifact check and 53 focused tests passed.
- [x] ✅ Exact cross-service demo state — the isolated real-store gate verified 1 System Admin, 3 Operators, 3 Operator Admins, 9 Drivers, 3 Assistants, 10 Passengers, 2 plans, 3 subscriptions, 5 Stations, 9 Routes, 3 AlternativeRoutes, 9 Vehicles, 9 schedules, 126 Trips, 3,948 seats, 10 funded wallets, the exact 5-Voucher/2-consent matrix, 2 ParcelRouteFares, and 3 searchable RAG documents/chunks. The acceptance matrix is executable in `scripts/run-day44-seed-e2e.mjs:419-662`.
- [x] ✅ Reproducible and idempotent in under two minutes — two isolated runs took `62,531 ms` and `63,294 ms`; both emitted checksum `154307f1127c8b1452fcfa4cb097a3510c195257425371056f069abffcfddc17`, followed by `IDEMPOTENT_RERUN=PASS`.
- [x] ✅ Provider-independent RAG seed — offline attestation emitted `RAG_FIXTURE_PROVENANCE=PASS`; the committed fixture is model `nvidia/llama-nemotron-embed-vl-1b-v2:free`, dimension 2,048, with exactly 3 finite one-chunk vectors. The isolated provider trap observed zero `/embeddings` requests and the gate emitted `RAG_READY=PASS`.
- [x] ✅ Demo accounts can immediately transact — real Gateway smoke logged in `passenger01@demo.vietride.local`, created and idempotently replayed one wallet Booking and one wallet Parcel, checked tenant snapshots and non-negative wallet balances, then emitted `BOOKING_READY=PASS`, `PARCEL_READY=PASS`, and `DAY44_RUN=PASS` (`scripts/run-day44-seed-e2e.mjs:334-416`).
- [x] ✅ Foreign rows remain protected — preflight queries cover both fixed IDs and natural keys; any unowned collision or full-state drift throws before writes (`scripts/seed-dev-data.ts:643-679`, `716-1,131`). Focused tests cover random IDs, OAuth attachments, collision, partial ledger, and RAG attestation drift.
- [x] ✅ Timeline DoD — the seed script completed each real-store run in less than 2 minutes and the unique isolated Compose project was recreated from empty state.
- [x] ✅ Timeline Review — rerun idempotency passed with identical real-store checksum and no duplicate financial/event evidence; seeded passenger credentials immediately completed wallet Booking and Parcel flows through Gateway.

## Tasks completed

- Task 44.1 — Record the frozen deterministic manifest — ✅ audit-verified.
- Task 44.2 — Reconcile the RAG source-of-truth and generated diagram — ✅ audit-verified; BSOT is version `1.60.0` with a §13 changelog row.
- Task 44.3 — Build the deterministic Identity fixture module — ✅ focused and real-store evidence passed.
- Task 44.4 — Build the deterministic Trip fixture module — ✅ focused and real-store evidence passed.
- Task 44.5 — Build the deterministic commerce fixture module — ✅ focused and real-store evidence passed.
- Task 44.6 — Generate and attest the RAG embedding fixture once — ✅ mocked generator tests and offline provenance passed; no live regeneration was attempted.
- Task 44.7 — Build the offline RAG seed module — ✅ focused access/provenance tests and real pgvector checks passed.
- Task 44.8 — Orchestrate and prove the isolated real-store seed — ✅ two-run Docker E2E and Gateway business smoke passed.
- Task 44.9 — Document the verified demo handoff — ✅ runbook content, credential scan, and formatting passed.

## Changed files

- `.env.example` — blank runtime placeholders for demo password and OpenRouter key.
- `package.json` — registers `seed:demo` and `e2e:day44`; dependency sections are unchanged.
- `infra/docker/docker-compose.yml` — pins PostgreSQL timezone to `Asia/Ho_Chi_Minh` for deterministic ICT fixtures.
- `infra/docker/docker-compose.day44-e2e.yml` — isolated ports/containers, RAG provider trap, and cleanup-safe topology.
- `scripts/seed-dev-data.ts`, `scripts/seed-dev-data.test.ts` — cross-database preflight, deterministic batches, exact-state validation, and checksum tests.
- `scripts/run-day44-seed-e2e.mjs`, `scripts/run-day44-seed-e2e.test.mjs` — unique Compose project, two bounded seed runs, acceptance matrix, provider isolation, Gateway Booking/Parcel smoke, and unconditional cleanup.
- `scripts/day44/seed-identity.ts`, `scripts/day44/seed-identity.test.ts` — exact Identity accounts/plans/subscriptions fixture.
- `scripts/day44/seed-trip.ts`, `scripts/day44/seed-trip.test.ts` — exact Station/Route/Vehicle/Schedule/14-day Trip fixture.
- `scripts/day44/seed-commerce.ts`, `scripts/day44/seed-commerce.test.ts` — wallet, paid subscription saga, Voucher consent, and Parcel fare fixture.
- `scripts/day44/seed-rag.ts`, `scripts/day44/seed-rag.test.ts` — offline RAG documents/chunks and access matrix.
- `scripts/day44/generate-rag-fixture.ts`, `scripts/day44/generate-rag-fixture.test.ts` — one-time redacted generator plus offline verifier.
- `scripts/day44/fixtures/rag-embeddings.json`, `scripts/day44/fixtures/rag-embeddings.provenance.json` — attested 2,048-dimensional embeddings and checksums.
- `docs/rag/vietride-{public,operator,admin}-demo-knowledge-base.txt` — canonical demo RAG contents.
- `docs/handoff/day-44-plan.md`, `docs/handoff/day-44-demo-data-manifest.md`, `docs/handoff/day-44-demo-seed-runbook.md` — approved plan, frozen fixture contract, and operator handoff.
- `SU26SE101_VIETRIDE_technical_context_v7.md`, `BACKEND_SOURCE_OF_TRUTH.md` — current Cloudinary/OpenRouter/`halfvec(2048)` contract; BSOT version/changelog updated.
- `db-schema/_global/_drawio_generator.py`, `db-schema/_global/{README.md,SCHEMA_REVIEW_REPORT.md,ERD_DRAWING_MASTER.md}`, `db-schema/rag-ai/{README.md,schema.drawio,schema.sql}` — synchronized RAG diagrams/docs; `schema.sql` changes only the current chat-model header comment, not DDL.

## Verification run

| Command / check | Result | Notes |
|---|---|---|
| Independent SOT/DoD code review against technical context → API contract → BSOT → ADR/timeline → DDL | PASS | Fixed fixtures, roles, money, Voucher funding/consent, 14-day materialization, RAG access/model/dimension, API payloads, and collision behavior match the cited SOTs. |
| `node --test --require ts-node/register/transpile-only <all Day 44 test files>` | PASS | 53/53 tests, 7/7 suites; no skipped/todo tests. |
| `npx tsc --noEmit ... <all Day 44 TS files>` | PASS | No type errors. |
| `npx eslint <all Day 44 TS/MJS files>` | PASS | No lint errors. |
| `npx prettier --check <Day 44 code/config/docs assets>` | PASS | All matched files use Prettier style. |
| `node scripts/day44/generate-rag-fixture.ts --verify ...` with `OPENROUTER_API_KEY` removed | PASS | `RAG_FIXTURE_PROVENANCE=PASS`; no provider call. |
| Manifest, runbook, RAG fixture/provenance artifact validators | PASS | `MANIFEST_ARTIFACT=PASS`, `RUNBOOK_ARTIFACT=PASS`, `RAG_FIXTURE_ARTIFACT=PASS`. |
| `docker compose --env-file .env.example -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.day44-e2e.yml config --quiet` | PASS | Compose configuration resolves with blank secret placeholders and isolated override. |
| `nx run-many -t build --all --exclude="VietRide.*"` (`NX_DAEMON=false`) | PASS | 10 projects plus 3 dependency tasks. Webpack source-map/dependency warnings remain non-fatal. |
| `nx run-many -t lint --all --exclude="VietRide.*"` (`NX_DAEMON=false`) | PASS | 14 projects, 0 errors; 16 pre-existing warnings surfaced outside Day 44 files. |
| `nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` (`NX_DAEMON=false`) | PASS | 140/140 suites, 1,070/1,070 tests. `nest-config`, `nest-redis`, and `nest-persistence` have no tests and passed via `--passWithNoTests`. |
| `dotnet build/format/test libs/dotnet/VietRide.Libs.sln` | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; 190/190 unit tests. |
| `dotnet build/format/test apps/identity/VietRide.Identity.sln` | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; unit 344/344, integration 179/179. |
| `dotnet build/format/test apps/trip/VietRide.Trip.sln` on clean Day 44 merge | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; unit 673/673, integration 317/317. |
| `dotnet build/format/test apps/booking/VietRide.Booking.sln` on clean Day 44 merge | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; unit 608/608; integration 245/245 on the isolated audit PostgreSQL (`max_connections=300`). Two earlier dev-DB runs hit Npgsql connection-read timeouts at `max_connections=100`; no deterministic assertion failed. |
| `dotnet build/format/test apps/payment/VietRide.Payment.sln` on clean Day 44 merge | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; unit 231/231, integration 108/108. |
| `dotnet build/format/test apps/parcel/VietRide.Parcel.sln` on clean Day 44 merge | PASS | Build `0 Warning(s), 0 Error(s)`; format clean; unit 460/460, integration 86/86. |
| EF migration apply/down/re-apply | SKIP | Day 44 adds no migration and no physical DDL change. The RAG `schema.sql` diff is a header-comment correction only. Fresh real stores were nevertheless created by the isolated Docker E2E. |
| `npm run e2e:day44 -- --start-date=2026-08-11` on clean merge, random process-only password, no OpenRouter key | PASS | Runs `62,531 ms` and `63,294 ms`; identical checksum; all `IDEMPOTENT_RERUN`, `RAG_READY`, `BOOKING_READY`, `PARCEL_READY`, and `DAY44_RUN` markers passed. |
| Real dev `/health` matrix | PASS + inherent RAG SKIP | Gateway, Identity, Trip, Booking, Payment, Parcel, Tracking, Notification returned HTTP 200. RAG dev container could not start because live OpenRouter/Cloudinary credentials are absent; the testable RAG leg passed in the isolated provider-trap stack with real PostgreSQL/pgvector and zero provider requests. |
| Review artifact validation | PASS | Manifest/runbook/fixture parse and content checks passed; no credential-like value was found. |
| Review execution against Docker/local stack | PASS | Two real-store seed runs plus real Gateway Booking/Parcel happy path and idempotent replays executed; this is not artifact-only evidence. |
| Day 44 Review bullet overall | PASS | Idempotent rerun and immediately usable booking/parcel demo accounts proved. The unavailable live-provider credential leg is inherent and not part of ordinary `seed:demo`/`e2e:day44`. |
| Hard invariants | PASS | CPM clean; no banned dependency; MediatR `11.1.0`; no dependency-section change; no `Co-Authored-By`; `git diff --check` clean; all 35 changed files have expected EOL. |

### Verification environment note

The original Day 44 audit isolated the Day 44 merge from the then-uncommitted ETA/Tracking work. The exact Day 44 merge passed Trip `673/673 + 317/317`, so the later `AddPlannedEtaSource` failure was not scored as a Day 44 defect.

### Post-audit CI follow-up

The ETA branch subsequently fixed PostgreSQL `42804` by changing the `planned_eta_source` database default from an integer-backed CLR enum value to the explicit SQL cast `'ROUTE_BASELINE'::vietride_trip.planned_eta_source`. EF configuration, migration, designer, and model snapshot now agree.

- `dotnet build apps/trip/VietRide.Trip.sln --configuration Release` — PASS, 0 warnings and 0 errors.
- Targeted `RouteRepositoryTenantIsolationTests` migration regression — PASS, 2/2.
- `dotnet ef migrations has-pending-model-changes ...` — PASS, no pending model changes.
- Generated migration SQL inspection — PASS, enum type and casted default are emitted before the column is used.
- Isolated migration apply/down/re-apply lifecycle — PASS; the temporary database was removed afterward.
- Targeted `dotnet format ... --verify-no-changes` — PASS; changed C# files are CRLF.
- Exact CI command `dotnet test apps/trip/VietRide.Trip.sln --no-build --configuration Release ...` — PASS, unit 678/678 and integration 317/317.

## Contract / event / schema changes shipped

- REST endpoints, Gateway routes, public DTOs, error codes, routing keys, and integration events: **none**.
- EF/Prisma migrations and physical DDL: **none**.
- Demo-only assets: deterministic seed modules, isolated Docker E2E topology, attested RAG fixture, and runbook.
- Infrastructure: local PostgreSQL timezone is explicitly `Asia/Ho_Chi_Minh` for deterministic ICT seed formulas.
- RAG convention reconciliation: Cloudinary raw document storage, OpenRouter chat/embedding, model `nvidia/llama-nemotron-embed-vl-1b-v2:free`, `halfvec(2048)`, and HNSW cosine indexing. BSOT was bumped to `1.60.0` and the §13 changelog row is present.

## Known gaps & carry-over for Day 45

- Live dev RAG `/health` needs valid runtime OpenRouter and Cloudinary credentials. Ordinary Day 44 seed/E2E intentionally requires neither and passed with provider egress trapped at zero requests.
- Booking integration tests can exhaust the shared dev PostgreSQL connection budget while the full app stack is running. Use the documented per-service `VIETRIDE_BOOKING_TEST_CONNECTION_STRING` against an isolated test PostgreSQL for stable full-regression evidence.
- No Day 44 functional or artifact carry-over remains.

## Notes for Day 45 planning

- Reuse `npm run e2e:day44 -- --start-date=<future-ICT-date>` as the reproducible prerequisite for passenger/parcel rehearsal; provide `DEMO_SEED_ACCOUNT_PASSWORD` only at process runtime.
- Do not regenerate RAG embeddings during normal seed/E2E. A fixture refresh requires explicit approval, a real runtime OpenRouter key, and review of both fixture and provenance diffs.
- Preserve namespace `day44-v1`, fixed IDs, and fail-closed ownership checks so Day 45 scenarios can rely on stable seeded references.
