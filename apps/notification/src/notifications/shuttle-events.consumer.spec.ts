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

  it('creates passenger and driver assignment notifications per booking event', async () => {
    await consumer.handle(
      'trip.shuttle.assigned',
      {
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        bookingId: '36000000-0000-4000-8000-000000000003',
        passengerUserId: '36000000-0000-4000-8000-000000000004',
        direction: 'INBOUND_TO_STATION',
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

    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000004',
        type: NotificationType.SHUTTLE_ASSIGNED,
        dedupeKey:
          'trip.shuttle.assigned:36000000-0000-4000-8000-000000000003:passenger:36000000-0000-4000-8000-000000000004',
      }),
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000005',
        type: NotificationType.SHUTTLE_ASSIGNED,
        data: expect.objectContaining({
          shuttleTripId: '36000000-0000-4000-8000-000000000001',
          mainTripId: '36000000-0000-4000-8000-000000000002',
        }),
        dedupeKey:
          'trip.shuttle.assigned:36000000-0000-4000-8000-000000000003:driver:36000000-0000-4000-8000-000000000005',
      }),
    );
  });

  it.each([
    ['trip.shuttle.cancelled', NotificationType.SHUTTLE_CANCELLED, 'CANCELLED'],
    ['trip.shuttle.picked_up', NotificationType.SHUTTLE_PICKED_UP, 'PICKED_UP'],
    ['trip.shuttle.delivered', NotificationType.SHUTTLE_DELIVERED, 'DELIVERED'],
    ['trip.shuttle.no_show', NotificationType.SHUTTLE_NO_SHOW, 'NO_SHOW'],
    ['trip.shuttle.completed', NotificationType.SHUTTLE_COMPLETED, 'COMPLETED'],
  ] as const)(
    'maps %s with canonical event identity and tenant recipients',
    async (routingKey, type, status) => {
      const eventId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
      const operatorUserId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
      const recipients = consumer as unknown as {
        recipients: OperatorRecipientProvider;
      };
      (recipients.recipients.resolveOperatorRecipientUserIds as jest.Mock).mockResolvedValue([
        operatorUserId,
      ]);

      await consumer.handle(
        routingKey,
        {
          eventId,
          occurredAt: '2026-08-04T01:00:00Z',
          shuttleTripId: '36000000-0000-4000-8000-000000000001',
          mainTripId: '36000000-0000-4000-8000-000000000002',
          operatorId: '36000000-0000-4000-8000-000000000008',
          bookingId: '36000000-0000-4000-8000-000000000003',
          passengerUserId: '36000000-0000-4000-8000-000000000004',
          direction: 'OUTBOUND_FROM_STATION',
          serviceAddress: '123 Service Road',
          serviceOrder: 2,
          status,
          roadDistanceMeters: null,
        },
        { properties: { messageId: 'transport-message-id' } } as ConsumeMessage,
      );

      expect(
        (consumer as unknown as { idempotency: MessageIdempotencyService }).idempotency.begin,
      ).toHaveBeenCalledWith(routingKey, eventId, undefined);
      expect(notifications.createNotification).toHaveBeenCalledWith(
        expect.objectContaining({
          userId: '36000000-0000-4000-8000-000000000004',
          type,
          data: expect.objectContaining({
            direction: 'OUTBOUND_FROM_STATION',
            status,
            pickupOrder: 2,
          }),
        }),
      );
      expect(notifications.createNotification).toHaveBeenCalledWith(
        expect.objectContaining({ userId: operatorUserId, type }),
      );
    },
  );

  it('deduplicates assignment replay by eventId even when MessageId changes', async () => {
    const eventId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const payload = {
      eventId,
      shuttleTripId: '36000000-0000-4000-8000-000000000001',
      mainTripId: '36000000-0000-4000-8000-000000000002',
      bookingId: '36000000-0000-4000-8000-000000000003',
      passengerUserId: '36000000-0000-4000-8000-000000000004',
      direction: 'INBOUND_TO_STATION',
      ticketIds: ['36000000-0000-4000-8000-000000000007'],
      pickupOrder: 1,
      scheduledDepartureTime: '2026-07-13T01:00:00Z',
      scheduledEndTime: '2026-07-13T02:00:00Z',
      driver: {
        userId: '36000000-0000-4000-8000-000000000005',
        displayName: 'Driver',
        phone: '+84910000036',
      },
      vehicle: { id: '36000000-0000-4000-8000-000000000006', licensePlate: '51B-360.36' },
    };
    const idempotency = (consumer as unknown as { idempotency: MessageIdempotencyService })
      .idempotency;
    (idempotency.begin as jest.Mock)
      .mockResolvedValueOnce('acquired')
      .mockResolvedValueOnce('duplicate');

    await consumer.handle('trip.shuttle.assigned', payload, {
      properties: { messageId: 'transport-1' },
    } as ConsumeMessage);
    await consumer.handle('trip.shuttle.assigned', payload, {
      properties: { messageId: 'transport-2' },
    } as ConsumeMessage);

    expect(idempotency.begin).toHaveBeenNthCalledWith(
      1,
      'trip.shuttle.assigned',
      eventId,
      undefined,
    );
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      2,
      'trip.shuttle.assigned',
      eventId,
      undefined,
    );
    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
  });

  it('notifies passengers and Shuttle dispatch recipients when the trip starts', async () => {
    const recipientProvider = (consumer as unknown as { recipients: OperatorRecipientProvider })
      .recipients;
    (recipientProvider.resolveOperatorRecipientUserIds as jest.Mock).mockResolvedValue([
      'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    ]);

    await consumer.handle(
      'trip.shuttle.started',
      {
        eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        occurredAt: '2026-08-22T01:00:00Z',
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        operatorId: '36000000-0000-4000-8000-000000000008',
        driverUserId: '36000000-0000-4000-8000-000000000005',
        direction: 'INBOUND_TO_STATION',
        passengers: [
          {
            passengerUserId: '36000000-0000-4000-8000-000000000004',
            bookingId: '36000000-0000-4000-8000-000000000003',
            pickupOrder: 2,
          },
        ],
      },
      message,
    );

    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000004',
        type: NotificationType.SHUTTLE_STARTED,
        data: expect.objectContaining({
          bookingId: '36000000-0000-4000-8000-000000000003',
          pickupOrder: 2,
        }),
      }),
    );
  });

  it('notifies passengers, old and new drivers, and dispatch recipients after reassignment', async () => {
    const recipientProvider = (consumer as unknown as { recipients: OperatorRecipientProvider })
      .recipients;
    (recipientProvider.resolveOperatorRecipientUserIds as jest.Mock).mockResolvedValue([
      'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    ]);

    await consumer.handle(
      'trip.shuttle.reassigned',
      {
        eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        occurredAt: '2026-08-22T01:00:00Z',
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        operatorId: '36000000-0000-4000-8000-000000000008',
        direction: 'OUTBOUND_FROM_STATION',
        oldDriverUserId: '36000000-0000-4000-8000-000000000009',
        newDriver: {
          userId: '36000000-0000-4000-8000-000000000005',
          displayName: 'Tài xế mới',
          phone: '0900000000',
        },
        oldVehicle: {
          id: '36000000-0000-4000-8000-000000000006',
          licensePlate: '51B-000.01',
        },
        newVehicle: {
          id: '36000000-0000-4000-8000-000000000007',
          licensePlate: '51B-000.02',
        },
        reason: 'Xe cũ cần bảo trì',
        passengers: [
          {
            passengerUserId: '36000000-0000-4000-8000-000000000004',
            bookingId: '36000000-0000-4000-8000-000000000003',
            pickupOrder: 1,
          },
        ],
      },
      message,
    );

    expect(notifications.createNotification).toHaveBeenCalledTimes(4);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000009',
        type: NotificationType.SHUTTLE_REASSIGNED,
      }),
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000005',
        type: NotificationType.SHUTTLE_REASSIGNED,
      }),
    );
  });

  it('notifies the assigned driver once when an entire Shuttle trip is cancelled', async () => {
    await consumer.handle(
      'trip.shuttle.cancelled',
      {
        eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        occurredAt: '2026-08-22T01:00:00Z',
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        operatorId: '36000000-0000-4000-8000-000000000008',
        bookingId: '36000000-0000-4000-8000-000000000003',
        passengerUserId: '36000000-0000-4000-8000-000000000004',
        direction: 'INBOUND_TO_STATION',
        serviceOrder: 1,
        status: 'CANCELLED',
        reason: 'Xe không khả dụng',
        driverUserId: '36000000-0000-4000-8000-000000000005',
        cancellationScope: 'SHUTTLE_TRIP',
      },
      message,
    );

    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: '36000000-0000-4000-8000-000000000005',
        type: NotificationType.SHUTTLE_CANCELLED,
        dedupeKey:
          'trip.shuttle.cancelled:36000000-0000-4000-8000-000000000001:driver:36000000-0000-4000-8000-000000000005',
      }),
    );
  });
});
