import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { EmailTemplateKey, NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import {
  OPERATOR_RECIPIENT_PROVIDER,
  BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
  SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
} from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const PARCEL_ID = '44444444-4444-4444-8444-444444444444';
const VOUCHER_ID = '55555555-5555-4555-8555-555555555555';
const MESSAGE_ID = 'phase-6-message-1';
const INVOICE_ID = '66666666-6666-4666-8666-666666666666';
const TRIP_ID = '77777777-7777-4777-8777-777777777777';
const PARCEL_EVENT_ID = '88888888-8888-4888-8888-888888888888';
const RECIPIENT_EMAIL = 'operator-admin@vietride.local';

describe('ParcelSubscriptionOperatorEventsConsumer', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let operatorRecipientProvider: jest.Mocked<OperatorRecipientProvider>;
  let consumer: ParcelSubscriptionOperatorEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = {
      subscribe: jest.fn(),
    } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notificationsService = {
      createNotification: jest.fn(),
      enqueueEmail: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    operatorRecipientProvider = {
      resolveOperatorRecipientUserIds: jest.fn(),
      resolveOperatorRecipientEmails: jest.fn(),
    };
    consumer = new ParcelSubscriptionOperatorEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
      operatorRecipientProvider,
    );
  });

  it('rejects Sprint 4 Parcel producer-consumer schema drift before persistence', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    await expect(
      consumer.handle(
        PARCEL_LOADED_ROUTING_KEY,
        { parcelId: 'invalid' },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('subscribes all phase 6 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(
      PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS.length,
    );
    for (const binding of PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }
  });

  it('creates parcel notification for a new valid message', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.PARCEL_LOADED),
    );

    await consumer.handle(
      PARCEL_LOADED_ROUTING_KEY,
      parcelLoadedPayload(),
      createMessage(PARCEL_EVENT_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_LOADED,
        dedupeKey: `${PARCEL_LOADED_ROUTING_KEY}:${PARCEL_EVENT_ID}:${USER_ID}:${NotificationType.PARCEL_LOADED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      PARCEL_LOADED_ROUTING_KEY,
      PARCEL_EVENT_ID,
    );
  });

  it('uses operator recipient provider when payload only has operatorId', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED),
    );

    await consumer.handle(
      SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
      {
        operatorId: OPERATOR_ID,
        planName: 'Starter',
      },
      createMessage(MESSAGE_ID),
    );

    expect(operatorRecipientProvider.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(
      OPERATOR_ID,
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
        dedupeKey: `${SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED}`,
      }),
    );
  });

  it('creates voucher consent notification for operator recipients', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.VOUCHER_CONSENT_ACCEPTED),
    );

    await consumer.handle(
      BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY,
      {
        operatorId: OPERATOR_ID,
        voucherId: VOUCHER_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.VOUCHER_CONSENT_ACCEPTED,
        dedupeKey:
          `${BOOKING_VOUCHER_CONSENT_ACCEPTED_ROUTING_KEY}:` +
          `${MESSAGE_ID}:${USER_ID}:${NotificationType.VOUCHER_CONSENT_ACCEPTED}`,
      }),
    );
  });

  it('marks empty operator recipients as processed without DLQ', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([]);

    await expect(
      consumer.handle(
        SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
        {
          operatorId: OPERATOR_ID,
          planName: 'Starter',
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
      MESSAGE_ID,
    );
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('creates dedicated invoice in-app/push data and one deduplicated email using invoiceWebUrl', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    (operatorRecipientProvider.resolveOperatorRecipientEmails as jest.Mock).mockResolvedValue([
      { userId: USER_ID, email: RECIPIENT_EMAIL },
    ]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.INVOICE_ISSUED),
    );
    notificationsService.enqueueEmail.mockResolvedValue({
      id: '77777777-7777-4777-8777-777777777777',
      toEmail: RECIPIENT_EMAIL,
      templateKey: EmailTemplateKey.INVOICE_NOTICE,
      status: 'PENDING',
      createdAt: '2026-07-14T00:00:00.000Z',
    });

    const invoiceWebUrl = `https://operator.vietride.vn/invoices/${INVOICE_ID}`;
    const downloadApiUrl = `https://api.vietride.vn/v1/operator/invoices/${INVOICE_ID}/download`;
    await consumer.handle(
      INVOICE_ISSUED_ROUTING_KEY,
      {
        invoiceId: INVOICE_ID,
        invoiceNumber: 'VR-INV-202607-000001',
        operatorId: OPERATOR_ID,
        amount: '1200000',
        invoiceWebUrl,
        downloadApiUrl,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.INVOICE_ISSUED,
        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
        data: expect.objectContaining({ invoiceWebUrl }),
      }),
    );
    expect(notificationsService.createNotification.mock.calls[0]?.[0].data).not.toHaveProperty(
      'downloadApiUrl',
    );
    expect(notificationsService.enqueueEmail).toHaveBeenCalledWith({
      notificationId: '99999999-9999-4999-8999-999999999999',
      dedupeKey: `${INVOICE_ISSUED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:email`,
      toEmail: RECIPIENT_EMAIL,
      templateKey: EmailTemplateKey.INVOICE_NOTICE,
      templateData: {
        invoiceNumber: 'VR-INV-202607-000001',
        amountVnd: '1200000',
        invoiceUrl: invoiceWebUrl,
      },
    });
    expect(operatorRecipientProvider.resolveOperatorRecipientEmails).toHaveBeenCalledWith(
      OPERATOR_ID,
      [USER_ID],
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(INVOICE_ISSUED_ROUTING_KEY, MESSAGE_ID);
  });

  it('drops an invoice recipient that is no longer an active admin in the same tenant', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    (operatorRecipientProvider.resolveOperatorRecipientEmails as jest.Mock).mockResolvedValue([]);

    await consumer.handle(
      INVOICE_ISSUED_ROUTING_KEY,
      {
        invoiceId: INVOICE_ID,
        invoiceNumber: 'VR-INV-202607-000001',
        operatorId: OPERATOR_ID,
        amount: '1200000',
        invoiceWebUrl: `https://operator.vietride.vn/invoices/${INVOICE_ID}`,
        downloadApiUrl: `https://api.vietride.vn/v1/operator/invoices/${INVOICE_ID}/download`,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(notificationsService.enqueueEmail).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(INVOICE_ISSUED_ROUTING_KEY, MESSAGE_ID);
  });

  it('skips duplicate message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      PARCEL_LOADED_ROUTING_KEY,
      {
        userId: USER_ID,
        parcelId: PARCEL_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('uses canonical payload eventId across transport republish', async () => {
    const eventId = '88888888-8888-4888-8888-888888888888';
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      INVOICE_ISSUED_ROUTING_KEY,
      {
        eventId,
        invoiceId: INVOICE_ID,
        invoiceNumber: 'VR-INV-202607-000001',
        operatorId: OPERATOR_ID,
        amount: '1200000',
        invoiceWebUrl: `https://operator.vietride.vn/invoices/${INVOICE_ID}`,
        downloadApiUrl: `https://api.vietride.vn/v1/operator/invoices/${INVOICE_ID}/download`,
      },
      createMessage('different-outbox-row-id'),
    );

    expect(idempotency.begin).toHaveBeenCalledWith(INVOICE_ISSUED_ROUTING_KEY, eventId);
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops malformed payload without rethrowing', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        PARCEL_LOADED_ROUTING_KEY,
        {
          userId: USER_ID,
          parcelId: 'not-a-uuid',
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(PARCEL_LOADED_ROUTING_KEY, MESSAGE_ID);
  });

  it('drops messages without id before idempotency check without poisoning the queue', async () => {
    await expect(
      consumer.handle(
        PARCEL_LOADED_ROUTING_KEY,
        {
          userId: USER_ID,
          parcelId: PARCEL_ID,
        },
        createMessage(undefined),
      ),
    ).resolves.toBeUndefined();

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('releases the processing lock and rethrows transient side-effect failures', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockRejectedValue(new Error('DATABASE_UNAVAILABLE'));

    await expect(
      consumer.handle(
        PARCEL_LOADED_ROUTING_KEY,
        parcelLoadedPayload(),
        createMessage(PARCEL_EVENT_ID),
      ),
    ).rejects.toThrow('DATABASE_UNAVAILABLE');

    expect(idempotency.release).toHaveBeenCalledWith(PARCEL_LOADED_ROUTING_KEY, PARCEL_EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });
});

function createMessage(messageId: string | undefined): ConsumeMessage {
  return {
    properties: {
      messageId,
      correlationId: undefined,
    },
  } as ConsumeMessage;
}

function parcelLoadedPayload(): Record<string, unknown> {
  return {
    eventId: PARCEL_EVENT_ID,
    occurredAt: '2026-07-22T03:00:00Z',
    parcelId: PARCEL_ID,
    tripId: TRIP_ID,
    actualWeightKg: 12.5,
    userIds: [USER_ID],
  };
}

function createNotification(type: NotificationType): {
  id: string;
  userId: string;
  type: NotificationType;
  title: string;
  body: string;
  data: null;
  readAt: null;
  createdAt: string;
} {
  return {
    id: '99999999-9999-4999-8999-999999999999',
    userId: USER_ID,
    type,
    title: 'Title',
    body: 'Body',
    data: null,
    readAt: null,
    createdAt: '2026-06-01T10:00:00.000Z',
  };
}

void OPERATOR_RECIPIENT_PROVIDER;
