# Day 13 — Final checklist

> Produced by `/audit-day 13` AFTER all tasks are done and verification ran.
> Honest record: if verification failed but the day was closed, say so. Don't claim green.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 13 (Jira: SCV-84)
- **Plan**: docs/handoff/day-13-plan.md
- **Status**: ✅ READY

## DoD result
- [x] ✅ `POST /v1/bookings/round-trip` creates 2 Booking rows sharing a non-null `bookingGroupId` with `tripDirection=OUTBOUND/RETURN` — `CreateRoundTripBookingCommandHandler.cs` creates the shared group and directions; Booking tests now pass `77/77` unit and `23/23` integration.
- [x] ✅ Round-trip eligibility is enforced: missing return-route config returns `ROUTE_RETURN_NOT_CONFIGURED`; return departure not after outbound arrival returns `BOOKING_ROUND_TRIP_INVALID`.
- [x] ✅ Round-trip WALLET uses one all-or-nothing Payment batch-charge call; VNPay uses `BOOKING_GROUP` only.
- [x] ✅ Round-trip Payment response hardening is fixed: Booking now validates returned Payment `referenceId` set exactly equals `{outboundBooking.Id, returnBooking.Id}`; `dotnet-reviewer` rechecked this as PASS.
- [x] ✅ Round-trip seats use a Trip-owned atomic lock seam — Booking calls one `LockRoundTripSeatsAsync`; Trip exposes `POST /internal/v1/trips/round-trip/lock-seats`; Redis Lua lock is all-or-nothing.
- [x] ✅ `edit-pickup` before cutoff is price-neutral-only: equal fare succeeds; any fare difference returns `BOOKING_EDIT_PICKUP_PRICE_CHANGED`; no payment/refund/event side effects.
- [x] ✅ `edit-dropoff` before cutoff updates the dropoff target and validates route membership / `allowDropoff` / order after pickup; no fare/refund/charge side effects.
- [x] ✅ Exact T-2h cutoff boundary is covered by executed tests: `EditPickupCommandHandlerTests.Handle_AfterCutoff_ThrowsCutoffExceeded` and `EditDropoffCommandHandlerTests.Handle_AfterCutoff_ThrowsCutoffExceeded` set `departureDateTime = Now.AddHours(2)` and assert `BOOKING_CUTOFF_EXCEEDED` with no update.
- [x] ✅ Both edit endpoints require `Idempotency-Key`, are owner-scoped, and are PASSENGER-gated at controller and Gateway; Gateway access-gates tests cover `/v1/bookings/round-trip`, `/edit-pickup`, `/edit-dropoff` subpaths.
- [x] ✅ Static deterministic verification is green for touched .NET solutions: Booking, Payment, Trip, and shared libs build/format/test pass.
- [x] ✅ EF migration verification is green: Trip migration `20260615104826_AddTripsAndTripSeats` applies, rolls back to `20260611044831_AddTripVehiclesAndDriverSchedules`, reapplies, and the temp DB drops successfully.
- [x] ✅ TS verification is green: build/lint/test pass; Postman collection/environment variables are now self-contained; Day-13 Newman runner passes.
- [x] ✅ Real Docker app stack builds/starts with `--profile app --build`; all app + infra containers report healthy; all 9 `/health` endpoints return HTTP 200.
- [x] ✅ Day-13 Review bullet is closed for Day-13 scope: exact T-2h cutoff is executed and PASS; “round-trip cancel one leg doesn't cancel the other” is explicitly deferred to Day 17 because Booking cancellation is Day-17 scope (`POST /bookings/{id}/cancel` in `BE_TIMELINE_VU.md:185-192`) and no cancel endpoint exists in `apps/booking/src` today.

## Tasks completed
- Task 13.0 — Booking domain edit methods + repository find-by-id seam — ✅ implemented and verified.
- Task 13.05 — Payment WALLET batch-charge seam for round-trip atomicity — ✅ implemented and verified.
- Task 13.1 — Round-trip booking command + handler + controller action — ✅ implemented and hardened after audit.
- Task 13.2 — Edit pickup endpoint (price-neutral-only) — ✅ implemented and verified.
- Task 13.3 — Edit dropoff endpoint (no reprice) — ✅ implemented and verified.
- Task 13.4 — Gateway sub-path RBAC test + Postman cumulative artifacts — ✅ implemented; Postman collection/environment fixed after review.
- Follow-up — Trip-owned atomic round-trip seat-lock seam + Trip EF migration — ✅ implemented and rollback-verified.
- Follow-up — Trip integration test DB defaults/override — ✅ fixed; Trip integration tests now pass `19/19`.
- Follow-up — Booking WALLET batch-charge reference-id validation — ✅ fixed; mismatch negative test added.

