# BE Gaps for Mobile — Handoff from Passenger alignment (BE v1.62.1)

**Audience:** Backend team
**From:** Mobile Passenger app alignment batch (Grok docs)
**Mobile baseline:** `d6d7b05`
**Backend baseline:** local implementation baseline `995310b9` on `codex/fix-be-gaps-for-mobile`
**Created:** 2026-08-07
**Last updated:** 2026-08-07

Mobile will **not** implement workarounds that invent bookability, fake routes, bulk mark-all, or destination stop ids. This document is the sole BE work request list from the Grok Mobile batch.

### Status vocabulary

Status values:

- **`BLOCKED_BE`** — backend contract/code/evidence is incomplete.
- **`READY_FOR_MOBILE_VERIFY`** — backend contract/code/evidence is complete; Mobile re-verification is pending.
- **`VERIFIED`** — Mobile has re-verified the shipped backend contract.

Tracker fields used below:
`ID | Priority | Capability | Source of truth | Status | Dependencies | Acceptance criteria | Evidence | Last updated`

Plus mandatory detail blocks: **current behavior**, **Mobile impact**, **required BE behavior**, **security/concurrency**, **acceptance evidence**, **Mobile capability unblocked**.

### Explicit non-gaps (do not file)

| Topic | Why excluded |
|---|---|
| Avatar reset | BE already supports `avatarUrl: null` |
| Province/City | Contract already matches Mobile |
| Parcel public contract | Already aligned for current Passenger flows |
| Shuttle public tracking/context | Already aligned for current Passenger flows |
| Wallet | Contract already matches |
| History payment redirect | Contract already matches |

---

## Tracker summary

| ID | Priority | Capability | Source of truth | Status | Dependencies | Acceptance criteria | Evidence | Last updated |
|---|---|---|---|---|---|---|---|---|
| `TRIP-BE-001` | P0 | Search only returns bookable trips or authoritative bookability | Trip search + tests | `READY_FOR_MOBILE_VERIFY` | Create Booking SCHEDULED-only rule | No BOARDING-only ghost trips without bookability signal; no Mobile N+1 detail | Trip integration suite on PostgreSQL + Redis: 317 passed | 2026-08-07 |
| `BOOK-BE-001` | P0 | Round-trip return route identity enforced server-side | Booking round-trip handler + tests | `READY_FOR_MOBILE_VERIFY` | Outbound `ReturnRouteId` present | `returnTrip.RouteId == outboundTrip.ReturnRouteId` before seat lock | Booking integration suite on PostgreSQL: 244 passed | 2026-08-07 |
| `BOOK-BE-002` | P1 | Leg-scoped seat conflict fields on RT 409 | Trip lock + Booking HTTP propagation | `READY_FOR_MOBILE_VERIFY` | Existing 409 envelope | Structured outbound/return seat lists; top-level envelope unchanged | Booking integration suite on PostgreSQL: 244 passed | 2026-08-07 |
| `TRK-BE-001` | P0 | Route geometry/stops/ETag use effective route when alternative assigned | Trip + Tracking route geometry | `READY_FOR_MOBILE_VERIFY` | `AlternativeRouteId` on Trip | Geometry+ETag follow effective route; ETag changes on reassignment | Trip integration suite on PostgreSQL + Redis: 317 passed | 2026-08-07 |
| `TRK-BE-002` | P1 | Canonical destination ETA identity | Tracking ETA + effective route | `READY_FOR_MOBILE_VERIFY` | Operational next-stop ETA already exists | Response discriminates intermediate stop vs destination station | Tracking E2E: 84 passed, including real Redis STATION ETA | 2026-08-07 |
| `HIST-BE-001` | P1 | History tracking target as discriminated union | Booking + Parcel history projections | `READY_FOR_MOBILE_VERIFY` | Ticket + Parcel history where tracking applies | `stopId` **or** `stationId` target; no name matching | Parcel integration suite on PostgreSQL: 84 passed | 2026-08-07 |
| `NOTIF-BE-001` | P1 | Atomic user-scoped read-all | Notification controller/service/repository | `READY_FOR_MOBILE_VERIFY` | Redis + Prisma transaction | UUID-v4 idempotency, no-op empty, server cutoff, `markedCount`+`readAt` | Notification E2E: 24 passed, including real PostgreSQL + Redis | 2026-08-07 |
| `NOTIF-BE-002` | P2 | Stable notification pagination under inserts | Notification cursor implementation | `READY_FOR_MOBILE_VERIFY` | Existing page contract | Snapshot keyset cursor with deterministic `createdAt,id` order | Notification E2E: 24 passed, including interleaved-insert snapshot paging | 2026-08-07 |
| `RAG-BE-001` | P3 | Swagger matches runtime `RAG_RATE_LIMIT_EXCEEDED` | RAG Swagger annotation | `READY_FOR_MOBILE_VERIFY` | Mobile already maps runtime code | Docs show `RAG_RATE_LIMIT_EXCEEDED` not generic `RATE_LIMIT_EXCEEDED` | Full TS build/test/lint passed | 2026-08-07 |
| `OPR-BE-001` | P1 | Reactivate a suspended operator | Identity lifecycle + migration | `READY_FOR_MOBILE_VERIFY` | Existing suspend flow and ActivityLog | `SUSPENDED -> APPROVED`, preserve subscription and require a fresh login | Identity integration suite on PostgreSQL: 179 passed | 2026-08-07 |

