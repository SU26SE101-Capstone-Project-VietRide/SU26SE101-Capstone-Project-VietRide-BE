import {
  Body,
  Controller,
  ForbiddenException,
  Headers,
  HttpCode,
  Post,
  Req,
  UseGuards,
} from '@nestjs/common';
import { ApiBearerAuth, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { RequestWithNotificationUser } from '../auth/notification-user.types';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import {
  CreateOperatorAnnouncementSchema,
  type CreateOperatorAnnouncementDto,
} from './dto/create-operator-announcement.dto';
import {
  OperatorAnnouncementService,
  type OperatorAnnouncementResult,
} from './operator-announcement.service';

@ApiTags('Operator notifications')
@ApiBearerAuth()
@UseGuards(UserJwtAuthGuard)
@Controller('/v1/operator/notifications')
export class OperatorNotificationsController {
  constructor(private readonly announcements: OperatorAnnouncementService) {}

  @Post()
  @HttpCode(202)
  @ApiResponse({ status: 202, description: 'Announcement accepted for delivery.' })
  async create(
    @Body(new ZodValidationPipe(CreateOperatorAnnouncementSchema))
    dto: CreateOperatorAnnouncementDto,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
    @Req() request: RequestWithNotificationUser,
  ): Promise<OperatorAnnouncementResult> {
    const user = request.user;
    if (!user || !['OPERATOR_ADMIN', 'OPERATOR_STAFF'].includes(user.role) || !user.operatorId) {
      throw new ForbiddenException({
        errorCode: 'FORBIDDEN',
        detail: 'Operator administrator or staff scope is required',
      });
    }
    return this.announcements.create(user.userId, user.operatorId, idempotencyKey, dto);
  }
}
