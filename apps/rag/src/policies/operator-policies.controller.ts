import {
  Body,
  Controller,
  Delete,
  Get,
  Param,
  Patch,
  Post,
  Query,
  Req,
  UseGuards,
} from '@nestjs/common';
import {
  ApiBearerAuth,
  ApiBody,
  ApiOperation,
  ApiParam,
  ApiQuery,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import {
  errorEnvelopeSchema,
  pagedDataSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';
import { ApiIdempotencyRequired } from '../swagger/idempotency.swagger';
import { CreatePolicyDto, CreatePolicySchema } from './dto/create-policy.dto';
import { ListPoliciesQueryDto, ListPoliciesValidationSchema } from './dto/list-policies.dto';
import { UpdatePolicyDto, UpdatePolicySchema } from './dto/update-policy.dto';
import { PoliciesService } from './policies.service';
import {
  policyCreateBodySchema,
  policyMutation409Schema,
  policyMutation422Schema,
  policySchema,
  policyUpdateBodySchema,
  policyValidationErrorSchema,
} from './policies.swagger';
import type { PolicyPage, PolicyResponse } from './policies.types';
import { PolicyUuidPipe } from './policy-uuid.pipe';
import { PolicyValidationPipe } from './policy-validation.pipe';

@ApiTags('Operator Policies')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/operator/policies')
export class OperatorPoliciesController {
  constructor(private readonly policies: PoliciesService) {}

  @Get()
  @ApiOperation({ summary: 'List Policies for the caller operator tenant' })
  @ApiQuery({ name: 'policyType', required: false, enum: ['FOR_OPERATOR', 'FOR_USER'] })
  @ApiQuery({ name: 'category', required: false, type: String })
  @ApiQuery({ name: 'active', required: false, enum: ['true', 'false'] })
  @ApiQuery({ name: 'search', required: false, type: String })
  @ApiQuery({ name: 'page', required: false, type: Number, minimum: 1 })
  @ApiQuery({ name: 'pageSize', required: false, type: Number, minimum: 1, maximum: 100 })
  @ApiQuery({
    name: 'sortBy',
    required: false,
    enum: ['updatedAt', 'createdAt', 'title', 'version'],
  })
  @ApiQuery({ name: 'sortDir', required: false, enum: ['asc', 'desc'] })
  @ApiResponse({
    status: 200,
    schema: successEnvelopeSchema(200, {
      ...pagedDataSchema,
      properties: { ...pagedDataSchema.properties, items: { type: 'array', items: policySchema } },
    }),
  })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 403, schema: errorEnvelopeSchema(403, 'FORBIDDEN', 'Forbidden') })
  @ApiResponse({ status: 422, schema: policyValidationErrorSchema })
  list(
    @Query(new PolicyValidationPipe(ListPoliciesValidationSchema)) query: ListPoliciesQueryDto,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<PolicyPage> {
    return this.policies.list('OPERATOR', query, req.user);
  }

  @Get(':policyId')
  @ApiOperation({ summary: 'Get a Policy in the caller operator tenant' })
  @ApiParam({ name: 'policyId', format: 'uuid' })
  @ApiResponse({ status: 200, schema: successEnvelopeSchema(200, policySchema) })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 403, schema: errorEnvelopeSchema(403, 'FORBIDDEN', 'Forbidden') })
  @ApiResponse({
    status: 404,
    schema: errorEnvelopeSchema(404, 'POLICY_NOT_FOUND', 'Policy not found'),
  })
  @ApiResponse({ status: 422, schema: policyValidationErrorSchema })
  get(
    @Param('policyId', new PolicyUuidPipe()) policyId: string,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<PolicyResponse> {
    return this.policies.get('OPERATOR', policyId, req.user);
  }

  @Post()
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Create a Policy in the caller operator tenant' })
  @ApiBody({ schema: policyCreateBodySchema })
  @ApiResponse({ status: 201, schema: successEnvelopeSchema(201, policySchema) })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 403, schema: errorEnvelopeSchema(403, 'FORBIDDEN', 'Forbidden') })
  @ApiResponse({
    status: 409,
    schema: errorEnvelopeSchema(409, 'IDEMPOTENCY_REQUEST_PENDING', 'Request is processing'),
  })
  @ApiResponse({ status: 422, schema: policyMutation422Schema })
  @ApiResponse({
    status: 503,
    schema: errorEnvelopeSchema(503, 'UPSTREAM_UNAVAILABLE', 'Identity unavailable'),
  })
  create(
    @Body(new PolicyValidationPipe(CreatePolicySchema)) dto: CreatePolicyDto,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<PolicyResponse> {
    return this.policies.create('OPERATOR', dto, req.user);
  }

  @Patch(':policyId')
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Update a Policy in the caller operator tenant' })
  @ApiParam({ name: 'policyId', format: 'uuid' })
  @ApiBody({ schema: policyUpdateBodySchema })
  @ApiResponse({ status: 200, schema: successEnvelopeSchema(200, policySchema) })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 403, schema: errorEnvelopeSchema(403, 'FORBIDDEN', 'Forbidden') })
  @ApiResponse({
    status: 404,
    schema: errorEnvelopeSchema(404, 'POLICY_NOT_FOUND', 'Policy not found'),
  })
  @ApiResponse({ status: 409, schema: policyMutation409Schema })
  @ApiResponse({ status: 422, schema: policyMutation422Schema })
  @ApiResponse({
    status: 503,
    schema: errorEnvelopeSchema(503, 'UPSTREAM_UNAVAILABLE', 'Identity unavailable'),
  })
  update(
    @Param('policyId', new PolicyUuidPipe()) policyId: string,
    @Body(new PolicyValidationPipe(UpdatePolicySchema)) dto: UpdatePolicyDto,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<PolicyResponse> {
    return this.policies.update('OPERATOR', policyId, dto, req.user);
  }

  @Delete(':policyId')
  @ApiIdempotencyRequired()
  @ApiOperation({ summary: 'Soft-delete a Policy in the caller operator tenant' })
  @ApiParam({ name: 'policyId', format: 'uuid' })
  @ApiResponse({ status: 200, schema: successEnvelopeSchema(200, policySchema) })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 403, schema: errorEnvelopeSchema(403, 'FORBIDDEN', 'Forbidden') })
  @ApiResponse({
    status: 404,
    schema: errorEnvelopeSchema(404, 'POLICY_NOT_FOUND', 'Policy not found'),
  })
  @ApiResponse({ status: 409, schema: policyMutation409Schema })
  @ApiResponse({ status: 422, schema: policyMutation422Schema })
  @ApiResponse({
    status: 503,
    schema: errorEnvelopeSchema(503, 'UPSTREAM_UNAVAILABLE', 'Identity unavailable'),
  })
  delete(
    @Param('policyId', new PolicyUuidPipe()) policyId: string,
    @Req() req: RequestWithRagInternalUser,
  ): Promise<PolicyResponse> {
    return this.policies.delete('OPERATOR', policyId, req.user);
  }
}
