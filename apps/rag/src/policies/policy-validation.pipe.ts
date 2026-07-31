import { UnprocessableEntityException, type PipeTransform } from '@nestjs/common';
import type { ZodSchema } from 'zod';

export class PolicyValidationPipe<T> implements PipeTransform<unknown, T> {
  constructor(private readonly schema: ZodSchema<T>) {}

  transform(value: unknown): T {
    const result = this.schema.safeParse(value);
    if (!result.success) {
      throw new UnprocessableEntityException({
        errorCode: 'VALIDATION_ERROR',
        message: 'Policy request validation failed',
        errors: result.error.issues.map((issue) => ({
          path: issue.path.join('.'),
          code: issue.code,
          message: issue.message,
        })),
      });
    }
    return result.data;
  }
}
