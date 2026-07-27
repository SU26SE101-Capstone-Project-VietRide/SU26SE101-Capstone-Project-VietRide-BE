import { EmailDeliveryStatus } from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import { NotificationsRepository } from './notifications.repository';

describe('NotificationsRepository email delivery lease', () => {
  const prisma = {
    $queryRaw: jest.fn(),
    $executeRaw: jest.fn(),
  };
  const emailDelivery = {
    updateMany: jest.fn(),
    findMany: jest.fn(),
  };
  const repository = new NotificationsRepository({
    ...prisma,
    emailDelivery,
  } as unknown as NotificationPrismaService);

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('atomically claims pending/retrying rows or a stale SENDING lease', async () => {
    const leaseCutoff = new Date('2026-07-27T10:00:00.000Z');
    const persistedClaimToken = '2026-07-27 10:05:00.123456+00';
    prisma.$queryRaw.mockResolvedValue([{ claimToken: persistedClaimToken }]);

    await expect(
      repository.markEmailDeliverySending(
        '11111111-1111-4111-8111-111111111111',
        leaseCutoff,
      ),
    ).resolves.toEqual(persistedClaimToken);

    const claimQuery = prisma.$queryRaw.mock.calls[0][0];
    expect(claimQuery.strings.join('')).toContain(
      'RETURNING "updated_at"::text AS "claimToken"',
    );
    expect(claimQuery.values).toEqual([
      '11111111-1111-4111-8111-111111111111',
      leaseCutoff,
    ]);
  });

  it('returns null when another worker already owns the sending lease', async () => {
    prisma.$queryRaw.mockResolvedValue([]);

    await expect(
      repository.markEmailDeliverySending(
        '11111111-1111-4111-8111-111111111111',
        new Date('2026-07-27T10:00:00.000Z'),
      ),
    ).resolves.toBeNull();
  });

  it('fences the SENT transition with the exact sending claim token', async () => {
    const claimToken = '2026-07-27 10:05:00.123456+00';
    prisma.$executeRaw.mockResolvedValue(1);

    await expect(
      repository.markEmailDeliverySent(
        '11111111-1111-4111-8111-111111111111',
        'provider-message-id',
        claimToken,
      ),
    ).resolves.toBe(true);

    const sentQuery = prisma.$executeRaw.mock.calls[0][0];
    expect(sentQuery.strings.join('')).toContain('"updated_at" = ');
    expect(sentQuery.strings.join('')).toContain('::timestamptz');
    expect(sentQuery.values).toEqual([
      'provider-message-id',
      expect.any(Date),
      '11111111-1111-4111-8111-111111111111',
      claimToken,
    ]);
  });

  it('selects only stale SENDING row IDs in deterministic bounded batches', async () => {
    const leaseCutoff = new Date('2026-07-27T10:00:00.000Z');
    emailDelivery.findMany.mockResolvedValue([{ id: 'delivery-1' }, { id: 'delivery-2' }]);

    await expect(repository.listStaleSendingEmailDeliveryIds(leaseCutoff, 100)).resolves.toEqual([
      'delivery-1',
      'delivery-2',
    ]);

    expect(emailDelivery.findMany).toHaveBeenCalledWith({
      where: {
        status: EmailDeliveryStatus.SENDING,
        updatedAt: { lte: leaseCutoff },
      },
      orderBy: { updatedAt: 'asc' },
      take: 100,
      select: { id: true },
    });
  });
});
