export const RABBITMQ_CONNECTION = Symbol('RABBITMQ_CONNECTION');
export const RABBITMQ_OPTIONS = Symbol('RABBITMQ_OPTIONS');

export interface NestRabbitMqOptions {
  url: string;
  exchange: string;
  exchangeType?: 'topic' | 'direct' | 'fanout' | 'headers';
}
