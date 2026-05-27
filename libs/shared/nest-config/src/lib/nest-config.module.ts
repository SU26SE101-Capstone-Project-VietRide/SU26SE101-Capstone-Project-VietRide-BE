import { DynamicModule, Global, Module } from '@nestjs/common';
import type { ZodSchema } from 'zod';
import { loadEnv } from '../load-env';

/**
 * DI token under which the parsed + validated env object is registered.
 * Inject via `@Inject(ENV_TOKEN) private readonly env: TEnv`.
 */
export const ENV_TOKEN = Symbol('VIETRIDE_ENV');

export interface NestConfigOptions<TEnv> {
  /** Zod schema that validates `process.env`. Throws synchronously on failure. */
  schema: ZodSchema<TEnv>;
  /** Source object to parse — defaults to `process.env`. Override in tests. */
  source?: NodeJS.ProcessEnv | Record<string, unknown>;
}

/**
 * Global dynamic config module.
 *
 * Usage in any NestJS app (gateway, tracking, notification, rag):
 *
 *   import { baseEnvSchema, ENV_TOKEN, NestConfigModule } from '@vietride/nest-config';
 *   const envSchema = baseEnvSchema.extend({ TRACKING_BATCH_SIZE: z.coerce.number().default(50) });
 *
 *   @Module({ imports: [NestConfigModule.forRoot({ schema: envSchema }), ...] })
 *   export class AppModule {}
 *
 *   // Then inject:
 *   constructor(@Inject(ENV_TOKEN) private readonly env: z.infer<typeof envSchema>) {}
 *
 * Per BACKEND_SOURCE_OF_TRUTH §3.6 — config wiring is shared via libs/shared/nest-config,
 * not duplicated per service.
 */
@Global()
@Module({})
export class NestConfigModule {
  static forRoot<TEnv>(options: NestConfigOptions<TEnv>): DynamicModule {
    const env = loadEnv(options.schema, options.source);
    return {
      module: NestConfigModule,
      providers: [{ provide: ENV_TOKEN, useValue: env }],
      exports: [ENV_TOKEN],
    };
  }
}
