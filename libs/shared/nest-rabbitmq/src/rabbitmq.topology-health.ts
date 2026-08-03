import { Injectable } from '@nestjs/common';

/**
 * A consumer whose queue/exchange topology the broker refused, so the consumer
 * never started. `error` holds the raw broker message for logs only - callers
 * that expose this over HTTP must not echo it back (see readiness services).
 */
export interface FailedSubscription {
  queue: string;
  routingKey: string;
  error: string;
}

/**
 * Tracks consumers that failed topology assertion. `RabbitMqConsumer` keeps the
 * process alive when the broker rejects a declaration (e.g. 406 inequivalent
 * args after a routing-key rename), which would otherwise leave a queue with no
 * consumer and no outward sign of trouble. Readiness reads this registry so the
 * degraded state is visible instead of silent.
 */
@Injectable()
export class RabbitMqTopologyHealth {
  private readonly failures = new Map<string, FailedSubscription>();

  record(failure: FailedSubscription): void {
    this.failures.set(failure.queue, failure);
  }

  clear(queue: string): void {
    this.failures.delete(queue);
  }

  list(): FailedSubscription[] {
    return [...this.failures.values()];
  }

  get isHealthy(): boolean {
    return this.failures.size === 0;
  }
}
