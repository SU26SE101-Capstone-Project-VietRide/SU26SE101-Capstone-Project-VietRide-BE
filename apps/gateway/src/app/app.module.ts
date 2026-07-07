import { MiddlewareConsumer, Module, NestModule, RequestMethod } from '@nestjs/common';
import { APP_FILTER, APP_GUARD, APP_INTERCEPTOR } from '@nestjs/core';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';
import { ThrottlerStorageRedisService } from '@nest-lab/throttler-storage-redis';
import { Redis } from 'ioredis';
import {
  ApiResponseExceptionFilter,
  LoggingInterceptor,
  NestCommonModule,
} from '@vietride/nest-common';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import { UserJwtMiddleware } from '../auth/user-jwt.middleware';
import { loadEnv, type Env } from '../config/env.schema';
import { DeeplinkController } from '../deeplink/deeplink.controller';
import { HealthController } from '../health/health.controller';
import { ENV_TOKEN } from './tokens';

const env = loadEnv();

// Redis-backed throttle storage (per BACKEND_SOURCE_OF_TRUTH §3.4.1 — rate limit
// must be Redis-backed so it survives Gateway horizontal scale-out, not per-pod).
// `THROTTLER_STORAGE_DISABLE_REDIS=1` opts back to in-memory (useful for unit
// tests + local dev without Redis running).
const throttlerStorage =
  process.env.THROTTLER_STORAGE_DISABLE_REDIS === '1'
    ? undefined
    : new ThrottlerStorageRedisService(
        new Redis({
          host: env.REDIS_HOST,
          port: env.REDIS_PORT,
          password: env.REDIS_PASSWORD,
          // Don't crash boot if Redis is briefly down — let Nest's lazy retry
          // handle reconnect. Throttler degrades gracefully on storage errors.
          lazyConnect: false,
          enableOfflineQueue: true,
          maxRetriesPerRequest: 1,
          retryStrategy: (times) => Math.min(times * 200, 2000),
          // Use a dedicated key prefix so it doesn't collide with app caches.
          keyPrefix: 'gateway:rate_limit:',
        }),
      );

@Module({
  imports: [
    NestCommonModule,
    ThrottlerModule.forRoot({
      // Default rate limit per IP: 120 req / 60s per BACKEND_SOURCE_OF_TRUTH §11.3.
      // Per-route overrides Day 3+.
      throttlers: [{ ttl: 60_000, limit: env.RATE_LIMIT_DEFAULT_PER_MIN }],
      ...(throttlerStorage ? { storage: throttlerStorage } : {}),
    }),
  ],
  controllers: [HealthController, DeeplinkController],
  providers: [
    { provide: ENV_TOKEN, useValue: env },
    {
      provide: InternalJwtSigner,
      useFactory: (e: Env) => new InternalJwtSigner(e.INTERNAL_JWT_SECRET, e.INTERNAL_JWT_TTL_SEC),
      inject: [ENV_TOKEN],
    },
    UserJwtMiddleware,
    { provide: APP_GUARD, useClass: ThrottlerGuard },
    { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
  ],
  exports: [ENV_TOKEN, InternalJwtSigner],
})
export class AppModule implements NestModule {
  configure(consumer: MiddlewareConsumer): void {
    // User JWT auth — applies to all routes except the public whitelist.
    // Day 2 minimal list; expand Day 3+ as endpoints come online.
    const publicPaths = [
      { path: 'health', method: RequestMethod.ALL },
      { path: 'ready', method: RequestMethod.ALL },
      // Deep-link endpoints served on the apex domain (no user JWT).
      { path: '.well-known/*path', method: RequestMethod.ALL },
      { path: 'auth/set-password', method: RequestMethod.GET },
      { path: 'v1/auth/register', method: RequestMethod.ALL },
      { path: 'v1/auth/verify-email', method: RequestMethod.ALL },
      { path: 'v1/auth/set-initial-password', method: RequestMethod.POST },
      { path: 'v1/auth/login', method: RequestMethod.ALL },
      { path: 'v1/auth/google', method: RequestMethod.POST },
      { path: 'v1/auth/refresh', method: RequestMethod.ALL },
      { path: 'v1/.well-known/*path', method: RequestMethod.ALL },
      { path: 'v1/operators/register', method: RequestMethod.ALL },
      { path: 'v1/payments/vnpay-ipn', method: RequestMethod.ALL },
      { path: 'v1/payments/vnpay-topup-ipn', method: RequestMethod.ALL },
      { path: 'v1/identity/health', method: RequestMethod.ALL },
      { path: 'v1/trip/health', method: RequestMethod.ALL },
      { path: 'v1/booking/health', method: RequestMethod.ALL },
      { path: 'v1/payment/health', method: RequestMethod.ALL },
      { path: 'v1/parcel/health', method: RequestMethod.ALL },
    ];

    consumer
      .apply(UserJwtMiddleware)
      .exclude(...publicPaths)
      .forRoutes('*');
  }
}
