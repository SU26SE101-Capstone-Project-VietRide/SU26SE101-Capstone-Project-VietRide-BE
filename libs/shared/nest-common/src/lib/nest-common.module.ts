import { Global, MiddlewareConsumer, Module, NestModule } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { CorrelationIdMiddleware } from '../request-context/correlation-id.middleware';
import { RequestContextService } from '../request-context/request-context.service';
import { ApiResponseInterceptor } from '../interceptors/api-response.interceptor';
import { ApiResponseExceptionFilter } from '../filters/api-response-exception.filter';

/**
 * Aggregates shared cross-cutting concerns for every VietRide NestJS app:
 *   - CorrelationIdMiddleware (auto-applied to all routes)
 *   - RequestContextService (request-scoped accessor for requestId / userId / role)
 *   - ApiResponseInterceptor (auto-wraps success responses into `ApiResponse<T>`)
 *   - ApiResponseExceptionFilter (maps exceptions to the error envelope)
 *
 * Per ADR 0004, the interceptor and filter are registered as global
 * APP_INTERCEPTOR / APP_FILTER so they apply to every HTTP endpoint.
 * Apps can still compose additional app-specific instances.
 */
@Global()
@Module({
  providers: [
    RequestContextService,
    {
      provide: APP_INTERCEPTOR,
      useClass: ApiResponseInterceptor,
    },
    {
      provide: APP_FILTER,
      useClass: ApiResponseExceptionFilter,
    },
  ],
  exports: [RequestContextService],
})
export class NestCommonModule implements NestModule {
  configure(consumer: MiddlewareConsumer): void {
    consumer.apply(CorrelationIdMiddleware).forRoutes('*');
  }
}
