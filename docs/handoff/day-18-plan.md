# Day 18 — Plan

> Produced by `manager`. Gated by `reviewer` (PLAN-REVIEW) before any worker runs.

- **Timeline ref**: BE_TIMELINE_VU.md -> Day 18 — DriverSchedule + Manifest + Boarding APIs (Jira: SCV-92)
- **Prior checklist**: docs/handoff/day-17-checklist.md (found — Day-17 closeable for its own scope; carry-over: RAG TS/Docker failures pre-existing and NOT Day-18; Postman cumulative collection still owes the cancel + booking-stats flow; round-trip real-Redis Testcontainers regression test recommended; dev DB holds Day-17 artifacts — reset via scripts/reset-local.sh for a clean E2E)
- **Plan status**: APPROVED (human resolved Q1/Q2/Q3/Q4/Q5/Q6/Q7; decisions recorded below)

## Objective
Day 18 ships the Driver/Assistant operational layer: a driver/assistant reads their assigned trips, opens a PII-free passenger manifest ordered by pickup-stop sequence, ticks each passenger boarded (manually or via QR bookingCode scan), and the system flags a missing-passenger warning when a trip leaves a stop with un-ticked passengers. This makes the boarding lifecycle real (Passenger boardingStatus PENDING->BOARDED) and unblocks Day-24 NO_SHOW/PARTIAL_NO_SHOW detection. The work spans Trip (driver schedule read; the trip is the authorization anchor via Trip.DriverUserId/AssistantUserId) and Booking (it owns the Passenger entity whose boardingStatus the boarding tick mutates). Per the resolved decisions below, Booking owns the manifest read + both boarding POST endpoints (single owner of Passenger state) and calls the Trip internal snapshot for stop order + driver/assistant auth; the boarding-warning event is registered contract-only on Day 18 (emitter deferred to Day 24).

## Resolved decisions
> Human resolved the gating open questions per the manager recommendations. Recorded here and propagated into the affected tasks below.

- **Q1 — RESOLVED (option a): Booking owns** GET manifest + both boarding POST endpoints (single owner of Passenger state). 18.2/18.3/18.4 live entirely under apps/booking/... . Booking authorizes caller-is-trip-driver/assistant by calling the Trip internal snapshot (ITripServiceClient.GetTripSnapshotAsync) for stop order + driver/assistant ids. Trip domain/persistence MUST NOT be edited by these tasks (read-only HTTP seam only).
- **Q2 — RESOLVED (extend, not new endpoint):** EXTEND the FROZEN TripSnapshot / InternalTripSnapshotDto with DriverUserId/AssistantUserId (both Guid?, assistant nullable per Trip.cs 16-17). NO dedicated /internal/v1/trips/{id}/crew endpoint. 18.0 proceeds on the extension path; BSOT section 13 row 1.18.0 appended atomically with 18.0 before 18.2+ dispatch.
- **Q3 — RESOLVED (contract-only now):** Register the boarding-warning event CONTRACT-ONLY on Day 18. Task 18.5 is registry-only — BSOT section 7 registry row + section 13 changelog + payload doc, NO emitter wired on Day 18. Routing key = trip.stop.departed_with_pending (well-formed: svc=trip, aggregate=stop, verb=departed_with_pending). Emitter is wired on Day 24 with NO_SHOW detection. DoD line 5 adjusted to event contract registered (emitter deferred to Day 24).
- **Q6 — RESOLVED (Booking-owned gateway prefix):** Manifest/boarding move to a Booking-owned gateway prefix, NOT under /v1/trips. Final paths: GET /v1/bookings/trips/{tripId}/manifest; POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}; POST /v1/bookings/trips/{tripId}/boarding/qr-scan. This prefix /v1/bookings/trips is longest-prefix-distinct from the existing /v1/bookings row (routes.ts line 190, requiredRoles PASSENGER) — confirmed against routes.ts: matchRoute is longest-prefix-wins (lines 324-328), /v1/bookings/trips out-specifics /v1/bookings, so the new prefix DRIVER/ASSISTANT gate applies and does NOT collide with the PASSENGER row. Target = BOOKING_BASE_URL.
- **Q7 — RESOLVED (reuse, no new code):** REUSE BOOKING_NOT_FOR_THIS_TRIP (422) for passengerRecordId exists but not on this tripId in the boarding-tick path. Broaden its BSOT section 5.9 note (line 1346) to cover the boarding-tick path (not just QR-scan). No new error code, no extra BSOT section 13 row. Minor section 5.9 doc change owned by 18.3.
- **Q4 — RESOLVED (ICT inclusive date bounds):** `from`/`to` are ICT (UTC+7) date bounds, inclusive at both ends. If both are omitted, default to today through today + 14 days. If exactly one is supplied, return 422 VALIDATION_ERROR.
- **Q5 — RESOLVED (terminal pickup first):** Bookings with `pickupStationId` set and `pickupStopId` NULL sort at the beginning as the origin terminal with effective `orderIndex = 0`.
- **18.5 payload — RESOLVED:** `trip.stop.departed_with_pending` carries common integration-event metadata `eventId` (Guid), `occurredAt` (DateTime UTC), and `eventType` (constant `trip.stop.departed_with_pending`), plus `tripId` (Guid), `stopId` (Guid), `stopName` (string snapshot), `pendingPassengerCount` (positive int), `driverUserId` (Guid), `assistantUserId` (Guid?, nullable), and `departedAt` (DateTimeOffset serialized as UTC ISO-8601). It deliberately excludes passenger IDs, booking codes, and PII; clients re-read the manifest for detail.

