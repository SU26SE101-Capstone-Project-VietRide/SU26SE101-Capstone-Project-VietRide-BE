import { Inject, Logger, Optional } from '@nestjs/common';
import {
  ConnectedSocket,
  MessageBody,
  OnGatewayInit,
  SubscribeMessage,
  WebSocketGateway,
  WebSocketServer,
} from '@nestjs/websockets';
import type { Server, Socket } from 'socket.io';
import { transformFrontendTimestamps } from '@vietride/nest-common';
import { TRACKING_AUTHORIZATION_ADAPTER, TRACKING_JWT_VERIFIER } from '../app/tokens';
import { ApproachingAlertService } from '../approaching-alert/approaching-alert.service';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import type { TrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import type {
  BookingTransferredEvent,
  OperationalBookingCancelledEvent,
  OperationalBookingCreatedEvent,
  PassengerBoardedEvent,
} from '@vietride/contracts';
import { EtaService } from '../eta/eta.service';
import { OffRouteService } from '../off-route/off-route.service';
import { TripDelayService, type TripDelayEtaUpdate } from '../trip-delay/trip-delay.service';
import { shuttleRoom } from '../shuttle/shuttle.constants';
import { JoinShuttleTrackingSchema, ShuttleGpsUpdateSchema } from '../shuttle/shuttle.dto';
import { ShuttleService } from '../shuttle/shuttle.service';
import { ShuttleEtaService } from '../shuttle/shuttle-eta.service';
import {
  TRACKING_SOCKET_PATH,
  trackingOperatorFleetRoom,
  trackingTripCrewRoom,
  trackingTripRoom,
} from './location.constants';
import { JoinTripTrackingSchema } from './dto/join-trip-tracking.dto';
import { UpdateLocationSchema } from './dto/update-location.dto';
import { LocationService, type GpsUpdateEvent } from './location.service';
import { TripShareRealtimePublisher } from '../trip-sharing/trip-share-realtime.publisher';
import { OperatorTripProjectionProvider } from '../tracking-data/operator-trip-projection.provider';
import type { RouteChangeProposalEvent } from '@vietride/contracts';

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
    private readonly shuttleService: ShuttleService,
    private readonly shuttleEtaService: ShuttleEtaService,
    private readonly tripShareRealtime: TripShareRealtimePublisher,
    @Optional() private readonly operatorTrips?: OperatorTripProjectionProvider,
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

  @SubscribeMessage('joinOperatorFleet')
  async joinOperatorFleet(@ConnectedSocket() socket: TrackingSocket): Promise<JoinTripTrackingAck> {
    const user = socket.data.user;
    if (!user) return { success: false, error: 'UNAUTHORIZED' };
    if (!['OPERATOR_ADMIN', 'OPERATOR_STAFF'].includes(user.role) || !user.operatorId) {
      return { success: false, error: 'ACCESS_DENIED' };
    }
    const room = trackingOperatorFleetRoom(user.operatorId);
    await socket.join(room);
    return { success: true, room, scope: 'OPERATOR' };
  }

  @SubscribeMessage('joinShuttleTracking')
  async joinShuttleTracking(
    @ConnectedSocket() socket: TrackingSocket,
    @MessageBody() payload: unknown,
  ): Promise<JoinTripTrackingAck> {
    const parsed = JoinShuttleTrackingSchema.safeParse(payload);
    if (!parsed.success) return { success: false, error: 'VALIDATION_ERROR' };
    const user = socket.data.user;
    if (!user) return { success: false, error: 'UNAUTHORIZED' };
    try {
      const context = await this.shuttleService.getContext(user, parsed.data.shuttleTripId);
      if (!context.allowed || !context.scope) return { success: false, error: 'ACCESS_DENIED' };
      const room = shuttleRoom(parsed.data.shuttleTripId);
      await socket.join(room);
      return {
        success: true,
        tripId: parsed.data.shuttleTripId,
        room,
        scope: context.scope,
      };
    } catch (error) {
      return { success: false, error: (error as Error).message };
    }
  }

  @SubscribeMessage('shuttle:gps:update')
  async updateShuttleLocation(
    @ConnectedSocket() socket: TrackingSocket,
    @MessageBody() payload: unknown,
  ): Promise<GpsUpdateAck> {
    const parsed = ShuttleGpsUpdateSchema.safeParse(payload);
    if (!parsed.success) return { success: false, error: 'VALIDATION_ERROR' };
    const user = socket.data.user;
    if (!user || user.role !== 'DRIVER') return { success: false, error: 'ACCESS_DENIED' };
    try {
      const context = await this.shuttleService.getContext(user, parsed.data.shuttleTripId);
      if (!context.allowed || context.scope !== 'DRIVER')
        return { success: false, error: 'ACCESS_DENIED' };
      const result = await this.shuttleService.recordLocation(parsed.data);
      if (result.duplicate) return { success: true };
      const room = shuttleRoom(parsed.data.shuttleTripId);
      this.server.to(room).emit('shuttle:gps:update', transformFrontendTimestamps(result.gps));
      void this.shuttleEtaService
        .handleGpsUpdate(parsed.data, context)
        .then((eta) => {
          if (eta)
            this.server.to(room).emit('shuttle:eta:update', transformFrontendTimestamps(eta));
        })
        .catch((error) => {
          this.logger.error(
            `Shuttle ETA chain failed for ${parsed.data.shuttleTripId}: ${(error as Error).message}`,
          );
        });
      return { success: true };
    } catch (error) {
      if ((error as Error).message === 'GPS_OPERATION_PAYLOAD_MISMATCH') {
        return { success: false, error: 'IDEMPOTENCY_KEY_REUSED' };
      }
      this.logger.error(`Failed shuttle GPS update: ${(error as Error).message}`);
      return { success: false, error: 'TRACKING_UNAVAILABLE' };
    }
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

    const authorization = await this.authorizationAdapter.authorizeTripTracking(
      user,
      parsed.data.tripId,
    );
    if (!authorization.allowed || !authorization.scope) {
      return { success: false, error: authorization.error ?? 'ACCESS_DENIED' };
    }

    const room = trackingTripRoom(parsed.data.tripId);
    await socket.join(room);
    if (authorization.scope === 'DRIVER' || authorization.scope === 'ASSISTANT') {
      await socket.join(trackingTripCrewRoom(parsed.data.tripId));
    }
    return {
      success: true,
      tripId: parsed.data.tripId,
      room,
      scope: authorization.scope,
    };
  }

  emitBookingCreated(event: OperationalBookingCreatedEvent): void {
    this.server
      .to(trackingTripCrewRoom(event.tripId))
      .emit('booking:created', transformFrontendTimestamps(event));
    this.server.to(trackingTripCrewRoom(event.tripId)).emit(
      'booking:updated',
      transformFrontendTimestamps({
        eventId: event.eventId,
        occurredAt: event.occurredAt,
        tripId: event.tripId,
        bookingId: event.bookingId,
        bookingCode: event.bookingCode,
        seatNumbers: event.seatNumbers,
        reason: 'BOOKING_CREATED',
      }),
    );
  }

  emitBookingCancelled(event: OperationalBookingCancelledEvent): void {
    this.server.to(trackingTripCrewRoom(event.tripId)).emit(
      'booking:updated',
      transformFrontendTimestamps({
        eventId: event.eventId,
        occurredAt: event.occurredAt,
        tripId: event.tripId,
        bookingId: event.bookingId,
        bookingCode: event.bookingCode,
        seatNumbers: event.seatNumbers,
        reason: 'BOOKING_CANCELLED',
        cancellationReason: event.cancellationReason,
      }),
    );
  }

  emitPassengerBoarded(event: PassengerBoardedEvent): void {
    this.server.to(trackingTripCrewRoom(event.tripId)).emit(
      'booking:updated',
      transformFrontendTimestamps({
        eventId: event.eventId,
        occurredAt: event.occurredAt,
        tripId: event.tripId,
        bookingId: event.bookingId,
        bookingCode: event.bookingCode,
        seatNumbers: [event.seatNumber],
        reason: 'PASSENGER_BOARDED',
        passengerRecordId: event.passengerRecordId,
        ticketCode: event.ticketCode,
        boardedAt: event.boardedAt,
      }),
    );
  }

  emitBookingTransferred(event: BookingTransferredEvent): void {
    for (const tripId of new Set([event.oldTripId, event.newTripId])) {
      this.server.to(trackingTripCrewRoom(tripId)).emit(
        'booking:updated',
        transformFrontendTimestamps({
          eventId: event.eventId,
          occurredAt: event.occurredAt,
          tripId,
          bookingId: event.bookingId,
          reason: 'BOOKING_TRANSFERRED',
          oldTripId: event.oldTripId,
          newTripId: event.newTripId,
          transfers: event.transfers,
        }),
      );
    }
  }

  emitRouteProposal(event: RouteChangeProposalEvent): void {
    const eventName =
      event.status === 'PENDING' ? 'routeProposal:created' : 'routeProposal:resolved';
    this.server.to(trackingOperatorFleetRoom(event.operatorId)).emit(
      eventName,
      transformFrontendTimestamps({
        proposalId: event.proposalId,
        tripId: event.tripId,
        status: event.status,
        createdAt: event.occurredAt,
      }),
    );
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

    const authorization = await this.authorizationAdapter.authorizeTripTracking(
      user,
      parsed.data.tripId,
    );
    if (
      !authorization.allowed ||
      (authorization.scope !== 'DRIVER' && authorization.scope !== 'ASSISTANT')
    ) {
      return { success: false, error: authorization.error ?? 'ACCESS_DENIED' };
    }

    let result: Awaited<ReturnType<LocationService['recordLocation']>>;
    try {
      result = await this.locationService.recordLocation(parsed.data);
    } catch (error) {
      if ((error as Error).message === 'GPS_OPERATION_PAYLOAD_MISMATCH') {
        return { success: false, error: 'IDEMPOTENCY_KEY_REUSED' };
      }
      this.logger.error(
        `Failed to record gps:update for trip ${parsed.data.tripId}: ${(error as Error).message}`,
      );
      return { success: false, error: 'TRACKING_UNAVAILABLE' };
    }

    if (result.duplicate) return { success: true };
    const event = result.event;

    this.server
      .to(trackingTripRoom(parsed.data.tripId))
      .emit('gps:update', transformFrontendTimestamps(event));
    void this.publishFleetGps(user, event);
    this.publishSharedGps(event);
    void this.runDetection(event, result.rawEvent).catch((error) => {
      this.logger.error(
        `Tracking detection chain failed for trip ${event.tripId}: ${(error as Error).message}`,
      );
    });
    this.logger.debug(`Broadcasted gps:update for trip ${parsed.data.tripId}`);
    return { success: true };
  }

  private async publishFleetGps(user: TrackingUser, event: GpsUpdateEvent): Promise<void> {
    if (!user.operatorId || !this.operatorTrips) return;
    try {
      const projection = (await this.operatorTrips.list(user.operatorId)).find(
        (trip) => trip.tripId === event.tripId,
      );
      if (!projection) return;
      this.server.to(trackingOperatorFleetRoom(user.operatorId)).emit(
        'fleet:gps:update',
        transformFrontendTimestamps({
          ...event,
          status: projection.status,
        }),
      );
    } catch (error) {
      this.logger.warn(
        `Fleet GPS projection failed for ${event.tripId}: ${(error as Error).message}`,
      );
    }
  }

  private async runDetection(event: GpsUpdateEvent, rawEvent: GpsUpdateEvent): Promise<void> {
    await this.offRouteService.handleGpsUpdate(rawEvent);
    const etaUpdate = await this.etaService.handleGpsUpdate(event);
    if (etaUpdate) {
      const { etas, ...nextEtaUpdate } = etaUpdate;
      const tripDelayEtaUpdate = await this.tripDelayService.handleEtaUpdate(nextEtaUpdate);
      const { statusTransition, ...etaPayload } = tripDelayEtaUpdate;
      this.server
        .to(trackingTripRoom(event.tripId))
        .emit('eta:update', transformFrontendTimestamps(etaPayload));
      if (etas) {
        this.server.to(trackingTripRoom(event.tripId)).emit(
          'eta:batch:update',
          transformFrontendTimestamps({
            tripId: event.tripId,
            etas,
            updatedAt: etaPayload.updatedAt,
          }),
        );
      }
      this.publishSharedEta(etaPayload);
      if (statusTransition) {
        this.server.to(trackingTripRoom(event.tripId)).emit(
          'trip:statusChanged',
          transformFrontendTimestamps({
            tripId: tripDelayEtaUpdate.tripId,
            stopId: tripDelayEtaUpdate.stopId,
            status: statusTransition,
            delayMinutes: tripDelayEtaUpdate.delayMinutes,
            updatedAt: tripDelayEtaUpdate.updatedAt,
          }),
        );
        this.publishSharedStatus(etaPayload, statusTransition);
      }
      await this.approachingAlertService.handleEtaUpdate(tripDelayEtaUpdate);
    }
  }

  private publishSharedGps(event: GpsUpdateEvent): void {
    try {
      this.tripShareRealtime.publishGps(event);
    } catch (error) {
      this.logger.warn(
        `Shared GPS broadcast failed for trip ${event.tripId}: ${(error as Error).message}`,
      );
    }
  }

  private publishSharedEta(event: TripDelayEtaUpdate): void {
    try {
      this.tripShareRealtime.publishEta(event);
    } catch (error) {
      this.logger.warn(
        `Shared ETA broadcast failed for trip ${event.tripId}: ${(error as Error).message}`,
      );
    }
  }

  private publishSharedStatus(
    event: Omit<TripDelayEtaUpdate, 'statusTransition'>,
    status: 'DELAYED' | 'DELAY_CLEARED',
  ): void {
    try {
      this.tripShareRealtime.publishStatus({
        tripId: event.tripId,
        status,
        delayMinutes: event.delayMinutes,
        updatedAt: event.updatedAt,
      });
    } catch (error) {
      this.logger.warn(
        `Shared status broadcast failed for trip ${event.tripId}: ${(error as Error).message}`,
      );
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
