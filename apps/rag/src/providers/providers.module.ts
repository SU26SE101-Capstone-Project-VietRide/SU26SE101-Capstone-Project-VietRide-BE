import { Module } from '@nestjs/common';
import { CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, STORAGE_PROVIDER } from '../app/tokens';
import { CloudinaryStorageProvider } from './cloudinary-storage.provider';
import { ShopAiKeyChatCompletionProvider } from './shopaikey-chat-completion.provider';
import { ShopAiKeyEmbeddingProvider } from './shopaikey-embedding.provider';

@Module({
  providers: [
    ShopAiKeyChatCompletionProvider,
    ShopAiKeyEmbeddingProvider,
    CloudinaryStorageProvider,
    { provide: CHAT_COMPLETION_PROVIDER, useExisting: ShopAiKeyChatCompletionProvider },
    { provide: EMBEDDING_PROVIDER, useExisting: ShopAiKeyEmbeddingProvider },
    { provide: STORAGE_PROVIDER, useExisting: CloudinaryStorageProvider },
  ],
  exports: [CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, STORAGE_PROVIDER],
})
export class ProvidersModule {}
