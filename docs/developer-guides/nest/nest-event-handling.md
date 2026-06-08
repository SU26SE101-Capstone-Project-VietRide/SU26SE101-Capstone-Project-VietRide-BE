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

## Checklist

- [ ] Routing key follows `<svc>.<aggregate>.<verb_past>` pattern
- [ ] Publish only after successful DB write
- [ ] Consumer checks idempotency before processing via `RedisService` (`NX`)
- [ ] Payload validated with Zod — malformed payloads are dropped not requeued
- [ ] Consumer registered as provider in module (so `onModuleInit` fires)
- [ ] Lint and build succeed
