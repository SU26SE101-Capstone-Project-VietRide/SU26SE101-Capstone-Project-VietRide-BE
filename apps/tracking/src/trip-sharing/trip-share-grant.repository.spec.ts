import type { Env } from '../config/env.schema';
import type { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import type { TripShareTokenCodec } from './trip-share-token.codec';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const OTHER_USER_ID = '33333333-3333-4333-8333-333333333333';
const GRANT_ID = '44444444-4444-4444-8444-444444444444';
const NOW = new Date('2026-08-03T00:00:00.000Z');
const EXPIRES_AT = new Date('2026-08-04T00:00:00.000Z');

describe('TripShareGrantRepository', () => {
  const prisma = createPrismaMock();
  const repository = new TripShareGrantRepository(prisma as unknown as TrackingPrismaService);

  beforeEach(() => jest.clearAllMocks());

  it('expires only the owner active rows whose expiry has passed', async () => {
    prisma.tripShareGrant.updateMany.mockResolvedValue({ count: 1 });

    await expect(repository.expireActiveForOwnerTrip(TRIP_ID, USER_ID, NOW)).resolves.toBe(1);
    expect(prisma.tripShareGrant.updateMany).toHaveBeenCalledWith({
      where: { tripId: TRIP_ID, createdByUserId: USER_ID, revokedAt: null, expiresAt: { lte: NOW } },
      data: { revokedAt: NOW, revokeReason: 'EXPIRED' },
    });
  });

  it('reads only a non-revoked, non-expired grant for the owner and trip', async () => {
    prisma.tripShareGrant.findFirst.mockResolvedValue(grantRow());

    await expect(repository.findActiveByOwnerTrip(TRIP_ID, USER_ID, NOW)).resolves.toEqual(grantRow());
    expect(prisma.tripShareGrant.findFirst).toHaveBeenCalledWith({
      where: { tripId: TRIP_ID, createdByUserId: USER_ID, revokedAt: null, expiresAt: { gt: NOW } },
    });
  });

  it('creates from hash-only input and never accepts a plaintext token field', async () => {
    prisma.tripShareGrant.create.mockResolvedValue(grantRow());

    await repository.create({
      id: GRANT_ID,
      tripId: TRIP_ID,
      createdByUserId: USER_ID,
      tokenHash: 'a'.repeat(64),
      tokenVersion: 1,
      expiresAt: EXPIRES_AT,
    });

    const data = prisma.tripShareGrant.create.mock.calls[0]?.[0]?.data as Record<string, unknown>;
    expect(data).toEqual(expect.objectContaining({ tokenHash: 'a'.repeat(64) }));
    expect(data).not.toHaveProperty('token');
    expect(data).not.toHaveProperty('shareUrl');
  });

  it('scopes user revocation to the creator, trip, and active row', async () => {
    prisma.tripShareGrant.updateMany.mockResolvedValue({ count: 1 });

    await repository.revokeOwnActiveGrant(TRIP_ID, USER_ID, NOW);
    expect(prisma.tripShareGrant.updateMany).toHaveBeenCalledWith({
      where: { tripId: TRIP_ID, createdByUserId: USER_ID, revokedAt: null },
      data: { revokedAt: NOW, revokeReason: 'USER_REVOKED' },
    });
  });

  it('revokes one grant by id with the supplied reason', async () => {
    prisma.tripShareGrant.updateMany.mockResolvedValue({ count: 1 });

    await repository.revokeGrantById(GRANT_ID, 'CREATION_ROLLBACK', NOW);
    expect(prisma.tripShareGrant.updateMany).toHaveBeenCalledWith({
      where: { id: GRANT_ID, revokedAt: null },
      data: { revokedAt: NOW, revokeReason: 'CREATION_ROLLBACK' },
    });
  });

  it('revokes every active grant for a terminal trip', async () => {
    prisma.tripShareGrant.updateMany.mockResolvedValue({ count: 2 });

    await expect(repository.revokeAllActiveForTrip(TRIP_ID, NOW)).resolves.toBe(2);
    expect(prisma.tripShareGrant.updateMany).toHaveBeenCalledWith({
      where: { tripId: TRIP_ID, revokedAt: null },
      data: { revokedAt: NOW, revokeReason: 'TRIP_TERMINATED' },
    });
  });
});

describe('TripShareGrantService', () => {
  const codec = {
    create: jest.fn((grantId: string) => ({ token: `v1.${grantId}.signature`, tokenHash: 'b'.repeat(64) })),
  } as unknown as jest.Mocked<TripShareTokenCodec>;
  const env = { TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400 } as Env;

  beforeEach(() => jest.clearAllMocks());

  it('returns the stable deterministic token for an existing active grant', async () => {
    const repository = createRepositoryMock();
    repository.findActiveByOwnerTrip.mockResolvedValue(grantRow());
    const service = new TripShareGrantService(repository, codec, env);

    await expect(service.ensureActive(TRIP_ID, USER_ID, NOW)).resolves.toEqual({
      grant: grantRow(),
      token: `v1.${GRANT_ID}.signature`,
    });
    expect(repository.create).not.toHaveBeenCalled();
  });

  it('creates a grant with only the deterministic token hash', async () => {
    const repository = createRepositoryMock();
    repository.findActiveByOwnerTrip.mockResolvedValue(null);
    repository.create.mockImplementation(async (input) => grantRow({
      id: input.id,
      createdByUserId: input.createdByUserId,
      tokenHash: input.tokenHash,
      expiresAt: input.expiresAt,
    }));
    const service = new TripShareGrantService(repository, codec, env);

    const result = await service.ensureActive(TRIP_ID, USER_ID, NOW);
    const createInput = repository.create.mock.calls[0]?.[0] as unknown as Record<string, unknown>;

    expect(result.token).toContain('v1.');
    expect(createInput.tokenHash).toBe('b'.repeat(64));
    expect(createInput).not.toHaveProperty('token');
    expect(createInput).not.toHaveProperty('shareUrl');
    expect(createInput.expiresAt).toEqual(EXPIRES_AT);
  });

  it('returns the partial-unique race winner after Prisma P2002', async () => {
    const repository = createRepositoryMock();
    repository.findActiveByOwnerTrip
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce(grantRow());
    repository.create.mockRejectedValue({ code: 'P2002' });
    const service = new TripShareGrantService(repository, codec, env);

    await expect(service.ensureActive(TRIP_ID, USER_ID, NOW)).resolves.toEqual({
      grant: grantRow(),
      token: `v1.${GRANT_ID}.signature`,
    });
  });

  it('rethrows non-P2002 create errors', async () => {
    const repository = createRepositoryMock();
    const failure = new Error('database unavailable');
    repository.findActiveByOwnerTrip.mockResolvedValue(null);
    repository.create.mockRejectedValue(failure);
    const service = new TripShareGrantService(repository, codec, env);

    await expect(service.ensureActive(TRIP_ID, USER_ID, NOW)).rejects.toBe(failure);
  });

  it('keeps two users on the same trip independent', async () => {
    const repository = createRepositoryMock();
    repository.findActiveByOwnerTrip.mockResolvedValue(null);
    repository.create.mockImplementation(async (input) => grantRow({
      id: input.id,
      createdByUserId: input.createdByUserId,
      tokenHash: input.tokenHash,
      expiresAt: input.expiresAt,
    }));
    const service = new TripShareGrantService(repository, codec, env);

    const first = await service.ensureActive(TRIP_ID, USER_ID, NOW);
    const second = await service.ensureActive(TRIP_ID, OTHER_USER_ID, NOW);

    expect(first.grant.createdByUserId).toBe(USER_ID);
    expect(second.grant.createdByUserId).toBe(OTHER_USER_ID);
    expect(first.grant.id).not.toBe(second.grant.id);
  });
});

function createPrismaMock() {
  return {
    tripShareGrant: {
      updateMany: jest.fn(),
      findFirst: jest.fn(),
      findUnique: jest.fn(),
      create: jest.fn(),
    },
  };
}

function createRepositoryMock(): jest.Mocked<TripShareGrantRepository> {
  return {
    expireActiveForOwnerTrip: jest.fn().mockResolvedValue(0),
    findActiveByOwnerTrip: jest.fn(),
    findById: jest.fn(),
    create: jest.fn(),
    revokeOwnActiveGrant: jest.fn(),
    revokeGrantById: jest.fn(),
    revokeAllActiveForTrip: jest.fn(),
  } as unknown as jest.Mocked<TripShareGrantRepository>;
}

function grantRow(overrides: Record<string, unknown> = {}) {
  return {
    id: GRANT_ID,
    tripId: TRIP_ID,
    createdByUserId: USER_ID,
    tokenHash: 'a'.repeat(64),
    tokenVersion: 1,
    expiresAt: EXPIRES_AT,
    revokedAt: null,
    revokeReason: null,
    createdAt: NOW,
    updatedAt: NOW,
    ...overrides,
  };
}
