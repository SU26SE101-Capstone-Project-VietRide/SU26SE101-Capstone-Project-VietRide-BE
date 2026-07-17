# Day 23 schedule-change focused evidence

> Evidence state: Task 23.9 focused runtime gate complete on 2026-07-17 (Asia/Bangkok). Full
> solution/workspace regression is intentionally deferred to `/audit-day 23`.

## Reproducible entry points

| Scope | Command | Result |
|---|---|---|
| Runner syntax | `node --check scripts/run-day23-schedule-change-local.mjs` | PASS |
| TAP self-tests | `node --test --test-reporter=tap scripts/run-day23-schedule-change-local.test.mjs` | PASS; strict summary requires pass > 0 and fail/cancelled/skipped/todo = 0 |
| Postman parse | PowerShell `ConvertFrom-Json` for both cumulative artifacts | PASS |
| Focused evidence runner | `node scripts/run-day23-schedule-change-local.mjs --focused` | PASS |
| Changed-file hygiene | Task 23.9 exact `$changed` array with `git diff --check` | PASS |

Complete targeted output is retained in
[`day-23-schedule-change-verification.txt`](day-23-schedule-change-verification.txt).

The runner and Postman collection send public requests only to `GATEWAY_BASE_URL` (default
`http://localhost:3000`). The only schedule producer is
`PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=ALL_PENDING`; the passenger mutation is
`POST /v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`. There is no dedicated Trip
schedule endpoint, `/accept` or `/reject` alias, direct service URL, internal clock route, or job
trigger.

## Acceptance-to-evidence map

| Acceptance area | Deterministic evidence owner |
|---|---|
| Same-ICT MINOR `<=2h`, MEDIUM `>2h && <6h`, MAJOR `>=6h`/date change; old/new two-hour equality and strict-too-late preflight | `Day23AllPendingScheduleChangeProducerTests` and real-PostgreSQL `Day23AllPendingScheduleChangeProducerIntegrationTests` |
| PENDING_PAYMENT projection-only; CONFIRMED facts/action; immutable snapshot, current reads, causal CAS apply/duplicate/quarantine | `Day23ScheduleProjectionRulesTests`, `Day23ScheduleProjectionCasIntegrationTests`, and `Day23CurrentDepartureOperationalReadIntegrationTests` |
| Owner/role masking and all 12 exact resolver errors | `Day23ResolveScheduleActionAuthorizationTests`, controller tests, Gateway access-gate spec, plus the Day-23 Postman folder |
| UUID-v4 key, byte-identical replay, mismatch/pending, and new-key terminal conflicts | `Day23ResolveScheduleActionIdempotencyTests` |
| ACCEPTED preserves CONFIRMED; MEDIUM 50% and MAJOR 100% REJECTED use immutable exact-VND basis and one transaction | `Day23ScheduleChangeRefundRulesTests`, `Day23ResolveScheduleActionHandlerTests`, and PostgreSQL `Day23ResolveScheduleActionTransactionTests` |
| Canonical cancelled event identity and exact legacy fallback | `Day23BookingCancelledIdentityProducerTests`, shared-contract tests, and Trip/Payment/Booking/Notification compatibility tests |
| Day-22 T+2 versus Day-23 initial/terminal phase identity; before/equal/one-tick-after; passenger/job and duplicate-job races; rollback/repair; no timeout refund | frozen-clock `Day23RealertScheduleSeparationTests`, `Day23ScheduleChangeTimeoutStateMachineTests`, PostgreSQL `Day23RealertPhaseSeparationIntegrationTests`, `Day23ScheduleChangeTimeoutRegistrationTests`, and `Day23ScheduleChangeTimeoutRaceIntegrationTests` |
| `payload.eventId == outbox_events.id == RabbitMQ MessageId`, including restart | `Day23ExplicitOutboxIdentityTests`, `Day23OutboxRestartIdentityTests`, and `Day23RabbitMqEnvelopeIdentityTests` |
| Required/re-alerted/auto-resolved mapping, durable binding, ACK/redelivery, and MessageId dedupe | `day23-schedule-change-notification.spec.ts` and `day23-schedule-change-notification.e2e-spec.ts` |

The runner validates 12 exact retained result locators under
`TestResults/day23/task-23-9-evidence`: each TRX must name its declared suite/filter, execute at
least one test, and report zero failures; the Jest JSON must report success, passed tests, and zero
failures. The manifest includes current-departure reads, projection CAS, resolver
authorization/idempotency/transaction, PostgreSQL microsecond deadline precision, frozen-clock
races, explicit Outbox identity, restart, RabbitMQ identity, and Notification mapping/dedupe. These controlled results remain authoritative
for equality, multi-hour phases, fault injection, and races; the live journey does not wait against
the production clock or invent a clock/job HTTP backdoor.

## Postman and fixture boundary

The Postman environment contains placeholders only. The runner creates runtime JWTs, fresh UUID-v4
idempotency keys, and a uniquely tagged six-flow fixture graph in memory. It drives MINOR,
MEDIUM accept, MEDIUM 50% reject, MAJOR 100% reject, PENDING_PAYMENT projection-only, and a
MEDIUM case more than 24 hours in the future that exercises PostgreSQL/.NET timestamp precision;
the resolver matrix covers replay/new-key conflicts, masking, and all 12 registered error codes,
including a bounded concurrent in-flight request. The live DB assertions require exactly zero
actions/events for PENDING_PAYMENT, exactly one informational event and zero actions for MINOR,
and exactly one required event/action with frozen severity/refund metadata for MEDIUM/MAJOR.
The concurrent probe uses an explicit `LOCK_ACQUIRED`/`LOCK_RELEASED` handshake and bounded child
cleanup; it does not use a fixed database sleep. Its nested `finally` releases or rolls back the
locker, aborts the first request if its bounded settlement expires, and awaits settlement before
outer fixture cleanup can begin. TAP fault injection asserts that ordering.

`runIsolatedGatewayJourney()` enters `try/finally` before setup begins. IDs are recorded before
inserts; generated audit, Outbox, action, history, notification, and delivery IDs are captured
before deletion. Cleanup deletes only the owned Identity operator/users; Trip schedules, trips,
vehicles, route, stations, type, audits, skip logs and Outbox; Booking rows, actions, history and
Outbox; Notification rows and both delivery tables; and exact Redis idempotency keys. It then
asserts every owned set is zero. TAP separately proves successful, partial-setup,
runtime-failure, and combined journey/cleanup-failure semantics. Non-fixture and destructive
cleanup is forbidden.

## Scope ledger and deferred work

- Scope expansion: `docs/handoff/evidence/day-23-schedule-change-verification.txt` — complete
  targeted-command transcript required for reviewer-readable evidence; authorized by the Task 23.9
  evidence auto-expand envelope.
- Dependencies/secrets: none added.
- Production, Docker/CI, schema/migration, and prior-day artifacts: unchanged.
- Full regression: deferred exclusively to `/audit-day 23`.
