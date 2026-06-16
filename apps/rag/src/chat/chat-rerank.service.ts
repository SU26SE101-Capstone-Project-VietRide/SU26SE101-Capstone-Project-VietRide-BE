import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'node:crypto';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import type { RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';
import {
  RAG_RERANK_CACHE_TTL_SECONDS,
  RAG_RERANK_FINAL_LIMIT,
  RAG_RERANK_TIMEOUT_MS,
} from './chat.constants';
import type { RagRetrievedChunk } from './chat.types';

const logger = pino({ name: 'RagChatRerankService' });

@Injectable()
export class ChatRerankService {
  constructor(
    private readonly redis: RedisService,
    @Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider,
  ) {}

  async rerank(
    query: string,
    chunks: RagRetrievedChunk[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): Promise<RagRetrievedChunk[]> {
    const fallback = chunks.slice(0, RAG_RERANK_FINAL_LIMIT);
    if (chunks.length <= RAG_RERANK_FINAL_LIMIT) return fallback;

    const cacheKey = this.cacheKey(query, chunks);
    const cached = await this.getCachedOrder(cacheKey);
    if (cached) return this.applyOrder(chunks, cached, fallback);

    try {
      const result = await this.withTimeout(
        this.chatProvider.complete({
          stream: false,
          messages: this.buildRerankMessages(query, chunks, runtimeConfig),
        }),
      );
      const orderedIds = this.parseOrderedIds(result, chunks);
      if (orderedIds.length === 0) return fallback;

      await this.redis.set(cacheKey, JSON.stringify(orderedIds), RAG_RERANK_CACHE_TTL_SECONDS);
      return this.applyOrder(chunks, orderedIds, fallback);
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG rerank failed; using retrieval order');
      return fallback;
    }
  }

  private buildRerankMessages(
    query: string,
    chunks: RagRetrievedChunk[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): ChatMessage[] {
    return [
      {
        role: 'system',
        content: runtimeConfig
          .getString(RAG_RUNTIME_CONFIG_KEYS.rerankPrompt)
          .replaceAll('{rerank_final_limit}', RAG_RERANK_FINAL_LIMIT.toString()),
      },
      {
        role: 'user',
        content: [
          `Query: ${query}`,
          'Candidates:',
          chunks
            .map((chunk, index) =>
              [
                `${index + 1}. id=${chunk.id}`,
                `title=${chunk.documentTitle}`,
                `section=${chunk.sectionHeader ?? ''}`,
                `content=${chunk.content.slice(0, 700)}`,
              ].join('\n'),
            )
            .join('\n\n'),
        ].join('\n'),
      },
    ];
  }

  private parseOrderedIds(response: string, chunks: RagRetrievedChunk[]): string[] {
    const allowed = new Set(chunks.map((chunk) => chunk.id));
    try {
      const parsed = JSON.parse(response) as unknown;
      if (!Array.isArray(parsed)) return [];
      return parsed
        .filter((value): value is string => typeof value === 'string' && allowed.has(value))
        .slice(0, RAG_RERANK_FINAL_LIMIT);
    } catch {
      return [];
    }
  }

  private applyOrder(
    chunks: RagRetrievedChunk[],
    orderedIds: string[],
    fallback: RagRetrievedChunk[],
  ): RagRetrievedChunk[] {
    const byId = new Map(chunks.map((chunk) => [chunk.id, chunk]));
    const ordered = orderedIds
      .map((id) => byId.get(id))
      .filter((chunk): chunk is RagRetrievedChunk => Boolean(chunk));
    return ordered.length > 0 ? ordered.slice(0, RAG_RERANK_FINAL_LIMIT) : fallback;
  }

  private async getCachedOrder(cacheKey: string): Promise<string[] | undefined> {
    const cached = await this.redis.get(cacheKey);
    if (!cached) return undefined;
    try {
      const parsed = JSON.parse(cached) as unknown;
      if (!Array.isArray(parsed) || parsed.some((value) => typeof value !== 'string')) {
        return undefined;
      }
      return parsed;
    } catch {
      return undefined;
    }
  }

  private cacheKey(query: string, chunks: RagRetrievedChunk[]): string {
    const hash = createHash('sha256')
      .update(query)
      .update('|')
      .update(chunks.map((chunk) => chunk.id).join(','))
      .digest('hex');
    return `rag:chat:rerank:${hash}`;
  }

  private async withTimeout<T>(promise: Promise<T>): Promise<T> {
    let timeout: NodeJS.Timeout | undefined;
    try {
      return await Promise.race([
        promise,
        new Promise<T>((_, reject) => {
          timeout = setTimeout(() => reject(new Error('RERANK_TIMEOUT')), RAG_RERANK_TIMEOUT_MS);
        }),
      ]);
    } finally {
      if (timeout) clearTimeout(timeout);
    }
  }

  private toSafeErrorLog(error: unknown): { name: string; status?: number } {
    if (error instanceof Error) {
      const status = 'getStatus' in error ? (error.getStatus as () => number)() : undefined;
      return { name: error.name, ...(status ? { status } : {}) };
    }
    return { name: 'UnknownError' };
  }
}
