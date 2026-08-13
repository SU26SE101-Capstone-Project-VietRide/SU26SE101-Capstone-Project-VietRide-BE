import {
  BadRequestException,
  ForbiddenException,
  HttpException,
  Inject,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import pino from 'pino';
import { CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, ENV_TOKEN } from '../app/tokens';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import type { Env } from '../config/env.schema';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import { RuntimeConfigService, type RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type {
  KnowledgeDocumentAccess,
  RagConversation,
  RagConversationRole,
  RagMessage,
} from '../generated/rag-prisma-client';
import type { ChatCompletionProvider, ChatMessage } from '../providers/chat-completion.provider';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import type { CreateChatDto } from './dto/create-chat.dto';
import { RAG_CHAT_HISTORY_MESSAGE_LIMIT, RAG_RERANK_CANDIDATE_LIMIT } from './chat.constants';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatIntentService } from './chat-intent.service';
import { ChatQueryRewriteService } from './chat-query-rewrite.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatRepository } from './chat.repository';
import { ChatRerankService } from './chat-rerank.service';
import { ChatSummaryService } from './chat-summary.service';
import type {
  RagChatPreparedStream,
  RagChatSseEvent,
  RagFriendlyCitation,
  RagRetrievedChunk,
} from './chat.types';

const SYSTEM_ADMIN_ROLE = 'SYSTEM_ADMIN';
const OPERATOR_SCOPED_ROLES = new Set(['DRIVER', 'ASSISTANT', 'OPERATOR_STAFF', 'OPERATOR_ADMIN']);
const PASSENGER_ROLE = 'PASSENGER';
const SAFE_PROVIDER_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  RAG_PROVIDER_RATE_LIMITED: 'RAG chat provider rate limit reached',
  RAG_PROVIDER_CIRCUIT_OPEN: 'RAG chat provider circuit is open',
  RAG_PROVIDER_INVALID_RESPONSE: 'RAG chat provider returned an invalid response',
  RAG_PROVIDER_UNAVAILABLE: 'RAG chat provider is unavailable',
};
const logger = pino({ name: 'RagChatService' });

@Injectable()
export class ChatService {
  constructor(
    private readonly chatRepository: ChatRepository,
    private readonly embeddingCache: ChatEmbeddingCacheService,
    private readonly rateLimit: ChatRateLimitService,
    private readonly intentService: ChatIntentService,
    private readonly queryRewriteService: ChatQueryRewriteService,
    private readonly summaryService: ChatSummaryService,
    private readonly rerankService: ChatRerankService,
    @Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider,
    @Inject(EMBEDDING_PROVIDER) private readonly embeddingProvider: EmbeddingProvider,
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly runtimeConfig: RuntimeConfigService,
  ) {}

