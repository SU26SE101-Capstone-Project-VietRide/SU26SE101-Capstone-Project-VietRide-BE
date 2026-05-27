import { DynamicModule, Global, Module, Provider } from '@nestjs/common';
import * as amqplib from 'amqplib';
import type { ChannelModel } from 'amqplib';
import { RabbitMqConsumer } from './rabbitmq.consumer';
import { RabbitMqPublisher } from './rabbitmq.publisher';
import { RABBITMQ_CONNECTION, RABBITMQ_OPTIONS, type NestRabbitMqOptions } from './rabbitmq.tokens';

@Global()
@Module({})
export class NestRabbitMqModule {
  static forRoot(opts: NestRabbitMqOptions): DynamicModule {
    const connectionProvider: Provider = {
      provide: RABBITMQ_CONNECTION,
      useFactory: async (): Promise<ChannelModel> => {
        return amqplib.connect(opts.url);
      },
    };
    const optionsProvider: Provider = { provide: RABBITMQ_OPTIONS, useValue: opts };
    return {
      module: NestRabbitMqModule,
      providers: [optionsProvider, connectionProvider, RabbitMqPublisher, RabbitMqConsumer],
      exports: [optionsProvider, connectionProvider, RabbitMqPublisher, RabbitMqConsumer],
    };
  }
}
