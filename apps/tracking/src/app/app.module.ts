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
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { HealthController } from './health.controller';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';
import { loadEnv } from '../config/env.schema';
import { TrackingConfigModule } from '../config/tracking-config.module';
import { GpsBatchModule } from '../gps-batch/gps-batch.module';
import { LocationModule } from '../location/location.module';
import { OutboxModule } from '../outbox/outbox.module';
import { TrackingPrismaModule } from '../prisma/prisma.module';
import { TrackingDataModule } from '../tracking-data/tracking-data.module';

const env = loadEnv();

@Module({
  imports: [
    NestCommonModule,
    TrackingConfigModule,
    NestRedisModule.forRoot({ url: env.REDIS_URL }),
    NestRabbitMqModule.forRoot({
      url: env.RABBITMQ_URL,
      exchange: env.RABBITMQ_EXCHANGE,
      exchangeType: 'topic',
    }),
    TrackingPrismaModule,
    LocationModule,
    GpsBatchModule,
    OutboxModule,
    TrackingDataModule,
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
