# Day 23 — Final checklist

> Produced by `/audit-day 23` on 2026-07-18, then updated after the Day-23 branch merged
> `origin/main` at `0a1bd9f`. The post-merge rerun covered the independent SOT/code audit, full
> .NET/TS regression, four-service migration rollback/re-apply and model-drift gates,
> rebuilt 13-container health, the real Day-23 Gateway journey, the isolated Day-40 journey,
> and the affected earlier cancellation regression.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 23 — Schedule change 3 levels + BookingPendingAction (SCV-100)
- **Plan**: `docs/handoff/day-23-plan.md`
- **Audited merge**: Day-23 head `d9eb664` + `origin/main` `0a1bd9f`, including the staged conflict resolutions and audit remediations
- **Status**: ✅ READY

## DoD result

- [x] ✅ No dedicated Trip schedule endpoint, Gateway route, passenger `/accept`, or passenger `/reject` alias exists. The rebuilt-stack journey used only `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=ALL_PENDING` and the canonical passenger `/resolve` endpoint.
- [x] ✅ Severity is based on absolute delta and ICT calendar date: same-date `<=2h` MINOR, `>2h && <6h` MEDIUM, and `>=6h` or date-change MAJOR. The relevant tests executed inside the full Trip/Booking runs and the Day-23 TAP/E2E boundary checks passed.
- [x] ✅ `ALL_PENDING` captures one clock and permits exact two-hour equality for both old and computed new departures while rejecting either value below two hours. Dedicated producer coverage passed inside Trip unit `486/486` and integration `183/183`.
- [x] ✅ `trip_snapshot_departure` remains immutable. Migration `20260717000000_AddBookingTripCurrentDeparture` backfills/indexes `trip_current_departure`; rollback produced column/index `0/0`, re-apply produced `1/1`, and `divergent=0`. Existing `date` and `sortBy=departureAt` semantics and nested `trip.currentDepartureAt` passed real PostgreSQL and Gateway checks.
- [x] ✅ Booking applies schedule events only to `PENDING_PAYMENT|CONFIRMED`, uses causal CAS (`old→new`, duplicate no-op, third value fails), and creates facts/actions only for `CONFIRMED`. The focused E2E observed exact projection/action/event cardinality for MINOR, MEDIUM, MAJOR, pending-payment, and long-range cases.
- [x] ✅ Passenger resolution is PASSENGER/owner-only, UUID-v4 idempotent, equality-eligible, masked in the specified order, and exposes no operator seat-assignment behavior. The real Gateway run observed every ratified `401/403/404/409/422` error code plus byte-identical replay/mismatch/pending behavior.
- [x] ✅ Reject freezes immutable `Booking.totalAmount`, uses 50% MEDIUM / 100% MAJOR with VND rounding, and atomically resolves/cancels/histories/enqueues one authoritative cancellation. Runtime evidence observed `100001→50001` and `100001→100001` refunds.
- [x] ✅ The Day-22 occurrence `+2h` `PendingActionRealertJob` production file is byte-unchanged. Separate Day-23 timeout coverage passed for initial/terminal `+1s`, equality, lag/direct acceptance, phase-distinct ids, retry/race/rollback/repair, and no timeout cancellation/refund.
- [x] ✅ `booking.booking.pending_action_auto_resolved` is emitted only for terminal MEDIUM/MAJOR acceptance; explicit Outbox identity persists through `payload.eventId == outbox_events.id == RabbitMQ MessageId`. Shared Messaging `10/10` and Persistence `14/14` tests passed.
- [x] ✅ Notification maps required/re-alerted/auto-resolved facts to existing `TRIP_SCHEDULE_CHANGED`, dedupes by MessageId, preserves both intended re-alert phases, and has no Prisma schema/migration diff. Full TS test `68` suites / `495` tests passed.
- [x] ✅ Focused PostgreSQL/Gateway evidence and cleanup pass, and the mandatory close-out regression is green. The stale cancellation payload assertion now verifies the canonical ten-field payload plus `payload.eventId == outbox.Id` and the producer-captured `occurredAt`; Booking is `438/438` unit and `134/134` integration after the merge.