## Changed files
- `apps/booking/src/VietRide.Booking.Api/Controllers/BookingsController.cs` — round-trip, edit-pickup, and edit-dropoff actions.
- `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/*` — round-trip command/handler/validator/result; Payment `referenceId` exact-set validation.
- `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditPickup/*` — price-neutral pickup edit flow.
- `apps/booking/src/VietRide.Booking.Application/Features/Bookings/EditDropoff/*` — price-neutral dropoff edit flow.
- `apps/booking/src/VietRide.Booking.Domain/Entities/Booking.cs` and repository/service-client seams — edit/group/seat-lock/payment boundaries.
- `apps/booking/tests/VietRide.Booking.{UnitTests,IntegrationTests}/*` — round-trip, edit, cutoff, Payment mismatch, and integration coverage.
- `apps/payment/src/VietRide.Payment.Api/Controllers/InternalPaymentsController.cs` and `BatchChargePayment*` files — internal WALLET batch-charge endpoint.
- `apps/payment/src/VietRide.Payment.Domain/*` and `apps/payment/src/VietRide.Payment.Infrastructure/*` — wallet/payment ledger support for batch-charge.
- `apps/trip/src/VietRide.Trip.*` — Trip/TripSeat model, internal round-trip seat-lock endpoint, Redis lock store, EF mapping/migration.
- `apps/trip/tests/VietRide.Trip.{UnitTests,IntegrationTests}/*` — Trip lock/persistence tests; integration test connection defaults preserve `VIETRIDE_TRIP_TEST_CONNECTION_STRING` override.
- `libs/dotnet/VietRide.Shared.Application/Exceptions/ApplicationExceptions.cs` and `libs/dotnet/VietRide.Shared.Web/Filters/ApiResponseExceptionFilter.cs` — shared error/envelope support touched by Day 13.
- `apps/gateway/src/proxy/proxy.access-gates.spec.ts` — booking subpath PASSENGER RBAC assertions.
- `docs/api/postman/vietride.postman_collection.json` and `docs/api/postman/vietride.local.postman_environment.json` — Day-13 Booking requests and declared variables.
- `scripts/run-day13-newman-local.js` and `package.json` — local Day-13 Newman execution harness.
- `infra/docker/docker-compose.yml` and `apps/notification/Dockerfile` — container/runtime changes present in working tree.
- `docs/handoff/day-13-checklist.md` — this updated audit checklist.

## Verification run
| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | Re-run after fixes: `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | ✅ PASS | Exit 0; no output. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | Unit `77/77`, integration `23/23`, failed 0, skipped 0. |
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | ✅ PASS | Audit matrix run: `0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes` | ✅ PASS | Exit 0. |
| `dotnet test apps/payment/VietRide.Payment.sln -c Release` | ✅ PASS | Unit `6/6`, integration `7/7`, failed 0, skipped 0. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Audit matrix run: `0 Warning(s) 0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | ✅ PASS | Exit 0. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | ✅ PASS | Shared Persistence unit `4/4`, Shared Web unit `72/72`, failed 0, skipped 0. |
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | Re-run after fixes: `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | ✅ PASS | Exit 0; no output. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release` | ✅ PASS | Unit `156/156`, integration `19/19`, failed 0, skipped 0. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` / `npm run build:ts` | ✅ PASS | TS build green; existing non-blocking source-map warnings only. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` / `npm run lint:ts` | ✅ PASS | TS lint green for 14 projects. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` / `npm run test:ts` | ✅ PASS | TS tests green: contracts `27/27`, gateway `78/78`, rag `2/2`, tracking `29/29`, notification `69/69`. |
| Trip migration temp-DB command with `TRIP_DESIGN_CONNECTION=Host=localhost;Port=5432;Database=vietride_trip_migration_check;Username=vietride;Password=vietride_dev` applying `20260615104826_AddTripsAndTripSeats`, rolling back to `20260611044831_AddTripVehiclesAndDriverSchedules`, reapplying, then `dotnet ef database drop --force` | ✅ PASS | Apply succeeded; rollback succeeded; reapply succeeded; temp DB dropped. EF emitted existing design-time warning about `INTERNAL_JWT_SECRET` and enum sentinel warnings, but completed successfully. |
| `node -e "JSON.parse(...collection...); JSON.parse(...environment...)"` | ✅ PASS | Postman JSON parses. |
| Postman variable declaration check (`POSTMAN_ENV_REFS_OK`) | ✅ PASS | `POSTMAN_ENV_REFS_OK 375`; all collection refs are declared by collection/environment, excluding dynamic `{{$guid}}`. |
| `node --check scripts/run-day13-newman-local.js` | ✅ PASS | Syntax OK. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | ✅ PASS | Full app stack rebuilt after fixes. Compose warns Google OAuth env vars are blank; not relevant to Day-13 flow. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | ✅ PASS | Gateway, Identity, Trip, Booking, Payment, Parcel, Tracking, Notification, RAG, Postgres, RabbitMQ, Redis, PgBouncer all `Up ... (healthy)`. |
| `/health` matrix (`http://localhost:3000/health`, `:5001`, `:5002`, `:5003`, `:5004`, `:5005`, `:3001`, `:3002`, `:3003`) | ✅ PASS | All 9 endpoints returned HTTP `200` after rebuild. |
| `npm run postman:day13:local` | ✅ PASS | Real Gateway E2E against `:3000`: 5 requests, 5 test scripts, 10 assertions, 0 failed. Statuses: create booking `201`, create round-trip `201`, edit-pickup `200`, edit-dropoff `200`, max seats exceeded `422`. Tokens redacted. |
| Day-13 Review artifact validation — Postman collection/environment | ✅ PASS | Reviewer recheck PASS: collection top-level variables include Day-13 IDs; environment includes `createdBookingCode`; runner assumptions explicit enough. |
| Day-13 Review execution — exact T-2h cutoff | ✅ PASS | Executed via Booking unit tests in `dotnet test apps/booking/VietRide.Booking.sln -c Release`: both pickup/dropoff exact boundary tests pass and assert `BOOKING_CUTOFF_EXCEEDED`. |
| Day-13 Review execution — round-trip cancel one leg doesn't cancel the other | ✅ DEFERRED | Not a Day-13 blocker after BE-lead scope decision: Booking cancel endpoint is absent in `apps/booking/src`; grep found only future references, and Day 17 owns `POST /bookings/{id}/cancel` in the timeline. Carry-over recorded for Day-17 planning/audit. |
| `git diff --check` | ✅ PASS | No whitespace errors after final patches. |
| `git grep -n 'PackageReference.*Version=' -- '*.csproj' 'Directory.Packages.props'` | ✅ PASS | No output; no `.csproj` `PackageReference Version=` violations. |
| Banned deps / MediatR check | ✅ PASS | No AutoMapper/OpenTelemetry/Prometheus/Grafana/Tempo/Loki additions; MediatR remains v11.x via CPM. |
| `git log --format='%B' -n 20 | findstr /C:"Co-Authored-By"` | ✅ PASS | No output; no recent `Co-Authored-By` trailer. |
| EOL check (`git ls-files --eol`) | ✅ PASS | Policy observed for touched file classes: `.cs/.csproj/.sln` CRLF; `.ts/.json/.md/.yml/.yaml/.js` LF. |
| `dotnet-reviewer` read-only fix review | ✅ PASS | Booking Payment `referenceId` validation and Trip migration rollback PASS. Initial env override nit fixed and quick reviewer recheck PASS. |
| `nest-reviewer` / project reviewer read-only fix review | ✅ PASS | Postman collection/environment final recheck PASS. |