## Success criteria (DoD — binary, verifiable)
- [ ] GET /v1/driver/me/schedule returns trips assigned to the caller, filtered by driverId/assistantId from the User JWT (cross-operator/other-driver trips never appear). (BE_TIMELINE_VU Day 18; v7 line 478)
- [ ] GET /v1/bookings/trips/{tripId}/manifest returns the passenger list ordered by pickup-stop order, operational fields ONLY (seatNumber, bookingCode, pickup stop, boardingStatus) — no PII. (BE_TIMELINE_VU Day 18; v7 line 479, 1711; path per Q6)
- [ ] POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId} ticks one Passenger BOARDED; re-tick returns 409 BOOKING_PASSENGER_ALREADY_BOARDED; passenger not on this trip returns 422 BOOKING_NOT_FOR_THIS_TRIP. (BSOT 5.9 line 1347/1346; path per Q6; error per Q7)
- [ ] POST /v1/bookings/trips/{tripId}/boarding/qr-scan accepts a bookingCode, returns the matching booking passenger records; wrong-trip code -> 422 BOOKING_NOT_FOR_THIS_TRIP; unknown -> 404 BOOKING_NOT_FOUND. (v7 1636-1638; BSOT 5.9 1343/1346; path per Q6)
- [ ] Boarding-warning event CONTRACT registered (BSOT section 7 registry row + section 13 changelog + payload doc) for the Driver App alert; routing key trip.stop.departed_with_pending. Emitter deferred to Day 24 (wired with NO_SHOW detection) — no emitter on Day 18. (BE_TIMELINE_VU Day 18; v7 482-485; Q3)
- [ ] All Day-18 endpoints documented in VietRide_API_Contract_v1.md, routed through the Gateway, the new event in BSOT 7 registry + 13 changelog.
- [ ] dotnet build -c Release + dotnet format --verify-no-changes green per touched solution; dotnet test green; migration (if any) up+down clean; gateway build/lint/test green.

