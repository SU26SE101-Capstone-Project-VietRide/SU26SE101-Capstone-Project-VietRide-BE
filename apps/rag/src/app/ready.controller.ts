import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ReadinessDto, ReadinessService } from './readiness.service';

@ApiTags('Health')
@Controller('ready')
export class ReadyController {
  constructor(private readonly readinessService: ReadinessService) {}

  @Get()
  @ApiOperation({ summary: 'Readiness probe' })
  @ApiResponse({ status: 200, description: 'Service is ready' })
  @ApiResponse({ status: 503, description: 'A dependency is unavailable' })
  async check(): Promise<ReadinessDto> {
    return this.readinessService.check();
  }
}
