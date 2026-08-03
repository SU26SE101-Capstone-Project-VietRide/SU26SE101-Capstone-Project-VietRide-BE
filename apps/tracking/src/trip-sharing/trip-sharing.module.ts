import { Module } from '@nestjs/common';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { OffRouteModule } from '../off-route/off-route.module';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareAccessService } from './trip-share-access.service';
import { TripShareContextService } from './trip-share-context.service';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import { TripShareIdempotencyRepository } from './trip-share-idempotency.repository';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';
import { TripShareOwnerController } from './trip-share-owner.controller';
import { TripShareOwnerJwtGuard } from './trip-share-owner-jwt.guard';
import { TripShareOwnerService } from './trip-share-owner.service';
import { TripSharePublicController } from './trip-share-public.controller';
import { TripShareRateLimiter } from './trip-share-rate-limiter';
import { TripShareRouteStopsProvider } from './trip-share-route-stops.provider';
import { TripShareTokenGuard } from './trip-share-token.guard';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTrackingStateRepository } from './trip-share-tracking-state.repository';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';
import { TripShareGateway } from './trip-share.gateway';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';

@Module({
  imports: [OffRouteModule],
  controllers: [TripShareOwnerController, TripSharePublicController],
  providers: [
    TrackingInternalJwtSigner,
    BookingOwnerAuthorizationProvider,
    TripShareTripSnapshotProvider,
    TripShareTokenCodec,
    TripShareGrantRepository,
    TripShareGrantService,
    TripShareIdempotencyRepository,
    TripShareIdempotencyService,
    TripShareRateLimiter,
    TripShareRouteStopsProvider,
    TripShareAccessService,
    TripShareTrackingStateRepository,
    TripShareContextService,
    TripShareTokenGuard,
    TripShareOwnerJwtGuard,
    TripShareOwnerService,
    TripShareRealtimePublisher,
    TripShareGateway,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
  ],
  exports: [TripShareRealtimePublisher],
})
export class TripSharingModule {}
