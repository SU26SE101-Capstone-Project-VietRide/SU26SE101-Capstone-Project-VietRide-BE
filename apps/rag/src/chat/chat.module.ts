import { Module } from '@nestjs/common';
import { ProvidersModule } from '../providers/providers.module';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatController } from './chat.controller';
import { ChatRepository } from './chat.repository';
import { ChatService } from './chat.service';

@Module({
  imports: [ProvidersModule],
  controllers: [ChatController],
  providers: [ChatRepository, ChatService, ChatEmbeddingCacheService, ChatRateLimitService],
})
export class ChatModule {}
