import { Module } from '@nestjs/common';
import { ProvidersModule } from '../providers/providers.module';
import { RagSubscriptionEntitlementModule } from '../subscriptions/rag-subscription-entitlement.module';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatIntentService } from './chat-intent.service';
import { ChatQueryRewriteService } from './chat-query-rewrite.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatRerankService } from './chat-rerank.service';
import { ChatController } from './chat.controller';
import { ChatRepository } from './chat.repository';
import { ChatService } from './chat.service';
import { ChatSummaryService } from './chat-summary.service';
import { FeedbackController } from './feedback.controller';
import { FeedbackService } from './feedback.service';

@Module({
  imports: [ProvidersModule, RagSubscriptionEntitlementModule],
  controllers: [ChatController, FeedbackController],
  providers: [
    ChatRepository,
    ChatService,
    ChatEmbeddingCacheService,
    ChatRateLimitService,
    ChatIntentService,
    ChatQueryRewriteService,
    ChatSummaryService,
    ChatRerankService,
    FeedbackService,
  ],
})
export class ChatModule {}
