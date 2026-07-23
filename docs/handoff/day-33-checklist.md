# Day 33 - Final checklist

- **Timeline ref**: `BE_TIMELINE_VU.md` -> Day 33 - Trip disruption: operator cancel + alternative route (`SCV-112`)
- **Plan**: `docs/handoff/day-33-plan.md` - APPROVED
- **Audit date**: 2026-07-23
- **Status**: ✅ READY

## DoD result

- [x] ✅ **Trip cancellation triggers bulk refunds through Outbox with eventual retry max 5.** The final Gateway/Newman run returned `200` for preview and confirmation, persisted Trip `CANCELLED` with one Trip Outbox event, propagated Booking `CANCELLED` once, cancelled the Parcel with its full collected `25000` VND, credited the passenger wallet by `125000` VND, and left zero refund-failure rows. Payment tests verify that an initial failure is persisted at `retryCount=0` and the recurring job owns the maximum-five-attempt lifecycle.
- [x] ✅ **Route change creates pending actions.** The final Gateway/Newman run returned `200`, persisted one immutable pending action while keeping the Booking `CONFIRMED`, retained the selected alternative route and frozen candidate/fallback metadata, and emitted exactly one route-change event.
- [x] ✅ **Review: refund failure retry.** Payment unit `113/113` and integration `41/41` passed. During runtime fault discovery, one concurrent refund was deliberately observed as a persisted retriable failure while the Trip was already `CANCELLED`; after the platform-wallet serialization fix, the final two-refund run completed with `125000|0`.
- [x] ✅ **Review: partial refund failure does not block Trip `CANCELLED`.** Runtime evidence showed Trip `CANCELLED|1` before a downstream Payment concurrency failure was persisted. The final run then proved successful independent Booking and Parcel refund completion.

## Tasks completed

- Task 33.0 - Ratify docs and registry - ✅
- Task 33.1 - Add `alternative_route_id` migration - ✅
- Task 33.2 - Implement Trip cancellation preview/confirmation - ✅
- Task 33.3 - Consume Trip cancellation in Booking - ✅
- Task 33.4 - Process Booking/Parcel refunds with eventual retry - ✅
- Task 33.5 - Implement alternative-route change and immutable impact snapshot - ✅
- Task 33.6 - Create, resolve, and auto-fallback `ROUTE_CHANGE` pending actions - ✅
- Audit blocker closure - correct Parcel impact/refund aggregation, JSONB booking-group lookup, concurrent platform-wallet mutation, and operator-wallet consumer transaction ownership - ✅

## Changed files

- `BACKEND_SOURCE_OF_TRUTH.md`, `VietRide_API_Contract_v1.md`, `docs/handoff/day-33-plan.md` - aligned Day-33 contracts, event registry, timeout behavior, and retry ownership with the SOT hierarchy.
- `apps/trip/` - cancellation preview/confirmation, real Booking and Parcel impact clients, route-change transaction ownership, Outbox behavior, DI, and regression tests.
- `apps/booking/` - immutable impact totals, canonical station writes, pending-action fallback metadata, timeout auto-fallback event, and regression tests.
- `apps/parcel/` - internal Trip cancellation-impact query, full collected-amount refund, repository projection, and tests.
- `apps/payment/` - canonical cancellation compatibility, deferred retry lifecycle, trusted direct/group/Parcel-additional payment context lookup, serialized platform-wallet mutations, operator-wallet inbox ownership, and tests.
- `apps/notification/`, `libs/shared/contracts/` - `booking.booking.route_change_auto_fallback_applied` contract, consumer, mapper, and tests.
- `docs/api/postman/`, `scripts/run-day33-trip-disruption-e2e.mjs`, `package.json` - cumulative Day-33 Postman coverage and isolated local E2E runner with bounded fixture cleanup.
- `docs/handoff/day-33-checklist.md` - this audit record.

## Verification run

