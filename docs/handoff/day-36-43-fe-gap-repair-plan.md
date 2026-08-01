# Day 36/43 and FE Gap Repair Plan

**Plan status:** APPROVED
**Branch:** `codex/fix-days-36-43-fe-gaps`
**Approved by:** Backend lead (conversation authorization, 2026-07-31)
**Full regression owner:** `audit-day`

## Objective

Close the reopened Day 36 and Day 43 gaps, return the stored avatar from Google login,
make payment deadlines authoritative across Booking/Payment/Parcel, compensate captured
but unfulfillable Bookings, and safely enrich Booking and Passenger history with resumable
VNPay redirect URLs.

## Locked decisions

- Work sequentially in the current branch. Git worktrees are forbidden.
- Booking charge `DueAt` is the Trip seat-lock `ExpiresAt`; round trips use the earlier
  expiry of the two legs.
- `DueAt <= now` is expired. Legacy payments with `DueAt == null` use
  `CreatedAt + 15 minutes`.
- Docker and production deployment defaults use a 15-minute legacy VNPay timeout. This
  does not lengthen the 10-minute Trip seat lock.
- An expired Booking is never resurrected. A captured payment that cannot confirm its
  Booking is refunded idempotently.
- Redirect lookup selects the latest payment by `CreatedAt DESC, Id DESC` first and only
  then evaluates eligibility. It never falls back to an older URL.
- No new dependency, migration, index, column, `/bookings/me` endpoint, Gateway route, or
  cross-database foreign key is allowed.
- Preserve the untracked root files `GOOGLE_LOGIN_AVATAR_SUBTASK.md` and
  `PAYMENT_HISTORY_BE_PLAN.md`; do not stage, move, edit, or delete them.

## Common scope envelope

- Read and follow the cited source-of-truth sections before implementation.
- Preserve CRLF for `.cs/.csproj/.sln/.props/.targets` and LF for
  `.ts/.js/.mjs/.json/.yml/.yaml/.md/.sql`.
- Preserve Central Package Management, MediatR v11, Clean Architecture boundaries,
  internal JWT conventions, Outbox delivery, durable Inbox idempotency, and ADR 0004.
- Never log signed VNPay URLs, URL query strings, lookup response bodies, or secrets.
- Task-owned paths are a baseline write set. Auto-expansion is limited to directly affected
  interface/implementation pairs, DI registrations, contracts, and focused tests within the
  same approved concern.
- Full solution/workspace regression is deferred to R10 and the two day audits.

## Contract changes

### Authentication

`POST /v1/auth/google` reuses `UserSummaryDto` and returns optional `avatarUrl` exactly like
password login. A null avatar remains omitted.

### Payment charge and deadline

- `POST /internal/v1/payments/charge` documents nullable `dueAt`.
- Booking passes the exact one-way seat-lock expiry or the earlier round-trip leg expiry.
- Payment expiry uses `DueAt ?? CreatedAt + 15 minutes` and expires at the inclusive
  `<= now` boundary.

### Booking refund compensation

Register `booking.payment_refund.requested`, one event per Booking allocation:

```json
{
  "eventId": "uuid",
  "occurredAt": "date-time",
  "paymentId": "uuid",
  "paymentReferenceType": "BOOKING|BOOKING_GROUP",
  "paymentReferenceId": "uuid",
  "bookingId": "uuid",
  "userId": "uuid",
  "amount": 350000,
  "reason": "PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY|SEAT_CONFIRMATION_FAILED"
}
```

Payment must validate the authoritative captured VNPay Payment, owner, original reference,
and trusted-context allocation. It must not trust event `userId` or `amount`. A one-way
Payment becomes `REFUNDED` after its exact refund. A `BOOKING_GROUP` becomes `REFUNDED` only
when every trusted-context allocation has matching exact `BOOKING_REFUND` wallet credits.

### Internal redirect lookup

```http
POST /internal/v1/payments/redirect-sessions/lookup
X-Internal-Auth: Bearer <internal-jwt>
```

```json
{
  "userId": "uuid",
  "references": [
    {
      "referenceType": "BOOKING|BOOKING_GROUP|PARCEL|PARCEL_ADDITIONAL",
      "referenceId": "uuid"
    }
  ]
}
```

