import { Body, Controller, Headers, HttpCode, Post, UseGuards } from '@nestjs/common';
import { ApiBody, ApiHeader, ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { CreateEmailSendSchema, type CreateEmailSendDto } from './dto/create-email-send.dto';
import {
  createEmailSendBodySchema,
  emailDeliverySchema,
  errorEnvelopeSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';
import { NotificationsService, type EmailDeliveryDto } from './notifications.service';
import { ApiIdempotencyRequired } from '../swagger/idempotency.swagger';
import { requireUuidV4IdempotencyKey } from '../swagger/idempotency-key';

@ApiTags('Internal')
@UseGuards(InternalJwtAuthGuard)
@Controller('internal/v1/emails')
export class InternalEmailsController {
  constructor(private readonly notificationsService: NotificationsService) {}

  @Post()
  @ApiIdempotencyRequired()
  @HttpCode(202)
  @ApiOperation({
    summary: 'Enqueue an internal email delivery',
    description:
      'Internal services use this endpoint to enqueue SendGrid email delivery through notification.',
  })
  @ApiHeader({ name: 'X-Internal-Auth', required: true, description: 'Bearer internal JWT' })
  @ApiBody({
    description: 'Email enqueue request. Runtime validation is Zod-backed.',
    schema: createEmailSendBodySchema,
  })
  @ApiResponse({
    status: 202,
    description: 'Email delivery queued. Runtime response is wrapped in ApiResponse<T>.',
    schema: successEnvelopeSchema(202, emailDeliverySchema),
  })
  @ApiResponse({
    status: 400,
    description: 'Invalid email request. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Validation failed', { fields: true }),
  })
  @ApiResponse({
    status: 401,
    description: 'Unauthorized internal caller. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized'),
  })
  @ApiResponse({
    status: 500,
    description: 'Unexpected system error. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error'),
  })
  async enqueueEmail(
    @Body(new ZodValidationPipe(CreateEmailSendSchema)) dto: CreateEmailSendDto,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
  ): Promise<EmailDeliveryDto> {
    const key = requireUuidV4IdempotencyKey(idempotencyKey);
    return this.notificationsService.enqueueEmail({
      ...dto,
      dedupeKey: dto.dedupeKey ?? `http-email:${key}`,
    });
  }
}
