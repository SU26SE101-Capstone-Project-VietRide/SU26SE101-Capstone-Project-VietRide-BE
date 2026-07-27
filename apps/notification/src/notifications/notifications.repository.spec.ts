import { EmailDeliveryStatus } from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import { NotificationsRepository } from './notifications.repository';

describe('NotificationsRepository email delivery lease', () => {
  const emailDelivery = {
    updateMany: jest.fn(),
    findMany: jest.fn(),
  };
  const repository = new NotificationsRepository({
    emailDelivery,
  } as unknown as NotificationPrismaService);

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('atomically claims pending/retrying rows or a stale SENDING lease', async () => {
    const leaseCutoff = new Date('2026-07-27T10:00:00.000Z');
    const claimToken = new Date('2026-07-27T10:05:00.000Z');
    emailDelivery.updateMany.mockResolvedValue({ count: 1 });

    await expect(
      repository.markEmailDeliverySending(
        '11111111-1111-4111-8111-111111111111',
        leaseCutoff,
        claimToken,
      ),
    ).resolves.toBe(true);

    expect(emailDelivery.updateMany).toHaveBeenCalledWith({
      where: {
        id: '11111111-1111-4111-8111-111111111111',
        OR: [
          {
            status: {
              in: [EmailDeliveryStatus.PENDING, EmailDeliveryStatus.RETRYING],
            },
          },
          {
            status: EmailDeliveryStatus.SENDING,
            updatedAt: { lte: leaseCutoff },
          },
        ],
      },
      data: { status: EmailDeliveryStatus.SENDING, updatedAt: claimToken },
    });
  });

  it('fences the SENT transition with the exact sending claim token', async () => {
    const claimToken = new Date('2026-07-27T10:05:00.000Z');
    emailDelivery.updateMany.mockResolvedValue({ count: 1 });

    await expect(
      repository.markEmailDeliverySent(
        '11111111-1111-4111-8111-111111111111',
        'provider-message-id',
        claimToken,
      ),
    ).resolves.toBe(true);

    expect(emailDelivery.updateMany).toHaveBeenCalledWith({
      where: {
        id: '11111111-1111-4111-8111-111111111111',
        status: EmailDeliveryStatus.SENDING,
        updatedAt: claimToken,
      },
      data: {
        status: EmailDeliveryStatus.SENT,
        providerMessageId: 'provider-message-id',
        sentAt: expect.any(Date),
        lastError: null,
      },
    });
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
