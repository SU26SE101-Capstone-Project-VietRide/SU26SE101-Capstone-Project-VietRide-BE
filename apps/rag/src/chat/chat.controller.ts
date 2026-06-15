import { Body, Controller, Post, Req, Res, UseGuards } from '@nestjs/common';
import { ApiBearerAuth, ApiBody, ApiHeader, ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
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

const RAG_CHAT_SWAGGER_MAX_MESSAGE_CHARS = 500;

@ApiTags('RAG Chat')
@ApiBearerAuth()
@ApiHeader({
  name: 'X-Internal-Auth',
  description: 'Internal Gateway JWT header. Format: Bearer <token>.',
  required: true,
})
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/rag/chat')
export class ChatController {
  constructor(private readonly chatService: ChatService) {}

  @Post()
  @ApiOperation({ summary: 'Chat with RAG knowledge base using SSE' })
  @ApiBody({
    schema: {
      type: 'object',
      required: ['message'],
      properties: {
        conversationId: { type: 'string', format: 'uuid' },
        message: { type: 'string', minLength: 1, maxLength: RAG_CHAT_SWAGGER_MAX_MESSAGE_CHARS },
      },
    },
  })
  @ApiResponse({ status: 200, description: 'SSE stream with token and final citation events' })
  @ApiResponse({ status: 400, description: 'Invalid payload' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'Caller is not allowed to query RAG' })
  @ApiResponse({ status: 404, description: 'Conversation not found' })
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
