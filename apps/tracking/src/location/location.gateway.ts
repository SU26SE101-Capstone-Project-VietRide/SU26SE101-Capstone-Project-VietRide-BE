import { Inject, Logger } from '@nestjs/common';
import {
  ConnectedSocket,
  MessageBody,
  OnGatewayInit,
  SubscribeMessage,
  WebSocketGateway,
  WebSocketServer,
} from '@nestjs/websockets';
import type { Server, Socket } from 'socket.io';
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import { ApproachingAlertService } from '../approaching-alert/approaching-alert.service';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import type { TrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import { EtaService } from '../eta/eta.service';
import { OffRouteService } from '../off-route/off-route.service';
import { TripDelayService } from '../trip-delay/trip-delay.service';
import {
  TRACKING_SOCKET_PATH,
  trackingTripRoom,
} from './location.constants';
import { JoinTripTrackingSchema } from './dto/join-trip-tracking.dto';
import { UpdateLocationSchema } from './dto/update-location.dto';
import { LocationService } from './location.service';

interface TrackingSocket extends Socket {
  data: {
    user?: TrackingUser;
  };
}

interface JoinTripTrackingAck {
  success: boolean;
  tripId?: string;
  room?: string;
  scope?: string;
  error?: string;
  message?: string;
}

interface GpsUpdateAck {
  success: boolean;
  error?: string;
  message?: string;
}

@WebSocketGateway({
  path: TRACKING_SOCKET_PATH,
})
export class LocationGateway implements OnGatewayInit {
  private readonly logger = new Logger(LocationGateway.name);

  @WebSocketServer()
  private readonly server!: Server;

  constructor(
    private readonly locationService: LocationService,
    @Inject(TRACKING_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
    @Inject(TRACKING_AUTHORIZATION_ADAPTER)
    private readonly authorizationAdapter: TrackingAuthorizationAdapter,
    private readonly etaService: EtaService,
    private readonly approachingAlertService: ApproachingAlertService,
    private readonly offRouteService: OffRouteService,
    private readonly tripDelayService: TripDelayService,
  ) {}

  afterInit(server: Server): void {
    server.use(async (socket: TrackingSocket, next) => {
      const token = this.readHandshakeToken(socket);
      if (!token) {
        next(new Error('UNAUTHORIZED'));
        return;
      }

      try {
        socket.data.user = await this.jwtVerifier.verify(token);
        next();
      } catch {
        next(new Error('UNAUTHORIZED'));
      }
    });
  }

  @SubscribeMessage('joinTripTracking')
  async joinTripTracking(
    @ConnectedSocket() socket: TrackingSocket,
    @MessageBody() payload: unknown,
  ): Promise<JoinTripTrackingAck> {
    const parsed = JoinTripTrackingSchema.safeParse(payload);
    if (!parsed.success) {
      return { success: false, error: 'VALIDATION_ERROR', message: 'Invalid tripId' };
    }

    const user = socket.data.user;
    if (!user) {
      return { success: false, error: 'UNAUTHORIZED' };
    }

    const authorization = await this.authorizationAdapter.authorizeTripTracking(user, parsed.data.tripId);
    if (!authorization.allowed || !authorization.scope) {
      return { success: false, error: authorization.error ?? 'ACCESS_DENIED' };
    }

    const room = trackingTripRoom(parsed.data.tripId);
    await socket.join(room);
    return {
      success: true,
      tripId: parsed.data.tripId,
      room,
      scope: authorization.scope,
    };
  }

  @SubscribeMessage('gps:update')
  async updateLocation(
    @ConnectedSocket() socket: TrackingSocket,
    @MessageBody() payload: unknown,
  ): Promise<GpsUpdateAck> {
    const parsed = UpdateLocationSchema.safeParse(payload);
    if (!parsed.success) {
      return { success: false, error: 'VALIDATION_ERROR', message: 'Invalid GPS payload' };
    }

    const user = socket.data.user;
    if (!user) {
      return { success: false, error: 'UNAUTHORIZED' };
    }

    if (user.role !== 'DRIVER' && user.role !== 'ASSISTANT') {
      return { success: false, error: 'ACCESS_DENIED' };
    }

    const authorization = await this.authorizationAdapter.authorizeTripTracking(user, parsed.data.tripId);
    if (!authorization.allowed || (authorization.scope !== 'DRIVER' && authorization.scope !== 'ASSISTANT')) {
      return { success: false, error: authorization.error ?? 'ACCESS_DENIED' };
    }

    let event: Awaited<ReturnType<LocationService['recordLocation']>>;
    try {
      event = await this.locationService.recordLocation(parsed.data);
    } catch (error) {
      this.logger.error(`Failed to record gps:update for trip ${parsed.data.tripId}: ${(error as Error).message}`);
      return { success: false, error: 'TRACKING_UNAVAILABLE' };
    }

    this.server.to(trackingTripRoom(parsed.data.tripId)).emit('gps:update', event);
    void this.runDetection(event).catch((error) => {
      this.logger.error(`Tracking detection chain failed for trip ${event.tripId}: ${(error as Error).message}`);
    });
    this.logger.debug(`Broadcasted gps:update for trip ${parsed.data.tripId}`);
    return { success: true };
  }

  private async runDetection(event: Awaited<ReturnType<LocationService['recordLocation']>>): Promise<void> {
    await this.offRouteService.handleGpsUpdate(event);
    const etaUpdate = await this.etaService.handleGpsUpdate(event);
    if (etaUpdate) {
      const tripDelayEtaUpdate = await this.tripDelayService.handleEtaUpdate(etaUpdate);
      this.server.to(trackingTripRoom(event.tripId)).emit('eta:update', tripDelayEtaUpdate);
      if (tripDelayEtaUpdate.delayed) {
        this.server.to(trackingTripRoom(event.tripId)).emit('trip:statusChanged', {
          tripId: tripDelayEtaUpdate.tripId,
          stopId: tripDelayEtaUpdate.stopId,
          status: 'DELAYED',
          delayMinutes: tripDelayEtaUpdate.delayMinutes,
          updatedAt: tripDelayEtaUpdate.updatedAt,
        });
      }
      await this.approachingAlertService.handleEtaUpdate(tripDelayEtaUpdate);
    }
  }

  private readHandshakeToken(socket: Socket): string | undefined {
    const token = socket.handshake.auth?.token;
    if (typeof token === 'string' && token.length > 0) return token;

    const authorization = socket.handshake.headers.authorization;
    if (typeof authorization === 'string' && authorization.startsWith('Bearer ')) {
      return authorization.slice('Bearer '.length).trim();
    }

    return undefined;
  }
}
