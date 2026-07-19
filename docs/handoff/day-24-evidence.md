# Day 24 focused integration evidence

## Scope and mode

This is `FOCUSED_INTEGRATION` evidence for Day 24 only. It uses `ISOLATED_TESTSERVER`, the frozen clock `2026-07-18T12:00:00Z`, and `DIRECT` job invocation. It does not wait for either five-minute Hangfire schedule, does not claim a live cross-service journey, and does not claim full regression. Full regression remains owned by `/audit-day 24`.

The only recurring-job proof executed by Task 24.10 is the two named Booking PostgreSQL fixtures:

- `VietRide.Booking.IntegrationTests.Jobs.Day24StopDisabledAutoFallbackIntegrationTests`
- `VietRide.Booking.IntegrationTests.Jobs.Day24NoShowDetectionIntegrationTests`

Each fixture uses a UUID-suffixed isolated database, substitutes `IClock`, invokes the job method directly, and drops only its own database. A zero-test, skipped, pending, todo, failed, aborted, timed-out, or not-executed result is a failure.

## Deterministic evidence map

| Behavior | Focused executable owner | Assertion boundary |
|---|---|---|
| Stop disable and action creation | Day 24.1/24.2 focused endpoint, transaction, consumer, Outbox, and restart suites | Sole `DELETE` route; replacement validation; exact affected bookings/actions and event identity |
| Passenger replacement, fallback, terminal refusal | Day 24.3 focused choice suites | Edit pickup/dropoff, bodyless fallback, `STOP_DISABLED_REFUSED`, equality eligibility, ownership, atomic resolution/refund |
| Automatic fallback | `Day24StopDisabledAutoFallbackIntegrationTests` | Strict `deadline < now`; equality untouched; next direct pass resolves; pickup/dropoff mapping; concurrency/rerun; exact pending Outbox; no schedule-change/cancel/refund event |
| No-show transition | `Day24NoShowDetectionIntegrationTests` plus the Day 24.6 focused no-show suites | Along-route and terminal anchors, equality, all-pending, mixed boarded 3/5, all-boarded 5/5, `NO_SHOW`/`PARTIAL_NO_SHOW`, `MARK_NO_SHOW`, fail-closed upstream behavior, exact pending Outbox |
| Pending count | Day 24.7 unit and PostgreSQL endpoint suites | Exact confirmed + pending + trip + pickup-stop + operator predicate; positive/zero; invalid Internal JWT; malformed/all-zero Guid; absent logical references return raw zero |
| Driver departure | Day 24.8 unit/integration, Outbox, and RabbitMQ suites | `IN_PROGRESS` + `ARRIVED`; durable timestamp; zero/positive count; race winner/loser; restart; `TRIP_STOP_NOT_ARRIVED`; `TRIP_STOP_ALREADY_DEPARTED`; upstream rollback |
| Events and notifications | Day 24.9 strict-contract and Notification consumer/e2e mapper suites | Exact routing keys and payloads; recipient mapping; malformed reject policy; EventId dedupe; `eventId == OutboxEvent.Id == RabbitMQ MessageId` |

## Idempotency and HTTP boundary

The Postman folder `Day 24 - Stop disable and no-show evidence` is a manual boundary companion. It records exact `Idempotency-Key` replay, mismatch, and new-key terminal conflict probes for stop disable, passenger choice, and driver departure. Public requests use `{{baseUrl}}` and assert ADR 0004 envelopes. The raw pending-count probe alone uses `{{bookingBaseUrl}}`, requires `X-Internal-Auth`, and asserts exactly `{tripId,stopId,pendingPassengerCount}`.

Gateway/Postman evidence is deliberately limited to existing route matching, user/internal JWT rejection, and public-vs-raw envelope shape. It does not prove recurring-job execution. Tokens remain empty in the committed environment and must be supplied locally.

Any optional Notification observation must use exactly 20 attempts at 100 ms, maximum 2 seconds. No broad fixture deletion or cleanup is authorized.

## Task 24.10 command result

The verification matrix ran the two named Booking fixture filters in Release mode against PostgreSQL: fallback passed 5/5 and no-show passed 5/5, with zero failed or skipped tests. It then executes syntax checks, TAP tests, focused static/Postman validation, JSON parsing, LF validation, and `git diff --check`. This file records the reproducible command contract; terminal/TRX output is the authoritative execution result and is intentionally not committed with machine-specific paths.
