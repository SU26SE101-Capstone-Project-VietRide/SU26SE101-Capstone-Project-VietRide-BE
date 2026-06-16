import type {
  KnowledgeDocumentAccess,
  KnowledgeDocumentType,
  MessageFeedback,
  RagConversation,
  RagMessage,
} from '../generated/rag-prisma-client';
import type { RuntimeConfigSnapshot } from '../config/runtime-config.service';

export interface RagRetrievedChunk {
  id: string;
  documentId: string;
  documentTitle: string;
  sectionHeader: string | null;
  documentType: KnowledgeDocumentType;
  content: string;
  tokenCount: number;
  accessLevel: KnowledgeDocumentAccess;
  operatorId: string | null;
  distance: number;
}

export interface RagChatPreparedStream {
  conversation: RagConversation;
  userMessage: RagMessage;
  chunks: RagRetrievedChunk[];
  stream: AsyncIterable<string>;
  shouldSummarize: boolean;
  runtimeConfig: RuntimeConfigSnapshot;
}

export interface RagMessageWithConversation extends RagMessage {
  conversation: RagConversation;
}

export interface RagFeedbackListItem extends MessageFeedback {
  message: Pick<RagMessage, 'id' | 'role' | 'content' | 'citedChunkIds' | 'createdAt'>;
  conversation: Pick<RagConversation, 'id' | 'userId' | 'operatorId' | 'role'>;
}

export interface RagFeedbackPage {
  items: RagFeedbackListItem[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export type RagChatSseEvent =
  | {
      event: 'token';
      data: {
        content: string;
      };
    }
  | {
      event: 'done';
      data: {
        conversationId: string;
        userMessageId: string;
        assistantMessageId: string;
        citedChunkIds: string[];
      };
    }
  | {
      event: 'error';
      data: {
        code: string;
        message: string;
      };
    };
