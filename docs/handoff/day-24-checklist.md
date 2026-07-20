# Day 24 — Final checklist

> Produced by `/audit-day 24` after the remediation pass. The full applicable matrix was rerun independently; task tracker status was not used as audit evidence.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 24 (Stop disable + No-show)
- **Plan**: `docs/handoff/day-24-plan.md`
- **Audited scope**: `61b39f55..e81ba1c8` plus the current uncommitted Day-24 remediation diff
- **Status**: ✅ READY

## DoD result

- [x] ✅ Stop disable creates at most one active `STOP_DISABLED` action for eligible confirmed bookings, preserves the ratified deadline formula, and publishes with stable Outbox identity. Evidence: SOT gate 20/20, fallback fixture 5/5, live DELETE 200/replay 200/mismatch 422/terminal 409, and published `trip.stop.disabled` Outbox identity 1/1.
- [x] ✅ Passenger replacement, terminal fallback conflict, and `STOP_DISABLED_REFUSED` cancellation work with ownership, deadline, canonical-station, idempotency, and 100% refund semantics. Evidence: live replacement 200, byte-identical replay 200, changed-body mismatch 422, resolved-action conflict 409, cancellation 200, and persisted state `fallbackStation|ACCEPTED|CANCELLED|refund_override=true|REJECTED`.
- [x] ✅ Unresolved actions deterministically auto-fallback only after the strict deadline. `Day24StopDisabledAutoFallbackIntegrationTests` passed 5/5, including equality untouched and direct post-boundary execution.
- [x] ✅ No-show uses strict actual-arrival/actual-departure anchors, excludes equality, fails closed, and produces the ratified booking states. `Day24NoShowDetectionIntegrationTests` passed 5/5, including all-pending, mixed 3/5 `PARTIAL_NO_SHOW`, and all-boarded 5/5 unchanged.
- [x] ✅ Driver departure persists the durable timestamp and emits only for a positive pending count. Live depart returned 200, replayed byte-identically, rejected mismatch/state/auth/invalid-id cases with the ratified statuses, published one stable-identity `trip.stop.departed_with_pending` Outbox row, and produced exactly one assigned-driver notification through RabbitMQ.
- [x] ✅ Contracts, consumers, registries, and schema artifacts match the ratified Day-24 truth. The executable SOT gate verified all five canonical documents; the full TS suite and both migration lifecycles passed.

## Tasks completed

- Task 24.0 — contract, error/event/job registries and reconciliation — ✅.
- Task 24.0a — TripStop actual-departure EF migration — ✅ apply/down/reapply.
- Task 24.0b — Notification driver-warning enum migration — ✅ fresh deploy/custom down/reapply/model diff.
- Tasks 24.1–24.2 — stop disable producer and Booking action consumer — ✅.
- Tasks 24.3–24.4 — passenger choices and deterministic fallback — ✅.
- Tasks 24.5–24.6 — operational timing seam and no-show state machine — ✅.
- Tasks 24.7–24.8 — raw pending count and durable stop departure — ✅.
- Task 24.9 — strict shared contracts and Notification consumers — ✅.
- Task 24.10 — deterministic focused evidence and live authenticated boundary matrix — ✅.

## Remediation delivered

- Added the shared station canonicalizer to the fallback writer and its architecture assertions.
- Removed the nested fallback transaction; `TransactionBehavior` is again the sole transaction/save boundary.
- Restored ordinary edit-pickup/edit-dropoff integration DI isolation.
- Updated the frozen `MARK_NO_SHOW` history-source set.
- Removed EF test service-provider churn that broke full Booking/Trip integration runs.
- Normalized .NET format/EOL/EOF hygiene.
- Added `scripts/run-day24-newman-local.mjs`: short-lived JWT minting, exact owned fixture seed/cleanup, 19-request Newman execution, and DB/Outbox/RabbitMQ/Notification side-effect assertions without committed credentials.

## Changed files in remediation

- `apps/booking/src/VietRide.Booking.Application/Features/Bookings/AcceptStopDisabledFallback/AcceptStopDisabledFallbackCommandHandler.cs`
- `apps/booking/tests/VietRide.Booking.IntegrationTests/{EditPickupIntegrationTests.cs,EditDropoffIntegrationTests.cs,StationWritingArchitectureTests.cs}`
- `apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day24NoShowDetectionRaceIntegrationTests.cs`
- `apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/TripScheduleChangedIntegrationEventHandlerTests.cs`
- `apps/booking/tests/VietRide.Booking.IntegrationTests/StopDisabled/Day24StopDisabledResolutionTransactionTests.cs`
- `apps/booking/tests/VietRide.Booking.IntegrationTests/TripCompletedIntegrationEventTests.cs`
- `apps/booking/tests/VietRide.Booking.UnitTests/Domain/BookingStatusHistoryTests.cs`
- `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/Day24StopDisabledPassengerChoiceTests.cs`
- `apps/trip/tests/VietRide.Trip.IntegrationTests/{Stops/Day24StopDisableProducerIntegrationTests.cs,Trips/Operations/Day24DepartStopWarningIntegrationTests.cs}`
- `tests/dotnet/VietRide.Shared.Persistence.UnitTests/Outbox/Day24OutboxRestartIdentityTests.cs`
- `scripts/run-day24-newman-local.mjs`
- `docs/handoff/day-24-checklist.md`

