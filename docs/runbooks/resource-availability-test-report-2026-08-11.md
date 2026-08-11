# Resource Availability Test Report — 2026-08-11

## Outcome

The driver, assistant, and vehicle availability implementation passed the complete Trip unit and PostgreSQL/Redis integration suites, the affected TypeScript suites, native builds and formatting checks, and the Docker smoke matrix. The new reservation data observed in the local runtime database had no overlapping active pair and no orphan source.

Two pre-existing local-data observations remain outside this feature:

- Booking Hangfire jobs `4242` and `4246` retry a lookup for deleted Trip `00000000-0000-4000-8000-000000000013` and receive the expected `TRIP_NOT_FOUND` response.
- A seeded ShuttleTrip dated 2026-07-18 remains `SCHEDULED` with two matching `RESERVED` rows. Its reservation graph is internally consistent, but the business record is stale relative to the test date.

Neither item was mutated during this verification run.

## Automated verification

| Layer | Result | Evidence |
|---|---:|---|
| Trip unit tests | 726/726 passed | Resource policy, Google Routes fail-closed behavior, migration SQL, start-blocked alert deduplication, and existing Trip behavior |
| Trip integration tests | 353/353 passed | Real PostgreSQL and Redis; duration 6m07s |
| Gateway tests | 213/213 passed | Route/auth/proxy suite |
| Shared contract tests | 137/137 passed | 22 suites, including `trip.assignment.start_blocked` schema |
| Notification tests | 358/358 passed | 47 suites, including the assignment-start-blocked consumer |
| Trip Release build | Passed | 0 warnings, 0 errors |
| Trip format verification | Passed | `dotnet format --verify-no-changes` |
| Gateway/Contracts/Notification lint and build | Passed | Existing dependency source-map warnings only; no build errors |
| Idempotency inventory | Passed | 210 mutations, 190 required, 20 exemptions; 45 .NET handlers; 17 Notification subscriptions; 82 runtime bindings |
| Diff/EOL check | Passed | `git diff --check` |

The first parallel Jest run had one transient Gateway failure (212/213). Gateway immediately passed 213/213 in isolation, and the complete three-project suite subsequently passed sequentially with 708/708 tests. No code change was required for that transient result.

## Resource and lifecycle matrix

The dedicated real-database integration suite covers the following cases:

1. Clean migration `Up -> Down -> Up`, updated-at trigger, and PostgreSQL exclusion constraint.
2. Same-station 30-minute turnaround boundary for driver, assistant, and vehicle.
3. Cross-role crew protection: a person assigned as driver cannot simultaneously be assigned as assistant, and vice versa.
4. Different-location reposition using Google Routes duration, including the exact allowed boundary.
5. Google disabled, timeout, malformed/unavailable response, and missing coordinates all fail closed with no partial reservation or schedule mutation.
6. Main Trip -> ShuttleTrip and ShuttleTrip -> Main Trip conflicts in both creation orders.
7. Shuttle manifest-derived inbound/outbound endpoints.
8. ShuttleTrip -> ShuttleTrip driver and vehicle conflicts, start, and complete/release lifecycle.
9. Weekly DriverSchedule validation over the full validity range, including far-future occurrences, overnight schedules, cross-role conflicts, and independent drivers.
10. Rolling 30-day generation: five weekly Trips and fifteen reservations are created; a schedule offset by one minute generates zero Trips and five `DRIVER_CONFLICT` skip logs.
11. Main Trip start changes reservations to `ACTIVE`; an active predecessor blocks the next start without changing Trip state; completion releases resources; cancellation releases reservations.
12. Crew, vehicle, and time mutation conflicts roll back the Trip and reservation graph together.
13. Vehicle substitution releases the tracked old reservation and creates the replacement in one transaction.
14. Concurrent reservations for the same resource permit exactly one winner; the database contains no overlap.
15. Vehicle projection returns the current active assignment and nearest reserved assignment with the per-trip driver.

Existing Trip integration coverage also exercised Shuttle persistence, substitution endpoints, event/outbox behavior, and existing Trip/DriverSchedule handlers as part of the 353-test suite.

## Docker smoke matrix

All infrastructure and application containers were healthy:

- PostgreSQL, PgBouncer, Redis, and RabbitMQ
- Gateway, Identity, Trip, Booking, Payment, Parcel, Tracking, Notification, and RAG

HTTP probes returned 200 for all nine direct service health endpoints. Gateway passthrough returned 200 for Identity, Trip, Booking, Payment, and Parcel health routes. Missing and tampered internal JWTs returned 401 on a real Trip internal route; the operator availability route returned 401 without a user JWT.

RabbitMQ runtime verification found durable topic exchange `vietride.events` with the binding:

```text
trip.assignment.start_blocked -> notification:trip-assignment-start-blocked
```

## Runtime database audit

The local `vietride_trip` database applied both new migrations:

```text
20260810190743_AddResourceReservations
20260810193338_AddAssignmentStartBlockedAlert
```

`vietride_trip.resource_reservations` has the updated-at trigger and exclusion constraint `ex_resource_reservations_no_overlap`. The runtime audit returned:

```text
active/reserved overlapping pairs: 0
orphan Trip/ShuttleTrip sources:     0
```

The database contained two reservations for one seeded ShuttleTrip: one `CREW/DRIVER` and one `VEHICLE`, both with source, planned interval, and resource identifiers matching the ShuttleTrip.

## Defects found and corrected during verification

1. The resource-reservation migration referenced an updated-at trigger function that did not exist on a clean database. The migration now creates and reversibly drops a dedicated function.
2. Availability queries used no-tracking reads, which could ignore reservation state transitions staged in the same transaction during vehicle substitution. The query now respects tracked `RELEASED/CANCELLED` changes before creating the replacement reservation.
3. Existing Shuttle/substitution integration fixtures were updated to use the real availability service and valid station coordinates so they exercise production fail-closed behavior instead of bypassing it.

## Commands

```text
dotnet test apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj
dotnet test apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj
dotnet build apps/trip/VietRide.Trip.sln -c Release --no-restore
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes
npx nx run-many -t test -p gateway contracts notification --ci --passWithNoTests --skip-nx-cache --parallel=1
npx nx run-many -t lint build -p gateway contracts notification --skip-nx-cache
node scripts/verify-idempotency-inventory.mjs
git diff --check
docker compose --profile app -f infra/docker/docker-compose.yml up -d --no-build
```
