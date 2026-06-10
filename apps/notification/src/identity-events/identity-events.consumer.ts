import { Injectable, Logger, OnModuleInit } from '@nestjs/common';
import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
  IdentityOperatorApprovedEventSchema,
  IdentityOperatorSuspendedEventSchema,
  IdentityUserCreatedEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';

/**
 * Subscribes to the three Identity integration events on the `vietride.events`
 * topic exchange. Each event gets its own durable queue bound to the matching
 * routing key. Handlers validate the bare JSON payload against the shared Zod
 * contract and log a concise line; a validation failure throws so the shared
 * consumer nacks (no-requeue).
 *
 * No persistence yet — notification has no DB, so these are placeholder logs.
 */
@Injectable()
export class IdentityEventsConsumer implements OnModuleInit {
  private readonly logger = new Logger(IdentityEventsConsumer.name);

  constructor(private readonly consumer: RabbitMqConsumer) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      'notification.identity.user.created',
      IDENTITY_USER_CREATED_ROUTING_KEY,
      (payload) => {
        const event = IdentityUserCreatedEventSchema.parse(payload);
        this.logger.log(
          `Consumed ${IDENTITY_USER_CREATED_ROUTING_KEY} userId=${event.userId} role=${event.role}`,
        );
      },
    );

    await this.consumer.subscribe(
      'notification.identity.operator.approved',
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      (payload) => {
        const event = IdentityOperatorApprovedEventSchema.parse(payload);
        this.logger.log(
          `Consumed ${IDENTITY_OPERATOR_APPROVED_ROUTING_KEY} operatorId=${event.operatorId}`,
        );
      },
    );

    await this.consumer.subscribe(
      'notification.identity.operator.suspended',
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      (payload) => {
        const event = IdentityOperatorSuspendedEventSchema.parse(payload);
        this.logger.log(
          `Consumed ${IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY} operatorId=${event.operatorId}`,
        );
      },
    );
  }
}
