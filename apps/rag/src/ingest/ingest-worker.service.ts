import { Inject, Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import pino from 'pino';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  RAG_INGEST_OUTBOX_BATCH_SIZE,
  RAG_INGEST_WORKER_INTERVAL_MS,
} from './ingest.constants';
import { IngestService } from './ingest.service';

@Injectable()
export class IngestWorkerService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = pino({ name: IngestWorkerService.name });
  private timer: NodeJS.Timeout | undefined;
  private running = false;

  constructor(
    private readonly ingestService: IngestService,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  onModuleInit(): void {
    if (!this.env.RAG_INGEST_WORKER_ENABLED) return;
    this.timer = setInterval(() => {
      void this.tick();
    }, RAG_INGEST_WORKER_INTERVAL_MS);
    void this.tick();
  }

  onModuleDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  async tick(): Promise<void> {
    if (this.running) return;
    this.running = true;

    try {
      const processed = await this.ingestService.processPendingOnce(RAG_INGEST_OUTBOX_BATCH_SIZE);
      if (processed > 0) {
        this.logger.info({ processed }, 'Processed RAG ingest outbox events');
      }
    } catch (error) {
      this.logger.warn({ err: error }, 'RAG ingest worker tick failed');
    } finally {
      this.running = false;
    }
  }
}