## Contract changes
NOT yet in VietRide_API_Contract_v1.md (verified: only Day-9/11 DriverSchedule create/activate at lines 3417-3525; grep manifest/boarding/qr-scan = 0). New sections must be authored:
- REST (Trip): GET /v1/driver/me/schedule?from=&to= — auth DRIVER/ASSISTANT, tenant = self via JWT sub.
- REST (Booking — manifest/boarding, paths FINALIZED per Q6): GET /v1/bookings/trips/{tripId}/manifest; POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}; POST /v1/bookings/trips/{tripId}/boarding/qr-scan — auth DRIVER/ASSISTANT, only if caller is the trip driver or assistant (v7 line 2187), authorized via the Trip internal snapshot new DriverUserId/AssistantUserId.
- Internal (Trip): GET /internal/v1/trips/{tripId} raw-DTO shape gains DriverUserId/AssistantUserId (API Contract lines 1065-1179) — additive, mirrors the 18.0 TripSnapshot extension.
- Error codes: existing in BSOT 5.9 — BOOKING_NOT_FOUND 404 (line 1343), BOOKING_NOT_FOR_THIS_TRIP 422 (line 1346 — note broadened by Q7 to cover the boarding-tick path, not just QR-scan), BOOKING_PASSENGER_ALREADY_BOARDED 409 (line 1347); not-the-assigned-driver reuses FORBIDDEN 403 (line 1419). No new error code added (Q7 reuse).
- Event: boarding-warning (missing-passenger-on-stop-leave) — register CONTRACT-ONLY in BSOT section 7 registry (lines 1752-1765) + section 13 changelog, routing key trip.stop.departed_with_pending, payload documented; emitter deferred to Day 24 (Q3).
- Gateway routes (Task 18.6): (a) PATCH the existing /v1/driver + /v1/assistant rows (routes.ts 179-180, currently NO requiredRoles) to add requiredRoles DRIVER/ASSISTANT; (b) ADD a new /v1/bookings/trips prefix -> BOOKING_BASE_URL, authRequired user, requiredRoles DRIVER/ASSISTANT (longest-prefix-distinct from /v1/bookings PASSENGER row, line 190).
- Migration: none expected — Passenger has BoardingStatus/BoardedAt/BoardedAtStopId (Passenger.cs 17-21); BookingStatus has PARTIAL_NO_SHOW/NO_SHOW/COMPLETED. Confirm in DISCOVER before scaffolding.

## Tasks

> Day 18 lists no explicit Pre-reqs/architecture-baseline line, BUT this is a cross-service day with a now-resolved ownership seam (Q1=Booking) and a missing internal-snapshot field (Q2=extend). Task 18.0 is the baseline that adds the internal driver/assistant fields the manifest auth needs; every feature task depends on it.

### Task 18.0 — Architecture baseline: expose driver/assistant on internal trip snapshot
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | (none) targeted additive change |
| owned files | apps/trip/src/VietRide.Trip.Application/Features/Internal/Trips/GetTripSnapshot/InternalTripSnapshotDto.cs (append DriverUserId/AssistantUserId, both Guid?); .../GetTripSnapshotHandler.cs (populate from Trip.DriverUserId/AssistantUserId); apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients/ITripServiceClient.cs (append DriverUserId/AssistantUserId to the TripSnapshot record at lines 12-25 ONLY — do NOT touch the lock-seats unions); BACKEND_SOURCE_OF_TRUTH.md (section 13 changelog row 1.18.0); VietRide_API_Contract_v1.md (lines 1065-1179 internal-snapshot shape, append the two fields). NOTE: apps/booking/src/VietRide.Booking.Infrastructure/Http/TripServiceClient.cs needs NO edit — GetTripSnapshotAsync (lines 34-57) deserializes via ReadFromJsonAsync<TripSnapshot> (line 50) with web defaults, so appended nullable record fields bind automatically; confirm in DISCOVER, edit only if a positional ctor mismatch surfaces |
| forbidden scope | .env, secrets; any other service; Identity/Payment/Parcel; gateway; git ops; do NOT add manifest/boarding endpoints here (18.2/18.3); do NOT change seat-lock seam shapes (the LockSeats* unions in ITripServiceClient.cs); do NOT reorder/remove/retype existing TripSnapshot fields — APPEND ONLY |
| depends on | (none — gating Qs resolved). Implements the Q2 extension path (decided). |
| invariant flags | CRLF cs; CPM (no Version attribute); MediatR v11; no cross-DB FK (driver/assistant ids stay logical keys, no EF relationship); internal endpoints return raw DTO (no ApiResponse envelope, section 1.6.2); APPEND-ONLY to the FROZEN TripSnapshot (additive MINOR change — new fields appended at the end, nullable, none removed/retyped) |
| acceptance | InternalTripSnapshotDto + TripSnapshot carry DriverUserId/AssistantUserId (both Guid?); handler populates from Trip.DriverUserId/AssistantUserId; both solutions (Trip + Booking) build + dotnet format clean; existing GetTripSnapshot tests updated/green; a BSOT section 13 changelog row (next version 1.18.0) for the TripSnapshot extension is appended and committed atomically with this task BEFORE 18.2+ dispatch — the row documents adding DriverUserId/AssistantUserId (both Guid?, assistant nullable per Trip.cs 16-17) to the FROZEN inter-service TripSnapshot DTO (currently BSOT section 13 row 1.8.0; ITripServiceClient.cs line 4 marks it FROZEN) plus the mirrored GET /internal/v1/trips/{tripId} raw-DTO shape in VietRide_API_Contract_v1.md lines 1065-1179. MINOR additive contract change (new record fields appended, none removed/retyped) |
| source citations | v7 line 2187 (driver/assistant role check); Trip.cs 16-17 (DriverUserId Guid, AssistantUserId Guid?); InternalTripSnapshotDto.cs 3-16 (no driver fields today); ITripServiceClient.cs 12-25 (TripSnapshot FROZEN, BSOT 13 row 1.8.0), line 4 (FROZEN marker); TripServiceClient.cs 49-50 (ReadFromJsonAsync<TripSnapshot> — auto-binds appended fields) |

