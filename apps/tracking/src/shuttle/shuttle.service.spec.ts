import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';
import { ShuttleService, type ShuttleTrackingContext } from './shuttle.service';

describe('ShuttleService', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('writes only shuttle Redis keys without calculating ETA on the live GPS path', async () => {
    const client = {
      eval: jest.fn(async () => 1),
    };
    const redis = { getClient: jest.fn(() => client) } as unknown as RedisService;
    const signer = { sign: jest.fn() } as unknown as TrackingInternalJwtSigner;
    const service = new ShuttleService(redis, signer, {} as Env);

    const result = await service.recordLocation(
      {
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        latitude: 10.77,
        longitude: 106.7,
        speedKmh: 30,
        recordedAt: '2026-07-13T01:00:00.000Z',
      },
    );

    expect(result.gps.shuttleTripId).toBe('36000000-0000-4000-8000-000000000001');
    expect(result.duplicate).toBe(false);
    expect(client.eval).toHaveBeenCalledWith(
      expect.any(String),
      3,
      'tracking:shuttle:gps_idempotency:36000000-0000-4000-8000-000000000001:2026-07-13T01:00:00.000Z',
      'tracking:shuttle:latest:36000000-0000-4000-8000-000000000001',
      'tracking:shuttle:gps_buffer:36000000-0000-4000-8000-000000000001',
      expect.stringMatching(/^[a-f0-9]{64}$/),
      expect.any(String),
      '86400',
      '300',
      '1000',
      '86400',
    );
  });

  it('returns duplicate without recalculating ETA', async () => {
    const client = {
      eval: jest.fn(async () => 0),
      get: jest.fn(),
    };
    const service = new ShuttleService(
      { getClient: jest.fn(() => client) } as unknown as RedisService,
      { sign: jest.fn() } as unknown as TrackingInternalJwtSigner,
      {} as Env,
    );

    const result = await service.recordLocation(createGpsUpdate());

    expect(result).toEqual({ gps: createGpsUpdate(), duplicate: true });
    expect(client.get).not.toHaveBeenCalled();
  });

  it('rejects a reused shuttle operation identity with a different payload', async () => {
    const client = { eval: jest.fn(async () => -1) };
    const service = new ShuttleService(
      { getClient: jest.fn(() => client) } as unknown as RedisService,
      { sign: jest.fn() } as unknown as TrackingInternalJwtSigner,
      {} as Env,
    );

    await expect(service.recordLocation(createGpsUpdate())).rejects.toThrow(
      'GPS_OPERATION_PAYLOAD_MISMATCH',
    );
  });

  it('returns only own eligible pickups and counts unique pending orders from ETA state', async () => {
    const service = createPassengerContextService(JSON.stringify({ order: 2 }));
    const context = createTrackingContext([
      createStop(2, 'PENDING', false, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
      createStop(3, 'PENDING', false, 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'),
      createStop(3, 'PENDING', false, 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
      createStop(4, 'CANCELLED', false, 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'),
      createStop(5, 'PENDING', true, 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'),
    ]);

    const result = await service.getPassengerContext(context);

    expect(result.ownPickups).toEqual([{
      bookingId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
      pickupOrder: 5,
      serviceAddress: expect.any(String),
      serviceOrder: 5,
      latitude: 10.5,
      longitude: 106.5,
      status: 'PENDING',
      stopsBeforePickup: 2,
    }]);
    expect(JSON.stringify(result)).not.toContain('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
    expect(JSON.stringify(result)).not.toContain('"latitude":10.2');
    expect(result.station?.stationId).toBe('66666666-6666-4666-8666-666666666666');
  });

  it('redacts other outbound service addresses and coordinates while preserving direction', async () => {
    const service = createPassengerContextService(null);
    const context = createTrackingContext([
      {
        ...createStop(1, 'PICKED_UP', false, null),
        isStation: true,
      },
      {
        ...createStop(2, 'PENDING', true, 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'),
        serviceAddress: 'Own destination',
      },
      {
        ...createStop(3, 'PENDING', false, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
        latitude: 11.1,
        longitude: 107.1,
        serviceAddress: 'Other passenger destination',
      },
    ]);
    context.direction = 'OUTBOUND_FROM_STATION';

    const result = await service.getPassengerContext(context);

    expect(result.direction).toBe('OUTBOUND_FROM_STATION');
    expect(result.ownPickups).toEqual([
      expect.objectContaining({
        bookingId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
        pickupOrder: 2,
        serviceAddress: 'Own destination',
        serviceOrder: 2,
      }),
    ]);
    expect(JSON.stringify(result)).not.toContain('Other passenger destination');
    expect(JSON.stringify(result)).not.toContain('11.1');
  });

  it('falls back to the first non-terminal manifest order and returns PICKED_UP as zero', async () => {
    const service = createPassengerContextService(null);
    const context = createTrackingContext([
      createStop(1, 'PENDING', false, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
      createStop(2, 'PENDING', false, 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'),
      createStop(2, 'PENDING', false, 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
      createStop(3, 'PENDING', true, 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'),
      createStop(4, 'PICKED_UP', true, 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'),
    ]);

    const result = await service.getPassengerContext(context);

    expect(result.ownPickups).toEqual([
      expect.objectContaining({ pickupOrder: 3, stopsBeforePickup: 2 }),
      expect.objectContaining({ pickupOrder: 4, stopsBeforePickup: 0 }),
    ]);
  });

  it('returns null station for missing coordinates without exposing full stops', async () => {
    const service = createPassengerContextService(null);
    const context = createTrackingContext([
      createStop(1, 'PENDING', true, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    ]);
    context.station = {
      stationId: '66666666-6666-4666-8666-666666666666',
      name: 'Station',
      latitude: null,
      longitude: null,
      pickupOrder: 2,
    };

    const result = await service.getPassengerContext(context);

    expect(result.station).toBeNull();
    expect(result).not.toHaveProperty('stops');
  });

  it('maps the complete operator stop context without leaking internal pickup markers', () => {
    const service = createPassengerContextService(null);
    const context = createTrackingContext([
      {
        ...createStop(1, 'DELIVERED', false, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
        roadDistanceSnapshotMeters: 4_500,
      },
      {
        ...createStop(2, 'PENDING', false, null),
        isStation: true,
      },
    ]);
    context.scope = 'OPERATOR';
    context.direction = 'INBOUND_TO_STATION';
    context.status = 'IN_PROGRESS';

    const result = service.getOperatorContext(context);

    expect(result).toEqual({
      shuttleTripId: context.shuttleTripId,
      mainTripId: context.mainTripId,
      direction: 'INBOUND_TO_STATION',
      status: 'IN_PROGRESS',
      stops: [
        expect.objectContaining({
          pickupOrder: 1,
          bookingId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          status: 'DELIVERED',
          isStation: false,
          roadDistanceMeters: 4_500,
          passengerCount: 2,
          pickedUpAt: '2026-08-15T10:05:00.000Z',
          deliveredAt: '2026-08-15T10:25:00.000Z',
          statusReason: null,
        }),
        expect.objectContaining({
          pickupOrder: 2,
          bookingId: null,
          isStation: true,
          passengerCount: null,
          pickedUpAt: null,
          deliveredAt: null,
          statusReason: null,
        }),
      ],
      station: expect.objectContaining({
        stationId: '66666666-6666-4666-8666-666666666666',
        pickupOrder: 6,
      }),
    });
    expect(JSON.stringify(result)).not.toContain('isOwnPickup');
    expect(JSON.stringify(result)).not.toContain('roadDistanceSnapshotMeters');
    expect(JSON.stringify(result)).not.toContain('displayName');
    expect(JSON.stringify(result)).not.toContain('phone');
  });

  it('returns null operator station for incomplete coordinates and fails closed on malformed context', () => {
    const service = createPassengerContextService(null);
    const context = createTrackingContext([
      createStop(1, 'PENDING', false, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    ]);
    context.scope = 'OPERATOR';
    context.direction = 'INBOUND_TO_STATION';
    context.status = 'IN_PROGRESS';
    context.station = {
      stationId: '66666666-6666-4666-8666-666666666666',
      name: 'Station',
      latitude: null,
      longitude: null,
      pickupOrder: 2,
    };

    expect(service.getOperatorContext(context).station).toBeNull();

    delete context.status;
    expect(() => service.getOperatorContext(context)).toThrow(
      expect.objectContaining({
        status: 503,
        response: { errorCode: 'TRACKING_CONTEXT_UNAVAILABLE', detail: expect.any(String) },
      }),
    );
  });

  it('fails closed when own pickup or station metadata is incomplete', async () => {
    const service = createPassengerContextService(null);
    const invalidPickup = createTrackingContext([
      { ...createStop(1, 'PENDING', true, null), latitude: 91 },
    ]);
    const missingStation = createTrackingContext([
      createStop(1, 'PENDING', true, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
    ]);
    delete missingStation.station;

    await expect(service.getPassengerContext(invalidPickup)).rejects.toMatchObject({
      status: 503,
      response: { errorCode: 'TRACKING_CONTEXT_UNAVAILABLE' },
    });
    await expect(service.getPassengerContext(missingStation)).rejects.toMatchObject({
      status: 503,
      response: { errorCode: 'TRACKING_CONTEXT_UNAVAILABLE' },
    });
  });

  it('parses additive own-pickup and nullable station metadata from Trip context', async () => {
    const context = createTrackingContext([
      {
        ...createStop(1, 'PICKED_UP', true, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'),
        roadDistanceSnapshotMeters: 4_500,
      },
      {
        pickupOrder: 6,
        bookingId: null,
        latitude: 10.8,
        longitude: 106.8,
        status: 'PENDING',
        isStation: true,
        isOwnPickup: false,
        roadDistanceSnapshotMeters: null,
        passengerCount: null,
        pickedUpAt: null,
        deliveredAt: null,
        statusReason: null,
      },
    ]);
    context.station = {
      stationId: '66666666-6666-4666-8666-666666666666',
      name: 'Station',
      latitude: null,
      longitude: null,
      pickupOrder: 6,
    };
    global.fetch = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => context,
    } as Response)) as typeof fetch;
    const signer = { sign: jest.fn(async () => 'internal-token') } as unknown as TrackingInternalJwtSigner;
    const service = new ShuttleService(
      { getClient: jest.fn() } as unknown as RedisService,
      signer,
      {
        TRIP_SERVICE_BASE_URL: 'http://trip.test',
        TRACKING_AUTH_HTTP_TIMEOUT_MS: 1_000,
      } as Env,
    );

    const result = await service.getContext(
      { userId: '22222222-2222-4222-8222-222222222222', role: 'PASSENGER' },
      context.shuttleTripId,
    );

    expect(result.stops[0]?.isOwnPickup).toBe(true);
    expect(result.stops[1]?.roadDistanceSnapshotMeters).toBeNull();
    expect(result.station?.latitude).toBeNull();
    expect(global.fetch).toHaveBeenCalledTimes(1);
  });

  it('rejects malformed Trip context with TRACKING_CONTEXT_UNAVAILABLE', async () => {
    global.fetch = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ success: true, data: { shuttleTripId: 'invalid' } }),
    } as Response)) as typeof fetch;
    const service = new ShuttleService(
      { getClient: jest.fn() } as unknown as RedisService,
      { sign: jest.fn(async () => 'internal-token') } as unknown as TrackingInternalJwtSigner,
      {
        TRIP_SERVICE_BASE_URL: 'http://trip.test',
        TRACKING_AUTH_HTTP_TIMEOUT_MS: 1_000,
      } as Env,
    );

    await expect(service.getContext(
      { userId: '22222222-2222-4222-8222-222222222222', role: 'PASSENGER' },
      '36000000-0000-4000-8000-000000000001',
    )).rejects.toThrow('TRACKING_CONTEXT_UNAVAILABLE');
  });
});

function createGpsUpdate(): ShuttleGpsUpdateDto {
  return {
    shuttleTripId: '36000000-0000-4000-8000-000000000001',
    latitude: 10.77,
    longitude: 106.7,
    speedKmh: 30,
    recordedAt: '2026-07-13T01:00:00.000Z',
  };
}

function createPassengerContextService(etaState: string | null): ShuttleService {
  const client = { get: jest.fn(async () => etaState) };
  return new ShuttleService(
    { getClient: jest.fn(() => client) } as unknown as RedisService,
    { sign: jest.fn() } as unknown as TrackingInternalJwtSigner,
    {} as Env,
  );
}

function createTrackingContext(stops: ShuttleTrackingContext['stops']): ShuttleTrackingContext {
  return {
    shuttleTripId: '36000000-0000-4000-8000-000000000001',
    mainTripId: '11111111-1111-4111-8111-111111111111',
    operatorId: '22222222-2222-4222-8222-222222222222',
    driverUserId: '33333333-3333-4333-8333-333333333333',
    allowed: true,
    scope: 'PASSENGER',
    stops,
    station: {
      stationId: '66666666-6666-4666-8666-666666666666',
      name: 'Station',
      latitude: 10.8,
      longitude: 106.8,
      pickupOrder: 6,
    },
  };
}

function createStop(
  pickupOrder: number,
  status: string,
  isOwnPickup: boolean,
  bookingId: string | null,
): ShuttleTrackingContext['stops'][number] {
  return {
    pickupOrder,
    bookingId,
    latitude: 10 + pickupOrder / 10,
    longitude: 106 + pickupOrder / 10,
    status,
    isStation: false,
    isOwnPickup,
    serviceAddress: bookingId ? `Service ${bookingId}` : 'Service stop',
    serviceOrder: pickupOrder,
    passengerCount: 2,
    pickedUpAt: '2026-08-15T10:05:00.000Z',
    deliveredAt: '2026-08-15T10:25:00.000Z',
    statusReason: status === 'NO_SHOW' || status === 'CANCELLED' ? 'Passenger unavailable' : null,
  };
}
