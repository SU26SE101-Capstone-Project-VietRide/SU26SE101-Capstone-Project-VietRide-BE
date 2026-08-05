# Driver route-change proposals — Independent feature audit

> Produced on 2026-08-04 after implementation fixes, independent static re-review, full related regression, rebuilt Docker images, and real Gateway/PostgreSQL/RabbitMQ E2E.

- **Feature**: Assigned Driver/Assistant route-change proposals and Operator Admin decisions
- **Branch**: `feat/driver-route-change-proposals`
- **Status**: ✅ READY

## DoD result

- [x] Assigned `DRIVER` and `ASSISTANT` can list active AlternativeRoutes and all Trip proposals, and create `EXISTING|CUSTOM` proposals only for editable Trips.
- [x] `OPERATOR_ADMIN` is tenant-masked and can list/detail/approve/reject; the direct emergency change-route endpoint remains available and admin-only.
- [x] Public contract uses CUSTOM `route` and reject `reason`; unknown fields are rejected, UUID-v4 idempotency is required, and malformed path UUIDs return `422 VALIDATION_ERROR`.
- [x] CUSTOM geometry requires a valid precision-5 polyline and validates existing tenant Station/OperatorStation/Stop waypoints.
- [x] Approval promotes CUSTOM atomically, applies the shared Trip route-change service, approves the winner, supersedes other pending proposals, writes audit records, and stages Outbox facts.
- [x] Source changes and terminal Trip transitions serialize with creation and expire pending proposals; direct route change supersedes pending proposals.
- [x] Fixed lock protocol is source advisory lock → Trip → pending proposal UUIDs → deterministic dependency rows.
- [x] Fault injection after Outbox staging rolls back promoted AlternativeRoute/stops, Trip mutation, proposal state, audit, and Outbox together.
- [x] Five proposal lifecycle facts reach Notification with active-admin fan-out for create, proposer delivery for terminal outcomes, and message-id dedupe.
- [x] The former two-active-AlternativeRoute cap is removed.
- [x] Required proposal schema, enum default, constraints, indexes, reversible migration, canonical DDL, API/Postman, SOT, registries, and changelog are synchronized.

## Verification evidence

| Command/check | Result | Evidence |
|---|---:|---|
| Independent static re-review | PASS | No remaining implementation, auth/tenant, contract, persistence, event, or Notification findings. |
| Trip Release build | PASS | 0 warnings, 0 errors. |
| Trip format verification | PASS | No diagnostics. |
| Trip unit suite | PASS | 648/648. |
| Trip integration suite | PASS | 313/313, including PostgreSQL lock races and atomic rollback fault injection. |
| EF migration apply → rollback → reapply | PASS | Proposal tables/indexes/constraint verified; no pending model changes. |
| Gateway/Notification/contracts build and lint | PASS | Existing third-party source-map warnings only. |
| Gateway/Notification/contracts tests | PASS | 42 suites, 268 tests. |
| Docker rebuild and nine-app health matrix | PASS | All nine public health endpoints returned HTTP 200. |
| EOL/whitespace/dependency guards | PASS | Changed C# files are CRLF; no CPM or banned dependency violation. |

## Real-data E2E evidence

- Candidate AlternativeRoute list: `200` and contained the seeded active route.
- Malformed Trip UUID: `422 VALIDATION_ERROR`.
- Missing and malformed CUSTOM polyline: rejected with `422`.
- CUSTOM create: `201 PENDING`; replay returned the same proposal; same key/different body returned `422 IDEMPOTENCY_KEY_MISMATCH`.
- Competing EXISTING proposal: `201 PENDING`; Operator list returned two pending proposals.
- CUSTOM approve: `200 APPROVED`; Trip referenced the promoted AlternativeRoute; competitor became `SUPERSEDED/ANOTHER_PROPOSAL_APPROVED`.
- PostgreSQL contained the expected atomic state and Outbox facts.
- RabbitMQ produced created/approved/superseded notifications for the correct recipients.
- All isolated E2E fixture, audit, Outbox, processed-message, and Notification rows were removed after verification.

## Scope notes

- Earlier full-monorepo probing showed Docker-host timeout noise in pre-existing Identity/Booking/Parcel integration fixtures. No files in those services are changed by this feature.
- The feature-related Trip, Gateway, contracts, Notification, PostgreSQL, RabbitMQ, and Docker matrices are fully green.
- The user's unrelated untracked files were preserved.

## Conclusion

The feature satisfies the approved plan and has no known feature blocker or carry-over.