  async prepareChat(
    dto: CreateChatDto,
    user: RagInternalUser | undefined,
  ): Promise<RagChatPreparedStream> {
    const caller = this.assertCaller(user);

    if (dto.operatorId && caller.role !== SYSTEM_ADMIN_ROLE) {
      throw new ForbiddenException({
        errorCode: 'RAG_OPERATOR_SCOPE_FORBIDDEN',
        detail: 'Only SYSTEM_ADMIN can specify operatorId',
      });
    }

    const runtimeConfig = await this.runtimeConfig.getSnapshot();
    this.assertMessageLength(dto.message);
    await this.rateLimit.assertAllowed(caller);
    const { conversation, effectiveOperatorId } = await this.resolveConversation(
      dto.conversationId,
      caller,
      caller.role === SYSTEM_ADMIN_ROLE ? dto.operatorId : undefined,
    );
    const history = await this.chatRepository.findRecentMessages(
      conversation.id,
      RAG_CHAT_HISTORY_MESSAGE_LIMIT,
    );
    const userMessage = await this.chatRepository.createUserMessage(conversation.id, dto.message);

    if (this.env.INTENT_FILTER_ENABLED) {
      const intent = await this.intentService.classify(dto.message, history, runtimeConfig);
      if (!intent.allowed) {
        return {
          conversation,
          userMessage,
          chunks: [],
          stream: this.makeSingleTokenStream(intent.refusalMessage ?? ''),
          shouldSummarize: false,
          runtimeConfig,
        };
      }
    }

    const retrievalQuery = this.env.QUERY_REWRITE_ENABLED
      ? await this.queryRewriteService.rewriteIfNeeded(
          dto.message,
          history,
          conversation.summary,
          runtimeConfig,
        )
      : dto.message;
    const queryEmbedding = await this.resolveQueryEmbedding(retrievalQuery);
    const accessLevels = this.resolveAccessLevels(caller.role);
    const retrievedChunks = await this.chatRepository.searchChunks({
      queryText: retrievalQuery,
      queryEmbedding,
      accessLevels,
      ...(effectiveOperatorId ? { operatorId: effectiveOperatorId } : {}),
      callerRole: caller.role,
      limit: this.env.RERANK_ENABLED
        ? RAG_RERANK_CANDIDATE_LIMIT
        : this.env.RAG_MAX_RETRIEVED_CHUNKS,
      hybridSearchEnabled: this.env.HYBRID_SEARCH_ENABLED,
    });
    const chunks = this.env.RERANK_ENABLED
      ? await this.rerankService.rerank(retrievalQuery, retrievedChunks, runtimeConfig)
      : retrievedChunks;
    const contextChunks = this.selectContextChunks(chunks);
    const messages = this.buildProviderMessages(
      dto.message,
      history,
      contextChunks,
      conversation.summary,
      runtimeConfig,
    );
    const stream = this.chatProvider.stream({
      messages,
      stream: true,
      temperature: 0,
      reasoning: { enabled: false },
    });
    return {
      conversation,
      userMessage,
      chunks: contextChunks,
      stream,
      shouldSummarize: this.env.SUMMARIZE_ENABLED,
      runtimeConfig,
    };
  }

  async *streamPrepared(prepared: RagChatPreparedStream): AsyncIterable<RagChatSseEvent> {
    const citedChunkIds = prepared.chunks.map((chunk) => chunk.id);
    const citations = this.buildFriendlyCitations(prepared.chunks);
    let assistantContent = '';

    try {
      for await (const token of prepared.stream) {
        assistantContent += token;
        yield { event: 'token', data: { content: token } };
      }
    } catch (error) {
      logger.warn(
        { error: this.toSafeErrorLog(error), conversationId: prepared.conversation.id },
        'RAG chat provider stream failed',
      );
      yield this.toProviderSseError(error);
      return;
    }

    let assistantMessage: RagMessage;
    try {
      assistantMessage = await this.chatRepository.createAssistantMessage(
        prepared.conversation.id,
        assistantContent,
        citedChunkIds,
      );
    } catch (error) {
      logger.warn(
        { error: this.toSafeErrorLog(error), conversationId: prepared.conversation.id },
        'RAG assistant message persistence failed',
      );
      yield {
        event: 'error',
        data: {
          code: 'INTERNAL_ERROR',
          message: 'RAG response could not be saved',
        },
      };
      return;
    }

    if (prepared.shouldSummarize) {
      try {
        await this.summaryService.summarizeIfNeeded(
          prepared.conversation,
          assistantMessage.id,
          prepared.runtimeConfig,
        );
      } catch (error) {
        logger.warn(
          { error: this.toSafeErrorLog(error), conversationId: prepared.conversation.id },
          'RAG chat summarization skipped',
        );
      }
    }

    yield {
      event: 'done',
      data: {
        conversationId: prepared.conversation.id,
        userMessageId: prepared.userMessage.id,
        assistantMessageId: assistantMessage.id,
        citations,
      },
    };
  }