---

## TRIP-BE-001 — Search bookability vs Create Booking (P0)

| Field | Value |
|---|---|
| **ID** | `TRIP-BE-001` |
| **Priority** | P0 |
| **Capability** | Ticket search does not present trips that Create Booking will reject solely for lifecycle status — or search exposes authoritative bookability without Mobile N+1 |
| **Source of truth** | `apps/trip/src/VietRide.Trip.Application/Features/Trips/SearchTrips/SearchTripsHandler.cs` (filters `SCHEDULED \|\| BOARDING`); Booking create path accepts bookable `SCHEDULED` trips only (Passenger create contract) |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Product decision: hide non-bookable vs show with `isBookable`/`status` |
| **Acceptance criteria** | See required BE behavior |
| **Evidence** | BE commit `2e68a8b`; handler lines filter `TripStatus.SCHEDULED \|\| TripStatus.BOARDING`; Mobile search mapper does not receive bookability flag (`trip.ts` `TripSearchDto`) |
| **Last updated** | 2026-08-07 |

### Current behavior

- Public ticket search includes trips in **`BOARDING`** as well as `SCHEDULED`.
- Passenger Create Booking still requires a bookable scheduled trip; selecting a search result that is already `BOARDING` fails later in the funnel.
- Search item DTO (`SearchTripItem`) has no `status` / `isBookable` field today.

### Mobile impact

- Users can open a trip card that looks bookable and fail only at seat lock / create.
- Mobile **must not** call trip-detail for every search row to discover status (N+1 ban in Mobile plan).
- Without BE fix, Mobile can only show all results and surface a late error — poor UX, not a security bypass.

### Required BE behavior

Prefer one of:

1. **Filter:** search returns only `SCHEDULED` (and any other statuses product explicitly allows for booking), **or**
2. **Signal:** keep multi-status listing but return authoritative `status` and/or `isBookable` on each search item so Mobile can disable CTA without detail fan-out.

Do **not** require Mobile to N+1 `GET /trips/{id}` for every card.

### Security / concurrency

- Bookability must remain enforced on Create Booking / seat lock regardless of search presentation (defense in depth).
- No change to Operator surfaces required for this Passenger gap.

### Acceptance evidence

- Contract test: search page never returns a trip that create rejects **only** for lifecycle, **or** each such trip carries `isBookable=false` / non-bookable status.
- Gateway e2e sample: BOARDING trip either absent or non-actionable in Passenger search payload.

### Mobile capability unblocked

- Honest search → seat flow without surprise `BOOKING_TRIP_NOT_BOOKABLE` / status conflicts for BOARDING-only listings.
- Complements `MO-FARE-01` / booking UX polish (not a Mobile code blocker for effective fare).

---

## BOOK-BE-001 — Round-trip return route verification (P0)

| Field | Value |
|---|---|
| **ID** | `BOOK-BE-001` |
| **Priority** | P0 |
| **Capability** | Server rejects crafted round-trip pairs whose return route is not the outbound route’s configured return |
| **Source of truth** | `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs` @ `2e68a8b` |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Outbound trip exposes `ReturnRouteId` (already used for `ROUTE_RETURN_NOT_CONFIGURED`) |
| **Acceptance criteria** | Verify before seat lock / payment side effects |
| **Evidence** | Handler checks `outboundTrip.ReturnRouteId is null` and return departure ordering, but **does not** assert `returnTrip.RouteId == outboundTrip.ReturnRouteId` before `LockRoundTripSeatsAsync` |
| **Last updated** | 2026-08-07 |

### Current behavior

