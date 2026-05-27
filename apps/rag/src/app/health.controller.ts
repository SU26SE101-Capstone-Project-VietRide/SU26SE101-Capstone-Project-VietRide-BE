import { Controller, Get } from '@nestjs/common';

/**
 * Liveness probe at root `/health` (excluded from the global `api` prefix in main.ts)
 * so docker-compose + Nginx healthchecks can reach it without the version prefix.
 */
@Controller('health')
export class HealthController {
  @Get()
  check(): { status: string; service: string } {
    return { status: 'ok', service: 'rag' };
  }
}
