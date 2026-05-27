import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import { Pool, type QueryResult, type QueryResultRow } from 'pg';

export const PG_POOL = Symbol('PG_POOL');

@Injectable()
export class PgService implements OnModuleDestroy {
  private readonly logger = new Logger('PgService');

  constructor(@Inject(PG_POOL) private readonly pool: Pool) {}

  getPool(): Pool {
    return this.pool;
  }

  async query<R extends QueryResultRow = QueryResultRow>(
    sql: string,
    params?: ReadonlyArray<unknown>,
  ): Promise<QueryResult<R>> {
    return this.pool.query<R>(sql, params as unknown[] | undefined);
  }

  async onModuleDestroy(): Promise<void> {
    try {
      await this.pool.end();
    } catch (err) {
      this.logger.warn(`Error closing pg pool: ${(err as Error).message}`);
    }
  }
}
