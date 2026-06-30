# VietRide — Postman collection

This is the **single cumulative** Postman collection for VietRide — the graded deliverable the
external reviewer runs (`BE_TIMELINE_VU.md`: _"external reviewer runs full Postman collection
without errors"_). It also doubles as the **tier-5 real-app E2E** for `/audit-day` and `/verify`.

- `vietride.postman_collection.json` — the collection, organized by domain folders. **Grow this file
  per PR** (timeline: _"update Postman collection"_); do **not** add per-day `day-N-*.json` files.
- `vietride.local.postman_environment.json` — local environment: `baseUrl=http://localhost:3000`
  plus per-run placeholders. Externally-supplied secrets (`googleIdToken`,
  `systemAdminAccessToken`) are placeholders — fill them at run time, never commit a real token.

## Run with Newman (CLI)

```bash
# bring the stack up first (see /audit-day tier 4 or /smoke-test)
npx newman run docs/api/postman/vietride.postman_collection.json \
  -e docs/api/postman/vietride.local.postman_environment.json
```

Day-6 operator onboarding needs local-only OTP / SET_INITIAL_PASSWORD token lookup because those
secrets are intentionally not returned by production API responses. For a self-contained local
Day-6 audit run, use the helper wrapper instead of pasting tokens manually:

```bash
node scripts/run-day6-newman-local.js
```

The helper binds only `127.0.0.1`, reads the local dev database, mints a short-lived SYSTEM_ADMIN
JWT from the dev Identity key, and passes `localHarnessEnabled=true` to Newman. The helper requests
inside the cumulative collection are skipped unless that variable is enabled, so the normal full
collection remains runnable with externally supplied secrets/placeholders.

Day-7 station/stop adversarial cases are covered by the deterministic local harness below, so the
collection no longer depends on pre-supplied reviewer values for the required cross-operator and
non-approved checks:

```bash
node scripts/run-day7-newman-local.js
# or
npm run postman:day7:local
```

The helper seeds local-only Identity/Trip data, mints short-lived JWTs from the dev Identity key,
and provides the required variables at runtime. Never commit real token values.

Day-8 route/route-stop/fare-template/alternative-route adversarial cases are covered by a matching
local harness. It seeds deterministic local Identity/Trip records, mints short-lived JWTs in-process,
and runs only the cumulative collection's Day-8 folder through the Gateway (`http://localhost:3000`):

```bash
node scripts/run-day8-newman-local.js
# or
npm run postman:day8:local
```

The Day-8 helper seeds/mints the folder's required runtime values automatically, including
`operatorAdminAccessToken`, `operatorUserAccessToken`, `nonApprovedOperatorAccessToken`,
`operatorId`, `day8OriginStationId`, `day8DestinationStationId`,
`day8AlternativeDestinationStationId`, `day8MissingStationId`, `day8StopId`,
`day8SecondStopId`, and `day8CrossOperatorRouteId`. Never commit real token values.

Day-9 vehicle/driver-schedule adversarial cases use the same local-harness pattern. It seeds
approved + second-operator Identity/Trip data, mints short-lived JWTs in-process, and runs only the
cumulative collection's Day-9 folder through the Gateway (`http://localhost:3000`):

```bash
node scripts/run-day9-newman-local.js
# or
npm run postman:day9:local
```

The Day-9 helper supplies `operatorAdminAccessToken`, `operatorUserAccessToken`,
`day9OtherOperatorAccessToken`, `operatorId`, `day9RouteId`, `day9CrossOperatorVehicleId`,
`day9StandardVehicleTypeId`, `day9UnknownVehicleTypeId`, `day9DriverUserId`, and
`day9AssistantUserId`. The folder verifies the 3 system VehicleType seed rows (45/9/40), Vehicle
happy path and validation errors, tenant-hidden Vehicle reads, and DriverSchedule conflict handling.
Never commit real token values.

Day-11 trip search/detail/seat-map audit coverage uses a deterministic local harness. It seeds
approved Identity users plus prerequisite Trip config data only (Saigon → Can Tho stations,
operator stations, route, stop/fare template, vehicle seats A01/A02, and an inactive Monday
DriverSchedule), mints short-lived JWTs in-process, runs only the Day-11 activation/search/detail/
seat-map folder through the Gateway (`http://localhost:3000`), then verifies the activation-
generated Trip DB side effects after Newman:

```bash
node scripts/run-day11-newman-local.js
# or
npm run postman:day11:local
```

The Day-11 helper supplies `operatorAdminAccessToken`, `passengerAccessToken`,
`day11DriverScheduleId`, `day11OriginStationId`, `day11DestinationStationId`, and
`day11MissingStationId`, plus a runtime `day11DepartureDate` aligned to the next local ICT service
day. The folder stays Gateway-only: it activates the seeded DriverSchedule,
performs public trip search plus an empty-result adversarial search, and reads trip detail/seat-map
as a passenger. After Newman, the harness polls for exactly one activation-generated scheduled/
boarding Trip with generated A01/A02 seats, trip stops, and stop fares, then calls Trip service
internal endpoints directly (`http://localhost:5002` by default) with `X-Internal-Auth` to verify
lock/release/book/unavailable seat semantics. The harness prints `Day-11 generation evidence:` and
`Day-11 internal seam evidence:` lines for checklist handoff; JWTs and lock tokens are not printed
in full. Never commit real token values.

If you run the Day-8 folder manually without the helper, provide equivalent local values:

- `operatorAdminAccessToken` — a valid `OPERATOR_ADMIN` JWT for an `APPROVED`, active operator.
- `operatorUserAccessToken` — a valid operator user JWT for the same approved operator.
- `nonApprovedOperatorAccessToken` — a valid operator JWT for a non-`APPROVED` or inactive operator;
  the request must return exact `403 FORBIDDEN`.
- `operatorId` — the approved operator id that owns the Day-8 test data.
- `day8OriginStationId` and `day8DestinationStationId` — two active Station ids available to the
  approved operator; the origin/destination equality case must return `422`.
- `day8AlternativeDestinationStationId` — an active Station id used by AlternativeRoute create cases.
- `day8MissingStationId` — a syntactically valid Station id that does not exist; the request must
  return exact `404 STATION_NOT_FOUND`.
- `day8StopId` and `day8SecondStopId` — active Stop ids owned by the approved operator and valid for
  the Day-8 route-stop / alternative-route flow.
- `day8CrossOperatorRouteId` — a Route id owned by another operator; the request must return exact
  `404 ROUTE_NOT_FOUND`.

Or import both files into the Postman app (Collection + Environment) and run the folders.

The Day-17 carry-over and Day-18 driver folders run through Gateway on `{{baseUrl}}`. Before
running them, provide valid `passengerAccessToken`, `operatorAdminAccessToken`,
`driverAccessToken`, and (when testing the same flow as an assistant) `assistantAccessToken`.
Day 17 also needs a confirmed `day17BookingId`. Day 18 needs assigned `day18TripId` and
`day18OtherTripId` fixtures, plus `day18PassengerRecordId` and `day18BookingCode` from a confirmed
booking on the assigned trip. `day18OtherTripId` must identify a different trip assigned to the
same driver or assistant; otherwise the wrong-trip cases stop at authorization with `403` instead
of reaching the intended `422 BOOKING_NOT_FOR_THIS_TRIP`. The committed environment contains
placeholders only; never commit real JWTs or fixture secrets.

## Notes

- Requests hit the **Gateway** (`:3000`) using the real resource-prefixed routes
  (`/v1/auth/...`, `/v1/users/...`, `/v1/admin/...`) — see `apps/gateway/src/config/routes.ts`.
- Flows needing a real external credential (e.g. the Google OAuth path needs a real `googleIdToken`)
  are **SKIP** in an audit when that credential is unavailable — see the `/audit-day` Review-bullet
  scoring rule.
- Redact tokens/secrets when pasting run output into a checklist or PR.