- Validates outbound has a configured `ReturnRouteId`.
- Validates return departs after outbound arrival.
- Locks seats on whatever return `tripId` the client sent without proving that trip’s `RouteId` equals `outboundTrip.ReturnRouteId`.

### Mobile impact

- Honest Mobile already prefers server `returnRouteId` when constraining return search, but a **modified client** can pair arbitrary reverse trips.
- Mobile cannot close this security hole client-side.

### Required BE behavior

Before seat lock / payment:

```text
returnTrip.RouteId == outboundTrip.ReturnRouteId
```

On failure: stable 4xx conflict/validation code (document in contract), **no** partial seat holds.

### Security / concurrency

- This is a **server authorization / integrity** check against client-supplied trip ids.
- Must run on the same transactional path as atomic round-trip lock (no TOCTOU after lock if avoidable; at minimum fail closed before lock).

### Acceptance evidence

- Unit/integration: mismatched return route → rejected; matched route → proceeds.
- Attempt with two unrelated routes never creates holds/bookings.

### Mobile capability unblocked

- Safe round-trip product claim; Mobile keeps UX constraints but does not own integrity.

---

## BOOK-BE-002 — Leg-scoped `BOOKING_SEAT_UNAVAILABLE` (P1)

| Field | Value |
|---|---|
| **ID** | `BOOK-BE-002` |
| **Priority** | P1 |
| **Capability** | Round-trip seat conflict tells Mobile which leg/trip/seats failed without parsing free text |
| **Source of truth** | `CreateRoundTripBookingCommandHandler.cs` seat-unavailable branch; Trip round-trip lock outcome |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Existing top-level error envelope (`error.code`, `error.message`) |
| **Acceptance criteria** | Structured per-leg fields; envelope top-level unchanged |
| **Evidence** | Current throw joins unavailable seat labels into message string only |
| **Last updated** | 2026-08-07 |

### Current behavior

```text
409 BOOKING_SEAT_UNAVAILABLE
message: "One or more seats are unavailable: A4, B2, ..."
```

No machine-readable split of outbound vs return, no trip ids in structured fields.

### Mobile impact

- `MO-SEAT-01` must **conservatively mark both legs** when attribution is missing.
- Same seat label on both legs cannot be disambiguated from message text alone.

### Required BE behavior

Keep top-level envelope (`code`, `message`). Add structured detail (example shape — BE may name fields consistently with ADR conventions):

```json
{
  "error": {
    "code": "BOOKING_SEAT_UNAVAILABLE",
    "message": "...",
    "details": {
      "outbound": { "tripId": "...", "seatNumbers": ["A4"] },
      "return": { "tripId": "...", "seatNumbers": ["A4"] }
    }
  }
}
```

Empty array / omit leg when that leg had no conflicts. One-way can use a single leg object or existing shape extended consistently.

### Security / concurrency

- Do not leak seats of other passengers; only echo the requester’s conflicting seat numbers.
- Idempotency: definitive 409 must remain non-retryable with the same key (Mobile already rotates key).

### Acceptance evidence

- Contract test RT lock conflict returns leg fields.
- Mobile can navigate only the conflicted leg when one leg fails.

### Mobile capability unblocked

- Precise `seatConflictLegs` + CTA “Chọn lại ghế chiều đi/về” without dual-leg false positives.

---

## TRK-BE-001 — Effective route geometry when AlternativeRoute assigned (P0)

| Field | Value |
|---|---|
| **ID** | `TRK-BE-001` |
| **Priority** | P0 |
| **Capability** | Public route geometry / stops / ETag reflect the **effective** route after alternative assignment |
| **Source of truth** | Trip route-change / `AlternativeRouteId` domain; Tracking public `route-geometry` consumption of Trip internal geometry |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Existing public geometry contract (auth, private cache, strong ETag) |
| **Acceptance criteria** | Same public contract; content + ETag track effective assignment |
| **Evidence** | Alternative route lifecycle exists under `apps/trip/.../AlternativeRoutes` and route-change services; Mobile must not synthesize geometry |
| **Last updated** | 2026-08-07 |

### Current behavior

- Trips can be reassigned to an alternative route.
- If public geometry/stops still follow the original route after assignment, map and ETA context lie to the passenger.
- ETag must change when the effective route assignment changes so Mobile `If-None-Match` revalidates.

### Mobile impact

- Mobile renders BE geometry only; will **not** invent polylines or stop lists.
- Stale geometry after route change = wrong markers / planned path (`MO-TRK-01` / map shell).

### Required BE behavior