### Task 18.1 — GET /v1/driver/me/schedule (assigned trips by JWT)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | new apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/GetMyDriverSchedule/ (Query+Handler+DTO+Validator); apps/trip/src/VietRide.Trip.Api/Controllers/DriverController.cs (DISCOVER existing /driver controller first); apps/trip/src/VietRide.Trip.Infrastructure/Persistence/Repositories/ (read query: trips where DriverUserId or AssistantUserId equals caller, date-ranged); unit tests under apps/trip/tests/VietRide.Trip.UnitTests/; VietRide_API_Contract_v1.md (new section) |
| forbidden scope | .env, secrets; Booking/Identity/Payment/Parcel; gateway routes (the /v1/driver prefix already exists 179-180, do not duplicate); git ops; do NOT touch DriverSchedule create/activate (Day 9/11); do NOT mutate trip state |
| depends on | 18.0. Q4 resolved: ICT (UTC+7) inclusive date bounds; both omitted defaults to today..today+14 days; exactly one supplied returns 422 VALIDATION_ERROR. |
| invariant flags | CRLF cs; CPM; MediatR v11; IQuery so TransactionBehavior skips tx (Day-17 read-query precedent); tenant isolation equals caller sub only (never trust a query-param driverId) |
| acceptance | returns trips where DriverUserId matches sub OR AssistantUserId matches sub within from..to using ICT (UTC+7) date bounds inclusive at both ends; both omitted defaults to today..today+14 days; exactly one supplied returns 422 VALIDATION_ERROR; a different sub sees only their own; ApiResponse envelope (ADR 0004); at least 1 happy + 1 isolation test; Swagger annotation; build/format/test green; contract section added |
| source citations | BE_TIMELINE_VU Day 18 (filtered by driverId from JWT); v7 478; Trip.cs 16-17; CurrentUserClaims.cs (Trip) 7-16 (sub); routes.ts 179-180 |

