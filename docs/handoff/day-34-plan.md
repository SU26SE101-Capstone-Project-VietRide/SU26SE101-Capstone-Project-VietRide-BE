# Day 34 - Plan

- **Timeline ref**: `BE_TIMELINE_VU.md` -> Day 34 - Vehicle Substitution + BookingTransfer (Jira: SCV-114)
- **Prior checklist**: `docs/handoff/day-33-checklist.md` (found; Day 33 closed with no functional carry-over)
- **Plan status**: `APPROVED`
  <!-- Replace the value, do not append statuses. Allowed lifecycle:
  DRAFT | REVISION-REQUIRED | REVIEWER-APPROVED - AWAITING HUMAN | APPROVED -->

## Objective

Deliver the in-progress Vehicle Substitution flow: terminalize the original Trip as `DISRUPTED`, create a dedicated replacement Trip, transfer each eligible Booking passenger with an immutable seat-history record, and let assigned replacement crew confirm passengers individually. The flow must remain tenant-isolated, idempotent, transactional within each service, and eventually consistent through Outbox events. This unblocks Day 35 parcel transfer and no-substitution disruption work without coupling Booking or Parcel to the Trip database.

## Success criteria (DoD - binary, verifiable)

- [ ] One authorized substitution mutation on an eligible `IN_PROGRESS` Trip creates exactly one dedicated replacement Trip and marks the original Trip `DISRUPTED` with `hasSubstitution=true`.
- [ ] Every eligible Passenger of every eligible Booking has exactly one transfer record for this substitution occurrence, including original/new Trip and original/new seat history; an event replay creates no duplicate effects.
- [ ] Replacement crew can confirm physical transfer per Passenger through the dedicated BookingTransfer confirmation endpoint; a five-passenger fixture can persist exactly three `CONFIRMED` and two `PENDING_CONFIRM` transfer rows without changing sibling rows.
- [ ] Cross-tenant Trip, Vehicle, crew, Booking, and confirmation attempts fail closed without mutation or Outbox side effects.
- [ ] Trip and Booking business writes plus their respective Outbox rows are atomic; publisher restart preserves routing key and `MessageId == EventId`.

## Contract changes

The human approved the complete Day-34 architecture decision set on 2026-07-25, including the explicit choice that a non-`IN_PROGRESS` substitution uses the dedicated `TRIP_NOT_SUBSTITUTABLE` error at HTTP `409`. On the same date, after implementation discovery proved that Booking has no authoritative seat-type snapshot, the human confirmed that the Booking impact seam omits `seatType`; Trip derives a preference from its old `TripSeat` when possible and otherwise uses deterministic fallback. Task 34.0 must codify the decision freeze below into the technical context, API contract, BSOT registries, timeline, and canonical DDL/READMEs before feature workers start. Tasks 34.1-34.8 implement only the contracts that Task 34.0 codifies. Day-35 Parcel transfer remains out of scope.

Sources: `BE_TIMELINE_VU.md` Day 34; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12; `VietRide_API_Contract_v1.md` sections `POST /v1/operator/trips/{tripId}/substitute-vehicle` and `POST /v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}`; `BACKEND_SOURCE_OF_TRUTH.md` sections 5.9 and 7.3; `db-schema/trip-route-vehicle/schema.sql` (`trip_status`, `trip_source`, `trips`, `trip_seats`, `trip_stops`); `db-schema/booking/schema.sql` (`passengers`, `booking_transfers`).

### Approved Day-34 decision freeze (human-approved 2026-07-25)

1. **Replacement lifecycle and recovery timeline.** The original Trip must be `IN_PROGRESS`; any other status returns the substitution-only `409 TRIP_NOT_SUBSTITUTABLE`. The existing `TRIP_NOT_IN_PROGRESS` registry mapping remains HTTP `422` for depart-stop, arrival, incident, and every pre-Day-34 lifecycle context; Task 34.0 adds the dedicated substitution code without rewriting the existing row or prior endpoint contracts. The substitution locks/reloads the old Trip, captures one `disruptedAt`, and requires `estimatedRecoveryDepartureAt` to be strictly later than that locked value. Equality or an earlier timestamp returns `422 VALIDATION_ERROR` with exact field error `fields.estimatedRecoveryDepartureAt = ["must be later than disruptedAt"]`; neither failure writes Trip children, audit, or Outbox rows. A valid substitution transitions the original Trip to terminal `DISRUPTED` and sets `hasSubstitution=true`. The replacement Trip starts `BOARDING`, has `source=VEHICLE_SUBSTITUTION`, and uses `departureDateTime=estimatedRecoveryDepartureAt`. `recoveryDelay = estimatedRecoveryDepartureAt - disruptedAt`; the replacement destination ETA and every copied old `PENDING` TripStop `estimatedArrivalTime` equal the corresponding old baseline plus `recoveryDelay`. No non-`PENDING` TripStop is copied. The assigned replacement Driver uses the existing start flow to transition `BOARDING -> IN_PROGRESS` and capture `actualDepartureTime`; no new Trip status is introduced.
2. **Substitution HTTP contract.** `POST /v1/operator/trips/{tripId}/substitute-vehicle` is `OPERATOR_ADMIN`-only and requires a UUID-v4 `Idempotency-Key`. The strict request is exactly `{replacementVehicleId,estimatedRecoveryDepartureAt,reason,notifyPassengers,replacementCrew}`: `replacementVehicleId` is UUID; `estimatedRecoveryDepartureAt` is an absolute UTC timestamp; `reason` is required, trimmed, and at most 500 characters; `notifyPassengers` defaults to `true`; `replacementCrew` is optional nullable. Absent/null crew copies the old driver and assistant. Present crew is exactly `{driverId,assistantId}` where `driverId` is required UUID and `assistantId` is nullable UUID; both are validated for active role, operator ownership, and existing Trip conflict rules. Unknown fields are rejected, including legacy top-level `newVehicleId`, `estimatedArrivalMinutes`, `driverId`, and `assistantId`. Success data is exactly `{substitutionId,oldTripId,oldTripStatus,newTripId,newTripStatus,newTripDepartureDateTime,transferStatus,affectedBookingCount,affectedPassengerCount,pendingSeatAssignmentCount}` with `oldTripStatus=DISRUPTED`, `newTripStatus=BOARDING`, and `transferStatus=QUEUED`; `substitutionId` equals the canonical `trip.trip.vehicle_substituted` EventId. Counts are respectively eligible Bookings represented in the mapping, mapped `BOARDED|PENDING` Passengers, and mapped Passengers whose `newSeatNumber` is null. No Parcel count is returned.
3. **Booking impact seam and seat ownership.** Booking owns eligibility and exposes raw Internal-JWT-only `GET /internal/v1/bookings/trips/{tripId}/vehicle-substitution-impact?operatorId={operatorId}`. Success is exactly `{oldTripId,operatorId,bookings[]}`; each Booking is exactly `{bookingId,bookingStatus,passengers[]}` and each Passenger is exactly `{passengerId,boardingStatus,originalSeatNumber}`. `originalSeatNumber` is nullable: a chained safety substitution remains eligible even when the Passenger's current seat is still unresolved. Only `CONFIRMED|PARTIAL_NO_SHOW` Bookings and only their `BOARDED|PENDING` Passengers appear; results are ordered by `bookingId`, then `passengerId`; empty is `200` with `bookings:[]`. Trip owns both old/replacement `TripSeat` data, replacement Vehicle layout parsing, replacement `TripSeat` creation/reservation, and the mapping. For a non-null `originalSeatNumber`, Trip resolves the preferred type from the matching old-Trip `TripSeat`; if the seat is null or no old-Trip seat matches, the Passenger has no preferred type and allocation falls back deterministically to the remaining passenger-seat order, then null when exhausted. Trip never writes Booking DB; Booking never writes Trip DB.
4. **Trip facts.** One `trip.trip.vehicle_substituted` fact is emitted per substitution with exact payload `{eventId,occurredAt,substitutionId,disruptedAt,operatorId,oldTripId,oldTripStatus,oldVehicleId,newTripId,newTripStatus,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,actorUserId,reason,notifyPassengers,mappings[]}`. `occurredAt=disruptedAt`, `substitutionId=eventId`, statuses are `DISRUPTED`/`BOARDING`, and each mapping is exactly `{bookingId,passengerId,originalSeatNumber,newSeatNumber,originalBoardingStatus}` with both `originalSeatNumber` and `newSeatNumber` nullable and `originalBoardingStatus=BOARDED|PENDING`. A null original seat never blocks substitution or mapping. The same Trip-local transaction emits canonical `trip.trip.disrupted` with `hasSubstitution=true`. The two EventIds are distinct. For each fact independently, `payload.eventId == Outbox row id == RabbitMQ MessageId`; retry/restart reuses that identity and the exact routing key.
5. **BookingTransfer persistence and confirmation.** `passengers.seat_number` becomes nullable; PostgreSQL `UNIQUE (booking_id,seat_number)` continues to reject duplicate non-null seats while allowing multiple null pending assignments. Both `booking_transfers.original_seat_number` and `booking_transfers.new_seat_number` are nullable so chained substitutions retain real unresolved history without a sentinel. `booking_transfer_confirmation_status` is exactly `PENDING_CONFIRM|CONFIRMED|NOT_REQUIRED`. `booking_transfers` adds non-null `confirmation_status`, nullable `confirmed_at`, nullable logical Identity FK `confirmed_by_user_id`, and unique index `uq_booking_transfers_passenger_trip_pair` on `(passenger_id,original_trip_id,new_trip_id)`. Physical confirmation belongs only to BookingTransfer; it never rewrites Passenger boarding history or Ticket usage. Migration `Down()` must backfill every null `passengers.seat_number` before restoring `NOT NULL`: for each Passenger choose the most recent non-null `booking_transfers.new_seat_number`, otherwise the most recent non-null `original_seat_number`, ordered by `transferred_at DESC, id DESC`; fail `Down()` if any null remains and never invent a sentinel.
6. **Eligibility application.** `CONFIRMED|PARTIAL_NO_SHOW` Bookings are eligible and retain that status. For a mapped old `BOARDED` Passenger, Booking writes the mapped nullable seat and one transfer with `PENDING_CONFIRM`; for old `PENDING`, it writes the mapped nullable seat and one transfer with `NOT_REQUIRED`, after which normal boarding remains unchanged; old `NO_SHOW` is absent from the impact/mapping and gets no replacement seat or transfer. Duplicate delivery creates no additional transfer, state change, or Outbox fact.
7. **Physical confirmation HTTP contract.** `POST /v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm` is bodyless, `DRIVER|ASSISTANT` only, and requires UUID-v4 `Idempotency-Key`. The caller must be assigned to the replacement Trip. The matching active transfer is the row for the Passenger whose Booking currently points to `newTripId`; it must have non-null `newSeatNumber`. Success data is exactly `{bookingTransferId,passengerId,newTripId,confirmationStatus,confirmedAt,confirmedByUserId}`. First confirmation sets only that transfer to `CONFIRMED`; middleware replay and an already-confirmed request return idempotent `200` with persisted confirmation values. Missing/inactive transfer is `404 BOOKING_TRANSFER_NOT_FOUND`; null replacement seat is `409 BOOKING_TRANSFER_SEAT_PENDING`; invalid route input is `422 VALIDATION_ERROR`; unassigned crew is `403 FORBIDDEN`.
8. **Booking notification fact.** Booking emits exactly one canonical `booking.booking.transferred` fact per eligible Booking per substitution, even when `notifyPassengers=false`. Payload is exactly `{eventId,occurredAt,sourceSubstitutionEventId,bookingId,recipientUserId,operatorId,oldTripId,newTripId,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,notifyPassengers,transfers[]}`; `sourceSubstitutionEventId` equals the consumed `trip.trip.vehicle_substituted.eventId`; `recipientUserId` is exactly `Booking.passengerUserId`; each transfer is exactly `{passengerId,originalSeatNumber,newSeatNumber,confirmationStatus}` with both seat values nullable. No Passenger PII or alternate recipient is present. Notification dedupes by MessageId/EventId; when true it creates exactly one Booking-owner `VEHICLE_SUBSTITUTED` notification, and when false it creates no notification/push.

## Tasks

### Required per-task baseline protocol for Tasks 34.1-34.8

Immediately before dispatching each task, the orchestrator must capture the current dirty/untracked path hashes into a JSON manifest outside the repository and pass its absolute path to the worker as `VIETRIDE_TASK_BASELINE_FILE`. This is the task boundary used by the exact verification block: unchanged prior-task/user changes are ignored, but a current-task modification to any pre-existing dirty path changes its hash and is attributed to the current task. The manifest must be captured after the preceding task finishes and before the current worker edits anything. Use the following exact capture commands with the current task id:

```powershell
$ErrorActionPreference = 'Stop'
$taskId = '<replace-with-34.1-through-34.8>'
$workspace = (Get-Location).Path
$dirtyAtDispatch = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) |
  Where-Object { $_ } |
  ForEach-Object { $_.Replace('\','/') } |
  Sort-Object -Unique
$entries = @($dirtyAtDispatch | ForEach-Object {
  $relative = $_
  $absolute = Join-Path $workspace $relative
  $exists = Test-Path -LiteralPath $absolute -PathType Leaf
  [ordered]@{
    path = $relative
    exists = $exists
    sha256 = if ($exists) { (Get-FileHash -Algorithm SHA256 -LiteralPath $absolute).Hash } else { $null }
  }
})
$baselineFile = Join-Path ([System.IO.Path]::GetTempPath()) "vietride-day34-$taskId-$([guid]::NewGuid()).json"
$baselineJson = [ordered]@{ taskId = $taskId; workspace = $workspace; entries = $entries } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($baselineFile, $baselineJson, [Text.UTF8Encoding]::new($false))
$env:VIETRIDE_TASK_BASELINE_FILE = $baselineFile
Write-Host "VIETRIDE_TASK_BASELINE_FILE=$baselineFile"
```

The orchestrator must inject that exact path into the worker verification shell. Each task block rejects a missing/stale/wrong-task manifest, calculates its actual changed paths from path existence/hash deltas, rejects any current-task path outside the task envelope, and prints the actual-scope ledger. For every printed path that is not in the base write set, the worker must also include the required `path + reason + acceptance/citation or reviewer finding` auto-expansion entry in its handoff; a hash-ledger path alone is not an expansion reason. The baseline file is verification metadata only; it must never be added to the repository.

