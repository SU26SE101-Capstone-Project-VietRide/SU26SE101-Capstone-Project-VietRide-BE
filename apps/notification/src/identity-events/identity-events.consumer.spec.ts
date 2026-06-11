import { Logger } from '@nestjs/common';
import type { RabbitMqConsumer, RabbitMqHandler } from '@vietride/nest-rabbitmq';
import { IdentityEventsConsumer } from './identity-events.consumer';

describe('IdentityEventsConsumer', () => {
  let handlers: Record<string, RabbitMqHandler>;
  let consumer: IdentityEventsConsumer;

  beforeEach(async () => {
    handlers = {};
    const fakeConsumer = {
      subscribe: jest.fn((_queue: string, routingKey: string, handler: RabbitMqHandler) => {
        handlers[routingKey] = handler;
        return Promise.resolve();
      }),
    } as unknown as RabbitMqConsumer;

    jest.spyOn(Logger.prototype, 'log').mockImplementation(() => undefined);

    consumer = new IdentityEventsConsumer(fakeConsumer);
    await consumer.onModuleInit();
  });

  afterEach(() => jest.restoreAllMocks());

  const handlerFor = (routingKey: string): RabbitMqHandler => {
    const handler = handlers[routingKey];
    if (!handler) throw new Error(`No handler registered for ${routingKey}`);
    return handler;
  };

  it('subscribes one durable queue per identity routing key', () => {
    expect(Object.keys(handlers).sort()).toEqual([
      'identity.operator.approved',
      'identity.operator.suspended',
      'identity.user.created',
    ]);
  });

  it('validates and logs a well-formed identity.user.created payload', () => {
    const logSpy = jest.spyOn(Logger.prototype, 'log');
    expect(() =>
      handlerFor('identity.user.created')(
        {
          userId: '11111111-1111-1111-1111-111111111111',
          role: 'PASSENGER',
          email: 'rider@example.com',
          createdAt: '2026-06-10T08:30:00+07:00',
        },
        {} as never,
      ),
    ).not.toThrow();
    expect(logSpy).toHaveBeenCalledWith(expect.stringContaining('Consumed identity.user.created'));
  });

  it('throws on a malformed identity.user.created payload so the consumer nacks', async () => {
    expect(() =>
      handlerFor('identity.user.created')(
        { userId: 'not-a-uuid', role: 'passenger', email: 'bad', createdAt: 'nope' },
        {} as never,
      ),
    ).toThrow();
  });

  it('validates a well-formed identity.operator.approved payload', () => {
    expect(() =>
      handlerFor('identity.operator.approved')(
        {
          operatorId: '22222222-2222-2222-2222-222222222222',
          approvedAt: '2026-06-10T08:30:00+07:00',
        },
        {} as never,
      ),
    ).not.toThrow();
  });

  it('throws on a malformed identity.operator.suspended payload', async () => {
    expect(() =>
      handlerFor('identity.operator.suspended')({ operatorId: 'not-a-uuid' }, {} as never),
    ).toThrow();
  });
});
