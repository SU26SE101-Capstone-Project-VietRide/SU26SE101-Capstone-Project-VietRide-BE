import type {
  KnowledgeDocumentAccess,
  KnowledgeDocumentType,
  RagConversation,
  RagMessage,
} from '../generated/rag-prisma-client';

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
