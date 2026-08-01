import { randomUUID } from 'node:crypto';

const keysByLabel = new Map();

export function day36IdempotencyKey(label) {
  const existingKey = keysByLabel.get(label);
  if (existingKey) return existingKey;

  const key = randomUUID();
  keysByLabel.set(label, key);
  return key;
}