## Contract / event / schema changes shipped
- Public Booking endpoints shipped under existing Gateway `/v1/bookings` PASSENGER prefix:
  - `POST /v1/bookings/round-trip`
  - `POST /v1/bookings/{bookingId}/edit-pickup`
  - `POST /v1/bookings/{bookingId}/edit-dropoff`
- Internal Payment endpoint shipped: `POST /internal/v1/payments/batch-charge` (Internal JWT + Idempotency-Key; raw success DTO, ADR 0004 error envelope).
- Internal Trip endpoint shipped: `POST /internal/v1/trips/round-trip/lock-seats` (Internal JWT + Idempotency-Key; Redis Lua all-or-nothing seat lock).
- Events: no new edit event; round-trip reuses existing `booking.booking.confirmed` per leg.
- Error codes: no new unregistered Day-13 code observed; existing registry covers `BOOKING_ROUND_TRIP_INVALID`, `ROUTE_RETURN_NOT_CONFIGURED`, `BOOKING_CUTOFF_EXCEEDED`, `BOOKING_EDIT_PICKUP_PRICE_CHANGED`, `STOP_NOT_FOUND`, `STOP_NOT_DROPOFF_ALLOWED`.
- Schema: Trip EF migration `20260615104826_AddTripsAndTripSeats` shipped and rollback/reapply verified on a temp DB.
- BSOT/API registry updates for Payment batch-charge and Trip round-trip lock are present in the branch (`BACKEND_SOURCE_OF_TRUTH.md` changelog includes v1.11.1/v1.12.0; API contract includes internal seams).

## Known gaps & carry-over for Day 14/17
- **Day-17 carry-over (not a Day-13 blocker)**: execute “round-trip cancel one leg doesn't cancel the other” after `POST /bookings/{id}/cancel` exists. Current Day-13 code has no cancel endpoint; `IBookingService` comments also reference future `CancelBookingCommandHandler` on Day 17.
- **E2E limitation note (accepted for Day 13)**: `scripts/run-day13-newman-local.js` proves Day-13 Booking routes execute through Gateway using deterministic PASSENGER JWT and DevTripServiceClient IDs; it is not a fully seeded production-like Trip/Payment data-flow harness.
- Outbox publisher wiring remains a pre-existing carry-over and was not Day-13 scope.

## Notes for next planning step
- Day-13 technical blockers found by audit were fixed and verified; Day 13 can close as READY.
- Day-17 planning/audit must include the cancellation independence check when the cancel endpoint lands.
