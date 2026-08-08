# Day 44 demo seed runbook

This runbook hands off the deterministic `day44-v1` demo fixture verified by Task 44.8. It is
for an isolated demo or development environment only. The seed rejects `Production`; do not
point it at production databases or reuse the canonical system seed as demo data.

## Prerequisites

- Run from the repository root with the repository-supported Node.js/npm versions and Docker
  Engine with Docker Compose available.
- Ensure the service schemas and canonical system bootstrap have been applied. Identity must
  contain exactly one active, non-deleted System Admin created through
  `SYSTEM_ADMIN_BOOTSTRAP_*`. The demo seed does not create or replace that account.
- Choose `--start-date=<YYYY-MM-DD>` as a real calendar date at least one day in the future in
  `Asia/Ho_Chi_Minh`. The seed does not silently move an invalid or past date.
- Supply `DEMO_SEED_ACCOUNT_PASSWORD` only to the process that launches the command, using an
  approved runtime secret source. Never put it in `.env`, command arguments, shell history,
  logs, screenshots, tickets, or this document. Remove it from the process after the command.
- Ordinary seed, verification, and E2E runs must have `OPENROUTER_API_KEY` absent. They use the
  committed, attested fixture and make no embedding-provider request.

## Commands

Use the same explicit future ICT date for both commands in a handoff session.

Seed an already-running compatible local stack:

```powershell
npm run seed:demo -- --start-date=<YYYY-MM-DD>
```

Run the isolated real-store gate, including two seed runs, acceptance queries, Gateway Booking
and Parcel smoke checks, and cleanup:

```powershell
npm run e2e:day44 -- --start-date=<YYYY-MM-DD>
```

Before either command, inject `DEMO_SEED_ACCOUNT_PASSWORD` into that command's process without
printing it. Ensure `OPENROUTER_API_KEY` is removed from the child environment. Clear the
runtime variable in a `finally`/equivalent cleanup path even when the command fails.

Verify the committed RAG fixture offline:

```powershell
node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --verify --fixture=scripts/day44/fixtures/rag-embeddings.json --provenance=scripts/day44/fixtures/rag-embeddings.provenance.json --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt
```

This command must run without `OPENROUTER_API_KEY`, must not call the provider, and must emit:

```text
RAG_FIXTURE_PROVENANCE=PASS
```

## Login account mapping

Only the 25 Day 44-created accounts below (3 Operator Admins, 9 Drivers, 3 Assistants, and
10 Passengers) use the runtime `DEMO_SEED_ACCOUNT_PASSWORD`. The existing System Admin is not
one of those 25 accounts and retains its independently managed `SYSTEM_ADMIN_BOOTSTRAP_*`
credentials. This mapping intentionally contains no credential values.

| Role | Operator | Login email(s) |
|---|---|---|
| System Admin | Global | The existing `SYSTEM_ADMIN_BOOTSTRAP_EMAIL`; not created by Day 44 |
| Operator Admin | A, B, C | `operator.a@demo.vietride.local`, `operator.b@demo.vietride.local`, `operator.c@demo.vietride.local` |
| Driver | A | `driver.a1@demo.vietride.local` through `driver.a3@demo.vietride.local` |
| Driver | B | `driver.b1@demo.vietride.local` through `driver.b3@demo.vietride.local` |
| Driver | C | `driver.c1@demo.vietride.local` through `driver.c3@demo.vietride.local` |
| Assistant | A, B, C | `assistant.a@demo.vietride.local`, `assistant.b@demo.vietride.local`, `assistant.c@demo.vietride.local` |
| Passenger | Global | `passenger01@demo.vietride.local` through `passenger10@demo.vietride.local` |

Operators A and B use the Business Demo plan. Operator C uses Starter (Free Trial). No Day 44
OAuth identities or Operator Staff accounts are created.

## Expected state and verification markers

The E2E gate validates the complete fixture, including:

- Identity: 1 System Admin; 3 Operators; 3 Operator Admins; 0 Operator Staff; 9 Drivers;
  3 Assistants; 10 Passengers; 2 plans; 3 subscriptions; 2 upgrade attempts and matching inbox
  evidence.
- Payment: 2 succeeded subscription payments; 2 issued invoices with completed PDF metadata;
  2 processed events; 2 published Outbox events; and 2 immutable platform credits totaling
  4,000,000 VND.
- Trip: 5 Stations; 15 OperatorStation links; 9 Stops; 9 Routes; 3 return pairs;
  3 AlternativeRoutes; 9 RouteStops; 9 AlternativeRouteStops; 9 Vehicles; 9 schedules;
  126 Trips; 126 TripStops; and 3,948 TripSeats, with the calculated ICT monthly counters.
- Commerce: 10 wallets, 10 successful top-ups, and 10 immutable wallet transactions; exactly
  5 Vouchers and 2 accepted operator consents; exactly 2 active SMALL ParcelRouteFares.
- RAG: exactly 3 approved documents and 3 searchable chunks, each with a 2,048-dimensional
  attested embedding, correct model and access controls. The provider request counter remains
  zero.

The complete retained non-sensitive Task 44.8 E2E transcript was:

