import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { EtaUpdateEvent } from '../eta/eta.service';
import type { TripDataProvider } from '../eta/trip-data.provider';
import { TRACKING_ACTIVE_TRIPS_KEY, trackingEtaKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  TRIP_DELAY_DEDUPE_TTL_SECONDS,
  TRIP_DELAYED_EVENT_TYPE,
  trackingTripDelayedDedupeKey,
} from './trip-delay.constants';
import { TripDelayService } from './trip-delay.service';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';
const STATIC_ETA = '2026-06-04T10:00:00.000Z';
const ON_TIME_DYNAMIC_ETA = '2026-06-04T10:30:00.000Z';
const DELAYED_DYNAMIC_ETA = '2026-06-04T10:31:00.000Z';

describe('TripDelayService', () => {
  let service: TripDelayService;
  let redisSmembers: jest.MockedFunction<(key: string) => Promise<string[]>>;
  let redisGet: jest.MockedFunction<(key: string) => Promise<string | null>>;
  let redisSet: jest.MockedFunction<(
    key: string,
    value: string,
    mode: string,
    ttl: number,
    condition: string,
  ) => Promise<string | null>>;
  let outboxCreate: jest.MockedFunction<(args: unknown) => Promise<unknown>>;
  let tripDataProvider: jest.Mocked<TripDataProvider>;

  beforeEach(async () => {
    redisSmembers = jest.fn(async (key: string) => {
      void key;
      return [TEST_TRIP_ID];
    });
    redisGet = jest.fn(async (key: string) => {
      void key;
      return null;
    });
    redisSet = jest.fn(async (key: string, value: string, mode: string, ttl: number, condition: string) => {
      void key;
      void value;
      void mode;
      void ttl;
      void condition;
      return 'OK';
    });
    outboxCreate = jest.fn(async (args: unknown) => args);
    tripDataProvider = {
      getRouteStops: jest.fn(async (tripId: string) => {
        void tripId;
        return [createStop()];
      }),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        TripDelayService,
        {
          provide: RedisService,
          useValue: {
            getClient: jest.fn(() => ({
              smembers: redisSmembers,
              get: redisGet,
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
          provide: TRIP_DATA_PROVIDER,
          useValue: tripDataProvider,
        },
      ],
    }).compile();

    service = moduleRef.get(TripDelayService);
  });

  it('does not publish when delay is equal to the 30 minute threshold', async () => {
    redisGet.mockResolvedValue(JSON.stringify(createCachedEta(ON_TIME_DYNAMIC_ETA)));

    await expect(service.detectDelayedTrips()).resolves.toBe(0);

    expect(redisSmembers).toHaveBeenCalledWith(TRACKING_ACTIVE_TRIPS_KEY);
    expect(redisGet).toHaveBeenCalledWith(trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID));
    expect(redisSet).not.toHaveBeenCalled();
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('publishes TripDelayed when dynamic ETA exceeds static ETA by more than 30 minutes', async () => {
    redisGet.mockResolvedValue(JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));

    await expect(service.detectDelayedTrips()).resolves.toBe(1);

    expect(redisSet).toHaveBeenCalledWith(
      trackingTripDelayedDedupeKey(TEST_TRIP_ID, TEST_STOP_ID, String(Math.floor(new Date(DELAYED_DYNAMIC_ETA).getTime() / 300_000))),
      '1',
      'EX',
      TRIP_DELAY_DEDUPE_TTL_SECONDS,
      'NX',
    );
    expect(outboxCreate).toHaveBeenCalledWith({
      data: {
        eventType: TRIP_DELAYED_EVENT_TYPE,
        payload: {
          tripId: TEST_TRIP_ID,
          stopId: TEST_STOP_ID,
          staticEstimatedArrivalTime: STATIC_ETA,
          dynamicEstimatedArrivalTime: DELAYED_DYNAMIC_ETA,
          delayMinutes: 31,
          detectedAt: expect.any(String),
        },
      },
    });
  });

  it('does not publish duplicate detection in the same trip stop window', async () => {
    redisGet.mockResolvedValue(JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));
    redisSet.mockResolvedValue(null);

    await expect(service.detectTripDelay(TEST_TRIP_ID)).resolves.toBe(0);

    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('returns delayed flag for realtime ETA updates', async () => {
    const eta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };

    await expect(service.handleEtaUpdate(eta)).resolves.toEqual({
      ...eta,
      delayed: true,
      delayMinutes: 31,
    });
  });

  function createStop() {
    return {
      stopId: TEST_STOP_ID,
      latitude: 10.762622,
      longitude: 106.660172,
      sequence: 1,
      estimatedArrivalTime: STATIC_ETA,
    };
  }

  function createCachedEta(estimatedArrivalTime: string) {
    return {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      estimatedArrivalTime,
    };
  }
});
