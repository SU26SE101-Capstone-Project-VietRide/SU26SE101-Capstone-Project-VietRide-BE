import { Module } from '@nestjs/common';
import { CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, STORAGE_PROVIDER } from '../app/tokens';
import { CloudinaryStorageProvider } from './cloudinary-storage.provider';
import { OpenRouterChatCompletionProvider } from './openrouter-chat-completion.provider';
import { OpenRouterEmbeddingProvider } from './openrouter-embedding.provider';

@Module({
  providers: [
    OpenRouterChatCompletionProvider,
    OpenRouterEmbeddingProvider,
    CloudinaryStorageProvider,
    { provide: CHAT_COMPLETION_PROVIDER, useExisting: OpenRouterChatCompletionProvider },
    { provide: EMBEDDING_PROVIDER, useExisting: OpenRouterEmbeddingProvider },
    { provide: STORAGE_PROVIDER, useExisting: CloudinaryStorageProvider },
  ],
  exports: [CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, STORAGE_PROVIDER],
})
export class ProvidersModule {}
