import { Global, MiddlewareConsumer, Module, NestModule } from '@nestjs/common';
import { CorrelationIdMiddleware } from '../request-context/correlation-id.middleware';
import { RequestContextService } from '../request-context/request-context.service';

/**
 * Aggregates shared cross-cutting concerns for every VietRide NestJS app:
 *   - CorrelationIdMiddleware (auto-applied to all routes)
 *   - RequestContextService (request-scoped accessor for requestId / userId / role)
 *
 * Filters (ProblemDetailsExceptionFilter), pipes (ZodValidationPipe), and
 * interceptors (LoggingInterceptor) are exported as standalone classes that
 * apps wire into APP_FILTER / APP_INTERCEPTOR / useGlobalPipes themselves —
 * so they can compose them with app-specific instances (e.g. shared pino logger).
 */
@Global()
@Module({
  providers: [RequestContextService],
  exports: [RequestContextService],
})
export class NestCommonModule implements NestModule {
  configure(consumer: MiddlewareConsumer): void {
    consumer.apply(CorrelationIdMiddleware).forRoutes('*');
  }
}
