import { GoneException, HttpException, Inject } from '@nestjs/common';
import { OnGatewayInit, WebSocketGateway, WebSocketServer } from '@nestjs/websockets';
import type { Namespace, Socket } from 'socket.io';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { TRACKING_SOCKET_PATH } from '../location/location.constants';
import { TripShareAccessService, type TripShareAccessContext } from './trip-share-access.service';
import {
  SHARED_ACCESS_REVOKED_EVENT,
  TRIP_SHARE_SOCKET_NAMESPACE,
  sharedGrantRoom,
  sharedTripRoom,
  type TripShareAccessRevocationReason,
} from './trip-share-realtime.constants';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';

const MILLISECONDS_PER_SECOND = 1_000;
const SHARE_TOKEN_INVALID = 'TRACKING_SHARE_TOKEN_INVALID';
const SHARE_LINK_UNAVAILABLE = 'TRACKING_SHARE_LINK_UNAVAILABLE';
const RATE_LIMITED = 'RATE_LIMITED';
const ACCESS_UNAVAILABLE = 'TRACKING_SHARE_ACCESS_UNAVAILABLE';

interface TripShareSocketData {
  shareAccess?: TripShareAccessContext;
  shareToken?: string;
  expiryTimer?: NodeJS.Timeout;
  revalidationTimer?: NodeJS.Timeout;
}

interface TripShareSocket extends Socket {
  data: TripShareSocketData;
}

@WebSocketGateway({
  namespace: TRIP_SHARE_SOCKET_NAMESPACE,
  path: TRACKING_SOCKET_PATH,
})
export class TripShareGateway implements OnGatewayInit {
  @WebSocketServer()
  private readonly namespace!: Namespace;

  constructor(
    private readonly access: TripShareAccessService,
    private readonly realtime: TripShareRealtimePublisher,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  afterInit(namespace: Namespace): void {
    this.realtime.attach(namespace);
    namespace.use((socket: TripShareSocket, next) => {
      void this.authenticate(socket)
        .then(() => next())
        .catch((error: unknown) => {
          next(new Error(this.connectErrorCode(error)));
        });
    });
  }

  private async authenticate(socket: TripShareSocket): Promise<void> {
    const rawToken = this.readShareToken(socket);
    const context = await this.access.authorizeSocket(rawToken);
    if (!rawToken) throw new Error(SHARE_TOKEN_INVALID);

    socket.data.shareToken = rawToken;
    socket.data.shareAccess = context;
    await socket.join([sharedTripRoom(context.tripId), sharedGrantRoom(context.grantId)]);
    this.installLifecycle(socket, context);
  }

  private installLifecycle(socket: TripShareSocket, context: TripShareAccessContext): void {
    const expiresInMs = Math.max(0, context.expiresAt.getTime() - Date.now());
    socket.data.expiryTimer = setTimeout(() => {
      this.disconnect(socket, 'EXPIRED');
    }, expiresInMs);
    socket.data.expiryTimer.unref();

    const intervalMs = this.env.TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS * MILLISECONDS_PER_SECOND;
    socket.data.revalidationTimer = setInterval(() => {
      void this.revalidate(socket);
    }, intervalMs);
    socket.data.revalidationTimer.unref();

    socket.once('disconnect', () => this.clearLifecycle(socket));
  }

  private async revalidate(socket: TripShareSocket): Promise<void> {
    if (!socket.connected || !socket.data.shareToken) return;
    try {
      socket.data.shareAccess = await this.access.revalidate(socket.data.shareToken);
    } catch (error) {
      this.disconnect(socket, this.revalidationReason(error));
    }
  }

  private disconnect(socket: TripShareSocket, reason: TripShareAccessRevocationReason): void {
    if (!socket.connected) return;
    this.clearLifecycle(socket);
    socket.emit(SHARED_ACCESS_REVOKED_EVENT, { reason });
    socket.disconnect(true);
  }

  private clearLifecycle(socket: TripShareSocket): void {
    if (socket.data.expiryTimer) clearTimeout(socket.data.expiryTimer);
    if (socket.data.revalidationTimer) clearInterval(socket.data.revalidationTimer);
    delete socket.data.expiryTimer;
    delete socket.data.revalidationTimer;
  }

  private readShareToken(socket: Socket): string | undefined {
    const token = socket.handshake.auth?.shareToken;
    return typeof token === 'string' && token.length > 0 ? token : undefined;
  }

  private connectErrorCode(error: unknown): string {
    if (!(error instanceof HttpException)) return ACCESS_UNAVAILABLE;
    const response = error.getResponse();
    const errorCode =
      typeof response === 'object' && response !== null
        ? (response as Record<string, unknown>)['errorCode']
        : undefined;
    if (
      errorCode === SHARE_TOKEN_INVALID ||
      errorCode === SHARE_LINK_UNAVAILABLE ||
      errorCode === RATE_LIMITED
    ) {
      return errorCode;
    }
    return ACCESS_UNAVAILABLE;
  }

  private revalidationReason(error: unknown): TripShareAccessRevocationReason {
    if (error instanceof GoneException && error.cause === 'TRIP_ENDED') return 'TRIP_ENDED';
    if (error instanceof GoneException && error.cause === 'EXPIRED') return 'EXPIRED';
    if (error instanceof GoneException) return 'REVOKED';
    return 'ACCESS_UNAVAILABLE';
  }
}
