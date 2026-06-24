import { ForbiddenException, Injectable, NotFoundException, UnprocessableEntityException } from '@nestjs/common';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import type { MessageFeedback } from '../generated/rag-prisma-client';
import { ChatRepository } from './chat.repository';
import type { CreateFeedbackDto } from './dto/create-feedback.dto';
import type { ListFeedbackQueryDto } from './dto/list-feedback.dto';
import type { RagFeedbackPage, RagMessageWithConversation } from './chat.types';

const SYSTEM_ADMIN_ROLE = 'SYSTEM_ADMIN';

@Injectable()
export class FeedbackService {
  constructor(private readonly chatRepository: ChatRepository) {}

  async createFeedback(
    messageId: string,
    dto: CreateFeedbackDto,
    user: RagInternalUser | undefined,
  ): Promise<MessageFeedback> {
    const caller = this.assertCaller(user);
    const message = await this.resolveMessage(messageId);
    this.assertAssistantMessage(message);
    this.assertFeedbackOwner(message, caller);

    return this.chatRepository.upsertMessageFeedback({
      message,
      userId: caller.sub,
      rating: dto.rating,
    });
  }

  async listFeedback(
    query: ListFeedbackQueryDto,
    user: RagInternalUser | undefined,
  ): Promise<RagFeedbackPage> {
    const caller = this.assertCaller(user);
    if (caller.role !== SYSTEM_ADMIN_ROLE) {
      throw new ForbiddenException({
        errorCode: 'RAG_ADMIN_REQUIRED',
        detail: 'Only SYSTEM_ADMIN can audit RAG feedback',
      });
    }
    return this.chatRepository.listFeedback(query);
  }

  private async resolveMessage(messageId: string): Promise<RagMessageWithConversation> {
    const message = await this.chatRepository.findMessageWithConversation(messageId);
    if (!message) {
      throw new NotFoundException({
        errorCode: 'RAG_MESSAGE_NOT_FOUND',
        detail: `RAG message ${messageId} not found`,
      });
    }
    return message;
  }

  private assertAssistantMessage(message: RagMessageWithConversation): void {
    if (message.role !== 'ASSISTANT') {
      throw new UnprocessableEntityException({
        errorCode: 'RAG_FEEDBACK_ASSISTANT_ONLY',
        detail: 'Feedback is only allowed for assistant messages',
      });
    }
  }

  private assertFeedbackOwner(
    message: RagMessageWithConversation,
    caller: RagInternalUser & { sub: string; role: string },
  ): void {
    if (
      message.conversation.userId !== caller.sub ||
      message.conversation.role !== caller.role ||
      (message.conversation.operatorId ?? undefined) !== (caller.operatorId ?? undefined)
    ) {
      throw new ForbiddenException({
        errorCode: 'RAG_FEEDBACK_FORBIDDEN',
        detail: 'Caller cannot feedback this message',
      });
    }
  }

  private assertCaller(user: RagInternalUser | undefined): RagInternalUser & { sub: string; role: string } {
    if (!user?.sub || !user.role) {
      throw new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'Authenticated caller is required',
      });
    }
    return user as RagInternalUser & { sub: string; role: string };
  }
}
