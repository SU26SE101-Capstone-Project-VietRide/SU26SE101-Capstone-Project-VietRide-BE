import { Module } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { SentryModule } from '@sentry/nestjs/setup';
import {
  ApiResponseInterceptor,
  LoggingInterceptor,
  NestCommonModule,
} from '@vietride/nest-common';
import { NestRabbitMqModule } from '@vietride/nest-rabbitmq';
import { NestRedisModule } from '@vietride/nest-redis';
import { loadEnv } from '../config/env.schema';
import { NotificationConfigModule } from '../config/notification-config.module';
import { IdentityEventsModule } from '../identity-events/identity-events.module';
import { NotificationsModule } from '../notifications/notifications.module';
import { NotificationPrismaModule } from '../prisma/prisma.module';
import { HealthController } from './health.controller';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';
import { NotificationSentryExceptionFilter } from './notification-sentry-exception.filter';
import { createNotificationLogger } from '../notifications/notification-logger';

const env = loadEnv();

@Module({
  imports: [
    SentryModule.forRoot(),
    NestCommonModule,
    NotificationConfigModule,
    NestRedisModule.forRoot({ url: env.REDIS_URL }),
    NestRabbitMqModule.forRoot({
      url: env.RABBITMQ_URL,
      exchange: env.RABBITMQ_EXCHANGE,
      exchangeType: 'topic',
    }),
    NotificationPrismaModule,
    NotificationsModule,
    IdentityEventsModule,
  ],
  controllers: [HealthController, ReadyController],
  providers: [
    ReadinessService,
    { provide: APP_FILTER, useClass: NotificationSentryExceptionFilter },
    {
      provide: APP_INTERCEPTOR,
      useValue: new LoggingInterceptor(createNotificationLogger('NotificationHttp')),
    },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
  ],
})
export class AppModule {}
