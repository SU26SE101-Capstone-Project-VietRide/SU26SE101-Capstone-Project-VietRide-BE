import { MiddlewareConsumer, Module, NestModule, RequestMethod } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import { UserJwtMiddleware } from '../auth/user-jwt.middleware';
import { loadEnv, type Env } from '../config/env.schema';
import { HealthController } from '../health/health.controller';
import { ENV_TOKEN } from './tokens';

const env = loadEnv();

@Module({
  imports: [
    ThrottlerModule.forRoot([
      // Default rate limit per IP: 100 req / 60s. Per-route overrides Day 3+.
      { ttl: 60_000, limit: 100 },
    ]),
  ],
  controllers: [HealthController],
  providers: [
    { provide: ENV_TOKEN, useValue: env },
    {
      provide: InternalJwtSigner,
      useFactory: (e: Env) => new InternalJwtSigner(e.INTERNAL_JWT_SECRET, e.INTERNAL_JWT_TTL_SEC),
      inject: [ENV_TOKEN],
    },
    UserJwtMiddleware,
    { provide: APP_GUARD, useClass: ThrottlerGuard },
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
      { path: 'v1/auth/*path', method: RequestMethod.ALL },
      { path: 'v1/auth', method: RequestMethod.ALL },
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

    consumer.apply(UserJwtMiddleware).exclude(...publicPaths).forRoutes('*');
  }
}
