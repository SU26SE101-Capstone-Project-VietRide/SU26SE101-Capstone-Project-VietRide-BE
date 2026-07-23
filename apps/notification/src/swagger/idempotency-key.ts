import { UnprocessableEntityException } from '@nestjs/common';

const UUID_V4_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function requireUuidV4IdempotencyKey(value: string | undefined): string {
  const key = value?.trim().toLowerCase();
  if (!key) {
    throw new UnprocessableEntityException({
      errorCode: 'IDEMPOTENCY_KEY_REQUIRED',
      detail: 'Idempotency-Key header is required',
    });
  }
  if (!UUID_V4_PATTERN.test(key)) {
    throw new UnprocessableEntityException({
      errorCode: 'VALIDATION_ERROR',
      detail: 'Idempotency-Key must be a UUID v4',
    });
  }
  return key;
}
