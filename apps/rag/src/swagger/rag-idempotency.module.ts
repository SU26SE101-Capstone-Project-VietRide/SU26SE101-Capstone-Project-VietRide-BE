import { Global, Module } from '@nestjs/common';
import {
  RagIdempotencyInterceptor,
  RagMultipartIdempotencyInterceptor,
} from './rag-idempotency.interceptor';
import { RagIdempotencyService } from './rag-idempotency.service';

@Global()
@Module({
  providers: [RagIdempotencyService, RagIdempotencyInterceptor, RagMultipartIdempotencyInterceptor],
  exports: [RagIdempotencyService, RagIdempotencyInterceptor, RagMultipartIdempotencyInterceptor],
})
export class RagIdempotencyModule {}
