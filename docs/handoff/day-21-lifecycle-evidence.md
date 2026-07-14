# Day 21 lifecycle verification evidence

> Executed locally on 2026-07-14 against the Docker application profile. No production data,
> committed credential, clock override, lifecycle job-control endpoint, or cross-service database
> write was used.

## Day 21 result

The isolated Day-21 lifecycle path passed end to end through Gateway, Trip Outbox, RabbitMQ,
Parcel, and Booking. The harness generated runtime-only access tokens and UUID-v4 idempotency
keys, redacted credentials, bounded every asynchronous poll, and removed its fixtures in `finally`.
The reviewer-fix re-run passed from a clean Day-21 fixture state; a separate forced-failure run
exited non-zero as intended and independently passed the same DB plus Redis cleanup assertion.

| Verification | Result | Evidence |
|---|---|---|
| `npm run postman:day21:local` | PASS | Exit `0`; a unique Route, Vehicle, VehicleType, origin/destination Stations, assigned `BOARDING` Trip, four Booking status fixtures, and one `LOADED` Parcel were created without borrowing pre-existing fixture data. |
| Start authorization and validation | PASS | Assistant and unassigned driver denied `403 FORBIDDEN`; malformed key denied `422 VALIDATION_ERROR`; assigned driver start returned `200 IN_PROGRESS`. |
| Idempotency | PASS | Same-key start/complete replay returned the original `200`; subject/path fingerprint mismatch returned `422 IDEMPOTENCY_KEY_MISMATCH`; a fresh key after each completed transition returned `409 TRIP_INVALID_TRANSITION`. |
| Started event and Parcel consumer | PASS | `trip.trip.started` Outbox row reached `PUBLISHED`; matching Parcel changed `LOADED -> IN_TRANSIT`. |
| Completion, audit, and Booking consumer | PASS | Assigned assistant completion returned `200 COMPLETED`; exactly one `TRIP_COMPLETED_MANUAL` audit row was written; `trip.trip.completed` reached `PUBLISHED`; only `CONFIRMED` and `PARTIAL_NO_SHOW` Bookings changed to `COMPLETED`, with two `COMPLETE_ON_TRIP_COMPLETED` history rows; `NO_SHOW` and `CANCELLED` remained unchanged. |
| Duplicate delivery | PASS | A duplicate `trip.trip.completed` message was published through the RabbitMQ management HTTP API and confirmed with `routed=true`; every bound queue reported an acknowledgement increase and drained to zero ready/unacknowledged messages before Trip audit, Booking history, and completed state were asserted unchanged. |
| Success-path cleanup | PASS | The runner removed its Trip-schema dependency graph, Trip/Parcel/Booking rows, Outbox/audit/history rows, and every exact `trip:idem:<uuid>` key, then reported `PASS | Day-21 fixture cleanup verified`. |
| Forced failure cleanup | PASS | With an intentionally unreachable Gateway base URL, the runner exited `1` and verified the same DB and Redis cleanup path. |

Deterministic in-flight pending behavior remains covered by Task 21.1's controlled Redis-backed
middleware integration test. Lifecycle fallback boundaries remain covered by Task 21.4's
fake-clock integration tests. The live E2E did not introduce timing races or test backdoors.

## Smoke-test matrix

| Check | Result |
|---|---|
| Gateway `:3000/health` and Gateway proxy routes for Identity, Trip, Booking, Payment, and Parcel | PASS — all HTTP `200` |
| Direct Identity, Trip, Booking, Payment, Parcel, Tracking, Notification, and RAG health endpoints | PASS — all HTTP `200` |
| Tampered `X-Internal-Auth` JWT against an Identity internal endpoint | PASS — HTTP `401` |
| RabbitMQ management endpoint | PASS — reachable |
| RabbitMQ exchange | PASS — `vietride.events` exists as `topic` |
| Lifecycle bindings | PASS — `trip.trip.started -> parcel.trip-started`; `trip.trip.completed -> booking.trip-completed` and `parcel.trip-completed` |

## Fresh build, format, and test matrix

| Solution | Build | Format | Tests |
|---|---|---|---|
| `libs/dotnet/VietRide.Libs.sln` | PASS — 0 warnings, 0 errors | PASS | Messaging `4/4`, Persistence `4/4`, Web `73/73` |
| `apps/trip/VietRide.Trip.sln` | PASS — 0 warnings, 0 errors | PASS | Unit `256/256`, integration `115/115` |
| `apps/parcel/VietRide.Parcel.sln` | PASS — 0 warnings, 0 errors | PASS | Unit `156/156`, integration `19/19` |
| `apps/booking/VietRide.Booking.sln` | PASS — 0 warnings, 0 errors | PASS | Unit `338/338`, integration `50/50` |

`node --check` passed for both changed runners, both Postman JSON artifacts parsed successfully,
`git diff --check` passed, and the cumulative-runner ordering check confirmed `D21` remains after
`D18-crossday`.

## Cumulative Day-20 regression entry point

`scripts/run-full-e2e-local.mjs` now invokes `D21` last, after the existing D11-D19 and
`D18-crossday` sequence, and retains the existing missing-stage/unapproved-skip rejection logic.
Two fresh cumulative executions both reached this final `D21` stage; `D21` passed with cleanup in
both runs.

The cumulative command itself did not finish fully green because of observed legacy-stage
failures outside this task's write set (the Day-20 checklist had previously recorded its own
entry point as green):

- A plain `npm run postman:full:local` run finished `13/15`; D15 lacked the host-process
  `VNPAY_HASH_SECRET` and D16 consequently failed, while their cleanup/mode-restore paths ran.
- A rerun with the existing `.env` loaded in-memory finished `12/15`; D15's legacy invalid-signature
  assertion expected `401` but received `200`, D16 consequently failed, and a later D18-crossday
  child Node process exited `3221226505`. The earlier plain run had passed D18-crossday.

No Payment/VNPay or Day-18 production/harness file was changed because those failures are outside
Task 21.7. The Day-21 stage itself, its final ordering, and its fixture cleanup are reproducibly
green.

## Runtime cleanup

The verification reused the pre-existing Postgres and Redis containers and their volumes. The
existing cumulative runner intentionally restarts (and does not delete or reset) that same
Postgres container to reset connections between stub and real stages; this explains its shortened
uptime while preserving container identity, volume identity, and data. All other
application-profile containers started for this verification were stopped afterward; Postgres
and Redis were left running. No volume was deleted or reset.
