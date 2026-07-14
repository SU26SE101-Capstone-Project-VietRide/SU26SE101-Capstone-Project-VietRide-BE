import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { ShuttleEventsConsumer } from './shuttle-events.consumer';

describe('ShuttleEventsConsumer', () => {
  const message = { properties: { messageId: 'event-1' } } as ConsumeMessage;
  let notifications: jest.Mocked<NotificationsService>;
  let consumer: ShuttleEventsConsumer;

  beforeEach(() => {
    notifications = {
      createNotification: jest.fn(async () => ({})),
    } as unknown as jest.Mocked<NotificationsService>;
    consumer = new ShuttleEventsConsumer(
      { subscribe: jest.fn() } as unknown as RabbitMqConsumer,
      {
        begin: jest.fn(async () => 'acquired'),
        markProcessed: jest.fn(async () => undefined),
        release: jest.fn(async () => undefined),
      } as unknown as MessageIdempotencyService,
      notifications,
      { resolveOperatorRecipientUserIds: jest.fn(async () => []) } as OperatorRecipientProvider,
    );
  });

  it('creates one assignment notification per booking event with driver snapshot', async () => {
    await consumer.handle(
      'trip.shuttle.assigned',
      {
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        bookingId: '36000000-0000-4000-8000-000000000003',
        passengerUserId: '36000000-0000-4000-8000-000000000004',
        ticketIds: ['36000000-0000-4000-8000-000000000007'],
        pickupOrder: 1,
        scheduledDepartureTime: '2026-07-13T01:00:00Z',
        scheduledEndTime: '2026-07-13T02:00:00Z',
        driver: {
          userId: '36000000-0000-4000-8000-000000000005',
          displayName: 'Driver Day 36',
          phone: '+84910000036',
        },
        vehicle: {
          id: '36000000-0000-4000-8000-000000000006',
          licensePlate: '51B-360.36',
        },
      },
      message,
    );

    expect(notifications.createNotification).toHaveBeenCalledTimes(1);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        type: NotificationType.SHUTTLE_ASSIGNED,
        dedupeKey: 'trip.shuttle.assigned:36000000-0000-4000-8000-000000000003',
      }),
    );
  });
});
