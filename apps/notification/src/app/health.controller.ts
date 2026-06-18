import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { healthSchema, successEnvelopeSchema } from '../swagger/api-response.schemas';

/**
 * Liveness probe at root `/health` (excluded from the global `api` prefix in main.ts)
 * so docker-compose + Nginx healthchecks can reach it without the version prefix.
 */
@ApiTags('Health')
@Controller('health')
export class HealthController {
  @Get()
  @ApiOperation({ summary: 'Liveness probe' })
  @ApiResponse({
    status: 200,
    description: 'Service is healthy. Runtime response is wrapped in ApiResponse<T>.',
    schema: successEnvelopeSchema(200, healthSchema),
  })
  check(): { status: string; service: string } {
    return { status: 'ok', service: 'notification' };
  }
}
