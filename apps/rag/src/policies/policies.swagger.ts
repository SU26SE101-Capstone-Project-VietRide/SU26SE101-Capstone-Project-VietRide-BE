import type { SchemaObject } from '@nestjs/swagger/dist/interfaces/open-api-spec.interface';
import { errorEnvelopeSchema } from '../swagger/api-response.schemas';

export const policyValidationErrorSchema = errorEnvelopeSchema(
  422,
  'VALIDATION_ERROR',
  'Policy request validation failed',
  { fields: true },
);

export const policyMutation422Schema: SchemaObject = {
  oneOf: [
    policyValidationErrorSchema,
    errorEnvelopeSchema(422, 'IDEMPOTENCY_KEY_REQUIRED', 'Idempotency-Key is required'),
    errorEnvelopeSchema(422, 'IDEMPOTENCY_KEY_MISMATCH', 'Idempotency-Key was reused'),
  ],
};

export const policyMutation409Schema: SchemaObject = {
  oneOf: [
    errorEnvelopeSchema(409, 'POLICY_VERSION_CONFLICT', 'Policy version conflict'),
    errorEnvelopeSchema(409, 'IDEMPOTENCY_REQUEST_PENDING', 'Request is still processing'),
  ],
};

export const policySchema: SchemaObject = {
  type: 'object',
  required: [
    'id',
    'operatorId',
    'title',
    'description',
    'content',
    'policyType',
    'category',
    'version',
    'active',
    'createdBy',
    'createdAt',
    'updatedAt',
  ],
  properties: {
    id: { type: 'string', format: 'uuid' },
    operatorId: { type: 'string', format: 'uuid', nullable: true },
    title: { type: 'string' },
    description: { type: 'string' },
    content: { type: 'string' },
    policyType: { type: 'string', enum: ['FOR_OPERATOR', 'FOR_USER'] },
    category: { type: 'string' },
    version: { type: 'integer', minimum: 1 },
    active: { type: 'boolean' },
    createdBy: {
      type: 'object',
      required: ['userId', 'displayName', 'email'],
      properties: {
        userId: { type: 'string', format: 'uuid' },
        displayName: { type: 'string' },
        email: { type: 'string', format: 'email' },
      },
    },
    createdAt: { type: 'string', format: 'date-time' },
    updatedAt: { type: 'string', format: 'date-time' },
  },
};

export const publishedPolicySchema: SchemaObject = {
  type: 'object',
  required: [
    'id',
    'operatorId',
    'title',
    'description',
    'content',
    'category',
    'version',
    'createdAt',
    'updatedAt',
  ],
  properties: {
    id: { type: 'string', format: 'uuid' },
    operatorId: { type: 'string', format: 'uuid', nullable: true },
    title: { type: 'string' },
    description: { type: 'string' },
    content: { type: 'string' },
    category: { type: 'string' },
    version: { type: 'integer', minimum: 1 },
    createdAt: { type: 'string', format: 'date-time' },
    updatedAt: { type: 'string', format: 'date-time' },
  },
};

export const policyCreateBodySchema: SchemaObject = {
  type: 'object',
  additionalProperties: false,
  required: ['title', 'description', 'content', 'policyType', 'category', 'active'],
  properties: {
    title: { type: 'string', minLength: 1 },
    description: { type: 'string', minLength: 1 },
    content: { type: 'string', minLength: 1 },
    policyType: { type: 'string', enum: ['FOR_OPERATOR', 'FOR_USER'] },
    category: { type: 'string', minLength: 1 },
    active: { type: 'boolean' },
  },
};

export const policyUpdateBodySchema: SchemaObject = {
  type: 'object',
  additionalProperties: false,
  required: ['version'],
  anyOf: [
    { required: ['title'] },
    { required: ['description'] },
    { required: ['content'] },
    { required: ['policyType'] },
    { required: ['category'] },
    { required: ['active'] },
  ],
  properties: {
    version: { type: 'integer', minimum: 1 },
    title: { type: 'string', minLength: 1 },
    description: { type: 'string', minLength: 1 },
    content: { type: 'string', minLength: 1 },
    policyType: { type: 'string', enum: ['FOR_OPERATOR', 'FOR_USER'] },
    category: { type: 'string', minLength: 1 },
    active: { type: 'boolean' },
  },
};
