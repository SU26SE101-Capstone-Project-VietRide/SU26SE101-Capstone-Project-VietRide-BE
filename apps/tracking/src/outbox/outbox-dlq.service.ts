import { Injectable } from '@nestjs/common';
import type { OutboxDlqQueryDto } from './outbox-dlq-query.dto';
import { OutboxRepository, type OutboxDlqReadItem } from './outbox.repository';

@Injectable()
export class OutboxDlqService {
  constructor(private readonly repository: OutboxRepository) {}

  list(query: OutboxDlqQueryDto): Promise<OutboxDlqReadItem[]> {
    return this.repository.readDlq(query);
  }
}
