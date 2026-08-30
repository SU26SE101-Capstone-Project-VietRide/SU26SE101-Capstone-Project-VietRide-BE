# ADR 0006 — Parcel chain of custody, incidents, claims, and compensation

**Status:** Accepted — 2026-08-21
**Owners:** Vũ (BE lead)
**Supersedes:** none
**Amends:** [SU26SE101_VIETRIDE_technical_context_v7.md](../../SU26SE101_VIETRIDE_technical_context_v7.md), [BACKEND_SOURCE_OF_TRUTH.md](../../BACKEND_SOURCE_OF_TRUTH.md), [VietRide_API_Contract_v1.md](../../VietRide_API_Contract_v1.md)

## Context

`ParcelStatusHistory` records the commercial lifecycle but cannot prove physical possession. The former unload flow accepted a stop that had once reached `ARRIVED`, even after departure, and did not persist the actual station/stop, handoff actor, scan confidence, forwarding leg, search case, claim decision, or operator-funded payout. A wrong physical unload outside the API could therefore leave no reliable trail.

## Decision

### Physical custody is append-only

Parcel owns `ParcelTransitLeg`, append-only `ParcelCustodyEvent`, and the replaceable `ParcelCurrentCustody` projection. Each custody fact records expected and actual locations, Trip/vehicle, actor, source, evidence references, occurrence/recording timestamps, an idempotency key, and sequence. The database rejects update/delete of custody events. QR/ParcelCode is the primary identity; package photos, description, dimensions, weight, and serial/IMEI are corroborating evidence.

Public tracking exposes only the latest confirmed business location and milestones. GPS may support an investigation but is never custody proof. Tracking confidence is `CONFIRMED_SCAN`, `MANUAL_EXCEPTION`, `INFERRED_FROM_MANIFEST`, or `UNKNOWN`.

Unload requires the requested actual location to equal both the Parcel destination and the Trip operational location. A route stop must be `ARRIVED` with no actual departure; terminal delivery requires the destination-arrival anchor. A mismatch returns `409 PARCEL_CUSTODY_LOCATION_MISMATCH` before Parcel or Trip cargo mutation.

Manual handling is explicit. An unreadable/missing QR requires a photographed `MANUAL_CUSTODY_EXCEPTION` or an `UNIDENTIFIED_PACKAGE` with a temporary tag. A stop close reconciles expected, scanned, manual-exception, and unresolved Parcels. When unresolved cargo remains, the Assistant may create a `PENDING_APPROVAL` stop-departure request but cannot name or impersonate its reviewer. The assigned Driver or a same-tenant Operator Staff/Admin decides it using identity from that reviewer's JWT. Trip calls the Parcel clearance endpoint before committing departure and fails closed unless it receives `CLEAR` or an approved override matching the exact current unresolved snapshot. Absence of a scan does not by itself confirm loss.

Reconciliation trusts only persisted custody facts. Clients do not submit asserted scanned/manual
Parcel lists: normal stop reconciliation has an empty body, an override request supplies only its
reason, and destination reconciliation is bodyless. A Trip must have an assigned Assistant before
it may accept a new Parcel or be selected as a transfer/forwarding target; Driver remains the
supervisor and is not granted Assistant cargo mutations in v1.

Trip emits `trip.stop.departed` for every committed stop departure, independently of the
passenger-only `trip.stop.departed_with_pending` warning. Parcel consumes the operational fact to
reconcile its own manifest. `trip.destination.arrived` opens the normal terminal unload window; it
does not classify a terminal-bound Parcel that is still on the vehicle as missing. The fallback
missing search starts only when `trip.trip.completed` still finds the Parcel `LOADED|IN_TRANSIT`,
or from an explicit report/reconciliation exception. Neither arrival/departure fact is treated as
custody proof or used to infer a new physical location.

### Incident/search owns loss; ParcelStatus does not

`ParcelIncident` owns missing, wrong-stop, identity-mismatch, unscanned-handoff, not-received, damage, and partial-loss cases. The loss branch is `OPEN -> SEARCHING -> ESCALATED -> SEARCH_EXPIRED -> LOST_CONFIRMED`; recovered cargo follows `FOUND -> FORWARDING -> RESOLVED -> CLOSED`. `LOST` is not added to `ParcelStatus`; active custody exceptions use `PENDING_OPERATOR_ACTION/CUSTODY_EXCEPTION` and retain the resume status.

The default search SLA is 72 hours, with search tasks for vehicle/manifest, crew, station, lost-and-found, next Trip/substitution, and evidence reconciliation. Wrong-station recovery creates a new transit leg and paired `FORWARDED_OUT`/`FORWARDED_IN` custody facts. History is never rewritten and forwarding is not charged to the sender.