- When Trip has `AlternativeRouteId` (or equivalent effective assignment), geometry + ordered stops served to Tracking/public geometry use that effective route.
- Strong ETag (or equivalent validators) **must change** when assignment changes even if URL stays the same.
- Keep existing auth / `403/404/304` matrix; no new Passenger Operator endpoints.

### Security / concurrency

- Authorization remains booking/trip ownership as today.
- Do not leak internal proposal/audit payloads on the public geometry DTO.

### Acceptance evidence

- Before/after alternative assignment: geometry payload differs; ETag differs; `304` only when unchanged.
- Integration test with assigned alternative.

### Mobile capability unblocked

- Trustworthy map-first tracking after operational route changes without Mobile hacks.

---

## TRK-BE-002 — Canonical destination ETA (P1)

| Field | Value |
|---|---|
| **ID** | `TRK-BE-002` |
| **Priority** | P1 |
| **Capability** | ETA API distinguishes intermediate stop vs destination station so Mobile does not forge a stopId for destination |
| **Source of truth** | Tracking `GET /v1/tracking/trips/{tripId}/eta`; trip stops are intermediate-only in several contracts |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Operational next-stop without `stopId` already available (Mobile `MO-TRK-01`) |
| **Acceptance criteria** | Discriminated target identity in response and/or query |
| **Evidence** | Mobile parser requires UUID stopId for target; destination often station-scoped in booking history |
| **Last updated** | 2026-08-07 |

### Current behavior

- Target ETA is stop-scoped.
- Destination is a **station**, not necessarily a `trip_stops` row.
- Mobile is forbidden from inventing a synthetic stopId for destination.

### Mobile impact

- Passenger “ETA to my destination” may be missing or incorrectly bound to an intermediate stop.
- `MO-TRK-01` can still ship operational next-stop + intermediate target; destination target remains incomplete.

### Required BE behavior

Response (and query if needed) must discriminate:

- Intermediate: `targetKind: "STOP"`, `stopId`
- Destination: `targetKind: "STATION"`, `stationId` (and tripId)

Do not force Mobile to invent stop ids. Keep nullable ETA semantics when cache cold.

### Security / concurrency

- Same ownership auth as current ETA.
- No Haversine obligation on Mobile; BE remains ETA authority.

### Acceptance evidence

- Contract samples for stop vs destination targets.
- 400 on mismatched kind/id pairs.

### Mobile capability unblocked

- Accurate dual-row presentation when next stop ≠ destination station; clean target query enablement.

---

## HIST-BE-001 — History canonical tracking target (P1)

| Field | Value |
|---|---|
| **ID** | `HIST-BE-001` |
| **Priority** | P1 |
| **Capability** | Unified Passenger History returns a discriminated tracking target for Ticket and Parcel when tracking applies |
| **Source of truth** | Passenger history endpoints (e.g. parcel `PassengerHistoryController` and booking history projections) |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | `TRK-BE-002` complementary for ETA kinds |
| **Acceptance criteria** | Discriminated `stopId` **or** `stationId`; never name-only matching |
| **Evidence** | Mobile currently may only have names/stations from history cards; cannot safely derive stop UUID by string match |
| **Last updated** | 2026-08-07 |

### Current behavior

- History items expose human labels and some ids, but not a single canonical “open tracking focused on this target” structure for both intermediate pickup and destination.

### Mobile impact

- Tracking deep-link from history cannot set `targetEta` stopId reliably.
- Name matching is explicitly forbidden in Mobile rules.

### Required BE behavior

For Ticket and Parcel history rows where tracking applies:

```ts
trackingTarget:
  | { kind: 'STOP'; stopId: Uuid }
  | { kind: 'STATION'; stationId: Uuid }
  | null
```

(Field names may follow BE DTO conventions; semantics must match.)

### Security / concurrency

- Only return targets the caller is authorized to track.
- No cross-user stop/station leakage.

### Acceptance evidence

- History payload fixtures for intermediate pickup booking vs terminal destination.
- Parcel equivalent when service point / station tracking applies.

### Mobile capability unblocked

- History → Tracking with correct target query enablement (`MO-TRK-01`).

---

## NOTIF-BE-001 — Atomic user-scoped read-all (P1)