  private assertCaller(
    user: RagInternalUser | undefined,
  ): RagInternalUser & { role: RagConversationRole } {
    if (!user?.role) {
      throw new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'Authenticated caller role is required',
      });
    }
    if (!this.isSupportedRole(user.role)) {
      throw new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'Caller role is not allowed to chat with RAG',
      });
    }
    if (OPERATOR_SCOPED_ROLES.has(user.role) && !user.operatorId) {
      throw new ForbiddenException({
        errorCode: 'RAG_OPERATOR_SCOPE_REQUIRED',
        detail: 'Operator-scoped roles require operatorId',
      });
    }
    return user as RagInternalUser & { role: RagConversationRole };
  }

  private assertMessageLength(message: string): void {
    if (message.length > this.env.RAG_MAX_MESSAGE_CHARS) {
      throw new BadRequestException({
        errorCode: 'RAG_MESSAGE_TOO_LONG',
        detail: 'RAG chat message exceeds configured limit',
      });
    }
  }

  private async resolveConversation(
    conversationId: string | undefined,
    user: RagInternalUser & { role: RagConversationRole },
    dtoOperatorId: string | undefined,
  ): Promise<{ conversation: RagConversation; effectiveOperatorId: string | undefined }> {
    if (!conversationId) {
      const effectiveOperatorId = user.role === SYSTEM_ADMIN_ROLE ? dtoOperatorId : user.operatorId;
      const conversation = await this.chatRepository.createConversation({
        userId: user.sub,
        role: user.role,
        ...(effectiveOperatorId ? { operatorId: effectiveOperatorId } : {}),
      });
      return { conversation, effectiveOperatorId };
    }

    const conversation = await this.chatRepository.findConversation(conversationId);
    if (!conversation) {
      throw new NotFoundException({
        errorCode: 'RAG_CONVERSATION_NOT_FOUND',
        detail: `RAG conversation ${conversationId} not found`,
      });
    }
    if (conversation.userId !== user.sub || conversation.role !== user.role) {
      throw new ForbiddenException({
        errorCode: 'RAG_CONVERSATION_FORBIDDEN',
        detail: 'Conversation does not belong to caller',
      });
    }

    if (user.role === SYSTEM_ADMIN_ROLE) {
      if (dtoOperatorId && conversation.operatorId && dtoOperatorId !== conversation.operatorId) {
        throw new ForbiddenException({
          errorCode: 'RAG_CONVERSATION_SCOPE_MISMATCH',
          detail: 'Cannot change operator scope of existing conversation',
        });
      }
      const effectiveOperatorId = conversation.operatorId ?? dtoOperatorId;
      return { conversation, effectiveOperatorId };
    }

    const effectiveOperatorId = user.operatorId;
    if ((conversation.operatorId ?? null) !== (effectiveOperatorId ?? null)) {
      throw new ForbiddenException({
        errorCode: 'RAG_CONVERSATION_FORBIDDEN',
        detail: 'Conversation does not belong to caller',
      });
    }
    return { conversation, effectiveOperatorId };
  }

  private resolveAccessLevels(role: RagConversationRole): KnowledgeDocumentAccess[] {
    if (role === PASSENGER_ROLE) return ['PUBLIC'];
    if (role === SYSTEM_ADMIN_ROLE) return ['PUBLIC', 'OPERATOR', 'ADMIN'];
    return ['PUBLIC', 'OPERATOR'];
  }

  private async resolveQueryEmbedding(message: string): Promise<number[]> {
    const cached = await this.embeddingCache.get(message);
    if (cached) return cached;

    const embedding = await this.embeddingProvider.embed({ input: message });
    await this.embeddingCache.set(message, embedding);
    return embedding;
  }

  private buildProviderMessages(
    currentMessage: string,
    history: RagMessage[],
    chunks: RagRetrievedChunk[],
    summary: string | null,
    runtimeConfig: RuntimeConfigSnapshot,
  ): ChatMessage[] {
    return [
      {
        role: 'system',
        content: this.buildSystemPrompt(chunks, summary, runtimeConfig),
      },
      ...history.map(
        (message): ChatMessage => ({
          role: message.role === 'USER' ? 'user' : 'assistant',
          content: message.content,
        }),
      ),
      {
        role: 'user',
        content: currentMessage,
      },
    ];
  }

  private buildSystemPrompt(
    chunks: RagRetrievedChunk[],
    summary: string | null,
    runtimeConfig: RuntimeConfigSnapshot,
  ): string {
    // Retrieved context stays in the prompt for MVP; system prompt instructs the model to treat it as untrusted.
    return runtimeConfig
      .getString(RAG_RUNTIME_CONFIG_KEYS.chatSystemPrompt)
      .replaceAll(
        '{conversation_summary}',
        summary ?? runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.chatNoSummaryText),
      )
      .replaceAll('{retrieved_context}', this.buildContextBlock(chunks, runtimeConfig))
      .replaceAll(
        '{insufficient_context_message}',
        runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.chatInsufficientContextMessage),
      );
  }

  private buildContextBlock(
    chunks: RagRetrievedChunk[],
    runtimeConfig: RuntimeConfigSnapshot,
  ): string {
    if (chunks.length === 0) {
      return runtimeConfig.getString(RAG_RUNTIME_CONFIG_KEYS.chatNoContextText);
    }

    return chunks
      .map((chunk) =>
        [
          `Tiêu đề tài liệu: ${chunk.documentTitle}`,
          `Mục: ${chunk.sectionHeader ?? 'Không có tiêu đề mục'}`,
          `Loại tài liệu: ${chunk.documentType}`,
          chunk.content,
        ].join('\n'),
      )
      .join('\n\n');
  }

  private buildFriendlyCitations(chunks: RagRetrievedChunk[]): RagFriendlyCitation[] {
    const seen = new Set<string>();
    const citations: RagFriendlyCitation[] = [];

    for (const chunk of chunks) {
      const citation = {
        title: chunk.documentTitle,
        section: chunk.sectionHeader,
      };
      const key = JSON.stringify([citation.title, citation.section]);
      if (seen.has(key)) continue;
      seen.add(key);
      citations.push(citation);
    }

    return citations;
  }

  private selectContextChunks(chunks: RagRetrievedChunk[]): RagRetrievedChunk[] {
    // tokenCount is currently an ingest-time whitespace word count; keep this budget conservative.
    let totalTokens = 0;
    const selected: RagRetrievedChunk[] = [];
    for (const chunk of chunks) {
      if (totalTokens + chunk.tokenCount > this.env.RAG_MAX_CONTEXT_TOKENS) break;
      totalTokens += chunk.tokenCount;
      selected.push(chunk);
    }
    return selected;
  }

  private toProviderSseError(error: unknown): RagChatSseEvent {
    const candidateCode = this.readHttpErrorCode(error);
    const code =
      candidateCode && candidateCode in SAFE_PROVIDER_ERROR_MESSAGES
        ? candidateCode
        : 'RAG_PROVIDER_UNAVAILABLE';
    return {
      event: 'error',
      data: {
        code,
        message: SAFE_PROVIDER_ERROR_MESSAGES[code] ?? 'RAG chat provider is unavailable',
      },
    };
  }

  private readHttpErrorCode(error: unknown): string | undefined {
    if (!(error instanceof HttpException)) return undefined;
    const response = error.getResponse();
    if (!response || typeof response !== 'object' || !('errorCode' in response)) return undefined;
    return typeof response.errorCode === 'string' ? response.errorCode : undefined;
  }

  private toSafeErrorLog(error: unknown): { name: string; status?: number; code?: string } {
    if (error instanceof Error) {
      const status = 'getStatus' in error ? (error.getStatus as () => number)() : undefined;
      const code = this.readHttpErrorCode(error);
      return { name: error.name, ...(status ? { status } : {}), ...(code ? { code } : {}) };
    }
    return { name: 'UnknownError' };
  }

  private isSupportedRole(role: string): role is RagConversationRole {
    return role === PASSENGER_ROLE || role === SYSTEM_ADMIN_ROLE || OPERATOR_SCOPED_ROLES.has(role);
  }

  private async *makeSingleTokenStream(content: string): AsyncIterable<string> {
    yield content;
  }
}