### Task 18.2 — GET /v1/bookings/trips/{tripId}/manifest (PII-free, pickup-order)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | (Booking-owned per Q1) new apps/booking/src/VietRide.Booking.Application/Features/Manifest/GetTripManifest/ (Query+Handler+DTO+Validator); apps/booking/src/VietRide.Booking.Api/Controllers/ — new operational controller for /v1/bookings/trips/{tripId}/... (do NOT add to BookingsController.cs which is the PASSENGER surface; create e.g. TripManifestController.cs / BoardingController.cs); apps/booking/tests/VietRide.Booking.UnitTests/ (+ IntegrationTests if a controller test is added); VietRide_API_Contract_v1.md (manifest section, path per Q6). Booking authorizes via ITripServiceClient.GetTripSnapshotAsync (now carries DriverUserId/AssistantUserId from 18.0) and reads stop order from TripSnapshot.Stops OrderIndex. |
| forbidden scope | .env, secrets; Trip/Identity/Payment/Parcel domain+persistence (Booking MAY call the Trip internal snapshot read-only via ITripServiceClient, but MUST NOT edit Trip source); gateway routes (18.6 owns routes.ts); git ops; do NOT expose any PII field; do NOT mutate boarding state (18.3) |
| depends on | 18.0. Q5 resolved: terminal-pickup bookings (`pickupStationId` set, `pickupStopId` NULL) sort first as origin `orderIndex = 0`. |
| invariant flags | CRLF cs; CPM; MediatR v11; IQuery (read-only, skip tx); tenant/role isolation equals caller must be the trip DriverUserId or AssistantUserId (from TripSnapshot, v7 2187) else 403 FORBIDDEN; no PII in DTO; no cross-DB FK |
| acceptance | items ordered by pickup-stop order (TripSnapshot.Stops OrderIndex); terminal-pickup bookings (`pickupStationId` set, `pickupStopId` NULL) sort first as origin `orderIndex = 0`; each item equals seatNumber/bookingCode/pickupStop/boardingStatus and NOTHING else; non-driver/assistant gets 403 FORBIDDEN; trip with no confirmed bookings gets empty 200 (not 404); ApiResponse envelope; at least 1 happy + 1 auth-isolation + 1 no-PII-leaked test; include a terminal-pickup test asserting origin-first ordering; build/format/test green; contract section added at /v1/bookings/trips/{tripId}/manifest. |
| source citations | BE_TIMELINE_VU Day 18 (pickup-order, operational fields only, no PII); v7 479,1711,2187; BookingRepository.GetByTripIdWithPassengersAsync (BSOT 612); Passenger.cs 14-21; TripStopSnapshot.OrderIndex (ITripServiceClient.cs 33); ITripServiceClient.GetTripSnapshotAsync (line 136, now returns DriverUserId/AssistantUserId per 18.0) |

### Task 18.3 — POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId} (tick one boarded)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | (Booking-owned per Q1 — Booking owns Passenger mutation; Passenger.MarkBoarded is on the Booking aggregate) new apps/booking/src/VietRide.Booking.Application/Features/Boarding/TickPassengerBoarded/ (Command+Handler+Validator); add the POST route to the Booking operational controller created in 18.2 (BoardingController.cs); apps/booking/tests/VietRide.Booking.UnitTests/ (+ IntegrationTests for the controller); VietRide_API_Contract_v1.md (boarding section, path per Q6); BACKEND_SOURCE_OF_TRUTH.md (MINOR doc edit: broaden the section 5.9 BOOKING_NOT_FOR_THIS_TRIP note at line 1346 to also cover the boarding-tick path, per Q7) |
| forbidden scope | .env, secrets; Trip/Identity/Payment/Parcel domain+persistence; gateway routes (18.6); git ops; do NOT implement NO_SHOW/PARTIAL_NO_SHOW (Day 24); do NOT auto-complete the trip; do NOT add a new section 5.9 error code (Q7 = reuse) or a new BSOT section 13 row |
| depends on | 18.0, 18.2 (shares the Booking operational controller). (Q7 resolved — no longer blocked.) |
| invariant flags | CRLF cs; CPM; MediatR v11; idempotency — re-tick of already-BOARDED returns 409 BOOKING_PASSENGER_ALREADY_BOARDED (BSOT 1347); mutate through the Booking aggregate root (Passenger.MarkBoarded, Passenger.cs 42); tenant/role equals trip driver/assistant via TripSnapshot (403 else); no cross-DB FK; boardedAtStopId logical FK to trip.stops (Passenger.cs 20) |
| acceptance | boardingStatus flips PENDING->BOARDED with BoardedAt set; second tick gets 409 BOOKING_PASSENGER_ALREADY_BOARDED (BSOT 5.9 1347); passenger record exists but does NOT belong to tripId -> 422 BOOKING_NOT_FOR_THIS_TRIP (Q7 reuse — and the section 5.9 note at line 1346 is broadened to cover this boarding-tick path); passengerRecordId does not exist at all -> 404 BOOKING_NOT_FOUND (BSOT 5.9 1343); non-driver/assistant gets 403 FORBIDDEN; ApiResponse envelope; at least 1 happy + 1 re-tick-409 + 1 wrong-trip-422 + 1 auth-403 test; build/format/test green; contract section added at /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId} |
| source citations | BE_TIMELINE_VU Day 18; v7 1633-1634 (per-passenger tick), 2187 (role), 2267-2273 (boarding fields); Passenger.cs 42-47; BSOT 5.9 1347 (ALREADY_BOARDED), 1346 (BOOKING_NOT_FOR_THIS_TRIP — note broadened per Q7), 1343 (BOOKING_NOT_FOUND), 1419 (FORBIDDEN) |

