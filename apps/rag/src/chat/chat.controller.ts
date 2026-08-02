import { Body, Controller, Post, Req, Res, UseGuards } from '@nestjs/common';
import { ApiBearerAuth, ApiBody, ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import type { Response } from 'express';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import {
  RAG_CHAT_SSE_EVENT_DONE,
  RAG_CHAT_SSE_EVENT_ERROR,
  RAG_CHAT_SSE_EVENT_TOKEN,
} from './chat.constants';
import { ChatService } from './chat.service';
import type { RagChatSseEvent } from './chat.types';
import { CreateChatDto, CreateChatSchema } from './dto/create-chat.dto';
import { errorEnvelopeSchema } from '../swagger/api-response.schemas';
import { ApiIdempotencyRequired } from '../swagger/idempotency.swagger';
import { RagSubscriptionEntitlementGuard } from '../subscriptions/rag-subscription-entitlement.guard';

const RAG_CHAT_REQUEST_MAX_MESSAGE_CHARS = 4_000;
const ragChatForbiddenSchema = {
  oneOf: [
    errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'Caller is not allowed to query RAG'),
    errorEnvelopeSchema(403, 'SUBSCRIPTION_MODULE_DISABLED', 'RAG module is disabled'),
  ],
};
const ragChatUnavailableSchema = {
  oneOf: [
    errorEnvelopeSchema(503, 'RAG_PROVIDER_UNAVAILABLE', 'Provider unavailable or circuit open'),
    errorEnvelopeSchema(503, 'UPSTREAM_UNAVAILABLE', 'Identity subscription is unavailable'),
  ],
};

@ApiTags('RAG Chat')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard, RagSubscriptionEntitlementGuard)
@Controller('v1/rag/chat')
export class ChatController {
  constructor(private readonly chatService: ChatService) {}

  @Post()
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Chat with RAG knowledge base using SSE' })
  @ApiBody({
    schema: {
      type: 'object',
      required: ['message'],
      properties: {
        conversationId: { type: 'string', format: 'uuid' },
        message: { type: 'string', minLength: 1, maxLength: RAG_CHAT_REQUEST_MAX_MESSAGE_CHARS },
        operatorId: { type: 'string', format: 'uuid', description: 'SYSTEM_ADMIN only: scope retrieval to specific operator' },
      },
    },
  })
  @ApiResponse({ status: 200, description: 'SSE text/event-stream. Events: token, done, error.' })
  @ApiResponse({ status: 400, description: 'Invalid payload', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Validation failed', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'Caller is not allowed to query RAG or the module is disabled', schema: ragChatForbiddenSchema })
  @ApiResponse({ status: 404, description: 'Conversation not found', schema: errorEnvelopeSchema(404, 'RAG_CONVERSATION_NOT_FOUND', 'Conversation not found') })
  @ApiResponse({ status: 429, description: 'Rate limit exceeded', schema: errorEnvelopeSchema(429, 'RATE_LIMIT_EXCEEDED', 'Rate limit exceeded') })
  @ApiResponse({ status: 503, description: 'RAG provider or Identity subscription lookup is unavailable', schema: ragChatUnavailableSchema })
  async create(
    @Body(new ZodValidationPipe(CreateChatSchema)) dto: CreateChatDto,
    @Req() req: RequestWithRagInternalUser,
    @Res() response: Response,
  ): Promise<void> {
    const prepared = await this.chatService.prepareChat(dto, req.user);
    response.status(200);
    response.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
    response.setHeader('Cache-Control', 'no-cache, no-transform');
    response.setHeader('Connection', 'keep-alive');
    response.flushHeaders?.();

    for await (const event of this.chatService.streamPrepared(prepared)) {
      response.write(this.formatSseEvent(event));
    }
    response.end();
  }

  private formatSseEvent(event: RagChatSseEvent): string {
    const eventName = this.toWireEventName(event.event);
    return `event: ${eventName}\ndata: ${JSON.stringify(event.data)}\n\n`;
  }

  private toWireEventName(event: RagChatSseEvent['event']): string {
    if (event === 'token') return RAG_CHAT_SSE_EVENT_TOKEN;
    if (event === 'done') return RAG_CHAT_SSE_EVENT_DONE;
    return RAG_CHAT_SSE_EVENT_ERROR;
  }
}
