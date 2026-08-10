# Day 18 — Final checklist

> Produced by `/audit-day 18` after independent source-of-truth review and verification on 2026-07-01.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 18 — DriverSchedule + Manifest + Boarding APIs (Jira: SCV-92)
- **Plan**: docs/handoff/day-18-plan.md
- **Status**: ⚠️ CLOSED-WITH-GAPS

## DoD result

- ✅ `GET /v1/driver/me/schedule` derives the caller from JWT `sub`, returns only trips assigned as driver/assistant, and applies inclusive Asia/Ho_Chi_Minh date bounds (default today through today + 14 days). Trip unit tests and the real Gateway flow passed.
- ✅ `GET /v1/bookings/trips/{tripId}/manifest` authorizes assigned crew, sorts terminal pickup first then route stops by `OrderIndex`, and exposes only `seatNumber`, `bookingCode`, `pickupStop`, and `boardingStatus`. The real Gateway response passed the no-PII assertion.
- ✅ `POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}` persists `PENDING → BOARDED` with `boardedAt`; re-tick returns `409 BOOKING_PASSENGER_ALREADY_BOARDED`; wrong-trip passenger returns `422 BOOKING_NOT_FOR_THIS_TRIP`.
- ✅ `POST /v1/bookings/trips/{tripId}/boarding/qr-scan` returns passenger records for a confirmed booking without mutating them; wrong-trip returns `422 BOOKING_NOT_FOR_THIS_TRIP`; unknown code returns `404 BOOKING_NOT_FOUND`.
- ❌ The literal Day-18 warning behavior is not implemented: when leaving a stop with PENDING passengers, no Trip handler currently emits a warning event. BSOT `1.19.0` intentionally registers `trip.stop.departed_with_pending` contract-only and defers the emitter/NO_SHOW wiring to Day 24.
- ✅ All four public endpoints and the internal TripSnapshot extension are documented; Gateway routes enforce DRIVER/ASSISTANT; BSOT §7 and §13 contain the frozen warning-event contract.
- ✅ Day-18 Review executed against the fresh Docker stack: wrong-trip QR returned 422 and manifest contained operational fields only, with no passenger PII.

## Tasks completed

- Task 18.0 — Expose driver/assistant on internal Trip snapshot — ✅
- Task 18.1 — Assigned driver/assistant schedule — ✅
- Task 18.2 — PII-free trip manifest — ✅
- Task 18.3 — Tick one passenger BOARDED — ✅
- Task 18.4 — Resolve booking QR to passenger records — ✅
- Task 18.5 — Register boarding-warning event contract — ⚠️ contract complete; runtime emitter deferred to Day 24
- Task 18.6 — Gateway role gates, routes, Postman, and docs — ✅

## Changed files