### Task 18.4 — POST /v1/bookings/trips/{tripId}/boarding/qr-scan (resolve bookingCode to passenger records)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-endpoint |
| owned files | (Booking-owned per Q1 — Booking owns bookingCode + Passenger) new apps/booking/src/VietRide.Booking.Application/Features/Boarding/ScanBookingCodeForTrip/ (Query/Command+Handler+Validator); add the POST route to the Booking operational controller created in 18.2 (BoardingController.cs); apps/booking/tests/VietRide.Booking.UnitTests/ (+ IntegrationTests); VietRide_API_Contract_v1.md (qr-scan section, path per Q6) |
| forbidden scope | .env, secrets; Trip/Identity/Payment/Parcel domain+persistence; gateway routes (18.6); git ops; do NOT tick boarding here (scan returns records only; tick is 18.3); do NOT decode anything but the plain bookingCode string |
| depends on | 18.0, 18.2 (shares the Booking operational controller) |
| invariant flags | CRLF cs; CPM; MediatR v11; read-mostly (IQuery if pure read); bookingCode regex anchored VR-8digits-8base32 (v7 1631); tenant/role equals trip driver/assistant via TripSnapshot (403); no cross-DB FK |
| acceptance | scan returns passenger records (seatNumber + boardingStatus) for a bookingCode belonging to tripId; different-trip code gets 422 BOOKING_NOT_FOR_THIS_TRIP; unknown code gets 404 BOOKING_NOT_FOUND; non-CONFIRMED handled per v7 1636 (no state change); non-driver/assistant gets 403; ApiResponse envelope; at least 1 happy + 1 wrong-trip-422 + 1 unknown-404 test; build/format/test green; contract section added at /v1/bookings/trips/{tripId}/boarding/qr-scan |
| source citations | BE_TIMELINE_VU Day 18; v7 1631-1644, 1636-1638; BSOT 5.9 1343/1346; BookingRepository.GetByCodeAsync (BSOT 604) |

