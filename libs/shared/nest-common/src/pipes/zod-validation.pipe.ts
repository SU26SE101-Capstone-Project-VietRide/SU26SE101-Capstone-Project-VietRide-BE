import { HttpException, Injectable, PipeTransform } from '@nestjs/common';
import type { ZodType, ZodTypeDef } from 'zod';

/**
 * NestJS pipe that validates / parses incoming payloads against a Zod schema.
 * On failure raises the configured HTTP status and registry error code plus
 * flattened Zod issues; defaults remain 400/VALIDATION_FAILED for existing
 * endpoints, while strict instant inputs opt into 422/VALIDATION_ERROR.
 *
 * Usage:
 *   @Body(new ZodValidationPipe(MyDtoSchema)) dto: MyDto
 */
@Injectable()
export class ZodValidationPipe<T> implements PipeTransform<unknown, T> {
  constructor(
    private readonly schema: ZodType<T, ZodTypeDef, unknown>,
    private readonly options: { statusCode?: 400 | 422; errorCode?: string } = {},
  ) {}

  transform(value: unknown): T {
    const result = this.schema.safeParse(value);
    if (!result.success) {
      const statusCode = this.options.statusCode ?? 400;
      throw new HttpException({
        errorCode: this.options.errorCode ?? 'VALIDATION_FAILED',
        message: 'Validation failed',
        errors: result.error.issues.map((i) => ({
          path: i.path.join('.'),
          code: i.code,
          message: i.message,
        })),
      }, statusCode);
    }
    return result.data;
  }
}
