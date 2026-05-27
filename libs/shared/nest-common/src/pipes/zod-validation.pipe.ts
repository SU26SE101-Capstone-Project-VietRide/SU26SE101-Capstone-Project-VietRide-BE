import { BadRequestException, Injectable, PipeTransform } from '@nestjs/common';
import type { ZodSchema, ZodIssue } from 'zod';

/**
 * NestJS pipe that validates / parses incoming payloads against a Zod schema.
 * On failure raises a BadRequestException carrying RFC 7807-ish detail with
 * the flattened Zod issues so ProblemDetailsExceptionFilter can render them.
 *
 * Usage:
 *   @Body(new ZodValidationPipe(MyDtoSchema)) dto: MyDto
 */
@Injectable()
export class ZodValidationPipe<T> implements PipeTransform<unknown, T> {
  constructor(private readonly schema: ZodSchema<T>) {}

  transform(value: unknown): T {
    const result = this.schema.safeParse(value);
    if (!result.success) {
      throw new BadRequestException({
        errorCode: 'VALIDATION_FAILED',
        title: 'Validation failed',
        detail: formatIssues(result.error.issues),
        issues: result.error.issues.map((i) => ({
          path: i.path.join('.'),
          code: i.code,
          message: i.message,
        })),
      });
    }
    return result.data;
  }
}

function formatIssues(issues: ZodIssue[]): string {
  return issues
    .map((i) => `${i.path.length ? i.path.join('.') : '(root)'}: ${i.message}`)
    .join('; ');
}
