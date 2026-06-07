import {
  Controller,
  Get,
  HttpCode,
  Param,
  Post,
  Query,
  Req,
  UnauthorizedException,
  UseGuards,
} from '@nestjs/common';
import {
  ApiBearerAuth,
  ApiOperation,
  ApiParam,
  ApiQuery,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { RequestWithNotificationUser } from '../auth/notification-user.types';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import {
  ListNotificationsQuerySchema,
  type ListNotificationsQueryDto,
} from './dto/list-notifications-query.dto';
import {
  NotificationIdParamSchema,
  type NotificationIdParamDto,
} from './dto/notification-param.dto';
import { NotificationsService, type PagedNotificationsDto } from './notifications.service';

@ApiTags('Notifications')
@ApiBearerAuth()
@UseGuards(UserJwtAuthGuard)
@Controller('/v1/notifications')
export class NotificationsController {
  constructor(private readonly notificationsService: NotificationsService) {}

  @Get()
  @ApiOperation({ summary: 'List in-app notification history for current user' })
  @ApiQuery({ name: 'unreadOnly', type: Boolean, required: false })
  @ApiQuery({ name: 'page', type: Number, required: false })
  @ApiQuery({ name: 'pageSize', type: Number, required: false, description: 'Maximum 100' })
  @ApiQuery({ name: 'sortBy', enum: ['createdAt', 'readAt', 'type'], required: false })
  @ApiQuery({ name: 'sortDir', enum: ['asc', 'desc'], required: false })
  @ApiResponse({ status: 200, description: 'Notification history retrieved successfully.' })
  @ApiResponse({ status: 401, description: 'Unauthorized' })
  @ApiResponse({ status: 400, description: 'Invalid query options' })
  async listNotifications(
    @Query(new ZodValidationPipe(ListNotificationsQuerySchema)) query: ListNotificationsQueryDto,
    @Req() request: RequestWithNotificationUser,
  ): Promise<PagedNotificationsDto> {
    return this.notificationsService.listForUser(this.readUserId(request), query);
  }

  @Post(':notificationId/read')
  @HttpCode(204)
  @ApiOperation({ summary: 'Mark a notification as read for current user' })
  @ApiParam({ name: 'notificationId', type: String, format: 'uuid' })
  @ApiResponse({ status: 204, description: 'Notification marked as read.' })
  @ApiResponse({ status: 401, description: 'Unauthorized' })
  @ApiResponse({ status: 404, description: 'Notification not found' })
  async markRead(
    @Param(new ZodValidationPipe(NotificationIdParamSchema)) params: NotificationIdParamDto,
    @Req() request: RequestWithNotificationUser,
  ): Promise<void> {
    await this.notificationsService.markRead(params.notificationId, this.readUserId(request));
  }

  private readUserId(request: RequestWithNotificationUser): string {
    if (!request.user?.userId) {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Missing user context',
      });
    }

    return request.user.userId;
  }
}
