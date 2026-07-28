import type { FactoryProvider } from '@nestjs/common';
import { Logger } from '@nestjs/common';
import * as amqplib from 'amqplib';
import { NestRabbitMqModule } from './rabbitmq.module';
import { RABBITMQ_CONNECTION } from './rabbitmq.tokens';

jest.mock('amqplib', () => ({ connect: jest.fn() }));

describe('NestRabbitMqModule', () => {
  beforeEach(() => {
    jest.spyOn(Logger.prototype, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('attaches an error listener to the connection so socket errors do not crash the process', async () => {
    const conn = { on: jest.fn() };
    (amqplib.connect as jest.Mock).mockResolvedValue(conn);

    const dynamicModule = NestRabbitMqModule.forRoot({
      url: 'amqp://localhost',
      exchange: 'vietride.events',
    });
    const provider = dynamicModule.providers?.find(
      (p): p is FactoryProvider => typeof p === 'object' && 'provide' in p && p.provide === RABBITMQ_CONNECTION,
    );
    expect(provider).toBeDefined();

    const connection = await provider?.useFactory?.();

    expect(connection).toBe(conn);
    const errorListener = conn.on.mock.calls.find(([event]) => event === 'error')?.[1] as
      | ((err: Error) => void)
      | undefined;
    expect(errorListener).toBeDefined();
    expect(() => errorListener?.(new Error('socket reset by broker'))).not.toThrow();
  });
});
