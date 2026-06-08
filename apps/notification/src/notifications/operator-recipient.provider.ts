export interface OperatorRecipientProvider {
  resolveOperatorRecipientUserIds(operatorId: string): Promise<string[]>;
}

