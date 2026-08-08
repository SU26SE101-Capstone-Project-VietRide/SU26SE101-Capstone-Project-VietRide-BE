# Day 44 — E2E seed data + demo scenarios plan

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 44 (Jira: SCV-133)
- **Prior checklist**: `docs/handoff/day-43-checklist.md` (found; `READY`, no carry-over)
- **Plan status**: `APPROVED`
  <!-- Allowed future values: REVISION-REQUIRED | REVIEWER-APPROVED — AWAITING HUMAN | APPROVED -->

## Objective

Deliver one deterministic, rerunnable demo seed for Identity, Trip, Booking, Payment, Parcel,
and RAG. The isolated Day 44 gate must finish each run in under two minutes, prove exact
cross-service fixture state, and leave login-ready accounts able to create a wallet Booking and
Parcel immediately. This plan incorporates the human-approved Q1–Q7 decisions; no production
business rule, endpoint, event, Hangfire horizon, or canonical system seed is changed.

## Success criteria (DoD — binary, verifiable)

- [ ] `npm run seed:demo -- --start-date=<YYYY-MM-DD>` requires a future ICT date and runtime
      `DEMO_SEED_ACCOUNT_PASSWORD`, rejects Production, and validates all inputs before writes.
- [ ] The manifest is `schemaVersion: 1`, namespace `day44-v1`, timezone
      `Asia/Ho_Chi_Minh`, contains only fixed UUIDs, and never generates a random fixture ID.
- [ ] The exact real-store state is: 1 System Admin; 3 Operators; 3 Operator Admins; 9 Drivers;
      3 Assistants; 10 Passengers; Starter plus Business Demo plans; 3 subscriptions; 2 paid
      subscription sagas; 5 Stations; 9 Routes; 3 AlternativeRoutes; 9 Vehicles; 9 schedules;
      126 Trips and canonical snapshots/children; 10 funded wallets/top-ups/ledger entries; the
      exact five-Voucher/two-consent matrix; 2 ParcelRouteFares; and 3 searchable RAG documents.
- [ ] Two immediate isolated runs each take less than 120 seconds; the second preserves the
      owned fixture IDs/full-state checksum and creates no duplicate credit or event evidence.
- [ ] The committed RAG fixture passes model/dimension/content/fixture provenance verification;
      ordinary `seed:demo` and `e2e:day44` pass with the real `OPENROUTER_API_KEY` absent and
      make zero OpenRouter `/embeddings` requests.
- [ ] Gateway/API smoke creates one wallet Booking and one wallet Parcel on seeded future Trips,
      with tenant isolation, idempotency, entitlement, and non-negative balances preserved.
- [ ] No write affects a row outside fixed `day44-v1` IDs; a foreign natural-key collision fails
      closed rather than adopting, overwriting, or deleting that row.

## Frozen Day 44 manifest contract

Task 44.1 records these values verbatim in the durable manifest; downstream tasks may not vary
them.

- Root: `schemaVersion=1`, `namespace=day44-v1`, `timezone=Asia/Ho_Chi_Minh`, required
  `startDate`; valid only when `startDate >= current ICT date + 1 day`. No silent date shift.
  UUID namespace is `44000000-0000-5000-8000-000000000001`; every root and child ID is listed
  in the manifest, derived offline with UUIDv5/SHA-1 from that namespace and a canonical fixture
  key. Runtime uses built-in Node crypto only to verify the listed ID, never to generate a new ID.
- Plan aliases are manifest-only: `STARTER_TRIAL -> Starter (Free Trial)` and
  `BUSINESS -> Business (Demo)`; they are not enums. Starter retains its existing fixed seed ID,
  prices `0/0`, limits vehicles/drivers/assistants/operator-users/routes/trips-per-month
  `3/5/5/3/5/100`, flags parcel/shuttle/RAG `false/false/true`, active. Business Demo uses fixed
  ID `44000000-0000-4000-8000-000000000001`, prices `2,000,000/20,000,000` VND, limits
  `20/40/40/20/30/2,000`, flags parcel/shuttle/RAG all true, active, and a description explicitly
  saying its commercial pricing is demo-only and non-canonical.
- Operators are named exactly `Day44 Business Operator A`, `Day44 Business Operator B`, and
  `Day44 Starter Operator C`. A/B use Business Demo; C uses Starter. Each has one login-ready Operator Admin,
  three distinct login-ready Drivers, and one login-ready Assistant. Account keys/emails are
  `operator.{a,b,c}@demo.vietride.local`, `driver.{a,b,c}{1,2,3}@demo.vietride.local`,
  `assistant.{a,b,c}@demo.vietride.local`, and `passenger{01..10}@demo.vietride.local`.
  System Admin comes only from `SYSTEM_ADMIN_BOOTSTRAP_*`; all other passwords come only from
  runtime `DEMO_SEED_ACCOUNT_PASSWORD`.
- Starter subscription is `ACTIVE`, starts at ICT midnight on `startDate`, expires after 30 days
  at ICT midnight on `startDate+30`, and has null billing period/payment method. Each Business
  subscription is `ACTIVE/MONTHLY/VNPAY`, starts at ICT midnight on `startDate`, and expires one
  calendar month later. A/B each have one deterministic SUCCEEDED subscription Payment, one
  SUCCEEDED upgrade attempt, processed payment-event/inbox evidence, one `ISSUED` Invoice with
  completed PDF metadata, and the corresponding platform subscription-credit ledger entry.
- Stations use exactly the approved six-decimal values: (1) Bến xe Miền Tây, Hồ Chí Minh,
  An Lạc, `10.741037,106.618980`; (2) Bến xe Miền Đông mới, Hồ Chí Minh, Long Bình,
  `10.879550,106.816190`; (3) Bến xe Trung tâm TP Cần Thơ, Cần Thơ, Cái Răng,
  `10.005200,105.772310`; (4) Bến xe khách Phường Long Châu, Vĩnh Long, Long Châu,
  `10.238230,105.957730`; (5) Bến xe Bến Tre, Vĩnh Long, Sơn Đông,
  `10.267025,106.359834`. No geocoding occurs.
- Each Operator has the same three templates, named `D44 {A|B|C} R1 Miền Tây - Cần Thơ`,
  `D44 {A|B|C} R2 Cần Thơ - Miền Tây`, and `D44 {A|B|C} R3 Miền Tây - Bến Tre`:
  R1 Miền Tây → Cần Thơ (primary; 08:00 ICT,
  240 minutes, 170 km, base fare 180,000 VND), R2 Cần Thơ → Miền Tây (return pair; 14:00 ICT,
  240 minutes, 170 km, base fare 180,000 VND), and R3 Miền Tây → Bến Tre (10:00 ICT,
  150 minutes, 90 km, base fare 120,000 VND). Stations 2 and 4 remain available but are not
  Day 44 endpoints. Per Operator, R3 uses three operator-owned waypoint copies at the exact
  coordinates of Stations 2/3/4, primary order `[2,3,4]`, and polyline
  `ozp\`As_wiSu\`Zqoe@twiDf{jEmol@{ec@_sDcpmA`; its single AlternativeRoute uses order `[4,2,3]`
  and polyline `ozp\`As_wiSpeaBxc\`Cgg|BktfDtwiDf{jEmcr@_wqB`. Thus there are exactly 9 Stops,
  9 RouteStops, 9 AlternativeRouteStops, and 3 AlternativeRoutes, all with the same Bến Tre
  destination as R3. No geocoding occurs.
