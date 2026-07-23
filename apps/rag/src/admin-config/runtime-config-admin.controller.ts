import { Body, Controller, Get, Param, Patch, Post, Req, UseGuards } from '@nestjs/common';
import { ApiBearerAuth, ApiBody, ApiOperation, ApiParam, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import {
  RollbackRuntimeConfigDto,
  RollbackRuntimeConfigSchema,
  RuntimeConfigKeyParamDto,
  RuntimeConfigKeyParamSchema,
  UpdateRuntimeConfigDto,
  UpdateRuntimeConfigSchema,
} from './dto/runtime-config.dto';
import { RuntimeConfigAdminService } from './runtime-config-admin.service';
import {
  errorEnvelopeSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';
import {
  ApiIdempotencyExempt,
  ApiIdempotencyRequired,
} from '../swagger/idempotency.swagger';

@ApiTags('RAG Runtime Config')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/admin/rag-config')
export class RuntimeConfigAdminController {
  constructor(private readonly adminService: RuntimeConfigAdminService) {}

  @Get()
  @ApiOperation({ summary: 'List RAG runtime config keys' })
  @ApiResponse({ status: 200, description: 'Runtime config list', schema: successEnvelopeSchema(200, { type: 'array', items: { type: 'object' } }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async list(@Req() req: RequestWithRagInternalUser) {
    return this.adminService.list(req.user);
  }

  @Post('reload')
  @ApiIdempotencyExempt(
    'Runtime-config reload only invalidates an in-memory cache and is naturally repeatable.',
  )
  @ApiOperation({ summary: 'Reload RAG runtime config cache manually' })
  @ApiResponse({ status: 201, description: 'Runtime config cache reloaded', schema: successEnvelopeSchema(201, { type: 'object', properties: { reloaded: { type: 'boolean', example: true } } }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async reload(@Req() req: RequestWithRagInternalUser) {
    return this.adminService.reload(req.user);
  }

  @Get(':key')
  @ApiOperation({ summary: 'Get one RAG runtime config key with history' })
  @ApiParam({ name: 'key', description: 'Runtime config key' })
  @ApiResponse({ status: 200, description: 'Runtime config detail', schema: successEnvelopeSchema(200, { type: 'object', properties: { key: { type: 'string' }, value: {} } }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 404, description: 'Config key not found', schema: errorEnvelopeSchema(404, 'RUNTIME_CONFIG_NOT_FOUND', 'Config key not found') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async get(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.get(params.key, req.user);
  }

  @Patch(':key')
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Update one RAG runtime config key' })
  @ApiParam({ name: 'key', description: 'Runtime config key' })
  @ApiBody({
    schema: {
      type: 'object',
      required: ['value'],
      properties: {
        value: {
          oneOf: [
            { type: 'string' },
            { type: 'number' },
            { type: 'boolean' },
            { type: 'array', items: { type: 'string' } },
          ],
        },
        reason: { type: 'string', maxLength: 500 },
      },
    },
  })
  @ApiResponse({ status: 200, description: 'Runtime config updated', schema: successEnvelopeSchema(200, { type: 'object', properties: { key: { type: 'string' }, value: {} } }) })
  @ApiResponse({ status: 400, description: 'Invalid config value', schema: errorEnvelopeSchema(400, 'RUNTIME_CONFIG_INVALID_VALUE', 'Invalid config value') })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required or config key is read-only', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required or config key is read-only') })
  @ApiResponse({ status: 404, description: 'Config key not found', schema: errorEnvelopeSchema(404, 'RUNTIME_CONFIG_NOT_FOUND', 'Config key not found') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async update(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Body(new ZodValidationPipe(UpdateRuntimeConfigSchema)) dto: UpdateRuntimeConfigDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.update(params.key, dto, req.user);
  }

  @Get(':key/history')
  @ApiOperation({ summary: 'List one RAG runtime config history' })
  @ApiParam({ name: 'key', description: 'Runtime config key' })
  @ApiResponse({ status: 200, description: 'Runtime config history', schema: successEnvelopeSchema(200, { type: 'array', items: { type: 'object' } }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required') })
  @ApiResponse({ status: 404, description: 'Config key not found', schema: errorEnvelopeSchema(404, 'RUNTIME_CONFIG_NOT_FOUND', 'Config key not found') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async history(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.history(params.key, req.user);
  }

  @Post(':key/rollback')
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Rollback one RAG runtime config key to a history entry' })
  @ApiParam({ name: 'key', description: 'Runtime config key' })
  @ApiBody({
    schema: {
      type: 'object',
      required: ['historyId'],
      properties: {
        historyId: { type: 'string', format: 'uuid' },
      },
    },
  })
  @ApiResponse({ status: 201, description: 'Runtime config rolled back', schema: successEnvelopeSchema(201, { type: 'object', properties: { key: { type: 'string' }, value: {} } }) })
  @ApiResponse({ status: 400, description: 'Invalid payload', schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Invalid payload', { fields: true }) })
  @ApiResponse({ status: 401, description: 'Missing or invalid access token', schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Missing or invalid access token') })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required or config key is read-only', schema: errorEnvelopeSchema(403, 'INSUFFICIENT_ROLE', 'SYSTEM_ADMIN role is required or config key is read-only') })
  @ApiResponse({ status: 404, description: 'Config key or history not found', schema: errorEnvelopeSchema(404, 'RUNTIME_CONFIG_NOT_FOUND', 'Config key or history not found') })
  @ApiResponse({ status: 500, description: 'Unexpected error', schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error') })
  async rollback(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Body(new ZodValidationPipe(RollbackRuntimeConfigSchema)) dto: RollbackRuntimeConfigDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.rollback(params.key, dto, req.user);
  }
}
