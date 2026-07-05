import {
  Body,
  Controller,
  Param,
  ParseUUIDPipe,
  Post,
  Put,
  Req,
  UploadedFile,
  UseGuards,
  UseInterceptors,
} from '@nestjs/common';
import { FileInterceptor } from '@nestjs/platform-express';
import {
  ApiBearerAuth,
  ApiBody,
  ApiConsumes,
  ApiOperation,
  ApiParam,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import { CreateDocumentDto, CreateDocumentSchema } from './dto/create-document.dto';
import {
  RAG_DOCUMENT_FILE_FIELD,
  RAG_DOCUMENT_UPLOAD_HARD_CAP_BYTES,
} from './documents.constants';
import { DocumentsService } from './documents.service';
import type { KnowledgeDocumentResponse, UploadedDocumentFile } from './documents.types';
import {
  errorEnvelopeSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';

@ApiTags('RAG Documents')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/rag/documents')
export class DocumentsController {
  constructor(private readonly documentsService: DocumentsService) {}

  @Post()
  @UseInterceptors(
    FileInterceptor(RAG_DOCUMENT_FILE_FIELD, {
      limits: { fileSize: RAG_DOCUMENT_UPLOAD_HARD_CAP_BYTES },
    }),
  )
  @ApiOperation({ summary: 'Upload, auto-approve, and request ingest for a RAG knowledge document' })
  @ApiConsumes('multipart/form-data')
  @ApiBody({
    schema: {
      type: 'object',
      required: ['file', 'title', 'accessLevel', 'category', 'documentType'],
      properties: {
        file: { type: 'string', format: 'binary' },
        title: { type: 'string', maxLength: 500 },
        description: { type: 'string' },
        accessLevel: { type: 'string', enum: ['PUBLIC', 'OPERATOR', 'ADMIN'] },
        operatorId: { type: 'string', format: 'uuid' },
        category: {
          type: 'string',
          enum: ['CUSTOMER_SUPPORT', 'OPERATOR_POLICY', 'PLATFORM_ADMIN'],
        },
        documentType: { type: 'string', enum: ['FAQ', 'POLICY', 'SOP', 'GUIDE', 'TERMS'] },
        audienceRoles: {
          type: 'string',
          description: 'Comma-separated roles or JSON array',
        },
        language: { type: 'string', enum: ['vi'] },
      },
    },
  })
  @ApiResponse({ status: 201, description: 'Document uploaded, approved, and queued for ingest', schema: successEnvelopeSchema(201, { type: 'object', properties: { id: { type: 'string', format: 'uuid' } } }) })
  @ApiResponse({ status: 400, description: 'Invalid payload or file', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Invalid payload or file', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  @ApiResponse({ status: 503, description: 'Storage or provider unavailable', schema: errorEnvelopeSchema(503, 'SERVICE_UNAVAILABLE', 'Storage or provider unavailable') })
  async create(
    @Body(new ZodValidationPipe(CreateDocumentSchema)) dto: CreateDocumentDto,
    @UploadedFile() file: UploadedDocumentFile | undefined,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<KnowledgeDocumentResponse> {
    return this.documentsService.create(dto, file, req.user);
  }

  @Put(':documentId/approve')
  @ApiOperation({ summary: 'Approve a pending RAG knowledge document for ingest' })
  @ApiParam({ name: 'documentId', format: 'uuid', description: 'Knowledge document ID' })
  @ApiResponse({ status: 200, description: 'Document approved', schema: successEnvelopeSchema(200, { type: 'object', properties: { id: { type: 'string', format: 'uuid' }, status: { type: 'string' } } }) })
  @ApiResponse({ status: 400, description: 'Invalid UUID', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Invalid UUID', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 404, description: 'Document not found', schema: errorEnvelopeSchema(404, 'DOCUMENT_NOT_FOUND', 'Document not found') })
  @ApiResponse({ status: 409, description: 'Document is not pending review', schema: errorEnvelopeSchema(409, 'DOCUMENT_NOT_PENDING', 'Document is not pending review') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async approve(
    @Param('documentId', new ParseUUIDPipe()) documentId: string,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<KnowledgeDocumentResponse> {
    return this.documentsService.approve(documentId, req.user);
  }
}
