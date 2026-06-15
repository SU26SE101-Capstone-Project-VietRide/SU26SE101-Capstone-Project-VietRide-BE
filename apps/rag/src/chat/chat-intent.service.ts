import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import type { RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';

interface IntentDecision {
  allowed: boolean;
  refusalMessage?: string;
}

const logger = pino({ name: 'RagChatIntentService' });
const AMBIGUOUS_MESSAGE_MAX_CHARS = 80;
const CLASSIFIER_CONTEXT_LIMIT = 4;

@Injectable()
export class ChatIntentService {
  constructor(@Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider) {}

  async classify(
    message: string,
    history: RagMessage[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): Promise<IntentDecision> {
    const normalized = this.normalize(message);
    if (this.hasAnyTerm(normalized, runtimeConfig.getStringList(RAG_RUNTIME_CONFIG_KEYS.intentInScopeTerms))) {
      return { allowed: true };
    }
    const refusalMessage = runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.intentOffTopicRefusal);
    if (this.hasAnyTerm(normalized, runtimeConfig.getStringList(RAG_RUNTIME_CONFIG_KEYS.intentOffTopicTerms))) {
      return { allowed: false, refusalMessage };
    }
    if (!this.isAmbiguous(message, history)) return { allowed: true };

    try {
      const result = await this.chatProvider.complete({
        stream: false,
        messages: this.buildClassifierMessages(message, history, runtimeConfig),
      });
      if (this.normalize(result).includes('off_topic')) {
        return { allowed: false, refusalMessage };
      }
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG intent classifier failed');
    }

    return { allowed: true };
  }

  private buildClassifierMessages(
    message: string,
    history: RagMessage[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): ChatMessage[] {
    const context = history
      .slice(-CLASSIFIER_CONTEXT_LIMIT)
      .map((item) => `${item.role}: ${item.content}`)
      .join('\n');
    return [
      {
        role: 'system',
        content: runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.intentClassifierPrompt),
      },
      {
        role: 'user',
        content: ['Conversation context:', context || 'None', '', `User message: ${message}`].join('\n'),
      },
    ];
  }

  private isAmbiguous(message: string, history: RagMessage[]): boolean {
    return history.length > 0 && message.length <= AMBIGUOUS_MESSAGE_MAX_CHARS;
  }

  private hasAnyTerm(message: string, terms: string[]): boolean {
    return terms.some((term) => message.includes(this.normalize(term)));
  }

  private normalize(value: string): string {
    return value.trim().toLowerCase();
  }

  private toSafeErrorLog(error: unknown): { name: string; status?: number } {
    if (error instanceof Error) {
      const status = 'getStatus' in error ? (error.getStatus as () => number)() : undefined;
      return { name: error.name, ...(status ? { status } : {}) };
    }
    return { name: 'UnknownError' };
  }
}
