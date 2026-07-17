# Day 23 — Schedule change 3 levels + BookingPendingAction

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 23 — Schedule change 3 levels + BookingPendingAction (SCV-100)
- **Prior checklist**: `docs/handoff/day-22-checklist.md`
- **Plan status**: `APPROVED`
  <!-- Allowed lifecycle: DRAFT | REVISION-REQUIRED | REVIEWER-APPROVED — AWAITING HUMAN | APPROVED -->

## Objective

Finish schedule-change handling through the existing DriverSchedule `ALL_PENDING` mutation and the existing passenger pending-action resolver. Add a mutable Booking departure projection without changing historical snapshots, make passenger rejection/refunds and timeout acceptance transactional, and preserve event identity through Outbox and RabbitMQ. Reconcile the lower-priority Day-23 timeline/API/BSOT text before code work.

## Success criteria (DoD — binary, verifiable)

- [ ] No dedicated Trip schedule endpoint or Gateway route exists; `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=ALL_PENDING` remains the only schedule-change producer.
- [ ] Same-ICT-date delta `<=2h` is MINOR, `>2h && <6h` is MEDIUM, and `>=6h` or an ICT date change is MAJOR.
- [ ] `ALL_PENDING` with a `CONFIRMED` Booking permits exact two-hour equality but rejects when either the old or computed new departure is less than two hours from the one captured clock.
- [ ] `trip_snapshot_departure` remains immutable; `trip_current_departure` is backfilled, indexed, causally/CAS-sequenced by schedule events, and used for STOP_DISABLED deadlines. Existing `date` and `sortBy=departureAt` query keys operate on the current projection, with the existing same-direction `id` tie-breaker; nested `trip.currentDepartureAt` is returned beside immutable `trip.departureAt`, with no top-level duplicate or new sort key.
- [ ] The Booking consumer updates `PENDING_PAYMENT|CONFIRMED` projections, but only `CONFIRMED` creates informational facts or one active `SCHEDULE_CHANGE` action. Duplicate and out-of-order events obey `current==old` apply, `current==new` duplicate, otherwise retry/quarantine.
- [ ] Only the Booking owner can resolve a `SCHEDULE_CHANGE`; UUID-v4 idempotency, equality eligibility, masking, replay, and every exact error mapping below are covered. No accept/reject aliases or operator seat-assignment behavior is introduced.
- [ ] Rejection calculates 50%/100% from immutable `Booking.totalAmount` with `MidpointRounding.AwayFromZero`, freezes basis/percent/amount metadata, and atomically resolves the action, cancels the Booking, records history, and emits exactly one authoritative `booking.booking.cancelled.refundAmount`.
- [ ] Existing Day-22 `PendingActionRealertJob` remains unchanged: at action occurrence `+2h`, it re-alerts unresolved `PENDING_SEAT_ASSIGNMENT` and MEDIUM/MAJOR `SCHEDULE_CHANGE` at most once for that intended phase. Separate Day-23 `ScheduleChangeAutoAcceptJob` handles `initialDeadline + 1s` and, only when MAJOR has `initialDeadline < terminalDeadline`, the optional initial-phase re-alert plus `terminalDeadline + 1s` final acceptance; direct/final timeout never auto-refunds and equality remains passenger-eligible.
- [ ] `booking.booking.pending_action_auto_resolved` is emitted only for MEDIUM timeout and MAJOR terminal/direct timeout. Every Day-23 event satisfies `payload.eventId == outbox_events.id == RabbitMQ MessageId`.
- [ ] Notification maps required/re-alerted/auto-resolved schedule facts to existing `TRIP_SCHEDULE_CHANGED`, dedupes redelivery, and requires no Prisma migration.
- [ ] Focused real-PostgreSQL and Gateway evidence covers the producer, projection, action, refund, timeout phases, event envelopes, compatibility fallback, and cleanup; `/audit-day 23` alone owns full regression.

## Contract changes

- Reuse `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`; delete the proposed `/operator/trips/{id}/schedule` and `/accept|reject` aliases from lower-priority text. No Gateway route change.
- `POST /v1/bookings/{bookingId}/pending-actions/{actionId}/resolve` is authenticated `PASSENGER` only, owner only, UUID-v4 idempotent, and limited to persisted reason `SCHEDULE_CHANGE`. Request is exactly `{ action: ACCEPTED|REJECTED, note? }`; `selectedStopId` is invalid. Operator seat assignment remains a future route/DTO/handler whose path is not ratified in Day 23.
- Endpoint errors are decision-complete:

  | HTTP | Error code | Exact trigger / masking rule |
  |---|---|---|
  | 401 | `AUTH_TOKEN_INVALID` | Missing, invalid, or expired user JWT. |
  | 403 | `FORBIDDEN` | Valid JWT whose role is not `PASSENGER`; reject before Booking/action lookup. |
  | 404 | `BOOKING_NOT_FOUND` | Booking is missing/not owned, or a discovered booking/action ownership mismatch must be masked before action state is revealed. |
  | 404 | `BOOKING_PENDING_ACTION_NOT_FOUND` | Booking was found and owner-authorized, but `{actionId}` does not exist under that Booking. |
  | 409 | `BOOKING_PENDING_ACTION_NOT_RESOLVABLE` | Active action exists, but its persisted reason/state or Booking state does not support this Day-23 resolution. |
  | 409 | `BOOKING_PENDING_ACTION_SUPERSEDED` | A new idempotency key targets an action terminally resolved as `SUPERSEDED`. |
  | 409 | `BOOKING_PENDING_ACTION_ALREADY_RESOLVED` | A new idempotency key targets an `ACCEPTED` or `REJECTED` action. |
  | 409 | `BOOKING_PENDING_ACTION_EXPIRED` | The passenger request is strictly after the effective cutoff; timeout owns the outcome and never refunds. Equality remains eligible. |
  | 409 | `IDEMPOTENCY_REQUEST_PENDING` | Same key/fingerprint is still executing. |
  | 422 | `IDEMPOTENCY_KEY_REQUIRED` | Required header absent. |
  | 422 | `IDEMPOTENCY_KEY_MISMATCH` | Same key with a different actor/method/path/query/raw-body fingerprint. |
  | 422 | `VALIDATION_ERROR` | Malformed/non-v4 key; malformed route UUID; missing/invalid `action`; `selectedStopId` present; or another request-shape failure. |

  A valid same-key/same-payload request replays the byte-identical stored response even after the action becomes terminal. Only a new key reaches the terminal conflicts above.
- Add nullable Booking column `trip_current_departure TIMESTAMPTZ`, backfill it from `trip_snapshot_departure`, index it as `idx_bookings_trip_current_departure`, and map `TripCurrentDeparture`. Query keys remain unchanged: `date` filters the ICT calendar-day half-open interval over `trip_current_departure`; existing `sortBy=departureAt` orders by `trip_current_departure`, then by `id` in the same direction as `sortDir`; there is no `currentDepartureAt` sort key. List and detail responses add nested `trip.currentDepartureAt` beside immutable `trip.departureAt`, with no top-level duplicate.
- Freeze schedule metadata as `sourceEventId`, `oldDeparture`, `newDeparture`, `severity`, `initialDeadline`, nullable `terminalDeadline`, `refundBasisAmount`, `refundPercent`, and `refundAmount`. The immutable basis is `Booking.totalAmount`.
- Preserve the existing Day-22 `PendingActionRealertJob` schedule and scope unchanged: action occurrence `+2h` for unresolved `PENDING_SEAT_ASSIGNMENT` and MEDIUM/MAJOR `SCHEDULE_CHANGE`. Day-23 timeout work is separate: `ScheduleChangeAutoAcceptJob` runs at `initialDeadline + 1s`; MEDIUM finalizes there, while MAJOR with `initialDeadline < terminalDeadline` may emit one distinct initial-phase re-alert and schedules final acceptance at `terminalDeadline + 1s`. Lag already beyond the terminal deadline accepts directly without that optional phase; `initialDeadline >= terminalDeadline` never emits it. Each intended re-alert phase has a distinct deterministic identity and is at most once under retries.
- Register `booking.booking.pending_action_auto_resolved` with exact payload `{eventId,occurredAt,bookingId,tripId,userId,pendingActionId,resolvedAction,severity,oldDeparture,newDeparture}`; `resolvedAction` is `ACCEPTED`.
- Extend `booking.booking.cancelled` with required fresh UUID-v4 `eventId` and producer-captured offset-date-time `occurredAt`. The shared TS contract exposes a strict canonical producer schema in which both fields are required and a separate one-release consumer union that accepts only either the complete canonical shape or the exact legacy shape with both fields absent; partial identity presence, malformed values, and extra fields fail. New producers switch together and never use the legacy branch; consumers fall back to `bookingId` only for an exact legacy payload.

For every task, the paths in its verification `$changed` arrays are the exact base write set. If documented auto-expand scope is used, the implementer must append each exact expanded path to the relevant format/lint and `git diff --check` arrays before review; directory-wide or implicit hygiene evidence is not accepted.

## Tasks

