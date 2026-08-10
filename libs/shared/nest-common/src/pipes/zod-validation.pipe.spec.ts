import { HttpException } from '@nestjs/common';
import { z } from 'zod';
import { ZodValidationPipe } from './zod-validation.pipe';

describe('ZodValidationPipe', () => {
  const schema = z.object({ value: z.string().uuid() });

  it('keeps the existing 400 VALIDATION_FAILED default', () => {
    const pipe = new ZodValidationPipe(schema);

    expectException(pipe, 400, 'VALIDATION_FAILED');
  });

  it('supports the strict timestamp contract 422 VALIDATION_ERROR policy', () => {
    const pipe = new ZodValidationPipe(schema, {
      statusCode: 422,
      errorCode: 'VALIDATION_ERROR',
    });

    expectException(pipe, 422, 'VALIDATION_ERROR');
  });
});

function expectException(
  pipe: ZodValidationPipe<{ value: string }>,
  expectedStatus: number,
  expectedCode: string,
): void {
  try {
    pipe.transform({ value: 'invalid' });
    throw new Error('Expected validation to throw');
  } catch (error) {
    expect(error).toBeInstanceOf(HttpException);
    const exception = error as HttpException;
    expect(exception.getStatus()).toBe(expectedStatus);
    expect(exception.getResponse()).toEqual(expect.objectContaining({ errorCode: expectedCode }));
  }
}