### Task 18.5 — Register boarding-warning event CONTRACT (registry-only, emitter deferred to Day 24)
| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | add-integration-event |
| owned files | (registry-only per Q3 — NO emitter) BACKEND_SOURCE_OF_TRUTH.md (section 7 event registry row + section 13 changelog row); VietRide_API_Contract_v1.md or docs/ event-payload doc (the boarding-warning payload schema). DO NOT add an Outbox-emit call, a domain event, or any handler wiring on Day 18 — that is Day-24 work. |
| forbidden scope | .env, secrets; any service source code (no emitter, no handler, no domain event); consumers in other repos (Notification is Tuyen-owned, register routing key + payload contract only); git ops; do NOT wire the emitter (Day 24 with NO_SHOW detection) |
| depends on | (none for the registry rows — Q3 resolved; logically sits alongside 18.3 since it documents the boarding domain, but writes only registry/doc files) |
| invariant flags | docs/registry only — no .cs emitter. Routing key trip.stop.departed_with_pending is well-formed per svc.aggregate.verb_past (svc=trip, aggregate=stop, verb=departed_with_pending); register exactly this key. Frozen payload: eventId Guid, occurredAt DateTime UTC, eventType constant string, tripId Guid, stopId Guid, stopName string snapshot, pendingPassengerCount positive int, driverUserId Guid, assistantUserId Guid? nullable, departedAt DateTimeOffset serialized as UTC ISO-8601. No passenger IDs, booking codes, or PII. |
| acceptance | routing key trip.stop.departed_with_pending registered in BSOT section 7 registry (lines 1752-1765) + a section 13 changelog row; payload schema documented with exactly the 10 frozen fields/types in invariant flags; NO emitter, NO Outbox call, NO test of an emit condition on Day 18 (DoD line 5 = event contract registered, emitter deferred to Day 24); both touched docs build/format clean (markdown); no .cs file changed by this task |
| source citations | BE_TIMELINE_VU Day 18 (emit event when Trip leaves a stop with PENDING passengers — emitter deferred per Q3); v7 482-485; BSOT 7 1752-1765 (no boarding-warning key exists today) |

