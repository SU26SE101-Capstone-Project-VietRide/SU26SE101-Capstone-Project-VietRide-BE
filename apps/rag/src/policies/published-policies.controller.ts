import { Controller, Get, Param, Query, UseGuards } from '@nestjs/common';
import {
  ApiBearerAuth,
  ApiOperation,
  ApiParam,
  ApiQuery,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import {
  errorEnvelopeSchema,
  pagedDataSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';
import {
  ListPublishedPoliciesQueryDto,
  ListPublishedPoliciesValidationSchema,
} from './dto/list-published-policies.dto';
import { PoliciesService } from './policies.service';
import { publishedPolicySchema, policyValidationErrorSchema } from './policies.swagger';
import type { PublishedPolicyPage, PublishedPolicyResponse } from './policies.types';
import { PolicyUuidPipe } from './policy-uuid.pipe';
import { PolicyValidationPipe } from './policy-validation.pipe';

@ApiTags('Published Policies')
@ApiBearerAuth()
@UseGuards(InternalJwtAuthGuard)
@Controller('v1/policies')
export class PublishedPoliciesController {
  constructor(private readonly policies: PoliciesService) {}

  @Get()
  @ApiOperation({ summary: 'List active user-facing platform and operator Policies' })
  @ApiQuery({ name: 'operatorId', required: false, type: String, format: 'uuid' })
  @ApiQuery({ name: 'category', required: false, type: String })
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
      properties: {
        ...pagedDataSchema.properties,
        items: { type: 'array', items: publishedPolicySchema },
      },
    }),
  })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({ status: 422, schema: policyValidationErrorSchema })
  list(
    @Query(new PolicyValidationPipe(ListPublishedPoliciesValidationSchema))
    query: ListPublishedPoliciesQueryDto,
  ): Promise<PublishedPolicyPage> {
    return this.policies.listPublished(query);
  }

  @Get(':policyId')
  @ApiOperation({ summary: 'Get an active user-facing Policy' })
  @ApiParam({ name: 'policyId', format: 'uuid' })
  @ApiResponse({ status: 200, schema: successEnvelopeSchema(200, publishedPolicySchema) })
  @ApiResponse({ status: 401, schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized') })
  @ApiResponse({
    status: 404,
    schema: errorEnvelopeSchema(404, 'POLICY_NOT_FOUND', 'Policy not found'),
  })
  @ApiResponse({ status: 422, schema: policyValidationErrorSchema })
  get(@Param('policyId', new PolicyUuidPipe()) policyId: string): Promise<PublishedPolicyResponse> {
    return this.policies.getPublished(policyId);
  }
}