| Field | Value |
|---|---|
| **ID** | `NOTIF-BE-001` |
| **Priority** | P1 |
| **Capability** | One authenticated call marks all of the user’s unread notifications read, safely |
| **Source of truth** | `apps/notification/src/notifications/notifications.controller.ts` (+ service/repository) — per-id read only at `2e68a8b` |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Existing UUID-v4 idempotency middleware patterns |
| **Acceptance criteria** | See required BE behavior |
| **Evidence** | Mobile currently hides mark-all and must not `Promise.all` multi-read (`MO-NOTIF-01`) |
| **Last updated** | 2026-08-07 |

### Current behavior

- `POST /notifications/{id}/read` only.
- No atomic read-all.

### Mobile impact

- Mark-all UI must stay **fully hidden**.
- Client-side loops are banned (partial failure, race with FCM inserts, rate limits).

### Required BE behavior

- New user-scoped read-all endpoint (exact path per BE conventions).
- **UUID-v4** `Idempotency-Key` required.
- Successful **no-op** when zero unread (still 2xx).
- **Server-side cutoff** timestamp so notifications created after the cutoff are not marked read by a stale request.
- Response minimum: `markedCount`, `readAt` (cutoff or completion time — document precisely).

### Security / concurrency

- Strictly **current user** scope; never mark another user’s rows.
- Idempotent under retries; concurrent read-all + single-read must not corrupt `readAt`.
- Rate-limit as appropriate; document code.

### Acceptance evidence

- E2E: N unread → markedCount=N; second identical idempotent call → markedCount=0 or stable replay.
- Insert during request: post-cutoff items remain unread.

### Mobile capability unblocked

- Re-enable “Đánh dấu tất cả” in `MO-NOTIF-01` follow-up.

---

## NOTIF-BE-002 — Stable pagination under inserts (P2)

| Field | Value |
|---|---|
| **ID** | `NOTIF-BE-002` |
| **Priority** | P2 |
| **Capability** | List pagination does not skip/duplicate unpredictably when new notifications arrive mid-scroll |
| **Source of truth** | Notification list query DTO/sort (`list-notifications-query.dto.ts`, repository ordering) |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Mobile infinite query (`MO-NOTIF-01`) still ships with residual risk documented |
| **Acceptance criteria** | Deterministic total order; long-term cursor/snapshot |
| **Evidence** | Offset `page` + `createdAt` desc alone can skip items when inserts land on page boundaries |
| **Last updated** | 2026-08-07 |

### Current behavior

- Classic page/pageSize offset pagination.
- Sort options include `createdAt` but tie-breaking by id is not guaranteed for Mobile’s no-skip requirement.

### Mobile impact

- Infinite scroll can skip or duplicate under high insert rate (FCM storms).
- Mobile dedupes by id in memory but **cannot** guarantee no-skip with pure offset.

### Required BE behavior

- **Near term:** stable sort `createdAt DESC, id DESC` (or ASC pair documented).
- **Long term:** opaque cursor or snapshot pagination.

### Security / concurrency

- Pagination must remain user-scoped; cursors must not be forgeable across users.

### Acceptance evidence

- Concurrent insert test while paging does not drop pre-existing ids under stable sort (cursor preferred for hard guarantee).

### Mobile capability unblocked

- Stronger infinite-list guarantees beyond best-effort dedupe.

---

## RAG-BE-001 — Swagger/docs error code for RAG rate limit (P3)

| Field | Value |
|---|---|
| **ID** | `RAG-BE-001` |
| **Priority** | P3 |
| **Capability** | Public docs/Swagger document the **runtime** error code Mobile already handles |
| **Source of truth** | Runtime: Mobile maps `RAG_RATE_LIMIT_EXCEEDED` (`useChatSession.ts`); docs mention rate limits under mixed names (`docs/api/rag-service-integration.md` lists `RAG_RATE_LIMIT_EXCEEDED`; some Swagger surfaces still risk generic `RATE_LIMIT_EXCEEDED`) |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | None for Mobile runtime |
| **Acceptance criteria** | Swagger + contract show `RAG_RATE_LIMIT_EXCEEDED` for RAG 429 |
| **Evidence** | Mobile chatbot tests assert runtime code `RAG_RATE_LIMIT_EXCEEDED`; this is a **docs contract gap**, not a Mobile bug |
| **Last updated** | 2026-08-07 |

### Current behavior

- Runtime returns `RAG_RATE_LIMIT_EXCEEDED` (Mobile correct).
- Documentation/Swagger may still advertise `RATE_LIMIT_EXCEEDED` for the same path in some surfaces.

### Mobile impact

- None if runtime stays stable. Confusion for future clients/QA.

### Required BE behavior

- Align Swagger/OpenAPI and any published contract tables with **`RAG_RATE_LIMIT_EXCEEDED`**.
- Do not rename runtime without a versioned Mobile migration.

