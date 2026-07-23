import { applyDecorators, SetMetadata } from '@nestjs/common';
import { ApiExtension, ApiHeader } from '@nestjs/swagger';

export const IDEMPOTENCY_REQUIRED_METADATA = 'vietride:idempotency-required';
export const IDEMPOTENCY_EXEMPT_METADATA = 'vietride:idempotency-exempt';
export const IDEMPOTENCY_MULTIPART_DEFERRED_METADATA = 'vietride:idempotency-multipart-deferred';
export const IDEMPOTENCY_OPENAPI_EXTENSION = 'x-vietride-idempotency';

export function ApiIdempotencyRequired(): MethodDecorator {
  return applyDecorators(
    SetMetadata(IDEMPOTENCY_REQUIRED_METADATA, true),
    ApiHeader({
      name: 'Idempotency-Key',
      required: true,
      schema: { type: 'string', format: 'uuid' },
      description: 'UUID v4 retained for retries of the same operation.',
    }),
    ApiExtension(IDEMPOTENCY_OPENAPI_EXTENSION, {
      required: true,
      keyFormat: 'uuid-v4',
      ttlSeconds: 86_400,
    }),
  );
}

export function ApiIdempotencyExempt(reason: string): MethodDecorator {
  return applyDecorators(
    SetMetadata(IDEMPOTENCY_EXEMPT_METADATA, reason),
    ApiExtension(IDEMPOTENCY_OPENAPI_EXTENSION, { required: false, reason }),
  );
}

export function DeferRagMultipartIdempotency(): MethodDecorator {
  return SetMetadata(IDEMPOTENCY_MULTIPART_DEFERRED_METADATA, true);
}
