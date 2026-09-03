# Focused Parcel compensation live E2E

## Scope

Runner: `scripts/run-parcel-compensation-focused-e2e.mjs`.

This is a bounded HTTP/broker test, not a full-day runner. It uses the existing
`vietride_postgres`, `vietride_redis`, and `vietride_rabbitmq` containers. It never runs Docker
build, pull, compose up, or creates an app container.

Fixtures are SQL-seeded into four **new isolated databases**: two operators (one tenant-fence
fixture), an operator admin/staff, driver, assistant, passenger, foreign admin, one assigned trip,
seven accepted/lost Parcels, and simulated funding. Identity bootstraps a test System Admin.
Carriage, payment of freight, investigation completion, and the later Trip-settled snapshot are
simulated; registration, login/password, email, VNPay, booking and Notification are not tested.
Synthetic evidence references are stored through the API; no document or image is fetched.

Gateway validates real RS256 test tokens against the running Identity JWKS. Application code
runs from current Release builds. Claim submission, evidence upload, previews, decisions, appeal,
idempotency, RabbitMQ/Outbox payout, Parcel status consumption, and FE financial reads run live.
The test does not set a claim/appeal to PAID or insert compensation wallet transactions manually.

## Run safely

Prerequisites: current Release builds for Identity, Trip, Payment and Parcel, and current
Gateway/shared-library output under `dist/`. Build only those projects, sequentially if RAM is
limited. No full-day script, all-project build, image build, or new dependency is needed.

```powershell
docker start vietride_postgres vietride_redis vietride_rabbitmq
node --max-old-space-size=256 scripts/run-parcel-compensation-focused-e2e.mjs
```

The runner reserves localhost ports `18100`, `18101`, `18102`, `18104`, `18105` and refuses to
start if any is occupied. It starts Gateway plus four .NET hosts sequentially, with one Hangfire
worker per service and a 256 MiB .NET managed-heap limit. Gateway uses the installed Nx workspace
resolver, without a watcher/build process tree. No changes to workspace package links are made.

Each run generates fresh DB credentials, an RSA key and an isolated RabbitMQ vhost/user. These
are never written to the report. Redis uses unique request IDs; it is never flushed. Services
apply their existing migrations to test databases only. Existing demo databases and RabbitMQ
queues are not used.

`finally` stops only this run's process IDs, drops only its named test databases and DB role,
and removes only its RabbitMQ vhost/user. The three existing infra containers remain running.
Reports and service logs are retained under `artifacts/parcel-compensation-focused-e2e/<run>/`.
An unexpected OS kill/power loss can bypass cleanup; inspect that run's exact resources before
removing anything. Never use broad cleanup/prune/reset commands.

## Assertions

All examples use a frozen 50% rate, 30m cargo cap, legacy multiplier 4, and 150,000 VND freight.

| Case | Cargo | Remaining freight | Total |
|---|---:|---:|---:|
| No declaration, NO_PROOF | 0 | 150,000 | 150,000 |
| Declared 200,000, NO_PROOF | 0 | 150,000 | 150,000 |
| Declared 10m, UNVERIFIED, injected client award ignored | 0 | 150,000 | 150,000 |
| Declared 10m, VERIFIED direct loss 200,000 | 100,000 | 150,000 | 250,000 |
| No declaration, VERIFIED direct loss 200,000 | 100,000 | 150,000 | 250,000 |
| UNVERIFIED, previously refunded 50,000 | 0 | 100,000 | 100,000 |
| NO_PROOF, previously refunded all 150,000 | 0 | 0 | 0 (preview 200; approval 422) |

- Role and cross-tenant fences, required proof/loss combinations, duplicate/wrong evidence.
- Direct Parcel access with a correctly shaped but forged Internal JWT returns 401; RabbitMQ
  management is reachable and the isolated `vietride.events` exchange is a topic exchange.
- Rejected validation leaves claim SUBMITTED; zero-total approval creates no payout.
- Upload alone never makes proof VERIFIED; accepted evidence links match the reviewer.
- Preview has no Outbox write; accepted mutation uses the same calculated award.
- Six claims reach PAID through real Payment events, with one passenger credit and one source
  debit each. Same-key replay and a new-key duplicate decision cannot pay again.
- A paid NO_PROOF claim cannot gain another freight refund through a NO_PROOF appeal.
- A VERIFIED appeal revises 150,000 to 250,000 but pays only the 100,000 difference; the original
  claim remains unchanged. A simulated settled Trip makes this payout debit OperatorWallet.
- FE APIs show six Admin PlatformWallet debits, one OperatorWallet debit, seven OperatorLedger
  entries, and PassengerWallet compensation history. Final passenger balance is 1,150,000 VND.
- ADR 0004 envelope and traceId are checked on every business HTTP response.

## Recorded verification

2026-09-03, final run `8978aec38a`: **PASS — 23 check groups, 87 Gateway business HTTP requests**,
plus direct health, forged Internal JWT and broker checks. Earlier run `6eb7dbd22f` also passed
the business matrix (21 groups, 89 requests); polling count varies with event delivery timing.
Reports: `artifacts/parcel-compensation-focused-e2e/8978aec38a/report.md` and `report.json`.
Cleanup completed: all five app processes, four databases, DB role, RabbitMQ vhost/user removed.

Two earlier setup attempts are retained transparently: `c0cd3096af` failed to resolve a built
Gateway workspace library; `73cd19dc54` seeded manual adjustment funding that is intentionally
excluded from Trip holding and reached FUNDING_PENDING. The runner now uses Nx's normal loader
and simulated collected revenue respectively. Neither was a compensation formula failure.

This result covers the listed matrix, not full financial regression, exhaustion of multiple
operators' pooled holding, historical payout recovery, forged-document detection, or full
registration/carriage/settlement workflows.
