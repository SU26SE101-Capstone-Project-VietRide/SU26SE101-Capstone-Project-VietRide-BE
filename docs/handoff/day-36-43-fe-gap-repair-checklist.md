# Day 36/43 and FE Gap Repair — Final checklist

- **Plan**: `docs/handoff/day-36-43-fe-gap-repair-plan.md`
- **Branch**: `codex/fix-days-36-43-fe-gaps`
- **Audit date**: 2026-08-01
- **Status**: ✅ READY

## Repair tracker

| Task | Result | Key evidence |
| --- | --- | --- |
| R0 — source-of-truth/config freeze | PASS | Contract, technical context, BSOT changelog, DDL comments and VNPay 15-minute legacy default synchronized |
| R1 — Day 36 atomic fan-out/harness | PASS | Trip targeted tests, UUID-v4 helper tests, Day 36 E2E 10/10 |
| R2 — Day 43 parser | PASS | Relative/absolute routes, `[NonAction]` and duplicate discovery tests |
| R3 — Google avatar | PASS | Stored/custom/new/null avatar cases; Identity full suites green |
| R4 — authoritative deadlines | PASS | One-way/round-trip dueAt propagation and Payment expiry boundary tests |
| R5 — captured-but-unfulfillable Booking compensation | PASS | Allocation-scoped refund event/consumer, retry and idempotent wallet credit tests |
| R6 — IPN/expiry race recovery | PASS | PostgreSQL concurrency/IPN tests and parcel recovery regression |
| R7 — redirect lookup | PASS | Latest-first, owner/amount/deadline/HTTPS-authority validation and no-store endpoint tests |
| R8 — Booking History enrichment | PASS | 23/23 focused tests; single fail-open batch lookup |
| R9 — Passenger History enrichment | PASS | 30/30 focused tests; ticket forwarding and parcel eligibility |
| R10 — inventory/audit closure | PASS | Day 36, Day 43, parcel settlement, full solution/TS matrix, real stack health and hard invariants |

## Final acceptance

- [x] Day 36: five confirmed Bookings, 15 Tickets, 15 unique Shuttle manifests, complete Inbox
  markers and no confirmation delivery in the DLQ.
- [x] Day 43: exhaustive verifier and reliability E2E pass on the final code.
- [x] Google login returns the stored avatar; provider data does not overwrite an existing custom
  avatar; null remains omitted.
- [x] Booking passes the seat-lock expiry to Payment; round-trip uses the earlier leg; legacy
  `DueAt == null` uses `CreatedAt + 15 minutes`; `DueAt <= now` is expired.
- [x] VNPay capture after expiry is not abandoned. Expired/unseatable Bookings are not resurrected;
  exact trusted allocations are refunded idempotently to the wallet.
- [x] Parcel final-payment callback recovery reverses forfeiture only for an authoritative on-time
  capture and returns the Parcel to `READY_TO_LOAD`.
- [x] History returns only the latest owned, exact-amount, unexpired VNPay HTTPS URL from the exact
  configured authority. Payment lookup failure leaves base history successful with null URL.
- [x] No dependency, migration, index, cross-DB FK, `/bookings/me` endpoint or Gateway route was
  added.
- [x] `GOOGLE_LOGIN_AVATAR_SUBTASK.md` and `PAYMENT_HISTORY_BE_PLAN.md` remain untouched, untracked
  and outside every commit.

## Final verification summary

| Gate | Result |
| --- | --- |
| Five .NET service build/format/unit/integration matrices | PASS |
| Shared libraries build/format/tests | PASS — Messaging 43, Web 99, Reporting 11, Persistence 37 |
| TS lint/test/build matrix | PASS |
| Idempotency verifier and parser tests | PASS — inventory plus 9/9 tests |
| Day 36 real-app E2E | PASS — 10/10 checkpoints, 204.4s |
| Day 43 real-app reliability E2E | PASS — all checkpoints, 479s |
| Parcel settlement real-app E2E | PASS — 631 clean-build assertions; post-factory regression 643 assertions |
| Production-like compose build/up and nine-port health matrix | PASS — all HTTP 200 |
| Docker local and production compose config validation | PASS |
| CPM, banned dependencies, commit trailers, diff and EOL | PASS |

## Audit-discovered repairs

- Identity transactional email now supplies a stable per-operation UUID-v4 idempotency key.
- Day 36 fixtures now seed the authoritative Stop snapshot and an active Shuttle subscription.
- Parcel settlement E2E allocates a bindable deterministic port block instead of Windows-reserved
  fixed ports.
- RabbitMQ consumers recreate their topology after channel loss and capture their original channel
  for delivery settlement.
- RabbitMQ connection creation no longer holds a shared monitor during network retries, and each
  attempt is bounded to five seconds so Outbox failure remains observable.

## Remaining gaps

None. The branch is ready for human review and push/PR when requested.