### Task 34.0 - Codify the approved Vehicle Substitution contracts and registries

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | `worker` |
| review agent | `reviewer` |
| skill | (none) |
| owned files (base write set) | `BE_TIMELINE_VU.md`; `SU26SE101_VIETRIDE_technical_context_v7.md`; `VietRide_API_Contract_v1.md`; `BACKEND_SOURCE_OF_TRUTH.md`; `db-schema/trip-route-vehicle/schema.sql`; `db-schema/trip-route-vehicle/README.md`; `db-schema/booking/schema.sql`; `db-schema/booking/README.md` |
| auto-expand scope | Only the cited Day-34 paragraphs, the two approved public endpoint sections, the approved internal endpoint registry row, the additive `TRIP_NOT_SUBSTITUTABLE` error-registry row plus preservation wording for the existing `TRIP_NOT_IN_PROGRESS` row, approved event registry rows, exact schema enum/columns/index/comments, README entity/index/cross-service notes, and one BSOT changelog row needed to copy the decision freeze without semantic expansion. |
| forbidden scope | `.env`, secrets, `API-Response.md` (pre-existing untracked user file), application/service code, migrations, tests, unrelated services/docs, new dependencies, unresolved business/API/schema decisions, destructive operations, and all git operations (branch/commit/push). |
| depends on | none |
| parallel-safe | no |
| verification tier | `DOCS` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | LF for Markdown/SQL; source hierarchy is technical context (business) > API contract (HTTP shape) > BSOT (implementation) > ADRs > timeline > DDL; error codes are registered UPPER_SNAKE_CASE; routing keys are `<service>.<aggregate>.<verb_past>`; no cross-DB FK; no new Trip status; no Day-35 Parcel count/behavior. |
| acceptance | Every SOT copies the approved decision freeze exactly: the replacement is `BOARDING` with the approved strict-after-locked-`disruptedAt` boundary, exact `422 VALIDATION_ERROR` field error, substitution-only `409 TRIP_NOT_SUBSTITUTABLE`, UTC/recovery formulas, and existing start transition; the existing `422 TRIP_NOT_IN_PROGRESS` registry row and all prior depart-stop/arrival/incident and other lifecycle contracts remain unchanged. Both HTTP contracts, strict fields, roles, UUID-v4 idempotency, responses, and errors are explicit; the internal impact raw shape and ownership are registered with nullable original seat and chained-substitution eligibility; both event rows have exact payload/cardinality/consumers, nullable original/new seats, and identity rule; Booking eligibility and Passenger treatment are exact; Booking DDL makes Passenger and both transfer seat-history values nullable and adds the exact confirmation enum/columns/unique triple without a cross-DB FK; the deterministic fail-closed Down backfill rule is documented; Notification cardinality/recipient/suppression and Day-35 deferral are explicit. One BSOT changelog row records the additive Day-34 code/contract freeze, and no unresolved Day-34 wording remains. |
| source citations | Approved Day-34 decision freeze in this plan; `BE_TIMELINE_VU.md` Day 34 (lines 371-378); `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 (lines 3937-4053, 4125-4139), updated by 34.0 to use dedicated `TRIP_NOT_SUBSTITUTABLE`; current `VietRide_API_Contract_v1.md` substitute-vehicle section (lines 2398-2425), updated by 34.0 to use `409 TRIP_NOT_SUBSTITUTABLE`, and existing boarding/depart-stop/arrival contracts as preservation controls; `BACKEND_SOURCE_OF_TRUTH.md` sections 5.6 and 5.9 (`TRIP_NOT_SUBSTITUTABLE` 409 additive; existing `TRIP_NOT_IN_PROGRESS` 422 preserved), 7.2-7.4, 8.1-8.2; `db-schema/trip-route-vehicle/schema.sql` `trip_status`, `trip_source`, `trips`, `trip_seats`, `trip_stops`; `db-schema/booking/schema.sql` `booking_status`, `passenger_boarding_status`, `passengers`, `booking_transfers`; both service schema READMEs. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-RequiredMatch([string]$Text,[string]$Pattern,[string]$Name) {
  $match = [regex]::Match($Text,$Pattern)
  if (-not $match.Success) { throw "$Name missing" }
  return $match
}
function Assert-ExactProperties($Object,[string[]]$Expected,[string]$Name) {
  $actual = @($Object.PSObject.Properties.Name | Sort-Object)
  $wanted = @($Expected | Sort-Object)
  $delta = @(Compare-Object -ReferenceObject $wanted -DifferenceObject $actual)
  if ($delta.Count -gt 0) { throw "$Name fields differ: $($delta | Out-String)" }
}
function Assert-Patterns([string]$Text,[string[]]$Patterns,[string]$Name) {
  foreach ($pattern in $Patterns) {
    if ($Text -notmatch $pattern) { throw "$Name missing exact rule /$pattern/" }
  }
}
function Assert-LfOnly([string[]]$Paths) {
  foreach ($path in @($Paths | Where-Object { $_ -match '\.(md|sql)$' -and (Test-Path -LiteralPath $_ -PathType Leaf) })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($bytes -contains 13) { throw "$path is not LF-only" }
  }
}
$owned = @(
  'BE_TIMELINE_VU.md',
  'SU26SE101_VIETRIDE_technical_context_v7.md',
  'VietRide_API_Contract_v1.md',
  'BACKEND_SOURCE_OF_TRUTH.md',
  'db-schema/trip-route-vehicle/schema.sql',
  'db-schema/trip-route-vehicle/README.md',
  'db-schema/booking/schema.sql',
  'db-schema/booking/README.md'
)
$api = Get-Content -Raw 'VietRide_API_Contract_v1.md'
$bsot = Get-Content -Raw 'BACKEND_SOURCE_OF_TRUTH.md'
$technical = Get-Content -Raw 'SU26SE101_VIETRIDE_technical_context_v7.md'
$timeline = Get-Content -Raw 'BE_TIMELINE_VU.md'
$tripSchema = Get-Content -Raw 'db-schema/trip-route-vehicle/schema.sql'
$bookingSchema = Get-Content -Raw 'db-schema/booking/schema.sql'
$bookingReadme = Get-Content -Raw 'db-schema/booking/README.md'

$substitute = Get-RequiredMatch $api '(?ms)^### POST `/v1/operator/trips/\{tripId\}/substitute-vehicle`\s*(?<body>.*?)(?=^### |^## |\z)' 'API substitute-vehicle section'
$substituteBody = $substitute.Groups['body'].Value
$substituteRequest = Get-RequiredMatch $substituteBody '(?ms)^Request(?: is exactly)?:\s*```jsonc?\s*(?<json>.*?)^```' 'substitute request JSON'
try { $substituteRequestObject = $substituteRequest.Groups['json'].Value | ConvertFrom-Json } catch { throw "substitute request JSON is not parseable: $($_.Exception.Message)" }
Assert-ExactProperties $substituteRequestObject @('replacementVehicleId','estimatedRecoveryDepartureAt','reason','notifyPassengers','replacementCrew') 'substitute request'
if ($null -eq $substituteRequestObject.replacementCrew) { throw 'substitute request example must expose the exact replacementCrew object fields; prose separately permits null' }
Assert-ExactProperties $substituteRequestObject.replacementCrew @('driverId','assistantId') 'substitute replacementCrew'
Assert-Patterns $substituteBody @(
  'Auth:\s*`?OPERATOR_ADMIN`?-only',
  'Idempotency-Key.*required UUID v4',
  'replacementVehicleId.*required.*UUID',
  'estimatedRecoveryDepartureAt.*required.*absolute UTC',
  'reason.*required.*trimmed.*500',
  'notifyPassengers.*optional.*defaults? to `?true`?',
  'replacementCrew.*optional.*nullable',
  'driverId.*required UUID',
  'assistantId.*nullable UUID',
  '(?s)replacementCrew.*absent.*null.*copies.*old driver.*assistant',
  '(?s)replacementCrew.*active role.*operator ownership.*Trip conflict',
  'Unknown fields are rejected',
  '(?s)newVehicleId.*estimatedArrivalMinutes.*top-level `?driverId`?.*top-level `?assistantId`?',
  '(?s)estimatedRecoveryDepartureAt.*strictly later than.*locked.*disruptedAt',
  'fields\.estimatedRecoveryDepartureAt\s*=\s*\["must be later than disruptedAt"\]',
  'Statuses:\s*`?200`?\s*,\s*`?401`?\s*,\s*`?403`?\s*,\s*`?404`?\s*,\s*`?409`?\s*,\s*`?422`?',
  '401\s+`?AUTH_TOKEN_INVALID`?',
  '403\s+`?FORBIDDEN`?',
  '404\s+`?TRIP_NOT_FOUND`?',
  '409\s+`?TRIP_NOT_SUBSTITUTABLE`?',
  '409\s+`?TRIP_VEHICLE_CONFLICT`?',
  '422\s+`?VEHICLE_NOT_ACTIVE`?',
  '422\s+`?VALIDATION_ERROR`?',
  '(?s)substitutionId.*trip\.trip\.vehicle_substituted.*eventId',
  '(?s)affectedBookingCount.*eligible Booking.*affectedPassengerCount.*BOARDED\\?\|PENDING.*pendingSeatAssignmentCount.*newSeatNumber.*null',
  '(?s)same-key replay.*persisted.*200'
) 'substitute contract'
$substituteResponse = Get-RequiredMatch $substituteBody '(?ms)^Response `200`.*?```jsonc?\s*(?<json>.*?)^```' 'substitute response JSON'
try { $substituteResponseObject = $substituteResponse.Groups['json'].Value | ConvertFrom-Json } catch { throw "substitute response JSON is not parseable: $($_.Exception.Message)" }
Assert-ExactProperties $substituteResponseObject @('success','statusCode','data','meta') 'substitute response envelope'
Assert-ExactProperties $substituteResponseObject.meta @('traceId','timestamp') 'substitute response meta'
if ($substituteResponseObject.success -ne $true -or [int]$substituteResponseObject.statusCode -ne 200) { throw 'substitute response envelope status mismatch' }
Assert-ExactProperties $substituteResponseObject.data @('substitutionId','oldTripId','oldTripStatus','newTripId','newTripStatus','newTripDepartureDateTime','transferStatus','affectedBookingCount','affectedPassengerCount','pendingSeatAssignmentCount') 'substitute response data'
if ($substituteResponseObject.data.oldTripStatus -ne 'DISRUPTED' -or $substituteResponseObject.data.newTripStatus -ne 'BOARDING' -or $substituteResponseObject.data.transferStatus -ne 'QUEUED') { throw 'substitute response literals mismatch' }
foreach ($forbiddenResponseField in @('parcelTransferCount','bookingTransferCount')) {
  if ($substituteResponseObject.data.PSObject.Properties.Name -contains $forbiddenResponseField) { throw "substitute response contains forbidden $forbiddenResponseField" }
}
$depart = Get-RequiredMatch $api '(?ms)^### POST `/v1/driver/trips/\{tripId\}/stops/\{stopId\}/depart`\s*(?<body>.*?)(?=^### |^## |\z)' 'API depart-stop section'
$departBody = $depart.Groups['body'].Value
Assert-Patterns $departBody @(
  'Trip outside `?IN_PROGRESS`? returns the',
  'existing `?422 TRIP_NOT_IN_PROGRESS`?'
) 'existing API depart-stop lifecycle contract'
$apiOutsideSubstitute = $api.Remove($substitute.Index,$substitute.Length)
if (@([regex]::Matches($apiOutsideSubstitute,'TRIP_NOT_SUBSTITUTABLE')).Count -ne 0) { throw 'TRIP_NOT_SUBSTITUTABLE leaked into an existing API endpoint' }
if (@([regex]::Matches($apiOutsideSubstitute,'TRIP_NOT_IN_PROGRESS')).Count -ne 1) { throw 'existing API lifecycle TRIP_NOT_IN_PROGRESS occurrence count changed' }

$confirm = Get-RequiredMatch $api '(?ms)^### POST `/v1/bookings/trips/\{newTripId\}/transfers/passengers/\{passengerId\}/confirm`\s*(?<body>.*?)(?=^### |^## |\z)' 'API transfer-confirm section'
$confirmBody = $confirm.Groups['body'].Value
Assert-Patterns $confirmBody @(
  'Auth:\s*`?DRIVER`?\s*(?:or|\|)\s*`?ASSISTANT`?',
  'caller.*assigned.*replacement Trip',
  '(?:bodyless|no body)',
  'Idempotency-Key.*required UUID v4',
  'Statuses:\s*`?200`?\s*,\s*`?401`?\s*,\s*`?403`?\s*,\s*`?404`?\s*,\s*`?409`?\s*,\s*`?422`?',
  '401\s+`?AUTH_TOKEN_INVALID`?',
  '403\s+`?FORBIDDEN`?',
  '404\s+`?BOOKING_TRANSFER_NOT_FOUND`?',
  '409\s+`?BOOKING_TRANSFER_SEAT_PENDING`?',
  '422\s+`?VALIDATION_ERROR`?',
  '(?s)same-key replay.*persisted.*200',
  '(?s)already-confirmed.*persisted.*200'
) 'transfer-confirm contract'
$confirmResponse = Get-RequiredMatch $confirmBody '(?ms)^Response `200`.*?```jsonc?\s*(?<json>.*?)^```' 'transfer-confirm response JSON'
try { $confirmResponseObject = $confirmResponse.Groups['json'].Value | ConvertFrom-Json } catch { throw "transfer-confirm response JSON is not parseable: $($_.Exception.Message)" }
Assert-ExactProperties $confirmResponseObject @('success','statusCode','data','meta') 'transfer-confirm response envelope'
Assert-ExactProperties $confirmResponseObject.meta @('traceId','timestamp') 'transfer-confirm response meta'
if ($confirmResponseObject.success -ne $true -or [int]$confirmResponseObject.statusCode -ne 200) { throw 'transfer-confirm response envelope status mismatch' }
Assert-ExactProperties $confirmResponseObject.data @('bookingTransferId','passengerId','newTripId','confirmationStatus','confirmedAt','confirmedByUserId') 'transfer-confirm response data'
if ($confirmResponseObject.data.confirmationStatus -ne 'CONFIRMED') { throw 'transfer-confirm response confirmationStatus mismatch' }

$errorSection = (Get-RequiredMatch $bsot '(?ms)^### 5\.9 Canonical Error Code Registry\s*(?<body>.*?)(?=^### 5\.10|^## 6\.)' 'BSOT section 5.9').Groups['body'].Value
$expectedErrors = [ordered]@{
  TRIP_NOT_FOUND = 404
  TRIP_NOT_IN_PROGRESS = 422
  TRIP_NOT_SUBSTITUTABLE = 409
  TRIP_VEHICLE_CONFLICT = 409
  VEHICLE_NOT_ACTIVE = 422
  BOOKING_TRANSFER_NOT_FOUND = 404
  BOOKING_TRANSFER_SEAT_PENDING = 409
}
foreach ($entry in $expectedErrors.GetEnumerator()) {
  $rows = @($errorSection -split "`r?`n" | Where-Object { $_ -match ('^\|\s*(?:\*\*[^|]+\*\*)?\s*\|\s*`'+[regex]::Escape($entry.Key)+'`\s*\|\s*'+$entry.Value+'\s*\|[^|\r\n]+\|\s*$') })
  if ($rows.Count -ne 1) { throw "BSOT 5.9 must contain exactly one four-column $($entry.Key) / HTTP $($entry.Value) row" }
}
$legacyTripRows = @($errorSection -split "`r?`n" | Where-Object { $_ -match '^\|[^|\r\n]*\|\s*`TRIP_NOT_IN_PROGRESS`\s*\|\s*422\s*\|[^|\r\n]+\|\s*$' })
if ($legacyTripRows.Count -ne 1) { throw 'BSOT 5.9 must preserve exactly one existing TRIP_NOT_IN_PROGRESS / HTTP 422 row' }
if ($legacyTripRows[0] -notmatch '^\|\s*\|\s*`TRIP_NOT_IN_PROGRESS`\s*\|\s*422\s*\|\s*[^|\r\n]*`IN_PROGRESS`\s*\|\s*$') { throw 'BSOT 5.9 existing TRIP_NOT_IN_PROGRESS lifecycle mapping changed' }
$substitutionRows = @($errorSection -split "`r?`n" | Where-Object { $_ -match '^\|[^|\r\n]*\|\s*`TRIP_NOT_SUBSTITUTABLE`\s*\|\s*409\s*\|[^|\r\n]+\|\s*$' })
if ($substitutionRows.Count -ne 1 -or $substitutionRows[0] -notmatch '(?i)substitut') { throw 'BSOT 5.9 must register exactly one substitution-specific TRIP_NOT_SUBSTITUTABLE / HTTP 409 row' }

$internalSection = (Get-RequiredMatch $bsot '(?ms)^### 7\.2 HTTP internal endpoint registry.*?(?<body>.*?)(?=^### 7\.3 )' 'BSOT section 7.2').Groups['body'].Value
$impactRows = @($internalSection -split "`r?`n" | Where-Object { $_ -match '^\|\s*`GET /internal/v1/bookings/trips/\{tripId\}/vehicle-substitution-impact\?operatorId=\{operatorId\}`\s*\|\s*Trip\s*\|' })
if ($impactRows.Count -ne 1) { throw 'BSOT 7.2 must contain exactly one Booking vehicle-substitution-impact row with Trip caller' }
$impactDetails = (($impactRows[0] -replace '^\|[^|]+\|[^|]+\|','') -replace '\|\s*$','')
$impactCompact = $impactDetails -replace '[\s`]',''
$impactShape = '{oldTripId,operatorId,bookings:[{bookingId,bookingStatus,passengers:[{passengerId,boardingStatus,originalSeatNumber}]}]}'
if ($impactCompact -notmatch [regex]::Escape($impactShape)) { throw 'BSOT 7.2 impact raw success shape is not exact' }
Assert-Patterns $impactDetails @(
  'Internal-JWT-only',
  'CONFIRMED\\?\|PARTIAL_NO_SHOW',
  'BOARDED\\?\|PENDING',
  'originalSeatNumber.*nullable',
  'bookingId.*passengerId.*order',
  'empty.*200.*bookings:\[\]',
  '401.*AUTH_TOKEN_INVALID',
  '422.*VALIDATION_ERROR',
  'tripId.*operatorId.*predicate',
  'no PII'
) 'BSOT 7.2 impact row'

