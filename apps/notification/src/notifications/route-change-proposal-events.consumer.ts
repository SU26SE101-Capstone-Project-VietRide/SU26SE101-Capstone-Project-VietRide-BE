import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import type { RouteChangeProposalRoutingKey } from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import {
  ROUTE_CHANGE_PROPOSAL_QUEUE_BINDINGS,
  TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
} from './route-change-proposal-events.constants';
import {
  mapRouteChangeProposalToNotifications,
  parseRouteChangeProposalEvent,
} from './route-change-proposal-notification.mapper';
import { createNotificationLogger } from './notification-logger';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';

@Injectable()
export class RouteChangeProposalEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(RouteChangeProposalEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER)
    private readonly operatorRecipients: OperatorRecipientProvider,
    private readonly tripRecipients?: TripAnnouncementRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      ROUTE_CHANGE_PROPOSAL_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          {
            prefetch: RABBITMQ_PREFETCH_ONE,
            deadLetter: true,
            maxRetries: 5,
            retryDelayMs: 10_000,
          },
        ),
      ),
    );
  }

  async handle(
    routingKey: RouteChangeProposalRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const parsed = parseRouteChangeProposalEvent(routingKey, payload);
    const messageId = parsed.success ? parsed.data.eventId : getBrokerMessageId(raw);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);

    const state = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (state === 'duplicate') return;
    if (state === 'locked') throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);

    try {
      if (!parsed.success) {
        this.logger.warn(
          { routingKey, messageId, issues: parsed.error.issues },
          'Dropping malformed route-change proposal event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      const resolvedRecipientUserIds =
        routingKey === TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY
          ? await this.operatorRecipients.resolveOperatorRecipientUserIds(parsed.data.operatorId)
          : await this.tripRecipients?.resolveTripCrewUserIds(
              parsed.data.tripId,
              parsed.data.operatorId,
            ) ?? [];
      const mapped = mapRouteChangeProposalToNotifications(
        routingKey,
        parsed.data,
        resolvedRecipientUserIds,
      );
      await Promise.all(
        mapped.map((notification) =>
          this.notifications.createNotification({
            ...notification,
            dedupeKey: `${routingKey}:${messageId}:${notification.userId}:${notification.type}`,
          }),
        ),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, notificationCount: mapped.length },
        'Processed route-change proposal notification event',
      );
    } catch (error) {
      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}

function getBrokerMessageId(raw: ConsumeMessage): string | undefined {
  return raw.properties.messageId ?? raw.properties.correlationId ?? undefined;
}