- `BACKEND_SOURCE_OF_TRUTH.md` — broadened the reused booking error note; registered `trip.stop.departed_with_pending`; added changelog rows `1.18.0` and `1.19.0`.
- `VietRide_API_Contract_v1.md` — documented internal snapshot crew fields, four Day-18 endpoints, and warning-event payload.
- `apps/trip/src/**`, `apps/trip/tests/**` — internal snapshot crew fields and driver/assistant schedule endpoint/tests.
- `apps/booking/src/**`, `apps/booking/tests/**` — manifest, passenger tick, QR scan, controller, persistence query, and tests.
- `apps/gateway/src/config/routes.ts`, `routes.spec.ts` — DRIVER/ASSISTANT route gates and Booking operational prefix.
- `docs/api/postman/**`, `scripts/run-day18-newman-local.mjs` — Day-18 collection plus reproducible local fixture/JWT/DB verification harness.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/trip/VietRide.Trip.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes` | PASS | Exit 0; no formatting changes. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release --no-build` | PASS | Unit 205/205; integration 56/56; NetArchTest included. |
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | PASS | Exit 0; no formatting changes. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release --no-build` | PASS | Unit 221/221; integration 41/41; NetArchTest included. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects plus dependencies; non-fatal generated/dependency source-map warnings. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 projects. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | All configured targets passed; notable totals: Gateway 102, RAG 72, Tracking 44, Notification 86, Contracts 33, RabbitMQ 4. Nx marked RAG flaky but the executed run passed. |
| EF migration apply/down/re-apply | SKIP | Day 18 added, edited, squashed, or reordered no migration. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | All nine app images built; 13 app/infra containers started healthy. Google OAuth variables were unset, an inherent external-credential environment note unrelated to Day-18 flow. |
| `/health` matrix | PASS | Gateway 3000; Identity 5001; Trip 5002; Booking 5003; Payment 5004; Parcel 5005; Tracking 3001; Notification 3002; RAG 3003 all returned HTTP 200. |
| Postman collection/environment parse and Day-18 artifact inspection | PASS | Collection includes schedule, RBAC, manifest no-PII, tick/re-tick, wrong-trip, QR, and unknown-code assertions. |
| `npm run postman:day18:local` against `http://localhost:3000` | PASS | 11/11 requests, 22/22 assertions. Observed: schedule 200; role gates 403; manifest 200/no PII; tick 200; re-tick 409; wrong-trip tick 422; QR 200; wrong-trip QR 422; unknown code 404. DB confirmed `boardingStatus=BOARDED`, `boardedAt` set; fixture cleanup passed. |
| Day-18 Review bullet overall | PASS | Both adversarial requirements executed through Gateway: wrong-trip QR 422 and manifest no-PII. |
| Warning-emitter truth/DoD check | FAIL / DEFERRED | Repository search finds the routing key only in contract/docs; BSOT `1.19.0` explicitly defers code, handler, test, and emitter to Day 24. |
| Hard invariants | PASS | No `.csproj` `PackageReference Version=`; no prohibited dependency or MediatR 12+ declaration; no Day-18 `Co-Authored-By`; `git diff --check` clean; Day-18 C# working files are CRLF and TS/JSON/Markdown are LF per attributes. |

## Contract / event / schema changes shipped

- REST: `GET /v1/driver/me/schedule`; `GET /v1/bookings/trips/{tripId}/manifest`; `POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}`; `POST /v1/bookings/trips/{tripId}/boarding/qr-scan`.
- Internal DTO: append nullable `driverUserId` and `assistantUserId` to `GET /internal/v1/trips/{tripId}` / frozen TripSnapshot.
- Gateway: `/v1/driver` and `/v1/assistant` require DRIVER/ASSISTANT; `/v1/bookings/trips` routes to Booking and requires DRIVER/ASSISTANT.
- Event contract only: `trip.stop.departed_with_pending`, with common metadata plus trip/stop/crew/pending-count/departure fields and no passenger PII. BSOT registry and §13 changelog are updated.
- Errors: no new code; reuse `BOOKING_NOT_FOUND`, `BOOKING_NOT_FOR_THIS_TRIP`, `BOOKING_PASSENGER_ALREADY_BOARDED`, and `FORBIDDEN`.
- Schema/migration: none.

## Known gaps & carry-over for Day 19

- Preserve the explicit dependency that Day 24 must implement and verify the leave-stop pending-passenger warning emitter together with NO_SHOW detection. If Day 18 must be considered READY before then, the human must re-baseline the canonical Day-18 timeline DoD; the audit cannot silently reinterpret it.
- Real Google OAuth remains unexecuted because local Google credentials/token are unavailable. This inherent external-credential skip does not affect the fully local Day-18 business flow.

## Notes for Day 19 planning

- Day-18 business endpoints, persistence side effect, Gateway RBAC, fresh Docker build, health matrix, and Review adversarial cases are green.
- Keep `/v1/bookings/trips` more specific than the PASSENGER-only `/v1/bookings` route; Gateway depends on longest-prefix matching.
- Do not treat the warning contract as an emitted event: no Outbox side effect is expected until the Day-24 implementation.
