import { Module } from '@nestjs/common';
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import { MvpTrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { TrackingDataController } from './tracking-data.controller';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataService } from './tracking-data.service';

@Module({
  controllers: [TrackingDataController],
  providers: [
    TrackingDataService,
    TrackingDataRepository,
    { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
    { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: MvpTrackingAuthorizationAdapter },
  ],
})
export class TrackingDataModule {}
