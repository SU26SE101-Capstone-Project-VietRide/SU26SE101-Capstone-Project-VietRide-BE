import { Injectable } from '@nestjs/common';
import type { OperatorRecipientProvider } from './operator-recipient.provider';

@Injectable()
export class NoopOperatorRecipientProvider implements OperatorRecipientProvider {
  async resolveOperatorRecipientUserIds(operatorId: string): Promise<string[]> {
    void operatorId;
    return [];
  }
}