### Task 23.0 — Reconcile the Day-23 contract and source-of-truth

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | `BE_TIMELINE_VU.md`; `SU26SE101_VIETRIDE_technical_context_v7.md`; `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `db-schema/booking/schema.sql`; `db-schema/booking/README.md`; `db-schema/trip-route-vehicle/README.md` |
| auto-expand scope | Only matching Day-23 sections, exact registry/schema/index rows, BSOT version, and one BSOT changelog row in the seven owned documents. |
| forbidden scope | All `apps/**`, `libs/**`, migrations, scripts, Postman, `.env`, secrets, dependencies, unrelated SOT sections, unratified operator seat-assignment paths, destructive operations, and git operations. |
| depends on | none |
| parallel-safe | no — architecture gate for all implementation tasks. |
| verification tier | `DOCS` |
| verification commands | Run the exact DOCS block below. |
| full regression owner | `audit-day` |
| invariant flags | Markdown/SQL LF; SOT hierarchy; ADR 0004; exact lower-case past-tense routing keys; Money BIGINT/AwayFromZero; immutable snapshots; no cross-DB FK; no dependency change. |
| acceptance | Every owned document contains its own applicable canonical Day-23 values; a value in another file cannot satisfy its gate. The API table and BSOT §5.9 ratify exactly `401 AUTH_TOKEN_INVALID`, `403 FORBIDDEN`, `404 BOOKING_NOT_FOUND`, `404 BOOKING_PENDING_ACTION_NOT_FOUND`, `409 BOOKING_PENDING_ACTION_NOT_RESOLVABLE`, `409 BOOKING_PENDING_ACTION_SUPERSEDED`, `409 BOOKING_PENDING_ACTION_ALREADY_RESOLVED`, `409 BOOKING_PENDING_ACTION_EXPIRED`, `409 IDEMPOTENCY_REQUEST_PENDING`, and `422 IDEMPOTENCY_KEY_REQUIRED|IDEMPOTENCY_KEY_MISMATCH|VALIDATION_ERROR`, including the masking/replay triggers in Contract changes. The operator list contract keeps its query keys/allow-list unchanged: `date` uses the ICT day of `trip_current_departure`, `sortBy=departureAt` sorts that projection followed by `id` in `sortDir`, and list/detail place `currentDepartureAt` only under `trip` beside immutable `departureAt`. BSOT is `1.35.0` with a dated changelog row; §7.3 contains exact event rows; §10.1 preserves the existing occurrence+2h `PendingActionRealertJob` scope and defines the separate Day-23 `ScheduleChangeAutoAcceptJob` initial/terminal phases, each at most once for its intended phase. Booking DDL and README contain the exact column/backfill/index contract. There is no dedicated Trip endpoint, alias, new `currentDepartureAt` sort key, timeout auto-refund/auto-cancel/fallback wording, MAJOR-only narrowing of the Day-22 job, or `+3h`-as-MAJOR contradiction. |
| source citations | technical context §6.13 and Booking entity requirements; API contract around lines 1267 and 4539; BSOT §§4.3, 5.9, 7.3–7.4, 8.10, 10.1, 13; Day-23 timeline; Booking/Trip schema READMEs. |

```powershell
$docs=@(
  'BE_TIMELINE_VU.md',
  'SU26SE101_VIETRIDE_technical_context_v7.md',
  'VietRide_API_Contract_v1.md',
  'BACKEND_SOURCE_OF_TRUTH.md',
  'db-schema/booking/schema.sql',
  'db-schema/booking/README.md',
  'db-schema/trip-route-vehicle/README.md'
)
git diff --check -- $docs
if ($LASTEXITCODE -ne 0) { throw 'Day-23 document diff hygiene failed' }

function Require-Literal([string]$Path,[string]$Literal) {
  $text=Get-Content -Raw $Path
  if (-not $text.Contains($Literal)) { throw "Missing exact '$Literal' in $Path" }
}
function Require-Regex([string]$Path,[string]$Pattern) {
  $text=Get-Content -Raw $Path
  if ($text -notmatch $Pattern) { throw "Missing canonical pattern '$Pattern' in $Path" }
}
function Require-Severity([string]$Path) {
  Require-Regex $Path '(?is)(?:MINOR.{0,180}(?:<=|≤)\s*2\s*(?:h|giờ)|(?:<=|≤)\s*2\s*(?:h|giờ).{0,180}MINOR)'
  Require-Regex $Path '(?is)(?:MEDIUM.{0,220}(?:>|&gt;)\s*2\s*(?:h|giờ).{0,100}(?:<|&lt;)\s*6\s*(?:h|giờ)|(?:>|&gt;)\s*2\s*(?:h|giờ).{0,100}(?:<|&lt;)\s*6\s*(?:h|giờ).{0,220}MEDIUM)'
  Require-Regex $Path '(?is)(?:MAJOR.{0,220}(?:>=|≥)\s*6\s*(?:h|giờ).{0,220}(?:ICT|ngày|date)|(?:>=|≥)\s*6\s*(?:h|giờ).{0,220}(?:ICT|ngày|date).{0,220}MAJOR)'
}

# Per-document positives: no union search is allowed.
Require-Literal 'BE_TIMELINE_VU.md' 'PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`'
Require-Literal 'BE_TIMELINE_VU.md' 'POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`'
Require-Literal 'BE_TIMELINE_VU.md' 'trip_current_departure'
Require-Severity 'BE_TIMELINE_VU.md'

Require-Literal 'SU26SE101_VIETRIDE_technical_context_v7.md' 'PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`'
Require-Literal 'SU26SE101_VIETRIDE_technical_context_v7.md' 'POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`'
Require-Literal 'SU26SE101_VIETRIDE_technical_context_v7.md' 'trip_current_departure'
Require-Literal 'SU26SE101_VIETRIDE_technical_context_v7.md' 'refundBasisAmount'
Require-Severity 'SU26SE101_VIETRIDE_technical_context_v7.md'

Require-Literal 'VietRide_API_Contract_v1.md' '### PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`'
Require-Literal 'VietRide_API_Contract_v1.md' '### POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`'
Require-Literal 'VietRide_API_Contract_v1.md' '`currentDepartureAt`'
Require-Literal 'VietRide_API_Contract_v1.md' '`trip_current_departure`'
Require-Severity 'VietRide_API_Contract_v1.md'

Require-Literal 'BACKEND_SOURCE_OF_TRUTH.md' 'PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`'
Require-Literal 'BACKEND_SOURCE_OF_TRUTH.md' 'POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`'
Require-Literal 'BACKEND_SOURCE_OF_TRUTH.md' '`trip_current_departure`'
Require-Literal 'BACKEND_SOURCE_OF_TRUTH.md' '`currentDepartureAt`'
Require-Severity 'BACKEND_SOURCE_OF_TRUTH.md'

Require-Literal 'db-schema/booking/schema.sql' 'trip_current_departure TIMESTAMPTZ NULL'
Require-Literal 'db-schema/booking/schema.sql' 'CREATE INDEX idx_bookings_trip_current_departure ON bookings (trip_current_departure DESC);'
Require-Literal 'db-schema/booking/README.md' '`trip_current_departure`'
Require-Literal 'db-schema/booking/README.md' '`idx_bookings_trip_current_departure`'
Require-Regex 'db-schema/booking/README.md' '(?is)trip_snapshot_departure.{0,220}(?:immutable|bất biến).{0,220}trip_current_departure'
Require-Literal 'db-schema/trip-route-vehicle/README.md' 'PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`'
Require-Regex 'db-schema/trip-route-vehicle/README.md' '(?is)oldDeparture.{0,200}(?:>=|≥)\s*2h.{0,200}newDeparture.{0,200}(?:>=|≥)\s*2h'

$api=Get-Content -Raw 'VietRide_API_Contract_v1.md'
$operatorList=[regex]::Match($api,'(?ms)^### GET `/v1/operator/bookings`.*?(?=^### )').Value
$operatorDetail=[regex]::Match($api,'(?ms)^### GET `/v1/operator/bookings/\{id\}`.*?(?=^### )').Value
if ([string]::IsNullOrWhiteSpace($operatorList)) { throw 'Operator booking list section not found' }
if ([string]::IsNullOrWhiteSpace($operatorDetail)) { throw 'Operator booking detail section not found' }

$dateRow=[regex]::Match($operatorList,'(?m)^\| `date` \|.*$').Value
if ($dateRow -notmatch 'ICT|Asia/Ho_Chi_Minh' -or $dateRow -notmatch '\[fromUtc, toUtc\)' -or $dateRow -notmatch '`trip_current_departure`') {
  throw 'Operator booking date row must filter the ICT half-open day over trip_current_departure'
}
$expectedSortRow='| `sortBy` | string | `createdAt` | Allow-list: `createdAt`, `departureAt`, `bookingCode`, `status`, `totalAmount`; otherwise `400 INVALID_SORT_FIELD`. |'
if (-not $operatorList.Contains($expectedSortRow)) { throw 'Operator booking sort key allow-list changed' }
if ($operatorList -notmatch '(?is)`sortBy=departureAt`.{0,120}(?:sorts|orders).{0,100}`trip_current_departure`') {
  throw 'Existing departureAt sort key must order trip_current_departure'
}
if (-not $operatorList.Contains('Sort always adds `id` as the deterministic tie-breaker in the same direction as `sortDir`.')) {
  throw 'Operator booking stable secondary ordering changed'
}

$listJson=[regex]::Match($operatorList,'(?ms)Response `200`.*?```json\s*(\{.*?\})\s*```').Groups[1].Value
$detailJson=[regex]::Match($operatorDetail,'(?ms)Response `200`.*?```json\s*(\{.*?\})\s*```').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($listJson) -or [string]::IsNullOrWhiteSpace($detailJson)) { throw 'Operator booking response example missing' }
try {
  $listExample=$listJson | ConvertFrom-Json -ErrorAction Stop
  $detailExample=$detailJson | ConvertFrom-Json -ErrorAction Stop
} catch {
  throw "Operator booking response example contains invalid JSON: $($_.Exception.Message)"
}
$listItem=$listExample.data.items | Select-Object -First 1
$detailItem=$detailExample.data
foreach($shape in @($listItem,$detailItem)) {
  if (-not $shape.trip.PSObject.Properties['departureAt'] -or -not $shape.trip.PSObject.Properties['currentDepartureAt']) {
    throw 'Operator booking response must nest departureAt and currentDepartureAt under trip'
  }
  if ($shape.PSObject.Properties['currentDepartureAt']) { throw 'Operator booking response must not duplicate currentDepartureAt at top level' }
}

$resolve=[regex]::Match($api,'(?ms)^### POST `/v1/bookings/\{bookingId\}/pending-actions/\{actionId\}/resolve`.*?(?=^### )').Value
if ([string]::IsNullOrWhiteSpace($resolve)) { throw 'Resolve endpoint section not found' }
foreach($row in @(
  '| 401 | `AUTH_TOKEN_INVALID` |',
  '| 403 | `FORBIDDEN` |',
  '| 404 | `BOOKING_NOT_FOUND` |',
  '| 404 | `BOOKING_PENDING_ACTION_NOT_FOUND` |',
  '| 409 | `BOOKING_PENDING_ACTION_NOT_RESOLVABLE` |',
  '| 409 | `BOOKING_PENDING_ACTION_SUPERSEDED` |',
  '| 409 | `BOOKING_PENDING_ACTION_ALREADY_RESOLVED` |',
  '| 409 | `BOOKING_PENDING_ACTION_EXPIRED` |',
  '| 409 | `IDEMPOTENCY_REQUEST_PENDING` |',
  '| 422 | `IDEMPOTENCY_KEY_REQUIRED` |',
  '| 422 | `IDEMPOTENCY_KEY_MISMATCH` |',
  '| 422 | `VALIDATION_ERROR` |'
)) { if (-not $resolve.Contains($row)) { throw "API resolve error table missing $row" } }
foreach($v in @('same key','same payload','byte-identical','new key','selectedStopId','SCHEDULE_CHANGE','effective cutoff')) {
  if ($resolve -notmatch [regex]::Escape($v)) { throw "API resolve section missing $v" }
}

Require-Regex 'BACKEND_SOURCE_OF_TRUTH.md' '(?m)^> \*\*Phiên bản:\*\* 1\.35\.0\s*$'
Require-Regex 'BACKEND_SOURCE_OF_TRUTH.md' '(?m)^\| \*\*1\.35\.0\*\* \| 2026-07-17 \| BE lead \(Vũ\) \| \*\*MINOR\*\* — Day-23 schedule-change contract, projection, errors, events, and jobs\.'
foreach($row in @(
  '| | `BOOKING_PENDING_ACTION_NOT_FOUND` | 404 |',
  '| | `BOOKING_PENDING_ACTION_NOT_RESOLVABLE` | 409 |',
  '| | `BOOKING_PENDING_ACTION_SUPERSEDED` | 409 |',
  '| | `BOOKING_PENDING_ACTION_ALREADY_RESOLVED` | 409 |',
  '| | `BOOKING_PENDING_ACTION_EXPIRED` | 409 |'
)) { Require-Literal 'BACKEND_SOURCE_OF_TRUTH.md' $row }
Require-Regex 'BACKEND_SOURCE_OF_TRUTH.md' '(?m)^\| `booking\.booking\.pending_action_auto_resolved` \| Booking \| Notification \| Exact `\{ eventId, occurredAt, bookingId, tripId, userId, pendingActionId, resolvedAction, severity, oldDeparture, newDeparture \}`; `resolvedAction=ACCEPTED` \|\s*$'
Require-Regex 'BACKEND_SOURCE_OF_TRUTH.md' '(?m)^\| `booking\.booking\.cancelled` \| Booking \| Notification, Trip \(release seats\), Payment \(refund\), Booking \(BookingStats counter\) \|.*eventId.*occurredAt.*refundAmount.*$'
$bsotText=Get-Content -Raw 'BACKEND_SOURCE_OF_TRUTH.md'
$existingRealertRow=[regex]::Match($bsotText,'(?m)^\| `PendingActionRealertJob` \|.*$').Value
if ([string]::IsNullOrWhiteSpace($existingRealertRow)) { throw 'PendingActionRealertJob registry row missing' }
foreach($v in @('action occurrence + 2h','PENDING_SEAT_ASSIGNMENT','SCHEDULE_CHANGE','MEDIUM','MAJOR','unchanged Day-22','at most once')) {
  if (-not $existingRealertRow.Contains($v)) { throw "PendingActionRealertJob row missing $v" }
}
$autoAcceptRow=[regex]::Match($bsotText,'(?m)^\| `ScheduleChangeAutoAcceptJob` \|.*$').Value
if ([string]::IsNullOrWhiteSpace($autoAcceptRow)) { throw 'ScheduleChangeAutoAcceptJob registry row missing' }
foreach($v in @('initialDeadline + 1s','terminalDeadline + 1s','initialDeadline < terminalDeadline','MEDIUM','MAJOR','initial-phase','direct','at most once','never refund')) {
  if (-not $autoAcceptRow.Contains($v)) { throw "ScheduleChangeAutoAcceptJob row missing $v" }
}
if ($existingRealertRow -match 'MAJOR\s+first\s+phase\s+only' -or $autoAcceptRow -match 'max\s*\(\s*initialDeadline\s*,\s*terminalDeadline\s*\)') {
  throw 'Day-22 re-alert and Day-23 timeout schedules were conflated'
}

# Robust negatives. Endpoint/alias forms are forbidden anywhere in the four contract documents.
$contractText=($docs[0..3] | ForEach-Object { Get-Content -Raw $_ }) -join "`n"
foreach($bad in @(
  '(?i)/(?:v1/)?operator/trips/\{[^}]+\}/(?:schedule(?:-change)?|departure(?:-time)?)\b',
  '(?i)/(?:v1/)?bookings/\{[^}]+\}/pending-actions/\{[^}]+\}/(?:accept(?:\|reject)?|reject)\b'
)) { if ($contractText -match $bad) { throw "Obsolete endpoint/alias remains: $($Matches[0])" } }

$timeline=[regex]::Match((Get-Content -Raw 'BE_TIMELINE_VU.md'),'(?ms)^### Day 23\b.*?(?=^### Day 24\b)').Value
$technical=[regex]::Match((Get-Content -Raw 'SU26SE101_VIETRIDE_technical_context_v7.md'),'(?ms)^### 6\.13\b.*?(?=^### 6\.14\b)').Value
$bsot=[regex]::Match((Get-Content -Raw 'BACKEND_SOURCE_OF_TRUTH.md'),'(?ms)^### 8\.10\b.*?(?=^### 8\.11\b)').Value
$scheduleBlocks="$timeline`n$technical`n$resolve`n$bsot"
$badTimeout='(?is)(?:passenger\s+accepts?\s+or\s+auto(?:matic(?:ally)?)?[- ]?refund|timeout.{0,180}(?:auto(?:matic(?:ally)?)?[- ]?(?:refund(?:ed)?|cancel(?:led)?|fallback)|(?:tự động|auto).{0,50}(?:hoàn tiền|hoàn|hủy))|(?:auto(?:matic(?:ally)?)?[- ]?refund(?:ed)?|tự động.{0,30}hoàn).{0,180}timeout)'
if ($scheduleBlocks -match $badTimeout) { throw "Obsolete schedule timeout/refund wording remains: $($Matches[0])" }
$badThreeHours='(?is)(?:\+?\s*3\s*(?:h|hours?|giờ).{0,180}MAJOR|MAJOR.{0,180}\+?\s*3\s*(?:h|hours?|giờ))'
if ($scheduleBlocks -match $badThreeHours) { throw "Obsolete +3h-as-MAJOR wording remains: $($Matches[0])" }
```

### Task 23.1 — Preserve explicit event identity through the shared Outbox seam

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-integration-event` |
| owned files (base write set) | `libs/dotnet/VietRide.Shared.Application/Outbox/IIntegrationEventOutbox.cs`; `libs/dotnet/VietRide.Shared.Persistence/Outbox/IntegrationEventOutbox.cs`; `libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs`; `libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqEventPublisher.cs`; new `tests/dotnet/VietRide.Shared.Persistence.UnitTests/Outbox/Day23ExplicitOutboxIdentityTests.cs`; new `tests/dotnet/VietRide.Shared.Persistence.UnitTests/Outbox/Day23OutboxRestartIdentityTests.cs`; new `tests/dotnet/VietRide.Shared.Messaging.UnitTests/RabbitMq/Day23RabbitMqEnvelopeIdentityTests.cs` |
| auto-expand scope | Directly affected shared Outbox fixtures only; append every exact expansion to `$changed`. Pair interface/implementation changes. |
| forbidden scope | Service producers/consumers, payload/routing-key changes, schemas/migrations, topology changes, dependencies, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.0 |
| parallel-safe | yes — disjoint from Task 23.2. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact focused block below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF; Clean Architecture direction; CPM/no dependency; MediatR v11; Outbox durability; `vietride.events`; persistent delivery. |
| acceptance | Additive explicit-id enqueue rejects `Guid.Empty` and persists the supplied id; the legacy overload remains compatible and generates an id. Dedicated tests prove exact Outbox id, `Type=routingKey`, persistent delivery, `MessageId=eventId`, and restart delivery of the same unpublished row/id for every Day-23 routing key. No old broad messaging class can satisfy the gate. |
| source citations | current Outbox interface/implementation/worker/publisher; BSOT §§7.3–7.4, 9.8; `add-integration-event` focused producer/publisher/restart requirements. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23ExplicitOutboxIdentityTests' 'explicit-id'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'restart-id'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'amqp-id'

$changed=@(
  'libs/dotnet/VietRide.Shared.Application/Outbox/IIntegrationEventOutbox.cs',
  'libs/dotnet/VietRide.Shared.Persistence/Outbox/IntegrationEventOutbox.cs',
  'libs/dotnet/VietRide.Shared.Messaging/Outbox/OutboxBackgroundService.cs',
  'libs/dotnet/VietRide.Shared.Messaging/RabbitMq/RabbitMqEventPublisher.cs',
  'tests/dotnet/VietRide.Shared.Persistence.UnitTests/Outbox/Day23ExplicitOutboxIdentityTests.cs',
  'tests/dotnet/VietRide.Shared.Persistence.UnitTests/Outbox/Day23OutboxRestartIdentityTests.cs',
  'tests/dotnet/VietRide.Shared.Messaging.UnitTests/RabbitMq/Day23RabbitMqEnvelopeIdentityTests.cs'
)
dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes --include $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.1 focused format failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.1 diff hygiene failed' }
```

### Task 23.2 — Add and backfill the Booking current-departure projection

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `ef-migration` |
| owned files (base write set) | `apps/booking/src/VietRide.Booking.Domain/Entities/Booking.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/BookingConfiguration.cs`; exact new `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/20260717000000_AddBookingTripCurrentDeparture.cs`; exact new `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/20260717000000_AddBookingTripCurrentDeparture.Designer.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs`; new `apps/booking/tests/VietRide.Booking.UnitTests/Infrastructure/AddBookingTripCurrentDepartureMigrationTests.cs`; new `apps/booking/tests/VietRide.Booking.IntegrationTests/Migrations/Day23BookingCurrentDepartureBackfillIntegrationTests.cs`; new `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/Day23BookingCurrentDepartureInitializationTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateBookingCommandHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateRoundTripBookingCommandHandlerTests.cs` |
| auto-expand scope | Directly affected creation fixtures/builders only; append each exact path to `$changed`. Generate once, then normalize the generated filename and `[Migration]` id to deterministic `20260717000000_AddBookingTripCurrentDeparture`; never create a second migration. |
| forbidden scope | Historical snapshot mutation, schedule consumer/resolver/job behavior, unrelated migrations, cross-DB FK, dependencies, `.env`, secrets, databases other than the named scratch DB, and git operations. |
| depends on | 23.0 |
| parallel-safe | yes — disjoint from Task 23.1 and Trip-only Task 23.3. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact generation-once, lifecycle, SQL, dedicated-test, and hygiene blocks below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF; snake_case; TIMESTAMPTZ; reversible `Down()`; no cross-DB FK; immutable snapshot; no package; scratch database only. |
| acceptance | The deterministic migration adds nullable `trip_current_departure`, backfills every legacy row from `trip_snapshot_departure`, creates `idx_bookings_trip_current_departure`, and removes only that index/column in `Down()`. `AddBookingTripCurrentDepartureMigrationTests` has its own exact non-zero gate for Up/Down operations; the dedicated real-PostgreSQL backfill class migrates a legacy row from the prior migration and asserts current equals snapshot; the initialization class proves both creation paths. Empty apply, down, reapply, SQL inspection, and no-pending-model checks all have immediate exit checks. |
| source citations | BSOT §4.3; canonical Booking DDL/README; Booking entity/config/design factory; `ef-migration` lifecycle rules. |

Generation command, run once during implementation before deterministic normalization:

```powershell
dotnet ef migrations add AddBookingTripCurrentDeparture -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api -o Migrations
if ($LASTEXITCODE -ne 0) { throw 'Migration generation failed' }
# Use apply_patch to normalize the generated pair and [Migration] id to
# 20260717000000_AddBookingTripCurrentDeparture. Do not regenerate on verification.
```

```powershell
$migration='20260717000000_AddBookingTripCurrentDeparture'
$prior='20260712182713_AddBookingShuttleIntent'
$project='apps/booking/src/VietRide.Booking.Infrastructure'
$startup='apps/booking/src/VietRide.Booking.Api'
$env:BOOKING_DESIGN_CONNECTION='Host=localhost;Port=5432;Database=vietride_booking_day23_migration;Username=vietride;Password=vietride_dev'

foreach($p in @(
  "apps/booking/src/VietRide.Booking.Infrastructure/Migrations/$migration.cs",
  "apps/booking/src/VietRide.Booking.Infrastructure/Migrations/$migration.Designer.cs",
  'apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs'
)) { if (-not (Test-Path $p)) { throw "Missing deterministic migration path $p" } }

dotnet ef database drop --force -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Scratch database drop failed' }
dotnet ef database update $migration -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Empty-database apply failed' }
dotnet ef database update $prior -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Migration down-to-prior failed' }
dotnet ef database update $migration -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Migration reapply failed' }
dotnet ef migrations has-pending-model-changes -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Pending model changes remain' }

$sql=dotnet ef migrations script $prior $migration -p $project -s $startup
if ($LASTEXITCODE -ne 0) { throw 'Migration SQL generation failed' }
$text=$sql -join [Environment]::NewLine
foreach($v in @('trip_current_departure','idx_bookings_trip_current_departure','UPDATE bookings','trip_snapshot_departure')) {
  if ($text -notmatch [regex]::Escape($v)) { throw "Migration SQL missing $v" }
}
if ($text -match '"TripCurrentDeparture"|FOREIGN KEY[^;]*trip_current_departure') { throw 'Migration violates snake_case/logical-FK rules' }
if (-not (Select-String -Path "apps/booking/src/VietRide.Booking.Infrastructure/Migrations/$migration.cs" -Pattern 'protected override void Down' -Quiet)) { throw 'Migration Down is missing' }
```

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
$unit='apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj'
$integration='apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Infrastructure.AddBookingTripCurrentDepartureMigrationTests' 'migration-tests'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.Migrations.Day23BookingCurrentDepartureBackfillIntegrationTests' 'migration-backfill'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Features.Bookings.Day23BookingCurrentDepartureInitializationTests' 'initialization-tests'

$changed=@(
  'apps/booking/src/VietRide.Booking.Domain/Entities/Booking.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/BookingConfiguration.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Migrations/20260717000000_AddBookingTripCurrentDeparture.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Migrations/20260717000000_AddBookingTripCurrentDeparture.Designer.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Infrastructure/AddBookingTripCurrentDepartureMigrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Migrations/Day23BookingCurrentDepartureBackfillIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/Day23BookingCurrentDepartureInitializationTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateBookingCommandHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CreateRoundTripBookingCommandHandlerTests.cs'
)
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.2 focused format failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.2 diff hygiene failed' }
```

### Task 23.3 — Harden the existing ALL_PENDING schedule-change producer

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-integration-event` |
| owned files (base write set) | `apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/UpdateDriverScheduleHandler.cs`; `apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/TripGenerationService.cs`; `apps/trip/src/VietRide.Trip.Application/Events/TripScheduleChangedIntegrationEvent.cs`; new `apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/Day23AllPendingScheduleChangeProducerTests.cs`; new `apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/Day23MutableScheduleGenerationDedupeTests.cs`; new `apps/trip/tests/VietRide.Trip.IntegrationTests/DriverSchedules/Day23AllPendingScheduleChangeProducerIntegrationTests.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/UpdateDriverScheduleHandlerTests.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/Features/TripGeneration/TripGenerationServiceTests.cs` |
| auto-expand scope | Same producer/helper, interface/fake updates required by 23.1, and directly affected Trip fixtures; append exact paths to `$changed`. |
| forbidden scope | New Trip endpoint/controller/request, Gateway files, Booking/Payment/Notification production, one-off Trip edits, stable-identity migration, dependencies, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.0, 23.1 |
| parallel-safe | yes — Trip write set is disjoint from Task 23.2. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact dedicated producer/dedupe and shared identity blocks below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF; one captured clock; existing auth/idempotency; ICT comparison; atomic Trip+Outbox; no direct publisher; no endpoint/schema/dependency. |
| acceptance | Dedicated tests prove both old/new two-hour boundaries (equal allowed, one tick below rejected), all severity edges/date change, full-batch preflight before writes, one transaction rollback, one exact `trip.trip.schedule_changed` Outbox row, replay/no-op zero rows, and mutable schedule generation dedupe. Payload id equals row id; Task 23.1 dedicated publisher/restart classes prove routing key/MessageId and restart. Old broad classes cannot satisfy the gate. |
| source citations | technical context §6.13; DriverSchedule API contract; BSOT cascade/event registry; current handler/generation service; Day-22 checklist. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
$unit='apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj'
$integration='apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj'
Invoke-Day23Trx $unit 'VietRide.Trip.UnitTests.Features.DriverSchedules.Day23AllPendingScheduleChangeProducerTests' 'trip-producer-boundaries'
Invoke-Day23Trx $unit 'VietRide.Trip.UnitTests.Features.DriverSchedules.Day23MutableScheduleGenerationDedupeTests' 'trip-generation-dedupe'
Invoke-Day23Trx $integration 'VietRide.Trip.IntegrationTests.DriverSchedules.Day23AllPendingScheduleChangeProducerIntegrationTests' 'trip-producer-transaction'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'trip-amqp-envelope'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'trip-outbox-restart'

$changed=@(
  'apps/trip/src/VietRide.Trip.Application/Features/DriverSchedules/UpdateDriverScheduleHandler.cs',
  'apps/trip/src/VietRide.Trip.Application/Features/TripGeneration/TripGenerationService.cs',
  'apps/trip/src/VietRide.Trip.Application/Events/TripScheduleChangedIntegrationEvent.cs',
  'apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/Day23AllPendingScheduleChangeProducerTests.cs',
  'apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/Day23MutableScheduleGenerationDedupeTests.cs',
  'apps/trip/tests/VietRide.Trip.IntegrationTests/DriverSchedules/Day23AllPendingScheduleChangeProducerIntegrationTests.cs',
  'apps/trip/tests/VietRide.Trip.UnitTests/Features/DriverSchedules/UpdateDriverScheduleHandlerTests.cs',
  'apps/trip/tests/VietRide.Trip.UnitTests/Features/TripGeneration/TripGenerationServiceTests.cs'
)
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.3 focused format failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.3 diff hygiene failed' }
```

### Task 23.4 — Apply schedule events to the current projection and Booking-owned facts

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-integration-event` |
| owned files (base write set) | exact schedule command/handler and integration DTO/handler; exact Booking repository pair; STOP_DISABLED handler; exact operator list/detail DTO/query files; dedicated Day-23 unit/integration tests and directly affected legacy tests listed in `$changed` below. |
| auto-expand scope | Directly affected pending-action metadata parser, DTO/controller serialization fixture, or paired repository fixture only; append exact path to `$changed`. |
| forbidden scope | Resolve endpoint, timeout job, cancellation producers/consumers, Trip/Gateway/Payment/Notification production, snapshot mutation, schema/migrations, dependencies, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.0, 23.1, 23.2 |
| parallel-safe | no — later Booking tasks extend the same aggregate/repository. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact dedicated CAS/operational-read and identity blocks below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF; immutable snapshot; stable lock order; PENDING_PAYMENT|CONFIRMED projection; CONFIRMED facts only; explicit ids; one active action; no cross-DB access. |
| acceptance | Dedicated PostgreSQL tests prove `current==old` applies, `current==new` dedupes, and a third value fails before ACK/commit; failed transactions roll back projection/action/fact. Both eligible statuses update projection but only CONFIRMED emits facts/actions. `Day23CurrentDepartureOperationalReadIntegrationTests` proves that unchanged `date` filters the ICT half-open day over `trip_current_departure`, including rows moved earlier and later across both date boundaries; unchanged `sortBy=departureAt` orders the current projection with stable secondary `id` ordering in the same direction as `sortDir`; and list/detail serialize only nested `trip.currentDepartureAt` beside immutable `trip.departureAt`, with no top-level duplicate or new sort key. STOP_DISABLED deadlines use the current projection. Initial facts have payload id=row id, with Task 23.1 exact AMQP/restart gates. |
| source citations | technical context §6.13; API schedule facts/operator bookings; BSOT §§4.3, 7.3; current repository, STOP_DISABLED handler, schedule consumer/tests. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
Invoke-Day23Trx 'apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj' 'VietRide.Booking.UnitTests.Features.Bookings.Day23ScheduleProjectionRulesTests' 'projection-rules'
Invoke-Day23Trx 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj' 'VietRide.Booking.IntegrationTests.Messaging.Day23ScheduleProjectionCasIntegrationTests' 'projection-cas'
Invoke-Day23Trx 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj' 'VietRide.Booking.IntegrationTests.Day23CurrentDepartureOperationalReadIntegrationTests' 'projection-reads'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'booking-fact-amqp'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'booking-fact-restart'

$changed=@(
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/HandleScheduleChange/HandleScheduleChangeCommand.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/HandleScheduleChange/HandleScheduleChangeCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Messaging/TripScheduleChangedIntegrationEvent.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Messaging/TripScheduleChangedIntegrationEventHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/HandleStopDisabled/HandleStopDisabledCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/OperatorBookings/ListOperatorBookings/OperatorBookingListCriteria.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/OperatorBookings/ListOperatorBookings/OperatorBookingListItem.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/OperatorBookings/ListOperatorBookings/OperatorBookingTripDto.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/OperatorBookings/ListOperatorBookings/ListOperatorBookingsQueryHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/OperatorBookings/GetOperatorBookingDetail/OperatorBookingDetailDto.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/Day23ScheduleProjectionRulesTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/Day23ScheduleProjectionCasIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Day23CurrentDepartureOperationalReadIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/HandleScheduleChangeCommandHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/HandleStopDisabledCommandHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/OperatorBookings/ListOperatorBookingsQueryHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/TripScheduleChangedIntegrationEventHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/OperatorBookingsListRepositoryIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/OperatorBookingsDetailRepositoryIntegrationTests.cs'
)
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.4 focused format failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.4 diff hygiene failed' }
```

### Task 23.5 — Roll out canonical booking-cancelled event identity compatibly

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | `add-integration-event` |
| owned files (base write set) | new `libs/shared/contracts/src/events/booking-cancelled.event.ts`; new `libs/shared/contracts/src/events/__tests__/day23-booking-cancelled.event.spec.ts`; `libs/shared/contracts/src/index.ts`; exact Booking producers/consumer, Payment consumer, Trip shuttle consumer, Notification core consumer/mapper/module, and all dedicated/affected tests listed in the verification arrays. |
| auto-expand scope | Exact existing `booking.booking.cancelled` DTO/DI fixture required by compilation only; append every exact expansion to the applicable array. Task 23.8 must append its export to the already-updated shared index without replacing the cancellation export. |
| forbidden scope | New routing key, refund-policy changes, schedule resolver/job, Prisma schema/migrations, unrelated services/events, dependencies, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.0, 23.1 |
| parallel-safe | no — coordinated four-consumer compatibility rollout and shared-index write; execute alone. Task 23.8 is serialized after this task through 23.6→23.7. |
| verification tier | `FOCUSED` |
| verification commands | Run exact dedicated producer/consumer compatibility, shared identity, and hygiene blocks below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF/TS LF; fresh UUID-v4 per winning cancellation; one captured time; payload=row=MessageId; one-release fallback; refundAmount authoritative; no schema/dependency. |
| acceptance | `booking-cancelled.event.ts` defines and `index.ts` exports exactly `BookingCancelledEventSchema`/`BookingCancelledEvent`, `BookingCancelledLegacyEventSchema`, `BookingCancelledConsumerEventSchema`/`BookingCancelledConsumerEvent`, and `BOOKING_CANCELLED_ROUTING_KEY`. The strict canonical producer shape is `{eventId,occurredAt,bookingId,userId,refundAmount,refundOverride,cancellationReason,bookingCode?,ticketCodes?,ticketCount?}`: `eventId`/IDs are UUIDs; `occurredAt` is an offset date-time; `refundAmount` is a nonnegative whole-number JSON number or decimal-digit string; `refundOverride` is boolean; `cancellationReason` is a non-empty string; optional `bookingCode` and each optional `ticketCodes` entry are non-empty strings; optional `ticketCount` is a nonnegative integer; and unknown fields fail. The separately exported consumer schema is a union of that required canonical schema and one exact strict legacy branch with both `eventId` and `occurredAt` absent; it rejects either field alone and never weakens `BookingCancelledEventSchema`. Notification imports the consumer schema instead of retaining a local broad cancellation schema. Both existing .NET producers create fresh `eventId` and one `occurredAt` inside the winning transaction, serialize every canonical required field, and pass the id to explicit Outbox enqueue; no producer may use the legacy branch. Dedicated shared-contract and Booking tests enforce required producer identity and payload id=row id for both paths. Dedicated Booking/Payment/Trip/Notification consumer tests prove canonical `eventId` wins, only exact legacy payloads fall back to `bookingId`, malformed/empty/partial identity fails, and redelivery dedupes. Shared dedicated AMQP/restart classes prove MessageId/restart. Existing refund behavior is unchanged. |
| source citations | BSOT cancellation event row/Outbox; API cancellation facts; current producers and four consumers; `add-integration-event` requirements. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
Invoke-Day23Trx 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj' 'VietRide.Booking.IntegrationTests.Messaging.Day23BookingCancelledIdentityProducerTests' 'cancel-producers'
Invoke-Day23Trx 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj' 'VietRide.Booking.IntegrationTests.Messaging.Day23BookingCancelledCompatibilityTests' 'cancel-booking-consumer'
Invoke-Day23Trx 'apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj' 'VietRide.Payment.UnitTests.Infrastructure.Messaging.Day23BookingCancelledCompatibilityTests' 'cancel-payment-consumer'
Invoke-Day23Trx 'apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj' 'VietRide.Trip.IntegrationTests.Messaging.Day23BookingCancelledCompatibilityTests' 'cancel-trip-consumer'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'cancel-amqp'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'cancel-restart'

$contractOut=Join-Path ([IO.Path]::GetTempPath()) ('vr23-cancel-contract-'+[guid]::NewGuid()+'.json')
npx jest --config libs/shared/contracts/jest.config.cts --runInBand --ci --runTestsByPath libs/shared/contracts/src/events/__tests__/day23-booking-cancelled.event.spec.ts --testNamePattern '^Day 23 booking.cancelled contract:' --json --outputFile $contractOut
if ($LASTEXITCODE -ne 0) { throw 'Day-23 shared booking.cancelled contract failed' }
try {
  $contractJson=Get-Content -Raw $contractOut | ConvertFrom-Json -ErrorAction Stop
} catch {
  throw "Day-23 shared booking.cancelled contract produced invalid Jest JSON: $($_.Exception.Message)"
}
if (-not $contractJson.success -or $contractJson.numPassedTests -lt 1 -or $contractJson.numFailedTests -ne 0 -or $contractJson.numPendingTests -ne 0 -or $contractJson.numTodoTests -ne 0) {
  throw 'Day-23 shared booking.cancelled contract executed zero/failed/skipped tests'
}

$o=Join-Path ([IO.Path]::GetTempPath()) ('vr23-cancel-notification-'+[guid]::NewGuid()+'.json')
npx jest --config apps/notification/jest.config.cts --runInBand --ci --runTestsByPath apps/notification/src/notifications/day23-booking-cancelled-compatibility.spec.ts --testNamePattern '^Day 23 booking.cancelled compatibility:' --json --outputFile $o
if ($LASTEXITCODE -ne 0) { throw 'Day-23 Notification cancellation compatibility failed' }
try {
  $j=Get-Content -Raw $o | ConvertFrom-Json -ErrorAction Stop
} catch {
  throw "Day-23 Notification cancellation compatibility produced invalid Jest JSON: $($_.Exception.Message)"
}
if (-not $j.success -or $j.numPassedTests -lt 1 -or $j.numFailedTests -ne 0 -or $j.numPendingTests -ne 0 -or $j.numTodoTests -ne 0) { throw 'Day-23 Notification cancellation compatibility executed zero/failed/skipped tests' }

$contractChanged=@(
  'libs/shared/contracts/src/events/booking-cancelled.event.ts',
  'libs/shared/contracts/src/events/__tests__/day23-booking-cancelled.event.spec.ts',
  'libs/shared/contracts/src/index.ts'
)
$bookingChanged=@(
  'apps/booking/src/VietRide.Booking.Application/Events/BookingCancelledIntegrationEvent.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/CancelBooking/CancelBookingCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/HandleTripCancelled/HandleTripCancelledCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Messaging/BookingCancelledIntegrationEvent.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Messaging/BookingCancelledIntegrationEventHandler.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/CancelBookingCommandHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/HandleTripCancelledCommandHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/CancelBookingIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/TripCancelledIntegrationEventHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/Day23BookingCancelledIdentityProducerTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/Day23BookingCancelledCompatibilityTests.cs'
)
$paymentChanged=@(
  'apps/payment/src/VietRide.Payment.Infrastructure/Messaging/BookingCancelledIntegrationEvent.cs',
  'apps/payment/src/VietRide.Payment.Infrastructure/Messaging/BookingCancelledIntegrationEventHandler.cs',
  'apps/payment/tests/VietRide.Payment.UnitTests/Infrastructure/Messaging/BookingCancelledIntegrationEventHandlerTests.cs',
  'apps/payment/tests/VietRide.Payment.UnitTests/Infrastructure/Messaging/Day23BookingCancelledCompatibilityTests.cs'
)
$tripChanged=@(
  'apps/trip/src/VietRide.Trip.Infrastructure/Messaging/BookingShuttleCancelledIntegrationEvent.cs',
  'apps/trip/src/VietRide.Trip.Infrastructure/Messaging/BookingShuttleCancelledIntegrationEventHandler.cs',
  'apps/trip/tests/VietRide.Trip.IntegrationTests/Messaging/BookingShuttleCancelledIntegrationEventHandlerTests.cs',
  'apps/trip/tests/VietRide.Trip.IntegrationTests/Messaging/Day23BookingCancelledCompatibilityTests.cs'
)
$notificationChanged=@(
  'apps/notification/src/notifications/core-events.consumer.ts',
  'apps/notification/src/notifications/core-event-notification.mapper.ts',
  'apps/notification/src/notifications/notifications.module.ts',
  'apps/notification/src/notifications/core-events.consumer.spec.ts',
  'apps/notification/src/notifications/core-event-notification.mapper.spec.ts',
  'apps/notification/src/notifications/day23-booking-cancelled-compatibility.spec.ts'
)
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $bookingChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 Booking format failed' }
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --include $paymentChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 Payment format failed' }
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include $tripChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 Trip format failed' }
npx eslint $notificationChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 Notification lint failed' }
npx eslint $contractChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 shared-contract lint failed' }
npx nx build contracts
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 contracts build failed' }
npx nx build notification
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 Notification build failed' }
git diff --check -- ($contractChanged+$bookingChanged+$paymentChanged+$tripChanged+$notificationChanged)
if ($LASTEXITCODE -ne 0) { throw 'Task 23.5 diff hygiene failed' }
```

### Task 23.6 — Resolve passenger schedule actions and transact refunds/cancellation

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-endpoint` + `add-integration-event` |
| owned files (base write set) | exact controller/request/command/handler/validator/result, state machine/refund service, three repository interface/implementation pairs, Gateway access-gate spec, and all dedicated/affected test files listed in `$bookingChanged`/`$gatewayChanged`. |
| auto-expand scope | Directly affected DI fixture, cancellation event fixture, or repository fake only; append each exact path to verification arrays. |
| forbidden scope | Accept/reject aliases, operator seat-assignment route/DTO/handler, non-SCHEDULE_CHANGE mutation, Trip/Gateway production route, timeout job, new event key, schema/migration/dependency, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.2, 23.4, 23.5 |
| parallel-safe | no — shares action/repository/state machine with Task 23.7. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact separately non-zero handler/controller/auth-masking/idempotency/transaction/refund/Gateway and identity blocks below. |
| full regression owner | `audit-day` |
| invariant flags | Thin controller→MediatR; ADR 0004; `[RequireIdempotency]`; owner masking; strict expiry/equality; stable action→Booking lock order; immutable money/AwayFromZero; atomic history+cancellation+Outbox. |
| acceptance | Controller is PASSENGER-only, thin, carries `[RequireIdempotency]`, and has `[ProducesResponseType]` for exact `200,401,403,404,409,422` ADR-0004 shapes. Separate dedicated filters prove controller/handler dispatch and exact mapping: `401 AUTH_TOKEN_INVALID`; `403 FORBIDDEN`; masked `404 BOOKING_NOT_FOUND`; owner-authorized absent action `404 BOOKING_PENDING_ACTION_NOT_FOUND`; active wrong reason/state `409 BOOKING_PENDING_ACTION_NOT_RESOLVABLE`; new-key terminal `409 BOOKING_PENDING_ACTION_SUPERSEDED|BOOKING_PENDING_ACTION_ALREADY_RESOLVED`; strict-after `409 BOOKING_PENDING_ACTION_EXPIRED`; `409 IDEMPOTENCY_REQUEST_PENDING`; and `422 IDEMPOTENCY_KEY_REQUIRED|IDEMPOTENCY_KEY_MISMATCH|VALIDATION_ERROR`. Same key/body replays the original bytes after terminal state. Equality is eligible; strict-after returns EXPIRED unless the timeout race already won. ACCEPTED resolves once and preserves CONFIRMED. REJECTED validates frozen explicit-percent metadata, uses 50%/100% AwayFromZero, cancels with `SCHEDULE_CHANGED`/`refundOverride=true`, appends one automated history row, and atomically emits one canonical cancelled event whose `refundAmount` is authoritative. No Gateway route changes. |
| source citations | technical context §6.13; reconciled API endpoint/error table; BSOT §§3.2, 5.4–5.6, 5.9, 7.3; current refund calculator and pending-action schema; both named feature skills. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
$unit='apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj'
$integration='apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Features.Bookings.ResolvePendingAction.Day23ResolveScheduleActionHandlerTests' 'resolve-handler'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Controllers.Day23ResolveScheduleActionControllerTests' 'resolve-controller-swagger'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Domain.Day23ScheduleChangeRefundRulesTests' 'resolve-refund-rules'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.ResolvePendingAction.Day23ResolveScheduleActionAuthorizationTests' 'resolve-auth-masking'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.ResolvePendingAction.Day23ResolveScheduleActionIdempotencyTests' 'resolve-idempotency'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.ResolvePendingAction.Day23ResolveScheduleActionTransactionTests' 'resolve-transaction'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'resolve-amqp'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'resolve-restart'

$o=Join-Path ([IO.Path]::GetTempPath()) ('vr23-gateway-resolve-'+[guid]::NewGuid()+'.json')
npx jest --config apps/gateway/jest.config.cts --runInBand --ci --runTestsByPath apps/gateway/src/proxy/proxy.access-gates.spec.ts --testNamePattern '^Day 23 resolve schedule action: existing booking prefix and PASSENGER gate$' --json --outputFile $o
if ($LASTEXITCODE -ne 0) { throw 'Gateway resolve access-gate test failed' }
try {
  $j=Get-Content -Raw $o | ConvertFrom-Json -ErrorAction Stop
} catch {
  throw "Gateway resolve access-gate produced invalid Jest JSON: $($_.Exception.Message)"
}
if (-not $j.success -or $j.numPassedTests -lt 1 -or $j.numFailedTests -ne 0 -or $j.numPendingTests -ne 0 -or $j.numTodoTests -ne 0) { throw 'Gateway resolve access-gate executed zero/failed/skipped tests' }

$bookingChanged=@(
  'apps/booking/src/VietRide.Booking.Api/Controllers/BookingsController.cs',
  'apps/booking/src/VietRide.Booking.Api/Controllers/Requests/ResolvePendingActionRequest.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/ResolvePendingAction/ResolvePendingActionCommand.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/ResolvePendingAction/ResolvePendingActionCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/ResolvePendingAction/ResolvePendingActionCommandValidator.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/ResolvePendingAction/ResolvePendingActionResult.cs',
  'apps/booking/src/VietRide.Booking.Domain/Entities/BookingPendingAction.cs',
  'apps/booking/src/VietRide.Booking.Domain/Services/ScheduleChangeResolutionStateMachine.cs',
  'apps/booking/src/VietRide.Booking.Domain/Services/CancellationRefundCalculator.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingPendingActionRepository.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingStatusHistoryRepository.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingPendingActionRepository.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingStatusHistoryRepository.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/ResolvePendingAction/Day23ResolveScheduleActionHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Controllers/Day23ResolveScheduleActionControllerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Domain/Day23ScheduleChangeRefundRulesTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/ResolvePendingAction/Day23ResolveScheduleActionAuthorizationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/ResolvePendingAction/Day23ResolveScheduleActionIdempotencyTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/ResolvePendingAction/Day23ResolveScheduleActionTransactionTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Domain/CancellationRefundCalculatorTests.cs'
)
$gatewayChanged=@('apps/gateway/src/proxy/proxy.access-gates.spec.ts')
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $bookingChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.6 Booking format failed' }
npx eslint $gatewayChanged
if ($LASTEXITCODE -ne 0) { throw 'Task 23.6 Gateway lint failed' }
git diff --check -- ($bookingChanged+$gatewayChanged)
if ($LASTEXITCODE -ne 0) { throw 'Task 23.6 diff hygiene failed' }
```

### Task 23.7 — Run the shared MEDIUM/MAJOR timeout state machine durably

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | dotnet-worker |
| review agent | dotnet-reviewer |
| skill | `add-integration-event` |
| owned files (base write set) | exact state machine, new schedule-change auto-accept scheduler/job, DI registrations, schedule handler/action repository/auto-resolved event, and all dedicated/affected test files in `$changed`. The existing Day-22 re-alert job is verification-only, not a write target. |
| auto-expand scope | Deterministic-id helper or directly affected auto-accept scheduler/transaction fixture only; append each exact expansion to `$changed`. Do not auto-expand into the existing Day-22 re-alert production file or broad test. |
| forbidden scope | Any production change to `apps/booking/src/VietRide.Booking.Infrastructure/Jobs/PendingActionRealertJob.cs` or rewrite of existing `PendingActionRealertJobTests.cs`; removal/narrowing/rescheduling of the existing occurrence+2h `IPendingActionRealertScheduler` path; timeout cancellation/refund; custom poller/package; Day-24 reasons; resolve HTTP shape; schema/migration; Trip/Payment/Gateway production; unregistered fields; `.env`; secrets; destructive operations; and git operations. |
| depends on | 23.4, 23.6 |
| parallel-safe | no — extends the same state machine/action/repository. |
| verification tier | `FOCUSED` |
| verification commands | Run exact dedicated equality/phase/registration/race and identity blocks below. |
| full regression owner | `audit-day` |
| invariant flags | C# CRLF; existing Hangfire/queue `booking`; one clock; strict expiry; occurrence+2h Day-22 re-alert unchanged; Day-23 initial/terminal cutoff+1s; phase-distinct deterministic ids; atomic state+Outbox; repair after commit; no timeout refund. |
| acceptance | The schedule handler continues to call the existing `IPendingActionRealertScheduler` at action occurrence `+2h` for both MEDIUM and MAJOR `SCHEDULE_CHANGE`; the untouched Day-22 `PendingActionRealertJob` also remains valid for unresolved `PENDING_SEAT_ASSIGNMENT`, uses its existing deterministic identity, and produces at most one T+2 event under retries. A separate `ScheduleChangeAutoAcceptJob` owns Day-23 deadlines. Dedicated frozen-clock tests cover before/equal/one-tick-after for every cutoff: MEDIUM final auto-accepts once at `initialDeadline + 1s`; MAJOR with `initialDeadline < terminalDeadline` emits at most one optional initial-phase `booking.booking.pending_action_realerted` using an identity derived from action + `MAJOR_INITIAL_PHASE`, distinct from the Day-22 T+2 identity, then schedules/finalizes once at `terminalDeadline + 1s`; lag already past terminal accepts directly without the optional phase; `initialDeadline >= terminalDeadline` emits no optional phase and accepts only strictly after initial. Equality remains passenger-eligible. Dedicated separation and PostgreSQL tests prove both re-alert schedules/scopes, retries at each intended phase, passenger/job winners, duplicate jobs, rollback, commit/schedule failure repair, no duplicate phase or terminal outcome, and no timeout cancellation/refund. Auto-resolved id is deterministic from action+outcome and equals payload/row/MessageId. Existing broad Day-22 tests alone cannot satisfy the gate. |
| source citations | technical context §6.13; BSOT event/job registries; API re-alert semantics; current re-alert job/Hangfire registration; Task 23.0 contract. |

```powershell
function Invoke-Day23Trx([string]$Project,[string]$Fqn,[string]$Name) {
  $d=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid())
  dotnet test $Project -c Release --filter "FullyQualifiedName~$Fqn" --logger "trx;LogFileName=$Name.trx" --results-directory $d
  if ($LASTEXITCODE -ne 0) { throw "$Fqn failed" }
  $trx=(Get-ChildItem $d -Filter "$Name.trx" -Recurse | Select-Object -First 1).FullName
  if (-not $trx) { throw "$Fqn produced no TRX" }
  [xml]$x=Get-Content $trx
  $c=$x.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
  if ([int]$c.executed -lt 1 -or [int]$c.failed -ne 0) { throw "$Fqn executed zero or failed tests" }
}
$unit='apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj'
$integration='apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Jobs.Day23RealertScheduleSeparationTests' 'realert-schedule-separation'
Invoke-Day23Trx $unit 'VietRide.Booking.UnitTests.Jobs.Day23ScheduleChangeTimeoutStateMachineTests' 'timeout-equality-phases'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.Jobs.Day23RealertPhaseSeparationIntegrationTests' 'realert-phase-once'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.Jobs.Day23ScheduleChangeTimeoutRegistrationTests' 'timeout-registration-repair'
Invoke-Day23Trx $integration 'VietRide.Booking.IntegrationTests.Jobs.Day23ScheduleChangeTimeoutRaceIntegrationTests' 'timeout-races'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Messaging.UnitTests/VietRide.Shared.Messaging.UnitTests.csproj' 'VietRide.Shared.Messaging.UnitTests.RabbitMq.Day23RabbitMqEnvelopeIdentityTests' 'timeout-amqp'
Invoke-Day23Trx 'tests/dotnet/VietRide.Shared.Persistence.UnitTests/VietRide.Shared.Persistence.UnitTests.csproj' 'VietRide.Shared.Persistence.UnitTests.Outbox.Day23OutboxRestartIdentityTests' 'timeout-restart'

$changed=@(
  'apps/booking/src/VietRide.Booking.Domain/Services/ScheduleChangeResolutionStateMachine.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Jobs/IScheduleChangeAutoAcceptScheduler.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Jobs/HangfireScheduleChangeAutoAcceptScheduler.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Jobs/ScheduleChangeAutoAcceptJob.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Jobs/HangfireServiceCollectionExtensions.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs',
  'apps/booking/src/VietRide.Booking.Application/Features/Bookings/HandleScheduleChange/HandleScheduleChangeCommandHandler.cs',
  'apps/booking/src/VietRide.Booking.Domain/Entities/BookingPendingAction.cs',
  'apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingPendingActionRepository.cs',
  'apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingPendingActionRepository.cs',
  'apps/booking/src/VietRide.Booking.Application/Events/BookingPendingActionAutoResolvedIntegrationEvent.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Jobs/Day23RealertScheduleSeparationTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Jobs/Day23ScheduleChangeTimeoutStateMachineTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day23RealertPhaseSeparationIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day23ScheduleChangeTimeoutRegistrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/Day23ScheduleChangeTimeoutRaceIntegrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Jobs/BookingHangfireRegistrationTests.cs',
  'apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/TripScheduleChangedIntegrationEventHandlerTests.cs',
  'apps/booking/tests/VietRide.Booking.UnitTests/Features/Bookings/HandleScheduleChangeCommandHandlerTests.cs'
)
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.7 focused format failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.7 diff hygiene failed' }
```

### Task 23.8 — Add Notification and shared-contract compatibility for timeout outcomes

| Field | Value |
|---|---|
| stack/owner | nest |
| implement agent | nest-worker |
| review agent | nest-reviewer |
| skill | (none) |
| owned files (base write set) | exact shared contract/export/spec plus Notification schedule consumer/mapper/module, legacy direct-binding compatibility files/specs, and dedicated Day-23 unit/e2e specs in `$changed`. |
| auto-expand scope | Exact queue binding/idempotency fixture required by the dedicated specs only; append each path to `$changed`. No persistence model. The shared-index edit appends the auto-resolved export and must preserve the Task-23.5 booking-cancelled schema exports. |
| forbidden scope | Prisma schema/migrations/generated client, new notification type, Booking/Trip/Payment/Gateway code, route-change behavior, dependencies, `.env`, secrets, destructive operations, and git operations. |
| depends on | 23.7 |
| parallel-safe | no — dependency requires execution after 23.7, and the shared-index overlap with Task 23.5 is deliberately serialized. |
| verification tier | `FOCUSED` |
| verification commands | Run exact dedicated contract, binding/mapper/dedupe, e2e, build, lint, and diff blocks below. |
| full regression owner | `audit-day` |
| invariant flags | TS LF; no dependency; durable/manual-ack queue; MessageId/event dedupe; Booking-owned passenger messaging; existing `TRIP_SCHEDULE_CHANGED`; no Prisma; preserve route-change behavior. |
| acceptance | Dedicated contract spec proves the exact auto-resolved fields and preserves the exported Task-23.5 canonical/consumer cancellation schemas. Dedicated Notification specs separately prove the new auto-resolved binding, mapper copy/severity behavior, and MessageId dedupe; dedicated e2e proves registration/ACK/redelivery. Notification continues to map the existing Day-22 T+2 `booking.booking.pending_action_realerted` for unresolved `PENDING_SEAT_ASSIGNMENT` and MEDIUM/MAJOR `SCHEDULE_CHANGE`, and also maps the optional distinct Day-23 MAJOR initial-deadline re-alert emitted by `ScheduleChangeAutoAcceptJob`; redelivery is at most once per MessageId while the two intended phase identities remain independently deliverable. Required facts and terminal auto-resolved facts map to existing `TRIP_SCHEDULE_CHANGED`. Direct Trip schedule/cancel passenger bindings are absent while unrelated route/crew behavior remains. `schema.prisma` and migrations are byte-unchanged. No old broad spec can satisfy the gate. |
| source citations | BSOT §7.3 ownership/event registry; technical context §6.13; API Day-22 facts; current contracts and Notification consumers/mappers. |

```powershell
function Invoke-Day23Jest([string]$Config,[string]$Path,[string]$Pattern,[string]$Name) {
  $o=Join-Path ([IO.Path]::GetTempPath()) ("vr23-$Name-"+[guid]::NewGuid()+'.json')
  npx jest --config $Config --runInBand --ci --runTestsByPath $Path --testNamePattern $Pattern --json --outputFile $o
  if ($LASTEXITCODE -ne 0) { throw "$Name failed" }
  try {
    $j=Get-Content -Raw $o | ConvertFrom-Json -ErrorAction Stop
  } catch {
    throw "$Name produced invalid Jest JSON: $($_.Exception.Message)"
  }
  if (-not $j.success -or $j.numPassedTests -lt 1 -or $j.numFailedTests -ne 0 -or $j.numPendingTests -ne 0 -or $j.numTodoTests -ne 0) { throw "$Name executed zero/failed/skipped tests" }
}
Invoke-Day23Jest 'libs/shared/contracts/jest.config.cts' 'libs/shared/contracts/src/events/__tests__/day23-schedule-change-events.spec.ts' '^Day 23 schedule-change contract:' 'contracts'
Invoke-Day23Jest 'apps/notification/jest.config.cts' 'apps/notification/src/notifications/day23-schedule-change-notification.spec.ts' '^Day 23 schedule notification: (binding|mapper|dedupe)' 'notification-unit'
Invoke-Day23Jest 'apps/notification/jest.e2e.config.cts' 'apps/notification/src/notifications/day23-schedule-change-notification.e2e-spec.ts' '^Day 23 schedule notification e2e:' 'notification-e2e'

$changed=@(
  'libs/shared/contracts/src/events/booking-pending-action-auto-resolved.event.ts',
  'libs/shared/contracts/src/events/__tests__/day23-schedule-change-events.spec.ts',
  'libs/shared/contracts/src/index.ts',
  'apps/notification/src/notifications/booking-trip-change-events.consumer.ts',
  'apps/notification/src/notifications/booking-trip-change-notification.mapper.ts',
  'apps/notification/src/notifications/notifications.module.ts',
  'apps/notification/src/notifications/booking-trip-change-events.consumer.spec.ts',
  'apps/notification/src/notifications/booking-trip-change-events.consumer.e2e-spec.ts',
  'apps/notification/src/notifications/booking-trip-change-notification.mapper.spec.ts',
  'apps/notification/src/notifications/trip-tracking-alert-events.constants.ts',
  'apps/notification/src/notifications/trip-tracking-alert-events.consumer.ts',
  'apps/notification/src/notifications/trip-tracking-alert-notification.mapper.ts',
  'apps/notification/src/notifications/trip-tracking-alert-events.consumer.spec.ts',
  'apps/notification/src/notifications/trip-tracking-alert-events.consumer.e2e-spec.ts',
  'apps/notification/src/notifications/trip-tracking-alert-notification.mapper.spec.ts',
  'apps/notification/src/notifications/day23-schedule-change-notification.spec.ts',
  'apps/notification/src/notifications/day23-schedule-change-notification.e2e-spec.ts'
)
npx eslint $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.8 changed-file lint failed' }
npx nx build contracts
if ($LASTEXITCODE -ne 0) { throw 'Contracts project build failed' }
npx nx build notification
if ($LASTEXITCODE -ne 0) { throw 'Notification project build failed' }
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.8 diff hygiene failed' }
if (git diff --name-only -- apps/notification/prisma/schema.prisma apps/notification/prisma/migrations) { throw 'Task 23.8 changed Prisma state' }
```

### Task 23.9 — Prove the focused Day-23 journey end to end

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (base write set) | new `scripts/run-day23-schedule-change-local.mjs`; new `scripts/run-day23-schedule-change-local.test.mjs`; `docs/api/postman/vietride.postman_collection.json`; `docs/api/postman/vietride.local.postman_environment.json`; `docs/api/postman/README.md`; new `docs/handoff/evidence/day-23-schedule-change.md` |
| auto-expand scope | Day-23 Postman requests, script-local fixture/helper, and evidence only; append exact expansion to `$changed`. Prior runners may be invoked but not edited. |
| forbidden scope | Production code, hidden clock/job HTTP endpoints, Docker/CI, schemas/migrations, prior-day artifacts, secrets, dependencies, destructive non-fixture data, and git operations. |
| depends on | 23.1, 23.2, 23.3, 23.4, 23.5, 23.6, 23.7, 23.8 |
| parallel-safe | yes by artifact set, but must run last. |
| verification tier | `FOCUSED` |
| verification commands | Run the exact TAP/JSON/focused-run/diff block below. |
| full regression owner | `audit-day` |
| invariant flags | JS/JSON/Markdown LF; Gateway-only URLs; production clock; UUID-v4 idempotency; isolated fixtures/cleanup; exact VND; no wall-clock waits; no full regression. |
| acceptance | Runner uses DriverSchedule `ALL_PENDING` and passenger `/resolve` through Gateway; Postman contains no dedicated Trip schedule or accept/reject alias. It proves all severities/boundaries, projection-only versus CONFIRMED facts/actions, CAS quarantine, current reads, masking and every error code, replay/new-key conflicts, accept, 50%/100% reject, canonical cancelled identity, timeout phases/races, Notification rows, Outbox ids, and cleanup. Frozen-clock PostgreSQL evidence is referenced rather than waiting hours or adding a hidden endpoint. TAP self-tests must have runner exit 0, pass>0, fail/skipped/todo/cancelled all zero. |
| source citations | reconciled Day-23 SOT; DriverSchedule and pending resolver contracts; ADR 0004; Day-22 runner/Postman cleanup conventions; focused evidence in Tasks 23.3–23.8. |

```powershell
node --check scripts/run-day23-schedule-change-local.mjs
if ($LASTEXITCODE -ne 0) { throw 'Day-23 runner syntax check failed' }

$tap=node --test --test-reporter=tap scripts/run-day23-schedule-change-local.test.mjs 2>&1
$code=$LASTEXITCODE
$tap | Write-Output
$s=$tap -join [Environment]::NewLine
$required=@(
  '(?m)^# tests [1-9][0-9]*\s*$',
  '(?m)^# pass [1-9][0-9]*\s*$',
  '(?m)^# fail 0\s*$',
  '(?m)^# cancelled 0\s*$',
  '(?m)^# skipped 0\s*$',
  '(?m)^# todo 0\s*$'
)
if ($code -ne 0) { throw "Day-23 TAP runner exited $code" }
foreach($pattern in $required) { if ($s -notmatch $pattern) { throw "Day-23 TAP summary missing $pattern" } }

try {
  $null=Get-Content -Raw docs/api/postman/vietride.postman_collection.json | ConvertFrom-Json -ErrorAction Stop
  $null=Get-Content -Raw docs/api/postman/vietride.local.postman_environment.json | ConvertFrom-Json -ErrorAction Stop
} catch {
  throw "Day-23 Postman artifact contains invalid JSON: $($_.Exception.Message)"
}

node scripts/run-day23-schedule-change-local.mjs --focused
if ($LASTEXITCODE -ne 0) { throw 'Focused Day-23 journey failed' }

$changed=@(
  'scripts/run-day23-schedule-change-local.mjs',
  'scripts/run-day23-schedule-change-local.test.mjs',
  'docs/api/postman/vietride.postman_collection.json',
  'docs/api/postman/vietride.local.postman_environment.json',
  'docs/api/postman/README.md',
  'docs/handoff/evidence/day-23-schedule-change.md'
)
git diff --check -- $changed
if ($LASTEXITCODE -ne 0) { throw 'Task 23.9 diff hygiene failed' }
```

## Dispatch order

1. Task 23.0 is the mandatory contract/SOT gate.
2. Tasks 23.1 and 23.2 may run in parallel after 23.0.
3. Task 23.3 starts after 23.1 and may overlap the tail of 23.2. Task 23.5 runs alone because it coordinates four consumers.
4. `23.2 → 23.4`; then `23.5 → 23.6 → 23.7` serializes Booking aggregate/state-machine changes.
5. `23.7 → 23.8`; Task 23.9 runs last.
6. `/audit-day 23` alone runs full solution/workspace regression and writes the day checklist.

## Progress tracker

| Task | Status | Commit | Review | Notes |
|---|---|---|---|---|
| 23.0 | ✅ done | this commit | APPROVE | 2026-07-17; one review patch round; DOCS gate green; no scope expansion |
| 23.1 | ✅ done | this commit | APPROVE | 2026-07-17; one review patch round; 4+6+6 focused tests green; no scope expansion |
| 23.2 | ✅ done | this commit | APPROVE | 2026-07-17; one EOL patch round; migration lifecycle + 2+1+2 tests green; human-approved guard correction |
| 23.3 | ✅ done | this commit | APPROVE | 2026-07-17; one review patch round; 14+2+2+6+6 focused tests green; no scope expansion |
| 23.4 | ✅ done | this commit | APPROVE | 2026-07-17; 1 patch round: PostgreSQL microsecond causal/CAS normalization and atomic `updated_at`; 2 authorized test-fixture expansions |
| 23.5 | ✅ done | this commit | APPROVE | 2026-07-17; strict canonical/legacy identity rollout; reviewer-driven semantic/null hardening; ESLint config authorized; 2 Booking scope expansions |
| 23.6 | ✅ done | this commit | APPROVE | 2026-07-17; one review patch round; PostgreSQL commit/rollback/race evidence; 4 producer/repository expansions + authorized ESLint config |
| 23.7 | ✅ done | this commit | APPROVE | 2026-07-17; one review patch round; 40 focused tests green; PostgreSQL race/rollback coverage; 4 test-fixture/callsite expansions |
| 23.8 | ✅ done | this commit | APPROVE | 2026-07-17; 6 dedicated Jest tests green; lint/build/diff/Prisma fence pass; no scope expansion |
| 23.9 | ⬜ todo | — | — | Focused journey evidence |

## Open questions

None. The human-approved architecture and the exact error mapping above resolve all prior blockers. Operator seat assignment remains a nonblocking future contract outside Day 23.
