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
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { HealthController } from './health.controller';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';

const env = loadEnv();

@Module({
  imports: [
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
  ],
  controllers: [AppController, HealthController, ReadyController],
  providers: [
    AppService,
    ReadinessService,
    InternalJwtAuthGuard,
    { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
  ],
})
export class AppModule {}
