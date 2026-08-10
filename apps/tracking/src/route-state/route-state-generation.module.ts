import { Module } from '@nestjs/common';
import { RouteStateGenerationRegistry } from './route-state-generation.registry';

@Module({
  providers: [RouteStateGenerationRegistry],
  exports: [RouteStateGenerationRegistry],
})
export class RouteStateGenerationModule {}
