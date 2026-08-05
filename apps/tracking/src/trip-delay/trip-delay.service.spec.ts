import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { EtaUpdateEvent } from '../eta/eta.service';
import type { TripDataProvider, TripStopSnapshot } from '../eta/trip-data.provider';
import { TRACKING_ACTIVE_TRIPS_KEY, trackingEtaKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  TRIP_DELAY_DEDUPE_TTL_SECONDS,
  TRIP_DELAY_LOCK_TTL_SECONDS,
  TRIP_DELAYED_EVENT_TYPE,
  TRIP_DELAY_WINDOW_MS,
  trackingTripDelayedDedupeKey,
  trackingTripDelayLockKey,
  trackingTripDelayStateKey,
} from './trip-delay.constants';
import { TripDelayService } from './trip-delay.service';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';
const ALERT_RECIPIENT_USER_ID = '66666666-6666-4666-8666-666666666666';
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
  let redisEval: jest.MockedFunction<(...args: unknown[]) => Promise<number>>;
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
    redisEval = jest.fn(async () => 1);
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
              eval: redisEval,
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
    expect(redisSet).toHaveBeenCalledWith(
      trackingTripDelayStateKey(TEST_TRIP_ID, TEST_STOP_ID),
      expect.stringContaining('"delayStatus":"ON_TIME"'),
      'EX',
      86_400,
    );
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('publishes TripDelayed when dynamic ETA exceeds static ETA by more than 30 minutes', async () => {
    redisGet.mockResolvedValue(JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));

    await expect(service.detectDelayedTrips()).resolves.toBe(1);

    const windowId = String(Math.floor(Date.now() / TRIP_DELAY_WINDOW_MS));
    expect(redisSet).toHaveBeenCalledWith(
      trackingTripDelayedDedupeKey(TEST_TRIP_ID, TEST_STOP_ID, windowId),
      '1',
      'EX',
      TRIP_DELAY_DEDUPE_TTL_SECONDS,
    );
    expect(outboxCreate).toHaveBeenCalledWith({
      data: {
        eventType: TRIP_DELAYED_EVENT_TYPE,
        dedupeKey: `trip-delay:${TEST_TRIP_ID}:${TEST_STOP_ID}:${windowId}`,
        payload: {
          tripId: TEST_TRIP_ID,
          stopId: TEST_STOP_ID,
          userIds: [ALERT_RECIPIENT_USER_ID],
          staticEstimatedArrivalTime: STATIC_ETA,
          dynamicEstimatedArrivalTime: DELAYED_DYNAMIC_ETA,
          etaNew: DELAYED_DYNAMIC_ETA,
          delayMinutes: 31,
          detectedAt: expect.any(String),
        },
      },
    });
  });

  it('uses the evaluation wall-clock window instead of the dynamic ETA timestamp', async () => {
    jest.useFakeTimers().setSystemTime(new Date('2026-08-05T10:02:00.000Z'));
    try {
      redisGet.mockResolvedValue(JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));

      await expect(service.detectDelayedTrips()).resolves.toBe(1);

      const wallClockWindow = String(Math.floor(Date.now() / TRIP_DELAY_WINDOW_MS));
      expect(outboxCreate).toHaveBeenCalledWith(expect.objectContaining({
        data: expect.objectContaining({
          dedupeKey: `trip-delay:${TEST_TRIP_ID}:${TEST_STOP_ID}:${wallClockWindow}`,
        }),
      }));
      expect(outboxCreate).not.toHaveBeenCalledWith(expect.objectContaining({
        data: expect.objectContaining({
          dedupeKey: `trip-delay:${TEST_TRIP_ID}:${TEST_STOP_ID}:${String(Math.floor(new Date(DELAYED_DYNAMIC_ETA).getTime() / TRIP_DELAY_WINDOW_MS))}`,
        }),
      }));
    } finally {
      jest.useRealTimers();
    }
  });

  it('uses an existing Redis marker as a hot cache while preserving delayed truth', async () => {
    const dedupeKey = trackingTripDelayedDedupeKey(
      TEST_TRIP_ID,
      TEST_STOP_ID,
      String(Math.floor(Date.now() / TRIP_DELAY_WINDOW_MS)),
    );
    redisGet.mockImplementation(async (key: string) => {
      if (key === trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID)) {
        return JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA));
      }
      if (key === dedupeKey) return '1';
      return null;
    });

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'DELAYED',
      statusTransition: 'DELAYED',
    }));
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('falls back to durable Outbox dedupe when the Redis marker read fails', async () => {
    redisGet.mockImplementation(async (key: string) => {
      if (key === trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID)) {
        return JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA));
      }
      if (key.startsWith('tracking:trip_delayed:')) throw new Error('redis unavailable');
      return null;
    });
    outboxCreate.mockRejectedValue({ code: 'P2002' });

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'DELAYED',
    }));
    expect(outboxCreate).toHaveBeenCalledTimes(1);
  });

  it('does not publish duplicate detection in the same trip stop window', async () => {
    redisGet.mockResolvedValue(JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));
    outboxCreate.mockRejectedValue({ code: 'P2002' });

    await expect(service.detectTripDelay(TEST_TRIP_ID)).resolves.toBe(0);

    expect(outboxCreate).toHaveBeenCalled();
  });

  it('skips ARRIVED and SKIPPED stops during the background scan', async () => {
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop({ status: 'ARRIVED' }),
      createStop({
        stopId: '33333333-3333-4333-8333-333333333333',
        status: 'SKIPPED',
        sequence: 2,
      }),
    ]);

    await expect(service.detectTripDelay(TEST_TRIP_ID)).resolves.toBe(0);

    expect(redisGet).not.toHaveBeenCalledWith(
      expect.stringContaining('tracking:eta:'),
    );
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('does not overwrite realtime state while scanning multiple nonterminal stops', async () => {
    const secondStopId = '33333333-3333-4333-8333-333333333333';
    const realtimeStateWrites: string[] = [];
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop(),
      createStop({ stopId: secondStopId, sequence: 2 }),
    ]);
    redisGet.mockImplementation(async (key: string) => {
      if (key === trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID)) {
        return JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA));
      }
      if (key === trackingEtaKey(TEST_TRIP_ID, secondStopId)) {
        return JSON.stringify({
          ...createCachedEta(DELAYED_DYNAMIC_ETA),
          stopId: secondStopId,
        });
      }
      return null;
    });
    redisSet.mockImplementation(async (key: string) => {
      if (key.includes('tracking:trip_delay_state:')) realtimeStateWrites.push(key);
      return 'OK';
    });

    await expect(service.detectTripDelay(TEST_TRIP_ID)).resolves.toBe(2);

    expect(realtimeStateWrites).toEqual([
      trackingTripDelayStateKey(TEST_TRIP_ID, TEST_STOP_ID),
      trackingTripDelayStateKey(TEST_TRIP_ID, secondStopId),
    ]);
    expect(outboxCreate).toHaveBeenCalledTimes(2);
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
      delayStatus: 'DELAYED',
      statusTransition: 'DELAYED',
    });
  });

  it('does not let a stale older stop overwrite the realtime pointer or repeat DELAYED', async () => {
    const newerStopId = '33333333-3333-4333-8333-333333333333';
    const values = new Map<string, string>();
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop({ sequence: 1 }),
      createStop({ stopId: newerStopId, sequence: 2 }),
    ]);

    const newerEta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: newerStopId,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };
    const olderEta: EtaUpdateEvent = {
      ...newerEta,
      stopId: TEST_STOP_ID,
    };

    await expect(service.handleEtaUpdate(newerEta)).resolves.toEqual(
      expect.objectContaining({ statusTransition: 'DELAYED' }),
    );
    await expect(service.handleEtaUpdate(olderEta)).resolves.not.toHaveProperty('statusTransition');
    await expect(service.handleEtaUpdate(newerEta)).resolves.not.toHaveProperty('statusTransition');

    expect(JSON.parse(values.get(trackingTripDelayStateKey(TEST_TRIP_ID)) ?? '{}')).toEqual(
      expect.objectContaining({ stopId: newerStopId, stopSequence: 2 }),
    );
  });

  it('falls back to a requested stop state when the trip-level pointer is absent', async () => {
    const values = new Map<string, string>([
      [trackingTripDelayStateKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify({
        tripId: TEST_TRIP_ID,
        stopId: TEST_STOP_ID,
        delayStatus: 'DELAYED',
        delayMinutes: 31,
        evaluatedAt: '2026-06-04T09:00:00.000Z',
      })],
    ]);
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });
    tripDataProvider.getRouteStops.mockRejectedValue(new Error('trip provider unavailable'));

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'UNKNOWN',
      delayMinutes: 31,
    }));
  });

  it('does not emit a duplicate DELAYED transition when the first realtime state is legacy per-stop state', async () => {
    const values = new Map<string, string>([
      [trackingTripDelayStateKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify({
        tripId: TEST_TRIP_ID,
        stopId: TEST_STOP_ID,
        delayStatus: 'DELAYED',
        delayMinutes: 31,
        evaluatedAt: '2026-06-04T09:00:00.000Z',
      })],
    ]);
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.not.toHaveProperty('statusTransition');
  });

  it('rejects a realtime pointer with an invalid supplied stop sequence', async () => {
    const values = new Map<string, string>([
      [trackingTripDelayStateKey(TEST_TRIP_ID), JSON.stringify({
        tripId: TEST_TRIP_ID,
        stopId: TEST_STOP_ID,
        stopSequence: '2',
        delayStatus: 'DELAYED',
        delayMinutes: 31,
        evaluatedAt: '2026-06-04T09:00:00.000Z',
      })],
    ]);
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      statusTransition: 'DELAYED',
    }));
  });

  it('keeps delayed true when the same window already exists in the durable Outbox', async () => {
    let statePayload: string | null = null;
    redisGet.mockImplementation(async (key: string) => {
      if (key.includes('tracking:trip_delay_state:')) return statePayload;
      return null;
    });
    redisSet.mockImplementation(async (key: string, value: string) => {
      if (key.includes('tracking:trip_delay_state:')) statePayload = value;
      return 'OK';
    });
    outboxCreate.mockRejectedValue({ code: 'P2002' });

    const eta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };

    const first = await service.handleEtaUpdate(eta);
    const second = await service.handleEtaUpdate(eta);

    expect(first).toEqual(expect.objectContaining({ delayed: true, delayStatus: 'DELAYED' }));
    expect(second).toEqual(expect.objectContaining({ delayed: true, delayStatus: 'DELAYED' }));
    expect(second).not.toHaveProperty('statusTransition');
    expect(outboxCreate).toHaveBeenCalledTimes(2);
    expect(redisSet).not.toHaveBeenCalledWith(
      expect.stringContaining('tracking:trip_delayed:'),
      '1',
      'EX',
      expect.any(Number),
    );
  });

  it('retries Outbox creation after a transient database failure without a Redis dedupe marker', async () => {
    let statePayload: string | null = null;
    redisGet.mockImplementation(async (key: string) => {
      if (key.includes('tracking:trip_delay_state:')) return statePayload;
      return null;
    });
    redisSet.mockImplementation(async (key: string, value: string) => {
      if (key.includes('tracking:trip_delay_state:')) statePayload = value;
      return 'OK';
    });
    outboxCreate
      .mockRejectedValueOnce(new Error('tracking database unavailable'))
      .mockResolvedValue({});

    const eta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };

    await expect(service.handleEtaUpdate(eta)).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'DELAYED',
      delayMinutes: 31,
      statusTransition: 'DELAYED',
    }));
    expect(redisSet).not.toHaveBeenCalledWith(
      expect.stringContaining('tracking:trip_delayed:'),
      '1',
      'EX',
      expect.any(Number),
    );

    await expect(service.handleEtaUpdate(eta)).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'DELAYED',
      delayMinutes: 31,
    }));
    expect(outboxCreate).toHaveBeenCalledTimes(2);
    expect(redisSet).toHaveBeenCalledWith(
      expect.stringContaining('tracking:trip_delayed:'),
      '1',
      'EX',
      TRIP_DELAY_DEDUPE_TTL_SECONDS,
    );
  });

  it('serializes concurrent evaluations so only one request creates the Outbox event and transition', async () => {
    let statePayload: string | null = null;
    let lockOwner: string | null = null;
    redisGet.mockImplementation(async (key: string) => {
      if (key.includes('tracking:trip_delay_state:')) return statePayload;
      return null;
    });
    redisSet.mockImplementation(async (
      key: string,
      value: string,
      _mode: string,
      _ttl: number,
      condition?: string,
    ) => {
      if (key === trackingTripDelayLockKey(TEST_TRIP_ID)) {
        if (condition === 'NX' && lockOwner !== null) return null;
        lockOwner = value;
        return 'OK';
      }
      if (key.includes('tracking:trip_delay_state:')) statePayload = value;
      return 'OK';
    });
    redisEval.mockImplementation(async (_script: unknown, _keys: unknown, key: unknown) => {
      if (key === trackingTripDelayLockKey(TEST_TRIP_ID)) lockOwner = null;
      return 1;
    });
    outboxCreate.mockImplementation(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
      return {};
    });

    const eta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };

    const [first, second] = await Promise.all([
      service.handleEtaUpdate(eta),
      service.handleEtaUpdate(eta),
    ]);
    const updates = [first, second];

    expect(outboxCreate).toHaveBeenCalledTimes(1);
    expect(updates.filter((update) => update.statusTransition === 'DELAYED')).toHaveLength(1);
    expect(updates.some((update) => update.delayStatus === 'UNKNOWN')).toBe(true);
    expect(redisSet).toHaveBeenCalledWith(
      trackingTripDelayLockKey(TEST_TRIP_ID),
      expect.any(String),
      'EX',
      TRIP_DELAY_LOCK_TTL_SECONDS,
      'NX',
    );
  });

  it('emits DELAY_CLEARED once when a delayed stop returns on time', async () => {
    let statePayload = JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      delayStatus: 'DELAYED',
      delayMinutes: 31,
      evaluatedAt: '2026-06-04T09:00:00.000Z',
    });
    redisGet.mockImplementation(async (key: string) =>
      key.includes('tracking:trip_delay_state:') ? statePayload : null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      if (key.includes('tracking:trip_delay_state:')) statePayload = value;
      return 'OK';
    });

    const eta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };

    const cleared = await service.handleEtaUpdate(eta);
    const repeated = await service.handleEtaUpdate(eta);

    expect(cleared).toEqual(expect.objectContaining({
      delayed: false,
      delayStatus: 'ON_TIME',
      delayMinutes: 30,
      statusTransition: 'DELAY_CLEARED',
    }));
    expect(repeated).toEqual(expect.objectContaining({
      delayed: false,
      delayStatus: 'ON_TIME',
    }));
    expect(repeated).not.toHaveProperty('statusTransition');
  });

  it('does not emit DELAY_CLEARED when an on-time evaluation belongs to another stop', async () => {
    const previousStopId = TEST_STOP_ID;
    const currentStopId = '33333333-3333-4333-8333-333333333333';
    let statePayload = JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: previousStopId,
      delayStatus: 'DELAYED',
      delayMinutes: 31,
      evaluatedAt: '2026-06-04T09:00:00.000Z',
    });
    redisGet.mockImplementation(async (key: string) =>
      key.includes('tracking:trip_delay_state:') ? statePayload : null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      if (key.includes('tracking:trip_delay_state:')) statePayload = value;
      return 'OK';
    });
    tripDataProvider.getRouteStops.mockResolvedValue([createStop({
      stopId: currentStopId,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
    })]);

    const update = await service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: currentStopId,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    });

    expect(update).toEqual(expect.objectContaining({
      delayed: false,
      delayStatus: 'ON_TIME',
      delayMinutes: 0,
    }));
    expect(update).not.toHaveProperty('statusTransition');
  });

  it('returns UNKNOWN without a false clear when Trip evaluation fails', async () => {
    redisGet.mockResolvedValue(JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      delayStatus: 'DELAYED',
      delayMinutes: 31,
      evaluatedAt: '2026-06-04T09:00:00.000Z',
    }));
    tripDataProvider.getRouteStops.mockRejectedValue(new Error('trip provider unavailable'));

    await expect(service.handleEtaUpdate({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'UNKNOWN',
      delayMinutes: 31,
    }));
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('emits DELAYED when realtime changes stop after background prepopulates the new stop', async () => {
    const nextStopId = '33333333-3333-4333-8333-333333333333';
    const values = new Map<string, string>();
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });

    const firstEta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };
    await service.handleEtaUpdate(firstEta);

    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop(),
      createStop({ stopId: nextStopId, sequence: 2 }),
    ]);
    values.set(trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA)));
    values.set(
      trackingEtaKey(TEST_TRIP_ID, nextStopId),
      JSON.stringify(createCachedEta(DELAYED_DYNAMIC_ETA, nextStopId)),
    );
    await service.detectTripDelay(TEST_TRIP_ID);

    await expect(service.handleEtaUpdate({
      ...firstEta,
      stopId: nextStopId,
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'DELAYED',
      statusTransition: 'DELAYED',
    }));
  });

  it('emits DELAY_CLEARED from realtime state after background evaluates the stop on time', async () => {
    const values = new Map<string, string>();
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });

    const delayedEta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };
    await service.handleEtaUpdate(delayedEta);
    values.set(trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify(createCachedEta(ON_TIME_DYNAMIC_ETA)));
    await service.detectTripDelay(TEST_TRIP_ID);

    await expect(service.handleEtaUpdate({
      ...delayedEta,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
    })).resolves.toEqual(expect.objectContaining({
      delayed: false,
      delayStatus: 'ON_TIME',
      statusTransition: 'DELAY_CLEARED',
    }));
  });

  it('keeps UNKNOWN delayed truth from realtime state after background recovery', async () => {
    const values = new Map<string, string>();
    redisGet.mockImplementation(async (key: string) => values.get(key) ?? null);
    redisSet.mockImplementation(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    });

    const delayedEta: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 45,
      estimatedArrivalTime: DELAYED_DYNAMIC_ETA,
      distanceMeters: 10_000,
      updatedAt: '2026-06-04T09:15:00.000Z',
    };
    await service.handleEtaUpdate(delayedEta);
    values.set(trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify(createCachedEta(ON_TIME_DYNAMIC_ETA)));
    await service.detectTripDelay(TEST_TRIP_ID);
    tripDataProvider.getRouteStops.mockRejectedValue(new Error('trip provider unavailable'));

    await expect(service.handleEtaUpdate({
      ...delayedEta,
      etaMinutes: 30,
      estimatedArrivalTime: ON_TIME_DYNAMIC_ETA,
    })).resolves.toEqual(expect.objectContaining({
      delayed: true,
      delayStatus: 'UNKNOWN',
      delayMinutes: 31,
    }));
  });

  function createStop(overrides: Partial<TripStopSnapshot> = {}): TripStopSnapshot {
    return {
      stopId: TEST_STOP_ID,
      latitude: 10.762622,
      longitude: 106.660172,
      sequence: 1,
      alertRecipientUserIds: [ALERT_RECIPIENT_USER_ID],
      estimatedArrivalTime: STATIC_ETA,
      ...overrides,
    };
  }

  function createCachedEta(estimatedArrivalTime: string, stopId = TEST_STOP_ID) {
    return {
      tripId: TEST_TRIP_ID,
      stopId,
      estimatedArrivalTime,
    };
  }
});
