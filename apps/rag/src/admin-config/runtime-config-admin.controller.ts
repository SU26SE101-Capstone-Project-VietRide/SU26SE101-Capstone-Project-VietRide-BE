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

@ApiTags('RAG Runtime Config')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/admin/rag-config')
export class RuntimeConfigAdminController {
  constructor(private readonly adminService: RuntimeConfigAdminService) {}

  @Get()
  @ApiOperation({ summary: 'List RAG runtime config keys' })
  @ApiResponse({ status: 200, description: 'Runtime config list' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  async list(@Req() req: RequestWithRagInternalUser) {
    return this.adminService.list(req.user);
  }

  @Get(':key')
  @ApiOperation({ summary: 'Get one RAG runtime config key with history' })
  @ApiParam({ name: 'key', description: 'Runtime config key' })
  @ApiResponse({ status: 200, description: 'Runtime config detail' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  @ApiResponse({ status: 404, description: 'Config key not found' })
  async get(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.get(params.key, req.user);
  }

  @Patch(':key')
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
  @ApiResponse({ status: 200, description: 'Runtime config updated' })
  @ApiResponse({ status: 400, description: 'Invalid config value' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  @ApiResponse({ status: 404, description: 'Config key not found' })
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
  @ApiResponse({ status: 200, description: 'Runtime config history' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  @ApiResponse({ status: 404, description: 'Config key not found' })
  async history(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.history(params.key, req.user);
  }

  @Post(':key/rollback')
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
  @ApiResponse({ status: 201, description: 'Runtime config rolled back' })
  @ApiResponse({ status: 400, description: 'Invalid payload' })
  @ApiResponse({ status: 401, description: 'Missing or invalid internal JWT' })
  @ApiResponse({ status: 403, description: 'SYSTEM_ADMIN role is required' })
  @ApiResponse({ status: 404, description: 'Config key or history not found' })
  async rollback(
    @Param(new ZodValidationPipe(RuntimeConfigKeyParamSchema)) params: RuntimeConfigKeyParamDto,
    @Body(new ZodValidationPipe(RollbackRuntimeConfigSchema)) dto: RollbackRuntimeConfigDto,
    @Req() req: RequestWithRagInternalUser,
  ) {
    return this.adminService.rollback(params.key, dto, req.user);
  }
}
