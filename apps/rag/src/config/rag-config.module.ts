import { Global, Module } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import { RagPrismaModule } from '../prisma/prisma.module';
import { loadEnv } from './env.schema';
import { RuntimeConfigRepository } from './runtime-config.repository';
import { RuntimeConfigService } from './runtime-config.service';

const env = loadEnv();

@Global()
@Module({
  imports: [RagPrismaModule],
  providers: [{ provide: ENV_TOKEN, useValue: env }, RuntimeConfigRepository, RuntimeConfigService],
  exports: [ENV_TOKEN, RuntimeConfigService],
})
export class RagConfigModule {}
