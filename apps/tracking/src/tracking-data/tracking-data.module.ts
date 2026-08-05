import { Module } from '@nestjs/common';
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import { HttpTrackingAuthorizationAdapter } from '../authorization/http-tracking-authorization.adapter';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { OffRouteModule } from '../off-route/off-route.module';
import { TrackingDataAuthGuard } from './tracking-data-auth.guard';
import { TrackingDataController } from './tracking-data.controller';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataService } from './tracking-data.service';
import { TripRouteContextService } from './trip-route-context.service';
import { OperatorFleetAuthGuard } from './operator-fleet-auth.guard';
import { OperatorFleetController } from './operator-fleet.controller';
import { OperatorFleetService } from './operator-fleet.service';
import { OperatorTripProjectionProvider } from './operator-trip-projection.provider';
import { EtaModule } from '../eta/eta.module';

@Module({
  imports: [OffRouteModule, EtaModule],
  controllers: [TrackingDataController, OperatorFleetController],
  providers: [
    TrackingDataService,
    TripRouteContextService,
    TrackingDataRepository,
    TrackingDataAuthGuard,
    TrackingInternalJwtSigner,
    OperatorFleetAuthGuard,
    OperatorFleetService,
    OperatorTripProjectionProvider,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
    { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: HttpTrackingAuthorizationAdapter },
  ],
})
export class TrackingDataModule {}
