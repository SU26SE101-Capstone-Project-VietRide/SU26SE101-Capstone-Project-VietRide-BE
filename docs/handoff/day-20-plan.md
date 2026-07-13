# Day 20 — Sprint 3 buffer + demo prep

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 20 — Sprint 3 buffer + demo prep (no Jira key listed)
- **Prior checklist**: `docs/handoff/day-19-checklist.md` (`not found`; Day-19 completion evidence is available only in `docs/handoff/day-19-plan.md` at planning time)
- **Plan status**: ✅ APPROVED

## Objective

Produce a reproducible Gateway-level Sprint-3 demonstration path: passenger registration/login, VNPay sandbox wallet top-up, trip search, booking/payment, cancellation, and operator booking monitoring. Extend the existing local E2E runner so it executes the cumulative Day-11–18 feature set (the timeline's eight Sprint-3 implementation subtasks) plus the Day-19 monitor route, without committing credentials or mutating pre-existing data. Use the buffer only for defects evidenced by that run; do not turn it into unbounded refactoring. A review-ready demo deck remains blocked until its required format and destination are supplied.

## Success criteria (DoD — binary, verifiable)

- [ ] The Gateway-level Postman/Newman passenger journey `register → login → topup → search → book → pay → cancel` exits zero against the documented local VNPay sandbox/test configuration, with no real credentials committed.
- [ ] The cumulative local runner executes the eight Day-11–18 Sprint-3 feature folders and Day-19 operator booking monitor coverage in dependency order, exits non-zero on an assertion failure, and cleans every deterministic fixture it creates.
- [ ] All defects found by that reproducible run are either fixed with a focused regression test and the affected service verification green, or recorded as a human-approved carry-over with evidence and scope.
- [ ] An external reviewer can execute the documented collection/environment and reproduce a green run without hidden state.
- [ ] **Conditional human-completion gate (Q1):** after the human confirms the deck format and destination, a Sprint-3 demo deck is present there and covers the full booking flow with VNPay sandbox plus operator monitor. Until Q1 is resolved, this gate is blocked rather than counted as a failed executable E2E DoD item.

## Contract changes

No new REST, event-routing-key, database, error-code, or Gateway-route contract is authorized by Day 20. The existing Day-15 VNPay IPN contract, Day-17 cancellation contract, and Day-19 operator-monitor contracts are exercised only. Any E2E-discovered contract conflict must stop the affected task and be resolved against `SU26SE101_VIETRIDE_technical_context_v7.md`, `VietRide_API_Contract_v1.md`, and `BACKEND_SOURCE_OF_TRUTH.md` before a repair changes a public contract.

## Tasks

### Task 20.0 — Pre-reqs / architecture baseline: freeze Sprint-3 E2E matrix and runner boundaries

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `scripts/run-full-e2e-local.mjs`; `package.json` (only the existing `postman:full:local` script if its command must change, no dependency/version changes); `docs/api/postman/README.md`; `docs/api/postman/vietride.postman_collection.json`; `docs/api/postman/vietride.local.postman_environment.json`; new `docs/handoff/day-20-e2e-matrix.md` |
| forbidden scope | `.env`, `.env.example`, real VNPay/Google/JWT values, secrets, Docker/infra configuration, all service production source, migrations/schema, API/BSOT/ADR changes, git operations, package installs, and any file outside the declared write set |
| depends on | PLAN-REVIEW approval; Day-11 through Day-19 completed implementation according to their approved plan trackers. Task 20.1 and Task 20.2 depend on this task. |
| invariant flags | LF for `.mjs`/`.json`/`.md`; Gateway `:3000` is the public test boundary; test JWTs are short-lived/generated at run time and never printed in full; fixture writes are deterministic, isolated, and cleaned in reverse dependency order in `try/finally`; “all 8 Sprint 3 subtasks” means eight mandatory named matrix stages Day 11 through Day 18, not eight selected happy paths; Day 19 monitor is additionally required by the Day-20 demo bullet; `run-full-e2e-local.mjs` currently omits dedicated Day-12, Day-16, and Day-19 execution, so it is not sufficient until Task 20.1 closes those gaps; no new dependency; no direct cross-service DB access in application code. |
| acceptance | a checked-in matrix is authoritative: it maps each mandatory named stage below to its exact collection folder/harness invocation, service mode (real seam or documented dev stub), fixture owner, cleanup responsibility, and at least one observable assertion: **D11** trip generation/search/detail/seat-map; **D12** atomic five-seat-capable lock, competing-lock rejection, TTL release, and held→booked transition; **D13** pickup/dropoff cutoff plus two-leg round-trip atomic locking/per-leg independence; **D14** voucher applicability/consent/usage behavior; **D15** VNPay top-up signed-IPN credit and replay idempotency; **D16** Wallet and VNPay booking-payment confirmation plus cancellation/refund-to-wallet; **D17** cancellation-policy amount, idempotency, event-driven refund, and BookingStats; **D18** driver schedule, manifest no-PII, boarding/QR wrong-trip guards; and **D19** operator-monitor own-tenant list/detail plus denial. The matrix explicitly identifies every currently missing stage and its planned new/extended invocation; a permitted exclusion requires a named reason, SOT/timeline citation, human approval, and an explicit `SKIP` output—never replacement by another stage. `npm run postman:full:local` is the single documented entry point and must invoke these named matrix stages in dependency order, exit non-zero if any required stage is missing, skipped without an approved exclusion, or fails; JSON parses and no environment file contains a usable secret/token. |
| source citations | `BE_TIMELINE_VU.md` Day 11–20 (lines 131–215); `docs/handoff/day-19-plan.md` progress tracker and Task 19.4 verification-harness precedent; `scripts/run-full-e2e-local.mjs`; `package.json` existing `postman:full:local`; `docs/api/postman/README.md` full-run and secret-redaction rules; `BACKEND_SOURCE_OF_TRUTH.md` §§5.4–5.9, 6.10, 7.6; ADR 0004. |

### Task 20.1 — Implement the deterministic passenger journey and operator-monitor E2E run

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `scripts/run-full-e2e-local.mjs`; new `scripts/run-day20-sprint3-e2e-local.mjs`; `docs/api/postman/vietride.postman_collection.json`; `docs/api/postman/vietride.local.postman_environment.json`; `docs/api/postman/README.md`; `package.json` (one version-less script entry only); test-only fixture/harness files under `scripts/` needed by the new runner |
| forbidden scope | `.env`, `.env.example`, actual VNPay merchant credentials/hash secrets, committed JWTs, production `apps/**/src/**` code, DB migrations/schema, Gateway route changes, contract/BSOT/ADR edits, package/dependency changes, git operations, and mutation of pre-existing local records |
| depends on | 20.0; approved Day-11–19 implementations; Task 20.2 only after this runner produces reproducible failure evidence. |
| invariant flags | LF JS/JSON/MD; all external-facing requests use Gateway routes and ADR-0004 envelopes except the documented VNPay IPN response shape; passenger flow uses the Day-15 VNPay sandbox/IPN mechanism, never a bank credential or Return URL as source of truth; booking cancellation requires `Idempotency-Key` and must observe event-driven refund completion before asserting wallet/state; monitor uses an `OPERATOR_ADMIN`/`OPERATOR_STAFF` token whose `operatorId` is the fixture tenant and proves a cross-tenant denial; runner must preserve real-seam/dev-stub mode per the frozen 20.0 matrix and restore any temporary mode; cleanup must run after both Newman assertion and process failures. |
| acceptance | The new/extended harnesses implement every mandatory 20.0 matrix stage and print a distinct redacted `PASS`/`FAIL`/approved-`SKIP` line for each: **D11** generation/search/detail/seat-map; **D12** competing atomic seat-lock, TTL release, held→booked; **D13** pickup/dropoff cutoff and round-trip two-leg/per-leg-independence; **D14** voucher validation/consent/usage; **D15** signed VNPay top-up IPN, wallet credit, replay; **D16** both Wallet and VNPay booking-payment confirmations plus cancel/refund-to-Wallet; **D17** cancellation/refund/BookingStats; **D18** schedule/manifest-no-PII/boarding/QR wrong-trip; **D19** own-tenant monitor list/detail and tenant-or-role denial. `npm run postman:day20:local` is the end-to-end passenger journey stage—register/login, VNPay top-up request plus signed sandbox IPN, wallet-balance assertion, trip search, passenger booking/payment, cancellation plus eventual refund assertion, and Day-19 monitoring—through `http://localhost:3000`; it does not substitute for the other named stages. `npm run postman:full:local` invokes every matrix-required stage in dependency order, including new/extended D12, D16, and D19 stages, exits non-zero if any named required stage is absent/skipped/fails, and does not collapse failures into a generic summary; any permitted exclusion must be the approved, explicit `SKIP` from 20.0. All runners verify their deterministic fixtures are absent after success and forced Newman failure; collection/environment remain importable and have placeholders only. |
| source citations | `BE_TIMELINE_VU.md` Day 11–20; `VietRide_API_Contract_v1.md` Day-15 payment/VNPay, booking/cancellation, and §Booking Service `GET /v1/operator/bookings` / `{id}`; `BACKEND_SOURCE_OF_TRUTH.md` §§5.4–5.9, 6.2, 6.10, 7.6; `docs/handoff/day-15-checklist.md`, `day-17-checklist.md`, `day-18-checklist.md`; `docs/handoff/day-19-plan.md` Task 19.4; `scripts/run-day15-newman-local.mjs`, `run-day17-newman-local.mjs`, `run-day18-newman-local.mjs`, `run-day19-newman-local.mjs`. |

### Task 20.2 — Evidence-led Sprint-3 bug sweep and regression closure

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | `docs/handoff/day-20-e2e-matrix.md`; `docs/api/postman/vietride.postman_collection.json`; `docs/api/postman/README.md`; `scripts/run-day20-sprint3-e2e-local.mjs`; focused test files colocated with an E2E-proven defect under `apps/identity/tests/**`, `apps/trip/tests/**`, `apps/booking/tests/**`, `apps/payment/tests/**`, or `apps/gateway/src/**/*.spec.ts`; the specific production file(s) named in an approved defect record only |
| forbidden scope | `.env`, secrets, dependency/package versions, broad refactors, unrelated feature work, migrations/schema changes, API/BSOT/ADR changes without a demonstrated SOT conflict and human approval, other services, git operations; do not change a production file merely because its directory is listed — each repair needs a failing E2E stage, root cause, and reviewer-confirmed minimal write set. |
| depends on | 20.1; every repair is serial in the shared tree; a defect outside the listed service/test scopes requires a new approved task rather than scope expansion. |
| invariant flags | no invented requirement: existing SOT outranks timeline; preserve Clean Architecture direction and controller→`MediatR.Send`; .cs CRLF / TS-JS-MD LF; CPM has no `Version=` in `.csproj`; MediatR v11; BCrypt cost 12; BIGINT VND with no decimal and no floor-to-1000 transformation; Outbox/event consumers remain idempotent; no cross-DB FK/direct DB access; Idempotency-Key behavior remains intact; no observability/dependency additions. |
| acceptance | for each failure, the matrix contains command/output (redacted), endpoint/event/state evidence, SOT citation, root cause, minimal approved file list, regression test, and re-run result; relevant service build, `dotnet format --verify-no-changes`, and focused unit/integration tests pass (Gateway lint/test for Gateway repair); `npm run postman:day20:local` and `npm run postman:full:local` are green after closure, or each unresolved external/pre-existing blocker is documented with owner, reproducible command, and explicit human carry-over decision; no speculative repair lands. |
| source citations | `BE_TIMELINE_VU.md` Day 20 bug-sweep/DoD/review; `SU26SE101_VIETRIDE_technical_context_v7.md` business/status rules applicable to the failed flow; `VietRide_API_Contract_v1.md` affected endpoint; `BACKEND_SOURCE_OF_TRUTH.md` §§2.1, 3.2, 5.4–5.9, 6.10, 7.6, 8.1; ADR 0003/0004; `AGENTS.md` hard invariants and verification commands. |

### Task 20.3 — Sprint-3 demo deck and external-review handoff

| Field | Value |
|---|---|
| stack/owner | cross-cutting |
| implement agent | worker |
| review agent | reviewer |
| skill | (none) |
| owned files (write set) | **Blocked pending Open question Q1:** no existing Sprint-3 deck/template/destination is discoverable. After the human confirms a repository destination and format, only that confirmed deck file plus `docs/api/postman/README.md` and `docs/handoff/day-20-e2e-matrix.md` may be edited. |
| forbidden scope | `.env`, secrets, service code, scripts other than documentation corrections, migrations/schema, contracts/routes, package/dependency changes, git operations, and creating a presentation artifact at an invented path or in an invented format. |
| depends on | Q1 resolved; 20.1 green; 20.2 complete or explicitly human-approved carry-over. |
| invariant flags | deck/demo must not embed secrets, JWTs, customer/production data, or a claim unsupported by the final E2E evidence; it must distinguish VNPay sandbox/IPN test simulation from a real bank transaction; documentation uses LF; no product contract is changed. |
| acceptance | the human-confirmed deck location contains a review-ready deck covering: objective, local prerequisites, passenger register/login, VNPay sandbox top-up/IPN, trip search, booking/payment, cancellation/refund, tenant-scoped operator monitor, exact runnable command, expected evidence, and known exclusions/carry-over; an external reviewer follows the README and green command without hidden fixture state. |
| source citations | `BE_TIMELINE_VU.md` Day 20 DoD/review/demo bullets; `docs/handoff/sprint-2-demo-script.md` (only a script precedent, not an authorized Sprint-3 deck template); Task 20.0 matrix; Task 20.1 final redacted runner evidence. |

## Dispatch order

1. PLAN-REVIEW this plan; resolve Q1 before dispatching Task 20.3.
2. Task 20.0 — freeze E2E coverage, service-mode boundaries, fixture ownership, and the exact Stage matrix.
3. Task 20.1 — implement/run the full passenger-journey and Day-19 monitor stage after 20.0. Not parallel-safe: it owns the shared Postman collection, environment, runner, and README.
4. Task 20.2 — use only reproducible 20.1 failures for focused repairs; serial, one defect at a time in this shared tree. If no failure appears, record the clean sweep and do not manufacture code work.
5. Task 20.3 — create the demo deck/handoff only after Q1 and green E2E evidence. Not parallel-safe with 20.1/20.2 because it consumes their final command and results.

No tasks are parallel-safe by default: Tasks 20.0–20.2 share the collection, environment, runner, README, and evidence matrix; Git worktrees are forbidden.

## Progress tracker

> Orchestrator bookkeeping — the main thread updates this table after each `/implement-task` with the review verdict. Informational only; `/audit-day` independently re-verifies all evidence.

| Task | Status | Review verdict | Date | Notes |
|---|---|---|---|---|
| 20.0 | ✅ done | APPROVE | 2026-07-13 | Matrix/runner boundary frozen; D12/D16 gates intentionally fail until Task 20.1; verification passed. |
| 20.1 | ⬜ todo | — | — | — |
| 20.2 | ⬜ todo | — | — | — |
| 20.3 | ⬜ todo | — | — | Blocked by Q1. |

Legend: ⬜ todo · 🔄 in progress · ✅ done (reviewer APPROVED + human `/verify`) · ⚠️ done-with-carryover · ❌ blocked

## Open questions

1. **Demo deck format and destination (blocking Task 20.3):** the timeline requires a “demo deck,” but the repository contains only `docs/handoff/sprint-2-demo-script.md`; no Sprint-3 presentation template, required file format, or owner-approved destination is present. Should the deliverable be a Markdown demo script in `docs/handoff/`, a `.pptx` at a specified path, or an externally managed deck referenced from the repo?
2. **VNPay execution boundary:** Day 20 says “VNPay sandbox,” while the existing Day-15 Postman collection generates a signed test IPN against local configuration. Confirm whether this documented local sandbox/IPN simulation is sufficient for the Sprint review, or provide the approved sandbox merchant environment/credential-handling procedure. No credential will be committed either way.
