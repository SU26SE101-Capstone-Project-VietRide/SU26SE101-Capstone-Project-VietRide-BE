import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  APPROACHING_ALERT_DEDUPE_TTL_SECONDS,
  APPROACHING_ALERT_EVENT_TYPE,
  APPROACHING_ALERT_WAVE_1,
  APPROACHING_ALERT_WAVE_2,
  BOOKING_DATA_PROVIDER,
  trackingApproachingNotifiedKey,
} from './approaching-alert.constants';
import { ApproachingAlertService } from './approaching-alert.service';
import type { BookingDataProvider, PickupBookingSnapshot } from './booking-data.provider';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';
const TEST_BOOKING_ID = '33333333-3333-4333-8333-333333333333';

describe('ApproachingAlertService', () => {
  let service: ApproachingAlertService;
  let redisSet: jest.MockedFunction<(
    key: string,
    value: string,
    mode: string,
    ttl: number,
    condition: string,
  ) => Promise<string | null>>;
  let outboxCreate: jest.MockedFunction<(args: unknown) => Promise<unknown>>;
  let bookingDataProvider: jest.Mocked<BookingDataProvider>;

  beforeEach(async () => {
    redisSet = jest.fn(async (
      key: string,
      value: string,
      mode: string,
      ttl: number,
      condition: string,
    ) => {
      void key;
      void value;
      void mode;
      void ttl;
      void condition;
      return 'OK';
    });
    outboxCreate = jest.fn(async (args: unknown) => args);
    bookingDataProvider = {
      getPickupBookings: jest.fn(async (tripId: string, stopId: string) => {
        void tripId;
        void stopId;
        return [createBooking()];
      }),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        ApproachingAlertService,
        {
          provide: RedisService,
          useValue: {
            getClient: jest.fn(() => ({
              set: redisSet,
            })),
          },
        },
        {
          provide: TrackingPrismaService,
          useValue: {
            outboxEvent: {
              create: outboxCreate,
            },
          },
        },
        {
          provide: BOOKING_DATA_PROVIDER,
          useValue: bookingDataProvider,
        },
      ],
    }).compile();

    service = moduleRef.get(ApproachingAlertService);
  });

  it('creates wave 1 outbox event exactly once', async () => {
    redisSet.mockResolvedValueOnce('OK').mockResolvedValueOnce(null);

    await expect(service.handleEtaUpdate(createEta(25))).resolves.toBe(1);
    await expect(service.handleEtaUpdate(createEta(25))).resolves.toBe(0);

    expect(redisSet).toHaveBeenCalledWith(
      trackingApproachingNotifiedKey(TEST_TRIP_ID, TEST_BOOKING_ID, APPROACHING_ALERT_WAVE_1),
      '1',
      'EX',
      APPROACHING_ALERT_DEDUPE_TTL_SECONDS,
      'NX',
    );
    expect(outboxCreate).toHaveBeenCalledTimes(1);
    expect(outboxCreate).toHaveBeenCalledWith({
      data: {
        eventType: APPROACHING_ALERT_EVENT_TYPE,
        payload: {
          tripId: TEST_TRIP_ID,
          bookingId: TEST_BOOKING_ID,
          stopId: TEST_STOP_ID,
          etaMinutes: 25,
          wave: APPROACHING_ALERT_WAVE_1,
        },
      },
    });
  });

  it('creates wave 2 outbox event exactly once', async () => {
    redisSet
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce('OK')
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce(null);

    await expect(service.handleEtaUpdate(createEta(8))).resolves.toBe(1);
    await expect(service.handleEtaUpdate(createEta(8))).resolves.toBe(0);

    expect(outboxCreate).toHaveBeenCalledTimes(1);
    expect(outboxCreate).toHaveBeenCalledWith({
      data: {
        eventType: APPROACHING_ALERT_EVENT_TYPE,
        payload: {
          tripId: TEST_TRIP_ID,
          bookingId: TEST_BOOKING_ID,
          stopId: TEST_STOP_ID,
          etaMinutes: 8,
          wave: APPROACHING_ALERT_WAVE_2,
        },
      },
    });
  });

  it('does not create alerts for cancelled or no-show bookings', async () => {
    bookingDataProvider.getPickupBookings.mockResolvedValue([
      createBooking({ bookingId: '44444444-4444-4444-8444-444444444444', status: 'CANCELLED' }),
      createBooking({ bookingId: '55555555-5555-4555-8555-555555555555', status: 'NO_SHOW' }),
    ]);

    await expect(service.handleEtaUpdate(createEta(10))).resolves.toBe(0);

    expect(redisSet).not.toHaveBeenCalled();
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('does not process terminal pickup states in Tracking', async () => {
    bookingDataProvider.getPickupBookings.mockResolvedValue([
      createBooking({ bookingId: '44444444-4444-4444-8444-444444444444', pickupStatus: 'PICKED_UP' }),
      createBooking({ bookingId: '55555555-5555-4555-8555-555555555555', pickupStatus: 'MISSED' }),
    ]);

    await expect(service.handleEtaUpdate(createEta(10))).resolves.toBe(0);

    expect(redisSet).not.toHaveBeenCalled();
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  function createEta(etaMinutes: number) {
    return {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes,
    };
  }

  function createBooking(overrides: Partial<PickupBookingSnapshot> = {}): PickupBookingSnapshot {
    return {
      bookingId: TEST_BOOKING_ID,
      stopId: TEST_STOP_ID,
      status: 'CONFIRMED',
      pickupStatus: 'PENDING',
      ...overrides,
    };
  }
});
