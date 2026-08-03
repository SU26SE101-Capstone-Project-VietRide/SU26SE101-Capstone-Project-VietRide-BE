# RabbitMQ Event Handling — VietRide

> How to publish and consume events on the `vietride.events` exchange.
> Covers routing key convention, idempotency, and error handling.

## Prerequisites

Read `nest-conventions.md` first.

---

## Routing key convention

```
<svc>.<aggregate>.<verb_past>

Examples:
  tracking.location.updated
  identity.user.created
  booking.booking.confirmed
```

`<svc>` = app name (`tracking`, `identity`, `booking`, `payment`, `trip`, `parcel`, `notification`)
`<aggregate>` = snake_case aggregate name
`<verb_past>` = past-tense verb in snake_case

---

## Publishing an event

```typescript
// in your service
import { RabbitMqPublisher } from '@vietride/nest-rabbitmq';

@Injectable()
export class LocationService {
  constructor(
    private readonly locationRepository: LocationRepository,
    private readonly publisher: RabbitMqPublisher,
  ) {}

  async updateLocation(dto: UpdateLocationDto): Promise<void> {
    // 1. Persist first
    await this.locationRepository.upsert(dto);

    // 2. Publish after successful persist
    await this.publisher.publish('vietride.events', 'tracking.location.updated', {
      driverId: dto.driverId,
      lat:      dto.lat,
      lng:      dto.lng,
      timestamp: new Date().toISOString(),
    });
  }
}
```

**Always persist before publishing.** If publish fails, the data is still saved.
Never publish before the write succeeds.

---

## Consuming an event

```typescript
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import { Logger } from '@nestjs/common';
import type { ConsumeMessage } from 'amqplib';

@Injectable()
export class BookingConfirmedConsumer implements OnModuleInit {
  private readonly logger = new Logger('BookingConfirmedConsumer');

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly notificationService: NotificationService,
  ) {}

  onModuleInit(): void {
    // Correct signature: queue, routingKey, handler
    this.consumer.subscribe(
      'notification:booking-confirmed',  // queue name
      'booking.booking.confirmed',       // routing key
      this.handle.bind(this),
    );
  }

  private async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;

    // 1. Idempotency check — skip if already processed using raw RedisService
    const key = `idem:booking.confirmed:${messageId}`;
    const isNew = await this.redis.getClient().set(key, '1', 'EX', 86400, 'NX');
    if (!isNew) {
      this.logger.log(`Skipping duplicate message: ${messageId}`);
      return;
    }

    // 2. Parse and validate payload
    const result = BookingConfirmedPayloadSchema.safeParse(payload);
    if (!result.success) {
      this.logger.error(`Invalid payload shape — dropping message: ${messageId}`);
      // Do NOT requeue malformed messages — they will loop forever
      return;
    }

    // 3. Process
    await this.notificationService.sendBookingConfirmation(result.data);

    this.logger.log(`Processed messageId: ${messageId}, bookingId: ${result.data.bookingId}`);
  }
}
```

---

## Idempotency key pattern

TTL: 86 400 seconds (24 hours) — long enough to catch retries, short enough to not bloat Redis.

---

## Register consumer in module

```typescript
// notification.module.ts
@Module({
  providers: [
    NotificationService,
    BookingConfirmedConsumer,   // ← register as provider so onModuleInit fires
  ],
})
export class NotificationModule {}
```

---

## Renaming a routing key or a queue

**Durable queue arguments are immutable.** A queue keeps the arguments it was declared
with, forever. RabbitMQ answers a redeclare with different arguments with
`406 PRECONDITION_FAILED — inequivalent arg`, and the queue stays as it was.

That matters because `subscribe(..., { deadLetter: true })` bakes the routing key into
the queue's `x-dead-letter-routing-key`. So renaming a routing key silently invalidates
every existing queue that carries it:

```
queue notification:trip-vehicle-substituted
  declared with  x-dead-letter-routing-key = trip.vehicle_substituted
  code now wants x-dead-letter-routing-key = trip.trip.vehicle_substituted
  → 406, consumer never starts, events pile up unconsumed
```

**Rule: a routing-key or queue-name change is a topology migration, not a code change.**
When your PR touches either, the deploy is only complete once the stale queue is deleted
on every environment. Put it in the PR description.

- **Renaming a routing key** — the existing queue must be deleted so it can be
  redeclared with the new argument. Delete only the main queue; `.retry` keys off the
  queue name (`__retry__.<queue>`) and `.dlq` has no arguments, so both survive a
  routing-key rename untouched.
- **Renaming a queue** — no 406 (the new name declares cleanly), but the old queue keeps
  its binding and accumulates messages nobody consumes. Delete the old queue *and* its
  `.retry` / `.dlq`.
- **Never rename in two commits.** One conflicting queue kills one consumer; the others
  fail independently and you will find them one deploy at a time.

Recovery procedure and the broker commands to enumerate conflicts:
`docs/runbooks/rabbitmq-topology-conflict.md`.

### How a conflict surfaces

`RabbitMqConsumer` catches the rejection, logs it, and leaves the process running — one
bad queue must not take down every other consumer in the service. The failure is recorded
in `RabbitMqTopologyHealth`, and each service's readiness check reads that registry, so
`GET /ready` returns 503 naming the affected queues:

```json
{
  "errorCode": "NOTIFICATION_DEPENDENCY_UNAVAILABLE",
  "detail": "1 RabbitMQ consumer(s) failed topology assertion",
  "failedConsumers": [
    { "queue": "notification:trip-vehicle-substituted", "routingKey": "trip.trip.vehicle_substituted" }
  ]
}
```

`/health` is liveness only and stays 200, so the container will *not* restart on its own
and `docker ps` will keep reporting `(healthy)`. **Check `/ready` after any deploy that
touches routing keys or queue names** — that is the signal, not the container status.

---

## Checklist

- [ ] Routing key follows `<svc>.<aggregate>.<verb_past>` pattern
- [ ] Publish only after successful DB write
- [ ] Consumer checks idempotency before processing via `RedisService` (`NX`)
- [ ] Payload validated with Zod — malformed payloads are dropped not requeued
- [ ] Consumer registered as provider in module (so `onModuleInit` fires)
- [ ] Renamed a routing key or queue? Deletion of the stale queue is listed in the PR
      description, and `/ready` is checked after deploy
- [ ] Lint and build succeed
