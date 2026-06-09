import { Module } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import {
  ApiResponseExceptionFilter,
  ApiResponseInterceptor,
  LoggingInterceptor,
  NestCommonModule,
} from '@vietride/nest-common';
import { NestRabbitMqModule } from '@vietride/nest-rabbitmq';
import { NestRedisModule } from '@vietride/nest-redis';
import { loadEnv } from '../config/env.schema';
import { NotificationConfigModule } from '../config/notification-config.module';
import { NotificationsModule } from '../notifications/notifications.module';
import { NotificationPrismaModule } from '../prisma/prisma.module';
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { HealthController } from './health.controller';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';

const env = loadEnv();

@Module({
  imports: [
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
  ],
  controllers: [AppController, HealthController, ReadyController],
  providers: [
    AppService,
    ReadinessService,
    { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
  ],
})
export class AppModule {}
