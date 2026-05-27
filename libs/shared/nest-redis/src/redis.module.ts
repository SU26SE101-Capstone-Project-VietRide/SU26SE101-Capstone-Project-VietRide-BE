import { DynamicModule, Global, Module, Provider } from '@nestjs/common';
import Redis, { type RedisOptions } from 'ioredis';
import { REDIS_CLIENT, RedisService } from './redis.service';

export interface NestRedisModuleOptions {
  /** Connection URL e.g. `redis://:password@host:6379/0`. */
  url?: string;
  /** Alternative to `url`: pass full ioredis options. */
  options?: RedisOptions;
}

@Global()
@Module({})
export class NestRedisModule {
  static forRoot(opts: NestRedisModuleOptions): DynamicModule {
    const provider: Provider = {
      provide: REDIS_CLIENT,
      useFactory: (): Redis => {
        if (opts.url) return new Redis(opts.url, opts.options ?? {});
        return new Redis(opts.options ?? {});
      },
    };
    return {
      module: NestRedisModule,
      providers: [provider, RedisService],
      exports: [provider, RedisService],
    };
  }
}
