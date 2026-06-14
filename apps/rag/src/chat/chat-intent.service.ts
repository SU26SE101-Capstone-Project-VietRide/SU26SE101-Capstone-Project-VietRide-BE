import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER } from '../app/tokens';
import type { RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';

interface IntentDecision {
  allowed: boolean;
  refusalMessage?: string;
}

const logger = pino({ name: 'RagChatIntentService' });
const OFF_TOPIC_REFUSAL =
  'Xin lỗi, tôi chỉ có thể hỗ trợ các câu hỏi liên quan đến dịch vụ, chính sách và vận hành VietRide dựa trên kho tri thức hiện có.';
const AMBIGUOUS_MESSAGE_MAX_CHARS = 80;
const CLASSIFIER_CONTEXT_LIMIT = 4;
const IN_SCOPE_TERMS = [
  'vietride',
  'vé',
  'chuyến',
  'đặt xe',
  'đặt vé',
  'hủy vé',
  'hoàn tiền',
  'hành lý',
  'voucher',
  'thanh toán',
  'tài khoản',
  'nhà xe',
  'tài xế',
  'đón khách',
  'trả khách',
  'dashboard',
  'đơn hàng',
  'hàng ký gửi',
  'chính sách',
  'quy trình',
  'sop',
];
const OFF_TOPIC_TERMS = [
  'chứng khoán',
  'bitcoin',
  'bóng đá',
  'viết thơ',
  'truyện cười',
  'hack',
  'mật khẩu người khác',
  'dự đoán xổ số',
  'nấu ăn',
  'game',
];

@Injectable()
export class ChatIntentService {
  constructor(@Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider) {}

  async classify(message: string, history: RagMessage[]): Promise<IntentDecision> {
    const normalized = this.normalize(message);
    if (this.hasAnyTerm(normalized, IN_SCOPE_TERMS)) return { allowed: true };
    if (this.hasAnyTerm(normalized, OFF_TOPIC_TERMS)) {
      return { allowed: false, refusalMessage: OFF_TOPIC_REFUSAL };
    }
    if (!this.isAmbiguous(message, history)) return { allowed: true };

    try {
      const result = await this.chatProvider.complete({
        stream: false,
        messages: this.buildClassifierMessages(message, history),
      });
      if (this.normalize(result).includes('off_topic')) {
        return { allowed: false, refusalMessage: OFF_TOPIC_REFUSAL };
      }
    } catch (error) {
      logger.warn({ error: this.toSafeErrorLog(error) }, 'RAG intent classifier failed');
    }

    return { allowed: true };
  }

  private buildClassifierMessages(message: string, history: RagMessage[]): ChatMessage[] {
    const context = history
      .slice(-CLASSIFIER_CONTEXT_LIMIT)
      .map((item) => `${item.role}: ${item.content}`)
      .join('\n');
    return [
      {
        role: 'system',
        content: [
          'Classify whether the user message is about VietRide customer support, operator policy, platform admin, trip, booking, payment, account, luggage, parcel, or operations.',
          'Return exactly IN_SCOPE or OFF_TOPIC. Do not explain.',
        ].join('\n'),
      },
      {
        role: 'user',
        content: [`Conversation context:`, context || 'None', '', `User message: ${message}`].join('\n'),
      },
    ];
  }

  private isAmbiguous(message: string, history: RagMessage[]): boolean {
    return history.length > 0 && message.length <= AMBIGUOUS_MESSAGE_MAX_CHARS;
  }

  private hasAnyTerm(message: string, terms: string[]): boolean {
    return terms.some((term) => message.includes(term));
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