The raw `200` list contains `paymentId`, `referenceType`, `referenceId`, `amount`, `dueAt`,
and `paymentRedirectUrl`. The endpoint is internal-only, `[SkipIdempotency]`, uses
`Cache-Control: no-store`, accepts 1-100 unique references, performs one `AsNoTracking`
database query, preserves request order, and omits ineligible items. The selected latest
Payment must have the exact owner, valid trusted context, `VNPAY`, `PENDING_REDIRECT`, a
persisted `DueAt > now`, and an absolute HTTPS URL without credentials whose authority
(host and port) exactly matches the configured VNPay base URI.

### Public history

`BookingHistoryItemDto` and `PassengerHistoryItemDto` gain a final root property:

```csharp
string? PaymentRedirectUrl = null
```

The property is always serialized. Lookup failure or ineligibility yields `null`.

## Dispatch order

`R0 -> R1 -> R2 -> R3 -> R4 -> R5 -> R6 -> R7 -> R8 -> R9 -> R10`

## Progress tracker

| Task | Status | Review verdict | Date | Notes |
| --- | --- | --- | --- | --- |
| R0 | Completed | APPROVE | 2026-07-31 | One reviewer patch round; no scope expansion |
| R1 | Completed | APPROVE | 2026-07-31 | Trip 5/5; Node 3/3; one Node reviewer patch |
| R2 | Completed | APPROVE | 2026-07-31 | Node 5/5; one reviewer patch for Nest route semantics |
| R3 | Completed | APPROVE | 2026-07-31 | Identity focused tests 7/7; no scope expansion |
| R4 | Completed | APPROVE | 2026-07-31 | Booking 31/31; Payment unit 4/4 + PostgreSQL 2/2 |
| R5 | Completed | APPROVE | 2026-08-01 | Booking/Payment compensation; scope expanded to Shared.Messaging retry, legacy effective-deadline producer, refund correlation/ledger support, SOT registry/schema comments, and focused tests; Booking 15+209, Payment 18+61, support 56+22, Shared 16 |
| R6 | Pending | Pending | - | VNPay IPN/expiry race |
| R7 | Pending | Pending | - | Internal redirect lookup |
| R8 | Pending | Pending | - | Booking History enrichment |
| R9 | Pending | Pending | - | Passenger History enrichment |
| R10 | Pending | Pending | - | Final inventory and audits |

## R0 - Freeze source-of-truth and configuration

**Implement/review:** `worker` -> `reviewer`
**Verification tier:** DOCS
**Dependencies:** none
**Skill:** none

**Owned files**

- `docs/handoff/day-36-43-fe-gap-repair-plan.md`
- `docs/handoff/day-36-plan.md`
- `docs/handoff/day-43-plan.md`
- `SU26SE101_VIETRIDE_technical_context_v7.md`
- `VietRide_API_Contract_v1.md`
- `BACKEND_SOURCE_OF_TRUTH.md`
- `db-schema/payment-wallet/schema.sql`
- `db-schema/payment-wallet/README.md`
- `infra/docker/docker-compose.yml`
- `infra/docker/docker-compose.prod.yml`
- `infra/docker/DEPLOY.md`

**Acceptance**

- Ratify all locked deadline, late-IPN, refund-only, event, lookup, history, and avatar rules.
- Register `PAYMENT_DEADLINE_PASSED` and `booking.payment_refund.requested`.
- Bump the BSOT version and append its changelog.
- Correct the Payment DDL documentation without adding a migration or index.
- Normalize `VNPAY_PAYMENT_TIMEOUT_MINUTES` from 10 to 15 in local/prod compose and DEPLOY.
- Add reopening addenda to Day 36 and Day 43 without rewriting prior history.

**Targeted verification**

```powershell
npx prettier --check SU26SE101_VIETRIDE_technical_context_v7.md VietRide_API_Contract_v1.md BACKEND_SOURCE_OF_TRUTH.md db-schema/payment-wallet/README.md infra/docker/DEPLOY.md docs/handoff/day-36-43-fe-gap-repair-plan.md docs/handoff/day-36-plan.md docs/handoff/day-43-plan.md
docker compose -f infra/docker/docker-compose.yml config --quiet
docker compose -f infra/docker/docker-compose.prod.yml config --quiet
git diff --check
```

## R1 - Repair Day 36

**Implement/review:** `dotnet-worker` plus `worker` -> `dotnet-reviewer` plus `reviewer`
**Verification tier:** PROJECT
**Dependencies:** R0
**Skill:** none