$eventSection = (Get-RequiredMatch $bsot '(?ms)^### 7\.3 RabbitMQ event registry\s*(?<body>.*?)(?=^### 7\.4 )' 'BSOT section 7.3').Groups['body'].Value
function Get-EventRow([string]$Key) {
  $rows = @($eventSection -split "`r?`n" | Where-Object { $_ -match ('^\|\s*`'+[regex]::Escape($Key)+'`\s*\|') })
  if ($rows.Count -ne 1) { throw "BSOT 7.3 must contain exactly one $Key row" }
  $row = Get-RequiredMatch $rows[0] ('^\|\s*`'+[regex]::Escape($Key)+'`\s*\|\s*(?<producer>[^|]+?)\s*\|\s*(?<consumers>[^|]+?)\s*\|\s*(?<payload>.*)\|\s*$') "BSOT 7.3 $Key row shape"
  return $row
}
$tripEvent = Get-EventRow 'trip.trip.vehicle_substituted'
if ($tripEvent.Groups['producer'].Value.Trim() -ne 'Trip' -or $tripEvent.Groups['consumers'].Value.Trim() -ne 'Booking, Parcel (Day 35)') { throw 'trip.trip.vehicle_substituted producer/consumers mismatch' }
$tripEventPayload = $tripEvent.Groups['payload'].Value
Assert-Patterns $tripEventPayload @(
  'Exact\s+`?\{\s*eventId\s*,\s*occurredAt\s*,\s*substitutionId\s*,\s*disruptedAt\s*,\s*operatorId\s*,\s*oldTripId\s*,\s*oldTripStatus\s*,\s*oldVehicleId\s*,\s*newTripId\s*,\s*newTripStatus\s*,\s*newVehicleId\s*,\s*newVehiclePlateNumber\s*,\s*newTripDepartureDateTime\s*,\s*actorUserId\s*,\s*reason\s*,\s*notifyPassengers\s*,\s*mappings:\s*\[\s*\{\s*bookingId\s*,\s*passengerId\s*,\s*originalSeatNumber\s*,\s*newSeatNumber\s*,\s*originalBoardingStatus\s*\}\s*\]\s*\}`?',
  'exactly one.*per substitution',
  'occurredAt\s*=\s*disruptedAt',
  'substitutionId\s*=\s*eventId',
  'originalSeatNumber.*nullable',
  'newSeatNumber.*nullable',
  'payload\.eventId\s*==\s*Outbox row id\s*==\s*RabbitMQ MessageId',
  'oldTripStatus.*DISRUPTED',
  'newTripStatus.*BOARDING',
  'originalBoardingStatus.*BOARDED\\?\|PENDING'
) 'trip.trip.vehicle_substituted registry row'
if ($tripEventPayload -match 'affectedBookingIds|passengerName|passengerPhone|passengerEmail') { throw 'trip.trip.vehicle_substituted row contains forbidden payload fields' }

$bookingEvent = Get-EventRow 'booking.booking.transferred'
if ($bookingEvent.Groups['producer'].Value.Trim() -ne 'Booking' -or $bookingEvent.Groups['consumers'].Value.Trim() -ne 'Notification') { throw 'booking.booking.transferred producer/consumer mismatch' }
$bookingEventPayload = $bookingEvent.Groups['payload'].Value
Assert-Patterns $bookingEventPayload @(
  'Exact\s+`?\{\s*eventId\s*,\s*occurredAt\s*,\s*sourceSubstitutionEventId\s*,\s*bookingId\s*,\s*recipientUserId\s*,\s*operatorId\s*,\s*oldTripId\s*,\s*newTripId\s*,\s*newVehicleId\s*,\s*newVehiclePlateNumber\s*,\s*newTripDepartureDateTime\s*,\s*notifyPassengers\s*,\s*transfers:\s*\[\s*\{\s*passengerId\s*,\s*originalSeatNumber\s*,\s*newSeatNumber\s*,\s*confirmationStatus\s*\}\s*\]\s*\}`?',
  'exactly one.*per eligible Booking.*per substitution',
  'sourceSubstitutionEventId.*consumed.*trip\.trip\.vehicle_substituted.*eventId',
  'originalSeatNumber.*nullable',
  'newSeatNumber.*nullable',
  'confirmationStatus.*PENDING_CONFIRM\\?\|CONFIRMED\\?\|NOT_REQUIRED',
  'payload\.eventId\s*==\s*Outbox row id\s*==\s*RabbitMQ MessageId',
  'recipientUserId.*Booking\.passengerUserId',
  'notifyPassengers=false.*fact'
) 'booking.booking.transferred registry row'
if ($bookingEventPayload -match 'passengerName|passengerPhone|passengerEmail|recipientUserIds') { throw 'booking.booking.transferred row contains Passenger PII or alternate recipients' }

$technicalDay34 = (Get-RequiredMatch $technical '(?ms)^### 6\.12 Trip DISRUPTED.*?(?<body>.*?)(?=^#### 6\.12\.1 )' 'technical context section 6.12').Groups['body'].Value
Assert-Patterns $technicalDay34 @(
  'estimatedRecoveryDepartureAt.*strictly later than.*disruptedAt',
  'recoveryDelay\s*=\s*estimatedRecoveryDepartureAt\s*-\s*disruptedAt',
  'status\s*=\s*`?BOARDING`?',
  'PARTIAL_NO_SHOW',
  'originalSeatNumber.*nullable',
  'PENDING_CONFIRM',
  'NOT_REQUIRED',
  'transferred_at DESC, id DESC',
  'fail.*Down.*null',
  'sentinel',
  'booking\.booking\.transferred',
  'TRIP_NOT_SUBSTITUTABLE.*409',
  'TRIP_NOT_IN_PROGRESS.*422.*preserv'
) 'technical context 6.12'
$timelineDay34 = (Get-RequiredMatch $timeline '(?ms)^### Day 34 .*?(?<body>.*?)(?=^### Day 35 |\z)' 'timeline Day 34').Groups['body'].Value
Assert-Patterns $timelineDay34 @('BOARDING','TRIP_NOT_SUBSTITUTABLE.*409','TRIP_NOT_IN_PROGRESS.*422.*preserv','originalSeatNumber.*nullable','booking\.booking\.transferred','Day 35.*Parcel') 'timeline Day 34'

$tripStatusEnum = (Get-RequiredMatch $tripSchema "(?is)CREATE TYPE trip_status AS ENUM\s*\((?<body>.*?)\);" 'trip_status DDL').Groups['body'].Value
if (@([regex]::Matches($tripStatusEnum,"'DISRUPTED'")).Count -ne 1) { throw 'trip_status must contain DISRUPTED exactly once' }
$tripSourceEnum = (Get-RequiredMatch $tripSchema "(?is)CREATE TYPE trip_source AS ENUM\s*\((?<body>.*?)\);" 'trip_source DDL').Groups['body'].Value
if (@([regex]::Matches($tripSourceEnum,"'VEHICLE_SUBSTITUTION'")).Count -ne 1) { throw 'trip_source must contain VEHICLE_SUBSTITUTION exactly once' }
$tripsTable = (Get-RequiredMatch $tripSchema '(?is)CREATE TABLE trips\s*\((?<body>.*?)\);' 'trips DDL').Groups['body'].Value
if ($tripsTable -notmatch '(?m)^\s*has_substitution\s+BOOLEAN\s+NOT NULL\s+DEFAULT FALSE') { throw 'trips.has_substitution DDL mismatch' }

$confirmationEnum = (Get-RequiredMatch $bookingSchema '(?is)CREATE TYPE booking_transfer_confirmation_status AS ENUM\s*\((?<body>.*?)\);' 'booking transfer confirmation enum DDL').Groups['body'].Value
if (($confirmationEnum -replace '\s','') -ne "'PENDING_CONFIRM','CONFIRMED','NOT_REQUIRED'") { throw 'confirmation enum values/order mismatch' }
$passengersTable = (Get-RequiredMatch $bookingSchema '(?is)CREATE TABLE passengers\s*\((?<body>.*?)\);' 'passengers DDL').Groups['body'].Value
if ($passengersTable -notmatch '(?m)^\s*seat_number\s+VARCHAR\(20\)\s+NULL\s*,?\s*$') { throw 'passengers.seat_number is not nullable' }
if ($bookingSchema -notmatch '(?im)^CREATE UNIQUE INDEX\s+uq_passengers_booking_seat\s+ON\s+passengers\s*\(\s*booking_id\s*,\s*seat_number\s*\)\s*;') { throw 'passenger non-null seat uniqueness index mismatch' }
$transfersTable = (Get-RequiredMatch $bookingSchema '(?is)CREATE TABLE booking_transfers\s*\((?<body>.*?)\);' 'booking_transfers DDL').Groups['body'].Value
Assert-Patterns $transfersTable @(
  '(?m)^\s*original_seat_number\s+VARCHAR\(20\)\s+NULL\s*,?\s*$',
  '(?m)^\s*new_seat_number\s+VARCHAR\(20\)\s+NULL\s*,?\s*$',
  '(?m)^\s*confirmation_status\s+booking_transfer_confirmation_status\s+NOT NULL',
  '(?m)^\s*confirmed_at\s+TIMESTAMPTZ\s+NULL',
  '(?m)^\s*confirmed_by_user_id\s+UUID\s+NULL'
) 'booking_transfers DDL'
if ($transfersTable -match 'FOREIGN KEY\s*\([^)]*(original_trip_id|new_trip_id|transferred_by_user_id|confirmed_by_user_id)') { throw 'booking_transfers contains a cross-service FK' }
if ($bookingSchema -notmatch '(?im)^CREATE UNIQUE INDEX\s+uq_booking_transfers_passenger_trip_pair\s+ON\s+booking_transfers\s*\(\s*passenger_id\s*,\s*original_trip_id\s*,\s*new_trip_id\s*\)\s*;') { throw 'canonical transfer unique index mismatch' }
$migrationSection = (Get-RequiredMatch $bookingReadme '(?ms)^## Migration Strategy\s*(?<body>.*?)(?=^## |\z)' 'Booking README Migration Strategy').Groups['body'].Value
Assert-Patterns $migrationSection @('new_seat_number.*otherwise.*original_seat_number','transferred_at DESC, id DESC','fail.*Down.*null','never.*sentinel','SET NOT NULL') 'Booking README Migration Strategy'

