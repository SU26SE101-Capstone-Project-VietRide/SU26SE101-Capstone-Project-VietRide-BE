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
  @ApiOperation({ summary: 'Upload a RAG knowledge document' })
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
  @ApiResponse({ status: 201, description: 'Document uploaded' })
  @ApiResponse({ status: 400, description: 'Invalid payload or file' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  async create(
    @Body(new ZodValidationPipe(CreateDocumentSchema)) dto: CreateDocumentDto,
    @UploadedFile() file: UploadedDocumentFile | undefined,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<KnowledgeDocumentResponse> {
    return this.documentsService.create(dto, file, req.user);
  }

  @Put(':documentId/approve')
  @ApiOperation({ summary: 'Approve a RAG knowledge document for ingest' })
  @ApiParam({ name: 'documentId', format: 'uuid', description: 'Knowledge document ID' })
  @ApiResponse({ status: 200, description: 'Document approved' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  @ApiResponse({ status: 404, description: 'Document not found' })
  @ApiResponse({ status: 409, description: 'Document is not pending review' })
  async approve(
    @Param('documentId', new ParseUUIDPipe()) documentId: string,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<KnowledgeDocumentResponse> {
    return this.documentsService.approve(documentId, req.user);
  }
}
