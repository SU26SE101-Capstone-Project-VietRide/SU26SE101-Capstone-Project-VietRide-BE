import { Controller, Get, Query, UseGuards } from '@nestjs/common';
import {
  ApiHeader,
  ApiOkResponse,
  ApiOperation,
  ApiTags,
  ApiUnauthorizedResponse,
} from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import {
  outboxDlqQuerySchema,
  type OutboxDlqQueryDto,
} from './outbox-dlq-query.dto';
import { OutboxDlqService } from './outbox-dlq.service';
import type { OutboxDlqReadItem } from './outbox.repository';
import { TrackingInternalJwtGuard } from './tracking-internal-jwt.guard';

@ApiTags('Internal Outbox')
@Controller('internal/v1/outbox/dlq')
@UseGuards(TrackingInternalJwtGuard)
export class OutboxDlqController {
  constructor(private readonly service: OutboxDlqService) {}

  @Get()
  @ApiOperation({ summary: 'Lists terminal Tracking Outbox failures for the Identity facade' })
  @ApiHeader({ name: 'X-Internal-Auth', required: true })
  @ApiOkResponse({ description: 'Raw DLQ rows ordered by terminal cursor' })
  @ApiUnauthorizedResponse({ description: 'Internal JWT is missing or invalid' })
  list(
    @Query(new ZodValidationPipe(outboxDlqQuerySchema)) query: OutboxDlqQueryDto,
  ): Promise<OutboxDlqReadItem[]> {
    return this.service.list(query);
  }
}
