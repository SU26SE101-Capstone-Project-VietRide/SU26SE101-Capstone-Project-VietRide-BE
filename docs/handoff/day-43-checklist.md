# Day 43 — Final checklist

> Produced by `/audit-day 43` after the idempotency-inventory reopening and reliability regression
> audit on 2026-08-01.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 43 (SCV-131)
- **Plan**: `docs/handoff/day-43-plan.md`
- **Repair plan**: `docs/handoff/day-36-43-fe-gap-repair-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] Events exceeding the approved retry boundary enter the owning service DLQ exactly once. The
  final chaos run observed one terminal row with source `retry_count=6`.
- [x] `GET /v1/admin/outbox/dlq` remains available through the Identity facade with cursor and
  degraded-source behavior covered by the Day 43 E2E.
- [x] The exhaustive mutation inventory is green: 171 endpoints, 158 requiring idempotency and 13
  explicit exemptions.
- [x] All five Hangfire-owning services expose Internal-JWT job status with the approved lag
  semantics; the reliability E2E passed the job-health assertions.
- [x] RabbitMQ outage retains the Outbox event, records a failed publish, and drains after restart.
  The final run observed the event become `PUBLISHED` with `retry_count=1`.
- [x] Existing consumers recover after a broker restart without restarting the application;
  topology is recreated and old delivery tags remain bound to their original channel.

## Tasks completed

- Tasks 43.0–43.8 — original DLQ, idempotency, job-health and chaos delivery — ✅
- Reopen R2 — absolute action routes, `[NonAction]` and duplicate discovery parser repair — ✅
- Reopen R10 — exhaustive final inventory freeze and regression audit — ✅
- Audit repair — Identity transactional email now sends a UUID-v4 idempotency key — ✅
- Audit repair — consumer channel/topology recovery after broker restart — ✅
- Audit repair — concurrent connection creation no longer holds a monitor across network retries;
  each connection attempt is bounded to five seconds — ✅

## Changed files

- `scripts/verify-idempotency-inventory.mjs` and tests — exhaustive endpoint, handler,
  Notification binding and outbound HTTP discovery.
- `tests/dotnet/idempotency-endpoint-inventory.json` — final 171/158/13 inventory.
- `apps/identity/src/.../NotificationEmailClient.cs` and focused tests — required UUID-v4
  idempotency for transactional email.
- `libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqConsumerBackgroundService.cs` and tests —
  channel closure detection, topology recreation and stable per-consumer channel capture.
- `libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqConnectionFactory.cs`, options and tests —
  non-blocking double-check installation, bounded connection attempts and concurrency/disposal
  coverage.
- `docs/handoff/day-43-plan.md` — reopening addendum and corrected cross-system baseline.

## Verification run

| Command / check | Result | Notes |
| --- | --- | --- |
| Identity build / format / tests | PASS | 0 warnings/errors; unit 328/328; integration 159/159 |
| Trip build / format / tests | PASS | 0 warnings/errors; unit 581/581; integration 264/264 |
| Booking build / format / tests | PASS | 0 warnings/errors; unit 535/535; integration 209/209 |
| Payment build / format / tests | PASS | 0 warnings/errors; unit 160/160; integration 68/68 |
| Parcel build / format / tests | PASS | 0 warnings/errors; unit 368/368; integration 58/58 |
| Shared libraries final build / format / tests | PASS | 0 warnings/errors; Messaging 43/43, Web 99/99, Reporting 11/11, Persistence 37/37 |
| Full TS lint/test/build matrix | PASS | All 10 TS projects and dependencies completed; existing build warnings were non-fatal |
| `npm run verify:idempotency-inventory` | PASS | 171 total / 158 required / 13 exempt; 43 .NET handlers; 14 source subscribe callsites → 73 runtime bindings; 24 outbound mutation-style HTTP callsites / 5 exact exemptions |
| `node --test scripts/verify-idempotency-inventory.test.mjs` | PASS | 9/9 |
| `npm run e2e:day43` | PASS | Final post-fix run exit 0 in 479s: inventory, seed, DLQ, idempotency, job health, migration up/down/reapply, acceptance and cleanup |
| `npm run e2e:parcel-settlement -- --reuse-images` | PASS | Post-factory regression 643 assertions in 359s; broker recovery and callback reconciliation passed |
| Production-like `docker compose ... --profile app up -d --build` | PASS | Build/up exit 0 |
| Production-like `/health` matrix | PASS | Gateway plus Identity, Trip, Booking, Payment, Parcel, Tracking, Notification and RAG all HTTP 200; infra healthy |
| Hard invariants | PASS | CPM, banned deps, no co-author trailer, diff-check and EOL across 153 changed files |
| Day 43 Review bullet | PASS | Broker killed; Outbox retained/failed; broker restarted; row drained as `PUBLISHED retry_count=1`; terminal event produced exactly one DLQ row at retry 6 |

## Final inventory

| Service | Total | Required | Exempt |
| --- | ---: | ---: | ---: |
| Identity | 35 | 30 | 5 |
| Trip | 54 | 53 | 1 |
| Booking | 27 | 26 | 1 |
| Payment | 15 | 11 | 4 |
| Parcel | 30 | 29 | 1 |
| Notification | 3 | 3 | 0 |
| RAG | 7 | 6 | 1 |
| **Total** | **171** | **158** | **13** |

## Contract / event / schema changes shipped

The reopened inventory includes the internal Payment redirect lookup and Booking refund consumer
added by the repair plan. Their contract/event registry entries and BSOT changelog were updated in
R0/R5/R7. The Day 43 audit repairs add no endpoint, migration or schema object. RabbitMQ connection
attempt timeout is backward-compatible with existing configuration through a default of five
seconds.

## Known gaps & carry-over for Day 44

- None for Day 43 closure.
- The first final rerun hit a transient `fetch failed`; the clean rerun then exposed and drove the
  real Outbox starvation fix. The post-fix 479-second run is the authoritative result.

## Notes for Day 44 planning

Reliability tests that stop RabbitMQ must preserve both sides of the contract: the publisher must
return failure soon enough to mark Outbox retry, and existing consumers must recreate topology
without application restarts.