**Owned files**

- Trip Shuttle confirmed consumer and focused Inbox/manifest integration tests.
- `scripts/run-day36-shuttle-e2e.mjs`
- New process-local idempotency-key helper and Node tests under `scripts/`.

**Acceptance**

- Replace the consumer's nested transaction with `IUnitOfWork.ExecuteInTransactionAsync`.
- Real `EfIntegrationEventInbox<TripDbContext>` delivery commits/rolls back manifest writes and
  Inbox marker atomically; replay creates no duplicate manifests.
- Map readable harness labels to memoized UUID-v4 keys. The same label reuses the same UUID in
  one process; different labels and fresh processes receive fresh UUIDs.
- Preserve the intentional `day36-dispatch-1` same-key replay and all non-idempotency fixtures.

**Targeted verification**

```powershell
dotnet test apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ShuttlePersistenceIntegrationTests" --logger "console;verbosity=normal"
node --check scripts/run-day36-shuttle-e2e.mjs
node --check scripts/day36-idempotency-keys.mjs
node --test scripts/day36-idempotency-keys.test.mjs
npx eslint scripts/run-day36-shuttle-e2e.mjs scripts/day36-idempotency-keys.mjs scripts/day36-idempotency-keys.test.mjs
npx prettier --check scripts/run-day36-shuttle-e2e.mjs scripts/day36-idempotency-keys.mjs scripts/day36-idempotency-keys.test.mjs
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes --include apps/trip/src/VietRide.Trip.Infrastructure/Messaging apps/trip/tests/VietRide.Trip.IntegrationTests/Persistence
```

## R2 - Repair Day 43 discovery

**Implement/review:** `worker` -> `reviewer`
**Verification tier:** FOCUSED
**Dependencies:** R1
**Skill:** none

**Owned files**

- `scripts/verify-idempotency-inventory.mjs`
- Pure discovery helper module and Node tests under `scripts/`.

**Acceptance**

- Relative action routes combine with controller prefixes.
- `/...` and `~/...` action templates replace controller prefixes.
- `[NonAction]` methods are omitted.
- Pending-action endpoints are discovered once.
- Do not freeze final inventory yet.

**Targeted verification**

```powershell
node --check scripts/verify-idempotency-inventory.mjs
node --test scripts/verify-idempotency-inventory.test.mjs
npx eslint scripts/verify-idempotency-inventory.mjs scripts/verify-idempotency-inventory.test.mjs
npx prettier --check scripts/verify-idempotency-inventory.mjs scripts/verify-idempotency-inventory.test.mjs
```

## R3 - Return the stored Google avatar

**Implement/review:** `dotnet-worker` -> `dotnet-reviewer`
**Verification tier:** FOCUSED
**Dependencies:** R0
**Skill:** none

**Owned files**

- Google login handler and focused Identity tests.

**Acceptance**

- Existing linked users return their stored custom avatar.
- Google re-login does not overwrite an existing avatar.
- New Google accounts return the provider avatar seeded at account creation.
- Null remains omitted. Account linking, account locks, and token behavior remain intact.

**Targeted verification**

```powershell
dotnet test apps/identity/tests/VietRide.Identity.UnitTests/VietRide.Identity.UnitTests.csproj -c Release --filter "FullyQualifiedName~GoogleLoginCommandHandlerTests" --logger "console;verbosity=normal"
dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes --include apps/identity/src/VietRide.Identity.Application/Features/Auth/GoogleLogin apps/identity/tests/VietRide.Identity.UnitTests/Features/Auth
```

## R4 - Align Booking charge deadlines and Payment expiry

**Implement/review:** `dotnet-worker` -> `dotnet-reviewer`
**Verification tier:** PROJECT
**Dependencies:** R0, R3
**Skill:** none

**Owned files**

- Booking Payment client interface/implementations, one-way and round-trip creation handlers,
  and focused tests.
- Payment expiry command/repository interface/implementation and focused unit/PostgreSQL tests.

**Acceptance**

- Booking sends exact one-way lock expiry or the earlier round-trip expiry unchanged.
- Checkout compensation releases locks if the deadline has already elapsed.
- Payment expiry uses one atomic CAS predicate over `PENDING_REDIRECT` and the effective deadline.
- Future non-null deadlines do not expire based on age; null deadlines use the 15-minute fallback.
- `DueAt == now` expires; 30-minute Parcel final deadlines survive minute 15.
- Expiry transition and `payment.payment.expired` Outbox fact remain atomic.
- Capture `EXPLAIN ANALYZE` evidence without adding an index.

