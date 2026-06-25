import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { readinessOkSchema, successEnvelopeSchema, errorEnvelopeSchema } from '../swagger/api-response.schemas';
import { ReadinessDto, ReadinessService } from './readiness.service';

@ApiTags('Health')
@Controller('ready')
export class ReadyController {
  constructor(private readonly readinessService: ReadinessService) {}

  @Get()
  @ApiOperation({ summary: 'Readiness probe' })
  @ApiResponse({ status: 200, description: 'Service is ready', schema: successEnvelopeSchema(200, readinessOkSchema) })
  @ApiResponse({ status: 503, description: 'A dependency is unavailable', schema: errorEnvelopeSchema(503, 'RAG_DEPENDENCY_UNAVAILABLE', 'RAG dependency readiness check failed') })
  async check(): Promise<ReadinessDto> {
    return this.readinessService.check();
  }
}
