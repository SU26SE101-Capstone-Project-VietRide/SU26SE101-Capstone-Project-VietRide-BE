import { Module } from '@nestjs/common';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import { TripShareIdempotencyRepository } from './trip-share-idempotency.repository';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';
import { TripShareOwnerController } from './trip-share-owner.controller';
import { TripShareOwnerJwtGuard } from './trip-share-owner-jwt.guard';
import { TripShareOwnerService } from './trip-share-owner.service';
import { TripShareRateLimiter } from './trip-share-rate-limiter';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';

@Module({
  controllers: [TripShareOwnerController],
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
    TripShareOwnerJwtGuard,
    TripShareOwnerService,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
  ],
})
export class TripSharingModule {}