## Tasks completed

- Task 23.0 — Reconcile the Day-23 contract and source-of-truth — ✅ every `Get-Content` in the exact gate now specifies UTF-8; the unmodified block passes on Windows PowerShell `5.1.26100.8875`, and the merged Day-23/Day-40 BSOT baseline is `1.36.0`.
- Task 23.1 — Preserve explicit event identity through the shared Outbox seam — ✅ shared build/format and all `112/112` shared tests passed; no dependency-direction or broker-envelope mismatch found.
- Task 23.2 — Add and backfill the Booking current-departure projection — ✅ migration apply/down/re-apply, index/column inspection, backfill check, and pending-model check passed.
- Task 23.3 — Harden the existing ALL_PENDING schedule-change producer — ✅ Trip `669/669` tests and real producer flows passed.
- Task 23.4 — Apply schedule events to the current projection and Booking-owned facts — ✅ CAS, operational reads, STOP_DISABLED current deadline, event identity, and runtime projection/action cardinality passed.
- Task 23.5 — Roll out canonical booking-cancelled event identity compatibly — ✅ production/runtime canonical behavior and the directly affected concurrent-delivery assertion pass in the full Booking solution.
- Task 23.6 — Resolve passenger schedule actions and transact refunds/cancellation — ✅ real Gateway acceptance, masking/errors, replay, 50%/100% rejection, DB state, Outbox, and cleanup passed.
- Task 23.7 — Run the shared MEDIUM/MAJOR timeout state machine durably — ✅ frozen-clock unit/integration coverage ran in the full Booking suite; no timeout refund/cancellation production path exists.
- Task 23.8 — Add Notification and shared-contract compatibility for timeout outcomes — ✅ full TS build/lint/test passed; Notification and shared contracts have no Prisma/dependency change.
- Task 23.9 — Prove the focused Day-23 journey end to end — ✅ TAP `10/10`, focused Gateway journey exit `0`, exact runtime statuses/side effects, and isolated cleanup all passed.

## Changed files

The Day-23 range contains 130 tracked files:

- Root SOT (4 files): `BE_TIMELINE_VU.md`, `SU26SE101_VIETRIDE_technical_context_v7.md`, `VietRide_API_Contract_v1.md`, and `BACKEND_SOURCE_OF_TRUTH.md` — canonical producer, projection, resolve/error, event/job, and changelog truth.
- `apps/booking/**` (71 files) — current-departure entity/config/migration/repositories, schedule consumer, passenger resolver, refund/cancellation identity, timeout scheduler/job, operator reads, and focused/full tests.
- `apps/trip/**` (11 files) — DriverSchedule producer, severity/event identity, mutable generation dedupe, cancellation compatibility, and tests.
- `apps/payment/**` (4 files) — strict canonical/legacy booking-cancelled consumer compatibility and tests.
- `apps/gateway/**` (1 file) — access-gate regression proving the existing Booking prefix and PASSENGER role gate; no production route change.
- `apps/notification/**` (16 files) — schedule fact bindings/mappers/dedupe, strict cancellation compatibility, and focused unit/e2e specs; no Prisma change.
- `libs/dotnet/**` (3 files) and `tests/dotnet/**` (3 files) — explicit Outbox id seam, publisher propagation/restart tests, and RabbitMQ envelope identity.
- `libs/shared/**` (5 files) — strict canonical/legacy booking-cancelled schemas, auto-resolved schedule schema, exports, and tests.
- `db-schema/**` (3 files) — Booking column/backfill/index and Trip/DriverSchedule truth.
- `docs/api/postman/**` (3 files), `docs/handoff/**` (3 files), and `scripts/**` (2 files) — cumulative manual companion, Day-23 runner/self-tests, retained evidence, and approved plan.
- `eslint.config.mjs` — authorized test-file lint override used by the Day-23 focused verification additions.
- Merge reconciliation additionally combines the Day-23 and Day-40 Booking indexes/assertions/Postman variables, deduplicates the shared `FindByIdForUpdateAsync` contract and implementation while retaining both lock-seam behaviors, restores the complete Trip model snapshot, and makes the Day-40 isolated Redis port overridable on Windows-reserved port ranges.
- Remediation changes `TripCancelledIntegrationEventHandlerTests.cs` and `docs/handoff/day-23-plan.md`; the audit writes `docs/handoff/day-23-checklist.md`. Pre-existing unrelated modifications/untracked files shown by `git status` were preserved and not audited as Day-23 delivery.