| Command / gate | Result | Evidence |
|---|---|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | Exit `0`. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | Unit `536/536`; integration `234/234`. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | ✅ PASS | Exit `0`. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | Unit `474/474`; integration `169/169`. |
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | ✅ PASS | Final post-fix run: `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes` | ✅ PASS | Final post-fix run: exit `0`. |
| `dotnet test apps/payment/VietRide.Payment.sln -c Release --no-build` | ✅ PASS | Final post-fix run: unit `113/113`; integration `41/41`. |
| `dotnet build apps/parcel/VietRide.Parcel.sln -c Release` | ✅ PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes` | ✅ PASS | Exit `0`. |
| `dotnet test apps/parcel/VietRide.Parcel.sln -c Release` | ✅ PASS | Unit `200/200`; integration `44/44`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | All requested TS/NestJS targets passed; dependency source-map warnings only. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | All requested targets passed. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | All requested targets passed; Notification regression suite included `167/167`. |
| Trip EF apply -> rollback to `20260722093804_AddIntegrationInbox` -> reapply `20260723090510_AddTripAlternativeRoute` | ✅ PASS | Day-33 `Down()` reversed cleanly and the migration reapplied. |
| `dotnet ef migrations has-pending-model-changes ...` | ✅ PASS | No pending model changes; existing EF sentinel warnings only. |
| `docker compose ... --profile app up -d --build` plus final Payment rebuild | ✅ PASS | All nine applications and four infrastructure containers run the audited source. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | Gateway, five .NET services, three workers, PostgreSQL, Redis, RabbitMQ, and PgBouncer all healthy. |
| `/health` matrix | ✅ PASS | Ports `3000`, `5001`-`5005`, and `3001`-`3003` all returned HTTP `200`. |
| Postman collection/environment parse + `node --check scripts/run-day33-trip-disruption-e2e.mjs` | ✅ PASS | Both JSON artifacts parsed and the runner passed syntax validation. |
| `npm run postman:day33:local` | ✅ PASS | Four requests/assertions passed: preview `200`, cancel `200`, route change `200`, same-key/different-body `422 IDEMPOTENCY_KEY_MISMATCH`. |
| Day-33 DB/Outbox/RabbitMQ side effects | ✅ PASS | Trip `CANCELLED|1`; Booking `CANCELLED|1`; wallet `125000|0`; Parcel `CANCELLED|25000`; one frozen route action; one route event. |
| `identity.operator.approved` RabbitMQ runtime probe | ✅ PASS | Operator wallet `1`; generic inbox marker `1`; no nested transaction or duplicate marker. Probe rows were removed afterward. |
| Day-33 Review bullet overall | ✅ PASS | Happy, adversarial, downstream-failure isolation, retry persistence, and final successful propagation were all executed; no external credential skip. |
| CPM / banned dependencies / MediatR scan | ✅ PASS | No inline `.csproj` versions, no banned dependency declaration, no new dependency; MediatR `11.1.0`. |
| Commit trailer check | ✅ PASS | No `Co-Authored-By` in the delivered diff. |
| `git ls-files --eol` plus untracked-file byte check | ✅ PASS | .NET files are CRLF; TS/JSON/Markdown/MJS files are LF. |
| `git diff --check` | ✅ PASS | No whitespace errors. |
| Audit fixture/DLQ cleanup | ✅ PASS | Isolated SQL/Redis fixtures and all DLQ messages created by this audit were removed; pre-existing unrelated queue data was not altered. |

## Contract / event / schema changes shipped

- REST: cancellation preview/confirmation and alternative-route mutation remain the public Day-33 endpoints; preview now returns real Booking and Parcel IDs/totals.
- Internal REST: `GET /internal/v1/parcels/trips/{tripId}/cancel-impact?operatorId=...`.
- Events: `trip.trip.cancelled`, `trip.trip.route_changed`, canonical `booking.booking.cancelled`, and new `booking.booking.route_change_auto_fallback_applied`.
- Schema: Trip migration `20260723090510_AddTripAlternativeRoute` adds nullable `alternative_route_id` and `idx_trips_alternative_route_id`, with a reversible `Down()`.
- Retry/concurrency convention: the initial cancellation refund attempt persists an unresolved failure at `retryCount=0`; the recurring Hangfire job owns at most five retries; platform-wallet writes serialize within the ambient transaction.
- BSOT event registry and changelog were updated through version `1.43.0`.
- No new dependency or Day-33 error code was added.

## Known gaps & carry-over for Day 34

- No Day-33 functional gap remains.
- Local logs still contain known non-blocking EF default-sentinel warnings and the Newman dependency warning for deprecated `fs.F_OK`.
- Historical, unrelated local DLQ/backfill data predating the final audit remains intentionally untouched.

## Notes for Day 34 planning

- Day 34 may treat Day 33 as closed.
- Reuse `npm run postman:day33:local` as the regression probe for Trip cancellation/route-change seams.
- Keep Payment integration handlers under the generic inbox transaction; handlers must not begin a second transaction or write a duplicate processed-event marker.