Unrelated pre-existing working-tree changes under `.agents/`, `.codex/`, `.nx/`, and workflow/handoff documentation were preserved and excluded from this remediation.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `node --test scripts/verify-day24-sot.test.mjs` | ✅ PASS | 20 passed; 0 failed/cancelled/skipped/todo. |
| `node scripts/verify-day24-sot.mjs` | ✅ PASS | Verified 5 canonical contract files and 37 changed paths. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | 0 warnings, 0 errors. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | ✅ PASS | No changes required. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release --no-build` | ✅ PASS | Unit 459/459; integration 159/159; 0 skipped. |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | 0 warnings, 0 errors. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | No changes required. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release --no-build` | ✅ PASS | Unit 524/524; integration 199/199; 0 skipped. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | 0 warnings, 0 errors. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | No changes required. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-build` | ✅ PASS | Messaging 20/20; Persistence 24/24; Web 90/90. |
| Filtered `Day24StopDisabledAutoFallbackIntegrationTests` | ✅ PASS | 5/5, frozen clock, isolated DB, direct job invocation. |
| Filtered `Day24NoShowDetectionIntegrationTests` | ✅ PASS | 5/5, including equality, 3/5, and 5/5 cases. |
| `node --test scripts/day24-stop-noshow-e2e.test.mjs` | ✅ PASS | TAP 5/5; 0 failed/cancelled/skipped/todo. |
| `node scripts/day24-stop-noshow-e2e.mjs --focused-integration` with required Day-24 env | ✅ PASS | Named fixtures, 19-request artifact matrix, and bounded 2-second observation gate passed. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | ✅ PASS | 10 TS projects plus 3 dependencies; only known non-fatal source-map warnings. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | ✅ PASS | All 14 applicable projects completed with no lint errors. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | ✅ PASS | All 10 TS projects plus 3 dependencies passed. |
| Trip EF latest → `20260716194532_AddCompletedTripReportIndex` → latest | ✅ PASS | `20260718090000_AddTripStopActualDepartureTime` reverted and reapplied cleanly. |
| Notification Prisma scratch lifecycle | ✅ PASS | Fresh 10-migration deploy; enum 1 → custom Down 0 → reapply 1; legacy fixture preserved; Prisma diff empty; scratch DB dropped. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | ✅ PASS | All app images rebuilt from the audited code. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | 9 apps plus Postgres, Redis, RabbitMQ, and PgBouncer healthy. |
| `/health` matrix | ✅ PASS | Ports 3000, 5001–5005, and 3001–3003 all returned HTTP 200. |
| `node scripts/run-day24-newman-local.mjs` | ✅ PASS | 19 requests, 19 assertions, 0 failures; exact 200/401/409/422 matrix and byte-identical replay checks. |
| Live side-effect assertions | ✅ PASS | Booking/action state exact; Trip Outbox published identity `1|1`; assigned-driver notification `1`; fixture cleanup `0|0|0|0`. |
| Day-24 Review bullet overall | ✅ PASS | Equality, replay/mismatch, race/restart, `NO_SHOW`, mixed 3/5, and all-boarded 5/5 executed across focused and live evidence. |
| `git diff --check` | ✅ PASS | No whitespace/EOF errors. |
| `git ls-files --eol` expected-EOL scan | ✅ PASS | No tracked file violates `.gitattributes`. |
| CPM / banned dependency / MediatR scan | ✅ PASS | No `PackageReference Version=`, no banned dependency, MediatR 11.1.0. |
| Day-24 trailer scan and `git worktree list` | ✅ PASS | No `Co-Authored-By`; exactly one working tree. |

## Contract / event / schema changes shipped

- Public APIs: stop disable DELETE, terminal fallback accept, and assigned-crew stop depart.
- Internal APIs: additive Trip actual-departure snapshot and exact Booking pending-passenger count.
- Events: `trip.stop.disabled`, `booking.stop_disabled.affected`, `booking.booking.stop_disabled_auto_fallback_applied`, `booking.booking.passenger_no_show_marked`, and frozen `trip.stop.departed_with_pending`.
- Errors: `STOP_ALREADY_DISABLED`, `TRIP_STOP_NOT_ARRIVED`, `TRIP_STOP_ALREADY_DEPARTED`, and `UPSTREAM_UNAVAILABLE` are registered with ratified statuses.
- Schema: `trip_stops.actual_departure_time` and NotificationType `DRIVER_STOP_DEPARTED_WITH_PENDING`.
- BSOT registry and §13 changelog: ✅ updated; executable SOT gate confirms Day-24 version `1.37.0`.

## Known gaps & carry-over for Day 25

- None for Day 24.
- The remediation and checklist are intentionally uncommitted; normal review/stage/commit is the next repository action.

## Notes for Day 25 planning

Day 24 is closed and may be treated as a green dependency. Preserve the new live runner as the repeatable boundary check; it owns only fixed Day-24 UUIDs, commits no credentials, asserts messaging side effects, and removes its exact fixture graph after every run.