- Each Operator has one active STANDARD_BUS, LIMOUSINE, and SLEEPER_BUS, mapped in order to
  R1/R2/R3. It has three daily schedules, one per route/vehicle/distinct Driver, with
  `dayOfWeek=[1,2,3,4,5,6,7]`, `validFrom=startDate`, `validUntil=startDate+29 days`. Only R1
  has that Operator's Assistant; R2/R3 have null assistant. Production remains at the canonical
  rolling 14-day horizon: exactly `9 * 14 = 126` Trips are materialized, never 30 days.
  Plates are `51B-440.01/.02/.03` for A, `51B-441.01/.02/.03` for B, and
  `51B-442.01/.02/.03` for C. `currentTripsThisMonth` equals the count of that Operator's 42 materialized departures whose
  ICT year/month equals the ICT year/month of `startDate`; it is calculated, not fixed at 42.
- Each Passenger has exactly one SUCCEEDED TopUpRequest at `startDate-1 day 09:00 ICT`, one
  immutable `CREDIT/TOP_UP` WalletTransaction for 2,000,000 VND with before/after `0/2,000,000`
  and `referenceId=TopUpRequest.id`, and wallet balance 2,000,000. No MANUAL_ADJUSTMENT.
- Voucher validity is `[startDate-7 days 00:00 ICT, startDate+60 days 23:59:59 ICT]`; all are
  active, `totalUsageLimit=10000`, `perUserLimit=100`, and `newUserOnly=false`:
  - `D44RIDE10`: platform/VIETRIDE_FUNDED, PERCENT_OFF 10, min 100,000, max 50,000,
    BOOKING+PARCEL, all Operators/Routes, WALLET+VNPAY, no consent.
  - `D44BOOK50`: platform/VIETRIDE_FUNDED, FIXED_AMOUNT 50,000, min 200,000, null max,
    BOOKING, Operator A R1, WALLET, no consent.
  - `D44PARTNER15`: admin platform row/OPERATOR_FUNDED, PERCENT_OFF 15, min 100,000,
    max 75,000, BOOKING+PARCEL, Operators A+B and their R1 routes, WALLET+VNPAY; exactly two
    ACCEPTED consents, responded by the corresponding Operator Admins.
  - `D44OPA30`: Operator A self-owned/OPERATOR_FUNDED, FIXED_AMOUNT 30,000, min 150,000,
    null max, BOOKING, Operator A R1, WALLET, no consent row.
  - `D44OPBPARCEL20`: Operator B self-owned/OPERATOR_FUNDED, PERCENT_OFF 20, min 100,000,
    max 100,000, PARCEL, Operator B R1, WALLET+VNPAY, no consent row.
  Operator-owned scope is server-forced/self and every targeted route belongs to its Operator.
- Parcel fares are exactly 2 active rows: SMALL = `50,000` VND on R1 for each Business Operator
  A/B, matching the contract's batch-fare example and the two parcel-enabled tenants needed for
  the demo. No fare is seeded for Starter Operator C.
- RAG documents are titled exactly `Day44 Public Passenger Guide`, `Day44 Operator A Policy`,
  and `Day44 System Admin Runbook`, with storage paths
  `day44-v1/rag/{public-passenger-guide,operator-a-policy,system-admin-runbook}.txt`. They have
  exactly one or more searchable chunks each. All documents are
  `APPROVED`, `COMPLETED`, `chunkCount>=1`, have approved-by/approved-at/ingested-at values, and
  use recorded precomputed `halfvec(2048)` embeddings from
  `nvidia/llama-nemotron-embed-vl-1b-v2:free`. PUBLIC is global/all roles; OPERATOR is scoped to
  Operator A and DRIVER/ASSISTANT/OPERATOR_STAFF/OPERATOR_ADMIN; ADMIN is global/SYSTEM_ADMIN.
  PUBLIC explicitly lists PASSENGER/DRIVER/ASSISTANT/OPERATOR_STAFF/OPERATOR_ADMIN/SYSTEM_ADMIN.
  Default seed never calls Cloudinary or OpenRouter. The committed fixture contains exactly one
  vector per canonical document and is generated only by the explicit Task 44.6 command using a
  runtime `OPENROUTER_API_KEY`. Its separate provenance records schema/generator version 1,
  provider/model/dimension, all three canonical content SHA256 values, and final fixture SHA256.
  Offline verification rejects any drift; live ingest remains outside the default two-minute path.

## Contract changes

No REST endpoint, Gateway route, error code, migration, production job horizon, or canonical
`db-schema/*/seed.sql` changes. Task 44.2 reconciles stale RAG higher-SOT/diagram text to the
already implemented Cloudinary + OpenRouter + `halfvec(2048)` contract and records the required
BSOT version/changelog bump. Task 44.6 adds an explicit one-time OpenRouter fixture generator and
committed checksum provenance; it is never part of the default seed/E2E. Day 44 otherwise adds
only demo seed/test assets, isolated Compose config, `seed:demo`/`e2e:day44` scripts, and a runbook.

## Tasks

### Task 44.1 — Record the frozen deterministic manifest

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `docs/handoff/day-44-demo-data-manifest.md` |
| auto-expand scope | None. Any extra fixture field or file requires a manager patch to this plan's owned-files and exact command ledger before editing. |
| forbidden scope | Production code; schema/migrations; `db-schema/**/seed.sql`; API/event/error/job registries; `.env`; secrets; unrelated services/docs; new dependencies; unresolved business/API/schema decisions; destructive operations; git branch/commit/push; `.vscode/settings.json`; `SP26SE002_GSP72_HCM_Report5_Unit_Test_REF.xlsx`. |
| depends on | None; executes first. |
| parallel-safe | no — manifest gate for all feature tasks. |
| verification tier | `DOCS` |
| verification commands | `npx prettier --check docs/handoff/day-44-demo-data-manifest.md docs/handoff/day-44-plan.md`<br>`node -e "const fs=require('node:fs');const s=fs.readFileSync('docs/handoff/day-44-demo-data-manifest.md','utf8');for(const v of ['schemaVersion: 1','namespace: day44-v1','timezone: Asia/Ho_Chi_Minh','44000000-0000-4000-8000-000000000001','Business (Demo)','126','D44RIDE10','D44BOOK50','D44PARTNER15','D44OPA30','D44OPBPARCEL20','halfvec(2048)','nvidia/llama-nemotron-embed-vl-1b-v2:free'])if(!s.includes(v))throw Error('missing '+v);for(const bad of ['TBD','TODO','CHANGEME','as approved','as chosen'])if(s.includes(bad))throw Error('placeholder '+bad);if(/password\s*[:=]\s*[^$\n]/i.test(s))throw Error('secret-like value');"`<br>`git diff --check -- docs/handoff/day-44-demo-data-manifest.md docs/handoff/day-44-plan.md` |
| full regression owner | `audit-day` |
| invariant flags | LF `.md`; CRLF `.cs` untouched; CPM no `Version=`; MediatR v11; BCrypt cost 12; BIGINT VND; no banned/new dependency; existing Outbox key shape; no cross-DB FK/transaction; no secret. |
| acceptance | The document records every value in **Frozen Day 44 manifest contract**, including an exact fixed UUID for every fixture/root/child, canonical fixture key, full expected row state, relative timestamp formula, ownership namespace, and fail-closed natural-key rule. It distinguishes demo-only Business pricing from production commercial policy and contains no secret or placeholder. |
| source citations | `BE_TIMELINE_VU.md` §Day 44; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.4, §4.5, §6.5, §6.6, §6.8, §6.10–6.11; `BACKEND_SOURCE_OF_TRUTH.md` §3.1 and §12.4; `db-schema/{identity-user,trip-route-vehicle,booking,payment-wallet,parcel,rag-ai}/README.md`; human-approved Day 44 Q1–Q7 (2026-08-08), frozen above. |