## Verification run

| Command | Result | Notes |
|---|---|---|
| Task-23.0 exact DOCS block extracted from `docs/handoff/day-23-plan.md` | PASS | The block itself now uses `Get-Content -Encoding utf8`; it passes unchanged on Windows PowerShell `5.1.26100.8875`. |
| `git diff --cached --check` + Postman JSON parse + runner syntax | PASS | The staged merge is whitespace-clean; collection/environment parse; both Day-23 and Day-40 runners pass `node --check`. |
| Full `.NET` environment stabilization | PASS / diagnostic note | The first Shared Persistence run correctly failed because local PostgreSQL was stopped. After the repo infra profile became healthy, the complete matrix was rerun from the first solution and passed; the environment-only failure is not used as release evidence. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` + `dotnet test ... -c Release` | PASS | Format exit `0`; Messaging `10/10`, Persistence `14/14`, Web `88/88`; total `112/112`, skipped `0`. |
| `dotnet build apps/identity/VietRide.Identity.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes` + `dotnet test ... -c Release` | PASS | Format exit `0`; unit `269/269`, integration `153/153`; skipped `0`. |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` + `dotnet test ... -c Release` | PASS | Format exit `0`; unit `486/486`, integration `183/183`; skipped `0`. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | Exit `0`; no format drift. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | Unit `438/438`; integration `134/134`; failed/skipped `0`. The merged row-lock repository seam and remediated concurrent cancellation assertion pass in the full solution. |
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes` + `dotnet test ... -c Release` | PASS | Format exit `0`; unit `105/105`, integration `35/35`; skipped `0`. |
| `dotnet build apps/parcel/VietRide.Parcel.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes` + `dotnet test ... -c Release` | PASS | Format exit `0`; unit `175/175`, integration `24/24`; skipped `0`. |
| Full .NET aggregate | PASS | `2114/2114` passed, `0` failed, `0` skipped across shared libs and five services. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | All 10 TS/NestJS projects and three dependent tasks built successfully. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | Exit `0` for 14 projects; `0` errors and 10 warnings (six pre-existing Day-22 contract-test unused locals, two Notification non-null assertions, two Gateway test helper unused arguments). |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | 10 projects + 3 dependent tasks; `68` suites / `495` tests passed, no failed suite/test. |
| `dotnet ef database update ...` apply → each new migration `Down()` → re-apply | PASS | Identity three, Trip two, Booking three (Day-40 redirects/report index followed by Day-23 projection), and Parcel one migration all applied, rolled back individually, and re-applied cleanly. |
| `dotnet ef migrations has-pending-model-changes ...` (Identity/Trip/Booking/Parcel) | PASS | All four report `No changes have been made to the model since the last migration`; the merge-restored Trip snapshot includes the pre-existing Day-22 audit/fare metadata plus Day-40 station/report metadata. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | All app images were rebuilt from the merged source; all 13 app/infra containers became healthy. Missing Google OAuth client id/secret remains an unrelated dev-only external-credential note. |
| `/health` matrix | PASS | HTTP `200`: Gateway `3000`, Identity `5001`, Trip `5002`, Booking `5003`, Payment `5004`, Parcel `5005`, Tracking `3001`, Notification `3002`, RAG `3003`. |
| Review artifact validation | PASS | Day-23 Postman folder has 17 canonical Gateway requests and no Trip schedule/accept/reject/internal clock/job alias; JSON parses. Tokens are intentionally empty placeholders, so the repository-designated isolated runner owns execution. |
| `node --test --test-reporter=tap scripts/run-day23-schedule-change-local.test.mjs` | PASS | `10/10`; failed/cancelled/skipped/todo all `0`. |
| `node scripts/run-day23-schedule-change-local.mjs --focused` | PASS | Exit `0`; real Gateway producer/projection/resolver/error/refund/Notification/Outbox/RabbitMQ checks passed, including `200`, every ratified `401/403/404/409/422`, 50%/100% frozen refunds, and cleanup. Tokens/secrets were redacted. |
| `DAY40_E2E_REDIS_PORT=55382 npm run e2e:day40` | PASS | Isolated stack, deterministic seed and cleanup; `20/20` scenarios passed, including concurrency, shared idempotency, Station merge/relink consumers, platform reports, outage recovery, PostgreSQL/Redis/RabbitMQ assertions, and EF rollback/re-apply. The override avoids a Windows-reserved port while preserving the CI default. |
| `node scripts/run-day17-newman-local.mjs` (affected earlier cancellation regression) | PASS | Through Gateway: cancel `200`, operator stats `200`, Newman `2/2` requests and `4/4` assertions, fixture cleanup verified. Booking/Gateway were then recreated from normal compose and restored healthy/HTTP `200`. |
| Day-23 timeline Review bullet overall | PASS | Snapshot immutability, one active action, equality eligibility, and absence of timeout refund/cancellation/operator seat assignment all passed static, database, and Gateway checks. |
| Hard invariants | PASS | CPM has no `.csproj` `Version=`; MediatR `11.1.0`; no direct banned dependency declaration; no Day-23 dependency diff; no `Co-Authored-By`; 128 relevant Day-23 text files match `.gitattributes`; `git diff --check 424d4ba` passes; exactly one worktree exists. |

## Contract / event / schema changes shipped

- Reused `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`; no dedicated Trip schedule endpoint or Gateway route was added.
- Added canonical PASSENGER/owner `POST /v1/bookings/{bookingId}/pending-actions/{actionId}/resolve` with UUID-v4 idempotency, exact `{action,note?}` shape, ADR-0004 responses, masking, replay, and the Day-23 `404/409/422` error registry entries.
- Added nullable/backfilled/indexed `bookings.trip_current_departure` through `20260717000000_AddBookingTripCurrentDeparture`; immutable `trip_snapshot_departure` remains historical truth.
- Registered `booking.booking.pending_action_auto_resolved`; preserved `trip.trip.schedule_changed`, informational/required/re-alerted schedule facts, and exact payload/outbox/MessageId identity.
- Extended new `booking.booking.cancelled` producers with required UUID-v4 `eventId` and offset `occurredAt`; shared TS and .NET consumers accept only the strict canonical shape or exact legacy shape with both identity fields absent.
- Added separate `ScheduleChangeAutoAcceptJob`; preserved the Day-22 `PendingActionRealertJob` occurrence `+2h` production behavior unchanged.
- BSOT `1.36.0` contains the Day-23 convention/error/event/job registry updates and §13 merge changelog row while preserving the Day-40 baseline. No dependency, Prisma migration, new notification type, cross-DB FK, or operator seat-assignment contract was introduced.

## Known gaps & carry-over for Day 24

- No Day-23 release blocker remains. The two original blockers plus the post-merge duplicate Booking lock seam and Trip snapshot drift were fixed and verified by targeted gates plus the complete close-out matrix.
- The 10 lint warnings are non-blocking under the current CI command (exit `0`) and none is a production Day-23 semantic failure; clean them separately if the team adopts zero-warning TS lint.
- The audit encountered local Docker resource contention while RabbitMQ was stuck at about `402%` CPU. A correct-env container recreate cleared it, the exact Trip full suite then passed `635/635`, and final stack/health checks remained green. Treat recurrence as a local-runtime diagnostic, not a Day-24 feature dependency.

## Notes for Day 24 planning

- Day 24 may plan from a green Day-23 baseline (`✅ READY`, full close-out matrix passed).
- Preserve the ratified Day-23 producer/resolver shape, current-departure CAS, immutable refund basis, canonical/legacy cancellation compatibility, two independent re-alert identities, and no-timeout-refund rule.
- Keep operator seat assignment outside the passenger `/resolve` endpoint until a later contract explicitly ratifies its route/DTO/handler.
- The pre-existing unrelated dirty/untracked workspace files belong to the human and were not modified, staged, deleted, or included in this audit.
