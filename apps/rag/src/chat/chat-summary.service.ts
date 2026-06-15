import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import type { RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { RagConversation, RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';
import {
  RAG_MAX_SUMMARY_CHARS,
  RAG_SUMMARY_HISTORY_MESSAGE_LIMIT,
  RAG_SUMMARY_MIN_MESSAGE_COUNT,
} from './chat.constants';
import { ChatRepository } from './chat.repository';

const logger = pino({ name: 'RagChatSummaryService' });

@Injectable()
export class ChatSummaryService {
  constructor(
    private readonly chatRepository: ChatRepository,
    @Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider,
  ) {}

  async summarizeIfNeeded(
    conversation: RagConversation,
    assistantMessageId: string,
    runtimeConfig: RuntimeConfigSnapshot,
  ): Promise<void> {
    const messageCount = await this.chatRepository.countMessages(conversation.id);
    if (messageCount < RAG_SUMMARY_MIN_MESSAGE_COUNT) return;

    const history = await this.chatRepository.findRecentMessages(
      conversation.id,
      RAG_SUMMARY_HISTORY_MESSAGE_LIMIT,
    );
    const summary = await this.createSummary(conversation.summary, history, runtimeConfig);
    if (!summary) return;

    await this.chatRepository.updateConversationSummary({
      conversationId: conversation.id,
      summary,
      summaryFromMessageId: assistantMessageId,
    });
  }

  private async createSummary(
    existingSummary: string | null,
    history: RagMessage[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): Promise<string> {
    try {
      const raw = await this.chatProvider.complete({
        stream: false,
        messages: this.buildSummaryMessages(existingSummary, history, runtimeConfig),
      });
      return raw.trim().slice(0, RAG_MAX_SUMMARY_CHARS);
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG conversation summarization failed');
      return '';
    }
  }

  private buildSummaryMessages(
    existingSummary: string | null,
    history: RagMessage[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): ChatMessage[] {
    return [
      {
        role: 'system',
        content: runtimeConfig
          .getString(RAG_RUNTIME_CONFIG_KEYS.summaryPrompt)
          .replaceAll('{max_summary_chars}', RAG_MAX_SUMMARY_CHARS.toString()),
      },
      {
        role: 'user',
        content: [
          `Existing summary: ${existingSummary ?? 'None'}`,
          'Recent messages:',
          history.map((item) => `${item.role}: ${item.content}`).join('\n') || 'None',
        ].join('\n'),
      },
    ];
  }

  private toSafeErrorLog(error: unknown): { name: string; status?: number } {
    if (error instanceof Error) {
      const status = 'getStatus' in error ? (error.getStatus as () => number)() : undefined;
      return { name: error.name, ...(status ? { status } : {}) };
    }
    return { name: 'UnknownError' };
  }
}
