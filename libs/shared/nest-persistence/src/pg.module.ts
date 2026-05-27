import { DynamicModule, Global, Module, Provider } from '@nestjs/common';
import { Pool, type PoolConfig } from 'pg';
import { PG_POOL, PgService } from './pg.service';

export interface NestPersistenceOptions {
  connectionString?: string;
  poolConfig?: PoolConfig;
}

@Global()
@Module({})
export class NestPersistenceModule {
  static forRoot(opts: NestPersistenceOptions): DynamicModule {
    const provider: Provider = {
      provide: PG_POOL,
      useFactory: (): Pool => {
        const cfg: PoolConfig = { ...(opts.poolConfig ?? {}) };
        if (opts.connectionString) cfg.connectionString = opts.connectionString;
        return new Pool(cfg);
      },
    };
    return {
      module: NestPersistenceModule,
      providers: [provider, PgService],
      exports: [provider, PgService],
    };
  }
}