$changelogSection = (Get-RequiredMatch $bsot '(?ms)^## 13\. Changelog\s*(?<body>.*?)(?=^## Appendix A|\z)' 'BSOT section 13').Groups['body'].Value
$changelogRows = @($changelogSection -split "`r?`n" | Where-Object { $_ -match '^\|.*(?:Day-?34|SCV-114).*\|\s*$' })
if ($changelogRows.Count -ne 1) { throw "BSOT 13 must contain exactly one Day-34/SCV-114 changelog row, found $($changelogRows.Count)" }
if ($changelogRows[0] -notmatch '^\|\s*\*\*[0-9]+\.[0-9]+\.[0-9]+\*\*\s*\|\s*2026-07-25\s*\|\s*[^|]+\|\s*\*\*MINOR\*\*.*(?:Day-?34|SCV-114).*\|\s*$') { throw 'BSOT 13 Day-34 changelog row shape/date/version mismatch' }

$allDirty = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | Sort-Object -Unique
$outside = @($allDirty | Where-Object { $_ -notin @('API-Response.md','docs/handoff/day-34-plan.md') -and $_ -notin $owned })
if ($outside.Count -gt 0) { throw "Task 34.0 changed paths outside its envelope: $($outside -join ', ')" }
$taskLedger = @($allDirty | Where-Object { $_ -in $owned })
if ($taskLedger.Count -lt 1) { throw 'Task 34.0 actual-scope ledger is empty' }
Assert-LfOnly $taskLedger
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'docs diff check failed' }
```

### Task 34.1 - Freeze shared Vehicle Substitution event contracts

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | `worker` |
| review agent | `reviewer` |
| skill | (none) |
| owned files (base write set) | `libs/shared/contracts/src/events/trip-vehicle-substituted.event.ts`; `libs/shared/contracts/src/events/booking-transferred.event.ts`; `libs/shared/contracts/src/events/__tests__/day34-vehicle-substitution-events.spec.ts`; `libs/shared/contracts/src/index.ts` |
| auto-expand scope | Contract-only Zod schemas/types/constants and the single Day-34 contract spec under `libs/shared/contracts/src/events/`; no producer or consumer implementation. |
| forbidden scope | `.env`, secrets, `API-Response.md`, .NET/Nest producer or consumer implementation, service schemas/migrations, unrelated shared contracts, new dependencies, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.0 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | LF for `.ts`; strict Zod objects; routing keys exactly `trip.trip.vehicle_substituted` and `booking.booking.transferred`; timestamps accept offset/UTC strings; UUID fields use UUID validation; both `originalSeatNumber` and `newSeatNumber` are exactly nullable; no service implementation; no new dependency. |
| acceptance | `TripVehicleSubstitutedEventSchema` contains only the exact decision-freeze fields and literals, including `substitutionId=eventId`, `occurredAt=disruptedAt`, statuses, and exact mapping shape with nullable original/new seats and `BOARDED\|PENDING`. `BookingTransferredEventSchema` contains only its exact fields, one `recipientUserId`, the three-value confirmation enum, and exact transfer items with nullable original/new seats. Strict schemas accept null original seats for chained substitutions and reject missing, extra, legacy, alternate-recipient, PII, invalid enum, and wrong-nullability payloads. Both routing constants are exported from `@vietride/contracts`. |
| source citations | Approved Day-34 decision freeze items 4 and 8 in this plan; after 34.0: `BACKEND_SOURCE_OF_TRUTH.md` section 7.3 rows `trip.trip.vehicle_substituted` and `booking.booking.transferred`; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 BookingTransfer/notification flow; current pattern `libs/shared/contracts/src/events/trip-vehicle-swapped.event.ts` and its strict Zod schema. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) |
    Where-Object { $_ } |
    ForEach-Object { $_.Replace('\','/') } |
    Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
$spec = 'libs/shared/contracts/src/events/__tests__/day34-vehicle-substitution-events.spec.ts'
npx jest --config libs/shared/contracts/jest.config.cts --runInBand --passWithNoTests=false $spec
if ($LASTEXITCODE -ne 0) { throw 'Day-34 shared contract spec failed or selected zero tests' }
$allowed = '^libs/shared/contracts/src/(index\.ts|events/(trip-vehicle-substituted|booking-transferred)\.event\.ts|events/__tests__/day34-vehicle-substitution-events\.spec\.ts)$'
$taskLedger = @(Get-Day34TaskLedger '34.1' $allowed)
npx eslint $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'shared-contract changed-file lint failed' }
npx nx build contracts
if ($LASTEXITCODE -ne 0) { throw 'contracts affected-project build failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.2 - Expose the tenant-scoped Booking substitution-impact seam

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | `dotnet-worker` |
| review agent | `dotnet-reviewer` |
| skill | `add-endpoint` |
| owned files (base write set) | `apps/booking/src/VietRide.Booking.Api/Controllers/InternalBookingsController.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Internal/Bookings/GetVehicleSubstitutionImpactQuery.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Internal/Bookings/GetVehicleSubstitutionImpactQueryHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Internal/Bookings/GetVehicleSubstitutionImpactQueryValidator.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Internal/Bookings/VehicleSubstitutionImpactDto.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Features/Internal/Bookings/GetVehicleSubstitutionImpactQueryHandlerTests.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/Internal/VehicleSubstitutionImpactEndpointTests.cs` |
| auto-expand scope | Files in the same `Features/Internal/Bookings` feature, the `IBookingRepository`/`BookingRepository` pair, directly affected Booking unit/integration tests, and DI registration only if the handler is not already discovered by existing Application registration. |
| forbidden scope | `.env`, secrets, `API-Response.md`, Trip/Parcel/Payment/Identity/Notification service files, public Gateway routes, Booking schema/migrations or mutations, new dependencies, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.1 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | CRLF for `.cs`; thin internal controller -> `MediatR.Send`; Swashbuckle documents the raw internal success and ADR 0004 error envelope/statuses; valid Internal JWT only; tenant predicate uses trusted `operatorId` query input; stable `bookingId`/`passengerId` order; no PII; read-only `AsNoTracking`; no Trip callback; no cross-DB FK; MediatR v11; CPM no `Version=`; no new dependency. |
| acceptance | `GET /internal/v1/bookings/trips/{tripId}/vehicle-substitution-impact?operatorId={operatorId}` returns exactly `{oldTripId,operatorId,bookings[]}`. It includes only `CONFIRMED\|PARTIAL_NO_SHOW` Bookings matching both Trip and operator, and only `BOARDED\|PENDING` Passengers once each with exact `{passengerId,boardingStatus,originalSeatNumber}`; `originalSeatNumber` is nullable and an unresolved seat on a chained substitution is returned rather than filtered or rejected. Booking does not invent or return `seatType`; Trip owns that derivation. `NO_SHOW` and terminal/ineligible Bookings are absent. Results are deterministic, empty is raw `200`, malformed UUIDs are `422 VALIDATION_ERROR`, missing/invalid Internal JWT is `401 AUTH_TOKEN_INVALID`, and a foreign operator sees an empty snapshot without any Booking/Passenger/Outbox mutation. |
| source citations | Approved Day-34 decision freeze items 3 and 6 in this plan; after 34.0: `BACKEND_SOURCE_OF_TRUTH.md` section 7.2 internal endpoint row and sections 5.4-5.5; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 Booking transfer/NO_SHOW rules; `db-schema/booking/schema.sql` `bookings` and `passengers`; current mirror pattern `GET /internal/v1/bookings/trips/{tripId}/edit-impact` in `InternalBookingsController`/`GetTripEditImpactQueryHandler`. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-DotNetTestNonZero([string]$Project,[string]$Fqn,[string]$Tag) {
  $name = "day34-$Tag-$([guid]::NewGuid()).trx"
  $trx = Join-Path 'TestResults' $name
  Remove-Item -LiteralPath $trx -Force -ErrorAction SilentlyContinue
  dotnet test $Project --filter "FullyQualifiedName=$Fqn" --results-directory TestResults --logger "trx;LogFileName=$name"
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $Tag" }
  [xml]$xml = Get-Content -LiteralPath $trx
  $c = $xml.TestRun.ResultSummary.Counters
  if ([int]$c.executed -lt 1 -or [int]$c.passed -lt 1 -or [int]$c.failed -ne 0) { throw "zero/failing tests: $Tag" }
}
$project = 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Internal.VehicleSubstitutionImpactEndpointTests.ReturnsExactRawOrderedEligibleSnapshot' 'impact-shape'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Internal.VehicleSubstitutionImpactEndpointTests.RejectsInvalidInternalJwtAndInvalidRouteInput' 'impact-auth-input'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Internal.VehicleSubstitutionImpactEndpointTests.ThinControllerDispatchesMediatRAndDeclaresSwashbuckleRawSuccessAndApiResponseErrorMetadata' 'impact-controller-metadata'
$unit = 'apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj'
Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Features.Internal.Bookings.GetVehicleSubstitutionImpactQueryHandlerTests.FiltersTripOperatorBookingAndPassengerEligibilityWithoutWrites' 'impact-eligibility'
Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Features.Internal.Bookings.GetVehicleSubstitutionImpactQueryHandlerTests.ForeignOperatorAndNoMatchReturnEmptyOrderedSnapshot' 'impact-tenant-empty'
Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Features.Internal.Bookings.GetVehicleSubstitutionImpactQueryHandlerTests.IncludesChainedSubstitutionPassengerWithNullOriginalSeat' 'impact-null-original-seat'
$allowed = '^(apps/booking/src/VietRide\.Booking\.(Api/Controllers/InternalBookingsController\.cs|Application/(Features/Internal/Bookings/(GetVehicleSubstitutionImpact|VehicleSubstitutionImpact).*\.cs|Abstractions/Repositories/IBookingRepository\.cs)|Infrastructure/(Persistence/Repositories/BookingRepository\.cs|DependencyInjection/.*\.cs))|apps/booking/tests/VietRide\.Booking\.(IntegrationTests/Internal/VehicleSubstitutionImpactEndpointTests|UnitTests/Features/Internal/Bookings/GetVehicleSubstitutionImpactQueryHandlerTests)\.cs)$'
$taskLedger = @(Get-Day34TaskLedger '34.2' $allowed)
$csLedger = @($taskLedger | Where-Object { $_ -match '\.cs$' })
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $csLedger
if ($LASTEXITCODE -ne 0) { throw 'changed-file format failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.3 - Complete the Trip substitution mutation and Outbox producer

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | `dotnet-worker` |
| review agent | `dotnet-reviewer` |
| skill | `add-endpoint` + `add-integration-event` |
| owned files (base write set) | `apps/trip/src/VietRide.Trip.Api/Controllers/OperatorTripsController.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/SubstituteVehicleRequest.cs`; `apps/trip/src/VietRide.Trip.Api/Controllers/Requests/ReplacementCrewRequest.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/SubstituteVehicleCommand.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/SubstituteVehicleCommandValidator.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/SubstituteVehicleCommandHandler.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/SubstituteVehicleResponse.cs`; `apps/trip/src/VietRide.Trip.Application/Features/Trips/Operations/TripTerminalIntegrationEvents.cs`; `apps/trip/src/VietRide.Trip.Application/Events/TripVehicleSubstitutedIntegrationEvent.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/IBookingImpactClient.cs`; `apps/trip/src/VietRide.Trip.Application/Abstractions/ExternalClients/VehicleSubstitutionImpactProjection.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Http/BookingImpactClient.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/Http/BookingImpactClientOptions.cs`; `apps/trip/src/VietRide.Trip.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/trip/src/VietRide.Trip.Domain/Constants/TripAuditAction.cs`; `apps/trip/src/VietRide.Trip.Domain/Entities/Trip.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/Trips/SubstituteVehicleEndpointTests.cs`; `apps/trip/tests/VietRide.Trip.IntegrationTests/Events/TripVehicleSubstitutedIntegrationEventTests.cs`; `apps/trip/tests/VietRide.Trip.UnitTests/ExternalClients/BookingImpactClientTests.cs` |
| auto-expand scope | Files in the same Trip substitution feature; required Vehicle/Trip/TripSeat/TripStop/TripStopFare/TripAuditLog repository interface-implementation pairs; affected domain/unit/integration tests; Booking impact client interface/implementation/options/DI pair. The Identity crew-validation client and 34.1 shared contract are read-only dependencies. |
| forbidden scope | `.env`, secrets, `API-Response.md`, Gateway files (the endpoint-specific route/RBAC seam belongs to 34.8), shared contract shape changes, Booking persistence/consumer code, Parcel Day-35 behavior/counts, Payment/Identity/Notification/Tracking/RAG files, new dependencies, Trip schema/migrations or new statuses, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.1, 34.2, 34.8 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | CRLF for `.cs`; thin controller -> MediatR; Swashbuckle documents exact ADR 0004 success/error statuses; strict JSON unknown-field rejection; UUID-v4 `Idempotency-Key`; `OPERATOR_ADMIN`; masked/tenant-safe Trip/Vehicle/crew validation; old Trip exactly `IN_PROGRESS`; replacement exactly `BOARDING`/`VEHICLE_SUBSTITUTION`; existing start flow captures actual departure; no subscription trip-count increment; two distinct producer-allocated EventIds; each `EventId == Outbox row id == RabbitMQ MessageId`; both Outbox rows and business writes commit once; no HTTP call while a Trip DB transaction is open; no cross-DB write/FK/transaction; MediatR v11; CPM no `Version=`; no new dependency. |
| acceptance | Before opening the Trip transaction, the handler obtains the exact tenant-scoped Booking snapshot, including eligible Passengers whose nullable original seat is unresolved. Inside the Trip transaction it locks/reloads the old Trip, requires exact `IN_PROGRESS`, captures one `disruptedAt`, and requires `estimatedRecoveryDepartureAt > disruptedAt`. Equality or an earlier value returns `422 VALIDATION_ERROR` with `fields.estimatedRecoveryDepartureAt = ["must be later than disruptedAt"]`; any non-`IN_PROGRESS` old status returns substitution-only `409 TRIP_NOT_SUBSTITUTABLE`, without changing the existing `422 TRIP_NOT_IN_PROGRESS` registry mapping or behavior used by depart-stop, arrival, incident, and other prior lifecycle endpoints. A valid strict request creates one dedicated `BOARDING` replacement at `estimatedRecoveryDepartureAt`, sets its destination ETA to the old destination ETA plus `recoveryDelay`, derives passenger seats from the active replacement Vehicle layout, and assigns returned Passengers deterministically. For a non-null original seat, its matching old-Trip `TripSeat` supplies the preferred type; a null or unmatched old seat has no preferred type. Allocation takes a preferred-type passenger seat first when available, then the remaining passenger-seat order, then null when exhausted, without rejecting a null original seat. The handler copies only old `PENDING` TripStops with each old baseline plus `recoveryDelay`, copies required fare/pricing/cargo snapshots, transitions the old Trip to `DISRUPTED` with `hasSubstitution=true`, and appends `VEHICLE_SUBSTITUTION_TRIGGERED`. The existing assigned-Driver start endpoint can then transition the replacement to `IN_PROGRESS` and persist `actualDepartureTime`. The same Trip-local commit contains exactly one `trip.trip.vehicle_substituted` Outbox row and one `trip.trip.disrupted` row with distinct ids and exact payloads; mapped original/new seats remain nullable end-to-end. For each fact, an unprocessed row survives publisher restart and is delivered to `vietride.events` with its exact routing key and `MessageId==EventId`. The exact success counts/fields exclude Parcel. Every rejected boundary/status/strict-body/UTC/crew/Vehicle/conflict/tenant/auth/idempotency case leaves no Trip, seat, stop, audit, or Outbox partial effect. |
| source citations | Approved Day-34 decision freeze items 1-4 in this plan; after 34.0: `VietRide_API_Contract_v1.md` substitute-vehicle section (`409 TRIP_NOT_SUBSTITUTABLE`) plus existing depart-stop/arrival lifecycle sections as `422 TRIP_NOT_IN_PROGRESS` preservation controls; `BACKEND_SOURCE_OF_TRUTH.md` sections 5.6, 5.9 (dedicated substitution code plus preserved existing lifecycle code), 7.2-7.4 and 8.2; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 lifecycle/endpoint/audit/seat allocation; `db-schema/trip-route-vehicle/schema.sql` `trip_status`, `trip_source`, `trips`, `trip_seats`, `trip_stops`, `trip_stop_fares`; current start flow `StartTripCommandHandler`; current Vehicle layout pattern `TripGenerationService`/`TripVehicleSwapService`. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-DotNetTestNonZero([string]$Project,[string]$Fqn,[string]$Tag) {
  $name = "day34-$Tag-$([guid]::NewGuid()).trx"
  $trx = Join-Path 'TestResults' $name
  Remove-Item -LiteralPath $trx -Force -ErrorAction SilentlyContinue
  dotnet test $Project --filter "FullyQualifiedName=$Fqn" --results-directory TestResults --logger "trx;LogFileName=$name"
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $Tag" }
  [xml]$xml = Get-Content -LiteralPath $trx
  $c = $xml.TestRun.ResultSummary.Counters
  if ([int]$c.executed -lt 1 -or [int]$c.passed -lt 1 -or [int]$c.failed -ne 0) { throw "zero/failing tests: $Tag" }
}
$project = 'apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.SuccessCreatesBoardingReplacementFromVehicleLayoutAndRecoveryTimeline' 'substitute-success'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.StrictContractAuthCrewAndIdempotencyAreEnforced' 'substitute-http'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.InvalidStateTenantVehicleAndCrewLeaveNoPartialMutation' 'substitute-guards'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.RejectsRecoveryEqualToOrBeforeLockedDisruptedAtWithExactFieldErrorAndNoWrites' 'substitute-recovery-boundary'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.NonInProgressOldTripReturnsTripNotSubstitutableConflictAndNoWritesWhileLifecycleTripNotInProgressRemainsUnchanged' 'substitute-status-conflict'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.ChainedSubstitutionMapsNullOriginalSeatWithoutBlockingSafetyFlow' 'substitute-null-original-seat'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.ThinControllerDispatchesMediatRAndDeclaresApiResponseAndSwashbuckleMetadata' 'substitute-controller-metadata'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Trips.SubstituteVehicleEndpointTests.ReplacementUsesExistingStartFlowAndCapturesActualDepartureTime' 'substitute-start'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Events.TripVehicleSubstitutedIntegrationEventTests.BothFactsAndBusinessMutationAreAtomicWithDistinctCanonicalIdentities' 'substitute-two-outbox'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Events.TripVehicleSubstitutedIntegrationEventTests.VehicleSubstitutedPublisherRestartPreservesExchangeRoutingKeyMessageIdAndPayload' 'substitute-publisher'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Events.TripVehicleSubstitutedIntegrationEventTests.DisruptedPublisherRestartPreservesExchangeRoutingKeyMessageIdAndPayload' 'disrupted-publisher'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Events.TripVehicleSubstitutedIntegrationEventTests.RollbackRemovesTripsChildrenAuditAndBothOutboxRows' 'substitute-rollback'
Invoke-DotNetTestNonZero $project 'VietRide.Trip.IntegrationTests.Events.TripVehicleSubstitutedIntegrationEventTests.SerializedSubstitutionPayloadMatchesSharedContractFieldForField' 'substitute-parity'
$unit = 'apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj'
Invoke-DotNetTestNonZero $unit 'VietRide.Trip.UnitTests.ExternalClients.BookingImpactClientTests.VehicleSubstitutionImpactUsesExactPathAndRawShape' 'substitute-impact-client'
function Invoke-NxJestNonZero([string]$Spec,[string]$Pattern,[string]$Tag) {
  New-Item -ItemType Directory -Force -Path 'TestResults' | Out-Null
  $resultsDir = (Resolve-Path -LiteralPath 'TestResults').Path
  $jsonPath = Join-Path $resultsDir "day34-$Tag-$([guid]::NewGuid()).json"
  try {
    npx nx test gateway --runInBand --passWithNoTests=false --testPathPatterns=$Spec --testNamePattern=$Pattern --json --outputFile=$jsonPath
    if ($LASTEXITCODE -ne 0) { throw "Gateway spec failed: $Tag" }
    $result = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ([int]$result.numTotalTests -lt 1 -or [int]$result.numPassedTests -lt 1 -or [int]$result.numFailedTests -ne 0) { throw "zero/failing Gateway tests: $Tag" }
  }
  finally { Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue }
}
Invoke-NxJestNonZero 'apps/gateway/src/config/routes.spec.ts' 'routes substitute-vehicle to Trip with user auth' 'substitute-gateway-route'
Invoke-NxJestNonZero 'apps/gateway/src/proxy/proxy.access-gates.spec.ts' 'enforces OPERATOR_ADMIN auth and preserves Idempotency-Key for substitute-vehicle' 'substitute-gateway-access'
$allowed = '^(apps/trip/src/VietRide\.Trip\.(Api/Controllers/(OperatorTripsController\.cs|Requests/(SubstituteVehicle|ReplacementCrew)Request\.cs)|Application/(Features/Trips/Operations/(SubstituteVehicle.*|TripTerminalIntegrationEvents)\.cs|Events/TripVehicleSubstitutedIntegrationEvent\.cs|Abstractions/(Repositories/I(Trip|TripSeat|TripStop|TripStopFare|TripAuditLog|Vehicle)Repository\.cs|ExternalClients/(IBookingImpactClient|VehicleSubstitutionImpactProjection)\.cs)|Services/.*VehicleSubstitution.*\.cs)|Domain/(Constants/TripAuditAction\.cs|Entities/(Trip|TripSeat|TripStop|TripStopFare|TripAuditLog|Vehicle)\.cs)|Infrastructure/(Persistence/Repositories/(Trip|TripSeat|TripStop|TripStopFare|TripAuditLog|Vehicle)Repository\.cs|Http/BookingImpactClient(Options)?\.cs|DependencyInjection/.*\.cs))|apps/trip/tests/VietRide\.Trip\.(IntegrationTests/(Trips/SubstituteVehicleEndpointTests|Events/TripVehicleSubstitutedIntegrationEventTests)|UnitTests/ExternalClients/BookingImpactClientTests)\.cs)$'
$taskLedger = @(Get-Day34TaskLedger '34.3' $allowed)
$csLedger = @($taskLedger | Where-Object { $_ -match '\.cs$' })
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include $csLedger
if ($LASTEXITCODE -ne 0) { throw 'changed-file format failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.4 - Add BookingTransfer persistence and reversible migration

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | `dotnet-worker` |
| review agent | `dotnet-reviewer` |
| skill | `scaffold-aggregate` + `ef-migration` |
| owned files (base write set) | `apps/booking/src/VietRide.Booking.Domain/Entities/BookingTransfer.cs`; `apps/booking/src/VietRide.Booking.Domain/Entities/Passenger.cs`; `apps/booking/src/VietRide.Booking.Domain/Enums/BookingTransferConfirmationStatus.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingTransferRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/BookingTransferConfiguration.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Configurations/PassengerConfiguration.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingTransferRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/BookingDbContext.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Migrations/BookingDbContextModelSnapshot.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Domain/BookingTransferTests.cs`; `apps/booking/tests/VietRide.Booking.UnitTests/Infrastructure/BookingTransferModelTests.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/Migrations/BookingTransferMigrationLifecycleTests.cs` |
| auto-expand scope | Files in the same BookingTransfer aggregate, the repository interface-implementation pair, Booking DbContext/enum mapping/DI, Passenger null-seat mapping, affected tests, and generated migration/designer/snapshot files for `AddVehicleSubstitutionTransfers`; no unused Create/Get/List CQRS handlers. |
| forbidden scope | `.env`, secrets, `API-Response.md`, Trip/Parcel/Payment/Identity/Nest files, new dependencies, columns/enums/indexes beyond the approved DDL, unresolved business/API/schema decisions, cross-service DB FKs, editing merged migrations, destructive operations outside the exact isolated `vietride_day34_booking_migration` database, and all git operations. |
| depends on | 34.0 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below; migration lifecycle is task-specific, not a full regression. |
| full regression owner | `audit-day` |
| invariant flags | CRLF for `.cs`; default schema exactly `vietride_booking`; exact schema-qualified snake_case enum/columns/index; `Passenger.seat_number`, `BookingTransfer.original_seat_number`, and `BookingTransfer.new_seat_number` nullable while existing unique semantics remain PostgreSQL-default; local FKs only to Booking/Passenger/Ticket; Trip/User ids are logical references with no cross-DB FK; reversible non-empty fail-closed `Down()`; no sentinel seat; no soft-delete/activation/update audit columns on immutable transfer history; one row per `(passengerId,originalTripId,newTripId)`; dependency direction Domain -> none, Application -> Domain, Infrastructure -> Domain+Application; MediatR v11; CPM no `Version=`; no new dependency. |
| acceptance | EF models exact enum `PENDING_CONFIRM\|CONFIRMED\|NOT_REQUIRED`, all canonical BookingTransfer columns including nullable original/new seat history and confirmation fields, nullable Passenger seat, local FKs, logical cross-service references, and schema-qualified unique `(passenger_id,original_trip_id,new_trip_id)`. Duplicate non-null seats within a Booking remain rejected while multiple null pending assignments are allowed. A Passenger may transfer again for a later distinct old/new Trip pair, including when the current/original seat remains null. The entity can confirm only a pending row with a seat and preserves persisted first confirmation values on repeat. A rerunnable verification has exactly one `AddVehicleSubstitutionTransfers` migration, applies it to a deterministically cleaned isolated database, seeds a real multi-row null-seat transfer chain, and migrates down to `20260722093941_AddIntegrationInbox`. Before restoring Passenger `seat_number NOT NULL`, `Down()` chooses the latest non-null `new_seat_number`, otherwise latest non-null `original_seat_number`, ordered by `transferred_at DESC, id DESC`; a deterministic tie is won by greatest id, a Passenger with no recoverable value makes `Down()` fail, and no sentinel is written. The lifecycle proves recovered data and schema, removes only Day-34 table/enum/index objects, reapplies, and reports no pending model changes. |
| source citations | Approved Day-34 decision freeze item 5 in this plan; after 34.0: `db-schema/booking/schema.sql` enums, `passengers`, `booking_transfers`; `db-schema/booking/README.md` BookingTransfer/Index Strategy/Cross-service References/Migration Strategy; `BACKEND_SOURCE_OF_TRUTH.md` sections 3.2, 3.5, 4.3-4.4; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 BookingTransfer entity requirements. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
foreach ($name in @('PGHOST','PGPORT','PGUSER','PGPASSWORD')) {
  if ([string]::IsNullOrWhiteSpace((Get-Item "Env:$name" -ErrorAction SilentlyContinue).Value)) { throw "$name missing" }
}
$database = 'vietride_day34_booking_migration'
$env:BOOKING_DESIGN_CONNECTION = "Host=$env:PGHOST;Port=$env:PGPORT;Database=$database;Username=$env:PGUSER;Password=$env:PGPASSWORD"
function Invoke-DotNetTestNonZero([string]$Project,[string]$Fqn,[string]$Tag) {
  $name = "day34-$Tag-$([guid]::NewGuid()).trx"
  $trx = Join-Path 'TestResults' $name
  Remove-Item -LiteralPath $trx -Force -ErrorAction SilentlyContinue
  dotnet test $Project --filter "FullyQualifiedName=$Fqn" --results-directory TestResults --logger "trx;LogFileName=$name"
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $Tag" }
  [xml]$xml = Get-Content -LiteralPath $trx
  $c = $xml.TestRun.ResultSummary.Counters
  if ([int]$c.executed -lt 1 -or [int]$c.passed -lt 1 -or [int]$c.failed -ne 0) { throw "zero/failing tests: $Tag" }
}
try {
  psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -v ON_ERROR_STOP=1 -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();"
  if ($LASTEXITCODE -ne 0) { throw 'isolated database connection cleanup failed' }
  psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $database"
  if ($LASTEXITCODE -ne 0) { throw 'isolated database pre-clean failed' }
  psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $database"
  if ($LASTEXITCODE -ne 0) { throw 'isolated database create failed' }
  $migrationDir = 'apps/booking/src/VietRide.Booking.Infrastructure/Migrations'
  $migrationFiles = @(Get-ChildItem -LiteralPath $migrationDir -Filter '*_AddVehicleSubstitutionTransfers.cs' -File | Where-Object { $_.Name -notlike '*.Designer.cs' })
  if ($migrationFiles.Count -eq 0) {
    dotnet ef migrations add AddVehicleSubstitutionTransfers -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api -o Migrations
    if ($LASTEXITCODE -ne 0) { throw 'migration generate failed' }
    $migrationFiles = @(Get-ChildItem -LiteralPath $migrationDir -Filter '*_AddVehicleSubstitutionTransfers.cs' -File | Where-Object { $_.Name -notlike '*.Designer.cs' })
  }
  if ($migrationFiles.Count -ne 1) { throw "expected exactly one AddVehicleSubstitutionTransfers migration, found $($migrationFiles.Count)" }
  $migrationId = [IO.Path]::GetFileNameWithoutExtension($migrationFiles[0].Name)
  $designerPath = Join-Path $migrationDir "$migrationId.Designer.cs"
  if (-not (Test-Path -LiteralPath $designerPath -PathType Leaf)) { throw "expected designer for $migrationId" }
  $upSql = 'TestResults/day34-booking-transfers-up.sql'
  $downSql = 'TestResults/day34-booking-transfers-down.sql'
  New-Item -ItemType Directory -Force -Path 'TestResults' | Out-Null
  dotnet ef migrations script 20260722093941_AddIntegrationInbox $migrationId -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api -o $upSql
  if ($LASTEXITCODE -ne 0) { throw 'Up SQL generation failed' }
  dotnet ef migrations script $migrationId 20260722093941_AddIntegrationInbox -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api -o $downSql
  if ($LASTEXITCODE -ne 0) { throw 'Down SQL generation failed' }
  $up = Get-Content -Raw -LiteralPath $upSql
  $down = Get-Content -Raw -LiteralPath $downSql
  $schema = '"?vietride_booking"?'
  if ($up -notmatch ('(?i)CREATE TABLE\s+'+$schema+'\."?booking_transfers"?')) { throw 'Up SQL missing schema-qualified booking_transfers table' }
  if ($up -notmatch ('(?i)CREATE TYPE\s+'+$schema+'\."?booking_transfer_confirmation_status"?')) { throw 'Up SQL missing schema-qualified confirmation enum' }
  foreach ($column in @('id','booking_id','passenger_id','ticket_id','original_trip_id','new_trip_id','original_seat_number','new_seat_number','confirmation_status','confirmed_at','confirmed_by_user_id','transferred_at','transferred_by_user_id','note','created_at')) {
    if ($up -notmatch ('(?i)\b'+[regex]::Escape($column)+'\b')) { throw "Up SQL missing canonical column $column" }
  }
  foreach ($index in @('idx_booking_transfers_booking_id','idx_booking_transfers_passenger_id','idx_booking_transfers_ticket_id','idx_booking_transfers_original_trip_id','idx_booking_transfers_new_trip_id')) {
    if ($up -notmatch ('(?i)CREATE INDEX\s+"?'+[regex]::Escape($index)+'"?\s+ON\s+'+$schema+'\."?booking_transfers"?')) { throw "Up SQL missing schema-qualified canonical index $index" }
  }
  if ($up -notmatch ('(?i)CREATE UNIQUE INDEX\s+"?uq_booking_transfers_passenger_trip_pair"?\s+ON\s+'+$schema+'\."?booking_transfers"?\s*\(\s*"?passenger_id"?\s*,\s*"?original_trip_id"?\s*,\s*"?new_trip_id"?\s*\)')) { throw 'Up SQL missing exact schema-qualified unique transfer index' }
  foreach ($enumValue in @('PENDING_CONFIRM','CONFIRMED','NOT_REQUIRED')) {
    if ($up -notmatch ('(?i)\b'+$enumValue+'\b')) { throw "confirmation enum missing $enumValue" }
  }
  foreach ($forbiddenColumn in @('updated_at','row_version','deleted_at','is_active')) {
    if ($up -match ('(?i)\b'+[regex]::Escape($forbiddenColumn)+'\b')) { throw "unexpected audit/activation/soft-delete column $forbiddenColumn" }
  }
  if ($up -match '(?is)FOREIGN KEY\s*\([^)]*(original_trip_id|new_trip_id|transferred_by_user_id|confirmed_by_user_id)[^)]*\)') { throw 'cross-service foreign key emitted' }
  foreach ($localFk in @('booking_id','passenger_id','ticket_id')) {
    if ($up -notmatch ('(?is)FOREIGN KEY\s*\([^)]*'+[regex]::Escape($localFk)+'[^)]*\)')) { throw "local FK missing for $localFk" }
  }
  if ($up -notmatch ('(?is)ALTER TABLE\s+'+$schema+'\."?passengers"?.*ALTER COLUMN\s+"?seat_number"?.*DROP NOT NULL')) { throw 'Up SQL does not make schema-qualified passenger seat nullable' }
  foreach ($seatColumn in @('original_seat_number','new_seat_number')) {
    if ($up -notmatch ('(?is)\b'+$seatColumn+'\b')) { throw "Up SQL missing nullable transfer history column $seatColumn" }
  }
  if ($down -notmatch ('(?i)DROP TABLE\s+'+$schema+'\."?booking_transfers"?')) { throw 'Down SQL does not remove schema-qualified booking_transfers' }
  if ($down -notmatch ('(?i)DROP TYPE\s+'+$schema+'\."?booking_transfer_confirmation_status"?')) { throw 'Down SQL does not remove schema-qualified confirmation enum' }
  if ($down -notmatch ('(?is)UPDATE\s+'+$schema+'\."?passengers"?.*ORDER BY.*"?transferred_at"?\s+DESC\s*,\s*.*"?id"?\s+DESC')) { throw 'Down SQL missing deterministic transfer-history seat backfill' }
  foreach ($historyColumn in @('new_seat_number','original_seat_number')) {
    if ($down -notmatch ('(?i)\b'+$historyColumn+'\b')) { throw "Down SQL missing recovery source $historyColumn" }
  }
  if ($down -notmatch '(?is)RAISE\s+EXCEPTION.*seat_number.*NULL') { throw 'Down SQL must fail when a null passenger seat remains' }
  if ($down -notmatch ('(?is)ALTER TABLE\s+'+$schema+'\."?passengers"?.*ALTER COLUMN\s+"?seat_number"?.*SET NOT NULL')) { throw 'Down SQL does not restore schema-qualified passenger seat nullability' }
  $day34Drops = [regex]::Matches($down, '(?im)^\s*DROP\s+(TABLE|TYPE|COLUMN|CONSTRAINT|INDEX)\s+(?:IF EXISTS\s+)?(?<name>[a-zA-Z0-9_."-]+)')
  $normalizedDrops = @($day34Drops | ForEach-Object {
    $qualified = $_.Groups['name'].Value
    (($qualified -split '\.')[-1]).Trim('"')
  })
  $unexpectedDrops = @($normalizedDrops | Where-Object { $_ -notmatch '^booking_transfers$|^booking_transfer_confirmation_status$|^(idx|uq)_booking_transfers_' })
  if ($unexpectedDrops.Count -gt 0) { throw "Down SQL removes non-Day-34 objects: $($unexpectedDrops -join ', ')" }
  dotnet ef database update -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api
  if ($LASTEXITCODE -ne 0) { throw 'migration apply failed' }
  $integration = 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
  Invoke-DotNetTestNonZero $integration 'VietRide.Booking.IntegrationTests.Migrations.BookingTransferMigrationLifecycleTests.DownBackfillsRealNullSeatChainDeterministicallyRestoresSchemaAndDataAndRemovesOnlyDay34Objects' 'transfer-down-data'
  Invoke-DotNetTestNonZero $integration 'VietRide.Booking.IntegrationTests.Migrations.BookingTransferMigrationLifecycleTests.DownFailsWhenNoRecoverableSeatExistsAndNeverWritesSentinel' 'transfer-down-fail-closed'
  dotnet ef database update 20260722093941_AddIntegrationInbox -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api
  if ($LASTEXITCODE -ne 0) { throw 'migration Down failed' }
  dotnet ef database update $migrationId -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api
  if ($LASTEXITCODE -ne 0) { throw 'migration reapply failed' }
  dotnet ef migrations has-pending-model-changes -p apps/booking/src/VietRide.Booking.Infrastructure -s apps/booking/src/VietRide.Booking.Api
  if ($LASTEXITCODE -ne 0) { throw 'pending model changes found' }
  $unit = 'apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj'
  Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Domain.BookingTransferTests.CreatesExactConfirmationStateAndConfirmsIdempotentlyWithoutChangingHistory' 'transfer-domain'
  Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Infrastructure.BookingTransferModelTests.MatchesCanonicalEnumColumnsNullableSeatUniqueTripleAndLogicalForeignKeys' 'transfer-model'
  Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Architecture.LayeringTests.Domain_Should_Not_Depend_On_Other_Project_Layers' 'transfer-arch-domain'
  Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Architecture.LayeringTests.Application_Should_Depend_Only_On_Domain_Project_Layer' 'transfer-arch-app'
  Invoke-DotNetTestNonZero $unit 'VietRide.Booking.UnitTests.Architecture.LayeringTests.Infrastructure_Should_Depend_Only_On_Domain_And_Application_Project_Layers' 'transfer-arch-infra'
  $allowed = '^(apps/booking/src/VietRide\.Booking\.(Domain/(Entities/(BookingTransfer|Passenger)\.cs|Enums/BookingTransferConfirmationStatus\.cs)|Application/Abstractions/Repositories/IBookingTransferRepository\.cs|Infrastructure/(BookingDbContext\.cs|Persistence/(Configurations/(BookingTransfer|Passenger)Configuration\.cs|Repositories/BookingTransferRepository\.cs)|DependencyInjection/.*\.cs|Migrations/([0-9]+_AddVehicleSubstitutionTransfers(\.Designer)?|BookingDbContextModelSnapshot)\.cs))|apps/booking/tests/VietRide\.Booking\.(UnitTests|IntegrationTests)/(.*BookingTransfer.*|Architecture/LayeringTests)\.cs)$'
  $taskLedger = @(Get-Day34TaskLedger '34.4' $allowed)
  $csLedger = @($taskLedger | Where-Object { $_ -match '\.cs$' })
  dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $csLedger
  if ($LASTEXITCODE -ne 0) { throw 'changed-file format failed' }
  git diff --check -- $taskLedger
  if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
}
finally {
  Remove-Item -LiteralPath 'TestResults/day34-booking-transfers-up.sql','TestResults/day34-booking-transfers-down.sql' -Force -ErrorAction SilentlyContinue
  psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -v ON_ERROR_STOP=1 -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();"
  if ($LASTEXITCODE -ne 0) { throw 'isolated database final connection cleanup failed' }
  psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $database"
  if ($LASTEXITCODE -ne 0) { throw 'isolated database cleanup failed' }
}
```

