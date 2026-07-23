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
import { RuntimeConfigAdminModule } from '../admin-config/runtime-config-admin.module';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { ChatModule } from '../chat/chat.module';
import { loadEnv } from '../config/env.schema';
import { RagConfigModule } from '../config/rag-config.module';
import { DocumentsModule } from '../documents/documents.module';
import { EmbeddingModule } from '../embedding/embedding.module';
import { IngestModule } from '../ingest/ingest.module';
import { RagPrismaModule } from '../prisma/prisma.module';
import { ProvidersModule } from '../providers/providers.module';
import { HealthController } from './health.controller';
import { RagSentryExceptionFilter } from './rag-sentry-exception.filter';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';
import { RagIdempotencyInterceptor } from '../swagger/rag-idempotency.interceptor';
import { RagIdempotencyModule } from '../swagger/rag-idempotency.module';

const env = loadEnv();

@Module({
  imports: [
    SentryModule.forRoot(),
    NestCommonModule,
    RagConfigModule,
    NestRedisModule.forRoot({ url: env.REDIS_URL }),
    NestRabbitMqModule.forRoot({
      url: env.RABBITMQ_URL,
      exchange: env.RABBITMQ_EXCHANGE,
      exchangeType: 'topic',
    }),
    RagPrismaModule,
    ProvidersModule,
    EmbeddingModule,
    DocumentsModule,
    IngestModule,
    ChatModule,
    RuntimeConfigAdminModule,
    RagIdempotencyModule,
  ],
  controllers: [HealthController, ReadyController],
  providers: [
    ReadinessService,
    InternalJwtAuthGuard,
    { provide: APP_FILTER, useClass: RagSentryExceptionFilter },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
    { provide: APP_INTERCEPTOR, useClass: RagIdempotencyInterceptor },
  ],
})
export class AppModule {}
