import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import type { RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';

const logger = pino({ name: 'RagChatQueryRewriteService' });
const REWRITE_HISTORY_LIMIT = 6;
const REWRITE_MAX_CHARS = 500;
const CONTEXTUAL_PATTERNS = [
  /\b(cái đó|việc đó|điều đó|nó|vậy|như trên|trường hợp đó|cái này|việc này)\b/i,
  /\b(bao lâu|thì sao|làm sao|như thế nào|có được không)\??$/i,
];

@Injectable()
export class ChatQueryRewriteService {
  constructor(@Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider) {}

  async rewriteIfNeeded(
    message: string,
    history: RagMessage[],
    summary: string | null,
    runtimeConfig: RuntimeConfigSnapshot,
  ): Promise<string> {
    if (!this.needsRewrite(message, history)) return message;

    try {
      const rewritten = await this.chatProvider.complete({
        stream: false,
        messages: this.buildRewriteMessages(message, history, summary, runtimeConfig),
      });
      const cleaned = this.cleanRewrite(rewritten);
      return cleaned || message;
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG query rewrite failed');
      return message;
    }
  }

  private needsRewrite(message: string, history: RagMessage[]): boolean {
    if (history.length === 0) return false;
    return CONTEXTUAL_PATTERNS.some((pattern) => pattern.test(message));
  }

  private buildRewriteMessages(
    message: string,
    history: RagMessage[],
    summary: string | null,
    runtimeConfig: RuntimeConfigSnapshot,
  ): ChatMessage[] {
    const recentHistory = history
      .slice(-REWRITE_HISTORY_LIMIT)
      .map((item) => `${item.role}: ${item.content}`)
      .join('\n');
    return [
      {
        role: 'system',
        content: runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.queryRewritePrompt),
      },
      {
        role: 'user',
        content: [
          `Conversation summary: ${summary ?? 'None'}`,
          'Recent messages:',
          recentHistory || 'None',
          '',
          `Latest user question: ${message}`,
        ].join('\n'),
      },
    ];
  }

  private cleanRewrite(value: string): string {
    return value.replace(/^["'`]+|["'`]+$/g, '').trim().slice(0, REWRITE_MAX_CHARS);
  }

  private toSafeErrorLog(error: unknown): { name: string; status?: number } {
    if (error instanceof Error) {
      const status = 'getStatus' in error ? (error.getStatus as () => number)() : undefined;
      return { name: error.name, ...(status ? { status } : {}) };
    }
    return { name: 'UnknownError' };
  }
}
