---
name: add-integration-event
description: Add an Outbox-published integration event in a VietRide .NET service. Define the IIntegrationEvent payload and service-aggregate-past-verb routing key, enqueue an OutboxMessage in the same transaction, and record it in the BSOT event registry. Use when an endpoint or handler must emit a cross-service event.
---

# Add an integration event (Outbox + RabbitMQ)

Events are published transactionally via the **Outbox** pattern: the handler writes the
business change **and** an `OutboxMessage` row in the SAME `SaveChanges` transaction;
`OutboxBackgroundService` (poll 5s, batch 50 — `OutboxOptions`) drains unprocessed rows and
relays each to the RabbitMQ topic exchange `vietride.events`.

## How the implemented libs actually work (do not deviate)
- **`IIntegrationEvent`** (`VietRide.Shared.Messaging/Abstractions`) — marker with `EventId`,
  `OccurredAt`, and **`EventType`**. The publisher uses `EventType` **as the AMQP routing key**.
  Extend `IntegrationEventBase` (auto-fills `EventId`/`OccurredAt`); override `EventType`.
- **`OutboxMessage`** (`VietRide.Shared.Persistence/Outbox`) — durable row: `Type` (the routing
  key), `Payload` (UTF-8 JSON), `OccurredAt`. INSERTed in the same DbContext transaction.
- **`IOutboxStore.AddAsync(OutboxMessage)`** — the ONLY way Application/handler code enqueues an
  event. It adds to `DbContext.OutboxMessages`; your existing `SaveChangesAsync` commits both the
  business write and the outbox row atomically.
- **`IEventPublisher` is NOT called from Application/handler code.** Its own XML doc says so — it
  is for the outbox worker + integration tests only. Never `Publish(...)` from a handler.
- There is **no Outbox interceptor** and **no `RoutingKeys.cs`** in this repo — the routing key
  lives on each event's `EventType`. Don't reference either.

## Rules (BSOT §9 messaging)
- **Routing key** = `event.EventType`, shape `<svc>.<aggregate>.<verb_past>` — lowercase, past
  tense. Examples: `identity.user.created`, `identity.operator.approved`, `payment.topup.succeeded`.
- **Payload**: include only what consumers need + identifiers; snapshot values (don't force the
  consumer to call back synchronously). Serialize with System.Text.Json web defaults (camelCase JSON).
- **Idempotent consumers**: events may be delivered more than once. `EventId` becomes the RabbitMQ
  `MessageId` — consumers dedupe on it.
- **No new dependency** — `RabbitMQ.Client` is already pinned; reuse the shared messaging lib.

## Steps
1. Define the event class in the service's `…/Events/` (or shared messaging contracts) extending
   `IntegrationEventBase`, overriding `EventType` to return the `<svc>.<aggregate>.<verb_past>` string.
2. In the handler, after mutating state: serialize the event to JSON, build
   `new OutboxMessage { Type = evt.EventType, Payload = json, OccurredAt = clock.UtcNow }`, and call
   `IOutboxStore.AddAsync(message, ct)` — **before** the handler's `SaveChangesAsync`, so both land
   in one transaction. Do NOT call `IEventPublisher`.
3. **Register it in the BSOT event registry** (the event table) — name, routing key, producer
   service, payload fields, expected consumers.
4. **Cross-check the consumer schema** with the NestJS side (Notification/Tracking, owner: Tuyên) —
   the TS payload type in `libs/shared/contracts` must match field-for-field.

## Verify with the task policy

Use the invoking task's exact `verification commands` and tier. Do not append a full service
build/test matrix. Standalone use defaults to `FOCUSED` and defines exact filters first.

- Run the affected integration-test class/filter and confirm at least one test executes. Trigger
  the handler and assert an `OutboxMessages` row is written in the business transaction with the
  routing key and `ProcessedAt == null`.
- Run focused publisher filters/fixtures that prove the message reaches `vietride.events` with
  the expected routing key and `MessageId == EventId`, and that an unprocessed row survives a
  publisher restart and is eventually delivered. These event-specific checks remain mandatory;
  do not widen them into unrelated messaging suites.
- A targeted test compiles referenced production projects. Build only the smallest `.csproj`
  when no suitable test reference exists.
- Format only changed C# paths with
  `dotnet format <affected-sln> --verify-no-changes --include <changed paths>`; lint only changed
  contract files if a TS consumer contract changed.

Full touched-solution/workspace regression belongs only to `/audit-day <N>`.
