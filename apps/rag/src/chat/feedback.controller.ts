import { Body, Controller, Get, Param, Post, Query, Req, UseGuards } from '@nestjs/common';
import {
  ApiBearerAuth,
  ApiBody,
  ApiHeader,
  ApiOperation,
  ApiParam,
  ApiQuery,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import {
  CreateFeedbackDto,
  CreateFeedbackSchema,
  MessageIdParamDto,
  MessageIdParamSchema,
} from './dto/create-feedback.dto';
import { ListFeedbackQueryDto, ListFeedbackQuerySchema } from './dto/list-feedback.dto';
import { FeedbackService } from './feedback.service';
import {
  errorEnvelopeSchema,
  internalAuthHeaderSchema,
  successEnvelopeSchema,
  pagedDataSchema,
} from '../swagger/api-response.schemas';

@ApiTags('RAG Feedback')
@ApiBearerAuth()
@ApiHeader(internalAuthHeaderSchema)
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/rag')
export class FeedbackController {
  constructor(private readonly feedbackService: FeedbackService) {}

  @Post('messages/:messageId/feedback')
  @ApiOperation({ summary: 'Create or update feedback for an assistant RAG message' })
  @ApiParam({ name: 'messageId', format: 'uuid', description: 'Assistant message ID' })
  @ApiBody({
    schema: {
      type: 'object',
      required: ['rating'],
      properties: {
        rating: { type: 'integer', enum: [-1, 1] },
      },
    },
  })
  @ApiResponse({ status: 201, description: 'Feedback recorded', schema: successEnvelopeSchema(201, { type: 'object', properties: { rating: { type: 'integer', example: 1 } } }) })
  @ApiResponse({ status: 400, description: 'Invalid payload', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Invalid payload', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid internal JWT') })
  @ApiResponse({ status: 403, description: 'Caller cannot feedback this message', schema: errorEnvelopeSchema(403, 'RAG_FEEDBACK_FORBIDDEN', 'Caller cannot feedback this message') })
  @ApiResponse({ status: 404, description: 'Message not found', schema: errorEnvelopeSchema(404, 'RAG_MESSAGE_NOT_FOUND', 'Message not found') })
  @ApiResponse({ status: 422, description: 'Feedback target is not an assistant message', schema: errorEnvelopeSchema(422, 'RAG_FEEDBACK_ASSISTANT_ONLY', 'Feedback target is not an assistant message') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async create(
    @Param('messageId', new ZodValidationPipe(MessageIdParamSchema)) messageId: MessageIdParamDto,
    @Body(new ZodValidationPipe(CreateFeedbackSchema)) dto: CreateFeedbackDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.feedbackService.createFeedback(messageId, dto, req.user);
  }

  @Get('feedback')
  @ApiOperation({ summary: 'List all RAG message feedback for admin audit' })
  @ApiQuery({ name: 'page', required: false, type: Number, minimum: 1 })
  @ApiQuery({ name: 'pageSize', required: false, type: Number, minimum: 1, maximum: 100 })
  @ApiQuery({ name: 'sortBy', required: false, enum: ['createdAt', 'rating'] })
  @ApiQuery({ name: 'sortDir', required: false, enum: ['asc', 'desc'] })
  @ApiResponse({ status: 200, description: 'Paginated feedback list', schema: successEnvelopeSchema(200, pagedDataSchema) })
  @ApiResponse({ status: 400, description: 'Invalid query', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Invalid query', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid internal JWT') })
  @ApiResponse({ status: 403, description: 'Only SYSTEM_ADMIN can audit feedback', schema: errorEnvelopeSchema(403, 'RAG_ADMIN_REQUIRED', 'Only SYSTEM_ADMIN can audit feedback') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async list(
    @Query(new ZodValidationPipe(ListFeedbackQuerySchema)) query: ListFeedbackQueryDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.feedbackService.listFeedback(query, req.user);
  }
}
