import { BadRequestException, Injectable, PipeTransform } from '@nestjs/common';
import type { ZodSchema } from 'zod';

/**
 * NestJS pipe that validates / parses incoming payloads against a Zod schema.
 * On failure raises a BadRequestException carrying the BSOT registry
 * code plus flattened Zod issues; ApiResponseExceptionFilter maps them to
 * `error.fields[]` in the ADR 0004 error envelope.
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
        message: 'Validation failed',
        errors: result.error.issues.map((i) => ({
          path: i.path.join('.'),
          code: i.code,
          message: i.message,
        })),
      });
    }
    return result.data;
  }
}
