import { Controller, ForbiddenException, Get, Query, Req, UseGuards } from '@nestjs/common';
import { z } from 'zod';
import type { Request } from 'express';
import { ZodValidationPipe } from '@vietride/nest-common';
import type { TrackingUser } from '../auth/tracking-user.types';
import { OperatorFleetAuthGuard } from './operator-fleet-auth.guard';
import { OperatorFleetService } from './operator-fleet.service';

const FleetQuerySchema = z.object({
  status: z.enum(['SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'DISRUPTED']).optional(),
});

@Controller('/v1/tracking/operator')
@UseGuards(OperatorFleetAuthGuard)
export class OperatorFleetController {
  constructor(private readonly fleet: OperatorFleetService) {}

  @Get('fleet-latest')
  getLatest(
    @Req() request: Request & { user: TrackingUser },
    @Query(new ZodValidationPipe(FleetQuerySchema)) query: z.infer<typeof FleetQuerySchema>,
  ) {
    const operatorId = request.user.operatorId;
    if (!operatorId) throw new ForbiddenException({ errorCode: 'FORBIDDEN' });
    return this.fleet.getLatest(operatorId, query.status);
  }
}
