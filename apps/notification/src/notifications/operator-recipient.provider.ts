export interface OperatorRecipientProvider {
  resolveOperatorRecipientUserIds(operatorId: string): Promise<string[]>;
  resolveOperatorRecipientEmails?(
    operatorId: string,
    userIds: string[],
  ): Promise<OperatorRecipientEmail[]>;
}

export interface OperatorRecipientEmail {
  userId: string;
  email: string;
}
