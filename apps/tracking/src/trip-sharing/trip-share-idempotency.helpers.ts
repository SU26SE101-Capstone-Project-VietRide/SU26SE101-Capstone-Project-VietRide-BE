import { UnprocessableEntityException } from '@nestjs/common';

const UUID_V4_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function requireTripShareIdempotencyKey(value: string | undefined): string {
  if (!value) {
    throw new UnprocessableEntityException({
      errorCode: 'IDEMPOTENCY_KEY_REQUIRED',
      message: 'IDEMPOTENCY_KEY_REQUIRED',
      detail: 'The Idempotency-Key header is required',
    });
  }
  if (!UUID_V4_PATTERN.test(value)) {
    throw new UnprocessableEntityException({
      errorCode: 'VALIDATION_ERROR',
      message: 'VALIDATION_ERROR',
      detail: 'Idempotency-Key must be a UUID v4',
    });
  }
  return value.toLowerCase();
}