### Security / concurrency

- N/A beyond existing rate-limit behavior.

### Acceptance evidence

- Generated Swagger snippet for RAG 429 shows the runtime code.

### Mobile capability unblocked

- Docs parity only; no Mobile feature gate.

---

## OPR-BE-001 — Reactivate suspended operator (P1)

| Field | Value |
|---|---|
| **ID** | `OPR-BE-001` |
| **Priority** | P1 |
| **Capability** | System Admin can restore a suspended operator without changing its subscription |
| **Source of truth** | Identity Operator lifecycle and `AdminOperatorsController` |
| **Status** | `READY_FOR_MOBILE_VERIFY` |
| **Dependencies** | Existing suspend flow, refresh-token revocation and ActivityLog |
| **Target status** | `SUSPENDED -> APPROVED` only |
| **Acceptance criteria** | `isActive=true`; subscription, `approvedAt` and suspension metadata unchanged; revoked sessions remain revoked |
| **Evidence** | Identity integration suite on PostgreSQL: 179 passed; lifecycle endpoint suite: 12 passed; Gateway route suite: 58 passed |

### Required BE behavior

- Add `POST /v1/admin/operators/{operatorId}/reactivate` for `SYSTEM_ADMIN`, empty body and
  UUID-v4 `Idempotency-Key`.
- Reject every source state except `SUSPENDED` with `422 VALIDATION_ERROR`.
- Record both suspend and reactivate operations in ActivityLog with actor, operator ID and source.
- Do not emit a new integration event or notification. The operator must log in again because
  refresh tokens revoked during suspension are not restored.

### Acceptance evidence

- Happy path and invalid-state/role tests.
- ActivityLog rows exist for suspend and reactivate.
- Subscription snapshot is unchanged and a fresh login succeeds after reactivation.

---

## Backend verification — 2026-08-07

- Full TypeScript matrix passed without Nx cache: lint, tests and builds for Gateway, Tracking,
  Notification, RAG and shared libraries.
- Full .NET builds passed for all solutions. Full unit suites passed: Trip 665, Identity 344,
  Booking 599, Payment 219 and Parcel 448 tests.
- `dotnet format --verify-no-changes` passed for all six .NET solutions; the Identity migration has
  no pending EF model changes.
- Both new mutations are registered in the executable idempotency inventory. The standalone
  inventory audit still reports pre-existing Trip endpoint/RabbitMQ/outbound-callsite drift from
  baseline `995310b9`; it no longer reports either Mobile-gap endpoint.
- Docker-backed integration suites passed against real service dependencies: Identity 179/179,
  Trip 317/317, Booking 244/244 and Parcel 84/84 on PostgreSQL; Trip also used Redis.
- Notification E2E passed 24 tests (2 unrelated conditional tests skipped), including real
  PostgreSQL + Redis coverage for read-all cutoff,
  idempotent retry, post-cutoff inserts, Redis TTL and snapshot cursor pagination with an insert
  between pages. Tracking E2E passed 84 tests (2 unrelated conditional tests skipped), including
  real Redis coverage for STATION ETA.
- The rebuilt local stack is healthy on all 14 direct/proxied health endpoints. A tampered Internal
  JWT is rejected with 401, and RabbitMQ exposes `vietride.events`, retry and DLQ exchanges.
- Two stale Booking happy-path fixtures and two stale Notification E2E expectations were aligned
  with the new contracts discovered by these runs; all affected suites passed after correction.
- Mobile must still re-verify the wire contracts before changing any row to `VERIFIED`.

---

## Suggested BE implementation order

1. `TRIP-BE-001`, `BOOK-BE-001`, `TRK-BE-001` (P0 integrity / honesty)
2. `BOOK-BE-002`, `TRK-BE-002`, `HIST-BE-001`, `NOTIF-BE-001`, `OPR-BE-001` (P1 UX contracts)
3. `NOTIF-BE-002` (P2 pagination hardening)
4. `RAG-BE-001` (P3 docs)

---

## Cross-links

| Doc | Role |
|---|---|
| [GROK_MOBILE_V1_62_1_IMPLEMENTATION_PLAN.md](./GROK_MOBILE_V1_62_1_IMPLEMENTATION_PLAN.md) | Mobile four-slice plan + tracker |
| [PLAN.md](./PLAN.md) | Tracking program plan (BE baseline v1.62.1) |

**Note:** Mobile will not commit/push or change BE repositories in the Grok batch that produced this handoff.