A `LOADED` custody fact activates its transit leg. A validated destination unload completes that leg; a search that reaches `LOST_CONFIRMED` marks its active leg lost. When cargo is verified `FOUND`, remaining open/in-progress search tasks are cancelled. When loss is confirmed, remaining open/in-progress search tasks fail with the terminal search result. Completed task evidence is never overwritten.

For a system-created `MISSING|MISSING_AFTER_DEPARTURE` false positive, the assigned Assistant may
scan the Parcel QR and confirm it is physically still on the assigned Trip vehicle. The operation
appends `FOUND@VEHICLE`, resolves the incident, cancels outstanding tasks, and restores the frozen
`LOADED|IN_TRANSIT` resume status atomically. Assistant-reported manual exceptions still require
Driver or Operator review and cannot use this shortcut while approval is pending.

### Compensation is operator policy, snapshotted per Parcel

The sender owns the claim and receives payout; a linked recipient may track and report an incident but cannot become beneficiary. VietRide orchestrates evidence, clearing, audit, and payout; the operator bears the financial obligation and VietRide does not advance funds.

The default policy is 50% of assessed direct loss capped at 30,000,000 VND, with no-proof fallback of four times Parcel freight, a 30-day claim window, 72-hour search SLA, seven-business-day decision SLA, and three-business-day payout SLA. An operator may configure rate `1..100` and a positive cap. Below-default terms require explicit acknowledgement. The accepted policy/version is disclosed and frozen on the Parcel; later policy changes do not alter an existing claim.

For evidence-backed claims:

```text
assessedLoss = min(provenDirectLoss, declaredValue) when declaredValue exists
cargoAward = min(round(assessedLoss * rate / 100), policyCap)
totalAward = cargoAward + max(parcelFreight - priorRefunds, 0)
```

Without acceptable proof, `cargoAward = min(fallbackMultiplier * parcelFreight, policyCap)`. Money arithmetic uses integer VND and midpoint rounding away from zero. Expected profit and indirect loss are excluded. Investigation records prohibited/misdeclared goods, inadequate packaging/natural characteristics, sender/recipient fault, state seizure, force majeure, and invalid evidence. Wrong stop, operator/crew loss, or a missing valid custody fact is presumed an operational breach unless evidence establishes otherwise.

### Payout is durable and tenant-fenced

Payment owns one `ParcelCompensationPayout` per compensation reference. The original approved claim uses `claimId`; an approved appeal adjustment uses `appealId` and only its positive supplementary delta. Before Trip settlement it debits that operator/Trip's PlatformWallet holding; after settlement it debits that operator's OperatorWallet. It then credits the sender PassengerWallet and writes `PARCEL_COMPENSATION` wallet/ledger references atomically. Insufficient funds move the payout and owning claim/appeal to `FUNDING_PENDING`; a recurring job retries against future settlement funds. Operator wallets never become negative, funds never cross operator tenants, and replayed decision snapshots cannot create a second payout.

Cross-service changes use Internal JWT, Outbox, routing keys registered in the BSOT, UUID-v4 idempotency on public mutations, ADR 0004 envelopes, and tenant-masked reads.

A sender may create one separate `ParcelClaimAppeal` after a paid or rejected decision. The
original claim never transitions to `APPEALED`; its decision reason, reviewer, award and payout
remain immutable. The appeal is `SUBMITTED -> UNDER_REVIEW -> UPHELD` or
`SUBMITTED -> UNDER_REVIEW -> ADJUSTMENT_APPROVED -> FUNDING_PENDING -> PAID`.
`OPERATOR_ADMIN` may uphold the original outcome or approve a recalculated award only when it is
greater than the amount already paid. Payment credits only the positive supplementary delta and
uses `appealId` as its unique compensation reference.

## Consequences

- A scan failure, physical wrong-stop unload, and a confirmed loss are separate auditable facts rather than one overloaded Parcel status.
- Recovery can identify custody gaps from manifest/Trip/crew evidence even without a QR scan, while clearly communicating reduced confidence.
- Claim decisions remain reproducible because declaration, policy, evidence, calculation, actor, and payout references are frozen.
- Custody tables, incident/search/claim tables, an unidentified-package queue, Payment payout persistence, Trip operational-location read, notification bindings, and two recurring jobs are added.
- Personal luggage without a Parcel code, insurance sales, automatic legal conclusions, and unconfigured high-value two-person approval remain outside v1.
