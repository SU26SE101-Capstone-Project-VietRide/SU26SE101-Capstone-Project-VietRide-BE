import { Global, Module } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import { loadEnv } from './env.schema';

const env = loadEnv();

@Global()
@Module({
  providers: [{ provide: ENV_TOKEN, useValue: env }],
  exports: [ENV_TOKEN],
})
export class TrackingConfigModule {}
