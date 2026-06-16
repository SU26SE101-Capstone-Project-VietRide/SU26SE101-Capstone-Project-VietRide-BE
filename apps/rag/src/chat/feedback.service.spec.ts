import { ForbiddenException, UnprocessableEntityException } from '@nestjs/common';
import type { MessageFeedback } from '../generated/rag-prisma-client';
import { ChatRepository } from './chat.repository';
import { FeedbackService } from './feedback.service';
import type { RagFeedbackPage, RagMessageWithConversation } from './chat.types';

const USER_ID = '11111111-1111-1111-1111-111111111111';
const OTHER_USER_ID = '22222222-2222-2222-2222-222222222222';
const CONVERSATION_ID = '33333333-3333-3333-3333-333333333333';
const ASSISTANT_MESSAGE_ID = '44444444-4444-4444-4444-444444444444';
const USER_MESSAGE_ID = '55555555-5555-5555-5555-555555555555';

describe('FeedbackService', () => {
  let service: FeedbackService;
  let repository: jest.Mocked<ChatRepository>;

  beforeEach(() => {
    repository = {
      findMessageWithConversation: jest.fn(),
      upsertMessageFeedback: jest.fn(),
      listFeedback: jest.fn(),
    } as unknown as jest.Mocked<ChatRepository>;
    service = new FeedbackService(repository);
    repository.findMessageWithConversation.mockResolvedValue(makeMessage('ASSISTANT'));
    repository.upsertMessageFeedback.mockResolvedValue(makeFeedback());
    repository.listFeedback.mockResolvedValue(makeFeedbackPage());
  });

  it('records feedback for caller-owned assistant messages', async () => {
    const result = await service.createFeedback(
      ASSISTANT_MESSAGE_ID,
      { rating: 1 },
      { sub: USER_ID, role: 'PASSENGER' },
    );

    expect(result.rating).toBe(1);
    expect(repository.upsertMessageFeedback).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        rating: 1,
      }),
    );
  });

  it('rejects feedback for user messages', async () => {
    repository.findMessageWithConversation.mockResolvedValue(makeMessage('USER'));

    await expect(
      service.createFeedback(USER_MESSAGE_ID, { rating: -1 }, { sub: USER_ID, role: 'PASSENGER' }),
    ).rejects.toBeInstanceOf(UnprocessableEntityException);
  });

  it('rejects non-owner feedback', async () => {
    await expect(
      service.createFeedback(
        ASSISTANT_MESSAGE_ID,
        { rating: -1 },
        { sub: OTHER_USER_ID, role: 'PASSENGER' },
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('allows SYSTEM_ADMIN to audit all feedback', async () => {
    const result = await service.listFeedback(
      { page: 1, pageSize: 20, sortBy: 'createdAt', sortDir: 'desc' },
      { sub: OTHER_USER_ID, role: 'SYSTEM_ADMIN' },
    );

    expect(result.totalItems).toBe(1);
  });
});

function makeMessage(role: 'USER' | 'ASSISTANT'): RagMessageWithConversation {
  return {
    id: role === 'USER' ? USER_MESSAGE_ID : ASSISTANT_MESSAGE_ID,
    conversationId: CONVERSATION_ID,
    role,
    content: 'Câu trả lời',
    citedChunkIds: [],
    tokensUsed: null,
    createdAt: new Date('2026-06-14T00:00:00.000Z'),
    conversation: {
      id: CONVERSATION_ID,
      userId: USER_ID,
      operatorId: null,
      role: 'PASSENGER',
      summary: null,
      summaryUpdatedAt: null,
      summaryFromMessageId: null,
      startedAt: new Date('2026-06-14T00:00:00.000Z'),
      lastMessageAt: null,
      createdAt: new Date('2026-06-14T00:00:00.000Z'),
    },
  };
}

function makeFeedback(): MessageFeedback {
  return {
    id: '66666666-6666-6666-6666-666666666666',
    messageId: ASSISTANT_MESSAGE_ID,
    conversationId: CONVERSATION_ID,
    userId: USER_ID,
    rating: 1,
    queryRewritten: null,
    chunkIds: [],
    responseLength: 11,
    createdAt: new Date('2026-06-14T00:00:00.000Z'),
    updatedAt: new Date('2026-06-14T00:00:00.000Z'),
  };
}

function makeFeedbackPage(): RagFeedbackPage {
  return {
    items: [],
    page: 1,
    pageSize: 20,
    totalItems: 1,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
  };
}
