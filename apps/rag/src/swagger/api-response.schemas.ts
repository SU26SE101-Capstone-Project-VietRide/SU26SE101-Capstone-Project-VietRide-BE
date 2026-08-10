import type { SchemaObject } from '@nestjs/swagger/dist/interfaces/open-api-spec.interface';

const metaSchema: SchemaObject = {
  type: 'object',
  required: ['traceId', 'timestamp'],
  properties: {
    traceId: { type: 'string', example: 'req_01HZY7B9Q6Y8Y4J4XJ4Z6X9YQ8' },
    timestamp: { type: 'string', format: 'date-time', example: '2026-06-24T17:00:00.000+07:00' },
  },
};

const fieldErrorSchema: SchemaObject = {
  type: 'object',
  required: ['field', 'message'],
  properties: {
    field: { type: 'string', example: 'message' },
    message: { type: 'string', example: 'String must contain at least 1 character(s)' },
  },
};

export function successEnvelopeSchema(statusCode: number, dataSchema: SchemaObject): SchemaObject {
  return {
    type: 'object',
    required: ['success', 'statusCode', 'data', 'meta'],
    properties: {
      success: { type: 'boolean', enum: [true], example: true },
      statusCode: { type: 'integer', example: statusCode },
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
      success: { type: 'boolean', enum: [false], example: false },
      statusCode: { type: 'integer', example: statusCode },
      error: {
        type: 'object',
        required: ['code', 'message'],
        properties: {
          code: { type: 'string', example: code },
          message: { type: 'string', example: message },
          ...(options.fields
            ? {
                fields: {
                  type: 'array',
                  items: fieldErrorSchema,
                  example: [{ field: 'message', message: 'String must contain at least 1 character(s)' }],
                },
              }
            : {}),
        },
      },
      meta: metaSchema,
    },
  };
}

export const healthSchema: SchemaObject = {
  type: 'object',
  required: ['status', 'service'],
  properties: {
    status: { type: 'string', example: 'ok' },
    service: { type: 'string', example: 'rag' },
  },
};

export const readinessOkSchema: SchemaObject = {
  type: 'object',
  required: ['status', 'service', 'dependencies'],
  properties: {
    status: { type: 'string', example: 'ok' },
    service: { type: 'string', example: 'rag' },
    dependencies: {
      type: 'object',
      required: ['prisma', 'redis', 'rabbitmq', 'cloudinary', 'openrouter'],
      properties: {
        prisma: { type: 'string', example: 'ok' },
        redis: { type: 'string', example: 'ok' },
        rabbitmq: { type: 'string', example: 'ok' },
        cloudinary: { type: 'string', example: 'ok' },
        openrouter: { type: 'string', example: 'ok' },
      },
    },
  },
};

export const pagedDataSchema: SchemaObject = {
  type: 'object',
  required: ['items', 'page', 'pageSize', 'totalItems', 'totalPages', 'hasNextPage', 'hasPreviousPage'],
  properties: {
    items: { type: 'array', items: {} },
    page: { type: 'integer', example: 1 },
    pageSize: { type: 'integer', example: 20 },
    totalItems: { type: 'integer', example: 0 },
    totalPages: { type: 'integer', example: 0 },
    hasNextPage: { type: 'boolean', example: false },
    hasPreviousPage: { type: 'boolean', example: false },
  },
};

export const internalAuthHeaderSchema = {
  name: 'X-Internal-Auth',
  required: true,
  description: 'Internal Gateway JWT header. Format: Bearer <token>.',
  schema: { type: 'string', example: 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...' },
} as const;
