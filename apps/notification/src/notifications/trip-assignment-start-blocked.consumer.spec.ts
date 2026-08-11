import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { ShuttleEventsConsumer } from './shuttle-events.consumer';

describe('Trip assignment start-blocked notification', () => {
  it('notifies each operator admin once', async () => {
    const notifications = {
      createNotification: jest.fn(async () => ({})),
    } as unknown as jest.Mocked<NotificationsService>;
    const recipients = {
      resolveOperatorRecipientUserIds: jest.fn(async () => [
        '20000000-0000-4000-8000-000000000001',
        '20000000-0000-4000-8000-000000000001',
      ]),
    } as OperatorRecipientProvider;
    const consumer = new ShuttleEventsConsumer(
      { subscribe: jest.fn() } as unknown as RabbitMqConsumer,
      {
        begin: jest.fn(async () => 'acquired'),
        markProcessed: jest.fn(async () => undefined),
        release: jest.fn(async () => undefined),
      } as unknown as MessageIdempotencyService,
      notifications,
      recipients,
    );

    await consumer.handle(
      'trip.assignment.start_blocked',
      {
        eventId: '20000000-0000-4000-8000-000000000002',
        occurredAt: '2026-08-11T01:00:00Z',
        tripId: '20000000-0000-4000-8000-000000000003',
        operatorId: '20000000-0000-4000-8000-000000000004',
        resourceRole: 'VEHICLE',
        resourceId: '20000000-0000-4000-8000-000000000005',
        conflictingSourceType: 'TRIP',
        conflictingSourceId: '20000000-0000-4000-8000-000000000006',
        conflictReason: 'RESOURCE_ACTIVE',
        blockingUntil: null,
      },
      { properties: { messageId: 'transport-id' } } as ConsumeMessage,
    );

    expect(notifications.createNotification).toHaveBeenCalledTimes(1);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '20000000-0000-4000-8000-000000000001',
        type: NotificationType.TRIP_ASSIGNMENT_START_BLOCKED,
        dedupeKey:
          'trip.assignment.start_blocked:20000000-0000-4000-8000-000000000003:20000000-0000-4000-8000-000000000001',
      }),
    );
  });
});
