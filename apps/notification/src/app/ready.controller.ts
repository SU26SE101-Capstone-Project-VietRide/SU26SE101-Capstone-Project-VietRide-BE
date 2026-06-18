import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { errorEnvelopeSchema, readinessSchema, successEnvelopeSchema } from '../swagger/api-response.schemas';
import { ReadinessDto, ReadinessService } from './readiness.service';

@ApiTags('Health')
@Controller('ready')
export class ReadyController {
  constructor(private readonly readinessService: ReadinessService) {}

  @Get()
  @ApiOperation({ summary: 'Readiness probe' })
  @ApiResponse({
    status: 200,
    description: 'Service is ready. Runtime response is wrapped in ApiResponse<T>.',
    schema: successEnvelopeSchema(200, readinessSchema),
  })
  @ApiResponse({
    status: 503,
    description: 'A dependency is unavailable. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(
      503,
      'NOTIFICATION_DEPENDENCY_UNAVAILABLE',
      'Notification dependency readiness check failed',
    ),
  })
  async check(): Promise<ReadinessDto> {
    return this.readinessService.check();
  }
}
