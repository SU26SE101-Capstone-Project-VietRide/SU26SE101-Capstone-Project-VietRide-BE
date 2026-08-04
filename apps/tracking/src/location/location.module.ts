import { Module } from '@nestjs/common';
import { TRACKING_AUTHORIZATION_ADAPTER, TRACKING_JWT_VERIFIER } from '../app/tokens';
import { HttpTrackingAuthorizationAdapter } from '../authorization/http-tracking-authorization.adapter';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { ApproachingAlertModule } from '../approaching-alert/approaching-alert.module';
import { EtaModule } from '../eta/eta.module';
import { OffRouteModule } from '../off-route/off-route.module';
import { TripDelayModule } from '../trip-delay/trip-delay.module';
import { LocationGateway } from './location.gateway';
import { LocationService } from './location.service';
import { ShuttleService } from '../shuttle/shuttle.service';
import { ShuttleEtaService } from '../shuttle/shuttle-eta.service';
import { ShuttleTrackingController } from '../shuttle/shuttle-tracking.controller';
import { ShuttleTrackingAuthGuard } from '../shuttle/shuttle-tracking-auth.guard';
import { TripSharingModule } from '../trip-sharing/trip-sharing.module';
import { BookingCreatedRealtimeConsumer } from './booking-created-realtime.consumer';

@Module({
  imports: [EtaModule, ApproachingAlertModule, OffRouteModule, TripDelayModule, TripSharingModule],
  providers: [
    LocationGateway,
    LocationService,
    TrackingInternalJwtSigner,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
    { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: HttpTrackingAuthorizationAdapter },
    ShuttleService,
    ShuttleEtaService,
    ShuttleTrackingAuthGuard,
    BookingCreatedRealtimeConsumer,
  ],
  controllers: [ShuttleTrackingController],
  exports: [LocationService],
})
export class LocationModule {}
