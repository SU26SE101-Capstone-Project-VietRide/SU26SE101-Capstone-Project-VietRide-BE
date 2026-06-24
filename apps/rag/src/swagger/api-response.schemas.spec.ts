import type { SchemaObject } from '@nestjs/swagger/dist/interfaces/open-api-spec.interface';
import { successEnvelopeSchema, errorEnvelopeSchema } from './api-response.schemas';

function props(schema: SchemaObject): NonNullable<SchemaObject['properties']> {
  return schema.properties ?? {};
}

describe('api-response.schemas', () => {
  describe('successEnvelopeSchema', () => {
    it('returns a success envelope with given statusCode and data schema', () => {
      const schema = successEnvelopeSchema(200, { type: 'object', properties: { id: { type: 'string' } } });
      expect(schema).toMatchObject({
        type: 'object',
        required: ['success', 'statusCode', 'data', 'meta'],
      });
      const p = props(schema);
      expect(p.success).toMatchObject({ enum: [true] });
      expect(p.statusCode).toMatchObject({ example: 200 });
      expect(p.data).toMatchObject({ type: 'object' });
    });
  });

  describe('errorEnvelopeSchema', () => {
    it('returns an error envelope with given code and message', () => {
      const schema = errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Bad input');
      expect(schema).toMatchObject({
        type: 'object',
        required: ['success', 'statusCode', 'error', 'meta'],
      });
      const p = props(schema);
      expect(p.success).toMatchObject({ enum: [false] });
      expect(p.statusCode).toMatchObject({ example: 400 });
      const err = props(p.error as SchemaObject);
      expect(err.code).toMatchObject({ example: 'VALIDATION_FAILED' });
      expect(err.message).toMatchObject({ example: 'Bad input' });
    });

    it('includes fields array when options.fields is true', () => {
      const schema = errorEnvelopeSchema(422, 'VALIDATION_FAILED', 'Invalid', { fields: true });
      const p = props(schema);
      const err = props(p.error as SchemaObject);
      expect(err).toHaveProperty('fields');
    });
  });
});
