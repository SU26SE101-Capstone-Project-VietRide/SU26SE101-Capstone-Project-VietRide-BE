import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
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

  async summarizeIfNeeded(conversation: RagConversation, assistantMessageId: string): Promise<void> {
    const messageCount = await this.chatRepository.countMessages(conversation.id);
    if (messageCount < RAG_SUMMARY_MIN_MESSAGE_COUNT) return;

    const history = await this.chatRepository.findRecentMessages(
      conversation.id,
      RAG_SUMMARY_HISTORY_MESSAGE_LIMIT,
    );
    const summary = await this.createSummary(conversation.summary, history);
    if (!summary) return;

    await this.chatRepository.updateConversationSummary({
      conversationId: conversation.id,
      summary,
      summaryFromMessageId: assistantMessageId,
    });
  }

  private async createSummary(existingSummary: string | null, history: RagMessage[]): Promise<string> {
    try {
      const raw = await this.chatProvider.complete({
        stream: false,
        messages: this.buildSummaryMessages(existingSummary, history),
      });
      return raw.trim().slice(0, RAG_MAX_SUMMARY_CHARS);
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG conversation summarization failed');
      return '';
    }
  }

  private buildSummaryMessages(existingSummary: string | null, history: RagMessage[]): ChatMessage[] {
    return [
      {
        role: 'system',
        content: [
          'Summarize the VietRide RAG conversation in Vietnamese for future context.',
          'Keep only user goals, important constraints, and resolved topics.',
          'Do not include secrets, tokens, raw provider data, or unsupported facts.',
          `Return at most ${RAG_MAX_SUMMARY_CHARS} characters.`,
        ].join('\n'),
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