### Task 44.2 — Reconcile the RAG source-of-truth and generated diagram

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `SU26SE101_VIETRIDE_technical_context_v7.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `db-schema/_global/_drawio_generator.py`; `db-schema/rag-ai/schema.drawio`; `db-schema/rag-ai/README.md`; `db-schema/rag-ai/schema.sql` (header comment only); `db-schema/_global/README.md`; `db-schema/_global/SCHEMA_REVIEW_REPORT.md`; `db-schema/_global/ERD_DRAWING_MASTER.md`; `docs/developer-guides/nest/rag-service-timeline.md`; `docs/api/rag-service-integration.md`; `docs/runbooks/rag-retrieval-explain.md`. |
| auto-expand scope | Human-approved on 2026-08-08 after reviewer finding: reconcile the stale chat-model references in `db-schema/rag-ai/README.md` and the header comment of `db-schema/rag-ai/schema.sql`; DDL statements remain read-only. Historical changelog rows may remain only when explicitly labeled historical. |
| forbidden scope | `apps/rag/**`; Prisma schema/migrations; DDL statements in `db-schema/rag-ai/schema.sql`; non-RAG diagrams; application/runtime rewrite; new migration/dependency; non-RAG SOT prose; `.env`/secrets; unrelated services; destructive or git operations; the two dirty/untracked user files. |
| depends on | 44.1. Must land before Task 44.6. |
| parallel-safe | yes — disjoint documentation/generator files; may run alongside 44.3 after 44.1. |
| verification tier | `DOCS` |
| verification commands | `python -m py_compile db-schema/_global/_drawio_generator.py`<br>`python db-schema/_global/_drawio_generator.py rag-ai`<br>`git diff --exit-code -- db-schema/identity-user/schema.drawio db-schema/trip-route-vehicle/schema.drawio db-schema/booking/schema.drawio db-schema/payment-wallet/schema.drawio db-schema/parcel/schema.drawio db-schema/tracking/schema.drawio db-schema/notification/schema.drawio`<br>`node -e "const fs=require('node:fs');const technical='SU26SE101_VIETRIDE_technical_context_v7.md';const files=[technical,'BACKEND_SOURCE_OF_TRUTH.md','db-schema/_global/_drawio_generator.py','db-schema/rag-ai/schema.drawio','db-schema/_global/README.md','db-schema/_global/SCHEMA_REVIEW_REPORT.md','db-schema/_global/ERD_DRAWING_MASTER.md','docs/developer-guides/nest/rag-service-timeline.md','docs/api/rag-service-integration.md','docs/runbooks/rag-retrieval-explain.md'];const ragIndexFiles=new Set(['db-schema/_global/SCHEMA_REVIEW_REPORT.md','db-schema/_global/ERD_DRAWING_MASTER.md']);const stale=/(text-embedding-3-small|vector\s*\(\s*1536\s*\)|OpenAI Embedding API|rag:document-ingest[^\n]*OpenAI|OPENAI_EMBEDDING_MODEL|KnowledgeChunk[^\n]*ivfflat|knowledge_chunks\.embedding[^\n]*ivfflat|Object storage[^\n]*RAG|Firebase Storage[^\n]*(?:tài liệu RAG|RAG docs)|fileUrl[^\n]*Firebase Storage|(?:Upload|Download) file[^\n]*Firebase Storage|rag\/documents[^\n]*Firebase Storage)/i;for(const f of files){const lines=fs.readFileSync(f,'utf8').split(/\r?\n/);lines.forEach((x,i)=>{const staleSummary=f===technical&&(/^\|\s*\*\*File storage\*\*\s*\|\s*(?=[^|]*Firebase Storage)(?![^|]*Cloudinary)/i.test(x)||/^\|\s*\*\*LLM\*\*\s*\|\s*(?![^|]*OpenRouter)/i.test(x));const staleRag=stale.test(x)||(ragIndexFiles.has(f)&&/ivfflat/i.test(x))||staleSummary;if(staleRag&&!/(historical|legacy|superseded)/i.test(x))throw Error(f+':'+(i+1)+': stale RAG contract: '+x)})}const b=fs.readFileSync('BACKEND_SOURCE_OF_TRUTH.md','utf8');for(const v of ['Cloudinary','OpenRouter','halfvec(2048)','nvidia/llama-nemotron-embed-vl-1b-v2:free'])if(!b.includes(v))throw Error('BSOT missing current RAG value '+v);if(!/^## 13\.|^# 13\.|§13/m.test(b))throw Error('BSOT §13/changelog missing');"`<br>`node -e "const fs=require('node:fs');const lines=fs.readFileSync('SU26SE101_VIETRIDE_technical_context_v7.md','utf8').split(/\r?\n/);const storage=lines.find(x=>x.includes('| **File storage** |'));const llm=lines.find(x=>x.includes('| **LLM** |'));if(!storage||!storage.includes('Firebase Storage')||!storage.includes('Cloudinary')||!/RAG/i.test(storage))throw Error('quick-reference File storage row must preserve non-RAG Firebase and declare RAG Cloudinary');for(const v of ['OpenRouter','nvidia/nemotron-3-ultra-550b-a55b:free','nvidia/llama-nemotron-embed-vl-1b-v2:free'])if(!llm?.includes(v))throw Error('quick-reference LLM row missing '+v);const nonRagFirebase=lines.some(x=>/Firebase Storage/i.test(x)&&/(avatar|vehicle|parcel|incident|invoice|notification)/i.test(x)&&!/(tài liệu RAG|RAG docs|rag\/documents)/i.test(x));if(!nonRagFirebase)throw Error('non-RAG Firebase contract was removed');console.log('RAG_SUMMARY_SCOPE=PASS');"`<br>`npx prettier --check SU26SE101_VIETRIDE_technical_context_v7.md BACKEND_SOURCE_OF_TRUTH.md db-schema/_global/README.md db-schema/_global/SCHEMA_REVIEW_REPORT.md db-schema/_global/ERD_DRAWING_MASTER.md docs/developer-guides/nest/rag-service-timeline.md docs/api/rag-service-integration.md docs/runbooks/rag-retrieval-explain.md`<br>`git diff --check -- SU26SE101_VIETRIDE_technical_context_v7.md BACKEND_SOURCE_OF_TRUTH.md db-schema/_global/_drawio_generator.py db-schema/rag-ai/schema.drawio db-schema/_global/README.md db-schema/_global/SCHEMA_REVIEW_REPORT.md db-schema/_global/ERD_DRAWING_MASTER.md docs/developer-guides/nest/rag-service-timeline.md docs/api/rag-service-integration.md docs/runbooks/rag-retrieval-explain.md` |
| approved scope-expansion verification | `npx prettier --check db-schema/rag-ai/README.md`<br>`node -e "const fs=require('node:fs');for(const f of ['db-schema/rag-ai/README.md','db-schema/rag-ai/schema.sql']){const s=fs.readFileSync(f,'utf8');if(s.includes('nex-agi/nex-n2-pro:free'))throw Error(f+': stale chat model');if(!s.includes('nvidia/nemotron-3-ultra-550b-a55b:free'))throw Error(f+': missing canonical chat model')}"`<br>`git diff --check -- db-schema/rag-ai/README.md db-schema/rag-ai/schema.sql` |
| full regression owner | `audit-day` |
| invariant flags | LF docs/Python/drawio; no physical DDL/Prisma/migration rewrite; BSOT §13 version/changelog bump required; Cloudinary/OpenRouter/halfvec HNSW contract only; no provider secret; no new dependency; no cross-DB FK. |
| acceptance | Every current-contract RAG reference in the owned higher-SOT, generator, diagram, and directly applicable RAG docs states Cloudinary storage, OpenRouter embeddings, exact model `nvidia/llama-nemotron-embed-vl-1b-v2:free`, `halfvec(2048)`, and the current HNSW index. Technical-context RAG-only legacy claims are corrected; BSOT §4.2/config/job/provider/test references and §13 version/changelog are coherent. In technical-context quick-reference, `File storage` explicitly distinguishes Firebase for valid non-RAG client media from Cloudinary for RAG documents, while `LLM` names OpenRouter and both current chat and embedding model IDs. The stale scan targets only exact RAG embedding/storage/index phrases plus those two scoped summary rows; valid Firebase avatar, vehicle, Parcel-photo, Invoice, and notification prose remains untouched. The generator accepts the explicit `rag-ai` target and reproduces only the RAG diagram; all non-RAG diagrams remain byte-unchanged. Historical changelog text is explicitly marked and not presented as current. |
| source citations | Human-approved Q7; `SU26SE101_VIETRIDE_technical_context_v7.md` §6.8 and RAG references; `BACKEND_SOURCE_OF_TRUTH.md` §4.2, §8.1/§10 RAG job/provider/config/test references, §13; `db-schema/rag-ai/schema.sql` `knowledge_chunks.embedding halfvec(2048)` and HNSW index; `db-schema/rag-ai/README.md` §Data Taxonomy/embedding decision; `apps/rag/src/config/env.schema.ts` OpenRouter model/config; `apps/rag/src/providers/openrouter-embedding.provider.ts` request/response contract; `apps/rag/src/ingest/ingest.constants.ts` expected 2,048 dimensions; `apps/rag/prisma/schema.prisma` `Unsupported("halfvec(2048)")`. |

### Task 44.3 — Build the deterministic Identity fixture module

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/day44/seed-identity.ts`; `scripts/day44/seed-identity.test.ts` |
| auto-expand scope | None; supporting files require a plan command-ledger patch before edit. |
| forbidden scope | Identity production `.cs`, migrations/snapshot, canonical seed SQL, other feature modules, root config, `.env`/secrets, new dependencies, foreign rows, cross-DB operations, unrelated services, destructive/git operations, user dirty files. |
| depends on | 44.1. |
| parallel-safe | yes — disjoint from Task 44.2; later feature modules depend on its IDs. |
| verification tier | `FOCUSED` |
| verification commands | `$previousTsNodeCompilerOptions=$env:TS_NODE_COMPILER_OPTIONS; $env:TS_NODE_COMPILER_OPTIONS='{"module":"commonjs","moduleResolution":"node","ignoreDeprecations":"6.0"}'; try { node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 identity fixture planner" scripts/day44/seed-identity.test.ts; $testExit=$LASTEXITCODE } finally { $env:TS_NODE_COMPILER_OPTIONS=$previousTsNodeCompilerOptions }; exit $testExit` (human-approved command-ledger correction on 2026-08-08; at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --ignoreDeprecations 6.0 scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts`<br>`npx eslint scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts`<br>`npx prettier --check scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts`<br>`git diff --check -- scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts` |
| full regression owner | `audit-day` |
| invariant flags | LF `.ts`; CRLF `.cs` untouched; CPM no `Version=`; MediatR v11; BCrypt cost 12 through existing Identity lifecycle; no banned/new dependency; no plaintext password/token/OTP/hash; exact role/operator scoping; no cross-DB FK/query/transaction. |
| acceptance | Focused tests prove the module plans the exact manifest state: existing bootstrap System Admin; Operators A/B/C; 3 Operator Admins; 9 Drivers; 3 Assistants; 10 Passengers; unchanged Starter plus fixed Business Demo; A/B Business and C Starter subscription shapes; calculated per-plan counters including ICT-month Trip counter input. It rejects Production, missing password, non-future start date, random/unlisted IDs, foreign natural-key collisions, and any full-state mismatch. Tests prove it never logs credential material. Real migrated-store counts/state are owned by Task 44.8, not claimed here. |
| source citations | Frozen manifest above; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.4, §4.5 (`maxOperatorUsers` counts staff/admin only), §5; `VietRide_API_Contract_v1.md` Auth/Admin Operators/Operator Users; `db-schema/identity-user/schema.sql` `users`, `operators`, `subscription_plans`, `operator_subscriptions`, `subscription_upgrade_attempts`, `integration_inbox`; `db-schema/identity-user/seed.sql` Starter fixed row. |

### Task 44.4 — Build the deterministic Trip fixture module

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/day44/seed-trip.ts`; `scripts/day44/seed-trip.test.ts` |
| auto-expand scope | None; supporting files require a plan command-ledger patch before edit. |
| forbidden scope | Trip production `.cs`; Hangfire jobs/horizon; migrations/snapshot; canonical seed SQL; other modules/root config; `.env`/secrets; new dependencies; foreign rows; cross-DB operations; destructive/git operations; user dirty files. |
| depends on | 44.1, 44.3. |
| parallel-safe | yes — after 44.3 its Trip write set is disjoint from Tasks 44.6–44.7. |
| verification tier | `FOCUSED` |
| verification commands | `$previousTsNodeCompilerOptions=$env:TS_NODE_COMPILER_OPTIONS; $env:TS_NODE_COMPILER_OPTIONS='{"module":"commonjs","moduleResolution":"node","ignoreDeprecations":"6.0"}'; try { node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 trip fixture planner" scripts/day44/seed-trip.test.ts; $testExit=$LASTEXITCODE } finally { $env:TS_NODE_COMPILER_OPTIONS=$previousTsNodeCompilerOptions }; exit $testExit` (human-approved command-ledger correction on 2026-08-08; at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --ignoreDeprecations 6.0 scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts`<br>`npx eslint scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts`<br>`npx prettier --check scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts`<br>`git diff --check -- scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts` |
| full regression owner | `audit-day` |
| invariant flags | LF `.ts`; production 14-day job unchanged; fixed six-decimal coordinates/no geocode; platform Station/global VehicleType vs tenant-owned rows; same-tenant crew/route/vehicle; immutable snapshots; no banned/new dependency/event/cross-DB FK. |
| acceptance | Focused tests expand the frozen manifest to exactly 5 Stations, 15 OperatorStation links, 9 Stops, 9 RouteStops, 9 AlternativeRouteStops, 9 Vehicles, 9 Routes with 3 return-pair links, 3 AlternativeRoutes, 9 schedules, and 126 future Trips. They prove all-day schedules, 30-day validity but 14-day-only materialization, R1-only Assistant assignment, distinct Driver per schedule, canonical seat-layout/fare/stop snapshots, 3,948 available seats, 126 TripStops, and calculated ICT `currentTripsThisMonth` across a month-boundary case. They prove exact IDs/full-state comparison and fail-closed collision behavior. Real migrated-store assertions are Task 44.8. |
| source citations | Frozen manifest above; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.3, §6.10, §6.11 Auto-generate Trip; `VietRide_API_Contract_v1.md` Route/AlternativeRoute/Vehicle/DriverSchedule and Trip search; `db-schema/trip-route-vehicle/schema.sql` Station through Trip child tables; `db-schema/trip-route-vehicle/seed.sql` three VehicleTypes; `BACKEND_SOURCE_OF_TRUTH.md` §10.1. |

### Task 44.5 — Build the deterministic commerce fixture module

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/day44/seed-commerce.ts`; `scripts/day44/seed-commerce.test.ts` |
| auto-expand scope | None; supporting files require a plan command-ledger patch before edit. |
| forbidden scope | Booking/Payment/Parcel production `.cs`; migrations/snapshots; canonical seed SQL; other modules/root config; `.env`/secrets; new dependencies; foreign financial rows; cross-DB transactions; new event key/admin subscription shortcut; destructive/git operations; user dirty files. |
| depends on | 44.1, 44.3, 44.4. |
| parallel-safe | yes — after 44.4 its commerce write set is disjoint from Tasks 44.6–44.7. |
| verification tier | `FOCUSED` |
| verification commands | `node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 commerce fixture planner" scripts/day44/seed-commerce.test.ts` (at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --ignoreDeprecations 6.0 scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts`<br>`npx eslint scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts`<br>`npx prettier --check scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts`<br>`git diff --check -- scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts` |
| full regression owner | `audit-day` |
| invariant flags | LF `.ts`; BIGINT VND; immutable before/after ledger; fixed discounts have null max; Money/AwayFromZero; existing `payment.subscription.payment_succeeded` key and Outbox/inbox idempotency; no banned/new dependency/cross-DB FK; operator-owned Voucher scope server-forced/self. |
| acceptance | Focused tests plan exactly 10 wallets at 2,000,000, 10 SUCCEEDED top-ups and 10 matching CREDIT/TOP_UP transactions; no MANUAL_ADJUSTMENT. They plan the exact five Voucher/two ACCEPTED-consent matrix and 2 SMALL ParcelRouteFares. They also plan exactly two canonical paid Business sagas: SUCCEEDED Payments, corresponding published Outbox evidence and processed-consumer evidence, SUCCEEDED Identity upgrade attempts/inbox evidence, ACTIVE subscriptions, two ISSUED/COMPLETED-PDF Invoices, and reconciled platform subscription credits. Tests prove a rerun emits no second money/event mutation and rejects any state/cross-service logical-ID mismatch. No admin-assign shortcut exists. Real migrated-store assertions are Task 44.8. |
| source citations | Frozen manifest above; `SU26SE101_VIETRIDE_technical_context_v7.md` §4.4 Voucher funding, §4.5 paid upgrade, §6.5 wallet, §6.6 parcel fares; `VietRide_API_Contract_v1.md` Admin/Operator Vouchers, Wallet top-up, PUT `/v1/operator/parcel-route-fares/{routeId}/batch` (SMALL 50,000 example); `db-schema/payment-wallet/schema.sql` `payments`, `invoices`, `wallets`, `wallet_transactions`, `top_up_requests`, `platform_wallet_transactions`, `processed_integration_events`, `outbox_events`; `db-schema/booking/schema.sql` Voucher/consent; `db-schema/parcel/schema.sql` `parcel_route_fares`; `BACKEND_SOURCE_OF_TRUTH.md` §5.8 event registry `payment.subscription.payment_succeeded`. |

### Task 44.6 — Generate and attest the RAG embedding fixture once

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/day44/generate-rag-fixture.ts`; `scripts/day44/generate-rag-fixture.test.ts`; `scripts/day44/fixtures/rag-embeddings.json`; `scripts/day44/fixtures/rag-embeddings.provenance.json`; `docs/rag/vietride-public-demo-knowledge-base.txt`; `docs/rag/vietride-operator-demo-knowledge-base.txt`; `docs/rag/vietride-admin-demo-knowledge-base.txt` |
| auto-expand scope | None. A generator helper, extra fixture/provenance file, or extra document requires a plan patch adding its exact path to test/typecheck/lint/prettier/diff ledgers before edit. |
| forbidden scope | Default seed/E2E modules; `apps/rag/**`; Prisma/migrations/DDL; canonical seed SQL; `.env` or committed/logged API keys/Authorization headers; alternate model/provider; runtime dependency addition; silent refresh; unrelated files; destructive/git operations; user dirty files. |
| depends on | 44.1, 44.2. |
| parallel-safe | yes — after 44.2 its generator/fixture assets are disjoint from Identity/Trip/commerce modules. |
| verification tier | `FOCUSED` |
| one-time bootstrap command | `if ([string]::IsNullOrWhiteSpace($env:OPENROUTER_API_KEY)){throw 'RAG fixture bootstrap requires runtime OPENROUTER_API_KEY; no files were written'}; $fixturePath='scripts/day44/fixtures/rag-embeddings.json'; $provenancePath='scripts/day44/fixtures/rag-embeddings.provenance.json'; if((Test-Path -LiteralPath $fixturePath) -or (Test-Path -LiteralPath $provenancePath)){throw 'RAG fixture bootstrap refused because committed fixture/provenance already exists; refresh requires explicit human approval'}; $output=(& node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --generate --base-url=https://openrouter.ai/api/v1 --model=nvidia/llama-nemotron-embed-vl-1b-v2:free --fixture=$fixturePath --provenance=$provenancePath --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt 2>&1 | Out-String); $generatorExitCode=$LASTEXITCODE; $leakDetected=$output.Contains($env:OPENROUTER_API_KEY) -or $output -match '(?i)(Bearer\s+\S+|Authorization\s*[:=]|OPENROUTER_API_KEY\s*[:=]|api[_-]?key\s*[:=]|headers?\s*[:=]|HTTP-Referer\s*[:=]|X-Title\s*[:=])'; if($leakDetected){$output=$null; throw 'RAG fixture bootstrap failed: sensitive output detected and suppressed'}; $output=$null; if($generatorExitCode -ne 0){throw 'RAG fixture bootstrap failed: generator output suppressed'}; Write-Output 'RAG_FIXTURE_BOOTSTRAP=PASS'` — run exactly once only when both committed files are absent. Any later refresh requires explicit human approval and a separately reviewed plan/command; this bootstrap refuses to overwrite. |
| verification commands | `node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 RAG fixture generator" scripts/day44/generate-rag-fixture.test.ts` (mocked embedding provider; at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --resolveJsonModule --ignoreDeprecations 6.0 scripts/day44/generate-rag-fixture.ts scripts/day44/generate-rag-fixture.test.ts`<br>`npx eslint scripts/day44/generate-rag-fixture.ts scripts/day44/generate-rag-fixture.test.ts`<br>`npx prettier --check scripts/day44/generate-rag-fixture.ts scripts/day44/generate-rag-fixture.test.ts scripts/day44/fixtures/rag-embeddings.json scripts/day44/fixtures/rag-embeddings.provenance.json`<br>`node -e "const fs=require('node:fs');const fixture=JSON.parse(fs.readFileSync('scripts/day44/fixtures/rag-embeddings.json','utf8'));const provenance=JSON.parse(fs.readFileSync('scripts/day44/fixtures/rag-embeddings.provenance.json','utf8'));const sha=/^[0-9a-f]{64}$/;if(fixture.schemaVersion!==1||fixture.generatorVersion!==1||fixture.model!=='nvidia/llama-nemotron-embed-vl-1b-v2:free'||fixture.dimension!==2048||fixture.documents?.length!==3)throw Error('fixture metadata mismatch');if(provenance.schemaVersion!==1||provenance.generatorVersion!==1||provenance.provider!=='openrouter'||provenance.endpoint!=='https://openrouter.ai/api/v1/embeddings'||provenance.model!==fixture.model||provenance.dimension!==2048||!sha.test(provenance.fixtureSha256)||provenance.documents?.length!==3||provenance.documents.some(d=>!sha.test(d.contentSha256)))throw Error('provenance metadata/hash shape mismatch');if(fixture.documents.some(d=>d.chunks?.length!==1||d.chunks[0].embedding?.length!==2048||d.chunks[0].embedding.some(v=>!Number.isFinite(v))))throw Error('fixture vector shape mismatch');"`<br>`node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --verify --fixture=scripts/day44/fixtures/rag-embeddings.json --provenance=scripts/day44/fixtures/rag-embeddings.provenance.json --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt` (offline only; recomputes all document/fixture SHA256 values and must emit `RAG_FIXTURE_PROVENANCE=PASS`)<br>`git diff --check -- scripts/day44/generate-rag-fixture.ts scripts/day44/generate-rag-fixture.test.ts scripts/day44/fixtures/rag-embeddings.json scripts/day44/fixtures/rag-embeddings.provenance.json docs/rag/vietride-public-demo-knowledge-base.txt docs/rag/vietride-operator-demo-knowledge-base.txt docs/rag/vietride-admin-demo-knowledge-base.txt` |
| full regression owner | `audit-day` |
| human-approved bootstrap retry | Approved on 2026-08-08 after the first attempt stopped at the missing-key guard before provider call/file write. Retry exactly once with runtime-only `OPENROUTER_API_KEY`, the existing capture/redaction/refusal logic, and temporary `TS_NODE_COMPILER_OPTIONS={"module":"commonjs","moduleResolution":"node10","target":"ES2022","ignoreDeprecations":"6.0"}` restored after execution. This is initial artifact generation, not refresh authorization. |
| human-approved command corrections | The focused mocked-test and offline `--verify` commands must run with the same temporary `TS_NODE_COMPILER_OPTIONS` and restore its prior presence/value afterward; root config remains untouched. The final whitespace check must use `git diff --no-index --check` from a temporary empty file against each of the seven owned paths so untracked files are checked, accepting exit code 1 only when output is empty and rejecting exit code greater than 1. These corrections supersede only the affected original command invocations; all other exact Task 44.6 checks remain unchanged. |
| invariant flags | LF `.ts/.json/.txt`; Node built-ins only/no new dependency; runtime-only `OPENROUTER_API_KEY`; exact OpenRouter model; no key/request headers in files or output; exactly 2,048 finite numeric values per vector; explicit generation only; default seed/E2E offline; no cross-DB FK. |
| acceptance | The generator has explicit `--generate` and offline `--verify` modes. The task-local bootstrap is the only live-provider step: it fails on a missing key before any file write, refuses an existing fixture/provenance pair, captures and suppresses all child output, checks the exact key string plus Bearer/credential/header patterns before checking exit status, and emits only constant redacted success/failure messages. Generation sends only the three canonical UTF-8/LF document contents to `https://openrouter.ai/api/v1/embeddings` with the exact model and writes atomically only after all three 2,048-dimensional finite vectors validate. Mock-provider tests prove missing-key/no-write, malformed dimension/non-finite/no-write, deterministic serialization, and output/header redaction. The fixture has `schemaVersion=1`, `generatorVersion=1`, model, dimension 2048, and exactly three one-chunk vectors. Provenance has `schemaVersion=1`, provider/endpoint/model/dimension/generator version, each content SHA256, and final fixture SHA256. Repeatable worker/reviewer/patch verification consists only of mocked tests and offline checks: it never calls OpenRouter, never needs the key, and never generates or refreshes the fixture. Offline verification fails on any content/model/dimension/version/fixture/provenance drift; later refresh is impossible without explicit human approval and a revised command. |
| source citations | Human-approved Q7; `apps/rag/src/config/env.schema.ts` exact OpenRouter base/model/key names; `apps/rag/src/providers/openrouter-embedding.provider.ts` `/embeddings` request and finite-response validation; `apps/rag/src/ingest/ingest.constants.ts` `RAG_INGEST_EXPECTED_EMBEDDING_DIMENSIONS=2_048`; `db-schema/rag-ai/schema.sql` `halfvec(2048)`; frozen manifest RAG documents. |

### Task 44.7 — Build the offline RAG seed module

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/day44/seed-rag.ts`; `scripts/day44/seed-rag.test.ts` |
| auto-expand scope | None; any seed helper/test requires a plan command-ledger patch before edit. |
| forbidden scope | Generator/fixture/provenance assets owned by 44.6; `apps/rag/**`; Prisma/migrations/DDL; provider/network calls; canonical seed SQL; root config; `.env`/secrets; new dependencies; foreign rows; destructive/git operations; user dirty files. |
| depends on | 44.1, 44.3, 44.6. |
| parallel-safe | yes — after prerequisites its two offline seed files are disjoint from Trip/commerce. |
| verification tier | `FOCUSED` |
| verification commands | `node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 offline RAG seed planner" scripts/day44/seed-rag.test.ts` (at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --resolveJsonModule --ignoreDeprecations 6.0 scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts`<br>`npx eslint scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts`<br>`npx prettier --check scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts`<br>`node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --verify --fixture=scripts/day44/fixtures/rag-embeddings.json --provenance=scripts/day44/fixtures/rag-embeddings.provenance.json --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt` (must emit `RAG_FIXTURE_PROVENANCE=PASS` without `OPENROUTER_API_KEY`)<br>`git diff --check -- scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts` |
| full regression owner | `audit-day` |
| invariant flags | LF `.ts`; committed attested fixture is read-only; exact model/2048 check before DB writes; no provider/network branch or usable key; PUBLIC/OPERATOR/ADMIN access never widens; no banned/new dependency/cross-DB FK. |
| acceptance | Focused tests prove the module first performs the same offline provenance/hash/model/dimension checks, then plans exactly three APPROVED/COMPLETED documents and exactly three chunks, one per document, with approval/ingest timestamps and `chunkCount=1`. Each chunk is non-empty, uniquely indexed, searchable, and uses its attested 2,048-value vector. Tests prove PUBLIC all six roles, OPERATOR exact Operator A/operator roles, ADMIN global/SYSTEM_ADMIN only, and deny cross-tenant/role combinations. The module has no provider/network code path and fails before DB writes on any fixture/provenance drift. Real pgvector assertions and `RAG_READY=PASS` belong to Task 44.8. |
| source citations | Frozen manifest; Task 44.6 provenance contract; reconciled `SU26SE101_VIETRIDE_technical_context_v7.md` §6.8; reconciled `BACKEND_SOURCE_OF_TRUTH.md` §4.2/RAG access; `db-schema/rag-ai/schema.sql` document/chunk enums/tables and HNSW `halfvec(2048)`; `apps/rag/prisma/schema.prisma`. |

### Task 44.8 — Orchestrate and prove the isolated real-store seed

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `scripts/seed-dev-data.ts`; `scripts/seed-dev-data.test.ts`; `scripts/run-day44-seed-e2e.mjs`; `scripts/run-day44-seed-e2e.test.mjs`; `infra/docker/docker-compose.day44-e2e.yml`; `package.json`; `.env.example` |
| auto-expand scope | None. Any integration adapter/config/test expansion requires a plan patch that lists the exact file and adds it to typecheck/lint/prettier/diff commands before edit. |
| forbidden scope | Production app code/routes; migrations/schema/canonical seeds; generator refresh or fixture mutation; lockfile/dependency change; `.env`/real OpenRouter secret; provider network call; shared/persistent Compose project; unrelated config/services; non-Day44 rows; destructive cleanup beyond the validated unique Compose project; git operations; user dirty files. |
| depends on | 44.3, 44.4, 44.5, 44.7. |
| parallel-safe | no — cross-service integration and root config owner. |
| verification tier | `PROJECT` — root npm registration and isolated Compose topology require the affected Day44 integration project; this is not a full solution/workspace regression. |
| verification commands | `node --test --require ts-node/register/transpile-only --test-name-pattern="Day 44 seed orchestrator" scripts/seed-dev-data.test.ts scripts/run-day44-seed-e2e.test.mjs` (at least 1 test must execute and pass)<br>`npx tsc --noEmit --target ES2022 --module commonjs --moduleResolution node --esModuleInterop --skipLibCheck --resolveJsonModule --ignoreDeprecations 6.0 scripts/seed-dev-data.ts scripts/seed-dev-data.test.ts scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts`<br>`npx eslint scripts/seed-dev-data.ts scripts/seed-dev-data.test.ts scripts/run-day44-seed-e2e.mjs scripts/run-day44-seed-e2e.test.mjs scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts`<br>`npx prettier --check scripts/seed-dev-data.ts scripts/seed-dev-data.test.ts scripts/run-day44-seed-e2e.mjs scripts/run-day44-seed-e2e.test.mjs scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts infra/docker/docker-compose.day44-e2e.yml package.json`<br>`node -e "const fs=require('node:fs');const lines=fs.readFileSync('.env.example','utf8').split(/\r?\n/).filter(x=>x.trim()&&!x.trim().startsWith('#'));for(const x of lines)if(!/^[A-Z][A-Z0-9_]*=/.test(x))throw Error('invalid .env.example line: '+x);for(const k of ['DEMO_SEED_ACCOUNT_PASSWORD','OPENROUTER_API_KEY'])if(!lines.includes(k+'='))throw Error('missing or nonblank runtime placeholder '+k);"`<br>`node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --verify --fixture=scripts/day44/fixtures/rag-embeddings.json --provenance=scripts/day44/fixtures/rag-embeddings.provenance.json --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt` (offline; must emit `RAG_FIXTURE_PROVENANCE=PASS`)<br>`docker compose --env-file .env.example -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.day44-e2e.yml config --quiet`<br>`if ([string]::IsNullOrWhiteSpace($env:DEMO_SEED_ACCOUNT_PASSWORD)){throw 'Set DEMO_SEED_ACCOUNT_PASSWORD in the isolated runtime'}; node -e "const cp=require('node:child_process');const env={...process.env};delete env.OPENROUTER_API_KEY;const d=new Date(Date.now()+86400000).toLocaleDateString('en-CA',{timeZone:'Asia/Ho_Chi_Minh'});const npm=process.platform==='win32'?'npm.cmd':'npm';const r=cp.spawnSync(npm,['run','e2e:day44','--','--start-date='+d],{stdio:'inherit',env});if(r.error)throw r.error;process.exit(r.status??1);"` (explicit future date; no real provider key; non-zero assertions; must emit all PASS/timing/count markers below)<br>`git diff --check -- scripts/seed-dev-data.ts scripts/seed-dev-data.test.ts scripts/run-day44-seed-e2e.mjs scripts/run-day44-seed-e2e.test.mjs scripts/day44/seed-identity.ts scripts/day44/seed-identity.test.ts scripts/day44/seed-trip.ts scripts/day44/seed-trip.test.ts scripts/day44/seed-commerce.ts scripts/day44/seed-commerce.test.ts scripts/day44/seed-rag.ts scripts/day44/seed-rag.test.ts infra/docker/docker-compose.day44-e2e.yml package.json .env.example` |
| full regression owner | `audit-day` |
| invariant flags | LF JS/TS/JSON/YAML/env example; CRLF `.cs` untouched; Production rejection; secrets redacted; default seed/E2E has no usable OpenRouter key and no provider egress; BCrypt 12; BIGINT VND/immutable ledger; existing event/Outbox; no cross-DB FK/distributed transaction; tenant isolation and mutation Idempotency-Key. |
| acceptance | The command validates Production/password/date/manifest/full-state and offline RAG provenance before writes, then applies fixed owned IDs in dependency order. The isolated Compose override supplies only a non-secret disabled-provider sentinel required for RAG config, and the harness fails if any `/embeddings` request occurs. E2E asserts: 1 System Admin, 3 Operators, 3 Operator Admins, 0 Operator Staff, 9 Drivers, 3 Assistants, 10 Passengers, 2 plans, 3 subscriptions, 2 upgrade attempts/inbox events; 2 SUCCEEDED subscription Payments, 2 ISSUED/COMPLETED-PDF Invoices, 2 processed events, 2 published Outbox events, and 2 platform credits totaling 4,000,000; 5 Stations, 15 OperatorStation links, 9 Stops/RouteStops/AlternativeRouteStops, 9 Routes, 3 return pairs, 3 AlternativeRoutes, 9 Vehicles, 9 schedules, 126 Trips, 126 TripStops, 3,948 TripSeats, and calculated ICT counters; 10 exact wallets/top-ups/transactions; exact five Vouchers/two consents; 2 SMALL Parcel fares and entitlements; exactly 3 RAG documents/3 searchable chunks with access denials, attested 2,048 dimensions/model, and `RAG_READY=PASS`. Two runs each `<120000 ms` preserve checksum and emit `IDEMPOTENT_RERUN=PASS`; Gateway Booking/Parcel smoke emits `BOOKING_READY=PASS`, `PARCEL_READY=PASS`, and `DAY44_RUN=PASS`; no secret/header is logged. |
| source citations | `BE_TIMELINE_VU.md` §Day 44 DoD/Review; frozen manifest; schema citations in Tasks 44.3–44.7; `BACKEND_SOURCE_OF_TRUTH.md` §3.1, §5.6, §11, §12.1/§12.4; `VietRide_API_Contract_v1.md` Login, Trip search, Booking create, Parcel available/create, Wallet; `infra/docker/docker-compose.yml`; existing Day37 isolated E2E pattern. |

### Task 44.9 — Document the verified demo handoff

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `docs/handoff/day-44-demo-seed-runbook.md` |
| auto-expand scope | None; extra output/examples require a plan command-ledger patch before edit. |
| forbidden scope | Code/config/schema; `.env`/credentials/tokens/hashes/provider keys; unrelated docs; new dependencies; destructive/git operations; user dirty files. |
| depends on | 44.8. |
| parallel-safe | yes by write set, but ordered after 44.8 so evidence is real. |
| verification tier | `DOCS` |
| verification commands | `npx prettier --check docs/handoff/day-44-demo-seed-runbook.md`<br>`node -e "const fs=require('node:fs');const s=fs.readFileSync('docs/handoff/day-44-demo-seed-runbook.md','utf8');for(const v of ['DEMO_SEED_ACCOUNT_PASSWORD','OPENROUTER_API_KEY','generate-rag-fixture.ts --generate','generate-rag-fixture.ts --verify','--start-date','npm run seed:demo','npm run e2e:day44','RAG_FIXTURE_PROVENANCE=PASS','RAG_READY=PASS','IDEMPOTENT_RERUN=PASS','BOOKING_READY=PASS','PARCEL_READY=PASS','DAY44_RUN=PASS','/audit-day 44'])if(!s.includes(v))throw Error('missing '+v);for(const k of ['accessToken','refreshToken','password','otp','apiKey','Authorization'])if(new RegExp(k+'\\s*[:=]\\s*\\S+','i').test(s))throw Error('credential/header-like value');"`<br>`git diff --check -- docs/handoff/day-44-demo-seed-runbook.md` |
| full regression owner | `audit-day` |
| invariant flags | LF `.md`; no usable secret/header; runtime variables only; one-time generation separated from offline defaults; no schema/contract change; audit-day alone owns full regression. |
| acceptance | The runbook records prerequisites, Production rejection, exact seed/E2E commands, account mapping without passwords, expected counts/timing/PASS markers, and safe rerun/conflict behavior. It separately documents intentional one-time fixture generation requiring runtime `OPENROUTER_API_KEY`, its redacted output and reviewable fixture/provenance diff, and ordinary offline `--verify`/seed/E2E commands that neither require nor call the provider. It states canonical seed SQL remains system-only and `/audit-day 44` owns full regression/day closure. |
| source citations | `BE_TIMELINE_VU.md` §Day 44 Review; `BACKEND_SOURCE_OF_TRUTH.md` §3.1, §11.1, §12; `db-schema/identity-user/README.md` bootstrap System Admin; Tasks 44.1, 44.6, and 44.8 command/output contracts. |

## Dispatch order

1. 44.1 freezes the durable manifest.
2. 44.2 and 44.3 may run in parallel after 44.1.
3. 44.4 starts after 44.3 while one-time fixture generation 44.6 starts after 44.2; they are
   parallel-safe and disjoint.
4. 44.5 starts after 44.4. Offline RAG seed Task 44.7 starts after 44.3 and 44.6, and may overlap
   44.4/44.5.
5. 44.8 integrates all modules and performs the sole task-level real-store project E2E.
6. 44.9 records verified output. `/audit-day 44` alone runs full solution/workspace regression.

Parallel-safe tasks by disjoint write set: **44.2, 44.3, 44.4, 44.5, 44.6, 44.7, 44.9**.
Dependency edges still control when they can start. Tasks **44.1** and **44.8** are not
parallel-safe.

## Progress tracker

> Informational orchestrator bookkeeping only; `/audit-day` independently re-verifies all work.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 44.1 | ✅ done | APPROVE | 2026-08-08 | Approved after 1 patch round; later human-approved E.164 corrective patch re-reviewed. |
| 44.2 | ✅ done | APPROVE | 2026-08-08 | Approved after 1 patch round; human-approved README/schema header scope expansion. |
| 44.3 | ✅ done | APPROVE | 2026-08-08 | Approved after 1 patch round; human-approved focused-test command correction. |
| 44.4 | ✅ done | APPROVE | 2026-08-08 | Approved after 2 review patch rounds; human-approved focused-test command correction. |
| 44.5 | ⬜ todo | — | — | Commerce owns paid saga/Invoice consistency. |
| 44.6 | ✅ done | APPROVE | 2026-08-08 | Approved after 1 review patch round; human-approved bootstrap retry and command corrections. |
| 44.7 | ⬜ todo | — | — | Offline RAG seed only. |
| 44.8 | ⬜ todo | — | — | Exact isolated real-store E2E owner. |
| 44.9 | ⬜ todo | — | — | — |

## Closed decisions

Q1–Q7 were explicitly approved by the human on 2026-08-08 and are fully represented in the
frozen manifest and downstream task acceptance: plan aliases/paid saga; 30-day schedule with
14-day materialization; exact five-Station topology; exact crew/password policy; exact wallet
provenance; exact Voucher matrix; and current RAG contract with explicit one-time generation,
attested committed vectors, and provider-independent default seed/E2E.

## Open questions

None.
