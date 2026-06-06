import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';

@ApiTags('Health')
@Controller('ready')
export class ReadyController {
  @Get()
  @ApiOperation({ summary: 'Readiness probe' })
  @ApiResponse({ status: 200, description: 'Service is ready' })
  check(): { status: string; service: string } {
    return { status: 'ok', service: 'notification' };
  }
}
