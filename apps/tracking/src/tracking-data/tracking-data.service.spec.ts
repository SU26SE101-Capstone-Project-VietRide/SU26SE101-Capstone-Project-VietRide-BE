import type { TripDataProvider } from '../eta/trip-data.provider';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataService } from './tracking-data.service';
import type { EtaResponseDto } from './dto/eta-response.dto';

const ETA: EtaResponseDto = {
  tripId: '11111111-1111-4111-8111-111111111111',
  stopId: '33333333-3333-4333-8333-333333333333',
  etaMinutes: 65,
  estimatedArrivalTime: '2026-08-06T03:05:00.000Z',
  distanceMeters: 58_000,
  updatedAt: '2026-08-06T02:00:00.000Z',
  delayed: false,
  delayStatus: 'ON_TIME',
  delayMinutes: 0,
};

describe('TrackingDataService ETA selection', () => {
  it('adds stopName when stopId is supplied', async () => {
    const repository = {
      findEta: jest.fn(async () => ETA),
    } as unknown as TrackingDataRepository;
    const trips = {
      getRouteStops: jest.fn(async () => [{
        stopId: ETA.stopId,
        stopName: 'Ben xe Da Lat',
        latitude: 11.94,
        longitude: 108.44,
        sequence: 2,
        status: 'PENDING',
      }]),
    } as TripDataProvider;
    const service = new TrackingDataService(repository, trips);

    const result = await service.getEta(ETA.tripId, { stopId: ETA.stopId });

    expect(result.eta).toEqual({ ...ETA, stopName: 'Ben xe Da Lat' });
  });

  it('selects the first pending stop when stopId is omitted', async () => {
    const repository = {
      findLatest: jest.fn(async () => ({ tripId: ETA.tripId })),
      findEta: jest.fn(async (_tripId: string, stopId: string) =>
        stopId === ETA.stopId ? ETA : null),
    } as unknown as TrackingDataRepository;
    const trips = {
      getRouteStops: jest.fn(async () => [
        {
          stopId: '22222222-2222-4222-8222-222222222222',
          stopName: 'Past stop',
          latitude: 10.7,
          longitude: 106.6,
          sequence: 1,
          status: 'ARRIVED',
        },
        {
          stopId: ETA.stopId,
          stopName: 'Next stop',
          latitude: 11.94,
          longitude: 108.44,
          sequence: 2,
          status: 'PENDING',
        },
      ]),
    } as TripDataProvider;
    const service = new TrackingDataService(repository, trips);

    const result = await service.getEta(ETA.tripId, {});

    expect(result.eta).toEqual({ ...ETA, stopName: 'Next stop' });
    expect(repository.findEta).toHaveBeenCalledTimes(1);
  });

  it('returns null when latest GPS is unavailable', async () => {
    const repository = {
      findLatest: jest.fn(async () => null),
      findEta: jest.fn(),
    } as unknown as TrackingDataRepository;
    const trips = { getRouteStops: jest.fn(async () => []) } as TripDataProvider;
    const service = new TrackingDataService(repository, trips);

    await expect(service.getEta(ETA.tripId, {})).resolves.toEqual({ eta: null });
    expect(repository.findEta).not.toHaveBeenCalled();
  });
});
