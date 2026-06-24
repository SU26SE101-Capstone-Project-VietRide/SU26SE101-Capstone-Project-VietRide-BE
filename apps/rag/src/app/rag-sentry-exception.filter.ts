import { Catch, HttpException, HttpStatus } from '@nestjs/common';
import { ApiResponseExceptionFilter } from '@vietride/nest-common';
import * as Sentry from '@sentry/nestjs';
import type { ArgumentsHost } from '@nestjs/common';

@Catch()
export class RagSentryExceptionFilter extends ApiResponseExceptionFilter {
  override catch(exception: unknown, host: ArgumentsHost): void {
    const status = exception instanceof HttpException ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;

    if (status >= 500) {
      Sentry.captureException(exception);
    }

    super.catch(exception, host);
  }
}