```text
> vietride-backend@0.1.0 e2e:day44
> node scripts/run-day44-seed-e2e.mjs --start-date=2026-08-10

DAY44_SEED_CHECKSUM=abb63efac9a49b286af3c85f6fb7646f9133964316197ffd5c7587754a8e0a18
DAY44_SEED=PASS
DAY44_SEED_RUN_1_MS=64585
DAY44_SEED_CHECKSUM=abb63efac9a49b286af3c85f6fb7646f9133964316197ffd5c7587754a8e0a18
DAY44_SEED=PASS
DAY44_SEED_RUN_2_MS=66469
IDEMPOTENT_RERUN=PASS
RAG_READY=PASS
BOOKING_READY=PASS
PARCEL_READY=PASS
DAY44_RUN=PASS

{
  "ProcessPasswordAbsent": true,
  "UserPasswordAbsent": true,
  "LauncherCount": 0,
  "Containers": 0,
  "Volumes": 0,
  "Networks": 0
}
```

Both runs must remain below 120,000 ms and produce the same database-state checksum. The
isolated gate must also finish with no retained secret or project resources. The verified
cleanup JSON above confirms the runtime/User variables are absent and there are zero launcher
processes, matching containers, volumes, and networks.

## Safe rerun and conflict behavior

An exact rerun is supported: all fixed IDs, natural keys, full projected rows, immutable
financial evidence, RAG state, and checksum must already match. The second run must create no
duplicate credits, events, or child rows and emits `IDEMPOTENT_RERUN=PASS`.

The seed fails closed before its first write when it finds a partial fixture, a foreign row in
the owned namespace or parent scope, an ID/natural-key collision, changed full state, unexpected
financial evidence, an attached OAuth identity, incomplete RAG state, or invalid provenance.
Do not delete, rename, or manually repair conflicting rows merely to make the seed pass. Preserve
the diagnostics and investigate the drift. The isolated E2E harness always tears down its
validated unique Compose project with volumes and orphans, including after startup or smoke
failure.

## One-time RAG fixture generation

Fixture generation is not a normal seed, E2E, or verification step. It was a one-time bootstrap
that required explicit human approval, both fixture outputs to be absent, and a runtime-only
`OPENROUTER_API_KEY`. A refresh must have a new reviewed plan and must never overwrite the
committed fixture implicitly.

The generator invocation is:

```powershell
node --require ts-node/register/transpile-only scripts/day44/generate-rag-fixture.ts --generate --base-url=https://openrouter.ai/api/v1 --model=nvidia/llama-nemotron-embed-vl-1b-v2:free --fixture=scripts/day44/fixtures/rag-embeddings.json --provenance=scripts/day44/fixtures/rag-embeddings.provenance.json --documents=docs/rag/vietride-public-demo-knowledge-base.txt,docs/rag/vietride-operator-demo-knowledge-base.txt,docs/rag/vietride-admin-demo-knowledge-base.txt
```

For an approved regeneration only, inject the provider key into the generator process without
printing it, capture output in memory, suppress output if any credential/header-like content is
detected, and clear the variable afterward. Review the complete fixture/provenance diff,
including document SHA-256 values, model, dimension, and final fixture SHA-256, before accepting
it. Then remove the key and run the offline `generate-rag-fixture.ts --verify` command above.
Normal `seed:demo` and `e2e:day44` runs remain provider-independent.

## Task 44.9 verification evidence

The owned runbook passed its exact DOCS checks after the final documentation patch.

Prettier command and complete output:

```text
npx prettier --check docs/handoff/day-44-demo-seed-runbook.md
Checking formatting...
All matched files use Prettier code style!
```

Required-marker and credential-pattern scan command:

```text
node -e "const fs=require('node:fs');const s=fs.readFileSync('docs/handoff/day-44-demo-seed-runbook.md','utf8');for(const v of ['DEMO_SEED_ACCOUNT_PASSWORD','OPENROUTER_API_KEY','generate-rag-fixture.ts --generate','generate-rag-fixture.ts --verify','--start-date','npm run seed:demo','npm run e2e:day44','RAG_FIXTURE_PROVENANCE=PASS','RAG_READY=PASS','IDEMPOTENT_RERUN=PASS','BOOKING_READY=PASS','PARCEL_READY=PASS','DAY44_RUN=PASS','/audit-day 44'])if(!s.includes(v))throw Error('missing '+v);for(const k of ['accessToken','refreshToken','password','otp','apiKey','Authorization'])if(new RegExp(k+'\\s*[:=]\\s*\\S+','i').test(s))throw Error('credential/header-like value');"
```

Result: PASS (exit `0`, no output).

Tracked scoped whitespace command:

```text
git diff --check -- docs/handoff/day-44-demo-seed-runbook.md
```

Result: PASS (exit `0`, no output). Because the owned file was new and untracked, this approved
no-index check also inspected its actual contents:

```text
git diff --no-index --check -- /dev/null docs/handoff/day-44-demo-seed-runbook.md; $nativeExit=$LASTEXITCODE; if($nativeExit -eq 1){Write-Output 'UNTRACKED_NO_INDEX_DIFF_CHECK=PASS (native git exit 1; no whitespace diagnostics)'; exit 0}; exit $nativeExit
UNTRACKED_NO_INDEX_DIFF_CHECK=PASS (native git exit 1; no whitespace diagnostics)
```

## Ownership and closeout

Canonical `db-schema/*/seed.sql` remains system/bootstrap-only; it is not a source for Day 44
demo accounts and must not be changed for this handoff. Task-level checks do not close the day.
Run `/audit-day 44` to perform the full regression, independently audit the delivered behavior,
and own Day 44 closure.
