import type { SchemaObject } from '@nestjs/swagger/dist/interfaces/open-api-spec.interface';
import { NOTIFICATION_ACTION_TYPES } from '../notifications/notification-action';
import { EmailTemplateKey } from '../generated/notification-prisma-client';

const metaSchema: SchemaObject = {
  type: 'object',
  required: ['traceId', 'timestamp'],
  properties: {
    traceId: {
      type: 'string',
      example: 'req_01HZY7B9Q6Y8Y4J4XJ4Z6X9YQ8',
    },
    timestamp: {
      type: 'string',
      format: 'date-time',
      example: '2026-06-17T16:30:00.000+07:00',
    },
  },
};

const fieldErrorSchema: SchemaObject = {
  type: 'object',
  required: ['field', 'message'],
  properties: {
    field: {
      type: 'string',
      example: 'pageSize',
    },
    message: {
      type: 'string',
      example: 'Number must be less than or equal to 100',
    },
  },
};

export function successEnvelopeSchema(statusCode: number, dataSchema: SchemaObject): SchemaObject {
  return {
    type: 'object',
    required: ['success', 'statusCode', 'data', 'meta'],
    properties: {
      success: {
        type: 'boolean',
        enum: [true],
        example: true,
      },
      statusCode: {
        type: 'integer',
        example: statusCode,
      },
      data: dataSchema,
      meta: metaSchema,
    },
  };
}

export function errorEnvelopeSchema(
  statusCode: number,
  code: string,
  message: string,
  options: { fields?: boolean } = {},
): SchemaObject {
  return {
    type: 'object',
    required: ['success', 'statusCode', 'error', 'meta'],
    properties: {
      success: {
        type: 'boolean',
        enum: [false],
        example: false,
      },
      statusCode: {
        type: 'integer',
        example: statusCode,
      },
      error: {
        type: 'object',
        required: ['code', 'message'],
        properties: {
          code: {
            type: 'string',
            example: code,
          },
          message: {
            type: 'string',
            example: message,
          },
          ...(options.fields
            ? {
                fields: {
                  type: 'array',
                  items: fieldErrorSchema,
                  example: [
                    {
                      field: 'pageSize',
                      message: 'Number must be less than or equal to 100',
                    },
                  ],
                },
              }
            : {}),
        },
      },
      meta: metaSchema,
    },
  };
}

const notificationActionSchema: SchemaObject = {
  type: 'object',
  required: ['type', 'params'],
  properties: {
    type: {
      type: 'string',
      enum: [...NOTIFICATION_ACTION_TYPES],
      example: 'OPEN_BOOKING_DETAIL',
    },
    params: {
      type: 'object',
      additionalProperties: false,
      properties: {
        bookingId: { type: 'string', format: 'uuid' },
        tripId: { type: 'string', format: 'uuid' },
        parcelId: { type: 'string', format: 'uuid' },
        shuttleTripId: { type: 'string', format: 'uuid' },
      },
      example: { bookingId: '22222222-2222-4222-8222-222222222222' },
    },
  },
};

export const notificationItemSchema: SchemaObject = {
  type: 'object',
  required: ['id', 'userId', 'type', 'title', 'body', 'data', 'action', 'readAt', 'createdAt'],
  properties: {
    id: {
      type: 'string',
      format: 'uuid',
      example: '7e7d44b8-3d84-4dd5-b0a2-1f445de7c701',
    },
    userId: {
      type: 'string',
      format: 'uuid',
      example: '11111111-1111-4111-8111-111111111111',
    },
    type: {
      type: 'string',
      example: 'BOOKING_CONFIRMED',
    },
    title: {
      type: 'string',
      example: 'Dat ve thanh cong',
    },
    body: {
      type: 'string',
      example: 'Ve #VR-1024 da duoc xac nhan.',
    },
    data: {
      type: 'object',
      nullable: true,
      additionalProperties: true,
      example: {
        bookingId: '22222222-2222-4222-8222-222222222222',
        bookingCode: 'VR-1024',
      },
    },
    action: notificationActionSchema,
    readAt: {
      type: 'string',
      format: 'date-time',
      nullable: true,
      example: null,
    },
    createdAt: {
      type: 'string',
      format: 'date-time',
      example: '2026-06-17T16:20:00.000+07:00',
    },
  },
};

