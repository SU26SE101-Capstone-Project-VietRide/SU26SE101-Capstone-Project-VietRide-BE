import { outboxDlqQuerySchema } from './outbox-dlq-query.dto';

describe('outboxDlqQuerySchema timestamp contract', () => {
  const afterId = '11111111-1111-4111-8111-111111111111';

  it('normalizes an explicit Vietnam offset to the same UTC instant', () => {
    const result = outboxDlqQuerySchema.parse({
      afterTerminalAt: '2026-08-10T12:00:00+07:00',
      afterId,
    });

    expect(result.afterTerminalAt?.toISOString()).toBe('2026-08-10T05:00:00.000Z');
  });

  it('rejects an offsetless timestamp', () => {
    expect(() => outboxDlqQuerySchema.parse({
      afterTerminalAt: '2026-08-10T12:00:00',
      afterId,
    })).toThrow();
  });
});
