import { EMAIL_RECOVERY_BATCH_SIZE, EMAIL_SENDING_LEASE_MS } from './email-send.constants';
import { EmailDeliveryRecoveryService } from './email-delivery-recovery.service';
import { EmailSendQueue } from './email-send.queue';
import { NotificationsRepository } from './notifications.repository';

describe('EmailDeliveryRecoveryService', () => {
  it('retries retained jobs for stale SENDING rows and reports recovered count', async () => {
    const repository = {
      listStaleSendingEmailDeliveryIds: jest.fn().mockResolvedValue(['delivery-1', 'delivery-2']),
    } as unknown as jest.Mocked<NotificationsRepository>;
    const queue = {
      retryRetained: jest.fn().mockResolvedValueOnce(true).mockResolvedValueOnce(false),
    } as unknown as jest.Mocked<EmailSendQueue>;
    const service = new EmailDeliveryRecoveryService(repository, queue);
    const now = new Date('2026-07-27T10:00:00.000Z');

    await expect(service.runRecovery(now)).resolves.toBe(1);

    expect(repository.listStaleSendingEmailDeliveryIds).toHaveBeenCalledWith(
      new Date(now.getTime() - EMAIL_SENDING_LEASE_MS),
      EMAIL_RECOVERY_BATCH_SIZE,
    );
    expect(queue.retryRetained).toHaveBeenNthCalledWith(1, 'delivery-1');
    expect(queue.retryRetained).toHaveBeenNthCalledWith(2, 'delivery-2');
  });

  it('continues the recovery batch when one retained job fails', async () => {
    const repository = {
      listStaleSendingEmailDeliveryIds: jest.fn().mockResolvedValue(['blocked', 'recoverable']),
    } as unknown as jest.Mocked<NotificationsRepository>;
    const queue = {
      retryRetained: jest
        .fn()
        .mockRejectedValueOnce(new Error('unexpected job state'))
        .mockResolvedValueOnce(true),
    } as unknown as jest.Mocked<EmailSendQueue>;
    const service = new EmailDeliveryRecoveryService(repository, queue);

    await expect(service.runRecovery(new Date('2026-07-27T10:00:00.000Z'))).resolves.toBe(1);

    expect(queue.retryRetained).toHaveBeenNthCalledWith(1, 'blocked');
    expect(queue.retryRetained).toHaveBeenNthCalledWith(2, 'recoverable');
  });
});
