import { Global, Module } from '@nestjs/common';
import { TrackingPrismaService } from './tracking-prisma.service';

@Global()
@Module({
  providers: [TrackingPrismaService],
  exports: [TrackingPrismaService],
})
export class TrackingPrismaModule {}
