# Day 12 — Final checklist

> Produced by `/audit-day 12` after all tasks were delivered and the verification matrix was rerun.
> Audit date: 2026-06-11.

- **Timeline ref**: `BE_TIMELINE_VU.md` Day 12 (SCV-82) — Booking Service: Seat lock + Booking entity
- **Plan**: `docs/handoff/day-12-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] ✅ Booking architecture baseline is present. `Program.cs` wires shared web, the Booking DbContext, MediatR behaviors, infrastructure, and idempotency; Booking tests include NetArchTest layering coverage.
- [x] ✅ Booking-owned schema is implemented. `20260611043137_InitBookingSchema` creates `bookings`, `passengers`, `booking_pending_actions`, and `outbox_events` with the required checks, unique indexes, partial index, and max-5 passenger trigger. The current DB contains the four native enum types and `trg_passengers_max_5_per_booking`.
- [x] ✅ Migration history remains reversible. `20260611124031_NativeBookingEnumMappings` applied, rolled back to `20260611043137_InitBookingSchema`, and re-applied successfully.
- [x] ✅ Booking aggregate, Passenger sub-entity, and BookingCode value object match the selected SOT. The live happy path produced `VR-20260611-9FE1E829`; Passenger persisted only operational fields, with no request PII columns.
- [x] ✅ `POST /v1/bookings` is contract-correct for the audited paths. The authenticated WALLET happy path returned `201 CONFIRMED`, persisted the booking/passenger, and enqueued `booking.booking.confirmed`. A request with six seats returned HTTP `422` with `error.code=BOOKING_MAX_SEATS_EXCEEDED`.
- [x] ✅ PASSENGER authorization is enforced at both layers: `[Authorize(Roles = "PASSENGER")]` in `BookingsController.cs:41` and Gateway `requiredRoles: ['PASSENGER']` in `routes.ts:160`. Integration/Gateway tests cover non-passenger rejection.
- [x] ✅ Release compensation is centralized in `BookingService.ReleaseSeatsAsync`; the handler does not inline the Trip release call.
- [x] ✅ Mocked seat-lock behavior is proven: unit test `Handle_ConcurrentSameSeat_OneWins_OneSeatUnavailable_AndCreatesOneBooking` races two attempts and asserts one confirmed booking and one `BOOKING_SEAT_UNAVAILABLE`.
- [x] ✅ Day-12 runtime seams are explicit development-only stubs. Docker enables Trip and Payment stubs for the runnable Day-12 flow; default appsettings keep both stubs disabled.
- [x] ✅ Static verification is green: Booking build/format/test, shared libs build/format/test, and the complete TS build/lint/test suite passed.
- [x] ✅ Swagger exposes `POST /v1/bookings`; the cumulative Postman artifact parses and contains the happy/max-seat requests.
- [x] ✅ Day-12 Review execution is green for the Booking-owned scope approved in plan decision D1. The Gateway happy path and max-seat adversarial path passed; mocked concurrency proves one same-seat winner. The Trip-owned real Redis 50-way race and 10-minute TTL release remain the explicitly deferred dependency CO1.

## Tasks completed

- Task 12.0 — Booking architecture baseline — ✅
- Task 12.1 — Booking EF migration — ✅
- Task 12.2 — Trip inter-service client seam — ✅
- Task 12.3 — Create-booking saga core — ✅

## Changed files

- `apps/booking/src/**` — Booking baseline, aggregate, EF mappings/migrations, Trip/Payment clients and development stubs, repository/service, create-booking handler/controller.
- `apps/booking/tests/**` — architecture, domain, HTTP client, handler, concurrency, role, and integration coverage.
- `libs/dotnet/VietRide.Shared.Application/Behaviors/ValidationBehavior.cs` — preserves an explicit canonical FluentValidation error code when all failures use the same `UPPER_SNAKE_CASE` code; mixed/default validation remains `VALIDATION_ERROR`.
- `apps/gateway/src/config/routes.ts` and Gateway tests — PASSENGER role gate for `/v1/bookings`.
- `apps/{gateway,tracking,notification,rag}/Dockerfile` — corrected Nest shared-library image packaging/build inputs.
- `infra/docker/docker-compose.yml` — Booking Redis/service URLs and explicit Day-12 Trip/Payment development stubs.
- `docs/api/postman/vietride.postman_collection.json` — Day-12 Booking requests.

## Verification run

| Command / check | Result | Notes |
|---|---|---|
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | Exit 0, no changes required. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | Unit `43/43`; integration `7/7`; 0 failed/skipped. Includes exact HTTP assertion for `BOOKING_MAX_SEATS_EXCEEDED`. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | `0 Warning(s), 0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | PASS | Exit 0. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-build` | PASS | Shared Web `71/71`; Shared Persistence `4/4`. |
| Shared validation regression: Identity | PASS | Build `0W/0E`; unit `200/200`; integration `127/127`. |
| Shared validation regression: Trip | PASS | Build `0W/0E`; unit `106/106`; integration `16/16` with `VIETRIDE_TRIP_TEST_CONNECTION_STRING` set to the local Docker credential. |
| Shared validation regression: Payment | PASS | Build `0W/0E`; unit `1/1`; integration `2/2`. |
| Shared validation regression: Parcel | PASS | Build `0W/0E`; unit `1/1`; integration `2/2`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects + dependency task succeeded; Tracking emitted one existing missing-source-map warning. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 projects succeeded. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | Contracts `27`, Notification `7`, RAG `2`, Gateway `69`, Tracking `29`; all green. |
| `dotnet ef database update` (Booking) | PASS | DB already at `NativeBookingEnumMappings`. |
| `dotnet ef database update 20260611043137_InitBookingSchema` | PASS | Reverted `NativeBookingEnumMappings`. |
| `dotnet ef database update` (Booking re-apply) | PASS | Re-applied `NativeBookingEnumMappings`. |
| Booking schema inspection | PASS | 22 native enum labels present; max-5 passenger trigger present. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | All nine app images built; full app profile started. |
| `docker ps ...` | PASS | All app and infra containers healthy. |
| `/health` matrix | PASS | Gateway 3000; Identity 5001; Trip 5002; Booking 5003; Payment 5004; Parcel 5005; Tracking 3001; Notification 3002; RAG 3003 all returned HTTP 200. |
| Postman collection/environment JSON parse | PASS | Both artifacts parse; Booking folder contains happy and max-seat requests. |
| Identity auth setup through Gateway | PASS | Register `201` → OTP from dev DB → verify `200` → login `200`; PASSENGER token used for Booking run. Token/OTP redacted. |
| Newman `Booking — Bookings` folder | PASS | Happy path `201 CONFIRMED`; six-seat case `422 BOOKING_MAX_SEATS_EXCEEDED`; `4/4` assertions passed. |
| Happy/adversarial DB side effects | PASS | Happy booking `15257168-8430-4654-bc2d-c9b0e56d3048` persisted `CONFIRMED`; the six-seat request created no additional booking. |
| Outbox/RabbitMQ observation | PARTIAL | `booking.booking.confirmed` exists with `PENDING`, retry 0. Day-12 acceptance requires transactional enqueue, which passed; publisher wiring/publish was not part of the Day-12 write set and no RabbitMQ delivery was claimed. |
| Swagger `/swagger/v1/swagger.json` | PASS | HTTP 200 and contains `/v1/bookings`. |
| CPM / banned dependencies / MediatR | PASS | No `Version=` on `.csproj` PackageReference; no banned dependency; MediatR `11.1.0`. |
| Commit trailer invariant | PASS | No `Co-Authored-By` in the last 20 commits. |
| `git ls-files --eol` + new Day-12 `.cs` inspection | PASS | Tracked EOL rules clean; new C# and migration files are CRLF. |
| `git diff --check` | PASS | No whitespace errors. |
| Day-12 Review overall | PASS | Booking-owned happy/adversarial paths and mocked same-seat concurrency passed. Real Trip Redis stress remains deferred per D1/CO1. |

## Contract / event / schema changes shipped

- **REST**: `POST /v1/bookings`, PASSENGER-only, Idempotency-Key required.
- **Internal clients consumed**: Trip snapshot/lock/book/release seams and Payment charge seam.
- **Schema**: Booking migration chain creates the four Day-12 Booking-owned tables, enum types, checks/indexes, and max-passenger trigger.
- **Event**: `booking.booking.confirmed` is enqueued with the registered routing key; no new event registry entry is needed.
- **Errors**: existing registered codes are reused. The validator pipeline now preserves explicit canonical codes and returns `BOOKING_MAX_SEATS_EXCEEDED` for the six-seat rule.
- **BSOT reconciliation still open**: stale payment seam wording and short BookingCode examples identified in Day-12 plan C1/C2 still require a BSOT patch/changelog entry.

## Known gaps & carry-over for Day 13

- **Forward dependency CO1** — when Trip implements the real seat seam, run the 50-concurrent one-seat stress test and verify Redis TTL auto-release after 10 minutes.
- **Forward dependency CO2** — reconcile the Trip-owned Redis key prefix before the real lock implementation.
- **SOT documentation** — reconcile BSOT payment charge path and BookingCode examples per plan C1/C2.
- **Outbox runtime note** — the event is transactionally enqueued but remains `PENDING`; verify publisher wiring in the day that owns Booking message publication before claiming RabbitMQ delivery.

## Notes for Day 13 planning

- Day 12 is ready to close. The max-seat canonical code is covered by both HTTP integration and Newman execution.
- The prior blockers for PASSENGER RBAC, development payment/trip seams, mocked concurrent one-wins proof, Nest container packaging, Nx availability, EOL, and max-seat error mapping are closed.
- Day 13 may build on this Booking foundation while preserving CO1/CO2 as Trip-owned forward dependencies.
