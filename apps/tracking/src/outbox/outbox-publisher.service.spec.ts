import { RabbitMqPublisher } from '@vietride/nest-rabbitmq';
import {
  APPROACHING_ALERT_EVENT_TYPE,
  OFF_ROUTE_ALERT_EVENT_TYPE,
  TRACKING_APPROACHING_ROUTING_KEY,
  TRACKING_OFF_ROUTE_ROUTING_KEY,
  TRACKING_TRIP_DELAYED_ROUTING_KEY,
  TRIP_DELAYED_EVENT_TYPE,
} from './outbox.constants';
import { OutboxPublisherService } from './outbox-publisher.service';
import type { OutboxEventRecord } from './outbox.repository';
import { OutboxRepository } from './outbox.repository';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';

describe('OutboxPublisherService', () => {
  let repository: jest.Mocked<OutboxRepository>;
  let publisher: jest.Mocked<RabbitMqPublisher>;
  let service: OutboxPublisherService;

  beforeEach(() => {
    repository = {
      findPublishable: jest.fn(),
      markPublishing: jest.fn(async () => true),
      markPublished: jest.fn(async () => undefined),
      markFailed: jest.fn(async () => undefined),
    } as unknown as jest.Mocked<OutboxRepository>;
    publisher = {
      publish: jest.fn(async () => undefined),
    } as unknown as jest.Mocked<RabbitMqPublisher>;
    service = new OutboxPublisherService(repository, publisher);
  });

  it('publishes pending events and marks them published', async () => {
    repository.findPublishable.mockResolvedValueOnce([
      createEvent({
        eventType: TRIP_DELAYED_EVENT_TYPE,
        payload: {
          tripId: EVENT_ID,
          stopId: '22222222-2222-4222-8222-222222222222',
        },
      }),
    ]);

    await expect(service.publishPendingOnce(25)).resolves.toBe(1);

    expect(repository.findPublishable).toHaveBeenCalledWith(25);
    expect(repository.markPublishing).toHaveBeenCalledWith(EVENT_ID);
    expect(publisher.publish).toHaveBeenCalledWith(
      TRACKING_TRIP_DELAYED_ROUTING_KEY,
      {
        tripId: EVENT_ID,
        stopId: '22222222-2222-4222-8222-222222222222',
      },
      {
        eventId: EVENT_ID,
        eventType: TRIP_DELAYED_EVENT_TYPE,
      },
    );
    expect(repository.markPublished).toHaveBeenCalledWith(EVENT_ID, expect.any(Date));
    expect(repository.markFailed).not.toHaveBeenCalled();
  });

  it('marks failed and increments retry count when publish fails', async () => {
    const error = new Error('BROKER_DOWN');
    repository.findPublishable.mockResolvedValueOnce([
      createEvent({
        eventType: OFF_ROUTE_ALERT_EVENT_TYPE,
        payload: {
          tripId: EVENT_ID,
          latitude: 10.762622,
          longitude: 106.660172,
        },
      }),
    ]);
    publisher.publish.mockRejectedValueOnce(error);

    await expect(service.publishPendingOnce(25)).resolves.toBe(0);

    expect(publisher.publish).toHaveBeenCalledWith(
      TRACKING_OFF_ROUTE_ROUTING_KEY,
      expect.objectContaining({ tripId: EVENT_ID }),
      expect.objectContaining({ eventType: OFF_ROUTE_ALERT_EVENT_TYPE }),
    );
    expect(repository.markFailed).toHaveBeenCalledWith(EVENT_ID, error);
    expect(repository.markPublished).not.toHaveBeenCalled();
  });

  it('does not crash poller for malformed payloads', async () => {
    repository.findPublishable.mockResolvedValueOnce([
      createEvent({
        eventType: APPROACHING_ALERT_EVENT_TYPE,
        payload: null,
      }),
    ]);

    await expect(service.publishPendingOnce(25)).resolves.toBe(0);

    expect(publisher.publish).not.toHaveBeenCalled();
    expect(repository.markFailed).toHaveBeenCalledWith(EVENT_ID, expect.any(Error));
  });

  it('routes approaching alerts to the vehicle approaching key', async () => {
    repository.findPublishable.mockResolvedValueOnce([
      createEvent({
        eventType: APPROACHING_ALERT_EVENT_TYPE,
        payload: {
          tripId: EVENT_ID,
          bookingId: '33333333-3333-4333-8333-333333333333',
          stopId: '22222222-2222-4222-8222-222222222222',
          etaMinutes: 10,
          wave: 'w2',
        },
      }),
    ]);

    await expect(service.publishPendingOnce(25)).resolves.toBe(1);

    expect(publisher.publish).toHaveBeenCalledWith(
      TRACKING_APPROACHING_ROUTING_KEY,
      expect.objectContaining({ wave: 'w2' }),
      expect.objectContaining({ eventType: APPROACHING_ALERT_EVENT_TYPE }),
    );
  });
});

function createEvent(overrides: Partial<OutboxEventRecord>): OutboxEventRecord {
  return {
    id: EVENT_ID,
    eventType: TRIP_DELAYED_EVENT_TYPE,
    payload: {},
    status: 'PENDING',
    retryCount: 0,
    lastError: null,
    createdAt: new Date('2026-06-04T00:00:00.000Z'),
    publishedAt: null,
    ...overrides,
  };
}
