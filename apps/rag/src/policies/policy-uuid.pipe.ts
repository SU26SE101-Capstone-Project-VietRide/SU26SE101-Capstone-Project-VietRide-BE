import { UnprocessableEntityException, type PipeTransform } from '@nestjs/common';
import { z } from 'zod';

const policyIdSchema = z.string().uuid();

export class PolicyUuidPipe implements PipeTransform<unknown, string> {
  transform(value: unknown): string {
    const result = policyIdSchema.safeParse(value);
    if (!result.success) {
      throw new UnprocessableEntityException({
        errorCode: 'VALIDATION_ERROR',
        message: 'Policy ID must be a UUID',
        errors: [{ path: 'policyId', code: 'invalid_string', message: 'Invalid UUID' }],
      });
    }
    return result.data;
  }
}
