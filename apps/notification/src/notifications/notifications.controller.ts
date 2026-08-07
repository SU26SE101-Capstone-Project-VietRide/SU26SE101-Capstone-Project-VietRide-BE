import {
  Controller,
  Get,
  Headers,
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
import {
  errorEnvelopeSchema,
  pagedNotificationsSchema,
  successEnvelopeSchema,
} from '../swagger/api-response.schemas';
import { NotificationsService, type PagedNotificationsDto } from './notifications.service';
import { ApiIdempotencyRequired } from '../swagger/idempotency.swagger';
import { requireUuidV4IdempotencyKey } from '../swagger/idempotency-key';

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
  @ApiQuery({ name: 'cursor', type: String, required: false, description: 'Opaque continuation cursor' })
  @ApiResponse({
    status: 200,
    description:
      'Notification history retrieved successfully. Runtime response is wrapped in ApiResponse<T>.',
    schema: successEnvelopeSchema(200, pagedNotificationsSchema),
  })
  @ApiResponse({
    status: 400,
    description: 'Invalid query options. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Validation failed', { fields: true }),
  })
  @ApiResponse({
    status: 401,
    description:
      'Missing or invalid user access token. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized'),
  })
  @ApiResponse({
    status: 500,
    description: 'Unexpected system error. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error'),
  })
  async listNotifications(
    @Query(new ZodValidationPipe(ListNotificationsQuerySchema)) query: ListNotificationsQueryDto,
    @Req() request: RequestWithNotificationUser,
  ): Promise<PagedNotificationsDto> {
    return this.notificationsService.listForUser(this.readUserId(request), query);
  }

  @Post(':notificationId/read')
  @ApiIdempotencyRequired()
  @HttpCode(204)
  @ApiOperation({ summary: 'Mark a notification as read for current user' })
  @ApiParam({ name: 'notificationId', type: String, format: 'uuid' })
  @ApiResponse({
    status: 204,
    description: 'Notification marked as read. 204 responses intentionally have no envelope body.',
  })
  @ApiResponse({
    status: 400,
    description:
      'Invalid notification id or body. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(400, 'VALIDATION_FAILED', 'Validation failed', { fields: true }),
  })
  @ApiResponse({
    status: 401,
    description:
      'Missing or invalid user access token. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(401, 'UNAUTHORIZED', 'Unauthorized'),
  })
  @ApiResponse({
    status: 404,
    description: 'Notification does not exist or is not owned by the current user.',
    schema: errorEnvelopeSchema(
      404,
      'NOTIFICATION_NOT_FOUND',
      'Notification 7e7d44b8-3d84-4dd5-b0a2-1f445de7c701 not found',
    ),
  })
  @ApiResponse({
    status: 500,
    description: 'Unexpected system error. Runtime response is an ApiResponse error envelope.',
    schema: errorEnvelopeSchema(500, 'INTERNAL_ERROR', 'Unexpected error'),
  })
  async markRead(
    @Param(new ZodValidationPipe(NotificationIdParamSchema)) params: NotificationIdParamDto,
    @Req() request: RequestWithNotificationUser,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
  ): Promise<void> {
    requireUuidV4IdempotencyKey(idempotencyKey);
    await this.notificationsService.markRead(params.notificationId, this.readUserId(request));
  }

  @Post('read-all')
  @ApiIdempotencyRequired()
  @HttpCode(200)
  @ApiOperation({ summary: 'Atomically mark all current notifications as read' })
  @ApiResponse({
    status: 200,
    description: 'Unread notifications up to the server cutoff were marked as read.',
    schema: successEnvelopeSchema(200, {
      type: 'object',
      required: ['markedCount', 'readAt'],
      properties: {
        markedCount: { type: 'integer', minimum: 0, example: 4 },
        readAt: { type: 'string', format: 'date-time' },
      },
    }),
  })
  async markAllRead(
    @Req() request: RequestWithNotificationUser,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
  ): Promise<{ markedCount: number; readAt: string }> {
    const normalizedKey = requireUuidV4IdempotencyKey(idempotencyKey);
    return this.notificationsService.markAllRead(this.readUserId(request), normalizedKey);
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