**Targeted verification**

```powershell
dotnet test apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj -c Release --filter "FullyQualifiedName~CreateBookingCommandHandlerTests|FullyQualifiedName~CreateRoundTripBookingCommandHandlerTests|FullyQualifiedName~PaymentServiceClientTests" --logger "console;verbosity=normal"
dotnet test apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj -c Release --filter "FullyQualifiedName~ExpirePaymentCommandHandlerTests" --logger "console;verbosity=normal"
dotnet test apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~PaymentExpiry" --logger "console;verbosity=normal"
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include apps/booking/src/VietRide.Booking.Application apps/booking/src/VietRide.Booking.Infrastructure/Http apps/booking/tests/VietRide.Booking.UnitTests
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --include apps/payment/src/VietRide.Payment.Application apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories apps/payment/tests
```

## R5 - Compensate captured but unfulfillable Bookings

**Implement/review:** `dotnet-worker` -> `reviewer`
**Verification tier:** PROJECT
**Dependencies:** R4
**Skill:** `add-integration-event`

**Owned files**

- Booking mirror/consumer, Trip confirmation client outcome, terminal transitions, refund event,
  Outbox/Inbox tests, and required registrations.
- Payment refund consumer, trusted-context reconciliation, repository pairs, focused tests, and
  required registration.

**Acceptance**

- Booking consumes `method`, `paidAt`, and `dueAt`.
- Trip confirmation reports `Success`, `DefinitiveSeatUnavailable`, or `TransientFailure`.
- `paidAt >= dueAt`, already-expired Booking, and definitive seat loss never confirm/reopen a
  Booking; they transition terminally and emit one refund request per allocation.
- Network, timeout, 5xx, and other transient Trip failures throw for RabbitMQ retry without
  expiring or refunding.
- Round-trip allocations sum exactly to the Payment amount.
- Event and terminal Booking transition share the Inbox transaction.
- Payment revalidates Payment, owner, original reference, and trusted allocation; duplicate
  delivery creates one wallet credit per allocation.
- Payment status and `payment.payment.refunded` emission follow the locked one-way/group rules.

**Targeted verification**

```powershell
dotnet test apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~BookingPaymentRefundRequested" --logger "console;verbosity=normal"
dotnet test apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~BookingPaymentRefundRequested" --logger "console;verbosity=normal"
dotnet test apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj -c Release --no-build
dotnet test apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj -c Release --no-build
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include apps/booking/src/VietRide.Booking.Application apps/booking/src/VietRide.Booking.Infrastructure apps/booking/tests/VietRide.Booking.IntegrationTests
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --include apps/payment/src/VietRide.Payment.Application apps/payment/src/VietRide.Payment.Infrastructure apps/payment/tests/VietRide.Payment.IntegrationTests
```

## R6 - Resolve VNPay IPN/expiry races

**Implement/review:** `dotnet-worker` -> `reviewer`
**Verification tier:** PROJECT
**Dependencies:** R5
**Skill:** `add-integration-event` for the existing success fact

**Owned files**

- Payment aggregate, VNPay IPN command handler, locking/reload repository methods, focused unit
  and PostgreSQL concurrency tests, and directly affected Parcel recovery tests.

**Acceptance**

- Verify signature, merchant, signed amount/status, and trusted `vnp_PayDate` before mutation.
- Row-lock/reload the exact Payment inside the transaction; stale tracked state cannot overwrite
  `EXPIRED`.
- Signed `paidAt < effectiveDueAt` records financial success once after an expiry race.
- `paidAt >= effectiveDueAt` records capture but downstream remains refund-only.
- Replay and simultaneous callbacks do not duplicate platform credit, ledger, or Outbox facts.
- Cover `DueAt == now`, expiry-wins, IPN-wins, callback-after-expiry, and concurrent replay.

**Targeted verification**

