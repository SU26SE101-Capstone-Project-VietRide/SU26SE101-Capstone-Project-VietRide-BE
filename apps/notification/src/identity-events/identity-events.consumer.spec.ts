import { Logger } from '@nestjs/common';
import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_OTP_REQUESTED_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
} from '@vietride/contracts';
import type { RabbitMqConsumer, RabbitMqHandler } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { EmailTemplateKey, NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from '../notifications/message-idempotency.service';
import { NotificationsService } from '../notifications/notifications.service';
import type { OperatorRecipientProvider } from '../notifications/operator-recipient.provider';
import { IdentitySystemAdminRecipientProvider } from '../notifications/identity-system-admin-recipient.provider';
import { IdentityEventsConsumer } from './identity-events.consumer';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '22222222-2222-4222-8222-222222222222';
const MESSAGE_ID = 'identity-message-1';
const EVENT_ID = '33333333-3333-4333-8333-333333333333';

describe('IdentityEventsConsumer', () => {
  let handlers: Record<string, RabbitMqHandler>;
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let operatorRecipientProvider: jest.Mocked<OperatorRecipientProvider>;
  let systemAdminRecipientProvider: jest.Mocked<IdentitySystemAdminRecipientProvider>;
  let consumer: IdentityEventsConsumer;

  beforeEach(async () => {
    handlers = {};
    rabbitConsumer = {
      subscribe: jest.fn((_queue: string, routingKey: string, handler: RabbitMqHandler) => {
        handlers[routingKey] = handler;
        return Promise.resolve();
      }),
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
    };
    systemAdminRecipientProvider = {
      resolveSystemAdminRecipientUserIds: jest.fn(),
    } as unknown as jest.Mocked<IdentitySystemAdminRecipientProvider>;

    jest.spyOn(Logger.prototype, 'log').mockImplementation(() => undefined);
    jest.spyOn(Logger.prototype, 'warn').mockImplementation(() => undefined);

    consumer = new IdentityEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
      operatorRecipientProvider,
      systemAdminRecipientProvider,
    );
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
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      IDENTITY_OTP_REQUESTED_ROUTING_KEY,
      IDENTITY_USER_CREATED_ROUTING_KEY,
    ]);
    expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
      'notification.identity.operator.approved',
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });

  it('notifies each System Admin when an operator registration is submitted', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    systemAdminRecipientProvider.resolveSystemAdminRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.OPERATOR_REGISTRATION_SUBMITTED),
    );

    await consumer.handleOperatorRegistrationSubmitted(
      {
        eventId: EVENT_ID,
        occurredAt: '2026-07-27T08:30:00+07:00',
        operatorId: OPERATOR_ID,
        companyName: 'Nhà xe Việt Ride',
      },
      createMessage(MESSAGE_ID),
    );

    expect(systemAdminRecipientProvider.resolveSystemAdminRecipientUserIds).toHaveBeenCalled();
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.OPERATOR_REGISTRATION_SUBMITTED,
        dedupeKey: `${IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.OPERATOR_REGISTRATION_SUBMITTED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('releases registration lock when System Admin lookup fails', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    systemAdminRecipientProvider.resolveSystemAdminRecipientUserIds.mockRejectedValue(
      new Error('identity unavailable'),
    );

    await expect(
      consumer.handleOperatorRegistrationSubmitted(
        {
          eventId: EVENT_ID,
          occurredAt: '2026-07-27T08:30:00+07:00',
          operatorId: OPERATOR_ID,
          companyName: 'Nhà xe Việt Ride',
        },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('identity unavailable');
    expect(idempotency.release).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('validates and logs identity.user.created without creating notification', () => {
    expect(() =>
      handlerFor(IDENTITY_USER_CREATED_ROUTING_KEY)(
        {
          userId: USER_ID,
          role: 'PASSENGER',
          email: 'rider@example.com',
          createdAt: '2026-06-10T08:30:00+07:00',
        },
        createMessage(undefined),
      ),
    ).not.toThrow();

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('creates operator approved notification for resolved operator admins', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.OPERATOR_APPROVED),
    );

    await consumer.handleOperatorApproved(
      {
        eventId: EVENT_ID,
        operatorId: OPERATOR_ID,
        approvedAt: '2026-06-10T08:30:00+07:00',
      },
      createMessage(MESSAGE_ID),
    );

    expect(operatorRecipientProvider.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(
      OPERATOR_ID,
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.OPERATOR_APPROVED,
        data: { operatorId: OPERATOR_ID, approvedAt: '2026-06-10T08:30:00+07:00' },
        dedupeKey: `${IDENTITY_OPERATOR_APPROVED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.OPERATOR_APPROVED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('creates operator suspended notification for resolved operator admins', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.OPERATOR_SUSPENDED),
    );

    await consumer.handleOperatorSuspended(
      {
        operatorId: OPERATOR_ID,
        suspendedAt: '2026-06-10T08:30:00+07:00',
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.OPERATOR_SUSPENDED,
        data: { operatorId: OPERATOR_ID, suspendedAt: '2026-06-10T08:30:00+07:00' },
        dedupeKey: `${IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.OPERATOR_SUSPENDED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('skips duplicate operator lifecycle message', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handleOperatorApproved(
      {
        operatorId: OPERATOR_ID,
        approvedAt: '2026-06-10T08:30:00+07:00',
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('marks empty operator recipients as processed without DLQ', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([]);

    await expect(
      consumer.handleOperatorApproved(
        {
          eventId: EVENT_ID,
          operatorId: OPERATOR_ID,
          approvedAt: '2026-06-10T08:30:00+07:00',
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      MESSAGE_ID,
    );
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('releases idempotency and rethrows provider errors', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockRejectedValue(
      new Error('identity down'),
    );

    await expect(
      consumer.handleOperatorApproved(
        {
          eventId: EVENT_ID,
          operatorId: OPERATOR_ID,
          approvedAt: '2026-06-10T08:30:00+07:00',
        },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('identity down');

    expect(idempotency.release).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('drops malformed operator lifecycle payload after marking processed', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handleOperatorSuspended({ operatorId: 'not-a-uuid' }, createMessage(MESSAGE_ID)),
    ).resolves.toBeUndefined();

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      MESSAGE_ID,
    );
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('rejects operator lifecycle messages without id before idempotency check', async () => {
    await expect(
      consumer.handleOperatorApproved(
        {
          eventId: EVENT_ID,
          operatorId: OPERATOR_ID,
          approvedAt: '2026-06-10T08:30:00+07:00',
        },
        createMessage(undefined),
      ),
    ).rejects.toThrow('MISSING_MESSAGE_ID');

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  // --- OTP handler tests ---

  it('enqueues OTP email with correct args for a valid identity.otp.requested event', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.enqueueEmail.mockResolvedValue({
      id: 'email-delivery-id',
      toEmail: 'rider@example.com',
      templateKey: EmailTemplateKey.AUTH_OTP,
      status: 'PENDING',
      createdAt: '2026-06-01T10:00:00.000Z',
    });

    await consumer.handleOtpRequested(
      {
        userId: USER_ID,
        email: 'rider@example.com',
        code: '123456',
        purpose: 'REGISTRATION',
        ttlMinutes: 5,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.enqueueEmail).toHaveBeenCalledWith({
      dedupeKey: `${IDENTITY_OTP_REQUESTED_ROUTING_KEY}:${MESSAGE_ID}:email`,
      toEmail: 'rider@example.com',
      templateKey: EmailTemplateKey.AUTH_OTP,
      templateData: { code: '123456', purpose: 'REGISTRATION', ttlMinutes: 5 },
    });
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OTP_REQUESTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('skips duplicate OTP message', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handleOtpRequested(
      {
        userId: USER_ID,
        email: 'rider@example.com',
        code: '123456',
        purpose: 'REGISTRATION',
        ttlMinutes: 5,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.enqueueEmail).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('throws when OTP message is missing messageId', async () => {
    await expect(
      consumer.handleOtpRequested(
        {
          userId: USER_ID,
          email: 'rider@example.com',
          code: '123456',
          purpose: 'REGISTRATION',
          ttlMinutes: 5,
        },
        createMessage(undefined),
      ),
    ).rejects.toThrow(`MISSING_MESSAGE_ID_${IDENTITY_OTP_REQUESTED_ROUTING_KEY}`);

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.enqueueEmail).not.toHaveBeenCalled();
  });

  it('throws without releasing idempotency when locked OTP message arrives', async () => {
    idempotency.begin.mockResolvedValue('locked');

    await expect(
      consumer.handleOtpRequested(
        {
          userId: USER_ID,
          email: 'rider@example.com',
          code: '123456',
          purpose: 'REGISTRATION',
          ttlMinutes: 5,
        },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow(`MESSAGE_LOCKED_${IDENTITY_OTP_REQUESTED_ROUTING_KEY}_${MESSAGE_ID}`);

    expect(notificationsService.enqueueEmail).not.toHaveBeenCalled();
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('drops malformed OTP payload after marking processed', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handleOtpRequested(
        {
          userId: 'not-a-uuid',
          email: 'bad',
          code: '123456',
          purpose: 'REGISTRATION',
          ttlMinutes: 5,
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();

    expect(notificationsService.enqueueEmail).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      IDENTITY_OTP_REQUESTED_ROUTING_KEY,
      MESSAGE_ID,
    );
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('releases idempotency and rethrows enqueueEmail errors', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.enqueueEmail.mockRejectedValue(new Error('sendgrid down'));

    await expect(
      consumer.handleOtpRequested(
        {
          userId: USER_ID,
          email: 'rider@example.com',
          code: '123456',
          purpose: 'REGISTRATION',
          ttlMinutes: 5,
        },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('sendgrid down');

    expect(idempotency.release).toHaveBeenCalledWith(
      IDENTITY_OTP_REQUESTED_ROUTING_KEY,
      MESSAGE_ID,
    );
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

function createNotification(
  type: NotificationType,
): Awaited<ReturnType<NotificationsService['createNotification']>> {
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
