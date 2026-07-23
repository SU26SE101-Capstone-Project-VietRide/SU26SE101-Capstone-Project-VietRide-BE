import { Global, Module } from '@nestjs/common';
import { RagPrismaModule } from '../prisma/prisma.module';
import {
  RagIdempotencyInterceptor,
  RagMultipartIdempotencyInterceptor,
} from './rag-idempotency.interceptor';
import { RagIdempotencyService } from './rag-idempotency.service';

@Global()
@Module({
  imports: [RagPrismaModule],
  providers: [RagIdempotencyService, RagIdempotencyInterceptor, RagMultipartIdempotencyInterceptor],
  exports: [RagIdempotencyService, RagIdempotencyInterceptor, RagMultipartIdempotencyInterceptor],
})
export class RagIdempotencyModule {}