```powershell
dotnet test apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj -c Release --filter "FullyQualifiedName~ConfirmBookingPaymentCommandHandlerTests" --logger "console;verbosity=normal"
dotnet test apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ConfirmBookingPaymentIpnIntegrationTests|FullyQualifiedName~ExpiredPaymentRace" --logger "console;verbosity=normal"
dotnet test apps/parcel/tests/VietRide.Parcel.UnitTests/VietRide.Parcel.UnitTests.csproj -c Release --filter "FullyQualifiedName~PaymentEventHandlersTests|FullyQualifiedName~ParcelFinalPaymentTests" --logger "console;verbosity=normal"
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --include apps/payment/src/VietRide.Payment.Domain/Entities/Payment.cs apps/payment/src/VietRide.Payment.Application/Features/Payments/ConfirmBookingPayment apps/payment/src/VietRide.Payment.Infrastructure/Persistence/Repositories apps/payment/tests
```

## R7 - Add the internal redirect-session lookup

**Implement/review:** `dotnet-worker` -> `reviewer`
**Verification tier:** PROJECT
**Dependencies:** R6
**Skill:** `add-endpoint`

**Owned files**

- Internal Payment controller; lookup request/query/validator/result/handler; repository
  projection; trusted VNPay URL validation; focused endpoint and repository tests.

**Acceptance**

- Implement the exact internal-only contract without a Gateway route.
- Require internal JWT but no `Idempotency-Key`; apply `[SkipIdempotency]` and `no-store`.
- Validate owner and 1-100 unique allowed references.
- Select latest first, then enforce all eligibility and strict authority rules.
- Return `amount`, use one `AsNoTracking` DB query, preserve request order, and omit ineligible
  references.

**Targeted verification**

```powershell
dotnet test apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj -c Release --filter "FullyQualifiedName~LookupRedirectSessions" --logger "console;verbosity=normal"
dotnet test apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~InternalPaymentRedirectSessionLookup" --logger "console;verbosity=normal"
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes --include apps/payment/src/VietRide.Payment.Api/Controllers/InternalPaymentsController.cs apps/payment/src/VietRide.Payment.Application/Features/Internal apps/payment/src/VietRide.Payment.Application/Abstractions apps/payment/src/VietRide.Payment.Infrastructure apps/payment/tests
```

## R8 - Enrich Booking History

**Implement/review:** `dotnet-worker` -> `dotnet-reviewer`
**Verification tier:** FOCUSED
**Dependencies:** R7
**Skill:** none

**Owned files**

- Booking History query/handler/DTO, authoritative group-total read, Payment client
  interface/implementations, dedicated lookup HTTP helper, and focused tests.

**Acceptance**

- Use `IQuery<T>` and enrich only `PENDING_PAYMENT`.
- Use `BOOKING/bookingId` for one-way and `BOOKING_GROUP/groupId` for round trips.
- Batch-load authoritative group totals without N+1 and require exact response amount.
- Deduplicate references and call Payment at most once per nonempty page.
- Transport, non-200, or malformed responses fail open to null; cancellation propagates.
- Public and internal Booking History share the enriched DTO.

**Targeted verification**

```powershell
dotnet test apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj -c Release --filter "FullyQualifiedName~GetBookingHistoryQueryHandlerTests|FullyQualifiedName~PaymentServiceClientTests" --logger "console;verbosity=normal"
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes --include apps/booking/src/VietRide.Booking.Application/Features/Bookings/History apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients apps/booking/src/VietRide.Booking.Infrastructure/Http apps/booking/tests
```

## R9 - Enrich Passenger History

**Implement/review:** `dotnet-worker` -> `dotnet-reviewer`
**Verification tier:** FOCUSED
**Dependencies:** R8
**Skill:** none

**Owned files**

- Passenger History query/handler/DTO, Booking client DTO, Parcel Payment client
  interface/implementations, dedicated lookup HTTP helper, private enrichment projection, and
  focused tests.

**Acceptance**

- Use `IQuery<T>`. Ticket history forwards Booking URL without a second Payment call.
- Deposit candidates use `PENDING_PAYMENT`, `PARCEL`, exact `DepositPaymentId`, exact remaining
  deposit, and `LatestCheckInAt`.
- Final candidates use `PENDING_FINAL_PAYMENT`, `PARCEL_ADDITIONAL`, exact
  `BalancePaymentId`, exact remaining balance, and `FinalPaymentDeadline`.
- Exclude `PENDING_ADDITIONAL_PAYMENT`.
- Deduplicate into one Payment call per page and fail open to null.
- `/v1/parcels/sent` does not expose payment IDs, deadlines, or settlement internals.

**Targeted verification**

