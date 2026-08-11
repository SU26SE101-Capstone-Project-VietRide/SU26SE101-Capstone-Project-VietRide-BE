import { Inject, Logger } from '@nestjs/common';
import {
  OnGatewayConnection,
  OnGatewayInit,
  WebSocketGateway,
  WebSocketServer,
} from '@nestjs/websockets';
import { transformFrontendTimestamps } from '@vietride/nest-common';
import type { Server, Socket } from 'socket.io';
import { NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import type { NotificationUser } from '../auth/notification-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import type { NotificationItemDto } from './notifications.service';
import {
  NOTIFICATION_CREATED_EVENT,
  NOTIFICATION_SOCKET_PATH,
  notificationUserRoom,
} from './notifications-realtime.constants';

interface NotificationSocketData {
  user?: NotificationUser;
}

interface NotificationSocket extends Socket {
  data: NotificationSocketData;
}

@WebSocketGateway({ path: NOTIFICATION_SOCKET_PATH })
export class NotificationsRealtimeGateway implements OnGatewayInit, OnGatewayConnection {
  private readonly logger = new Logger(NotificationsRealtimeGateway.name);

  @WebSocketServer()
  private server?: Server;

  constructor(
    @Inject(NOTIFICATION_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
  ) {}

  afterInit(server: Server): void {
    this.server = server;
    server.use((socket: NotificationSocket, next) => {
      void this.authenticate(socket)
        .then(() => next())
        .catch(() => next(new Error('UNAUTHORIZED')));
    });
  }

  async handleConnection(socket: NotificationSocket): Promise<void> {
    const user = socket.data.user;
    if (!user) {
      socket.disconnect(true);
      return;
    }
    await socket.join(notificationUserRoom(user.userId));
  }

  publishCreated(notification: NotificationItemDto): void {
    if (!this.server) {
      this.logger.warn(`Skipped ${NOTIFICATION_CREATED_EVENT} before Socket.IO initialization`);
      return;
    }

    const { userId, ...publicPayload } = notification;
    try {
      this.server
        .to(notificationUserRoom(userId))
        .emit(NOTIFICATION_CREATED_EVENT, transformFrontendTimestamps(publicPayload));
    } catch {
      this.logger.warn(`Failed to emit ${NOTIFICATION_CREATED_EVENT} for notification ${notification.id}`);
    }
  }

  private async authenticate(socket: NotificationSocket): Promise<void> {
    const token = this.readHandshakeToken(socket);
    if (!token) throw new Error('UNAUTHORIZED');
    socket.data.user = await this.jwtVerifier.verify(token);
  }

  private readHandshakeToken(socket: Socket): string | undefined {
    const token = socket.handshake.auth?.token;
    if (typeof token === 'string' && token.length > 0) return token;

    const authorization = socket.handshake.headers.authorization;
    if (typeof authorization === 'string' && authorization.startsWith('Bearer ')) {
      const bearer = authorization.slice('Bearer '.length).trim();
      return bearer.length > 0 ? bearer : undefined;
    }
    return undefined;
  }
}
