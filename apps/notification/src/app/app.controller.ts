import { Controller, Get } from '@nestjs/common';
import { ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import { defaultResponseSchema, successEnvelopeSchema } from '../swagger/api-response.schemas';
import { AppService } from './app.service';

@ApiTags('Default')
@Controller()
export class AppController {
  constructor(private readonly appService: AppService) {}

  @Get()
  @ApiOperation({ summary: 'Default endpoint' })
  @ApiResponse({
    status: 200,
    description: 'Default response. Runtime response is wrapped in ApiResponse<T>.',
    schema: successEnvelopeSchema(200, defaultResponseSchema),
  })
  getData() {
    return this.appService.getData();
  }
}
