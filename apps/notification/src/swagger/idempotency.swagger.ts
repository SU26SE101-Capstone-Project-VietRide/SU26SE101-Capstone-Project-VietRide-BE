import { applyDecorators } from '@nestjs/common';
import { ApiExtension, ApiHeader } from '@nestjs/swagger';

export const IDEMPOTENCY_OPENAPI_EXTENSION = 'x-vietride-idempotency';

export function ApiIdempotencyRequired(): MethodDecorator {
  return applyDecorators(
    ApiHeader({
      name: 'Idempotency-Key',
      required: true,
      description: 'UUID v4. Reuse the same key only when retrying the same request.',
      schema: { type: 'string', format: 'uuid' },
    }),
    ApiExtension(IDEMPOTENCY_OPENAPI_EXTENSION, {
      required: true,
      keyFormat: 'uuid-v4',
      ttlSeconds: 86_400,
    }),
  );
}