export const pagedNotificationsSchema: SchemaObject = {
  type: 'object',
  required: ['items', 'page', 'pageSize', 'totalItems', 'totalPages', 'hasNextPage', 'hasPreviousPage', 'nextCursor'],
  properties: {
    items: {
      type: 'array',
      items: notificationItemSchema,
    },
    page: {
      type: 'integer',
      example: 1,
    },
    pageSize: {
      type: 'integer',
      example: 20,
    },
    totalItems: {
      type: 'integer',
      example: 42,
    },
    totalPages: {
      type: 'integer',
      example: 3,
    },
    hasNextPage: {
      type: 'boolean',
      example: true,
    },
    hasPreviousPage: {
      type: 'boolean',
      example: false,
    },
    nextCursor: {
      type: 'string',
      nullable: true,
      description: 'Opaque snapshot continuation cursor.',
    },
  },
};

export const createEmailSendBodySchema: SchemaObject = {
  type: 'object',
  required: ['toEmail', 'templateKey', 'templateData'],
  properties: {
    notificationId: {
      type: 'string',
      format: 'uuid',
      nullable: true,
      example: '7e7d44b8-3d84-4dd5-b0a2-1f445de7c701',
    },
    toEmail: {
      type: 'string',
      format: 'email',
      example: 'operator@example.com',
    },
    templateKey: {
      type: 'string',
      enum: Object.values(EmailTemplateKey),
      example: EmailTemplateKey.OPERATOR_SUBSCRIPTION_NOTICE,
    },
    templateData: {
      type: 'object',
      additionalProperties: true,
      example: {
        operatorName: 'VietRide Express',
        planName: 'Starter',
      },
    },
  },
};

export const emailDeliverySchema: SchemaObject = {
  type: 'object',
  required: ['id', 'toEmail', 'templateKey', 'status', 'createdAt'],
  properties: {
    id: {
      type: 'string',
      format: 'uuid',
      example: '3a64c7a7-b320-496a-a2f9-96b0248a9735',
    },
    toEmail: {
      type: 'string',
      format: 'email',
      example: 'operator@example.com',
    },
    templateKey: {
      type: 'string',
      example: EmailTemplateKey.OPERATOR_SUBSCRIPTION_NOTICE,
    },
    status: {
      type: 'string',
      example: 'PENDING',
    },
    createdAt: {
      type: 'string',
      format: 'date-time',
      example: '2026-06-17T16:25:00.000+07:00',
    },
  },
};

export const healthSchema: SchemaObject = {
  type: 'object',
  required: ['status', 'service'],
  properties: {
    status: {
      type: 'string',
      example: 'ok',
    },
    service: {
      type: 'string',
      example: 'notification',
    },
  },
};

export const readinessSchema: SchemaObject = {
  type: 'object',
  required: ['status', 'service', 'dependencies'],
  properties: {
    status: {
      type: 'string',
      example: 'ok',
    },
    service: {
      type: 'string',
      example: 'notification',
    },
    dependencies: {
      type: 'object',
      required: ['prisma', 'redis', 'rabbitmq'],
      properties: {
        prisma: { type: 'string', example: 'ok' },
        redis: { type: 'string', example: 'ok' },
        rabbitmq: { type: 'string', example: 'ok' },
      },
    },
  },
};

export const defaultResponseSchema: SchemaObject = {
  type: 'object',
  required: ['message'],
  properties: {
    message: {
      type: 'string',
      example: 'Hello API',
    },
  },
};