```powershell
dotnet test apps/parcel/tests/VietRide.Parcel.UnitTests/VietRide.Parcel.UnitTests.csproj -c Release --filter "FullyQualifiedName~GetPassengerHistoryQueryHandlerTests|FullyQualifiedName~PaymentServiceClientInternalClientTests|FullyQualifiedName~BookingServiceClientInternalClientTests" --logger "console;verbosity=normal"
dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes --include apps/parcel/src/VietRide.Parcel.Application/Features/PassengerHistory apps/parcel/src/VietRide.Parcel.Application/Features/History apps/parcel/src/VietRide.Parcel.Application/Abstractions/ServiceClients apps/parcel/src/VietRide.Parcel.Infrastructure/Http apps/parcel/tests
```

## R10 - Freeze Day 43 inventory and audit closure

**Implement/review:** `worker` -> `reviewer`, then independent `audit-day` reviews
**Verification tier:** FULL AUDIT
**Dependencies:** R1-R9
**Skill:** `audit-day 36`, `audit-day 43`

**Owned files**

- `tests/dotnet/idempotency-endpoint-inventory.json`
- `docs/handoff/day-43-plan.md`
- `docs/handoff/day-36-checklist.md`
- `docs/handoff/day-43-checklist.md`
- `docs/handoff/day-36-43-fe-gap-repair-checklist.md`

**Expected final inventory**

| Service | Total | Required | Exempt |
| --- | ---: | ---: | ---: |
| Identity | 35 | 30 | 5 |
| Trip | 54 | 53 | 1 |
| Booking | 27 | 26 | 1 |
| Payment | 15 | 11 | 4 |
| Parcel | 30 | 29 | 1 |
| Notification | 3 | 3 | 0 |
| RAG | 7 | 6 | 1 |
| Total | 171 | 158 | 13 |

Cross-system baselines: 43 .NET RabbitMQ handlers, 14 Notification subscriptions,
22 outbound mutation-style HTTP callsites, and four outbound exemption files including the
two dedicated read-only lookup helpers.

**Verification**

```powershell
npm run verify:idempotency-inventory
npm run e2e:day36
npm run e2e:day43
npm run e2e:parcel-settlement
dotnet build apps/identity/VietRide.Identity.sln -c Release
dotnet format apps/identity/VietRide.Identity.sln --verify-no-changes
dotnet test apps/identity/VietRide.Identity.sln -c Release
dotnet build apps/trip/VietRide.Trip.sln -c Release
dotnet format apps/trip/VietRide.Trip.sln --verify-no-changes
dotnet test apps/trip/VietRide.Trip.sln -c Release
dotnet build apps/booking/VietRide.Booking.sln -c Release
dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes
dotnet test apps/booking/VietRide.Booking.sln -c Release
dotnet build apps/payment/VietRide.Payment.sln -c Release
dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes
dotnet test apps/payment/VietRide.Payment.sln -c Release
dotnet build apps/parcel/VietRide.Parcel.sln -c Release
dotnet format apps/parcel/VietRide.Parcel.sln --verify-no-changes
dotnet test apps/parcel/VietRide.Parcel.sln -c Release
dotnet build libs/dotnet/VietRide.Libs.sln -c Release
dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes
dotnet test libs/dotnet/VietRide.Libs.sln -c Release
npx nx run-many -t build --all --exclude="VietRide.*"
npx nx run-many -t lint --all --exclude="VietRide.*"
npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests
```

The independent audits additionally rebuild the Docker app stack, execute the complete health
matrix, run the applicable real-app business E2Es, verify hard invariants, and write the two
numbered checklists. The combined checklist records Google/avatar, deadline/race/refund, lookup,
and history evidence.

## Final acceptance

- Day 36 produces five confirmed Bookings, 15 Tickets, and 15 unique Shuttle manifests with
  complete Inbox markers and no confirmation message in the DLQ.
- Day 43 verifier and reliability E2E pass against the final code inventory.
- Google login returns the stored avatar without overwriting user data.
- Booking, Payment, VNPay, and Parcel use one authoritative deadline.
- No captured payment is abandoned after an expiry race; expired Bookings are not resurrected.
- History returns only the latest, owned, exact-amount, unexpired URL from the trusted VNPay
  authority, while Payment unavailability never breaks base history.
- No dependency, migration, index, column, cross-database foreign key, or Gateway route is added.
