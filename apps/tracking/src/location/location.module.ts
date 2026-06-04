import { Module } from '@nestjs/common';
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import { MvpTrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { ApproachingAlertModule } from '../approaching-alert/approaching-alert.module';
import { EtaModule } from '../eta/eta.module';
import { OffRouteModule } from '../off-route/off-route.module';
import { TripDelayModule } from '../trip-delay/trip-delay.module';
import { LocationGateway } from './location.gateway';
import { LocationService } from './location.service';

@Module({
  imports: [EtaModule, ApproachingAlertModule, OffRouteModule, TripDelayModule],
  providers: [
    LocationGateway,
    LocationService,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
    { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: MvpTrackingAuthorizationAdapter },
  ],
  exports: [LocationService],
})
export class LocationModule {}