### Task 18.6 — Gateway routes for manifest/boarding + driver/assistant role gate + Postman + docs
| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files | apps/gateway/src/config/routes.ts — specifically: (a) PATCH the two EXISTING rows for prefix /v1/driver (line ~179) and prefix /v1/assistant (line ~180), both currently authRequired user with NO requiredRoles, to add requiredRoles DRIVER/ASSISTANT; (b) ADD a new row for prefix /v1/bookings/trips, target env.BOOKING_BASE_URL, authRequired user, requiredRoles DRIVER/ASSISTANT (per Q6) — place it near the Booking block (after line ~194); (c) confirm /v1/driver covers /v1/driver/me/schedule. gateway route tests under apps/gateway/src/config/routes.spec.ts; docs/api/postman/ (Day-18 schedule+manifest+boarding+qr-scan flow; also clears Day-17 cancel+booking-stats Postman carry-over) |
| forbidden scope | .env, secrets; .NET service source; git ops; do NOT change unrelated routes; do NOT touch the existing /v1/bookings PASSENGER row (line 190) or /v1/trips row (line 123) beyond what is listed |
| depends on | 18.1, 18.2, 18.3, 18.4 |
| invariant flags | LF ts/json; role gate is NOT yet present — /v1/driver and /v1/assistant (routes.ts ~179-180) today have NO requiredRoles, so a PASSENGER JWT currently passes the gateway for any /v1/driver/* or /v1/assistant/*; this task ADDS requiredRoles DRIVER/ASSISTANT to both (do not assume a gate exists). ROUTING-PRECEDENCE (Q6 RESOLVED): matchRoute (routes.ts 324-328) is longest-prefix-wins (the comment at line 325 confirms /v1/identity/health beats /v1/auth). The new /v1/bookings/trips prefix is LONGER than the existing /v1/bookings row (line 190, requiredRoles PASSENGER), so longest-prefix-wins routes /v1/bookings/trips/{tripId}/... to the new DRIVER/ASSISTANT row and does NOT collide with the PASSENGER row — the spec must assert this. (The old /v1/trips collision is moot: the manifest/boarding paths now live under /v1/bookings/trips, not /v1/trips.) No banned TS deps |
| acceptance | the two existing /v1/driver + /v1/assistant rows carry requiredRoles DRIVER/ASSISTANT; a new /v1/bookings/trips row -> BOOKING_BASE_URL with requiredRoles DRIVER/ASSISTANT exists; a PASSENGER JWT hitting /v1/driver/me/schedule (and any /v1/assistant/*) gets 403 at the gateway (routes.spec.ts test — none today); a routes.spec.ts test proves /v1/bookings/trips/{tripId}/manifest matches the NEW /v1/bookings/trips row (BOOKING target, DRIVER/ASSISTANT gate) and NOT the /v1/bookings PASSENGER row; a DRIVER/ASSISTANT JWT proxies all 4 new endpoints to the correct target; gateway build/lint/test green; Postman runs the Day-18 flow + carried-over Day-17 flow |
| source citations | BE_TIMELINE_VU cross-cutting (gateway route entry; update Postman); routes.ts 122-127 (/v1/trips -> TRIP_BASE_URL), 179-180 (/v1/driver + /v1/assistant, no requiredRoles today), 188-194 (/v1/bookings -> BOOKING_BASE_URL, requiredRoles PASSENGER), 324-328 (matchRoute longest-prefix-wins, comment line 325); day-17-checklist.md line 94 (Postman carry-over) |

## Dispatch order
1. Gating Open questions are RESOLVED (Q1/Q2/Q3/Q6/Q7 — see Resolved decisions). Q4/Q5 remain non-gating: resolve Q4 before 18.1 dispatch, Q5 before 18.2 dispatch.
2. Task 18.0 (baseline: internal snapshot driver/assistant fields + BSOT section 13 row 1.18.0 + contract mirror). parallel-safe: no (blocks 18.1/18.2/18.3/18.4).
3. Task 18.1 (driver schedule, Trip-only). parallel-safe: yes — Q1=Booking means 18.2-18.4 live in apps/booking, disjoint from 18.1 Trip write set (Trip DriverController + Trip feature folder). Resolve Q4 first.
4. Task 18.2 (manifest, Booking). parallel-safe: no (creates the shared Booking operational controller that 18.3/18.4 extend). Resolve Q5 first.
5. Task 18.3 (boarding tick) then 18.4 (qr-scan). serial within Booking (both extend the 18.2 controller). parallel-safe: no.
6. Task 18.5 (boarding-warning event CONTRACT — registry/docs only). parallel-safe: yes vs all .cs tasks (touches only BSOT + contract/payload docs); can run any time after Q3 (resolved). Mind doc-file merge order vs 18.0/18.3 which also edit BSOT.
7. Task 18.6 (gateway + Postman + docs), last; depends on 18.1-18.4 landing. parallel-safe: no.

## Progress tracker
> Orchestrator bookkeeping, informational only, NOT audit evidence. /audit-day re-verifies independently.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 18.0 | done | APPROVE | 2026-06-30 | Snapshot extension reviewed APPROVE; build/format/tests green; human `/verify` pending |
| 18.1 | done | APPROVE | 2026-06-30 | Q4 recorded; review APPROVE; build/format/unit tests green (205/205); full integration blocked by local PostgreSQL; human `/verify` pending |
| 18.2 | done | APPROVE | 2026-06-30 | Q5 recorded; shared BoardingController created; review APPROVE; build/format/tests green (Unit 206/206, Integration 37/37); human `/verify` pending |
| 18.3 | done | APPROVE | 2026-06-30 | APPROVE after 1 patch round (required Idempotency-Key); build/format/tests green (252/252); Q7 BSOT note broadened; human `/verify` pending |
| 18.4 | done | APPROVE | 2026-06-30 | QR scan read-only route added; review APPROVE; build/format/tests green (221 unit + 41 integration); human `/verify` pending |
| 18.5 | done | APPROVE | 2026-06-30 | Event contract registered with frozen 10-field schema; markdown/diff/EOL checks green; emitter deferred to Day 24 |
| 18.6 | todo | - | - | add /v1/bookings/trips -> BOOKING (DRIVER/ASSISTANT) + add requiredRoles to /v1/driver + /v1/assistant |

Legend: todo / in progress / done (reviewer APPROVED + human /verify) / done-with-carryover / blocked

## Open questions
Remaining NON-GATING refinements only (gating Q1/Q2/Q3/Q6/Q7 resolved — see Resolved decisions). Resolve before the noted task dispatches. Do NOT guess.

4. **RESOLVED:** from/to use ICT (UTC+7) date bounds inclusive at both ends. If both are omitted, default to today..today+14 days. If exactly one is supplied, return 422 VALIDATION_ERROR.
5. **RESOLVED:** terminal-pickup bookings with `pickupStationId` set and `pickupStopId` NULL sort first as the origin terminal with effective `orderIndex = 0`.
