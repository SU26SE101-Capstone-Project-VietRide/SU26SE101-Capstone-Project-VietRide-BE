import type { OutboxDlqQueryDto } from './outbox-dlq-query.dto';
import { OutboxDlqService } from './outbox-dlq.service';
import type { OutboxRepository } from './outbox.repository';

describe('OutboxDlqService', () => {
  it('delegates the validated query to the repository', async () => {
    const query: OutboxDlqQueryDto = {
      eventType: 'TripDelayed',
      pageSize: 25,
      sortDir: 'desc',
    };
    const rows = [
      {
        eventId: '11111111-1111-4111-8111-111111111111',
        eventType: 'TripDelayed',
        payload: { tripId: '22222222-2222-4222-8222-222222222222' },
        retryCount: 6,
        lastError: 'broker unavailable',
        createdAt: new Date('2026-07-18T10:00:00.000Z'),
        terminalAt: new Date('2026-07-18T10:05:00.000Z'),
        id: '33333333-3333-4333-8333-333333333333',
      },
    ];
    const repository = {
      readDlq: jest.fn().mockResolvedValue(rows),
    } as unknown as jest.Mocked<OutboxRepository>;
    const service = new OutboxDlqService(repository);

    await expect(service.list(query)).resolves.toEqual(rows);
    expect(repository.readDlq).toHaveBeenCalledWith(query);
  });
});
