# Day 20 Sprint 3 local E2E matrix

Status: authoritative execution matrix for Day 20. The only full-run entry point is
`npm run postman:full:local`; it calls `scripts/run-full-e2e-local.mjs` and exercises the
Gateway at `http://localhost:3000` for every public request. Harness-only fixture setup and
post-run inspection are local verification seams, not application request paths.

The matrix follows `BE_TIMELINE_VU.md` Days 11-20 and the ADR 0004 envelope/error rules. Each
harness owns deterministic fixture setup, short-lived generated test JWTs, and `try/finally`
cleanup in reverse dependency order. JWTs and secrets must never be printed in full.

| Stage | Required behavior and observable assertion | Collection folder / harness invocation | Mode | Fixture owner and cleanup |
|---|---|---|---|---|
| D11 | Generate a trip, search it, read detail and seat map; assert a generated active trip has available seats and the internal lock/release/book seam reaches its expected states. | `Trip — Day 11 search + seat-lock flow` / `node scripts/run-day11-newman-local.js` | Real Gateway + Trip/Identity seams | Day-11 harness; it removes its Identity and Trip prerequisites after its checks. |
| D12 | Atomic lock of up to five seats, competing-lock rejection, TTL release, and HELD → BOOKED transition; assert all five locks are acquired together or none are, loser is rejected, expiry releases, and payment confirmation books held seats. | **Missing — Task 20.1 must add** `Booking — Day 12 seat lock flow` / `node scripts/run-day12-newman-local.mjs` | Documented local Booking Trip/Payment dev stubs | New Day-12 harness; reverse cleanup of Booking children before bookings, then Trip/Identity prerequisites. |
| D13 | Pickup/dropoff acceptance before cutoff and rejection at/after cutoff; two-leg round-trip atomic locks and per-leg cancellation independence. | `Booking — Bookings` / `node scripts/run-day13-newman-local.js` | Documented local Booking Trip/Payment/Identity dev stubs | Day-13 harness; reverse cleanup in its `finally`. |
| D14 | Voucher route/operator applicability, consent requirement, and usage behavior; assert inapplicable/no-consent checkout fails and eligible checkout consumes usage without retroactively changing a confirmed discount. | Voucher collection coverage / `node scripts/run-day14-voucher-e2e.mjs` | Documented local Booking dev stubs | Day-14 harness; reverse cleanup in its `finally`. |
| D15 | VNPay top-up signed IPN credits Wallet once; replay is idempotent. | `Payment - Wallet top-up (Day 15)` / `node scripts/run-day15-newman-local.mjs` | Documented local VNPay/Booking dev stubs | Day-15 harness; reverse cleanup in its `finally`. |
| D16 | Wallet and VNPay booking-payment confirmation, then cancellation/refund-to-wallet; assert both payment paths confirm the booking and the cancellation credits Wallet. | **Missing — Task 20.1 must add** `Payment - Day 16 booking payment + refund flow` / `node scripts/run-day16-newman-local.mjs` | Documented local VNPay/Booking dev stubs | New Day-16 harness; reverse cleanup of payment rows before booking and identity/trip prerequisites. |
| D17 | Policy-derived cancellation amount, idempotent cancellation, event-driven wallet refund, and BookingStats; assert preview equals actual, repeat cannot double-count, and stats converge after the event. | `Booking - Day 17 cancellation + booking stats carry-over` / `node scripts/run-day17-newman-local.mjs` | Documented local Booking dev stubs | Day-17 harness; reverse cleanup in its `finally`. |
| D18 | Driver schedule, no-PII manifest, boarding, and wrong-trip QR guard; assert only assigned schedule is visible, manifest has no passenger PII, boarding persists, and wrong-trip QR is rejected. | `Driver - Day 18 schedule + manifest + boarding flow` / `node scripts/run-day18-newman-local.mjs`, then `node scripts/run-day18-crossday-local.mjs` | Real Gateway + Trip/Booking seams | Day-18 harnesses; each cleans its fixtures in reverse dependency order. |
| D19 | Operator monitor own-tenant list/detail and denial; assert own rows are returned and a foreign row is denied. | `Booking - Day 19 operator booking reads` / `node scripts/run-day19-newman-local.mjs` | Real Gateway + Booking/Identity seams | Day-19 harness; reverse cleanup of history/tickets/passengers, bookings, users, then operators. |

## Runner gates and exclusions

`run-full-e2e-local.mjs` treats D11-D19 as required stages in dependency order. Before it starts
Docker, it checks every listed harness exists. A missing harness, a failed invocation, or an
unapproved skip exits non-zero. D12 and D16 are intentionally failing missing-stage gates until
Task 20.1 supplies their named harnesses; they may not be replaced by another happy path.

No Sprint 3 stage has an approved exclusion. A future exclusion must name the stage, reason,
source-of-truth/timeline citation, and human approval, and the runner must emit an explicit
`SKIP | DNN | <approved reason>` line. Google OAuth is outside this Sprint-3 matrix; its existing
external-credential skip is therefore not an exclusion for any D11-D19 stage.