### Task 34.5 - Consume VehicleSubstituted and create per-Passenger transfers

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | `dotnet-worker` |
| review agent | `dotnet-reviewer` |
| skill | `add-integration-event` |
| owned files (base write set) | `apps/booking/src/VietRide.Booking.Infrastructure/Messaging/TripVehicleSubstitutedIntegrationEvent.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Messaging/TripVehicleSubstitutedIntegrationEventHandler.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/VehicleSubstitution/ApplyVehicleSubstitutionCommand.cs`; `apps/booking/src/VietRide.Booking.Application/Features/Bookings/VehicleSubstitution/ApplyVehicleSubstitutionCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Events/BookingTransferredIntegrationEvent.cs`; `apps/booking/src/VietRide.Booking.Domain/Entities/Booking.cs`; `apps/booking/src/VietRide.Booking.Domain/Entities/Passenger.cs`; `apps/booking/src/VietRide.Booking.Domain/Entities/BookingTransfer.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingTransferRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingTransferRepository.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/Messaging/TripVehicleSubstitutedConsumerTests.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/Events/BookingTransferredIntegrationEventTests.cs` |
| auto-expand scope | Files in the same VehicleSubstitution feature; Booking/Passenger/BookingTransfer repository interface-implementation pairs; existing generic integration-inbox consumer registration/config; directly affected Booking tests. The 34.1 shared contracts are read-only parity sources. |
| forbidden scope | `.env`, secrets, `API-Response.md`, shared contract shape changes, Trip implementation, Parcel Day-35 consumer, Payment/Identity/Nest implementation, new dependencies, schema/migrations, synchronous Trip callback, edits to existing `trip.trip.vehicle_swapped` pre-departure flow, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.1, 34.3, 34.4 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | CRLF for `.cs`; consume only exact `trip.trip.vehicle_substituted`; generic integration inbox owns the transaction/dedupe marker; handler starts no nested transaction and writes no duplicate marker; Booking status remains `CONFIRMED\|PARTIAL_NO_SHOW`; Passenger boarding/ticket history is unchanged; mapped nullable original/new seats are authoritative and never replaced with a sentinel; one unique transfer per mapped Passenger occurrence; one Booking fact per Booking; producer-allocated `EventId == Outbox id == MessageId`; no cross-DB FK/callback; Money unchanged; MediatR v11; CPM no `Version=`; no new dependency. |
| acceptance | The registered consumer validates the strict shared payload and groups mappings by Booking. It rechecks old Trip, operator, and eligible Booking status; changes each eligible Booking to `newTripId` without changing its status; changes only mapped Passenger `seatNumber` (including null); persists both nullable `originalSeatNumber` and nullable `newSeatNumber` exactly, so a chained substitution with an unresolved current seat is processed rather than blocked; creates `PENDING_CONFIRM` for original `BOARDED` and `NOT_REQUIRED` for original `PENDING`; creates no row for `NO_SHOW`/ineligible/terminal data; never changes Passenger boarding timestamps/status or Ticket status/seat/usage. In the same Booking-local inbox transaction it emits exactly one `booking.booking.transferred` Outbox fact per changed Booking, addressed only to `passengerUserId`, even when notifications are suppressed, with both seat-history values preserving null. Duplicate source delivery is a complete no-op. Injected failure rolls back Booking, Passenger, BookingTransfer, inbox, and every Booking Outbox row. The Booking fact survives publisher restart and reaches `vietride.events` with exact routing key/payload and `MessageId==EventId`. |
| source citations | Approved Day-34 decision freeze items 5, 6, and 8 in this plan; after 34.0: `BACKEND_SOURCE_OF_TRUTH.md` sections 7.3-7.4 and 8.1; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 Booking transfer/NO_SHOW/entity requirements; `db-schema/booking/schema.sql` `bookings`, `passengers`, `tickets`, `booking_transfers`; `db-schema/booking/README.md` BookingTransfer and cross-service references; current generic inbox registration pattern in `InfrastructureServiceCollectionExtensions`. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-DotNetTestNonZero([string]$Project,[string]$Fqn,[string]$Tag) {
  $name = "day34-$Tag-$([guid]::NewGuid()).trx"
  $trx = Join-Path 'TestResults' $name
  Remove-Item -LiteralPath $trx -Force -ErrorAction SilentlyContinue
  dotnet test $Project --filter "FullyQualifiedName=$Fqn" --results-directory TestResults --logger "trx;LogFileName=$name"
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $Tag" }
  [xml]$xml = Get-Content -LiteralPath $trx
  $c = $xml.TestRun.ResultSummary.Counters
  if ([int]$c.executed -lt 1 -or [int]$c.passed -lt 1 -or [int]$c.failed -ne 0) { throw "zero/failing tests: $Tag" }
}
$project = 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Messaging.TripVehicleSubstitutedConsumerTests.AppliesEligibleBookingAndPassengerRulesWithoutChangingBoardingOrTickets' 'consumer-rules'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Messaging.TripVehicleSubstitutedConsumerTests.ExcludesNoShowAndIneligibleBookingsAndPreservesNullableSeatSemantics' 'consumer-exclusions'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Messaging.TripVehicleSubstitutedConsumerTests.ChainedSubstitutionPersistsNullOriginalAndNewSeatsWithoutBlockingOrSentinel' 'consumer-null-seat-chain'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Messaging.TripVehicleSubstitutedConsumerTests.DuplicateAndInjectedFailureAreAtomicAcrossInboxStateTransfersAndOutbox' 'consumer-atomic-dedupe'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Messaging.TripVehicleSubstitutedConsumerTests.RegistrationUsesCanonicalBindingAndGenericInboxTransaction' 'consumer-registration'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Events.BookingTransferredIntegrationEventTests.EmitsOneExactFactPerBookingForOwnerEvenWhenNotificationIsSuppressed' 'booking-event-cardinality'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Events.BookingTransferredIntegrationEventTests.OutboxIsAtomicAndPublisherRestartPreservesRoutingKeyMessageIdAndPayload' 'booking-event-publisher'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.Events.BookingTransferredIntegrationEventTests.SerializedPayloadMatchesSharedContractFieldForField' 'booking-event-parity'
$allowed = '^(apps/booking/src/VietRide\.Booking\.(Infrastructure/(Messaging/TripVehicleSubstituted.*\.cs|DependencyInjection/.*\.cs|Persistence/Repositories/(Booking|BookingTransfer)Repository\.cs)|Application/(Features/Bookings/VehicleSubstitution/.*\.cs|Events/BookingTransferredIntegrationEvent\.cs|Abstractions/Repositories/I(Booking|BookingTransfer)Repository\.cs)|Domain/Entities/(Booking|Passenger|BookingTransfer)\.cs)|apps/booking/tests/VietRide\.Booking\.(IntegrationTests|UnitTests)/.*(TripVehicleSubstituted|BookingTransferred|VehicleSubstitution).*\.cs)$'
$taskLedger = @(Get-Day34TaskLedger '34.5' $allowed)
$csLedger = @($taskLedger | Where-Object { $_ -match '\.cs$' })
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $csLedger
if ($LASTEXITCODE -ne 0) { throw 'changed-file format failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.6 - Confirm replacement passengers individually through BookingTransfer

| Field | Value |
|---|---|
| stack/owner | dotnet |
| implement agent | `dotnet-worker` |
| review agent | `dotnet-reviewer` |
| skill | `add-endpoint` |
| owned files (base write set) | `apps/booking/src/VietRide.Booking.Api/Controllers/BookingTransfersController.cs`; `apps/booking/src/VietRide.Booking.Application/Features/BookingTransfers/ConfirmPassengerTransfer/ConfirmPassengerTransferCommand.cs`; `apps/booking/src/VietRide.Booking.Application/Features/BookingTransfers/ConfirmPassengerTransfer/ConfirmPassengerTransferCommandValidator.cs`; `apps/booking/src/VietRide.Booking.Application/Features/BookingTransfers/ConfirmPassengerTransfer/ConfirmPassengerTransferCommandHandler.cs`; `apps/booking/src/VietRide.Booking.Application/Features/BookingTransfers/ConfirmPassengerTransfer/ConfirmPassengerTransferResponse.cs`; `apps/booking/src/VietRide.Booking.Domain/Entities/BookingTransfer.cs`; `apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingTransferRepository.cs`; `apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingTransferRepository.cs`; `apps/booking/tests/VietRide.Booking.IntegrationTests/BookingTransfers/VehicleSubstitutionPassengerConfirmationEndpointTests.cs` |
| auto-expand scope | Files in the same confirm-transfer feature; BookingTransfer repository interface-implementation pair; existing `ITripServiceClient`/Trip snapshot implementation and DI only if the assigned-crew fields are not already available; directly affected controller/domain/integration tests. |
| forbidden scope | `.env`, secrets, `API-Response.md`, existing Boarding/TickPassengerBoarded files, Passenger or Ticket mutation code, Gateway routes (existing `/v1/bookings` prefix covers this path), Trip/Parcel/Payment/Identity/Notification implementation, new dependencies, schema/migrations, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.5, 34.8 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | CRLF for `.cs`; thin controller -> MediatR; Swashbuckle documents exact ADR 0004 success/error statuses; bodyless POST; UUID-v4 idempotency; `DRIVER\|ASSISTANT` and caller must match replacement Trip snapshot assignment; active transfer requires current `Booking.tripId==newTripId`; one transfer row at a time; persisted first confirmation values are replayed; no Passenger/Ticket/boarding mutation; no cross-DB FK; MediatR v11; CPM no `Version=`; no new dependency. |
| acceptance | `POST /v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm` accepts no body and returns the exact approved response. An assigned Driver or Assistant confirms only the matching active `PENDING_CONFIRM` transfer with non-null `newSeatNumber`; only that row changes to `CONFIRMED` with one captured UTC instant and caller id. Same-key replay and any already-confirmed retry return `200` with the persisted first values and do not confirm siblings. A five-`PENDING_CONFIRM` fixture supports exactly three confirmed rows and two pending rows after three distinct Passenger calls. Missing/inactive transfer, pending seat, invalid UUID, cross-Trip/tenant, wrong role, and unassigned crew produce the approved errors with no BookingTransfer, Passenger, Ticket, or Outbox side effect. |
| source citations | Approved Day-34 decision freeze items 5 and 7 in this plan; after 34.0: `VietRide_API_Contract_v1.md` dedicated transfer-confirm section; `BACKEND_SOURCE_OF_TRUTH.md` sections 5.6, 5.9, 6.6 and 7.2 Trip snapshot; `BE_TIMELINE_VU.md` Day 34; `db-schema/booking/schema.sql` `booking_transfers`; existing assigned-crew pattern in the boarding endpoint is authorization precedent only, not state ownership. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-DotNetTestNonZero([string]$Project,[string]$Fqn,[string]$Tag) {
  $name = "day34-$Tag-$([guid]::NewGuid()).trx"
  $trx = Join-Path 'TestResults' $name
  Remove-Item -LiteralPath $trx -Force -ErrorAction SilentlyContinue
  dotnet test $Project --filter "FullyQualifiedName=$Fqn" --results-directory TestResults --logger "trx;LogFileName=$name"
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $Tag" }
  [xml]$xml = Get-Content -LiteralPath $trx
  $c = $xml.TestRun.ResultSummary.Counters
  if ([int]$c.executed -lt 1 -or [int]$c.passed -lt 1 -or [int]$c.failed -ne 0) { throw "zero/failing tests: $Tag" }
}
$project = 'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.AssignedCrewConfirmsExactlyThreeOfFiveWithoutChangingTwoSiblings' 'confirm-partial'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.DriverAndAssistantReceiveExactResponseWithoutPassengerOrTicketMutation' 'confirm-crew'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.CrossTripInactiveTransferWrongRoleAndUnassignedCrewDoNotMutate' 'confirm-guards'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.PendingSeatIsConflictAndAlreadyConfirmedRetriesReturnPersistedValues' 'confirm-state'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.BodylessStrictRouteAndUuidV4IdempotencyAreEnforced' 'confirm-http'
Invoke-DotNetTestNonZero $project 'VietRide.Booking.IntegrationTests.BookingTransfers.VehicleSubstitutionPassengerConfirmationEndpointTests.ThinControllerDispatchesMediatRAndDeclaresApiResponseAndSwashbuckleMetadata' 'confirm-controller-metadata'
function Invoke-NxJestNonZero([string]$Spec,[string]$Pattern,[string]$Tag) {
  New-Item -ItemType Directory -Force -Path 'TestResults' | Out-Null
  $resultsDir = (Resolve-Path -LiteralPath 'TestResults').Path
  $jsonPath = Join-Path $resultsDir "day34-$Tag-$([guid]::NewGuid()).json"
  try {
    npx nx test gateway --runInBand --passWithNoTests=false --testPathPatterns=$Spec --testNamePattern=$Pattern --json --outputFile=$jsonPath
    if ($LASTEXITCODE -ne 0) { throw "Gateway spec failed: $Tag" }
    $result = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ([int]$result.numTotalTests -lt 1 -or [int]$result.numPassedTests -lt 1 -or [int]$result.numFailedTests -ne 0) { throw "zero/failing Gateway tests: $Tag" }
  }
  finally { Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue }
}
Invoke-NxJestNonZero 'apps/gateway/src/config/routes.spec.ts' 'routes passenger transfer confirmation to Booking with user auth' 'confirm-gateway-route'
Invoke-NxJestNonZero 'apps/gateway/src/proxy/proxy.access-gates.spec.ts' 'enforces DRIVER or ASSISTANT auth and preserves Idempotency-Key for passenger transfer confirmation' 'confirm-gateway-access'
$allowed = '^(apps/booking/src/VietRide\.Booking\.(Api/Controllers/BookingTransfersController\.cs|Application/(Features/BookingTransfers/ConfirmPassengerTransfer/.*\.cs|Abstractions/(Repositories/IBookingTransferRepository|ServiceClients/ITripServiceClient)\.cs)|Infrastructure/(Persistence/Repositories/BookingTransferRepository\.cs|Http/(Dev)?TripServiceClient\.cs|DependencyInjection/.*\.cs)|Domain/Entities/BookingTransfer\.cs)|apps/booking/tests/VietRide\.Booking\.(IntegrationTests|UnitTests)/.*VehicleSubstitutionPassengerConfirmation.*\.cs)$'
$taskLedger = @(Get-Day34TaskLedger '34.6' $allowed)
$csLedger = @($taskLedger | Where-Object { $_ -match '\.cs$' })
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include $csLedger
if ($LASTEXITCODE -ne 0) { throw 'changed-file format failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.7 - Notify passengers from the BookingTransferred fact

| Field | Value |
|---|---|
| stack/owner | nest |
| implement agent | `nest-worker` |
| review agent | `nest-reviewer` |
| skill | (none) |
| owned files (base write set) | `apps/notification/src/notifications/booking-trip-change-events.consumer.ts`; `apps/notification/src/notifications/booking-trip-change-notification.mapper.ts`; `apps/notification/src/notifications/notifications.service.ts`; `apps/notification/src/notifications/booking-trip-change-events.consumer.spec.ts`; `apps/notification/src/notifications/booking-trip-change-notification.mapper.spec.ts`; `apps/notification/src/notifications/notifications.service.spec.ts`; `apps/notification/src/notifications/notification-queues.spec.ts` |
| auto-expand scope | Existing Notification consumer binding/module registration; shared contract import already delivered by 34.1; `MessageIdempotencyService` and its exact focused spec only if directly required by the recovery acceptance or a reviewer finding; `FcmPushQueue` implementation only if the existing `jobId=notificationId` behavior itself requires a reviewer-requested correction; and directly affected Notification consumer/service/queue specs in `apps/notification/src/notifications/`. |
| forbidden scope | `.env`, secrets, `API-Response.md`, .NET service files, shared contract shape changes, Parcel Day-35 behavior, Tracking/RAG/Gateway files, new dependencies, Notification DB schema/migrations or new notification type, alternate recipients/PII, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.1, 34.5 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | LF for `.ts`; strict field-for-field shared schema; consume only `booking.booking.transferred`; do not change bindings or policies for any existing routing key; require MessageId before durable idempotency acquisition and require acquired idempotency before side effects; malformed strict/Zod-invalid payloads are finalized as processed without retry; post-acquisition downstream failures release the lock and rethrow into the existing broker retry/DLQ path; a persisted `VEHICLE_SUBSTITUTED` notification remains enqueue-recoverable on deduped service replay; FCM queue `jobId` remains exactly `notificationId`; one recipient exactly `recipientUserId`; `notifyPassengers=false` is a successful deduped no-op; no producer behavior, new dependency, cross-service DB read, PII, or alternate recipient. |
| acceptance | The existing Booking trip-change consumer binds the canonical routing key and validates the exact shared schema without changing any existing routing-key binding or handling policy. For the normal `notifyPassengers=true` path, one source fact creates exactly one persisted/pushed `VEHICLE_SUBSTITUTED` notification for the single `recipientUserId` Booking owner, summarizing the replacement plate/departure and every transfer's assigned or pending seat, including null original/new seat values, without Passenger PII or alternate recipients. A normal duplicate event whose MessageId is already processed is a no-op with no repository write and no enqueue. Push recovery is explicit: if the notification row is persisted but FCM enqueue throws, `NotificationsService.createNotification` rejects, the consumer releases the acquired inbox/idempotency lock, does not mark the inbox processed, and rethrows into the existing broker retry/DLQ path. On broker redelivery, the inbox is acquired again; the same notification dedupe key resolves the same persisted row, `NotificationsService` retries FCM enqueue for that existing `VEHICLE_SUBSTITUTED` row, and `FcmPushQueue` uses deterministic `jobId=notificationId`. After enqueue succeeds, the consumer marks the inbox processed. The complete failure/redelivery sequence leaves exactly one notification row and one effective queue job, with no second row or effective job. For `notifyPassengers=false`, the fact is consumed and marked processed but creates no notification or enqueue attempt, including on replay. A malformed strict/Zod-invalid payload is finalized or marked processed with zero notification side effect and no retry loop. A missing MessageId fails before idempotency acquisition and before any notification side effect. |
| source citations | Approved Day-34 decision freeze item 8 in this plan; after 34.0: `BACKEND_SOURCE_OF_TRUTH.md` event registry row `booking.booking.transferred`, sections 7.4 and Notification conventions; `SU26SE101_VIETRIDE_technical_context_v7.md` section 6.12 notification text and Notification type enum; current recovery/idempotency patterns `booking-trip-change-events.consumer.ts`, `notifications.service.ts`, `notifications.repository.ts`, `fcm-push.queue.ts` (`jobId: data.notificationId`), `notification-queues.spec.ts`, and `MessageIdempotencyService`. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-NotificationJestNonZero([string]$Spec,[string]$Pattern,[string]$Tag) {
  New-Item -ItemType Directory -Force -Path 'TestResults' | Out-Null
  $resultsDir = (Resolve-Path -LiteralPath 'TestResults').Path
  $jsonPath = Join-Path $resultsDir "day34-$Tag-$([guid]::NewGuid()).json"
  try {
    npx nx test notification --runInBand --passWithNoTests=false --testPathPatterns=$Spec --testNamePattern=$Pattern --json --outputFile=$jsonPath
    if ($LASTEXITCODE -ne 0) { throw "Notification focused spec failed: $Tag" }
    $result = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ([int]$result.numTotalTests -lt 1 -or [int]$result.numPassedTests -lt 1 -or [int]$result.numFailedTests -ne 0) { throw "zero/failing Notification tests: $Tag" }
  }
  finally { Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue }
}
$consumerSpec = 'apps/notification/src/notifications/booking-trip-change-events.consumer.spec.ts'
$mapperSpec = 'apps/notification/src/notifications/booking-trip-change-notification.mapper.spec.ts'
$serviceSpec = 'apps/notification/src/notifications/notifications.service.spec.ts'
$queueSpec = 'apps/notification/src/notifications/notification-queues.spec.ts'
Invoke-NotificationJestNonZero $consumerSpec 'binds booking\.booking\.transferred and validates its strict shared schema$' 'notification-binding-schema'
Invoke-NotificationJestNonZero $consumerSpec 'notifyPassengers true creates one persisted and pushed Booking-owner notification and duplicate MessageId is a no-op$' 'notification-true-dedupe'
Invoke-NotificationJestNonZero $consumerSpec 'notifyPassengers false marks the event processed without notification or push$' 'notification-false-noop'
Invoke-NotificationJestNonZero $consumerSpec 'malformed strict payload is marked processed with zero notification side effect and no retry$' 'notification-malformed-finalized'
Invoke-NotificationJestNonZero $consumerSpec 'missing MessageId fails before idempotency acquisition and notification side effects$' 'notification-missing-message-id'
Invoke-NotificationJestNonZero $consumerSpec 'downstream failure releases the acquired idempotency lock and rethrows for broker retry and DLQ$' 'notification-downstream-retry'
Invoke-NotificationJestNonZero $consumerSpec 'persisted row then enqueue failure then redelivery reuses the same row and marks the inbox processed after enqueue succeeds without a second row or effective job$' 'notification-full-push-recovery'
Invoke-NotificationJestNonZero $serviceSpec 'persisted VEHICLE_SUBSTITUTED row survives enqueue failure and redelivery re-enqueues the same deduped notification without creating a second row$' 'notification-service-reenqueue'
Invoke-NotificationJestNonZero $queueSpec 'uses notificationId as deterministic FCM jobId so replay produces one effective queue job$' 'notification-queue-job-id'
Invoke-NotificationJestNonZero $mapperSpec 'maps nullable original and new seats without Passenger PII or alternate recipients$' 'notification-nullable-seat-pii'
$allowed = '^apps/notification/src/notifications/(booking-trip-change-events\.consumer(\.spec)?\.ts|booking-trip-change-notification\.mapper(\.spec)?\.ts|notifications\.service(\.spec)?\.ts|notification-queues\.spec\.ts|notifications\.module\.ts|message-idempotency\.service(\.spec)?\.ts|fcm-push\.queue\.ts)$'
$taskLedger = @(Get-Day34TaskLedger '34.7' $allowed)
$tsLedger = @($taskLedger | Where-Object { $_ -match '\.(ts|tsx)$' })
npx eslint $tsLedger
if ($LASTEXITCODE -ne 0) { throw 'changed-file lint failed' }
npx nx build notification
if ($LASTEXITCODE -ne 0) { throw 'notification affected-project build failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

### Task 34.8 - Cover both public Day-34 endpoints at the Gateway seam

| Field | Value |
|---|---|
| stack/owner | nest |
| implement agent | `nest-worker` |
| review agent | `nest-reviewer` |
| skill | (none; supplies the Gateway checks required by `add-endpoint` in Tasks 34.3 and 34.6) |
| owned files (base write set) | `apps/gateway/src/config/routes.ts`; `apps/gateway/src/config/routes.spec.ts`; `apps/gateway/src/proxy/proxy.access-gates.spec.ts` |
| auto-expand scope | Directly affected Gateway route/proxy specs in the same config/proxy concern only. |
| forbidden scope | `.env`, secrets, `API-Response.md`, .NET services, downstream endpoint contracts/implementation, unrelated Gateway routes/auth policies, new dependencies, Swagger generation outside the two approved endpoints, unresolved business/API/schema decisions, destructive operations, and all git operations. |
| depends on | 34.0 |
| parallel-safe | no |
| verification tier | `FOCUSED` |
| verification commands | See exact command block below. |
| full regression owner | `audit-day` |
| invariant flags | LF for `.ts`; longest-prefix routing remains intact; health/public routes unchanged; substitution routes to Trip and requires `OPERATOR_ADMIN`; transfer confirmation routes to Booking and requires `DRIVER\|ASSISTANT`; user `Authorization` is verified/stripped according to the existing proxy contract; `X-Internal-Auth` is injected; `Idempotency-Key` is preserved verbatim; ADR 0004 auth errors; no new dependency. |
| acceptance | A dedicated exact `pathPattern` entry before the existing generic `/v1/operator/trips` entry makes `POST /v1/operator/trips/{tripId}/substitute-vehicle` select the Trip upstream with `OPERATOR_ADMIN` only, while unrelated operator-trip paths retain the existing `OPERATOR_ADMIN\|OPERATOR_STAFF` policy. The existing `/v1/bookings/trips` prefix remains sufficient for `POST /v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm` and selects Booking with `DRIVER\|ASSISTANT`. Endpoint-specific access-gate tests prove each approved role set is allowed, `OPERATOR_STAFF` is denied only for substitution, representative other disallowed/missing-auth requests return the existing ADR 0004 `401/403` without proxying, and a valid UUID-v4 `Idempotency-Key` reaches the downstream proxy unchanged together with the generated Internal JWT. Unrelated routing and public access remain unchanged. |
| source citations | After 34.0: `VietRide_API_Contract_v1.md` Day-34 substitute-vehicle and transfer-confirm sections; `BACKEND_SOURCE_OF_TRUTH.md` sections 5.6 and 6.6; current `apps/gateway/src/config/routes.ts`, `routes.spec.ts`, and `proxy.access-gates.spec.ts`; `add-endpoint` skill Gateway verification requirements. |

```powershell
$ErrorActionPreference = 'Stop'
function Get-Day34TaskLedger([string]$TaskId,[string]$AllowedPattern) {
  $manifestPath = $env:VIETRIDE_TASK_BASELINE_FILE
  if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'orchestrator-provided VIETRIDE_TASK_BASELINE_FILE is missing' }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.taskId -ne $TaskId) { throw "baseline task mismatch: expected $TaskId, got $($manifest.taskId)" }
  if ([IO.Path]::GetFullPath([string]$manifest.workspace) -ne [IO.Path]::GetFullPath((Get-Location).Path)) { throw 'baseline workspace mismatch' }
  $before = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($manifest.entries)) { $before[[string]$entry.path] = $entry }
  $dirtyNow = @(& git diff --name-only; & git diff --cached --name-only; & git ls-files --others --exclude-standard) | Where-Object { $_ } | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique
  $dirtySet = [Collections.Generic.HashSet[string]]::new([string[]]$dirtyNow,[StringComparer]::OrdinalIgnoreCase)
  $candidates = @($dirtyNow + @($before.Keys)) | Sort-Object -Unique
  $changed = @($candidates | Where-Object {
    $path = $_
    $existsNow = Test-Path -LiteralPath $path -PathType Leaf
    if (-not $before.ContainsKey($path)) { return $dirtySet.Contains($path) }
    $entry = $before[$path]
    if ($existsNow -ne [bool]$entry.exists) { return $true }
    if (-not $existsNow) { return $false }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ne [string]$entry.sha256
  })
  $outside = @($changed | Where-Object { $_ -notmatch $AllowedPattern })
  if ($outside.Count -gt 0) { throw "Task $TaskId changed paths outside its envelope: $($outside -join ', ')" }
  $ledger = @($changed | Where-Object { $_ -match $AllowedPattern })
  if ($ledger.Count -lt 1) { throw "Task $TaskId actual-scope ledger is empty" }
  $ledger | ForEach-Object { Write-Host "Task $TaskId actual-scope: $_" }
  foreach ($path in @($ledger | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $path))
    if ($path -match '\.(ts|tsx|js|json|md|sql|yml|yaml|sh)$') {
      if ($bytes -contains 13) { throw "$path is not LF-only" }
    }
    elseif ($path -match '\.(cs|csproj|sln|props|targets)$') {
      for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10 -and ($i -eq 0 -or $bytes[$i-1] -ne 13)) { throw "$path contains a non-CRLF newline" }
        if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i+1] -ne 10)) { throw "$path contains a lone CR" }
      }
    }
  }
  return $ledger
}
function Invoke-NxJestNonZero([string]$Spec,[string]$Pattern,[string]$Tag) {
  New-Item -ItemType Directory -Force -Path 'TestResults' | Out-Null
  $resultsDir = (Resolve-Path -LiteralPath 'TestResults').Path
  $jsonPath = Join-Path $resultsDir "day34-$Tag-$([guid]::NewGuid()).json"
  try {
    npx nx test gateway --runInBand --passWithNoTests=false --testPathPatterns=$Spec --testNamePattern=$Pattern --json --outputFile=$jsonPath
    if ($LASTEXITCODE -ne 0) { throw "Gateway spec failed: $Tag" }
    $result = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ([int]$result.numTotalTests -lt 1 -or [int]$result.numPassedTests -lt 1 -or [int]$result.numFailedTests -ne 0) { throw "zero/failing Gateway tests: $Tag" }
  }
  finally { Remove-Item -LiteralPath $jsonPath -Force -ErrorAction SilentlyContinue }
}
Invoke-NxJestNonZero 'apps/gateway/src/config/routes.spec.ts' 'routes substitute-vehicle to Trip with user auth' 'substitute-gateway-route'
Invoke-NxJestNonZero 'apps/gateway/src/proxy/proxy.access-gates.spec.ts' 'enforces OPERATOR_ADMIN auth and preserves Idempotency-Key for substitute-vehicle' 'substitute-gateway-access'
Invoke-NxJestNonZero 'apps/gateway/src/config/routes.spec.ts' 'routes passenger transfer confirmation to Booking with user auth' 'confirm-gateway-route'
Invoke-NxJestNonZero 'apps/gateway/src/proxy/proxy.access-gates.spec.ts' 'enforces DRIVER or ASSISTANT auth and preserves Idempotency-Key for passenger transfer confirmation' 'confirm-gateway-access'
$allowed = '^apps/gateway/src/(config/routes(\.spec)?\.ts|proxy/proxy\.access-gates\.spec\.ts)$'
$taskLedger = @(Get-Day34TaskLedger '34.8' $allowed)
$tsLedger = @($taskLedger | Where-Object { $_ -match '\.(ts|tsx)$' })
npx eslint $tsLedger
if ($LASTEXITCODE -ne 0) { throw 'Gateway changed-file lint failed' }
npx nx build gateway
if ($LASTEXITCODE -ne 0) { throw 'Gateway affected-project build failed' }
git diff --check -- $taskLedger
if ($LASTEXITCODE -ne 0) { throw 'diff check failed' }
```

## Dispatch order

1. Reviewer runs the PLAN-REVIEW gate on this revised `DRAFT`; no implementation task is dispatched before that gate and subsequent human plan approval.
2. Task 34.0 codifies the approved contracts and registries, including additive `409 TRIP_NOT_SUBSTITUTABLE` while preserving existing `422 TRIP_NOT_IN_PROGRESS`.
3. Task 34.1 freezes both shared event contracts and their exact parity fixtures.
4. Task 34.8 adds the endpoint-specific Gateway route/auth/idempotency seam tests required by both public endpoint tasks.
5. Task 34.2 exposes the Booking substitution-impact read seam.
6. Task 34.3 completes the Trip mutation and both canonical Outbox producers.
7. Task 34.4 adds BookingTransfer persistence and the reversible migration.
8. Task 34.5 consumes the substitution fact and creates transfers/Booking facts.
9. Task 34.6 confirms replacement passengers individually through BookingTransfer.
10. Task 34.7 adds Notification consumption and verifies persisted-row push recovery through deterministic queue identity.

No task is parallel-safe in the shared working tree: Task 34.1 owns the shared contracts consumed by later producers/consumers; Task 34.8 supplies mandatory verification consumed by both endpoint tasks; Tasks 34.2, 34.4, 34.5, and 34.6 share Booking repositories/DI/model surfaces; Tasks 34.2 and 34.3 share the internal seam; Tasks 34.3, 34.5, and 34.7 share event contracts. Run serially.

## Progress tracker

> Orchestrator bookkeeping - the main thread updates this table after each `/implement-task`
> or task completed by `/execute-day`, with the task's review verdict. Informational only;
> `/audit-day` must independently re-verify all work.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 34.0 | done | APPROVE | 2026-07-25 | Codified the contract freeze and human-approved Trip-owned seat-type derivation; plan verification bookkeeping corrected. |
| 34.1 | done | APPROVE | 2026-07-25 | Added strict shared contracts and 4 focused parity tests; one lint-only patch, no scope expansion. |
| 34.2 | done | APPROVE | 2026-07-25 | Added the tenant-scoped impact seam; reviewer patch added real PostgreSQL repository coverage, no scope expansion. |
| 34.3 | done | APPROVE | 2026-07-26 | Completed real HTTP/DB/Redis/Outbox substitution flow; two reviewer rounds replaced superficial tests and expanded guard/restart coverage. |
| 34.4 | done | APPROVE | 2026-07-26 | Added nullable-seat BookingTransfer persistence and reversible fail-closed migration; reviewer patch propagated truthful nullability through six direct consumers with 47 focused tests green. |
| 34.5 | done | APPROVE | 2026-07-26 | Added atomic substitution consumer and per-Booking transfer fact; one review patch accepts canonical empty mappings as a processed inbox no-op, with 9 focused tests green. |
| 34.6 | done | APPROVE | 2026-07-26 | Added bodyless crew confirmation endpoint; one review patch moved idempotency/body enforcement to shared middleware, locked Booking+transfer rows, and proved same-key replay with real PostgreSQL concurrency coverage. |
| 34.7 | todo | - | - | - |
| 34.8 | done | APPROVE | 2026-07-25 | Added both endpoint Gateway seams; one review round fixed absolute Nx/Jest evidence paths, no scope expansion. |

Legend: todo | in progress | done (reviewer APPROVED + targeted verification green) | done-with-carryover | blocked

## Repository-state note

- `API-Response.md` remains an unrelated untracked user file. Every task forbids touching or deleting it.

## Open questions

- None.
